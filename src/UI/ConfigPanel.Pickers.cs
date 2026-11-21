using System;
using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>
    /// 弹框类界面：子目录选择 + 选卡入口。
    /// 选卡窗口本身画在 CardPickerWindow.cs，数据来自后端的 <see cref="CardLibrary"/>。
    /// </summary>
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

        /// <summary>打开游戏内选卡窗口（单选，结果给场景替换的配置）。</summary>
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
                _cardSearch = "";
                _cardDir = null;          // 每次打开都从根目录起（_cardDir 由 CardLibrary.Root 兜底）
                _cardViewKey = null;
                CardLibrary.Ensure(isFemale);
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: PickCard error: " + e);
            }
        }

        /// <summary>导出主角色卡（单选）。</summary>
        public static void PickCardForExport()
        {
            PickCard(true, 1);
        }

        /// <summary>导出次角色卡（多选，点一下加入/移出，编号就是替换顺序）。</summary>
        public static void PickSubCards()
        {
            PickCard(true, 2);
        }
    }
}
