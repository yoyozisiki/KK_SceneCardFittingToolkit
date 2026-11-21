using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using UnityEngine;

namespace KKPEHeightLock
{
    public static partial class AutoExport
    {
        // ================================================== 导出期的「清场」模式
        // ReShade 抓的是 present 之后的最终画面，所以**任何**插件画在屏上的东西
        // 都会原样进视频：IMGUI 窗口、uGUI 面板、独立的控制台窗口，一个都跑不掉。
        // 导出期间必须把整个屏幕清空，只留游戏画面（和淡入淡出的黑场）。

        /// <summary>被临时 enabled=false 的 IMGUI 组件。</summary>
        private static readonly List<KeyValuePair<Behaviour, bool>> _mutedBehaviours
            = new List<KeyValuePair<Behaviour, bool>>();
        /// <summary>被临时 enabled=false 的 Canvas。</summary>
        private static readonly List<KeyValuePair<Canvas, bool>> _mutedCanvases
            = new List<KeyValuePair<Canvas, bool>>();
        /// <summary>被临时隐藏的真窗口（句柄 + 原本是否可见）。</summary>
        private static readonly List<KeyValuePair<IntPtr, bool>> _hiddenWindows
            = new List<KeyValuePair<IntPtr, bool>>();

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassNameW(IntPtr hWnd, StringBuilder buf, int max);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int cmd);

        /// <summary>
        /// 这些组件**不能**关 —— 关掉导出本身就跑不起来。
        /// 其余所有「自己声明了 OnGUI」的 MonoBehaviour 一律静音。
        /// </summary>
        private static readonly string[] CinemaKeepAlive =
        {
            "KKPEHeightLock.KKPEHeightLockPlugin",  // 我们自己的 Update/LateUpdate 驱动整个流程
            "VideoExport.VideoExport",              // 采集 + 编码
            "Screencap.ScreenshotManager",          // Screencap 通路
            "KKAPI.KoikatuAPI",                     // 各种钩子的宿主
        };

        private static bool IsKeepAlive(string fullName)
        {
            for (int i = 0; i < CinemaKeepAlive.Length; i++)
                if (CinemaKeepAlive[i] == fullName) return true;
            return false;
        }

        /// <summary>
        /// 进入清场模式：藏掉所有 Canvas、藏掉控制台窗口。
        ///
        /// **IMGUI 不在这里关** —— `enabled = false` 会连带停掉那个组件的
        /// `Update`，而场景加载阶段正是这些组件在摆相机、放地图、跑 Timeline。
        /// 早关的结果是：卡载进来了，相机还停在默认位置（对着天花板），
        /// 动画也不动。所以 IMGUI 只在真正开录前那一小段关掉，见 EnforceCinemaMode。
        /// </summary>
        public static void EnterCinemaMode()
        {
            _mutedBehaviours.Clear();
            _mutedCanvases.Clear();
            _hiddenWindows.Clear();

            MuteCanvases();
            HideConsoleWindows();

            Log("[清场] 藏 Canvas " + _mutedCanvases.Count + " 个、窗口 "
                + _hiddenWindows.Count + " 个");
        }

        /// <summary>
        /// 换场景后重新扫一遍。场景加载会新建 Canvas / 新组件，
        /// 只靠导出前那一次会漏掉。
        /// </summary>
        public static void EnforceCinemaMode()
        {
            MuteCanvases();
            HideConsoleWindows();
            MuteImguiComponents();
            Log("[清场] 静音 IMGUI " + _mutedBehaviours.Count + " 个");
        }

        /// <summary>录完一段立刻把 IMGUI 还原，别影响下一张卡的加载。</summary>
        public static void RestoreImgui()
        {
            for (int i = 0; i < _mutedBehaviours.Count; i++)
            {
                try
                {
                    if (_mutedBehaviours[i].Key != null && _mutedBehaviours[i].Value)
                        _mutedBehaviours[i].Key.enabled = true;
                }
                catch (Exception) { }
            }
            _mutedBehaviours.Clear();
        }

