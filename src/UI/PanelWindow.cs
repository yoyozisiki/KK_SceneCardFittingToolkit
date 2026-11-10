using System;
using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>
    /// 批量导出窗口的外壳：开关、布局、底栏、运行中界面、日志。
    ///
    /// 其余部分按职责拆在 UiTheme / UiWidgets / SceneLibrary / SceneTree / SettingsPanel 里，
    /// 都是同一个 partial class。
    /// </summary>
    public static partial class AutoExportPanel
    {
        private const int WindowId = 891290;
        private const float BarH = 78f;      // 底栏固定高度

        private static bool _visible;
        private static Rect _rect = new Rect(80, 24, 1380, 1072);   // 默认开高一点：右栏设置一屏放得下
        private static bool _rectInit;
        private static Vector2 _logScroll;

        public static bool Visible { get { return _visible; } }

        public static void Toggle()
        {
            _visible = !_visible;
            if (_visible && !_scanned) ScanScenes();
        }

        public static void Show()
        {
            _visible = true;
            if (!_scanned) ScanScenes();
        }

        internal static void OnGUI()
        {
            if (!_visible) return;
            InitTheme();

            // 面板要压在所有别的插件窗口上面，鼠标事件也得我们先拿到：
            // GUI.depth 越小越靠上、越先吃事件，否则别的插件的全屏控件会把
            // MouseDown 吃掉，表现就是「画出来了但点不动」。
            int oldDepth = GUI.depth;
            GUI.depth = -2000;
            try
            {
                DrawAll();
            }
            finally
            {
                GUI.depth = oldDepth;
            }
        }

        private static void DrawAll()
        {
            if (!_scanned) ScanScenes();

            if (!_rectInit)
            {
                _rectInit = true;
                _rect.width = Mathf.Min(_rect.width, Screen.width - 40f);
                _rect.height = Mathf.Min(_rect.height, Screen.height - 40f);
            }
            _rect.x = Mathf.Clamp(_rect.x, -_rect.width + 120f, Screen.width - 120f);
            _rect.y = Mathf.Clamp(_rect.y, 0f, Screen.height - 60f);

            var e = Event.current;
            _topMouse = (e != null) ? e.mousePosition : Vector2.zero;

            _rect = GUI.Window(WindowId, _rect, DrawWindow, L10n.T("场景卡批量导出"), _win);
            DrawPopup();   // 浮层要在主窗口之后画才盖得住
        }

        private static void DrawWindow(int id)
        {
            if (AutoExport.Running)
            {
                Guard("运行状态", DrawRunning);
            }
            else
            {
                float bodyH = _rect.height - BarH;
                float colW = _rect.width - 588f;

                GUILayout.BeginHorizontal(GUILayout.Height(bodyH));
                GUILayout.BeginVertical(GUILayout.Width(colW), GUILayout.Height(bodyH));
                Guard("左栏", delegate { DrawSceneColumn(bodyH, colW); });
                GUILayout.EndVertical();

                GUILayout.Space(10);

                GUILayout.BeginVertical(GUILayout.Width(556), GUILayout.Height(bodyH));
                Guard("右栏", delegate { DrawRightColumn(bodyH); });
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();

                // 底栏铺一层实心底再画控件，上层内容就算漏出来也盖得住。
                // 控件全部用绝对坐标，不进 BeginArea —— 实测 BeginArea 里的控件点不动。
                GUI.DrawTexture(new Rect(0f, _rect.height - BarH + 2f, _rect.width, BarH - 6f),
                    _texPanel);
                Guard("底栏", DrawBottomBar);
            }

            if (GUI.Button(new Rect(_rect.width - 30f, 4f, 24f, 20f), "×", _btnSm))
                _visible = false;

            // 拖拽区要让开右上角按钮，否则按按钮变成拖窗口
            GUI.DragWindow(new Rect(0, 0, _rect.width - 34f, 26f));
        }

        /// <summary>
        /// 单块内容出错时兜住异常：Begin/End 仍然配对，面板继续能用，
        /// 只在那一块显示错误，而不是整块变黑（v2.0.1 之前就是这样被一个
        /// 非法路径字符搞成黑板的）。同一个错误只记一次日志。
        /// </summary>
        private static void Guard(string where, Action draw)
        {
            try
            {
                draw();
            }
            catch (Exception e)
            {
                string key = where + "|" + e.Message;
                if (_lastGuiError != key)
                {
                    _lastGuiError = key;
                    KKPEHeightLockPlugin.Log.LogError("[界面] " + where + "绘制失败：" + e);
                }
                GUILayout.Label(L10n.T("这里的绘制出错了，看日志") + "：" + e.Message, _labelSm);
            }
        }

        private static string _lastGuiError;

        private static void DrawBottomBar()
        {
            float x = 8f;
            float w = _rect.width - 16f;
            float y = _rect.height - BarH + 12f;
            const float RowH = 30f;

            GUI.DrawTexture(new Rect(x, y - 9f, w, 1f), _texLine);

            GUI.Label(new Rect(x, y, 72f, RowH), L10n.T("输出目录"), _label);

            const float AccentW = 240f, DefW = 64f;
            float right = x + w;
            float fieldX = x + 76f;
            float fieldW = Mathf.Max(80f, right - AccentW - DefW - 8f - fieldX);

            string dir = PathFix.Read(KKPEHeightLockPlugin.ExportOutputDir.Value);
            string nd = GUI.TextField(new Rect(fieldX, y, fieldW, RowH), dir, _field);
            if (nd != dir) KKPEHeightLockPlugin.ExportOutputDir.Value = PathFix.ToConfig(nd);

            if (GUI.Button(new Rect(right - AccentW - DefW - 4f, y, DefW, RowH), L10n.T("默认"), _btn))
                KKPEHeightLockPlugin.ExportOutputDir.Value = "";

            bool veReady = AutoExport.VeCtl.Ready;
            bool old = GUI.enabled;
            GUI.enabled = veReady && _checkedCount > 0;
            if (GUI.Button(new Rect(right - AccentW, y, AccentW, RowH),
                    L10n.T("开始导出（") + _checkedCount + L10n.T(" 张）"), _btnAccent))
                StartExport();
            GUI.enabled = old;

            string outDir = PathFix.Read(KKPEHeightLockPlugin.ExportOutputDir.Value);
            string hint = string.IsNullOrEmpty(outDir)
                ? L10n.T("留空 = UserData/VideoExport/AutoExport")
                : outDir;
            if (!veReady) hint = L10n.T("未找到 VideoExport，导出不可用");
            GUI.Label(new Rect(x, y + RowH + 3f, w, 20f), hint, _labelSm);
        }

        private static void DrawRunning()
        {
            GUILayout.Label(L10n.T("正在导出"), _section);
            GUILayout.Space(4);
            GUILayout.Label(L10n.T("场景 ") + AutoExport.CurrentIndex + " / " + AutoExport.TotalCount
                + "　" + AutoExport.CurrentSceneName, _labelBold);
            GUILayout.Label(L10n.T("阶段：") + (AutoExport.Phase ?? "-"), _label);

            var bar = GUILayoutUtility.GetRect(10, 26, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(bar, _texField);
            var fill = new Rect(bar.x + 1, bar.y + 1,
                (bar.width - 2) * Mathf.Clamp01(AutoExport.Progress), bar.height - 2);
            GUI.DrawTexture(fill, _texAccent);

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L10n.T("停止"), _btn,
                    GUILayout.Width(250), GUILayout.Height(32)))
                AutoExport.Cancel();
            GUILayout.Label(L10n.T("已完成 ") + AutoExport.Segments.Count + L10n.T(" 段　失败 ")
                + AutoExport.Failures.Count + L10n.T(" 张"), _label);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            DrawLog(_rect.height - 258f);
        }

        private static void DrawLog(float height)
        {
            _logScroll = GUILayout.BeginScrollView(_logScroll, false, true,
                GUIStyle.none, GUI.skin.verticalScrollbar, _panel,
                GUILayout.Height(height));
            var lines = AutoExport.LogLines;
            var st = new GUIStyle(_labelSm) { wordWrap = false };
            for (int i = Mathf.Max(0, lines.Count - 300); i < lines.Count; i++)
                GUILayout.Label(lines[i], st);
            GUILayout.EndScrollView();
        }

        private static void StartExport()
        {
            var paths = new System.Collections.Generic.List<string>();
            for (int i = 0; i < _scenes.Count; i++)
                if (_scenes[i].check) paths.Add(_scenes[i].path);
            StartExport(paths);
        }

        /// <summary>按给定的卡列表组参数并开跑（勾选和测试入口都走这里）。</summary>
        internal static void StartExport(System.Collections.Generic.List<string> paths)
        {
            var o = new ExportOptions();
            for (int i = 0; i < paths.Count; i++) o.Scenes.Add(paths[i]);

            o.FemaleCard = PathFix.Read(KKPEHeightLockPlugin.ExportFemaleCard.Value);
            o.ReplaceFemale = KKPEHeightLockPlugin.ExportReplaceFemale.Value;
            o.ShapeFromScene = KKPEHeightLockPlugin.ExportShapeFromScene.Value;
            o.BustFromScene = KKPEHeightLockPlugin.ExportBustFromScene.Value;
            o.BoneFromScene = KKPEHeightLockPlugin.ExportBoneFromScene.Value;
            o.LockHeight = KKPEHeightLockPlugin.ExportLockHeight.Value;
            o.Outfit = KKPEHeightLockPlugin.ExportOutfit.Value;
            o.ClothesParts = Parts;
            o.Decensor = KKPEHeightLockPlugin.ExportDecensor.Value;
            o.RemoveSpheres = KKPEHeightLockPlugin.ExportRemoveSpheres.Value;
            o.MinSeconds = KKPEHeightLockPlugin.ExportMinSeconds.Value;
            o.FadeSeconds = KKPEHeightLockPlugin.ExportFadeSeconds.Value;
            o.Fps = Mathf.Clamp(KKPEHeightLockPlugin.ExportFps.Value, 1, 240);
            o.ResizeWidth = Mathf.Max(0, KKPEHeightLockPlugin.ExportResizeWidth.Value);
            o.ResizeHeight = Mathf.Max(0, KKPEHeightLockPlugin.ExportResizeHeight.Value);
            o.OutputDir = ResolveDir(PathFix.Read(KKPEHeightLockPlugin.ExportOutputDir.Value));
            o.Merge = KKPEHeightLockPlugin.ExportMerge.Value;
            o.KeepSegments = KKPEHeightLockPlugin.ExportKeepSegments.Value;
            o.UncapFps = KKPEHeightLockPlugin.ExportUncapFps.Value;

            // 后期只剩 ReShade 一条路：泛光/暗角在 present 之后加，
            // 只有 KKReshade 的共享内存通路能抓到（代价是用不了零拷贝）。
            // 强度不在这里传：ReShade 面板里是什么就是什么，插件不碰预设。
            o.RealtimeFx = true;
            o.ScreenshotPluginIndex = 2;

            AutoExport.Start(KKPEHeightLockPlugin.Instance, o);
        }
    }
}
