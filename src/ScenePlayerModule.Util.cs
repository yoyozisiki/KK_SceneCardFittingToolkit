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
        // ============ 工具 ============

        /// <summary>当前场景显示名。</summary>
        public static string SceneLabel()
        {
            EnsureScenes();
            if (_sceneFiles.Count == 0) return L10n.T("(无场景)");
            int idx = _sceneIndex < 0 ? 0 : _sceneIndex;
            return $"{(idx + 1)}/{_sceneFiles.Count} {System.IO.Path.GetFileNameWithoutExtension(_sceneFiles[idx])}";
        }

        /// <summary>FSS 显示名。</summary>
        public static string FssLabel()
        {
            if (_fssChars.Count == 0) return L10n.T("FSS: 无");
            return $"FSS: {_fssIndex + 1}/{_fssChars.Count}";
        }

        /// <summary>Timeline 显示名。</summary>
        public static string TimelineLabel()
        {
            try
            {
                float t = Timeline.Timeline.playbackTime;
                float d = Timeline.Timeline.duration;
                return string.Format("{0:0.0}/{1:0.0} {2}x", t, d, _timeScale);
            }
            catch (Exception) { return L10n.T("Timeline 未加载"); }
        }

        /// <summary>当前编辑角色:实时取当前工作室选中的第一个角色(不缓存,换人立即生效)。</summary>
        public static Studio.OCIChar CurrentEditChar()
        {
            try
            {
                var selected = KKAPI.Studio.StudioAPI.GetSelectedCharacters();
                if (selected != null)
                {
                    foreach (var c in selected)
                    {
                        if (c != null && c.charInfo != null)
                            return c;
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        private static string ResolvePath(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            if (p.StartsWith("UserData", StringComparison.OrdinalIgnoreCase))
                return System.IO.Path.Combine(UserData.Path, p.Substring("UserData/".Length));
            return p;
        }
    }
}
