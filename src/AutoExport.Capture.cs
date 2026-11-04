using System;
using System.Collections;
using System.IO;
using System.Diagnostics;
using UnityEngine;

namespace KKPEHeightLock
{
    public static partial class AutoExport
    {
        /// <summary>
        /// 逐帧出片：把时间轴放出来，让它按 `Time.captureFramerate` 的固定步长
        /// 自己往前走，VideoExport 每渲染一帧采一帧。
        ///
        /// **录制过程中我们完全不碰时间轴**（唯一例外是短卡循环回绕）——
        /// 这就是 4_AutoSceneExport 脚本那条路：它只做 `tl_hold(false)` / `tl_play()`，
        /// 之后全交给 VideoExport。之前每帧 `Seek(t)` 的写法会把骨骼每帧摆到绝对
        /// 位置，动态骨骼（头发/裙子/胸部）的惯性每帧归零，表现就是头发炸开、飞起来。
        ///
        /// 头尾的淡入淡出只是盖一层黑场，时间轴照常往前跑、不定格。
        /// </summary>
        private static IEnumerator CaptureFrames(ExportOptions o, float tlSec, bool loop,
                                                 int totalFrames, int fadeFrames, bool rtFade)
        {
            int fps = Mathf.Max(1, o.Fps);

            // 把 deltaTime 钉死成 1/fps：物理、动态骨骼、表情插值全部按固定步长走，
            // 渲染快慢不再影响画面内容。顺手放开帧率上限，不然就只能按实时速度爬。
            float oldScale = Time.timeScale;
            int oldTarget = Application.targetFrameRate;
            int oldVSync = QualitySettings.vSyncCount;
            Time.captureFramerate = fps;
            Time.timeScale = 1f;
            if (o.UncapFps)
            {
                Application.targetFrameRate = -1;
                QualitySettings.vSyncCount = 0;
            }

            try
            {
                // 开录前已经 Seek(0) + Pause 过，这里只负责「放行」。
                // 顺序照旧脚本：先定位、再播放，绝不在播放中 Seek。
                if (tlSec > 0.01f) ScenePlayerModule.TimelinePlay();

                // 循环判据用时间轴自己的原始 duration / playbackTime（和 TL LOOP 一致）
                float dur = 0f;
                if (loop)
                {
                    try { dur = ScenePlayerModule.TimelineDuration; } catch (Exception) { }
                }

                for (int i = 0; i < totalFrames; i++)
                {
                    // 短卡循环：播到末尾就回 0 重来。判据和旧脚本的 TL LOOP 完全相同
                    // （!isPlaying，或 playbackTime 已经到 duration - 0.02）。
                    if (loop && dur > 0.05f)
                    {
                        if (!ScenePlayerModule.TimelinePlaying
                            || ScenePlayerModule.TimelineTime >= dur - 0.02f)
                        {
                            ScenePlayerModule.TimelineSeek(0f);
                            ScenePlayerModule.TimelinePlay();
                        }
                    }

                    if (rtFade && fadeFrames > 0)
                    {
                        if (i < fadeFrames)
                            ApplyFade(1f - i / (float)fadeFrames);
                        else if (i >= totalFrames - fadeFrames)
                            ApplyFade((i - (totalFrames - fadeFrames) + 1) / (float)fadeFrames);
                        else
                            ApplyFade(0f);
                    }

                    // 等这一帧真的被采走：VideoExport 每渲染一帧 +1，凑够 totalFrames 收尾。
                    int target = i + 1;
                    float guard = Time.realtimeSinceStartup + 10f;
                    while (VeCtl.IsRecording && VeCtl.CurrentFrame < target
                           && Time.realtimeSinceStartup < guard)
                        yield return new WaitForEndOfFrame();
                    if (!VeCtl.IsRecording) break;
                }

                ScenePlayerModule.TimelinePause();
                ApplyFade(0f);
            }
            finally
            {
                // 这几项是全进程共享的，必须还原，否则录完之后整个 Studio 都不对劲
                Time.captureFramerate = 0;
                Time.timeScale = oldScale;
                Application.targetFrameRate = oldTarget;
                QualitySettings.vSyncCount = oldVSync;
            }
        }

        /// <summary>把字节数格式化成人看的大小。小于 1 MB 用 KB —— 画面几乎静止的短卡
        /// 原始片段只有几十 KB，用 "0.0 MB" 显示会让人以为文件是空的。</summary>
        private static string HumanSize(long bytes)
        {
            if (bytes >= 1048576L) return (bytes / 1048576.0).ToString("0.0") + " MB";
            if (bytes >= 1024L) return (bytes / 1024.0).ToString("0") + " KB";
            return bytes + " B";
        }

