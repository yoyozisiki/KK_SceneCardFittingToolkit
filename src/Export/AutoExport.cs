using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>
    /// 游戏内批量导出：把 python_tools/4_AutoSceneExport 那套「外部脚本 + 模拟热键」
    /// 的流程搬到进程内，逐张场景卡出片、最后合并成一个 mp4。
    ///
    /// 和外部脚本的关键差别（也照搬了 TRAE 那份可行性报告的结论）：
    ///   * 换场景走 Studio.LoadSceneCoroutine，并且**手动 MoveNext** 驱动 ——
    ///     加载协程里一旦有插件抛异常，交给 Unity 调度会把整条链掐掉，我们就永远等不到结束。
    ///   * VideoExport 用 **帧数模式**当外壳：限长只负责「采够 N 帧就收尾」，
    ///     时间轴完全由我们逐帧 seek 驱动（时间轴 1 秒 = fps 帧），渲染快慢不影响成片。
    ///     用秒数模式它按墙钟算，渲染一掉帧就少录，就是「没有达到预期帧数」的来源。
    ///   * VideoExport 的限长/输出目录都是**私有字段**，用反射写，不重启游戏、不改 cfg。
    ///   * 后期（泛光/暗角/淡入淡出）用 ffmpeg 在编码后统一烤进成片，
    ///     滤镜串和 python_tools/6_视频后期 完全一致，所以出片观感一样。
    /// </summary>
    public static partial class AutoExport
    {
        // ================================================================ 状态
        public static bool Running { get; private set; }
        public static bool CancelRequested { get; private set; }

        /// <summary>当前是预览（只替换不录制），UI 用它改文案。</summary>
        public static bool PreviewMode { get; private set; }

        /// <summary>
        /// 暂停中（ESC 切换）。暂停时整条流水线停在原地等，随时可以继续：
        ///   · 正在录的那张卡会被**放弃重录** —— ffmpeg 的管道已经开了，
        ///     半截视频接不回去，硬接着录只会得到前面冻住的一段；
        ///   · 已经录完的片段全都留着，继续后从这张卡重新开始。
        /// </summary>
        public static bool Paused { get; private set; }

        /// <summary>本轮录制是被「暂停」打断的（区别于出错/用户停止）。</summary>
        private static bool _pauseInterrupt;

        private static bool _suppressGui;

        /// <summary>
        /// 导出中要不要把本插件的界面藏起来。
        /// 走 ReShade 实时效果时必须藏：Reshade 截图抓的是 present 之后的**最终画面**，
        /// IMGUI 会原封不动被录进视频（实测日志窗口糊满左上角一大片）。
        /// 走 Screencap 时抓的是相机 RenderTexture，不含 UI，所以不用藏、进度条可以正常看。
        ///
        /// 暂停时例外：这时候没在录，界面必须露出来，否则连「停止」都点不到。
        /// </summary>
        public static bool SuppressGui { get { return Running && _suppressGui && !Paused; } }

        /// <summary>ESC：导出中暂停 / 继续。由插件的 Update 每帧调用。</summary>
        internal static void TickPauseKey()
        {
            if (!Running) return;
            try
            {
                if (Input.GetKeyDown(KeyCode.Escape)) TogglePause();
            }
            catch (Exception) { }
        }

        public static void TogglePause()
        {
            if (!Running) return;
            Paused = !Paused;
            if (Paused)
            {
                // 黑场先撤掉：暂停时用户要看画面
                ApplyFade(0f);
                // 正在录就把这张卡标记成「半成品」，CaptureFrames 见到标记会收尾
                if (VeCtl.IsRecording) _pauseInterrupt = true;
                SetPhase("已暂停：按 ESC 继续");
                Log("==== 已暂停：按 ESC 继续（正在录的这张卡会重录）====");
            }
            else
            {
                SetPhase("继续…");
                Log("==== 继续导出 ====");
            }
        }

        /// <summary>实时淡入淡出的黑场不透明度：0 = 不遮，1 = 全黑。</summary>
        public static float FadeBlack { get; private set; }

        /// <summary>
        /// 画实时淡入淡出的全屏黑场。
        ///
        /// 走 ReShade 实时通路时，截图抓的是 present 之后的**最终画面**，
        /// 所以 IMGUI 画的这层纯黑会被原样录进视频 —— 淡入淡出就白送，
        /// 后期那条 ffmpeg（整片重编码，1440p60 一段 4~7 秒）可以整段砍掉。
        ///
        /// 必须在 `SuppressGui` 判断**之前**调用：导出期间本插件自己的界面要藏，
        /// 但这层黑场恰恰是唯一需要留在画面上的东西。
        /// </summary>
        public static void DrawFadeOverlay()
        {
            if (FadeBlack <= 0.001f) return;
            // GUI.depth 越小越靠上。别的插件（日志覆盖层之类）默认 depth = 0，
            // 会在我们这层黑场之后画，黑底上就浮出一片白字。
            // 把黑场压到 -1000，它就永远盖在最上面，淡入淡出才是干净的黑。
            int oldDepth = GUI.depth;
            GUI.depth = -1000;
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(FadeBlack));
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.depth = oldDepth;
        }

        /// <summary>设置黑场不透明度（0~1），自动夹取。</summary>
        internal static void SetFadeBlack(float a)
        {
            FadeBlack = Mathf.Clamp01(a);
        }

        /// <summary>由插件的 LateUpdate() 调用：VideoExport 实例可能比本插件晚初始化。</summary>
        public static void TickVideoExport()
        {
            VeCtl.Tick();
        }

        /// <summary>
        /// 是不是要在时间轴一开播就按住它。
        ///
        /// 场景卡一加载完，Timeline 会自己从 0 开始播。等我们的加载协程返回再
        /// `Seek(0)`，中间已经过去几百毫秒 —— 那一次 Seek 等于把骨骼整段拉回去，
        /// 动态骨骼（头发/裙子）惯性直接炸开，成片开头头发就是「飞起来」的样子。
        /// 旧脚本用的是同一招：`tl_hold(true)`，插件的 Update 里
        /// 「一看到 isPlaying 就 Pause」，所以时间轴最多走一两帧。
        /// </summary>
        internal static bool HoldTimeline;

        /// <summary>加载场景期间每帧调用：时间轴一动就按住。</summary>
        internal static void TickTimelineHold()
        {
            if (!HoldTimeline) return;
            if (ScenePlayerModule.TimelinePlaying) ScenePlayerModule.TimelinePause();
        }

        /// <summary>
        /// 把当前相机/角色位置打进日志。排查「导出取景和手动加载不一样」用：
        /// 相机停在原点、或者相机到角色的距离接近 0，都会让画面糊满角色身体。
        /// </summary>
        internal static void LogCamera(string tag)
        {
            try
            {
                Camera cam = Camera.main;
                if (cam == null) { Log("[相机] " + tag + "：没有主相机"); return; }
                Vector3 p = cam.transform.position;
                Vector3 e = cam.transform.eulerAngles;
                Log($"[相机] {tag}：pos=({p.x:0.00}, {p.y:0.00}, {p.z:0.00}) "
                    + $"rot=({e.x:0.0}, {e.y:0.0}, {e.z:0.0}) fov={cam.fieldOfView:0.0}");

                var chars = ScenePlayerModule.GetSceneCharacters();
                for (int i = 0; i < chars.Count && i < 3; i++)
                {
                    var c = chars[i];
                    if (c == null || c.charInfo == null) continue;
                    string nm;
                    try { nm = c.charInfo.fileParam.fullname; }
                    catch (Exception) { nm = "?"; }
                    Vector3 cp = c.charInfo.transform.position;
                    Log($"[相机]   角色{i + 1}「{nm}」pos=({cp.x:0.00}, {cp.y:0.00}, {cp.z:0.00})"
                        + $" 距相机 {Vector3.Distance(cp, p):0.00}m");
                }
            }
            catch (Exception e) { Log("[相机] 读取失败：" + e.Message); }
        }

        /// <summary>
        /// 等相机停下来再录。场景载入后 Studio（和各种相机插件）还要摆一会儿镜头，
        /// 这期间开录就会录到「相机还在路上」的画面。最多等 maxWait 秒。
        /// </summary>
        private static IEnumerator WaitCameraSettle(float maxWait)
        {
            float t0 = Time.realtimeSinceStartup;
            Vector3 last = Vector3.zero;
            bool has = false;
            float still = 0f;
            while (Time.realtimeSinceStartup - t0 < maxWait)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 p = cam.transform.position;
                    if (has && Vector3.Distance(p, last) < 0.005f)
                    {
                        still += Time.unscaledDeltaTime;
                        if (still >= 0.4f) yield break;
                    }
                    else
                    {
                        still = 0f;
                        last = p;
                        has = true;
                    }
                }
                yield return null;
            }
        }

        /// <summary>相机是不是贴在角色身上（<0.35m 基本等于钻进模型了）。</summary>
        internal static bool CameraInsideCharacter()
        {
            try
            {
                Camera cam = Camera.main;
                if (cam == null) return false;
                var chars = ScenePlayerModule.GetSceneCharacters();
                for (int i = 0; i < chars.Count; i++)
                {
                    var c = chars[i];
                    if (c == null || c.charInfo == null) continue;
                    if (Vector3.Distance(c.charInfo.transform.position, cam.transform.position) < 0.35f)
                        return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>
        /// 等 Studio 启动完成。
        ///
        /// 不用固定秒数，用 Studio 自己的信号：
        ///   · 拿得到 `Studio.Studio` 和主相机；
        ///   · KKAPI 的 `SceneLoad` 至少触发过一次 —— Studio 启动时会把默认场景读进来，
        ///     这一次触发就代表它自己的初始化走完了。订阅在插件 Start 里，
        ///     所以哪怕事件在我们开始导出之前就发生，计数也留得住。
        ///
        /// `StartupDelay` 只当**上限**：正常十几秒就开跑；万一某台机器没装默认场景
        /// 插件、事件永远不来，到上限就带警告照跑，不会卡死。
        /// </summary>
        private static IEnumerator WaitStudioReady()
        {
            float ceiling = 15f;
            try { ceiling = Mathf.Max(5f, KKPEHeightLockPlugin.ExportStartupDelay.Value); }
            catch (Exception) { }

            SetPhase("等待 Studio 启动完成…");
            float t0 = Time.realtimeSinceStartup;
            bool announced = false;
            while (true)
            {
                if (CancelRequested) yield break;

                bool hasStudio;
                try
                {
                    hasStudio = UnityEngine.Object.FindObjectOfType<Studio.Studio>() != null
                                && Camera.main != null;
                }
                catch (Exception) { hasStudio = false; }

                // 只要 Studio 实例和主相机都在就直接开跑。
                // 不再要求「KKAPI 的 SceneLoad 触发过」——那个事件只在导出流程里才订阅，
                // 长时间开着的 Studio 永远收不到，结果每次都干等满上限（实测 60 秒）。
                if (hasStudio) break;

                if (Time.realtimeSinceStartup - t0 >= ceiling)
                {
                    Log($"[警告] 等了 {ceiling:0} 秒仍没等到 Studio 的 SceneLoad 事件，按已就绪处理");
                    break;
                }
                if (!announced)
                {
                    announced = true;
                    Log($"Studio 还在启动，等它的初始场景载入（最多 {ceiling:0} 秒）");
                }
                yield return new WaitForSeconds(1f);
            }
            Log($"Studio 已就绪（开机 {Time.realtimeSinceStartup:0}s，SceneLoad 共 {_sceneLoadCount} 次）");
        }

        public static bool Start(MonoBehaviour host, ExportOptions opts)
        {
            if (Running) return false;
            if (opts == null || opts.Scenes == null || opts.Scenes.Count == 0)
            {
                Log("没有勾选任何场景卡");
                return false;
            }
            _host = host;
            if (_host == null)
            {
                Log("拿不到 MonoBehaviour，无法启动协程");
                return false;
            }
            if (!VeCtl.Ready)
            {
                Log("VideoExport 还没就绪（没装或没初始化完），无法导出");
                return false;
            }
            CancelRequested = false;
            Paused = false;
            _pauseInterrupt = false;
            PreviewMode = false;
            Running = true;
            _suppressGui = opts.RealtimeFx;
            // 走 ReShade 通路时截的是最终画面，必须先把整屏清干净
            if (opts.RealtimeFx) EnterCinemaMode();
            Segments.Clear();
            Failures.Clear();
            MergedOutput = null;
            CurrentIndex = 0;
            TotalCount = opts.Scenes.Count;
            Progress = 0f;
            lock (_logLock) _log.Clear();
            _host.StartCoroutine(RunBatch(opts));
            return true;
        }

        /// <summary>
        /// 停止：**立刻中断**，不再往下做。
        ///
        /// 语义（和暂停的区别）：
        ///   · 暂停 = 停在原地，随时可以继续；正在录的那张卡丢弃重录，已录好的片段保留。
        ///   · 停止 = 不再继续。正在换人/换装 → 下一个检查点立刻退出，不再替换；
        ///     正在录制 → 让 VideoExport 收尾（管道不能硬切），当前这张丢弃，已完成的合并。
        /// </summary>
        public static void Cancel()
        {
            if (!Running) return;
            CancelRequested = true;
            Paused = false;    // 从暂停里点停止也要能继续往下走，否则会卡在等待循环
            // 正在录就把这张卡标记成「半成品」，CaptureFrames 见到标记会收尾退出
            try { if (VeCtl.IsRecording) _pauseInterrupt = true; }
            catch (Exception) { }
            Log("已请求停止：立刻中断当前流程（正在录的这张丢弃，已录好的片段会保留并合并）");
        }

        /// <summary>
        /// 预览：切到勾选的第一个场景，走完和导出**完全一样**的准备流程
        /// （加载 → 替换 female → 换装 → 姿态 → 碰撞体 → 去码 → 去遮挡物 → 等物理稳定），
        /// 但**不调用 VideoExport 录制**、也不隐藏界面。用来在正式导出前确认效果。
        /// </summary>
        public static bool StartPreview(MonoBehaviour host, ExportOptions opts)
        {
            if (Running) return false;
            if (opts == null || opts.Scenes == null || opts.Scenes.Count == 0)
            {
                Log("没有勾选任何场景卡");
                return false;
            }
            _host = host;
            if (_host == null)
            {
                Log("拿不到 MonoBehaviour，无法启动协程");
                return false;
            }
            opts.PreviewOnly = true;
            CancelRequested = false;
            Paused = false;
            _pauseInterrupt = false;
            PreviewMode = true;
            Running = true;
            _suppressGui = false;      // 预览绝不隐藏界面
            Segments.Clear();
            Failures.Clear();
            MergedOutput = null;
            CurrentIndex = 1;
            TotalCount = 1;
            Progress = 0f;
            lock (_logLock) _log.Clear();
            _host.StartCoroutine(RunPreview(opts));
            return true;
        }

        /// <summary>
        /// 确保皮肤材质缓存存在（预览和批量都要先调）。
        ///
        /// 拿模板卡材质本来只能 `Studio.AddFemale` 临时加载一个角色，而那个动作会重建
        /// 场景对象、冲掉隐藏角色的显隐。所以只在**缓存不存在**时做一次，提取完存到
        /// `BepInEx/config/SkinCache/*.bin`，之后每张卡只读文件，完全不碰场景。
        /// </summary>
        private static IEnumerator EnsureSkinCache(ExportOptions o)
        {
            if (string.IsNullOrEmpty(o.GlobalSkinCard)) yield break;

            SetPhase("准备皮肤材质缓存…");
            if (SkinMaterialCache.HasCache(o.GlobalSkinCard))
            {
                Log("[皮肤] 已有本地缓存，跳过提取");
                yield break;
            }

            Log("[皮肤] 首次使用，正在从角色卡提取材质（会临时加载一次模板角色）…");
            IEnumerator prep = SkinMaterialCache.EnsureCache(o.GlobalSkinCard);
            while (prep.MoveNext()) yield return prep.Current;
            yield return null;
        }

        private static IEnumerator RunPreview(ExportOptions o)
        {
            string scene = o.Scenes[0];
            CurrentSceneName = Path.GetFileNameWithoutExtension(scene);
            Log($"==== 预览：{CurrentSceneName}（只替换不录制）====");
            Log($"参数与导出一致：替换 female={Yn(o.ReplaceFemale)}，去码={Yn(o.Decensor)}，"
                + $"去球={Yn(o.RemoveSpheres)}，服装槽={o.Outfit}");

            IEnumerator warm = WaitStudioReady();
            while (warm.MoveNext()) yield return warm.Current;

            // 预览也要先把皮肤缓存准备好（否则 ApplyToAll 只会报"缓存为空"）
            IEnumerator skinWarm = EnsureSkinCache(o);
            while (skinWarm.MoveNext()) yield return skinWarm.Current;

            bool ok = false;
            IEnumerator one = ExportOne(o, scene, null, 1, 0,
                delegate(string p) { ok = !string.IsNullOrEmpty(p); });
            while (one.MoveNext()) yield return one.Current;

            Progress = 1f;
            if (!ok) Log("[预览] 没有走完准备流程，看看上面的日志");
            SetPhase(ok ? "预览完成：场景已替换，可以手动播放检查" : "预览结束（未完成）");
            Log("==== 预览结束（未录制）====");
            Finish();
        }

        // ============================================================ 主流程
        private static IEnumerator RunBatch(ExportOptions o)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string root = string.IsNullOrEmpty(o.OutputDir)
                ? Path.Combine(UserData.Path, "VideoExport", "AutoExport")
                : o.OutputDir;
            string segRoot = Path.Combine(root, "segments_" + stamp);
            try { Directory.CreateDirectory(segRoot); }
            catch (Exception e) { Log("建输出目录失败：" + e.Message); }

            Log($"==== 开始批量导出：{o.Scenes.Count} 张场景卡 ====");
            Log($"输出目录：{root}");
            LogMemory("批量开始");
            Log($"参数：{o.Fps}fps，时长=按 Timeline / 下限 {o.MinSeconds:0.##}s（短卡补齐）"
                + $"，头尾各 {o.FadeSeconds:0.##}s 淡入淡出（直出）"
                + $"，去码={Yn(o.Decensor)}，去球={Yn(o.RemoveSpheres)}，替换 female={Yn(o.ReplaceFemale)}");
            Log($"后期：{DescribeFx(o)}");
            // 只读检查，绝不写 ReShade 预设：泛光/暗角强度归用户在 ReShade 面板里调。
            if (o.RealtimeFx) CheckReshadePreset();

            float t0 = Time.realtimeSinceStartup;

            // 先把 Studio 自己的启动等完再动手。冷启动时 Studio 组件十几秒就存在了，
            // 但它自己的初始化（默认场景、UI、一堆插件）还要再跑二十多秒；这期间调
            // LoadSceneCoroutine，加载会被它的启动流程覆盖掉，卡等于没读进来 ——
            // 表现就是「画面是亮的但一直在变」，第一张永远做不成。
            IEnumerator warm = WaitStudioReady();
            while (warm.MoveNext()) yield return warm.Current;

            // ---- 皮肤材质缓存（只在缓存不存在时做一次）
            IEnumerator skinPrepRun = EnsureSkinCache(o);
            while (skinPrepRun.MoveNext()) yield return skinPrepRun.Current;

            // 注意这里用 while 而不是 for：被暂停打断的那张卡要**原地重录**，
            // 所以下标不能自增（见下面 _pauseInterrupt 分支）。
            for (int i = 0; i < o.Scenes.Count; )
            {
                if (CancelRequested) break;

                // 暂停：停在两张卡之间等 ESC。这里没在录，等多久都不会丢画面。
                while (Paused && !CancelRequested)
                {
                    SetPhase("已暂停：按 ESC 继续");
                    yield return null;
                }
                if (CancelRequested) break;

                CurrentIndex = i + 1;
                string scene = o.Scenes[i];
                CurrentSceneName = Path.GetFileNameWithoutExtension(scene);
                Progress = (float)i / o.Scenes.Count;
                Log($"---- [{i + 1}/{o.Scenes.Count}] {CurrentSceneName} ----");
                LogMemory("开卡前");

                _pauseInterrupt = false;
                int attempt = 0;
                string outPath = null;
                IEnumerator one = ExportOne(o, scene, segRoot, i + 1, attempt,
                    delegate(string p) { outPath = p; });
                while (true)
                {
                    bool moved;
                    object cur = null;
                    try
                    {
                        moved = one.MoveNext();
                        if (moved) cur = one.Current;
                    }
                    catch (Exception e)
                    {
                        Log("[失败] " + CurrentSceneName + "：" + e.Message);
                        Failures.Add(CurrentSceneName + "：" + e.Message);
                        break;
                    }
                    if (!moved) break;
                    if (cur is IEnumerator) { yield return _host.StartCoroutine((IEnumerator)cur); }
                    else yield return cur;
                }

                // 被暂停打断：这张卡只录了半截，管道已经关了接不回去。
                // 下次重录换一个新目录（见 ExportOne 的 attempt），这里**不删**旧目录 ——
                // VideoExport 收尾时还要用，删了它会报 DirectoryNotFound、混流也拿不到文件。
                if (_pauseInterrupt)
                {
                    _pauseInterrupt = false;
                    attempt++;
                    Log("[暂停] 这张卡没录完，继续后用新目录重录（第 " + (attempt + 1) + " 次尝试）");
                    HoldTimeline = false;
                    CurrentIndex = i + 1;
                    continue;
                }

                if (!string.IsNullOrEmpty(outPath))
                {
                    Segments.Add(outPath);
                    Log("片段完成：" + Path.GetFileName(outPath) + "（" + HumanSize(FileSize(outPath)) + "）");
                }
                else
                {
                    if (!CancelRequested) Failures.Add(CurrentSceneName + "：没有产出视频");
                }
                Progress = (float)(i + 1) / o.Scenes.Count;

                // 兜底：ExportOne 中途出错/提前 yield break 时 hold 可能还开着，
                // 时间轴会一直冻着（用户回 Studio 手动播放也没反应）。
                HoldTimeline = false;
                i++;

                // 录完一张就清一次资源（见 CleanupAfterCard 的注释：
                // Studio 换场景不释放旧卡的贴图/网格/材质，不手动清就会累积到 OOM）。
                IEnumerator cleanup = CleanupAfterCard();
                while (cleanup.MoveNext()) yield return cleanup.Current;
            }

            // 收尾：合并
            if (Segments.Count == 0)
            {
                SetPhase(CancelRequested ? "已停止：没有任何片段" : "失败：没有任何片段");
                Finish();
                yield break;
            }

            if (o.Merge && Segments.Count > 1)
            {
                SetPhase("合并 " + Segments.Count + " 段视频…");
                string merged = Path.Combine(root, "merged_" + stamp + "_" + Segments.Count + "scenes.mp4");
                bool ok = false;
                IEnumerator m = ConcatVideos(Segments, merged, delegate(bool r) { ok = r; });
                while (m.MoveNext()) yield return m.Current;
                if (ok)
                {
                    MergedOutput = merged;
                    Log("合并完成：" + merged);
                }
                else
                {
                    Failures.Add("合并：" + "ffmpeg 返回失败");
                }
            }
            else if (Segments.Count == 1)
            {
                MergedOutput = Segments[0];
            }

            // 保留分段 = 把每段的 mp4 留着（方便单独用）。不保留就删掉，
            // 顺手把 VideoExport 写原始文件用的 s001_xxx/ 空目录一起清掉 ——
            // 以前这里只打了一行日志、从来没真删过，勾不勾都留着。
            if (Segments.Count > 1 && o.Merge && !o.KeepSegments)
            {
                int removed = 0;
                try
                {
                    foreach (var f in Directory.GetFiles(segRoot, "*.mp4"))
                    {
                        try { File.Delete(f); removed++; } catch (Exception) { }
                    }
                    foreach (var d in Directory.GetDirectories(segRoot))
                    {
                        try { Directory.Delete(d, true); } catch (Exception) { }
                    }
                }
                catch (Exception e) { Log("[警告] 清理分段失败：" + e.Message); }
                Log("已删除分段：" + removed + " 个（只留合并后的成片）");
            }
            else if (Segments.Count > 1)
            {
                Log("分段保留在：" + segRoot);
            }

            Log($"==== 全部结束，用时 {(Time.realtimeSinceStartup - t0):0.0}s，"
                + $"成功 {Segments.Count} / {o.Scenes.Count} ====");
            if (Failures.Count > 0)
            {
                Log("失败清单：");
                for (int i = 0; i < Failures.Count; i++) Log("  · " + Failures[i]);
            }
            SetPhase(MergedOutput != null ? "完成：" + Path.GetFileName(MergedOutput) : "完成");
            Finish();
        }

        private static void Finish()
        {
            Running = false;
            PreviewMode = false;
            CancelRequested = false;
            Paused = false;
            _pauseInterrupt = false;
            _suppressGui = false;
            ApplyFade(0f);             // 别把黑场留在画面上
            ExitCinemaMode();          // 把静音的 GUI / Canvas / 窗口全部还原
            Progress = 1f;
        }

        private static string Yn(bool b) { return b ? "开" : "关"; }

        /// <summary>
        /// 打一行内存用量。批量导出时用来盯泄漏：Unity 侧（贴图/网格/材质）和托管堆分开看。
        /// </summary>
        internal static void LogMemory(string tag)
        {
            try
            {
                long total = 0, reserved = 0, mono = 0;
                try { total = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(); }
                catch (Exception) { }
                try { reserved = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong(); }
                catch (Exception) { }
                try { mono = GC.GetTotalMemory(false); }
                catch (Exception) { }
                Log($"[内存] {tag}：Unity 已用 {total / 1048576}MB / 保留 {reserved / 1048576}MB"
                    + $"，托管堆 {mono / 1048576}MB");
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 一张卡录完后的清理。
        ///
        /// 这是批量导出 OOM 闪退的主因：Studio 换场景只会销毁场景里的对象，
        /// **不会**释放旧卡的贴图/网格/材质实例 —— Unity 的 asset 只有
        /// `Resources.UnloadUnusedAssets()` 才会真正回收。一张 hongcha 卡解出来
        /// 就是几十 MB 贴图 + 上千个材质实例，连做十几张就吃满 32G。
        /// </summary>
        private static IEnumerator CleanupAfterCard()
        {
            // 先走一帧：Destroy 的对象在帧末才真正断开引用
            yield return null;

            AsyncOperation op = null;
            try { op = Resources.UnloadUnusedAssets(); }
            catch (Exception e) { Log("[清理] UnloadUnusedAssets 调用失败：" + e.Message); }
            if (op != null)
            {
                // 它是异步的，必须等它跑完，否则下一张卡加载时旧资源还在
                while (!op.isDone) yield return null;
            }

            // 托管堆：Material 名字/属性名这些字符串只在 GC 时回收
            try { GC.Collect(); } catch (Exception) { }

            LogMemory("卡间清理后");
        }

        /// <summary>
        /// 取文件名做日志。不能用裸的 Path.GetFileName：cfg 里的路径可能带着被转义
        /// 破坏过的字符，它会抛 Illegal characters in path —— 在协程里一抛就是
        /// 「替换出错」，把正常流程带崩。
        /// </summary>
        internal static string NameOf(string p, bool withExt = true)
        {
            if (string.IsNullOrEmpty(p)) return "";
            try { return withExt ? Path.GetFileName(p) : Path.GetFileNameWithoutExtension(p); }
            catch (Exception) { return p; }
        }

    }
}
