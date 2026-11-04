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
    /// 场景播放器(复刻 sceneplayer.py 核心功能):
    /// - 场景浏览/上一下一场景/加载/信息/删除(自然排序)
    /// - 随机替换角色(女/男/全部,保留身高)
    /// - 角色快速编辑(服装/Kinematic/身高/位置)
    /// - Timeline 控制(播放/暂停/停止/进度/时间缩放)
    /// - FSS 文件夹子场景切换
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
        private static bool _fssMode;

        // 当前编辑角色
        private static Studio.OCIChar _editChar;

        // 时间缩放
        private static float _timeScale = 1f;

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

        /// <summary>列出当前场景目录的直接子文件夹。</summary>
        public static System.Collections.Generic.List<string> ListSubdirs()
        {
            var result = new System.Collections.Generic.List<string>();
            try
            {
                string dir = CurrentSceneDir();
                if (!System.IO.Directory.Exists(dir)) return result;
                foreach (var d in System.IO.Directory.GetDirectories(dir))
                    result.Add(System.IO.Path.GetFileName(d));
                result.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception) { }
            return result;
        }

        /// <summary>进入子目录(多级:追加到当前子目录路径,支持子目录套子目录)。</summary>
        public static void SetSceneSubdir(string subdir)
        {
            if (string.IsNullOrEmpty(subdir)) return;
            _sceneSubdir = string.IsNullOrEmpty(_sceneSubdir) ? subdir : _sceneSubdir + "/" + subdir;
            RefreshSceneList();
        }

        /// <summary>返回上级目录。</summary>
        public static void GoParentDir()
        {
            try
            {
                if (string.IsNullOrEmpty(_sceneSubdir))
                {
                    // 已在根:跟随当前场景目录,回到配置根
                    return;
                }
                int idx = _sceneSubdir.LastIndexOf('\\');
                if (idx < 0) idx = _sceneSubdir.LastIndexOf('/');
                _sceneSubdir = idx >= 0 ? _sceneSubdir.Substring(0, idx) : "";
                RefreshSceneList();
            }
            catch (Exception) { }
        }

        /// <summary>子目录显示名。</summary>
        public static string SceneSubdirLabel()
        {
            return string.IsNullOrEmpty(_sceneSubdir) ? L10n.T("根目录") : _sceneSubdir;
        }

        /// <summary>扫描场景目录(自然排序),目录跟随当前加载场景所在目录;无则用配置 SceneDir。</summary>
        public static void RefreshSceneList()
        {
            try
            {
                _sceneFiles.Clear();
                // 保留当前索引(不重置 -1),否则 ◀▶ 每次刷新后都回到头尾两张,无法推进
                string dir = CurrentSceneDir();
                if (!System.IO.Directory.Exists(dir)) return;

                // 递归扫描所有子目录的场景卡(用户场景常分散在分类子目录,只扫根目录会漏)
                foreach (var f in System.IO.Directory.GetFiles(dir, "*.png", System.IO.SearchOption.AllDirectories))
                    _sceneFiles.Add(f);
                NaturalSort(_sceneFiles);
                _scenesLoaded = true;

                // 定位当前加载的场景(用 savePath 文件名);找不到则保留原索引(夹紧到合法范围)
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
        private static void NaturalSort(System.Collections.Generic.List<string> files)
        {
            files.Sort(delegate(string a, string b)
            {
                return CompareNatural(System.IO.Path.GetFileName(a), System.IO.Path.GetFileName(b));
            });
        }

        private static int CompareNatural(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                char ca = a[i], cb = b[j];
                if (char.IsDigit(ca) && char.IsDigit(cb))
                {
                    int na = 0, nb = 0;
                    while (i < a.Length && char.IsDigit(a[i])) { na = na * 10 + (a[i] - '0'); i++; }
                    while (j < b.Length && char.IsDigit(b[j])) { nb = nb * 10 + (b[j] - '0'); j++; }
                    if (na != nb) return na.CompareTo(nb);
                }
                else
                {
                    if (ca != cb) return ca.CompareTo(cb);
                    i++; j++;
                }
            }
            return a.Length.CompareTo(b.Length);
        }

        /// <summary>加载场景文件。</summary>
        public static void LoadScene(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;
                KKPEHeightLockPlugin.Log.LogMessage($"ScenePlayer: loading {path}");

                // 用 Studio 原生"加载场景协程"(Studio.Studio.LoadSceneCoroutine,public),
                // 与 UI 加载走同一条流程,且 JetPack 等插件的 VR 恢复 patch 挂在此协程上,
                // 加载完成后会自动恢复 VR 相机/手柄(直接用 studio.LoadScene 会跳过,
                // 导致加载后镜头锁死、Ermin VR 手消失)
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
