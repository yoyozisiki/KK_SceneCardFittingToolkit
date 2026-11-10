using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using HSPE.AMModules;
using KKAPI.Studio.SaveLoad;
using UnityEngine;

namespace KKPEHeightLock
{
public static partial class ConfigPanel
    {

        /// <summary>子目录选择窗口:列出当前层级子文件夹,点进下一级,可返回上级,选择/根目录/取消。</summary>
        private static void DrawSubdirPicker(int id)
        {
            // 和选卡弹框同一套黑底白字皮肤（游戏自带 IMGUI 皮肤是半透明的）
            var stBtn = AutoExportPanel.ThemeButton;
            var stLabel = AutoExportPanel.ThemeLabel;
            var stField = AutoExportPanel.ThemeField;
            var stPanel = AutoExportPanel.ThemePanel;

            GUILayout.BeginVertical();

            // 右上角关闭：和选卡弹框保持一致
            if (GUI.Button(new Rect(_subdirPickerRect.width - 30f, 4f, 24f, 22f), "×", stBtn))
                _subdirPickerVisible = false;

            // 当前路径
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L10n.T("↑ 上级"), stBtn, GUILayout.Width(70), GUILayout.Height(26)))
            {
                // 返回上级
                int idx = _subdirPickerPath.LastIndexOf('/');
                _subdirPickerPath = idx >= 0 ? _subdirPickerPath.Substring(0, idx) : "";
                RefreshSubdirList();
            }
            GUILayout.Label(L10n.T("路径: ") + (string.IsNullOrEmpty(_subdirPickerPath) ? L10n.T("(根目录)") : _subdirPickerPath),
                stField, GUILayout.ExpandWidth(true), GUILayout.Height(26));
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // 子文件夹列表(垂直列出,不横向溢出)
            _subdirScroll = GUILayout.BeginScrollView(_subdirScroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, stPanel);
            if (_subdirList.Count == 0)
            {
                GUILayout.Label(L10n.T("(此目录下没有子文件夹)"), stLabel);
            }
            else
            {
                foreach (var sub in _subdirList)
                {
                    if (GUILayout.Button("▸ " + sub, stBtn, GUILayout.Height(28)))
                    {
                        // 进入下一级
                        _subdirPickerPath = string.IsNullOrEmpty(_subdirPickerPath) ? sub : _subdirPickerPath + "/" + sub;
                        RefreshSubdirList();
                        break;
                    }
                }
            }
            GUILayout.EndScrollView();

            GUILayout.Space(4);

