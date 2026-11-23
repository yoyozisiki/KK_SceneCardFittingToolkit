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
    /// 场景播放器：场景浏览/上下一张/加载/删除（自然排序）、随机替换角色、
    /// 角色快速编辑、Timeline 控制、FSS 子场景切换。
    /// </summary>
    public static partial class ScenePlayerModule
    {
        // 场景列表状态
        private static System.Collections.Generic.List<string> _sceneFiles = new System.Collections.Generic.List<string>();
        private static int _sceneIndex = -1;
        private static bool _scenesLoaded;

        // FSS 状态
        private static System.Collections.Generic.List<Studio.OCIChar> _fssChars = new System.Collections.Generic.List<Studio.OCIChar>();
        private static int _fssIndex = -1;

        // ============ 场景浏览 ============

        private static string _sceneSubdir = ""; // 当前场景子目录(相对 SceneDir)

        /// <summary>当前场景目录:固定为配置 SceneDir(+子目录),保证 ◀▶ 能在全库场景间推进;
        /// 不跟随当前加载场景目录(否则子目录场景只有 1 张,无法推进)。</summary>
        private static string CurrentSceneDir()
        {
            string baseDir = ResolvePath(KKPEHeightLockPlugin.SceneDir.Value);
            if (string.IsNullOrEmpty(_sceneSubdir)) return baseDir;
            return System.IO.Path.Combine(baseDir, _sceneSubdir);
        }

        /// <summary>子目录显示名。</summary>
        public static string SceneSubdirLabel()
        {
            return string.IsNullOrEmpty(_sceneSubdir) ? L10n.T("根目录") : _sceneSubdir;
        }

        /// <summary>扫描场景目录（自然排序，递归含子目录）。</summary>
        public static void RefreshSceneList()
        {
            try
            {
                _sceneFiles.Clear();
                // 保留当前索引（不重置 -1），否则 ◀▶ 每次刷新后都回到头尾
                string dir = CurrentSceneDir();
                if (!System.IO.Directory.Exists(dir)) return;

                // 递归扫描，用户场景常分散在分类子目录里
                foreach (var f in System.IO.Directory.GetFiles(dir, "*.png", System.IO.SearchOption.AllDirectories))
                    _sceneFiles.Add(f);
                SortNatural(_sceneFiles);
                _scenesLoaded = true;

                // 定位当前加载的场景（按文件名），找不到就夹紧原索引
                if (!string.IsNullOrEmpty(Studio.Studio.savePath))
                {
                    string cur = System.IO.Path.GetFileNameWithoutExtension(Studio.Studio.savePath);
                    if (!string.IsNullOrEmpty(cur))
                    {
                        for (int i = 0; i < _sceneFiles.Count; i++)
                        {
                            if (System.IO.Path.GetFileNameWithoutExtension(_sceneFiles[i]) == cur)
                            {
                                _sceneIndex = i;
                                break;
                            }
                        }
                    }
                }
                if (_sceneFiles.Count > 0)
                    _sceneIndex = Mathf.Clamp(_sceneIndex, 0, _sceneFiles.Count - 1);
                KKPEHeightLockPlugin.Log.LogMessage($"ScenePlayer: found {_sceneFiles.Count} scenes in {dir}");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("ScenePlayer.RefreshSceneList error: " + e);
            }
        }

        /// <summary>自然排序(数字感知,同 sceneplayer.py 的 fns 排序)。</summary>
        private static void SortNatural(System.Collections.Generic.List<string> files)
        {
            files.Sort(delegate(string a, string b)
            {
                return NaturalSort.Compare(System.IO.Path.GetFileName(a), System.IO.Path.GetFileName(b));
            });
        }


        /// <summary>加载场景文件。</summary>
        public static void LoadScene(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;
                KKPEHeightLockPlugin.Log.LogMessage($"ScenePlayer: loading {path}");

                // 用 Studio 原生加载协程：JetPack 等插件的 VR 恢复 patch 挂在这条协程上，
                // 直接用 studio.LoadScene 会跳过，导致加载后镜头锁死、VR 手消失
                var studio = Singleton<Studio.Studio>.Instance;
                if (studio == null) return;
                var coroutine = studio.LoadSceneCoroutine(path);
                if (coroutine != null)
                {
                    KKPEHeightLockPlugin.Instance.StartCoroutine(coroutine);
                    return;
                }
                studio.LoadScene(path);
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("ScenePlayer.LoadScene error: " + e);
            }
        }

        /// <summary>加载上一场景。</summary>
        public static void LoadPrevScene()
        {
            EnsureScenes();
            if (_sceneFiles.Count == 0) return;
            _sceneIndex = (_sceneIndex - 1 + _sceneFiles.Count) % _sceneFiles.Count;
            LoadScene(_sceneFiles[_sceneIndex]);
        }

        /// <summary>加载下一场景。</summary>
        public static void LoadNextScene()
        {
            EnsureScenes();
            if (_sceneFiles.Count == 0) return;
            _sceneIndex = (_sceneIndex + 1) % _sceneFiles.Count;
            LoadScene(_sceneFiles[_sceneIndex]);
        }

        /// <summary>随机加载一个场景(排除当前,场景多时避免连续相同)。</summary>
        public static void RandomScene()
        {
            EnsureScenes();
            if (_sceneFiles.Count == 0) return;
            int idx = UnityEngine.Random.Range(0, _sceneFiles.Count);
            if (_sceneFiles.Count > 1 && idx == _sceneIndex)
                idx = (idx + 1) % _sceneFiles.Count;
            _sceneIndex = idx;
            LoadScene(_sceneFiles[_sceneIndex]);
        }

        /// <summary>删除当前场景文件(可选加载下一个)。</summary>
        public static void DeleteCurrentScene(bool loadNext)
        {
            try
            {
                if (_sceneIndex < 0 || _sceneIndex >= _sceneFiles.Count) return;
                string path = _sceneFiles[_sceneIndex];
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
                KKPEHeightLockPlugin.Log.LogMessage($"ScenePlayer: deleted {path}");
                _sceneFiles.RemoveAt(_sceneIndex);
                if (loadNext && _sceneFiles.Count > 0)
                {
                    _sceneIndex = _sceneIndex % _sceneFiles.Count;
                    LoadScene(_sceneFiles[_sceneIndex]);
                }
                else if (_sceneFiles.Count == 0)
                {
                    _sceneIndex = -1;
                }
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("ScenePlayer.DeleteCurrentScene error: " + e);
            }
        }

        private static void EnsureScenes()
        {
            if (!_scenesLoaded) RefreshSceneList();
        }
    }
}