        /// <summary>
        /// 取文件真实大小。
        ///
        /// 不能用 `new FileInfo(p).Length` —— 它读的是**目录项里缓存的大小**，
        /// 对一个正被别的进程写着的文件会滞后（实测 ffmpeg 已经把文件写到 1.3 MB，
        /// FileInfo 还报 0）。FileStream.Length 走的是文件句柄，拿到的是真正的 EOF。
        /// </summary>
        private static long FileSize(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                               FileShare.ReadWrite | FileShare.Delete))
                    return fs.Length;
            }
            catch (Exception) { return 0; }
        }

        /// <summary>
        /// mp4 是否已经完整写完 —— 这是「编码真的收尾了」的确定信号。
        ///
        /// 为什么不用别的判据：
        ///   · 进程名轮询：ffmpeg 可能被 VE 以别的名字拉起，实测出现过「进程已退但文件才几十 KB」；
        ///   · 文件大小不变：写入会间歇停顿，1~2 秒不变也可能没写完；
        ///   · 独占打开：ffmpeg 用 _SH_DENYNO，别人照样能打开，等于没判。
        ///
        /// 判据分三步，缺一不可：
        ///   ① 顶层 box 里出现过 moov（索引写完了）；
        ///   ② 出现过 mdat（真正的画面数据）—— 这条最容易被忽略：
        ///      faststart 模式下 ffmpeg 是「先写 ftyp+moov，再追加 mdat」，
        ///      moov 刚写完那一瞬间 box 链确实刚好走到文件末尾，只判 ①②③ 之外的
        ///      「走到末尾」会把它误判成完成，实测就是日志里「原始片段（0.0 MB）」的来源；
        ///   ③ 顺着 box 链能刚好走到文件末尾 —— 有哪个 box 声明的长度超出文件，
        ///      就说明 mdat 还在写。
        /// </summary>
        private static bool IsMp4Finalized(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                               FileShare.ReadWrite | FileShare.Delete))
                {
                    long len = fs.Length;
                    if (len < 16) return false;
                    long pos = 0;
                    bool sawMoov = false, sawMdat = false;
                    var head = new byte[16];
                    int guard = 0;
                    while (pos + 8 <= len && guard++ < 64)
                    {
                        fs.Position = pos;
                        if (fs.Read(head, 0, 8) < 8) return false;

                        long size = ((long)head[0] << 24) | ((long)head[1] << 16)
                                  | ((long)head[2] << 8) | head[3];
                        string type = "" + (char)head[4] + (char)head[5] + (char)head[6] + (char)head[7];

                        if (size == 1)
                        {
                            // 64 位长度：紧跟 8 字节
                            fs.Position = pos + 8;
                            if (fs.Read(head, 0, 8) < 8) return false;
                            size = 0;
                            for (int i = 0; i < 8; i++) size = (size << 8) | head[i];
                        }
                        else if (size == 0)
                        {
                            size = len - pos;      // 延伸到文件末尾
                        }

                        if (type == "moov") sawMoov = true;
                        if (type == "mdat") sawMdat = true;
                        if (size < 8) return false;
                        if (pos + size > len) return false;   // 这个 box 还没写完
                        pos += size;
                    }
                    return sawMoov && sawMdat && pos == len;
                }
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>删掉本段落的中间帧目录（VideoExport 写在 &lt;片段&gt;/frames 下）。</summary>
        private static void CleanupFrames(string segDir)
        {
            try
            {
                string frames = Path.Combine(segDir, "frames");
                if (Directory.Exists(frames))
                {
                    Directory.Delete(frames, true);
                    Log("已清理中间帧：" + frames);
                }

                // VideoExport 的原始 mp4 已经移走/删掉了，frames 也清了 ——
                // 这时 segDir 是个空壳，留着只是碍眼（`s001_#01` 那种空文件夹）。
                if (Directory.Exists(segDir))
                {
                    bool empty = true;
                    try
                    {
                        foreach (var _ in Directory.GetFileSystemEntries(segDir)) { empty = false; break; }
                    }
                    catch (Exception) { empty = false; }
                    if (empty)
                    {
                        try { Directory.Delete(segDir, false); }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception e) { Log("[警告] 清理中间帧失败：" + e.Message); }
        }

    }
}
