using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>
    /// 游戏内批量导出：逐张加载场景卡 → 替换角色/服装 → 逐帧驱时间轴录制 → 合并成一个 mp4。
    ///
    /// 关键设计：
    ///   · 换场景用手动 MoveNext 驱动 Studio.LoadSceneCoroutine，避免加载协程被 Unity 掐断；
    ///   · VideoExport 用「帧数」限长，时间轴由我们逐帧推进，渲染快慢不影响成片；
    ///   · VideoExport 的私有字段全部反射写，不重启游戏、不改 cfg；
    ///   · 泛光/暗角由 ReShade 实时出、淡入淡出由屏上黑场出，成片不再重编码。
    /// </summary>
    public static partial class AutoExport
    {
        // ================================================================ 状态
        public static bool Running { get; private set; }
        public static bool CancelRequested { get; private set; }

        /// <summary>当前是预览（只替换不录制），UI 用它改文案。</summary>
        public static bool PreviewMode { get; private set; }

        /// <summary>
        /// 暂停中（ESC 切换）：整条流水线停在原地等，随时可继续。
        /// 正在录的那张卡会被放弃重录（ffmpeg 管道接不回去），已录好的片段全部保留。
        /// </summary>
        public static bool Paused { get; private set; }

        /// <summary>本轮录制是被「暂停」打断的（区别于出错/用户停止）。</summary>
        private static bool _pauseInterrupt;

        private static bool _suppressGui;

        /// <summary>
        /// 导出中是否隐藏本插件界面。
        /// ReShade 通路抓的是 present 之后的最终画面，IMGUI 会被录进视频，必须藏；
        /// 暂停时例外，否则连「停止」都点不到。
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
        /// 画全屏黑场（实时淡入淡出）。必须在 SuppressGui 判断之前调用：
        /// 导出期间插件界面要藏，但这层黑场恰恰要留在画面上。
        /// </summary>
        public static void DrawFadeOverlay()
        {
            if (FadeBlack <= 0.001f) return;
            // GUI.depth 越小越靠上：压到 -1000，保证黑场盖在其他插件的日志覆盖层之上
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
        /// 加载场景期间按住时间轴：场景一加载完 Timeline 会自动开播，等加载协程返回再
        /// Seek(0) 会把骨骼整段拉回去、动态骨骼惯性炸开，所以一看到播放就暂停。
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
        /// 等 Studio 启动完成：拿得到 Studio 实例和主相机就算就绪；
        /// StartupDelay 只当上限，到点带警告照跑，不卡死。
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

                // 只要 Studio 实例和主相机都在就直接开跑（不再等 KKAPI 的 SceneLoad，
                // 长时间开着的 Studio 永远收不到那个事件）
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
        /// 停止：立刻中断，不再往下做。
        /// 正在换人/换装 → 下一个检查点退出；正在录制 → 让 VideoExport 收尾，当前这张丢弃，
        /// 已完成的片段照常合并。
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
        /// 预览：对第一个勾选的场景走完和导出完全一样的准备流程，但不录制、不隐藏界面。
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
        /// 缓存只在缺失时生成一次，存到 BepInEx/config/SkinCache/*.bin，之后每张卡只读文件。
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

            // 先等 Studio 自己的启动流程走完：这期间调 LoadSceneCoroutine 会被它覆盖掉
            IEnumerator warm = WaitStudioReady();
            while (warm.MoveNext()) yield return warm.Current;

            // ---- 皮肤材质缓存（只在缓存不存在时做一次）
            IEnumerator skinPrepRun = EnsureSkinCache(o);
            while (skinPrepRun.MoveNext()) yield return skinPrepRun.Current;

            // 用 while 而不是 for：被暂停打断的那张卡要原地重录，下标不能自增
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

                // 被暂停打断：这张卡只录了半截，继续后换新目录重录。旧目录不删，
                // VideoExport 收尾还要用。
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

                // 兜底：ExportOne 中途出错时 hold 可能还开着，时间轴会一直冻着
                HoldTimeline = false;
                i++;

                // 每张卡录完清一次资源，否则贴图/网格累积到 OOM
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

            // 保留分段 = 每段的 mp4 留着方便单独用；不保留就连 VideoExport 的临时目录一起清掉
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
        /// 一张卡录完后的清理：Studio 换场景不会释放旧卡的贴图/网格/材质，
        /// 必须 UnloadUnusedAssets + GC，否则连做十几张就 OOM。
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

        /// <summary>取文件名做日志。路径可能带非法字符，裸 Path.GetFileName 会抛异常。</summary>
        internal static string NameOf(string p, bool withExt = true)
        {
            if (string.IsNullOrEmpty(p)) return "";
            try { return withExt ? Path.GetFileName(p) : Path.GetFileNameWithoutExtension(p); }
            catch (Exception) { return p; }
        }

    }
}
