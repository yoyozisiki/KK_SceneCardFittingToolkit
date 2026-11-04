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
        // ============ FSS 文件夹子场景 ============

        /// <summary>收集 FSS 候选角色(同文件夹下唯一的角色,且不在根)。</summary>
        public static void RefreshFSS()
        {
            try
            {
                _fssChars.Clear();
                _fssIndex = -1;
                var studio = Singleton<Studio.Studio>.Instance;
                if (studio == null || studio.dicObjectCtrl == null) return;
                foreach (var kv in studio.dicObjectCtrl)
                {
                    var c = kv.Value as Studio.OCIChar;
                    if (c != null && c.charInfo != null)
                        _fssChars.Add(c);
                }
                _fssMode = _fssChars.Count > 1;
                KKPEHeightLockPlugin.Log.LogMessage($"ScenePlayer: FSS candidates {_fssChars.Count}");
            }
            catch (Exception e) { KKPEHeightLockPlugin.Log.LogWarning("ScenePlayer.RefreshFSS error: " + e); }
        }

        /// <summary>切换 FSS:只显示第 i 个角色,其余隐藏。</summary>
        public static void SwitchFSS(int index)
        {
            try
            {
                if (index < 0 || index >= _fssChars.Count) return;
                _fssIndex = index;
                for (int i = 0; i < _fssChars.Count; i++)
                {
                    if (_fssChars[i] != null)
                        SetVisible(_fssChars[i], i == index);
                }
                KKPEHeightLockPlugin.Log.LogMessage($"ScenePlayer: FSS switched to {index + 1}/{_fssChars.Count}");
            }
            catch (Exception e) { KKPEHeightLockPlugin.Log.LogWarning("ScenePlayer.SwitchFSS error: " + e); }
        }

        public static int FssCount { get { return _fssChars.Count; } }
        public static int FssIndex { get { return _fssIndex; } }
        public static bool FssMode { get { return _fssMode; } }
    }
}