        public static void ExitCinemaMode()
        {
            for (int i = 0; i < _mutedBehaviours.Count; i++)
            {
                try
                {
                    if (_mutedBehaviours[i].Key != null && _mutedBehaviours[i].Value)
                        _mutedBehaviours[i].Key.enabled = true;
                }
                catch (Exception) { }
            }
            _mutedBehaviours.Clear();

            for (int i = 0; i < _mutedCanvases.Count; i++)
            {
                try
                {
                    if (_mutedCanvases[i].Key != null && _mutedCanvases[i].Value)
                        _mutedCanvases[i].Key.enabled = true;
                }
                catch (Exception) { }
            }
            _mutedCanvases.Clear();

            for (int i = 0; i < _hiddenWindows.Count; i++)
            {
                try
                {
                    if (_hiddenWindows[i].Value)   // 原本是显示的才恢复
                        ShowWindow(_hiddenWindows[i].Key, SW_SHOW);
                }
                catch (Exception) { }
            }
            _hiddenWindows.Clear();
        }

        /// <summary>
        /// 把所有「自己声明了 OnGUI」的 MonoBehaviour 置 enabled = false。
        /// 只关 enabled，不动 GameObject —— `SetActive(false)` 会连带
        /// 把挂在 `BepInEx_Manager` 上的整条插件链停掉（踩过，导出直接跑死）。
        /// </summary>
        private static readonly StringBuilder _muteNames = new StringBuilder();
        private static readonly StringBuilder _skipNames = new StringBuilder();

        /// <summary>
        /// 这几个即使有 Update 也照关。`BepInEx.MessageCenter` 就是左上角那一片
        /// 白字日志的绘制者 —— 它的 Update 只管淡出计时，停掉不影响场景。
        /// 不关它，淡入淡出的黑底上就会浮出一层白字。
        /// </summary>
        private static readonly string[] MuteAnywayList =
        {
            "BepInEx.MessageCenter",
        };

        private static bool MuteAnyway(string fullName)
        {
            for (int i = 0; i < MuteAnywayList.Length; i++)
                if (MuteAnywayList[i] == fullName) return true;
            return false;
        }

        /// <summary>这个组件除了 OnGUI 还会不会收到 Update 之类的回调。</summary>
        private static bool HasUnityTick(Type t)
        {
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            return t.GetMethod("Update", F, null, Type.EmptyTypes, null) != null
                || t.GetMethod("LateUpdate", F, null, Type.EmptyTypes, null) != null
                || t.GetMethod("FixedUpdate", F, null, Type.EmptyTypes, null) != null;
        }

