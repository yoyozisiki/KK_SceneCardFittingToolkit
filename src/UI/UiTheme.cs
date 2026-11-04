using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>
    /// 面板配色与控件样式。游戏自带的 IMGUI 皮肤是半透明的，叠在亮画面上看不清，
    /// 所以整块界面都用自建的实心黑底白字样式。
    /// </summary>
    public static partial class AutoExportPanel
    {
        private static bool _themeReady;
        private static Texture2D _texBlack, _texPanel, _texPanel2, _texRowHi;
        private static Texture2D _texBtn, _texBtnHi, _texAccent, _texField, _texLine;

        private static GUIStyle _win, _label, _labelSm, _labelBold, _section, _head;
        private static GUIStyle _btn, _btnAccent, _btnSm, _toggle, _field, _panel, _line;
        private static GUIStyle _row, _rowOn, _rowFolder, _arrow, _checkOn, _checkOff;

        internal static GUIStyle ThemeWindow { get { InitTheme(); return _win; } }
        internal static GUIStyle ThemeButton { get { InitTheme(); return _btn; } }
        internal static GUIStyle ThemeButtonSm { get { InitTheme(); return _btnSm; } }
        internal static GUIStyle ThemeLabel { get { InitTheme(); return _label; } }
        internal static GUIStyle ThemeLabelSm { get { InitTheme(); return _labelSm; } }
        internal static GUIStyle ThemeLabelBold { get { InitTheme(); return _labelBold; } }
        internal static GUIStyle ThemeSection { get { InitTheme(); return _section; } }
        internal static GUIStyle ThemeField { get { InitTheme(); return _field; } }
        internal static GUIStyle ThemePanel { get { InitTheme(); return _panel; } }
        internal static GUIStyle ThemeRow { get { InitTheme(); return _row; } }
        internal static GUIStyle ThemeRowOn { get { InitTheme(); return _rowOn; } }

        private static Texture2D Solid(byte r, byte g, byte b)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, new Color32(r, g, b, 255));
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        /// <summary>带 1px 描边的实心块。1x1 纯色纹理拉伸后没有边界，输入框会「消失」。</summary>
        private static Texture2D Boxed(byte r, byte g, byte b, byte lr, byte lg, byte lb)
        {
            const int S = 8;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var fill = new Color32(r, g, b, 255);
            var line = new Color32(lr, lg, lb, 255);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                    t.SetPixel(x, y, (x == 0 || y == 0 || x == S - 1 || y == S - 1) ? line : fill);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        private static GUIStyle Bg(GUIStyle s, Texture2D normal, Texture2D hover, Texture2D active)
        {
            s.normal.background = normal;
            s.hover.background = hover;
            s.active.background = active;
            s.focused.background = active;
            s.onNormal.background = active;
            s.onHover.background = active;
            s.onActive.background = active;
            s.onFocused.background = active;
            s.normal.textColor = Color.white;
            s.hover.textColor = Color.white;
            s.active.textColor = Color.white;
            s.focused.textColor = Color.white;
            s.onNormal.textColor = Color.white;
            s.onHover.textColor = Color.white;
            s.onActive.textColor = Color.white;
            s.onFocused.textColor = Color.white;
            s.border = new RectOffset(1, 1, 1, 1);
            return s;
        }

        private static void InitTheme()
        {
            if (_themeReady) return;
            _themeReady = true;

            _texBlack = Solid(0, 0, 0);
            _texPanel = Boxed(18, 18, 18, 56, 56, 56);
            _texPanel2 = Solid(30, 30, 30);
            _texRowHi = Solid(56, 56, 56);
            _texBtn = Boxed(46, 46, 46, 82, 82, 82);
            _texBtnHi = Boxed(70, 70, 70, 120, 120, 120);
            _texAccent = Boxed(28, 96, 168, 62, 140, 214);
            _texField = Boxed(14, 14, 14, 86, 86, 86);
            _texLine = Solid(72, 72, 72);

            _win = new GUIStyle(GUI.skin.window);
            _win.fontSize = 17;
            _win.fontStyle = FontStyle.Bold;
            _win.alignment = TextAnchor.UpperLeft;
            _win.padding = new RectOffset(10, 10, 28, 10);
            _win.border = new RectOffset(2, 2, 2, 2);
            Bg(_win, Boxed(0, 0, 0, 40, 40, 40), _texBlack, _texBlack);

            _label = new GUIStyle(GUI.skin.label);
            _label.fontSize = 15;
            _label.padding = new RectOffset(0, 0, 2, 2);
            _label.normal.textColor = Color.white;
            _label.wordWrap = false;

            _labelSm = new GUIStyle(_label);
            _labelSm.fontSize = 13;
            _labelSm.normal.textColor = new Color(0.72f, 0.72f, 0.72f);

            _labelBold = new GUIStyle(_label);
            _labelBold.fontStyle = FontStyle.Bold;

            _section = new GUIStyle(_label);
            _section.fontSize = 16;
            _section.fontStyle = FontStyle.Bold;
            _section.normal.textColor = new Color(0.45f, 0.78f, 1f);

            // 列表表头：小一号、灰字
            _head = new GUIStyle(_labelSm);
            _head.fontSize = 13;
            _head.alignment = TextAnchor.MiddleLeft;
            _head.normal.textColor = new Color(0.62f, 0.62f, 0.62f);

            _btn = new GUIStyle(GUI.skin.button);
            _btn.fontSize = 15;
            _btn.padding = new RectOffset(8, 8, 5, 5);
            Bg(_btn, _texBtn, _texBtnHi, _texAccent);

            _btnAccent = new GUIStyle(_btn);
            _btnAccent.fontSize = 17;
            _btnAccent.fontStyle = FontStyle.Bold;
            Bg(_btnAccent, _texAccent, Solid(44, 124, 204), Solid(20, 78, 140));

            _btnSm = new GUIStyle(_btn);
            _btnSm.fontSize = 13;
            _btnSm.padding = new RectOffset(2, 2, 4, 4);

            _toggle = new GUIStyle(GUI.skin.button);
            _toggle.fontSize = 15;
            _toggle.alignment = TextAnchor.MiddleLeft;
            _toggle.padding = new RectOffset(8, 8, 5, 5);
            Bg(_toggle, _texBtn, _texBtnHi, _texAccent);

            _field = new GUIStyle(GUI.skin.textField);
            _field.fontSize = 15;
            _field.padding = new RectOffset(6, 6, 5, 5);
            Bg(_field, _texField, _texField, _texField);

            _panel = new GUIStyle(GUI.skin.box);
            _panel.padding = new RectOffset(10, 10, 8, 8);
            Bg(_panel, _texPanel, _texPanel, _texPanel);

            _line = new GUIStyle();
            _line.normal.background = _texLine;
            _line.border = new RectOffset(0, 0, 0, 0);
            _line.margin = new RectOffset(0, 0, 6, 6);

            _row = new GUIStyle(_label);
            _row.fontSize = 14;
            _row.padding = new RectOffset(6, 6, 3, 3);
            _row.alignment = TextAnchor.MiddleLeft;
            _row.normal.background = _texPanel2;
            _row.hover.background = _texRowHi;
            _row.normal.textColor = Color.white;

            _rowOn = new GUIStyle(_row);
            _rowOn.normal.background = _texAccent;
            _rowOn.hover.background = _texAccent;

            _rowFolder = new GUIStyle(_row);
            _rowFolder.fontStyle = FontStyle.Bold;
            _rowFolder.normal.background = _texPanel;
            _rowFolder.hover.background = _texRowHi;
            _rowFolder.normal.textColor = new Color(0.62f, 0.82f, 1f);

            // 折叠箭头：18 号字，够大看得清；底色跟目录行一致，不显边框
            _arrow = new GUIStyle(_btn);
            _arrow.fontSize = 18;
            _arrow.alignment = TextAnchor.MiddleCenter;
            _arrow.padding = new RectOffset(0, 0, 0, 0);
            Bg(_arrow, _texPanel, _texRowHi, _texAccent);

            // 「选」列的小方框
            _checkOn = new GUIStyle(_arrow);
            _checkOn.normal.textColor = new Color(0.62f, 0.88f, 1f);
            _checkOn.hover.textColor = Color.white;
            Bg(_checkOn, _texPanel2, _texRowHi, _texAccent);

            _checkOff = new GUIStyle(_checkOn);
            _checkOff.normal.textColor = new Color(0.55f, 0.55f, 0.55f);
            _checkOff.hover.textColor = Color.white;
        }

        private static void HLine()
        {
            GUILayout.Box("", _line, GUILayout.Height(1), GUILayout.ExpandWidth(true));
        }
    }
}
