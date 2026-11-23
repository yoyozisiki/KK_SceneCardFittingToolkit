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
        /// 逐帧出片：把时间轴放出来，让它按 Time.captureFramerate 的固定步长往前走，
        /// VideoExport 每渲染一帧采一帧。
        ///
        /// 录制过程中不碰时间轴（唯一例外是短卡循环回绕）—— 每帧 Seek 会把骨骼摆到绝对
        /// 位置，动态骨骼惯性归零，表现就是头发炸开。头尾淡入淡出只盖黑场，时间轴照常走。
        /// </summary>
        private static IEnumerator CaptureFrames(ExportOptions o, float tlSec, bool loop,
                                                 int totalFrames, int fadeFrames, bool rtFade)
        {
            int fps = Mathf.Max(1, o.Fps);

            // 把 deltaTime 钉死成 1/fps：物理、动骨、表情插值都按固定步长走，
            // 渲染快慢不再影响画面内容。
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
                // 开录前已经 Seek(0) + Pause 过，这里只负责放行
                if (tlSec > 0.01f) ScenePlayerModule.TimelinePlay();

                // 循环判据用时间轴自己的原始 duration / playbackTime（和 TL LOOP 一致）
                float dur = 0f;
                if (loop)
                {
                    try { dur = ScenePlayerModule.TimelineDuration; } catch (Exception) { }
                }

                for (int i = 0; i < totalFrames; i++)
                {
                    // 暂停：立刻收尾。ffmpeg 管道接不回去，上层会把这段半成品丢掉重录
                    if (Paused)
                    {
                        _pauseInterrupt = true;
                        Log($"暂停在 {i}/{totalFrames} 帧，这张卡将重录");
                        break;
                    }

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
                // 被打断时把 VideoExport 也停掉：它那边的 ffmpeg 管道必须收尾，
                // 不然下一张卡开录会抢同一个输出目录。
                if (_pauseInterrupt)
                {
                    try { VeCtl.Stop(); } catch (Exception) { }
                }
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
        /// 取文件真实大小。不能用 FileInfo.Length —— 那读的是目录项缓存，
        /// 对正在写入的文件会滞后；FileStream.Length 拿到的才是真 EOF。
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
        /// mp4 是否已经完整写完。判据三条，缺一不可：
        ///   ① 顶层 box 里出现过 moov（索引写完）；
        ///   ② 出现过 mdat（画面数据）—— faststart 是「先 moov 再追加 mdat」，
        ///      只判 moov 会把还没写数据文件的判成完成；
        ///   ③ 顺着 box 链刚好走到文件末尾。
        /// 进程名轮询、大小不变、独占打开都不可靠，所以不用。
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

                // VideoExport 的原始 mp4 已经移走、frames 也清了，空的片段目录留着只是碍眼
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
