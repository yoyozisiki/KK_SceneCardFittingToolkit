using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace KKPEHeightLock
{
    public static partial class AutoExport
    {
        // ======================================================== 单张场景卡
        private delegate void StringSink(string value);

        private static IEnumerator ExportOne(ExportOptions o, string scenePath,
                                             string segRoot, int index, StringSink done)
        {
            done(null);
            string tag = "s" + index.ToString("000") + "_" + SafeName(Path.GetFileNameWithoutExtension(scenePath));
            string segDir = Path.Combine(segRoot, tag);
            try { Directory.CreateDirectory(segDir); }
            catch (Exception e) { Log("建片段目录失败：" + e.Message); yield break; }

            // ---- 1) 加载场景（手动驱动 Studio 原生加载协程）
            SetPhase("加载场景：" + CurrentSceneName);
            HookSceneLoadEvent();
            // 场景卡一读完 Timeline 就会自动开播。必须**提前**按住：等加载协程
            // 返回再 Pause/Seek(0)，中间已经播了几百毫秒，那一下瞬移会把骨骼整段
            // 拉回去，动态骨骼惯性爆炸 —— 成片开头头发「飞起来」就是这么来的。
            HoldTimeline = true;
            bool loadOk = false;
            IEnumerator load = LoadScene(scenePath, delegate(bool ok) { loadOk = ok; });
            while (load.MoveNext()) yield return load.Current;
            if (!loadOk)
            {
                HoldTimeline = false;
                Log("[失败] 场景加载没成功，跳过这张");
                yield break;
            }

            // 加载协程返回 ≠ Studio 已经把卡吃下去了。等 KKAPI 的 SceneLoad 事件
            // ——那是 Studio 自己认为「这张卡读完了」的唯一权威信号。
            // 事件迟迟不来通常意味着这张卡没读进来（比如 Studio 还在初始化、
            // 加载被它自己的启动流程覆盖掉），这种情况只告警不拦，靠后面的画面自证。
            SetPhase("等待场景载入…");
            float waitLoad = Time.realtimeSinceStartup + 15f;
            while (!_sceneLoadFired && Time.realtimeSinceStartup < waitLoad)
                yield return null;
            if (!_sceneLoadFired)
                Log("[警告] 15 秒内没收到 KKAPI 的 SceneLoad 事件，这张卡可能没真正载入");
            else
                Log("场景已载入，开始处理");

            // 事件之后贴图/模型还要一点时间上屏，固定给 2 秒，再等相机停稳
            yield return new WaitForSeconds(2f);
            IEnumerator settle = WaitCameraSettle(8f);
            while (settle.MoveNext()) yield return settle.Current;

            // 加载完把动画按住：换角色期间画面不动。
            // **这里不 Seek(0)** —— hold 已经把它按在开头（最多走一两帧），
            // 再 Seek 一次就是把骨骼整段拉回去的瞬移，头发正是这么被甩飞的。
            // 起点差一两帧无所谓，比头发炸开好得多。
            // hold 要一直开到开录前：换人/换装过程中谁把时间轴放出来都会坏事。
            ScenePlayerModule.TimelinePause();
            for (int f = 0; f < 10; f++) yield return null;

            // ---- 2) 替换 female（含体型三维来源：体型滑块 / 乳量物理 / 骨架类型 / 身高）
            bool didReplace = false;
            if (o.ReplaceFemale && !string.IsNullOrEmpty(o.FemaleCard))
            {
                SetPhase("替换 female：" + Path.GetFileNameWithoutExtension(o.FemaleCard));
                IEnumerator rep = ReplaceFemale(o);
                while (rep.MoveNext()) yield return rep.Current;
                didReplace = true;
            }
            else if (o.ReplaceFemale)
            {
                Log("[提示] 勾了「替换 female」但没选角色卡，跳过替换");
            }

            // ---- 2-b) 服装预设 / 部件状态（放在换人之后、体型已落定，所以先存后还）
            SetPhase("设置服装…");
            IEnumerator cloth = ApplyClothes(o);
            while (cloth.MoveNext()) yield return cloth.Current;

            // ---- 2-c) 人物全部弄完之后，才应用时间轴姿态 + 等物理稳定
            //
            // 顺序很重要：换人（骨架/贴图/头发重建）、体型三维、乳量物理、骨架类型、
            // 身高、服装、饰品 —— 每一项都会重建网格或改骨骼，任何一项在「等待」之后
            // 发生，等待就白等了。所以这一整段必须放在**最后**。
            //
            // 姿态为什么不能省：ChangeChara 返回时新骨架是**角色卡的默认姿态**，
            // 和时间轴第 0 帧差得很远；直接开录的话 Play 那一下骨骼从默认姿态瞬移到
            // 时间轴姿态，动态骨骼（前发/裙子/胸口）惯性当场炸开。
            //
            // 等多久：旧脚本（recorder.py `_replace_female`）换完人之后是
            // `_wait_responsive()` 等主线程闲下来 + 日志安静满 1 秒，再换装、去球、
            // 去码，前后好几秒才开录。这里用一个可配的固定等待（默认 3 秒）等价实现。
            if (didReplace)
            {
                SetPhase("应用时间轴姿态…");
                HoldTimeline = false;
                ScenePlayerModule.TimelineSeek(0f);
                ScenePlayerModule.TimelinePlay();
                for (int f = 0; f < 3; f++) yield return null;
                ScenePlayerModule.TimelinePause();
                HoldTimeline = true;

                Log($"[时序] 换人+换装完成 → 应用姿态"
                    + $"（时间轴 {ScenePlayerModule.TimelineTime:0.###}s，"
                    + $"播放中={Yn(ScenePlayerModule.TimelinePlaying)}）");
            }

            // ---- 2-d) 关掉场景里独立动态骨骼碰撞体对 female 的吸附
            // 换完人之后新角色的衣服/头发是全新的动态骨骼，场景里那些
            // [J694] Dynamic Bone Collider 会继续抓着它们（手动要在 KKPE 高级模式里
            // 一个个取消 on）。这里统一关掉，成片里衣服/头发就不会被吸住。
            if (didReplace)
            {
                SetPhase("关闭动态骨骼碰撞体吸附…");
                try { TurnOffDynamicBoneColliders(); }
                catch (Exception e) { Log("[提示] 关闭碰撞体吸附出错：" + e.Message); }
                yield return null;
            }

            // ---- 3) 去码 / 去球
            if (o.RemoveSpheres)
            {
                SetPhase("去无用球体…");
                try { SceneTools.RemoveUselessSpheres(); }
                catch (Exception e) { Log("去球体失败：" + e.Message); }
                yield return null;
            }
            if (o.Decensor)
            {
                SetPhase("去马赛克…");
                try { SceneTools.DecensorScene(); }
                catch (Exception e) { Log("去码失败：" + e.Message); }
                yield return null;
            }

            // ---- 3-b) 等物理稳定（放在所有准备工作之后、开录之前）
            // 换人 / 换装 / 关碰撞体 / 去码 每一项都会重建网格或改骨骼，任何一项排在
            // 等待之后，这段等待就白等了（这就是以前"中途等 3 秒"的问题）。
            // 所以统一收到这里：全部改完 → 让动态骨骼（头发/裙子/胸）静下来 → 开录。
            if (didReplace)
            {
                float settleSec = 3f;
                try { settleSec = Mathf.Max(0f, KKPEHeightLockPlugin.ExportReplaceSettle.Value); }
                catch (Exception) { }
                if (settleSec > 0f)
                {
                    SetPhase("等物理稳定 " + settleSec.ToString("0.#") + "s…");
                    yield return new WaitForSeconds(settleSec);
                }
                Log($"[时序] 全部准备完成（换人/换装/碰撞体/去码）→ 等物理稳定 {settleSec:0.#}s → 开录");
            }

            // ---- 4) 算时长
            // 规则只有一条：content = max(卡里的 Timeline 秒数, MinSeconds)
            //   * 有 Timeline 且比下限长 → 按 Timeline 录
            //   * 没 Timeline / 比下限短 → 录 MinSeconds（短卡循环补齐）
            //
            // Timeline 秒数优先从**场景卡文件**读（TimelineReader，不加载场景也准），
            // 读不到再问运行时的 Timeline 插件。为什么不能只靠运行时：插件在
            // 「这张卡没有时间轴」时 duration 不是 0 而是默认 10 秒，光看 duration
            // 会把一张静态卡录成 10 秒静止画面。
            float minSec = Mathf.Max(0.5f, o.MinSeconds);
            float tlSec = 0f;
            string tlFrom = null;

            var cardTl = TimelineReader.Read(scenePath);
            if (cardTl != null && cardTl.Seconds > 0.01f)
            {
                tlSec = cardTl.Seconds;
                tlFrom = "卡片文件";
            }
            else
            {
                float liveDur = ScenePlayerModule.TimelineDuration;
                bool tlHasContent = ScenePlayerModule.TimelineHasContent;
                float scale = Time.timeScale > 0.01f ? Time.timeScale : 1f;
                if (tlHasContent && liveDur > 0.01f)
                {
                    tlSec = liveDur / scale;
                    tlFrom = "运行时·倍速 " + scale.ToString("0.##");
                }
            }

            float content = Mathf.Max(tlSec, minSec);
            bool loop = tlSec > 0.01f && tlSec < minSec - 0.001f;

            // 和旧脚本一样：头尾各多录 FadeSeconds 秒（时间轴照常往前跑），
            // 后期用 fade 把多出来的部分压黑 —— 正片一秒不少，动作也不会定格。
            float pad = o.FadePad;
            float total = content + pad;
            int expectFrames = Mathf.Max(1, Mathf.RoundToInt(total * o.Fps));
            string tail = (pad > 0 ? $" + 头尾各 {o.FadeSeconds:0.##}s 淡入淡出" : "")
                        + $" = {total:0.##}s / {expectFrames} 帧";
            if (tlSec <= 0.01f)
                Log($"没有 Timeline → 按 {content:0.##}s 录" + tail);
            else if (loop)
                Log($"Timeline {tlSec:0.##}s（{tlFrom}）短于下限 {minSec:0.##}s → 循环补齐到 "
                    + $"{content:0.##}s" + tail);
            else
                Log($"Timeline {tlSec:0.##}s（{tlFrom}）→ 正片 {content:0.##}s" + tail);

            // ---- 5) 注入 VideoExport 参数
            // 限长改成「帧数」模式：我们自己一帧一帧把时间轴定位好，VideoExport 只负责
            // 数够帧数就收尾。这样成片帧数和时间轴严格对齐，不会因为渲染掉帧而变速。
            SetPhase("配置 VideoExport…");
            if (!VeCtl.Configure(o, segDir, expectFrames, loop))
            {
                Log("[失败] VideoExport 参数写入失败，跳过这张");
                yield break;
            }
            yield return null;
            LogCamera("开录前");
            if (CameraInsideCharacter())
                Log("[警告] 相机贴在角色身上（<0.35m），这张卡的相机可能没被应用，成片会糊满身体");

            // ---- 6) 开录 + 逐帧驱时间轴
            SetPhase("出帧中…");
            // 换场景/换人都会新建 Canvas 和组件，开录前再清一次
            if (o.RealtimeFx) EnforceCinemaMode();
            string before = VeCtl.SnapshotNewestMp4(segDir);
            bool rtFade = o.RealtimeFade;   // 淡入淡出在屏上做，不走 ffmpeg

            // 时间轴继续保持按住（hold 从加载前就开着），开录后才由 CaptureFrames 放行。
            // 全程一次 Seek 都不做 —— 那才是把头发甩飞的东西。
            ScenePlayerModule.TimelinePause();
            HoldTimeline = false;
            // 黑场必须在 Record() 之前就压上去：VideoExport 从按下录制的那一帧开始采，
            // 下面「等第一帧」那段就已经把第 0 帧录进去了，晚一步首帧就是亮的。
            if (rtFade) ApplyFade(1f);

            VeCtl.Record();
            // 等第一帧真正被采集（避开 ffmpeg 启动抖动）
            float waitStart = Time.realtimeSinceStartup;
            while (VeCtl.CurrentFrame < 1 && VeCtl.IsRecording
                   && Time.realtimeSinceStartup - waitStart < 15f)
                yield return new WaitForEndOfFrame();
            if (!VeCtl.IsRecording)
            {
                Log("[失败] 录制没有启动（VideoExport 拒绝开始）");
                yield break;
            }

            int fps = Mathf.Max(1, o.Fps);
            int fadeFrames = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(0f, o.FadeSeconds) * fps),
                                         0, Mathf.Max(0, expectFrames / 2));
            Log($"逐帧渲染：{total:0.##}s × {fps}fps = {expectFrames} 帧"
                + $"（正片 {content:0.##}s，时间轴 {tlSec:0.##}s{(loop ? "，循环补齐" : "")}，"
                + $"头尾各 {fadeFrames} 帧淡入淡出）");

            IEnumerator cap = CaptureFrames(o, tlSec, loop, expectFrames, fadeFrames, rtFade);
            while (cap.MoveNext()) yield return cap.Current;

            if (o.RealtimeFx) RestoreImgui();   // 录完立刻还原，别拖累下一张卡

            int gotFrames = VeCtl.CurrentFrame;
            Log($"采集结束：{gotFrames} 帧（目标 {expectFrames}）");
            if (gotFrames < expectFrames)
                Log($"[警告] 少采了 {expectFrames - gotFrames} 帧，成片会比预期短"
                    + "（通常是渲染卡住或 VideoExport 提前收尾）");

            // ---- 7) 等 ffmpeg 把文件收尾
            // 这里踩过两个坑：
            //   ① VideoExport 的 _generatingVideo / IsFFmpegProcessRunning 在编码刚开始时
            //      都还是 false，靠它们判「编完了」会立刻返回；
            //   ② 「文件能独占打开」也不行 —— ffmpeg 用 _SH_DENYNO 打开输出文件，
            //      别人照样能独占打开，等于没判。
            // 真正可靠的信号：**编码 ffmpeg 进程还在不在**。VideoExport 是边采边用
            // stdin 把裸帧喂给 ffmpeg 的，进程退了文件才算写完。
            SetPhase("等待编码收尾…");
            float encStart = Time.realtimeSinceStartup;
            string raw = null;
            bool reported = false;
            long lastSize = -1;
            int stable = 0;
            while (Time.realtimeSinceStartup - encStart < 1800f)
            {
                raw = VeCtl.FindOutput(segDir, before);
                if (raw != null)
                {
                    long size = FileSize(raw);

                    if (size > 0 && size == lastSize) stable++;
                    else stable = 0;
                    lastSize = size;

                    // 首选判据：mp4 结构完整（moov + mdat + 刚好到文件末尾）+ 大小 1 秒没变
                    // 兜底：大小 10 秒没变。为什么不用「ffmpeg 进程在不在」当兜底 ——
                    //   faststart 的收尾重写阶段 ffmpeg 已经退出、文件却还在重写，
                    //   进程判据在这个窗口里会给出错误答案。
                    bool finalized = IsMp4Finalized(raw);
                    if (size > 0 && finalized && stable >= 5) break;
                    if (size > 0 && !finalized && stable >= 50)
                    {
                        Log("[提示] 10 秒没有新数据且 mp4 结构不完整，按大小稳定判定编码结束");
                        break;
                    }
                }
                else if (!reported && Time.realtimeSinceStartup - encStart > 20f)
                {
                    reported = true;
                    Log("[诊断] 20 秒还没等到视频文件，片段目录内容：");
                    try
                    {
                        foreach (var f in Directory.GetFileSystemEntries(segDir))
                            Log("  · " + Path.GetFileName(f));
                    }
                    catch (Exception e) { Log("  （列不出来：" + e.Message + "）"); }
                }
                yield return new WaitForSeconds(0.2f);
            }

            // ---- 7-b) 定稿确认（并且重新锁定文件）
            // 注意：VideoExport 混流成功后会把「原始文件」删掉，只留 final_<时间>.mp4
            // （autoMuxAudioVideo + deleteSourceFilesAfterMux）。所以这里每轮都要重新找
            // 当前最新的 mp4，不能抱着旧路径 —— 否则会一直盯着一个已经被删掉的文件，
            // 20 秒后判成「0 字节、没定稿」，整个片段就丢了。
            float settleEnd = Time.realtimeSinceStartup + 20f;
            long settleLast = -1;
            int settleOk = 0;
            while (Time.realtimeSinceStartup < settleEnd)
            {
                var cur = VeCtl.FindOutput(segDir, before);
                if (!string.IsNullOrEmpty(cur) && File.Exists(cur))
                {
                    long sz = FileSize(cur);
                    bool ok = sz > 0 && IsMp4Finalized(cur) && sz == settleLast;
                    settleOk = ok ? settleOk + 1 : 0;
                    settleLast = sz;
                    raw = cur;
                    if (settleOk >= 4) break;
                }
                else
                {
                    settleOk = 0;
                    settleLast = -1;
                }
                yield return new WaitForSeconds(0.2f);
            }

            // 最后再锁一次：混流刚完成的一瞬间，文件名会从 <时间>.mp4 变成 final_<时间>.mp4
            var latest = VeCtl.FindOutput(segDir, before);
            if (!string.IsNullOrEmpty(latest) && File.Exists(latest) && FileSize(latest) > 0
                && !string.Equals(latest, raw, StringComparison.OrdinalIgnoreCase))
            {
                Log("已锁定混流后的文件：" + Path.GetFileName(latest)
                    + (string.IsNullOrEmpty(raw) ? "" : "（原 " + Path.GetFileName(raw) + " 已不在）"));
                raw = latest;
            }
            else if (!string.IsNullOrEmpty(latest) && File.Exists(latest) && FileSize(latest) > 0)
                raw = latest;

            if (string.IsNullOrEmpty(raw) || !File.Exists(raw))
            {
                Log("[失败] 没找到 VideoExport 的输出文件");
                yield break;
            }

            long rawLen = FileSize(raw);
            if (settleOk < 4)
                Log("[警告] 20 秒内文件一直没定稿（大小 " + rawLen + " 字节），后面可能出错");
            Log("原始片段：" + Path.GetFileName(raw) + "（" + HumanSize(rawLen) + "）");
            if (rawLen < 2048)
                Log("[警告] 原始片段异常小（" + rawLen + " 字节），VideoExport 可能没正常编码");

            // ---- 8) 改名成本段的最终文件
            // 不跑 ffmpeg 后期：泛光/暗角由 ReShade 在 GPU 上实时做，淡入淡出是录制时
            // 盖的全屏黑场，两样都已经烙在画面里了，重编码只会白白掉画质。
            string finalPath = Path.Combine(segRoot, tag + ".mp4");
            TryMove(raw, finalPath);

            if (!File.Exists(finalPath))
            {
                Log("[失败] 片段文件没有生成：" + finalPath);
                yield break;
            }

            // 中间帧（BMP/PNG 序列）一张都几 MB，整段能吃掉几个 GB —— 成片出来就清掉
            CleanupFrames(segDir);

            done(finalPath);
        }


        // ======================================================== 场景加载
        private static bool _sceneLoadFired;

        /// <summary>本次进程里 SceneLoad 触发过几次。Studio 启动时会载入默认场景，
        /// 那一次也算，所以「> 0」就等于「Studio 已经把初始场景读完了」。</summary>
        private static int _sceneLoadCount;

        /// <summary>
        /// 挂上 KKAPI 的「场景加载完成」事件。
        ///
        /// 这就是界面提示 "scene has been loaded" 的来源 —— 它不在游戏本体里，
        /// 是 KKAPI 的 `StudioSaveLoadApi.SceneLoad`（插件 `KKS_StudioSceneLoadedSound`
        /// 就是订阅它来放提示音的）。比猜日志/猜亮度可靠得多：事件一响，说明
        /// Studio 自己认为这张卡已经读完了。
        ///
        /// </summary>
        internal static void HookSceneLoadEvent()
        {
            _sceneLoadFired = false;
            try
            {
                KKAPI.Studio.SaveLoad.StudioSaveLoadApi.SceneLoad -= OnKkapiSceneLoad;
                KKAPI.Studio.SaveLoad.StudioSaveLoadApi.SceneLoad += OnKkapiSceneLoad;
            }
            catch (Exception e) { Log("[场景] 订阅 SceneLoad 失败：" + e.Message); }
        }

        /// <summary>KKAPI 的 SceneLoad 回调。</summary>
        private static void OnKkapiSceneLoad(object sender, KKAPI.Studio.SaveLoad.SceneLoadEventArgs e)
        {
            _sceneLoadFired = true;
            _sceneLoadCount++;
        }

        private static IEnumerator LoadScene(string path, Action<bool> done)
        {
            done(false);
            Studio.Studio studio = null;
            try { studio = UnityEngine.Object.FindObjectOfType<Studio.Studio>(); }
            catch (Exception) { }
            if (studio == null)
            {
                Log("找不到 Studio 实例");
                yield break;
            }

            // 换场景前先把动画停掉：Timeline 一直跑会占着主线程，加载协程容易卡住
            ScenePlayerModule.TimelinePause();

            IEnumerator inner = null;
            try { inner = studio.LoadSceneCoroutine(path); }
            catch (Exception e) { Log("LoadSceneCoroutine 异常：" + e.Message); }

            bool failed = false;
            if (inner != null)
            {
                // 手动 MoveNext：加载协程内部一旦有插件抛异常，交给 Unity 调度会把
                // 整条链掐掉，我们就永远等不到结束（外部脚本当年就是卡 180 秒）。
                while (true)
                {
                    bool moved;
                    try { moved = inner.MoveNext(); }
                    catch (Exception e)
                    {
                        Log("加载过程中异常：" + e.Message);
                        failed = true;
                        break;
                    }
                    if (!moved) break;
                    yield return inner.Current;
                }
            }
            else
            {
                try { studio.LoadScene(path); }
                catch (Exception e) { Log("LoadScene 异常：" + e.Message); failed = true; }
            }

            if (!failed)
            {
                // 这里**不** Seek(0)：hold 已经把时间轴按在开头（最多走一两帧），
                // 再 Seek 一次反而就是那一下「把骨骼拉回去」的瞬移。
                ScenePlayerModule.TimelinePause();
                done(true);
            }
        }
    }
}
