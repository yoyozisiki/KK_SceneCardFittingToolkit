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
    /// <summary>
    /// 场景加载后按性别自动/手动替换角色。
    /// 自动模式:由 KKAPI 的 SceneLoad 事件触发,延迟数帧等角色完全构建后执行 ChangeChara。
    /// 手动模式:按快捷键或调用 ReplaceAllCharacters 立即替换当前场景角色。
    /// </summary>
    public static class SceneReplacer
    {
        private static MonoBehaviour _coroutineHost;
        private static bool _scheduled;
        private static System.Collections.Generic.List<Studio.OCIChar> _pendingCharacters;

        public static void ScheduleReplace(MonoBehaviour host, System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int, Studio.ObjectCtrlInfo>> loadedObjects)
        {
            if (_scheduled) return;
            _scheduled = true;
            _coroutineHost = host;
            _pendingCharacters = CollectCharacters(loadedObjects);
            if (_coroutineHost != null)
                _coroutineHost.StartCoroutine(ReplaceRoutine());
        }

        private static System.Collections.IEnumerator ReplaceRoutine()
        {
            // 等 30 帧,确保场景角色完全加载构建
            for (int i = 0; i < 30; i++)
                yield return null;

            _scheduled = false;
            var chars = _pendingCharacters;
            _pendingCharacters = null;
            ReplaceCharacters(chars);
        }

        public static void ReplaceAllCharacters()
        {
            if (!KKPEHeightLockPlugin.Enabled.Value) return;
            ReplaceCharacters(GetSceneCharacters());
        }

        private static void ReplaceCharacters(System.Collections.Generic.List<Studio.OCIChar> characters)
        {
            try
            {
                var femalePath = KKPEHeightLockPlugin.FemaleCardPath.Value;
                var malePath = KKPEHeightLockPlugin.MaleCardPath.Value;
                if (string.IsNullOrEmpty(femalePath) && string.IsNullOrEmpty(malePath))
                {
                    KKPEHeightLockPlugin.Log.LogWarning("SceneReplacer: no card paths configured, nothing to replace");
                    return;
                }

                int replaced = 0;
                if (characters != null)
                {
                    foreach (var oci in characters)
                    {
                        if (oci == null || oci.charInfo == null) continue;

                        string path = null;
                        try
                        {
                            var sex = oci.sex;
                            if (sex == 1 && !string.IsNullOrEmpty(femalePath) && KKPEHeightLockPlugin.ReplaceFemaleOnLoad.Value)
                                path = femalePath;
                            else if (sex == 0 && !string.IsNullOrEmpty(malePath) && KKPEHeightLockPlugin.ReplaceMaleOnLoad.Value)
                                path = malePath;
                        }
                        catch (Exception e)
                        {
                            KKPEHeightLockPlugin.Log.LogWarning($"SceneReplacer: failed to read sex: {e.Message}");
                            continue;
                        }

                        if (path == null) continue;

                        KKPEHeightLockPlugin.Log.LogMessage($"SceneReplacer: replacing {(oci.sex == 1 ? "female" : "male")} character with {path}");
                        oci.ChangeChara(path);
                        replaced++;
                    }
                }

                KKPEHeightLockPlugin.Log.LogMessage($"SceneReplacer: replaced {replaced} character(s)");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("SceneReplacer error: " + e);
            }
        }

        private static System.Collections.Generic.List<Studio.OCIChar> CollectCharacters(
            System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int, Studio.ObjectCtrlInfo>> loadedObjects)
        {
            var result = new System.Collections.Generic.List<Studio.OCIChar>();
            if (loadedObjects == null) return result;
            foreach (var kv in loadedObjects)
            {
                var oci = kv.Value as Studio.OCIChar;
                if (oci != null)
                    result.Add(oci);
            }
            return result;
        }

        public static System.Collections.Generic.List<Studio.OCIChar> GetSceneCharacters()
        {
            var result = new System.Collections.Generic.List<Studio.OCIChar>();
            try
            {
                // 遍历当前场景所有对象(Studio.Studio.Instance.dicObjectCtrl 含全部对象)
                var studio = Singleton<Studio.Studio>.Instance;
                if (studio == null || studio.dicObjectCtrl == null) return result;
                foreach (var kv in studio.dicObjectCtrl)
                {
                    var oci = kv.Value as Studio.OCIChar;
                    if (oci != null)
                        result.Add(oci);
                }
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("SceneReplacer: GetSceneCharacters error: " + e);
            }
            return result;
        }
    }
}
