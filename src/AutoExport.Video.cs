using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Diagnostics;
using UnityEngine;

namespace KKPEHeightLock
{
    public static partial class AutoExport
    {
        // ======================================================== 外部进程
        public static string FfmpegPath()
        {
            try
            {
                string dir = Path.Combine(Path.Combine(BepInEx.Paths.PluginPath, "VideoExport"), "ffmpeg");
                string a = Path.Combine(dir, "ffmpeg-64.exe");
                if (File.Exists(a)) return a;
                string b = Path.Combine(dir, "ffmpeg.exe");
                if (File.Exists(b)) return b;
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>
        /// 起一个后台进程并等它退出。Unity 的协程在主线程跑，这里用 Process 的
        /// 异步读 + 每帧轮询，避免 WaitForExit 把主线程卡死（导出时游戏还在渲染）。
        /// </summary>
        private static IEnumerator RunProcess(string exe, string args, Action<bool> done)
        {
            done(false);
            System.Diagnostics.Process p = null;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardError = true;
                psi.RedirectStandardOutput = true;
                p = System.Diagnostics.Process.Start(psi);
            }
            catch (Exception e)
            {
                Log("启动 ffmpeg 失败：" + e.Message);
                yield break;
            }

            var err = new StringBuilder();
            try
            {
                p.ErrorDataReceived += delegate(object s, System.Diagnostics.DataReceivedEventArgs e)
                {
                    if (!string.IsNullOrEmpty(e.Data)) lock (err) err.AppendLine(e.Data);
                };
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();
            }
            catch (Exception) { }

            float start = Time.realtimeSinceStartup;
            while (!p.HasExited)
            {
                if (Time.realtimeSinceStartup - start > 3600f)
                {
                    try { p.Kill(); } catch (Exception) { }
                    Log("ffmpeg 超时，已强制结束");
                    yield break;
                }
                yield return null;
            }

            int code = 0;
            try { code = p.ExitCode; } catch (Exception) { }
            if (code != 0)
            {
                string msg;
                lock (err) msg = err.ToString();
                msg = msg.Trim();
                if (msg.Length > 800) msg = msg.Substring(msg.Length - 800);
                Log("ffmpeg 返回 " + code + (msg.Length > 0 ? "：" + msg : "（没有输出）"));
            }
            try { p.Dispose(); } catch (Exception) { }
            done(code == 0);
        }

        private static string Q(string s) { return "\"" + s + "\""; }

        private static void TryMove(string src, string dst)
        {
            try
            {
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(src, dst);
            }
            catch (Exception e)
            {
                Log("改名失败：" + e.Message + "，直接复制");
                try { File.Copy(src, dst, true); } catch (Exception e2) { Log("复制也失败：" + e2.Message); }
            }
        }

        public static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "scene";
            var bad = "<>:\"/\\|?*";
            var sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
                sb.Append(bad.IndexOf(name[i]) >= 0 ? '_' : name[i]);
            string s = sb.ToString().Trim().Trim('.');
            return s.Length == 0 ? "scene" : s;
        }

        // ==================================================== VideoExport 反射
        /// <summary>
        /// VideoExport 的限长/输出目录全是私有字段，这里全反射写。
        /// 只碰「录制开始时才会读到」的那几个，所以录之前改就行，不用重启游戏。
        /// </summary>
        public static class VeCtl
        {
            private const string VeGuid = "com.joan6694.illusionplugins.videoexport";
            private const BindingFlags Inst = BindingFlags.Instance
                                             | BindingFlags.NonPublic
                                             | BindingFlags.Public;

            private static object _inst;
            private static Type _type;
            private static float _nextTry;

            private static FieldInfo _fLimitOn, _fLimitType, _fLimitNum, _fRealign, _fPrewarm, _fSettlePhysics;
            private static FieldInfo _fAutoVideo, _fFps, _fExportFps, _fOutFolder, _fFramesFolder;
            private static FieldInfo _fExtension, _fResize, _fResizeX, _fResizeY;
            private static FieldInfo _fRecording, _fCurFrame, _fFrameLimit, _fGenerating, _fSelectedPlugin;
            private static FieldInfo _fMp4HwAccel, _fMp4ZeroCopy, _fMp4ZeroCopyDepth, _fParallelEnc;
            private static MethodInfo _mRecord, _mStop, _mFfmpegRunning;

