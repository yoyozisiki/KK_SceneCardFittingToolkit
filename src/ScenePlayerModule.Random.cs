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
        // ============ 随机替换角色 ============

        private static string _rndFemaleSubdir = "";
        private static string _rndMaleSubdir = "";

        /// <summary>设置随机女卡目录子目录(多级追加;空串=回到根)。</summary>
        public static void SetRandomFemaleSubdir(string subdir)
        {
            if (string.IsNullOrEmpty(subdir)) { _rndFemaleSubdir = ""; return; }
            _rndFemaleSubdir = string.IsNullOrEmpty(_rndFemaleSubdir) ? subdir : _rndFemaleSubdir + "/" + subdir;
        }

        /// <summary>设置随机男卡目录子目录(多级追加;空串=回到根)。</summary>
        public static void SetRandomMaleSubdir(string subdir)
        {
            if (string.IsNullOrEmpty(subdir)) { _rndMaleSubdir = ""; return; }
            _rndMaleSubdir = string.IsNullOrEmpty(_rndMaleSubdir) ? subdir : _rndMaleSubdir + "/" + subdir;
        }

        /// <summary>随机女卡子目录显示名。</summary>
        public static string RandomFemaleSubdirLabel() { return string.IsNullOrEmpty(_rndFemaleSubdir) ? L10n.T("女卡根") : _rndFemaleSubdir; }

        /// <summary>随机男卡子目录显示名。</summary>
        public static string RandomMaleSubdirLabel() { return string.IsNullOrEmpty(_rndMaleSubdir) ? L10n.T("男卡根") : _rndMaleSubdir; }

        /// <summary>列出随机女卡目录的直接子文件夹。</summary>
        public static System.Collections.Generic.List<string> ListRandomFemaleSubdirs()
        {
            return ListSubdirsOf(string.IsNullOrEmpty(KKPEHeightLockPlugin.RandomFemaleDir.Value) ? "UserData/chara/female" : KKPEHeightLockPlugin.RandomFemaleDir.Value, _rndFemaleSubdir);
        }

        /// <summary>列出随机男卡目录的直接子文件夹。</summary>
        public static System.Collections.Generic.List<string> ListRandomMaleSubdirs()
        {
            return ListSubdirsOf(string.IsNullOrEmpty(KKPEHeightLockPlugin.RandomMaleDir.Value) ? "UserData/chara/male" : KKPEHeightLockPlugin.RandomMaleDir.Value, _rndMaleSubdir);
        }

        private static System.Collections.Generic.List<string> ListSubdirsOf(string baseDir, string subdir)
        {
            var result = new System.Collections.Generic.List<string>();
            try
            {
                string dir = ResolvePath(baseDir);
                if (!string.IsNullOrEmpty(subdir))
                    dir = System.IO.Path.Combine(dir, subdir);
                if (!System.IO.Directory.Exists(dir)) return result;
                foreach (var d in System.IO.Directory.GetDirectories(dir))
                    result.Add(System.IO.Path.GetFileName(d));
                result.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception) { }
            return result;
        }

        /// <summary>列出指定模式(0=场景 1=随机女 2=随机男 3=选卡)指定路径的直接子文件夹。路径用 "/" 分隔。</summary>
        public static System.Collections.Generic.List<string> ListSubdirsAt(int mode, string path)
        {
            switch (mode)
            {
                case 0: return ListSubdirsOf(KKPEHeightLockPlugin.SceneDir.Value, path);
                case 1: return ListSubdirsOf(string.IsNullOrEmpty(KKPEHeightLockPlugin.RandomFemaleDir.Value) ? "UserData/chara/female" : KKPEHeightLockPlugin.RandomFemaleDir.Value, path);
                case 2: return ListSubdirsOf(string.IsNullOrEmpty(KKPEHeightLockPlugin.RandomMaleDir.Value) ? "UserData/chara/male" : KKPEHeightLockPlugin.RandomMaleDir.Value, path);
                case 3:
                {
                    // 选卡目录:当前选卡窗口的根目录(UserData/chara/female 或 male)
                    string baseDir = System.IO.Path.Combine(UserData.Path, ConfigPanel.CardPickerBaseDir());
                    return ListSubdirsOf(baseDir, path);
                }
                default: return new System.Collections.Generic.List<string>();
            }
        }

        /// <summary>应用选择的子目录(0=场景 1=随机女 2=随机男 3=选卡),直接设为完整相对路径。</summary>
        public static void ApplySubdir(int mode, string path)
        {
            switch (mode)
            {
                case 0: _sceneSubdir = path ?? ""; RefreshSceneList(); break;
                case 1: _rndFemaleSubdir = path ?? ""; break;
                case 2: _rndMaleSubdir = path ?? ""; break;
                case 3: ConfigPanel.SetCardPickerDir(path ?? ""); break;
            }
        }

        /// <summary>随机替换所有角色(按性别)。</summary>
        public static void RandomReplaceAll()
        {
            RandomReplaceBySex(true, true);
        }

        /// <summary>随机替换女角色。</summary>
        public static void RandomReplaceFemale()
        {
            RandomReplaceBySex(true, false);
        }

        /// <summary>随机替换男角色。</summary>
        public static void RandomReplaceMale()
        {
            RandomReplaceBySex(false, true);
        }

        private static void RandomReplaceBySex(bool female, bool male)
        {
            try
            {
                var studio = Singleton<Studio.Studio>.Instance;
                if (studio == null || studio.dicObjectCtrl == null) return;

                int replaced = 0;
                foreach (var kv in studio.dicObjectCtrl)
                {
                    var oci = kv.Value as Studio.OCIChar;
                    if (oci == null || oci.charInfo == null) continue;
                    int sex = 0;
                    try { sex = oci.sex; } catch (Exception) { continue; }
                    if (!female && sex == 1) continue;
                    if (!male && sex == 0) continue;

                    string dir = sex == 1
                        ? (string.IsNullOrEmpty(KKPEHeightLockPlugin.RandomFemaleDir.Value) ? "UserData/chara/female" : KKPEHeightLockPlugin.RandomFemaleDir.Value)
                        : (string.IsNullOrEmpty(KKPEHeightLockPlugin.RandomMaleDir.Value) ? "UserData/chara/male" : KKPEHeightLockPlugin.RandomMaleDir.Value);
                    // 应用随机目录子目录
                    string rndSub = sex == 1 ? _rndFemaleSubdir : _rndMaleSubdir;
                    if (!string.IsNullOrEmpty(rndSub))
                        dir = System.IO.Path.Combine(dir, rndSub);
                    string card = PickRandomCard(dir);
                    if (card == null) continue;

                    // 保留身高
                    float origHeight = 0f;
                    if (KKPEHeightLockPlugin.RetainHeightOnRandom.Value)
                    {
                        try { origHeight = oci.charInfo.chaFile.custom.body.shapeValueBody[0]; } catch (Exception) { }
                    }

                    oci.ChangeChara(card);

                    if (KKPEHeightLockPlugin.RetainHeightOnRandom.Value && origHeight > 0f)
                    {
                        try { oci.charInfo.chaFile.custom.body.shapeValueBody[0] = origHeight; } catch (Exception) { }
                    }
                    replaced++;
                }
                KKPEHeightLockPlugin.Log.LogMessage($"ScenePlayer: randomly replaced {replaced} character(s)");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("ScenePlayer.RandomReplaceBySex error: " + e);
            }
        }

        private static string PickRandomCard(string dir)
        {
            try
            {
                string path = ResolvePath(dir);
                if (!System.IO.Directory.Exists(path)) return null;
                var cards = System.IO.Directory.GetFiles(path, "*.png");
                if (cards.Length == 0) return null;
                var rng = new System.Random();
                return cards[rng.Next(cards.Length)];
            }
            catch (Exception) { return null; }
        }
    }
}
