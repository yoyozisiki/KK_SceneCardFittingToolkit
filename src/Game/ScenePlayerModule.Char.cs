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
    public static partial class ScenePlayerModule
    {
        // ============ 场景人物选择(链接工作室选中)============

        /// <summary>获取当前场景所有角色(OCIChar 列表)。</summary>
        public static System.Collections.Generic.List<Studio.OCIChar> GetSceneCharacters()
        {
            return SceneReplacer.GetSceneCharacters();
        }

        /// <summary>在工作室场景树中选中指定角色(链接 Studio 原生选中)。</summary>
        public static void SelectCharacter(Studio.OCIChar chara)
        {
            try
            {
                var studio = Singleton<Studio.Studio>.Instance;
                if (studio == null || studio.treeNodeCtrl == null || chara == null) return;
                foreach (var kv in studio.dicInfo)
                {
                    if (kv.Value == chara)
                    {
                        studio.treeNodeCtrl.SelectSingle(kv.Key, false);
                        KKPEHeightLockPlugin.Log.LogMessage($"ScenePlayer: selected character {chara.charInfo?.fileParam?.fullname ?? "(unnamed)"}");
                        return;
                    }
                }
                KKPEHeightLockPlugin.Log.LogWarning("ScenePlayer: character not found in scene tree");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("ScenePlayer.SelectCharacter error: " + e);
            }
        }

        /// <summary>角色是否当前被工作室选中。</summary>
        public static bool IsCharacterSelected(Studio.OCIChar chara)
        {
            try
            {
                var studio = Singleton<Studio.Studio>.Instance;
                if (studio == null || studio.treeNodeCtrl == null || chara == null || studio.dicInfo == null) return false;
                var nodes = studio.treeNodeCtrl.selectNodes;
                if (nodes == null) return false;
                foreach (var n in nodes)
                {
                    Studio.ObjectCtrlInfo oci;
                    if (n != null && studio.dicInfo.TryGetValue(n, out oci) && oci == chara)
                        return true;
                }
            }
            catch (Exception) { }
            return false;
        }
    }
}