            public static bool Ready
            {
                get { Tick(); return _inst != null; }
            }

            public static bool IsRecording
            {
                get { return GetBool(_fRecording, false); }
            }

            /// <summary>VideoExport 是不是还在生成视频（帧已经采完、ffmpeg 还在编码）。</summary>
            public static bool IsGenerating
            {
                get
                {
                    if (GetBool(_fGenerating, false)) return true;
                    if (_mFfmpegRunning != null && _inst != null)
                    {
                        try { return Convert.ToBoolean(_mFfmpegRunning.Invoke(_inst, null)); }
                        catch (Exception) { }
                    }
                    return false;
                }
            }

            public static int CurrentFrame
            {
                get
                {
                    if (_fCurFrame == null || _inst == null) return 0;
                    try { return Convert.ToInt32(_fCurFrame.GetValue(_inst)); }
                    catch (Exception) { return 0; }
                }
            }

            public static void Tick()
            {
                if (_inst != null) return;
                float now = Time.realtimeSinceStartup;
                if (now < _nextTry) return;
                _nextTry = now + 2f;
                try
                {
                    foreach (var kv in BepInEx.Bootstrap.Chainloader.PluginInfos)
                    {
                        var pi = kv.Value;
                        if (pi == null || pi.Instance == null) continue;
                        if (pi.Metadata != null && pi.Metadata.GUID != VeGuid) continue;
                        if (Bind(pi.Instance)) return;
                    }
                    foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type t = null;
                        try { t = a.GetType("VideoExport.VideoExport", false); }
                        catch (Exception) { }
                        if (t == null) continue;
                        var o = UnityEngine.Object.FindObjectOfType(t);
                        if (o != null && Bind(o)) return;
                    }
                }
                catch (Exception) { }
            }

            private static bool Bind(object inst)
            {
                if (inst == null) return false;
                Type t = inst.GetType();
                var fOn = t.GetField("_limitDuration", Inst);
                var fType = t.GetField("_selectedLimitDuration", Inst);
                var fNum = t.GetField("_limitDurationNumber", Inst);
                if (fOn == null || fType == null || fNum == null) return false;

                _inst = inst;
                _type = t;
                _fLimitOn = fOn; _fLimitType = fType; _fLimitNum = fNum;
                _fRealign = t.GetField("_realignTimeline", Inst);
                _fPrewarm = t.GetField("_prewarmLoopCount", Inst);
                _fSettlePhysics = t.GetField("_settlePhysics", Inst);
                _fAutoVideo = t.GetField("_autoGenerateVideo", Inst);
                _fFps = t.GetField("_fps", Inst);
                _fExportFps = t.GetField("_exportFps", Inst);
                _fOutFolder = t.GetField("_outputFolder", Inst);
                _fFramesFolder = t.GetField("_globalFramesFolder", Inst);
                _fExtension = t.GetField("_selectedExtension", Inst);
                _fResize = t.GetField("_resize", Inst);
                _fResizeX = t.GetField("_resizeX", Inst);
                _fResizeY = t.GetField("_resizeY", Inst);
                _fRecording = t.GetField("_isRecording", Inst);
                _fCurFrame = t.GetField("_currentRecordingFrame", Inst);
                _fFrameLimit = t.GetField("_recordingFrameLimit", Inst);
                _fGenerating = t.GetField("_generatingVideo", Inst);
                _fSelectedPlugin = t.GetField("_selectedPlugin", Inst);
                _fMp4HwAccel = t.GetField("_mp4HwAccel", Inst);
                _fMp4ZeroCopy = t.GetField("_mp4ZeroCopy", Inst);
                _fMp4ZeroCopyDepth = t.GetField("_mp4ZeroCopyDepth", Inst);
                _fParallelEnc = t.GetField("_parallelScreenshotEncoding", Inst);
                _mRecord = t.GetMethod("RecordVideo", Inst, null, Type.EmptyTypes, null);
                _mStop = t.GetMethod("StopRecording", Inst, null, Type.EmptyTypes, null);
                _mFfmpegRunning = t.GetMethod("IsFFmpegProcessRunning", Inst, null, Type.EmptyTypes, null);

                KKPEHeightLockPlugin.Log.LogInfo("[导出] 已绑定 VideoExport 实例");
                return true;
            }

