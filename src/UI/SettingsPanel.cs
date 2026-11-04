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

        private static void DrawRightColumn(float bodyH)
        {
            // ---- 角色卡
            GUILayout.BeginVertical(_panel);
            GUILayout.Label(L10n.T("替换女性角色卡"), _section);
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            DrawCardPreview();
            GUILayout.Space(8);
            GUILayout.BeginVertical();

            string card = KKPEHeightLockPlugin.ExportFemaleCard.Value;
            GUILayout.Label(string.IsNullOrEmpty(card) ? L10n.T("(未选择)")
                : Path.GetFileNameWithoutExtension(card), _labelBold);
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
            GUILayout.EndVertical();

            GUILayout.Space(8);

            // ---- 导出设置
            GUILayout.BeginVertical(_panel, GUILayout.ExpandHeight(true));
            GUILayout.Label(L10n.T("导出设置"), _section);
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label(L10n.T("语言"), _label, GUILayout.Width(72));
            Dropdown("lang", 150f, L10n.LanguageNames,
                Mathf.Clamp((int)KKPEHeightLockPlugin.UiLanguage.Value, 0,
                            L10n.LanguageNames.Length - 1),
                delegate(int i) { KKPEHeightLockPlugin.UiLanguage.Value = (UiLang)i; });
            GUILayout.EndHorizontal();

            float setH = Mathf.Max(160f, bodyH - 244f);
            _setScroll = GUILayout.BeginScrollView(_setScroll, false, true,
                GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none,
                GUILayout.Height(setH));

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

            HLine();

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

            HLine();

            GUILayout.Label(L10n.T("后期：ReShade 实时（泛光 + 暗角）"), _labelSm);
            GUILayout.Label(L10n.T("强度改 KK_BloomVignette.ini，重启生效"), _labelSm);

            HLine();

            GUILayout.BeginHorizontal();
            KKPEHeightLockPlugin.ExportMerge.Value = GUILayout.Toggle(
                KKPEHeightLockPlugin.ExportMerge.Value, L10n.T("  合并成一个视频"), _toggle,
                GUILayout.Width(190), GUILayout.Height(28));
            KKPEHeightLockPlugin.ExportKeepSegments.Value = GUILayout.Toggle(
                KKPEHeightLockPlugin.ExportKeepSegments.Value, L10n.T("  保留分段视频"), _toggle,
                GUILayout.Width(180), GUILayout.Height(28));
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
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

        private static void DrawCardPreview()
        {
            const int S = 100;
            string card = KKPEHeightLockPlugin.ExportFemaleCard.Value;
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