        private static void MuteImguiComponents()
        {
            _muteNames.Length = 0;
            _skipNames.Length = 0;
            try
            {
                var asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int a = 0; a < asms.Length; a++)
                {
                    Type[] types;
                    try { types = asms[a].GetTypes(); }
                    catch (Exception) { continue; }
                    for (int i = 0; i < types.Length; i++)
                    {
                        Type t = types[i];
                        if (t == null || t.IsAbstract || !typeof(MonoBehaviour).IsAssignableFrom(t)) continue;
                        string full = t.FullName ?? t.Name;
                        if (IsKeepAlive(full)) continue;

                        MethodInfo onGui = t.GetMethod("OnGUI",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                            null, Type.EmptyTypes, null);
                        if (onGui == null || onGui.DeclaringType != t) continue;   // 只认自己声明的

                        UnityEngine.Object[] objs;
                        try { objs = Resources.FindObjectsOfTypeAll(t); }
                        catch (Exception) { continue; }
                        if (objs.Length == 0) continue;
                        // 只关「除了画 GUI 什么都不干」的组件。
                        // 有 Update/LateUpdate 的不能关 —— 关了就把它那一份逻辑
                        // 一起停了：VNGE 的脚本、胸部物理、NodesConstraints 的
                        // 约束全都在 Update 里跑，一关动画和物理就冻住，
                        // 录出来是一张静止图（踩过）。
                        if (HasUnityTick(t) && !MuteAnyway(full))
                        {
                            if (_skipNames.Length > 0) _skipNames.Append("　");
                            _skipNames.Append(full);
                            continue;
                        }

                        int hit = 0;
                        for (int k = 0; k < objs.Length; k++)
                        {
                            var mb = objs[k] as MonoBehaviour;
                            if (mb == null || !mb.enabled) continue;
                            _mutedBehaviours.Add(new KeyValuePair<Behaviour, bool>(mb, true));
                            mb.enabled = false;
                            hit++;
                        }
                        if (hit > 0)
                        {
                            if (_muteNames.Length > 0) _muteNames.Append("　");
                            _muteNames.Append(full);
                        }
                    }
                }
            }
            catch (Exception e) { Log("[清场] 静音 IMGUI 失败：" + e.Message); }
        }

        /// <summary>藏掉所有 Canvas（游戏 UI 和插件 UI 都是 uGUI）。</summary>
        private static void MuteCanvases()
        {
            try
            {
                Canvas[] all = Resources.FindObjectsOfTypeAll<Canvas>();
                for (int i = 0; i < all.Length; i++)
                {
                    Canvas c = all[i];
                    if (c == null || !c.enabled) continue;
                    if (c.gameObject == null || !c.gameObject.activeInHierarchy) continue;
                    _mutedCanvases.Add(new KeyValuePair<Canvas, bool>(c, true));
                    c.enabled = false;
                }
            }
            catch (Exception e) { Log("[清场] 藏 Canvas 失败：" + e.Message); }
        }

        /// <summary>
        /// 藏掉本进程的 `ConsoleWindowClass` 窗口（`Unity.Console` 给本进程
        /// AllocConsole 出来的真窗口，标题 `Unity Console`）。
        ///
        /// 走过的坑：`SetActive(false)` 会误伤 `BepInEx_Manager`；
        /// 反射调 `ConsolePlugin.Toggle()` 是**反的** —— 它的 `_enable`
        /// 表示「控制台功能启用」，不是「窗口可见」，一 Toggle 反而把它点开。
        /// </summary>
        private static void HideConsoleWindows()
        {
            try
            {
                uint me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                EnumWindows(delegate(IntPtr h, IntPtr l)
                {
                    uint pid;
                    GetWindowThreadProcessId(h, out pid);
                    if (pid != me) return true;

                    var cls = new StringBuilder(256);
                    GetClassNameW(h, cls, 256);
                    if (cls.ToString() != "ConsoleWindowClass") return true;

                    for (int i = 0; i < _hiddenWindows.Count; i++)
                        if (_hiddenWindows[i].Key == h) return true;   // 已经记过了

                    bool wasVisible = IsWindowVisible(h);
                    _hiddenWindows.Add(new KeyValuePair<IntPtr, bool>(h, wasVisible));
                    if (wasVisible) ShowWindow(h, SW_HIDE);
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception e) { Log("[清场] 藏控制台窗口失败：" + e.Message); }
        }

        public static int CurrentIndex { get; private set; }
        public static int TotalCount { get; private set; }
        public static string CurrentSceneName { get; private set; }
        public static string Phase { get; private set; }
        public static float Progress { get; private set; }

        /// <summary>本次运行的成片（按场景顺序）。</summary>
        public static readonly List<string> Segments = new List<string>();
        /// <summary>本次运行失败的场景与原因。</summary>
        public static readonly List<string> Failures = new List<string>();
        /// <summary>最近一条合并好的成片。</summary>
        public static string MergedOutput { get; private set; }

        private static readonly List<string> _log = new List<string>();
        private static readonly object _logLock = new object();

        public static List<string> LogLines
        {
            get { lock (_logLock) return new List<string>(_log); }
        }

        private static MonoBehaviour _host;

        internal static void Log(string msg)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + msg;
            lock (_logLock)
            {
                _log.Add(line);
                if (_log.Count > 400) _log.RemoveRange(0, _log.Count - 400);
            }
            KKPEHeightLockPlugin.Log.LogMessage("[导出] " + msg);
        }

        private static void SetPhase(string p)
        {
            Phase = p;
            KKPEHeightLockPlugin.Log.LogMessage("[导出] " + p);
        }

    }
}
