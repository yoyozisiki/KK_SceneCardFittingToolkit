using System;
using System.IO;
using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>右栏：替换角色卡 + 导出设置。</summary>
    public static partial class AutoExportPanel
    {
        // 0~3 对上 CoordinateType 的私服/泳装/睡衣/浴衣，4~6 是 mod 扩出来的槽位。
        // 译文缓存：语言切换后重建一次，别每帧 new 数组。
        private static string[] _outfitNames;
        private static int _outfitNamesVer = -1;
        private static string[] OutfitNames
        {
            get
            {
                if (_outfitNames == null || _outfitNamesVer != L10n.Version)
                {
                    _outfitNamesVer = L10n.Version;
                    _outfitNames = new[]
                    {
                        L10n.T("不改变"), L10n.T("私服"), L10n.T("泳装"), L10n.T("睡衣"),
                        L10n.T("浴衣"), L10n.T("校内"), L10n.T("体操"), L10n.T("社团")
                    };
                }
                return _outfitNames;
            }
        }

        private static Vector2 _setScroll;
        private static Texture2D _previewTex;
        private static string _previewPath;
        private static float _cardH;    // 角色卡区高度

        private static void DrawRightColumn(float bodyH)
        {
            // ---- 角色卡
            GUILayout.BeginVertical(_panel);
            GUILayout.Label(L10n.T("替换女性角色卡"), _section);
            Rect lblRect = GUILayoutUtility.GetLastRect();
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            DrawCardPreview();
            GUILayout.Space(8);
            GUILayout.BeginVertical();

            // 走 PathFix：cfg 里的 `\a` 会被 BepInEx 当成 BEL 读回来，
            // 直接喂给 Path.GetFileNameWithoutExtension 会抛 Illegal characters in path，
            // 整个面板就黑掉（v2.0.2 修复）。
            string card = PathFix.Read(KKPEHeightLockPlugin.ExportFemaleCard.Value);
            GUILayout.Label(string.IsNullOrEmpty(card) ? L10n.T("(未选择)")
                : SafeFileName(card), _labelBold);
            GUILayout.Label(string.IsNullOrEmpty(card) ? L10n.T("未选角色卡") : ShortPath(card),
                _labelSm);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L10n.T("选择角色卡…"), _btn, GUILayout.Width(140), GUILayout.Height(30)))
                ConfigPanel.PickCardForExport();
            if (!string.IsNullOrEmpty(card)
                && GUILayout.Button(L10n.T("清除"), _btn, GUILayout.Width(72), GUILayout.Height(30)))
                KKPEHeightLockPlugin.ExportFemaleCard.Value = "";
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            KKPEHeightLockPlugin.ExportReplaceFemale.Value = GUILayout.Toggle(
                KKPEHeightLockPlugin.ExportReplaceFemale.Value,
                L10n.T("  替换所有 female（保留 male）"), _toggle, GUILayout.Height(28));
            Rect togRect = GUILayoutUtility.GetLastRect();
            GUILayout.EndVertical();
            // 角色卡区高度 = 从标题顶部到 toggle 底边 + 面板上下内边距 8 + 与下面的 8 间隙
            if (lblRect.height > 1f && togRect.height > 1f && togRect.yMax > lblRect.y)
                _cardH = togRect.yMax - lblRect.y + 16f;

            GUILayout.Space(8);

            // ---- 导出设置（不再单独占一行标题，直接是设置项滚动区）
            GUILayout.BeginVertical(_panel, GUILayout.ExpandHeight(true));

            // 滚动区高度 = 右栏总高 - 角色卡区实际高度 - 间隙。
            // 以前写死 `bodyH-244`：角色卡区一变高（换行 / 缩放 / 多语言 / 预览图）
            // 就算不准，滚动视口伸到底栏下面 —— 最后那行（合并 / 保留分段）
            // 永远被底栏盖住。现在量角色卡区的真实高度，不猜；首帧没量到就先用经验值。
            float cardH = _cardH > 60f ? _cardH : 200f;
            float setH = Mathf.Max(160f, bodyH - cardH - 12f);

            _setScroll = GUILayout.BeginScrollView(_setScroll, false, true,
                GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none,
                GUILayout.Height(setH));

            // 语言也跟着滚：以前它钉在滚动区外面，列表一长就它不跟着走
            GUILayout.BeginHorizontal();
            GUILayout.Label(L10n.T("语言"), _label, GUILayout.Width(72));
            Dropdown("lang", 150f, L10n.LanguageNames,
                Mathf.Clamp((int)KKPEHeightLockPlugin.UiLanguage.Value, 0,
                            L10n.LanguageNames.Length - 1),
                delegate(int i) { KKPEHeightLockPlugin.UiLanguage.Value = (UiLang)i; });
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(L10n.T("输出尺寸"), _label, GUILayout.Width(72));
            IntField(KKPEHeightLockPlugin.ExportResizeWidth, 62);
            GUILayout.Label("×", _label, GUILayout.Width(14));
            IntField(KKPEHeightLockPlugin.ExportResizeHeight, 62);
            GUILayout.Label(L10n.T("0 = 窗口原尺寸"), _labelSm);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(L10n.T("帧率"), _label, GUILayout.Width(72));
            IntField(KKPEHeightLockPlugin.ExportFps, 62);
            GUILayout.Label("fps", _labelSm, GUILayout.Width(28));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(L10n.T("时长"), _label, GUILayout.Width(46));
            GUILayout.Label(L10n.T("按 Timeline，不足补齐到"), _label, GUILayout.Width(176));
            FloatField(KKPEHeightLockPlugin.ExportMinSeconds, 58);
            GUILayout.Label(L10n.T("秒"), _labelSm, GUILayout.Width(24));
            GUILayout.EndHorizontal();
            GUILayout.Label(L10n.T("时间轴更长按时间轴；更短或没有时间轴就录这么久（循环补齐）"),
                _labelSm);

            GUILayout.BeginHorizontal();
            GUILayout.Label(L10n.T("头尾各多录"), _label, GUILayout.Width(92));
            FloatField(KKPEHeightLockPlugin.ExportFadeSeconds, 58);
            GUILayout.Label(L10n.T("秒"), _labelSm);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(L10n.T("换人后等待"), _label, GUILayout.Width(92));
            FloatField(KKPEHeightLockPlugin.ExportReplaceSettle, 58);
            GUILayout.Label(L10n.T("秒（等头发/裙子物理稳定）"), _labelSm);
            GUILayout.EndHorizontal();

            HLine();

            GUILayout.Label(L10n.T("体型来源：选中 = 场景原角色，未选中 = 角色卡"), _labelSm);
            GUILayout.BeginHorizontal();
            KKPEHeightLockPlugin.ExportShapeFromScene.Value = GUILayout.Toggle(
                KKPEHeightLockPlugin.ExportShapeFromScene.Value, L10n.T("  体型滑块"), _toggle,
                GUILayout.Width(128), GUILayout.Height(28));
            KKPEHeightLockPlugin.ExportBustFromScene.Value = GUILayout.Toggle(
                KKPEHeightLockPlugin.ExportBustFromScene.Value, L10n.T("  乳量/物理"), _toggle,
                GUILayout.Width(128), GUILayout.Height(28));
            KKPEHeightLockPlugin.ExportBoneFromScene.Value = GUILayout.Toggle(
                KKPEHeightLockPlugin.ExportBoneFromScene.Value, L10n.T("  骨架类型"), _toggle,
                GUILayout.Width(128), GUILayout.Height(28));
            GUILayout.EndHorizontal();

            KKPEHeightLockPlugin.ExportLockHeight.Value = GUILayout.Toggle(
                KKPEHeightLockPlugin.ExportLockHeight.Value,
                L10n.T("  锁定身高 cf_n_height"), _toggle, GUILayout.Width(240), GUILayout.Height(28));

            HLine();

            GUILayout.BeginHorizontal();
            GUILayout.Label(L10n.T("服装预设"), _label, GUILayout.Width(78));
            Dropdown("outfit", 150f, OutfitNames,
                Mathf.Clamp(KKPEHeightLockPlugin.ExportOutfit.Value + 1, 0, OutfitNames.Length - 1),
                delegate(int i) { KKPEHeightLockPlugin.ExportOutfit.Value = i - 1; });
            GUILayout.Label(L10n.T("不改 = 保留场景原服装"), _labelSm);
            GUILayout.EndHorizontal();

            DrawPartsRows();

            HLine();

            GUILayout.BeginHorizontal();
            KKPEHeightLockPlugin.ExportDecensor.Value = GUILayout.Toggle(
                KKPEHeightLockPlugin.ExportDecensor.Value, L10n.T("  去马赛克/圣光"), _toggle,
                GUILayout.Width(190), GUILayout.Height(28));
            KKPEHeightLockPlugin.ExportRemoveSpheres.Value = GUILayout.Toggle(
                KKPEHeightLockPlugin.ExportRemoveSpheres.Value, L10n.T("  去无用球体"), _toggle,
                GUILayout.Width(160), GUILayout.Height(28));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            KKPEHeightLockPlugin.ExportMerge.Value = GUILayout.Toggle(
                KKPEHeightLockPlugin.ExportMerge.Value, L10n.T("  合并成一个视频"), _toggle,
                GUILayout.Width(190), GUILayout.Height(28));
            KKPEHeightLockPlugin.ExportKeepSegments.Value = GUILayout.Toggle(
                KKPEHeightLockPlugin.ExportKeepSegments.Value, L10n.T("  保留分段视频"), _toggle,
                GUILayout.Width(180), GUILayout.Height(28));
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();

            // 滚轮自己处理：这个游戏里 IMGUI 收不到 ScrollWheel（被 Studio 的相机缩放吃掉），
            // 滚动条只能拖、滚轮滚不动。这里直接读输入，在滚动区内滚。
            ScrollWheel(setH, cardH);

            GUILayout.EndVertical();
        }

        /// <summary>鼠标在设置区里时，用滚轮滚动设置列表。</summary>
        private static void ScrollWheel(float viewH, float cardH)
        {
            if (!_visible) return;
            var e = Event.current;
            if (e == null) return;

            // 右栏 x 起点：左边距 8 + 左栏宽 (rect.width-588) + 间隙 10
            float colX = 8f + (_rect.width - 588f) + 10f;
            float top = 8f + cardH + 8f;
            if (!new Rect(colX, top, 556f, viewH).Contains(e.mousePosition)) return;
            float dy = Input.mouseScrollDelta.y;
            if (Mathf.Abs(dy) < 0.01f) return;
            _setScroll.y = Mathf.Max(0f, _setScroll.y - dy * 40f);
        }

        /// <summary>9 个部件一行两个下拉，选项按部位给。</summary>
        private static void DrawPartsRows()
        {
            var parts = Parts;
            int col = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (col == 0) GUILayout.BeginHorizontal();
                GUILayout.Label(AutoExport.ClothesKindLabel(i), _label, GUILayout.Width(58));

                var states = AutoExport.ClothesKindStates(i);
                var items = new string[states.Length + 1];
                items[0] = L10n.T("不改");
                int cur = 0;
                for (int s = 0; s < states.Length; s++)
                {
                    items[s + 1] = AutoExport.ClothesStateLabel(states[s]);
                    if (parts[i] == states[s]) cur = s + 1;
                }

                int slot = i;
                Dropdown("part" + slot, 116f, items, cur, delegate(int pick)
                {
                    var st = AutoExport.ClothesKindStates(slot);
                    var arr = Parts;
                    arr[slot] = (pick == 0) ? -1 : st[pick - 1];
                    _partsLoaded = AutoExport.FormatClothesParts(arr);
                    KKPEHeightLockPlugin.ExportClothesParts.Value = _partsLoaded;
                }, 26f);

                if (++col >= 2) { GUILayout.EndHorizontal(); col = 0; }
            }
            if (col != 0) GUILayout.EndHorizontal();
        }

        /// <summary>
        /// 取文件名做显示。路径里有非法字符时 Path.GetFileNameWithoutExtension 会抛异常，
        /// 而这里在 OnGUI 里 —— 一抛就是整块面板变黑，所以必须兜住。
        /// </summary>
        private static string SafeFileName(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            try { return Path.GetFileNameWithoutExtension(p); }
            catch (Exception) { return p; }
        }

        private static void DrawCardPreview()
        {
            const int S = 100;
            string card = PathFix.Read(KKPEHeightLockPlugin.ExportFemaleCard.Value);
            if (!string.IsNullOrEmpty(card) && card != _previewPath)
            {
                if (_previewTex != null) { UnityEngine.Object.Destroy(_previewTex); _previewTex = null; }
                _previewPath = card;
                try { _previewTex = LoadCardPng(card); }
                catch (Exception) { _previewTex = null; }
            }

            var r = GUILayoutUtility.GetRect(S, S, GUILayout.Width(S), GUILayout.Height(S));
            GUI.DrawTexture(r, _texField);
            if (_previewTex != null)
            {
                float ar = _previewTex.width / (float)_previewTex.height;
                float w = S, h = S;
                if (ar > 1f) h = S / ar; else w = S * ar;
                GUI.DrawTexture(new Rect(r.x + (S - w) * 0.5f, r.y + (S - h) * 0.5f, w, h), _previewTex);
            }
            else GUI.Label(r, L10n.T("  无预览"), _labelSm);
        }

        // ---------------------------------------------------------------- 部件缓存
        private static int[] _parts;
        private static string _partsLoaded;

        /// <summary>
        /// 缓存部件数组。不能每帧从配置字符串解析再写回 —— ConfigEntry 一变就落盘，
        /// 每帧写一次等于每帧一个磁盘 IO。
        /// </summary>
        private static int[] Parts
        {
            get
            {
                string cfg = KKPEHeightLockPlugin.ExportClothesParts.Value ?? "";
                int n = AutoExport.ClothesKindNames.Length;
                if (_parts == null || _parts.Length != n || _partsLoaded != cfg)
                {
                    _partsLoaded = cfg;
                    _parts = AutoExport.ParseClothesParts(cfg, n);
                }
                return _parts;
            }
        }
    }
}