            private static bool GetBool(FieldInfo f, bool def)
            {
                if (f == null || _inst == null) return def;
                try { return Convert.ToBoolean(f.GetValue(_inst)); }
                catch (Exception) { return def; }
            }

            private static void Set(FieldInfo f, object v)
            {
                if (f == null || _inst == null) return;
                try
                {
                    // 有些字段是 BepInEx 的 ConfigEntry<T>（比如输出目录/帧目录），
                    // 直接 SetValue 会抛 "cannot be converted"；要写它里面的 Value。
                    object cur = f.GetValue(_inst);
                    Type target = f.FieldType;
                    object box = cur;
                    if (cur != null)
                    {
                        Type ct = cur.GetType();
                        if (ct.IsGenericType && ct.GetGenericTypeDefinition() == typeof(BepInEx.Configuration.ConfigEntry<>))
                        {
                            var vp = ct.GetProperty("Value");
                            if (vp != null)
                            {
                                target = vp.PropertyType;
                                vp.SetValue(cur, Coerce(v, target), null);
                                return;
                            }
                        }
                    }
                    f.SetValue(_inst, Coerce(v, target));
                }
                catch (Exception e)
                {
                    KKPEHeightLockPlugin.Log.LogWarning("[导出] 写字段 " + f.Name + " 失败：" + e.Message);
                }
            }

            /// <summary>把值转成目标类型（枚举要单独走 Enum.ToObject，ChangeType 不认）。</summary>
            private static object Coerce(object v, Type target)
            {
                if (v == null) return null;
                if (target.IsInstanceOfType(v)) return v;
                if (target.IsEnum)
                    return Enum.ToObject(target, Convert.ToInt32(v, CultureInfo.InvariantCulture));
                return Convert.ChangeType(v, target, CultureInfo.InvariantCulture);
            }