            // 底部操作:选择当前路径 / 根目录 / 取消
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L10n.T("选择此目录"), stBtn, GUILayout.Height(28)))
            {
                ScenePlayerModule.ApplySubdir(_subdirPickerMode, _subdirPickerPath);
                _subdirPickerVisible = false;
            }
            if (GUILayout.Button(L10n.T("根目录"), stBtn, GUILayout.Height(28)))
            {
                ScenePlayerModule.ApplySubdir(_subdirPickerMode, "");
                _subdirPickerVisible = false;
            }
            if (GUILayout.Button(L10n.T("取消"), stBtn, GUILayout.Height(28)))
                _subdirPickerVisible = false;
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        /// <summary>打开游戏内选卡窗口(类似工作室本体,带缩略图列表)。</summary>
        private static void PickCard(bool isFemale)
        {
            PickCard(isFemale, 0);
        }

        private static void PickCard(bool isFemale, int target)
        {
            try
            {
                _cardPickerIsFemale = isFemale;
                _cardPickTarget = target;
                _cardPickerVisible = true;
                _cardScroll = Vector2.zero;
                _cardPickerCurrentDir = "";
                _cardSearch = "";
                LoadCardList();
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: PickCard error: " + e);
            }
        }

        /// <summary>批量导出窗口用的选卡入口：结果写进 ExportFemaleCard。</summary>
        public static void PickCardForExport()
        {
            PickCard(true, 1);
        }

        /// <summary>只读取当前目录下的角色卡和直接子文件夹(不递归),卡多也不卡。</summary>
        private static void LoadCardList()
        {
            _cardEntries = new System.Collections.Generic.List<CardEntry>();
            _cardSubFolders = new System.Collections.Generic.List<string>();
            _cardScroll = Vector2.zero;
            try
            {
                string baseDir = System.IO.Path.Combine(UserData.Path, _cardPickerIsFemale ? "chara/female" : "chara/male");
                string currentDir = string.IsNullOrEmpty(_cardPickerCurrentDir)
                    ? baseDir
                    : System.IO.Path.Combine(baseDir, _cardPickerCurrentDir);

                // 直接子文件夹(仅当前层级)
                foreach (var d in System.IO.Directory.GetDirectories(currentDir))
                {
                    string name = System.IO.Path.GetFileName(d);
                    if (name.Length > 0)
                        _cardSubFolders.Add(name);
                }
                _cardSubFolders.Sort(StringComparer.OrdinalIgnoreCase);

                // 当前目录的直接角色卡
                foreach (var f in System.IO.Directory.GetFiles(currentDir, "*.png"))
                    _cardEntries.Add(new CardEntry { path = f });

                KKPEHeightLockPlugin.Log.LogInfo($"KKPEHeightLock: folder '{_cardPickerCurrentDir}': {_cardEntries.Count} cards, {_cardSubFolders.Count} subfolders");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: LoadCardList error: " + e);
            }
        }

        /// <summary>懒加载入口:每帧最多处理若干张卡的元数据/缩略图,避免一帧卡死。</summary>
        private static void LazyLoadCards(int maxPerFrame)
        {
            _cardsProcessedThisFrame = 0;
            if (_cardEntries == null) return;
            foreach (var entry in _cardEntries)
            {
                if (_cardsProcessedThisFrame >= maxPerFrame) return;

                // 只处理搜索条件下会显示的卡
                if (!string.IsNullOrEmpty(_cardSearch))
                {
                    string nm = entry.name ?? System.IO.Path.GetFileNameWithoutExtension(entry.path);
                    if (nm.IndexOf(_cardSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                }

                // 1) 角色名(元数据)懒加载
                if (!entry.metaLoaded)
                {
                    try
                    {
                        var chaFile = new ChaFileControl();
                        if (chaFile.LoadCharaFile(entry.path, 255, true, true))
                            entry.name = chaFile.parameter != null && !string.IsNullOrEmpty(chaFile.parameter.fullname)
                                ? chaFile.parameter.fullname
                                : System.IO.Path.GetFileNameWithoutExtension(entry.path);
                        else
                            entry.name = System.IO.Path.GetFileNameWithoutExtension(entry.path);
                    }
                    catch (Exception)
                    {
                        entry.name = System.IO.Path.GetFileNameWithoutExtension(entry.path);
                    }
                    entry.metaLoaded = true;
                    _cardsProcessedThisFrame++;
                    continue;
                }

                // 2) 缩略图懒加载(显示到的才加载)
                if (entry.texture == null)
                {
                    try { entry.texture = PngAssist.LoadTexture(entry.path); }
                    catch (Exception) { }
                    _cardsProcessedThisFrame++;
                    continue;
                }
            }
        }

        /// <summary>选卡窗口:目录导航 + 搜索框 + 缩略图网格(懒加载)。</summary>
        private static void DrawCardPicker(int id)
        {
            // 用批量导出那套黑底白字皮肤：游戏自带的 IMGUI 皮肤是半透明的，
            // 叠在亮画面上根本看不清。
            var stBtn = AutoExportPanel.ThemeButton;
            var stBtnSm = AutoExportPanel.ThemeButtonSm;
            var stLabel = AutoExportPanel.ThemeLabel;
            var stLabelSm = AutoExportPanel.ThemeLabelSm;
            var stBold = AutoExportPanel.ThemeLabelBold;
            var stField = AutoExportPanel.ThemeField;

            GUILayout.BeginVertical();

            // 右上角关闭：弹框可能被别的窗口压住一部分，留个固定的出口
            if (GUI.Button(new Rect(_cardPickerRect.width - 30f, 4f, 24f, 22f), "×", stBtnSm))
                _cardPickerVisible = false;

            // 每帧懒加载最多 6 张(元数据或缩略图),保持流畅
            LazyLoadCards(6);

            // --- 目录导航 ---
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L10n.T("← 上级"), stBtn, GUILayout.Width(70), GUILayout.Height(26)))
            {
                if (_cardPickerCurrentDir.Length > 0)
                {
                    int idx = _cardPickerCurrentDir.LastIndexOfAny(new[] { '\\', '/' });
                    _cardPickerCurrentDir = idx >= 0 ? _cardPickerCurrentDir.Substring(0, idx) : "";
                    LoadCardList();
                }
            }
            GUILayout.Label(L10n.T("目录: ") + (_cardPickerCurrentDir.Length > 0 ? _cardPickerCurrentDir : L10n.T("(根目录)")), stLabel, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            // --- 子文件夹(点"选择子文件夹"弹出子目录窗口) ---
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L10n.T("选择子文件夹"), stBtn, GUILayout.Width(96), GUILayout.Height(26)))
                OpenSubdirPicker(3);
            GUILayout.Label(L10n.T("目录: ") + (_cardPickerCurrentDir.Length > 0 ? _cardPickerCurrentDir : L10n.T("(根目录)")), stLabel, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            // --- 搜索框 ---
            GUILayout.BeginHorizontal();
            GUILayout.Label(L10n.T("搜索:"), stLabel, GUILayout.Width(55));
            _cardSearch = GUILayout.TextField(_cardSearch ?? "", stField, GUILayout.MinWidth(200), GUILayout.Height(26));
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // --- 卡片网格 ---
            if (_cardEntries == null || _cardEntries.Count == 0)
            {
                GUILayout.Label(L10n.T("当前目录下没有角色卡。"), stLabel);
            }
            else
            {
                // 搜索过滤
                var filtered = new System.Collections.Generic.List<CardEntry>();
                foreach (var e in _cardEntries)
                {
                    if (!string.IsNullOrEmpty(_cardSearch))
                    {
                        string nm = e.name ?? System.IO.Path.GetFileNameWithoutExtension(e.path);
                        if (nm.IndexOf(_cardSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    }
                    filtered.Add(e);
                }

                if (filtered.Count == 0)
                {
                    GUILayout.Label(L10n.T("没有匹配的卡。"), stLabel);
                }
                else
                {
                    _cardScroll = GUILayout.BeginScrollView(_cardScroll);

                    const float thumbWidth = 128f;
                    int columns = Mathf.Max(1, (int)((_cardPickerRect.width - 40f) / (thumbWidth + 8f)));

                    int index = 0;
                    while (index < filtered.Count)
                    {
                        GUILayout.BeginHorizontal();
                        for (int c = 0; c < columns && index < filtered.Count; c++, index++)
                        {
                            var entry = filtered[index];
                            GUILayout.BeginVertical(GUILayout.Width(thumbWidth));

                            // 缩略图(懒加载)
                            if (entry.texture != null)
                                GUILayout.Label(entry.texture, GUILayout.Width(thumbWidth), GUILayout.Height(thumbWidth));
                            else
                                GUILayout.Box("…", GUILayout.Width(thumbWidth), GUILayout.Height(thumbWidth));

                            string displayName = entry.name ?? System.IO.Path.GetFileNameWithoutExtension(entry.path);
                            GUILayout.Label(Truncate(displayName, 14), stLabelSm, GUILayout.Width(thumbWidth));

                            // 只留「选择」。这个弹框就是给批量导出挑一张卡，
                            // 「直接替换 / 替换选中(单个)」是旧的工作室替换入口，这里用不上。
                            if (GUILayout.Button(L10n.T("选择"), stBtnSm,
                                    GUILayout.Width(thumbWidth), GUILayout.Height(26)))
                            {
                                if (_cardPickTarget == 1)
                                    KKPEHeightLockPlugin.ExportFemaleCard.Value = PathFix.ToConfig(entry.path);
                                else if (_cardPickerIsFemale)
                                    KKPEHeightLockPlugin.FemaleCardPath.Value = entry.path;
                                else
                                    KKPEHeightLockPlugin.MaleCardPath.Value = entry.path;
                                KKPEHeightLockPlugin.Log.LogMessage($"KKPEHeightLock: {(_cardPickerIsFemale ? "female" : "male")} card set to {entry.path}");
                                _cardPickerVisible = false;
                            }

                            GUILayout.EndVertical();
                        }
                        GUILayout.EndHorizontal();
                    }

                    GUILayout.EndScrollView();
                }
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L10n.T("关闭"), stBtn, GUILayout.Width(80), GUILayout.Height(26)))
                _cardPickerVisible = false;
            GUILayout.Label(_cardEntries.Count + L10n.T(" 张卡"), stBold, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }
    }
}
