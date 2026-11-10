using System;
using System.Collections.Generic;
using System.IO;

namespace KKPEHeightLock
{
    /// <summary>场景卡的扫描、过滤和排序。</summary>
    public static partial class AutoExportPanel
    {
        private class SceneEntry
        {
            public string path;
            public string root;     // 所属扫描根目录（绝对路径）
            public string rel;      // 相对扫描根目录的子目录（"a/b"），根目录为 ""
            public string name;
            public long size;
            public bool check;

            // Timeline 懒加载：1500 张全解析要读十几 GB，用到才读
            public bool tlLoaded;
            public TimelineReader.Info tl;

            public float TlSeconds
            {
                get
                {
                    if (!tlLoaded) { tl = TimelineReader.Read(path); tlLoaded = true; }
                    return (tl != null) ? tl.Seconds : 0f;
                }
            }
        }

        private static readonly List<SceneEntry> _scenes = new List<SceneEntry>();
        private static readonly List<int> _filtered = new List<int>();
        private static readonly List<string> _roots = new List<string>();
        private static string _filterKey;
        private static string _search = "";
        private static bool _scanned;
        private static int _checkedCount;
        private static int _scanGen;
        private static string _scanInfo = "";

        public static void ScanScenes()
        {
            _scenes.Clear();
            _roots.Clear();
            _checkedCount = 0;
            _scanned = true;
            _scanGen++;
            _filterKey = null;
            _treeDirty = true;
            _checkDirty = true;
            _selectedIdx = -1;
            try
            {
                // SceneDir 支持多个根目录，用 ; 分隔
                string raw = PathFix.Read(KKPEHeightLockPlugin.ExportSceneDir.Value);
                var cand = new List<string>();
                foreach (var part in raw.Split(';'))
                {
                    string d = ResolveDir(part.Trim());
                    if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) continue;
                    d = Path.GetFullPath(d).TrimEnd('\\', '/');
                    bool dup = false;
                    for (int i = 0; i < cand.Count; i++)
                        if (string.Equals(cand[i], d, StringComparison.OrdinalIgnoreCase))
                        { dup = true; break; }
                    if (!dup) cand.Add(d);
                }

                // 根目录互相嵌套时只保留最外层：内层的文件本来就会被外层扫到，
                // 留着它只会让同一张卡多算出一条更浅的相对路径（目录就串层了）。
                for (int i = 0; i < cand.Count; i++)
                {
                    if (cand[i] == null) continue;
                    for (int j = 0; j < cand.Count; j++)
                    {
                        if (i == j || cand[j] == null) continue;
                        if (IsInside(cand[i], cand[j])) { cand[i] = null; break; }
                    }
                }
                for (int i = 0; i < cand.Count; i++)
                    if (cand[i] != null) _roots.Add(cand[i]);

                if (_roots.Count == 0)
                {
                    _scanInfo = L10n.T("场景目录不存在：") + raw;
                    return;
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var dir in _roots)
                {
                    foreach (var f in Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories))
                    {
                        string full = Path.GetFullPath(f);
                        if (!seen.Add(full)) continue;    // 多个根目录重叠时去重

                        var e = new SceneEntry();
                        e.path = full;
                        e.root = dir;
                        e.name = Path.GetFileNameWithoutExtension(full);
                        try { e.size = new FileInfo(full).Length; } catch (Exception) { }

                        string rel = full.Substring(dir.Length).TrimStart('\\', '/');
                        int cut = rel.LastIndexOfAny(new char[] { '\\', '/' });
                        e.rel = NormalizeRel(cut > 0 ? rel.Substring(0, cut) : "");
                        _scenes.Add(e);
                    }
                }

                // 先按目录、再按文件名自然排序
                _scenes.Sort(delegate(SceneEntry a, SceneEntry b)
                {
                    int c = string.Compare(a.rel ?? "", b.rel ?? "", StringComparison.OrdinalIgnoreCase);
                    return c != 0 ? c : CompareNatural(a.name, b.name);
                });

                _scanInfo = L10n.T("共 ") + _scenes.Count + L10n.T(" 张 / ") + _roots.Count + L10n.T(" 个目录");
            }
            catch (Exception e)
            {
                _scanInfo = L10n.T("扫描失败：") + e.Message;
            }
        }

        /// <summary>child 是否在 parent 目录里面（不含相等）。</summary>
        private static bool IsInside(string child, string parent)
        {
            string c = child.Replace('/', '\\').TrimEnd('\\');
            string p = parent.Replace('/', '\\').TrimEnd('\\');
            return c.Length > p.Length
                && c.StartsWith(p, StringComparison.OrdinalIgnoreCase)
                && c[p.Length] == '\\';
        }

        /// <summary>相对路径统一成 "a/b/c"：反斜杠换正斜杠、去掉空段和首尾分隔符。</summary>
        private static string NormalizeRel(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return "";
            string[] seg = rel.Replace('\\', '/').Split('/');
            var keep = new List<string>(seg.Length);
            for (int i = 0; i < seg.Length; i++)
            {
                string s = seg[i].Trim();
                if (s.Length > 0) keep.Add(s);
            }
            return string.Join("/", keep.ToArray());
        }

        private static string RootName(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            string s = p.Replace('/', '\\').TrimEnd('\\');
            int cut = s.LastIndexOf('\\');
            return cut >= 0 ? s.Substring(cut + 1) : s;
        }

        private static string ResolveDir(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            try
            {
                return Path.IsPathRooted(p) ? p : Path.Combine(BepInEx.Paths.GameRootPath, p);
            }
            catch (Exception) { return p; }
        }

        /// <summary>自然排序："场景10" 排在 "场景9" 之后。</summary>
        private static int CompareNatural(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    long va = 0, vb = 0;
                    try
                    {
                        va = long.Parse(a.Substring(si, i - si));
                        vb = long.Parse(b.Substring(sj, j - sj));
                    }
                    catch (Exception) { }
                    if (va != vb) return va < vb ? -1 : 1;
                }
                else
                {
                    if (a[i] != b[j]) return a[i] < b[j] ? -1 : 1;
                    i++; j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }

        /// <summary>搜索过滤结果（每帧重算一千多条太浪费，缓存）。</summary>
        private static List<int> Filtered()
        {
            string key = (_search ?? "") + "\u0001" + _scanGen;
            if (_filterKey == key) return _filtered;
            _filterKey = key;
            _filtered.Clear();
            for (int i = 0; i < _scenes.Count; i++)
            {
                if (string.IsNullOrEmpty(_search)
                    || _scenes[i].name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                    || (_scenes[i].rel ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                    _filtered.Add(i);
            }
            return _filtered;
        }

        private static void SetAll(bool on)
        {
            _checkedCount = 0;
            for (int i = 0; i < _scenes.Count; i++)
            {
                _scenes[i].check = on;
                if (on) _checkedCount++;
            }
            _checkDirty = true;
        }

        private static void InvertAll()
        {
            _checkedCount = 0;
            for (int i = 0; i < _scenes.Count; i++)
            {
                _scenes[i].check = !_scenes[i].check;
                if (_scenes[i].check) _checkedCount++;
            }
            _checkDirty = true;
        }
    }
}
