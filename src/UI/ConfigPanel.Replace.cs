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
        internal static int ReplaceSelectedCharacter(string cardPath)
        {
            int replaced = 0;
            try
            {
                if (string.IsNullOrEmpty(cardPath) || !System.IO.File.Exists(cardPath))
                {
                    KKPEHeightLockPlugin.Log.LogWarning($"KKPEHeightLock: card not found: {cardPath}");
                    return 0;
                }
                // 遍历当前选中对象,替换其中所有角色
                var selected = KKAPI.Studio.StudioAPI.GetSelectedObjects();
                if (selected == null) return 0;
                foreach (var obj in selected)
                {
                    var oci = obj as Studio.OCIChar;
                    if (oci == null || oci.charInfo == null) continue;
                    KKPEHeightLockPlugin.Log.LogMessage($"KKPEHeightLock: replacing selected character with {cardPath}");
                    oci.ChangeChara(cardPath);
                    replaced++;
                }
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: ReplaceSelectedCharacter error: " + e);
            }
            return replaced;
        }

        /// <summary>替换选中角色:必须恰好选中 1 个角色才执行,否则提示并拒绝。
        /// 用于"替换选中(单个)"按钮,避免误替换全部。</summary>
        internal static bool ReplaceSingleSelectedCharacter(string cardPath)
        {
            try
            {
                if (string.IsNullOrEmpty(cardPath) || !System.IO.File.Exists(cardPath))
                {
                    KKPEHeightLockPlugin.Log.LogWarning($"KKPEHeightLock: card not found: {cardPath}");
                    return false;
                }

                // 收集当前所有选中的角色
                var selectedChars = new System.Collections.Generic.List<Studio.OCIChar>();
                var selected = KKAPI.Studio.StudioAPI.GetSelectedObjects();
                if (selected != null)
                {
                    foreach (var obj in selected)
                    {
                        var oci = obj as Studio.OCIChar;
                        if (oci != null && oci.charInfo != null)
                            selectedChars.Add(oci);
                    }
                }

                if (selectedChars.Count == 0)
                {
                    KKPEHeightLockPlugin.Log.LogWarning(L10n.T("KKPEHeightLock: no character selected — 请先在工作室中选中要替换的角色"));
                    return false;
                }
                if (selectedChars.Count > 1)
                {
                    KKPEHeightLockPlugin.Log.LogWarning($"KKPEHeightLock: {selectedChars.Count} characters selected — 替换选中(单个)要求恰好选中 1 个角色,已取消");
                    return false;
                }

                var target = selectedChars[0];
                KKPEHeightLockPlugin.Log.LogMessage($"KKPEHeightLock: replacing single selected character with {cardPath}");
                target.ChangeChara(cardPath);
                return true;
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: ReplaceSingleSelectedCharacter error: " + e);
                return false;
            }
        }

        /// <summary>选卡窗口的根目录(相对 UserData):女/男卡目录。</summary>
        internal static string CardPickerBaseDir()
        {
            return _cardPickerIsFemale ? "chara/female" : "chara/male";
        }

        /// <summary>设置选卡窗口的当前目录(相对根)。</summary>
        internal static void SetCardPickerDir(string path)
        {
            _cardPickerCurrentDir = path ?? "";
            LoadCardList();
        }

        /// <summary>打开子目录选择窗口(0=场景 1=随机女 2=随机男 3=选卡)。</summary>
        private static void OpenSubdirPicker(int mode)
        {
            _subdirPickerMode = mode;
            _subdirPickerPath = "";
            _subdirPickerVisible = true;
            _subdirScroll = Vector2.zero;
            RefreshSubdirList();
        }

        private static void RefreshSubdirList()
        {
            _subdirList = ScenePlayerModule.ListSubdirsAt(_subdirPickerMode, _subdirPickerPath);
        }
    }
}
