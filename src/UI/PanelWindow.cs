using System;
using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>
    /// 批量导出窗口的外壳：开关、布局、底栏、运行中界面、日志。
    /// 其余部分按职责拆在 UiTheme / UiWidgets / SceneCatalog / SceneGrid / SettingsPanel 里。
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

        /// <summary>面板矩形（逻辑坐标，已经算进 GUI.matrix 的缩放前）。给点击拦截层用。</summary>
        internal static Rect PanelRect { get { return _rect; } }

        /// <summary>下拉浮层当前是否打开 / 它的矩形（浮层在主窗口外，也要挡）。</summary>
        internal static bool PopupVisible { get { return _popupOpen; } }
        internal static Rect PopupRect { get { return _popupRect; } }

        public static void Toggle()
        {
            if (_visible) SaveViewState();     // 关面板时记住目录折叠状态和滚动位置
            _visible = !_visible;
            if (_visible) SceneCatalog.Ensure();
        }

        internal static void OnGUI()
        {
            if (!_visible) return;
            InitTheme();

            // 面板要压在其他插件窗口上面，并且先拿到鼠标事件（GUI.depth 越小越靠上）
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
            SceneCatalog.Ensure();

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
            {
                _visible = false;
                SaveViewState();
            }

            // 拖拽区要让开右上角按钮，否则按按钮变成拖窗口
            GUI.DragWindow(new Rect(0, 0, _rect.width - 34f, 26f));
        }

        /// <summary>
        /// 单块内容出错时兜住异常：Begin/End 仍能配对，只在那一块显示错误，
        /// 而不是整块变黑。同一个错误只记一次日志。
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

            const float AccentW = 210f, DefW = 56f, PreviewW = 92f;
            float right = x + w;
            float fieldX = x + 76f;
            float fieldW = Mathf.Max(80f, right - AccentW - PreviewW - DefW - 12f - fieldX);

            string dir = PathFix.Read(KKPEHeightLockPlugin.ExportOutputDir.Value);
            string nd = GUI.TextField(new Rect(fieldX, y, fieldW, RowH), dir, _field);
            if (nd != dir) KKPEHeightLockPlugin.ExportOutputDir.Value = PathFix.ToConfig(nd);

            if (GUI.Button(new Rect(right - AccentW - PreviewW - DefW - 8f, y, DefW, RowH),
                    L10n.T("默认"), _btn))
                KKPEHeightLockPlugin.ExportOutputDir.Value = "";

            // 预览：走完替换流程但不录制、不隐藏界面，所以不需要 VideoExport 就绪
            bool busy = AutoExport.Running;
            bool old = GUI.enabled;
            GUI.enabled = !busy && SceneCatalog.CheckedCount > 0;
            if (GUI.Button(new Rect(right - AccentW - PreviewW - 4f, y, PreviewW, RowH),
                    L10n.T("预览"), _btn))
                StartPreview();
            GUI.enabled = old;

            bool veReady = AutoExport.VeCtl.Ready;
            GUI.enabled = !busy && veReady && SceneCatalog.CheckedCount > 0;
            if (GUI.Button(new Rect(right - AccentW, y, AccentW, RowH),
                    L10n.T("开始导出（") + SceneCatalog.CheckedCount + L10n.T(" 张）"), _btnAccent))
                StartExport();
            GUI.enabled = old;

            string outDir = PathFix.Read(KKPEHeightLockPlugin.ExportOutputDir.Value);
            string hint = string.IsNullOrEmpty(outDir)
                ? L10n.T("留空 = UserData/VideoExport/AutoExport")
                : outDir;
            if (!veReady) hint = L10n.T("未找到 VideoExport，只能预览、不能导出");
            if (SceneCatalog.CheckedCount > 0)
                hint += L10n.T("　·　预览只替换不录制，用来看效果");
            GUI.Label(new Rect(x, y + RowH + 3f, w, 20f), hint, _labelSm);
        }

        private static void DrawRunning()
        {
            GUILayout.Label(AutoExport.PreviewMode ? L10n.T("正在预览（不录制）") : L10n.T("正在导出"), _section);
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
            if (GUILayout.Button(AutoExport.Paused ? L10n.T("继续（ESC）") : L10n.T("暂停（ESC）"),
                    _btn, GUILayout.Width(150), GUILayout.Height(32)))
                AutoExport.TogglePause();
            if (GUILayout.Button(L10n.T("停止"), _btn,
                    GUILayout.Width(250), GUILayout.Height(32)))
                AutoExport.Cancel();
            GUILayout.Label(L10n.T("已完成 ") + AutoExport.Segments.Count + L10n.T(" 段　失败 ")
                + AutoExport.Failures.Count + L10n.T(" 张"), _label);
            GUILayout.EndHorizontal();

            GUILayout.Label(AutoExport.Paused
                ? L10n.T("已暂停：画面停住，可以随便看看。按 ESC 继续 —— 正在录的这张卡会重录，已录好的片段都保留。")
                : L10n.T("ESC 暂停 / 继续；停止 = 当前这张卡录完就收尾（已录的片段会合并）"), _labelSm);

            GUILayout.Space(6);
            DrawLog(_rect.height - 282f);
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
            StartExport(SceneCatalog.CheckedPaths());
        }

        /// <summary>
        /// 预览第一张勾选的场景：切过去 + 走完替换 female 的整套流程，但不录制。
        /// 勾了多张时只预览第一张（顺序就是列表里的顺序）。
        /// </summary>
        private static void StartPreview()
        {
            var paths = SceneCatalog.CheckedPaths();
            if (paths == null || paths.Count == 0) return;
            var one = new System.Collections.Generic.List<string>();
            one.Add(paths[0]);
            ExportRequest.StartPreview(one);
        }

        /// <summary>按给定的卡列表开跑（勾选和测试入口都走这里）。参数组装在后端。</summary>
        internal static bool StartExport(System.Collections.Generic.List<string> paths)
        {
            return ExportRequest.Start(paths);
        }
    }
}
