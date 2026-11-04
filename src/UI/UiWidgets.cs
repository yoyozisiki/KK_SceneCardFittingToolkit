using System;
using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>
    /// 通用控件：下拉框（IMGUI 没有原生 ComboBox，自己用「按钮 + 浮层窗口」拼的）
    /// 和数字输入框。
    /// </summary>
    public static partial class AutoExportPanel
    {
        private const int PopupWindowId = 891301;
        private const float PopupRowH = 26f;

        private static bool _popupOpen;
        private static Rect _popupRect;
        private static string[] _popupItems;
        private static int _popupCurrent;
        private static Action<int> _popupPick;
        private static GUIStyle _popupWin, _popupItem, _popupItemOn;

        private static string _popupOwner;
        private static Vector2 _popupLocal;   // 按钮左上角相对窗口左上角的偏移
        private static float _popupBtnW = 110f, _popupBtnH = 28f;
        private static Vector2 _topMouse;     // 顶层坐标的鼠标位置
        private static Rect _openBtnTopRect;  // 已打开下拉按钮的顶层矩形
        private static string _skipOpenKey;   // 关掉后要吞掉的那一次 MouseUp
        private static int _skipFrame;

        private static void InitPopupStyles()
        {
            if (_popupWin != null) return;
            _popupWin = new GUIStyle(GUI.skin.window);
            _popupWin.padding = new RectOffset(4, 4, 4, 4);
            _popupWin.border = new RectOffset(2, 2, 2, 2);
            Bg(_popupWin, Boxed(24, 24, 24, 96, 96, 96), _texBlack, _texBlack);

            _popupItem = new GUIStyle(GUI.skin.button);
            _popupItem.fontSize = 14;
            _popupItem.alignment = TextAnchor.MiddleLeft;
            _popupItem.padding = new RectOffset(10, 10, 2, 2);
            Bg(_popupItem, _texBlack, _texBtn, _texBtn);

            _popupItemOn = new GUIStyle(_popupItem);
            Bg(_popupItemOn, _texAccent, _texAccent, _texAccent);
        }

        /// <summary>
        /// 下拉框。点开后在按钮正下方弹一层浮层。
        ///
        /// 坐标是这里最容易错的地方：`GUILayoutUtility.GetRect` 给的是**当前 GUI 组**的
        /// 坐标，而这些下拉框都在 ScrollView 里，等于「滚动内容坐标」；而 OnGUI 开头取到的
        /// `Event.current.mousePosition` 是顶层坐标。两者相减会得到窗口左上角，
        /// 浮层就永远贴在左上角。正确做法见 <see cref="ButtonTopLeft"/>。
        /// </summary>
        private static void Dropdown(string key, float width, string[] items, int current,
                                     Action<int> onPick, float height = 28f)
        {
            InitPopupStyles();

            var r = GUILayoutUtility.GetRect(width, height,
                GUILayout.Width(width), GUILayout.Height(height));

            var e = Event.current;
            string label = (items != null && current >= 0 && current < items.Length)
                ? items[current] : "-";

            if (GUI.Button(r, label + "  ▾", _btn))
            {
                // 点「已经开着的那个按钮」时，MouseDown 那帧已经关掉了，
                // 紧接着的 MouseUp 又会打开 —— 这里吞一次，否则看起来关不掉。
                if (_skipOpenKey == key && Time.frameCount <= _skipFrame + 3)
                {
                    _skipOpenKey = null;
                }
                else
                {
                    Vector2 tl = ButtonTopLeft(r, e != null ? e.mousePosition : Vector2.zero);
                    _popupLocal = new Vector2(tl.x - _rect.x, tl.y - _rect.y);
                    TogglePopup(key, items, current, onPick, r.width, r.height, tl);
                }
            }
        }

        /// <summary>
        /// 按钮左上角在**顶层坐标**里的位置。`mouseInBtnSpace` 必须是和 btn 同一套
        /// 坐标系里的鼠标位置（即 Dropdown 里当场取的那个），不能是 OnGUI 开头的。
        /// </summary>
        private static Vector2 ButtonTopLeft(Rect btn, Vector2 mouseInBtnSpace)
        {
            // A: GUIToScreenPoint 直接把当前组坐标换成顶层坐标。
            //    它只在 Layout 事件里返回 (0,0)，而这里只在点击那一刻调用，所以安全。
            Vector2 byScreen = Vector2.zero;
            bool screenOk = false;
            try
            {
                byScreen = GUIUtility.GUIToScreenPoint(new Vector2(btn.x, btn.y));
                screenOk = InWindow(byScreen);
            }
            catch { }

            // B: 顶层鼠标 − 同一坐标系里的鼠标 + 按钮位置（纯差值，交叉校验）
            Vector2 byDiff = new Vector2(_topMouse.x - mouseInBtnSpace.x + btn.x,
                                         _topMouse.y - mouseInBtnSpace.y + btn.y);
            bool diffOk = _topMouse != Vector2.zero && InWindow(byDiff);

            if (screenOk) return byScreen;
            if (diffOk) return byDiff;
            // 两个都不可信：退回「按钮坐标当窗口内坐标」。滚动区里会偏一个滚动量，
            // 但至少不会飞到屏幕左上角。
            return new Vector2(_rect.x + 10f + btn.x, _rect.y + 28f + btn.y);
        }

        private static bool InWindow(Vector2 p)
        {
            return p.x >= _rect.x - 16f && p.x <= _rect.xMax + 16f
                && p.y >= _rect.y - 16f && p.y <= _rect.yMax + 16f;
        }

        private static void TogglePopup(string key, string[] items, int current,
                                        Action<int> onPick, float btnW, float btnH,
                                        Vector2 btnTopLeft)
        {
            if (_popupOpen && _popupOwner == key) { _popupOpen = false; return; }

            _popupOpen = true;
            _popupOwner = key;
            _popupItems = items;
            _popupCurrent = current;
            _popupPick = onPick;
            _popupBtnW = btnW;
            _popupBtnH = btnH;
            _openBtnTopRect = new Rect(btnTopLeft.x, btnTopLeft.y, btnW, btnH);
            PlacePopup(items);
        }

        /// <summary>贴在按钮下沿，放不下就往上翻、往左收。</summary>
        private static void PlacePopup(string[] items)
        {
            float ax = _rect.x + _popupLocal.x;
            float ay = _rect.y + _popupLocal.y;

            float w = Mathf.Max(_popupBtnW, 60f);
            for (int i = 0; i < items.Length; i++)
            {
                float need = _popupItem.CalcSize(new GUIContent(items[i])).x + 28f;
                if (need > w) w = need;
            }
            float h = items.Length * PopupRowH + 8f;

            float x = ax;
            float y = ay + _popupBtnH + 1f;
            if (y + h > Screen.height - 6f) y = Mathf.Max(6f, ay - h - 1f);
            if (x + w > Screen.width - 6f) x = Mathf.Max(6f, Screen.width - w - 6f);
            if (x < 0f) x = 6f;
            if (y < 0f) y = 6f;

            _popupRect = new Rect(x, y, w, h);
        }

        private static void DrawPopup()
        {
            if (!_popupOpen || _popupItems == null) return;

            PlacePopup(_popupItems);   // 拖主窗口时浮层跟着走

            var e = Event.current;
            Vector2 mouse = (e != null) ? e.mousePosition : Vector2.zero;

            _popupRect = GUI.Window(PopupWindowId, _popupRect, DrawPopupBody,
                GUIContent.none, _popupWin);

            // 点浮层外面就关掉。点在「已打开的这个按钮」上时要吞掉随后的 MouseUp。
            if (e != null && e.type == EventType.MouseDown && !_popupRect.Contains(mouse))
            {
                if (_openBtnTopRect.Contains(mouse))
                {
                    _skipOpenKey = _popupOwner;
                    _skipFrame = Time.frameCount;
                }
                _popupOpen = false;
                e.Use();
            }
        }

        private static void DrawPopupBody(int id)
        {
            // 用绝对坐标逐行排：GUILayout 会在行间自动加间距，
            // n 行就会比 n*PopupRowH 高出十几像素，最后一项被窗口裁掉半截。
            float w = _popupRect.width - 8f;
            for (int i = 0; i < _popupItems.Length; i++)
            {
                var r = new Rect(0f, i * PopupRowH, w, PopupRowH);
                if (GUI.Button(r, _popupItems[i],
                        i == _popupCurrent ? _popupItemOn : _popupItem))
                {
                    var pick = _popupPick;
                    _popupOpen = false;
                    if (pick != null) pick(i);
                    break;
                }
            }
        }

        // ---------------------------------------------------------------- 输入框
        private static void IntField(BepInEx.Configuration.ConfigEntry<int> e, float w)
        {
            string s = GUILayout.TextField(e.Value.ToString(), _field,
                GUILayout.Width(w), GUILayout.Height(28));
            int v;
            if (int.TryParse(s, out v) && v >= 0 && v <= 99999) e.Value = v;
        }

        private static void FloatField(BepInEx.Configuration.ConfigEntry<float> e, float w)
        {
            string s = GUILayout.TextField(e.Value.ToString("0.##"), _field,
                GUILayout.Width(w), GUILayout.Height(28));
            float v;
            if (float.TryParse(s, out v) && v >= 0f && v <= 99999f) e.Value = v;
        }

        // ---------------------------------------------------------------- 小工具
        private static string HumanSize(long b)
        {
            if (b >= 1048576L) return (b / 1048576.0).ToString("0.0") + " MB";
            if (b >= 1024L) return (b / 1024.0).ToString("0") + " KB";
            return b + " B";
        }

        private static string ShortPath(string p)
        {
            try
            {
                if (string.IsNullOrEmpty(p) || p.Length <= 46) return p ?? "";
                return "…" + p.Substring(p.Length - 45);
            }
            catch (Exception) { return p; }
        }
    }
}