            /// <summary>
            /// 写一整套录制参数。返回 false = 没绑上实例。
            ///
            /// frameLimit 用「帧数」而不是「秒数」限长：逐帧渲染下我们自己控制节奏，
            /// VideoExport 只需要在采够 frameLimit 帧时收尾。秒数模式它按墙钟算，
            /// 渲染一掉帧就少录，正是「没有达到预期帧数」的来源。
            /// </summary>
            public static bool Configure(ExportOptions o, string outFolder, int frameLimit,
                                         bool settlePhysics)
            {
                Tick();
                if (_inst == null) return false;

                // 顺序有讲究：先开开关、再改方式、最后写数值，中间不留半套状态
                Set(_fLimitOn, true);
                if (_fLimitType != null)
                {
                    // 私有嵌套枚举 LimitDurationType：0=Frames 1=Seconds 2=Animation 3=Timeline
                    try
                    {
                        Type et = _fLimitType.FieldType;
                        object cur = _fLimitType.GetValue(_inst);
                        if (cur != null && et.IsGenericType) et = et.GetGenericArguments()[0];
                        Set(_fLimitType, Enum.ToObject(et, 0));
                    }
                    catch (Exception e) { KKPEHeightLockPlugin.Log.LogWarning("[导出] 写限长方式失败：" + e.Message); }
                }
                Set(_fLimitNum, (float)frameLimit);

                // 关掉内置的 Timeline 预滚 / 预热：时间轴完全由我们的逐帧驱动控制，
                // 让 VE 再自己跑一遍会多出几帧对不上的画面。
                Set(_fRealign, false);
                if (_fPrewarm != null) Set(_fPrewarm, 0);

                // 「物理稳定」预滚：VideoExport 的 PreRollCapture 在开录前会
                // Timeline.Play() 空跑 _fps 帧（整整 1 秒），让动态骨骼把换人/
                // 换装留下的惯性耗掉，再开始截帧。旧脚本一直开着它（默认 true），
                // 这也是它头发不飞的另一半原因 —— 我们之前为了「第一帧要 0 秒」
                // 把它关了，结果开录那一下的姿态瞬移直接被录进第 0 帧。
                //
                // 现在只在**短卡循环补齐**时打开：那种卡时间轴比片长短，多跑 1 秒
                // 只是换个循环相位，看不出来；长卡从头放到尾，跑 1 秒会真的丢掉
                // 开头 1 秒、结尾冻 1 秒，所以长卡仍然关掉，靠我们自己的等待兜。
                if (_fSettlePhysics != null) Set(_fSettlePhysics, settlePhysics);

                Set(_fAutoVideo, true);
                if (_fFps != null) Set(_fFps, o.Fps);
                if (_fExportFps != null) Set(_fExportFps, o.Fps);

                // 边采边编：帧用 stdin 直接喂给 ffmpeg，硬件编码（AMD = h264_amf）。
                if (_fMp4HwAccel != null) Set(_fMp4HwAccel, true);
                if (_fMp4ZeroCopy != null) Set(_fMp4ZeroCopy, true);
                if (_fMp4ZeroCopyDepth != null) Set(_fMp4ZeroCopyDepth, 8);
                if (_fParallelEnc != null) Set(_fParallelEnc, true);

                if (_fExtension != null)
                {
                    try
                    {
                        Type et = _fExtension.FieldType;
                        object cur = _fExtension.GetValue(_inst);
                        if (cur != null && et.IsGenericType) et = et.GetGenericArguments()[0];
                        Set(_fExtension, Enum.ToObject(et, 0));
                    }
                    catch (Exception) { }
                }

                Set(_fOutFolder, outFolder);
                if (_fFramesFolder != null)
                {
                    // 这条通路实际不落中间帧（裸帧走 stdin 直接喂 ffmpeg），
                    // 目录只是 VideoExport 内部要用，给它一个合法路径即可
                    string frames = Path.Combine(outFolder, "frames");
                    try { Directory.CreateDirectory(frames); } catch (Exception) { }
                    Set(_fFramesFolder, frames);
                }

                // 截图插件：本机 KKS_Screencap 版本不匹配（每帧 MissingMethodException，
                // 而且出的帧尺寸和 VE 声明的 2560x1440 对不上，ffmpeg 直接编不出流），
                // 所以默认强制走内置的 Unity ScreenCapture。
                if (_fSelectedPlugin != null)
                {
                    try { Set(_fSelectedPlugin, o.ScreenshotPluginIndex); }
                    catch (Exception e) { KKPEHeightLockPlugin.Log.LogWarning("[导出] 设置截图插件失败：" + e.Message); }
                }

                if (o.ResizeWidth > 0 && o.ResizeHeight > 0)
                {
                    Set(_fResize, true);
                    if (_fResizeX != null) Set(_fResizeX, o.ResizeWidth);
                    if (_fResizeY != null) Set(_fResizeY, o.ResizeHeight);
                }
                else
                {
                    Set(_fResize, false);
                }

                return true;
            }

            public static void Record()
            {
                Tick();
                if (_mRecord == null) return;
                try { _mRecord.Invoke(_inst, null); }
                catch (Exception e) { KKPEHeightLockPlugin.Log.LogWarning("[导出] RecordVideo 失败：" + e.Message); }
            }

            public static void Stop()
            {
                if (_mStop == null) return;
                try { _mStop.Invoke(_inst, null); }
                catch (Exception e) { KKPEHeightLockPlugin.Log.LogWarning("[导出] StopRecording 失败：" + e.Message); }
            }

            /// <summary>记下目录里最新的 mp4，用来区分「这段是新录的」。</summary>
            public static string SnapshotNewestMp4(string dir)
            {
                try
                {
                    var files = Directory.GetFiles(dir, "*.mp4");
                    if (files.Length == 0) return "";
                    Array.Sort(files, delegate(string a, string b)
                    {
                        return File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b));
                    });
                    return files[files.Length - 1];
                }
                catch (Exception) { return ""; }
            }

            /// <summary>找这次录出来的 mp4（比 before 新的那个）。</summary>
            public static string FindOutput(string dir, string before)
            {
                try
                {
                    var files = Directory.GetFiles(dir, "*.mp4");
                    if (files.Length == 0) return null;
                    string best = null;
                    DateTime bestT = DateTime.MinValue;
                    foreach (var f in files)
                    {
                        if (!string.IsNullOrEmpty(before) &&
                            string.Equals(f, before, StringComparison.OrdinalIgnoreCase)) continue;
                        DateTime t;
                        try { t = File.GetLastWriteTimeUtc(f); } catch (Exception) { continue; }
                        if (t > bestT) { bestT = t; best = f; }
                    }
                    return best;
                }
                catch (Exception) { return null; }
            }
        }

    }
}
