using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>
    /// 一张场景卡的元数据（后端模型，界面只读）。
    /// </summary>
    public sealed class SceneEntry
    {
        public string path;     // 绝对路径
        public string root;     // 所属扫描根目录（绝对路径）
        public string rel;      // 相对根目录的子目录（"a/b"），根目录为 ""
        public string name;     // 文件名（不带扩展名）
        public long size;
        public bool check;      // 是否勾选（只能通过 SceneCatalog.SetCheck 改）

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

    /// <summary>
    /// 场景卡目录的扫描、过滤、勾选（后端）。
    ///
    /// 界面（SceneTree / PanelWindow）只读 <see cref="Items"/> 和 <see cref="Filter"/> 的结果，
    /// 不再自己持有列表 —— 以前扫描、勾选计数、过滤缓存全塞在 UI 的 partial 里，
    /// 界面一改就得连带改数据，没法单独测。
    /// </summary>
    public static class SceneCatalog
    {
        private static readonly List<SceneEntry> _items = new List<SceneEntry>();
        private static readonly List<string> _roots = new List<string>();
        private static readonly List<int> _filtered = new List<int>();
        private static string _info = "";
        private static string _filterKey;
        private static int _checkedCount;
        private static int _version;

        /// <summary>扫描结果（按「目录 → 文件名自然排序」排好）。只读用，别改。</summary>
        public static List<SceneEntry> Items { get { return _items; } }

        /// <summary>状态行文案（「共 N 张 / M 个目录」或错误信息）。</summary>
        public static string Info { get { return _info; } }

        /// <summary>是否已经扫过一次（界面用它决定要不要先 Scan）。</summary>
        public static bool Scanned { get; private set; }

        /// <summary>每扫一次 +1。界面用它判断树要不要重建。</summary>
        public static int Version { get { return _version; } }

        public static int Count { get { return _items.Count; } }

        public static int CheckedCount { get { return _checkedCount; } }

        public static void Ensure()
        {
            if (!Scanned) Scan();
        }

        /// <summary>重新扫描配置里的所有场景目录（递归）。</summary>
        public static void Scan()
        {
            _items.Clear();
            _roots.Clear();
            _checkedCount = 0;
            Scanned = true;
            _version++;
            _filterKey = null;

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
                    _info = L10n.T("场景目录不存在：") + raw;
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

                        // 相对路径别直接用 Substring(dir.Length)：分隔符混用/软链接时
                        // 会算出负数长度直接抛异常，把整次扫描带崩。
                        string rel = RelativeTo(dir, full);
                        int cut = rel.LastIndexOf('/');
                        e.rel = NormalizeRel(cut > 0 ? rel.Substring(0, cut) : "");
                        _items.Add(e);
                    }
                }

                // 先按目录、再按文件名自然排序
                _items.Sort(delegate(SceneEntry a, SceneEntry b)
                {
                    int c = string.Compare(a.rel ?? "", b.rel ?? "", StringComparison.OrdinalIgnoreCase);
                    return c != 0 ? c : NaturalSort.Compare(a.name, b.name);
                });

                _info = L10n.T("共 ") + _items.Count + L10n.T(" 张 / ") + _roots.Count + L10n.T(" 个目录");
            }
            catch (Exception e)
            {
                _info = L10n.T("扫描失败：") + e.Message;
            }
        }

        public static void SetAll(bool on)
        {
            _checkedCount = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].check = on;
                if (on) _checkedCount++;
            }
        }

        public static void InvertAll()
        {
            _checkedCount = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].check = !_items[i].check;
                if (_items[i].check) _checkedCount++;
            }
        }

        /// <summary>改一张卡的勾选状态，顺手维护已选计数。</summary>
        public static void SetCheck(SceneEntry e, bool on)
        {
            if (e == null || e.check == on) return;
            e.check = on;
            _checkedCount += on ? 1 : -1;
        }

        /// <summary>
        /// 搜索过滤结果（每帧重算一千多条太浪费，按「搜索词 + 扫描版本」缓存）。
        /// 返回的是内部下标列表，别改。
        /// </summary>
        public static List<int> Filter(string query)
        {
            string key = (query ?? "") + "\u0001" + _version;
            if (_filterKey == key) return _filtered;
            _filterKey = key;
            _filtered.Clear();
            for (int i = 0; i < _items.Count; i++)
            {
                if (string.IsNullOrEmpty(query)
                    || _items[i].name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || (_items[i].rel ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    _filtered.Add(i);
            }
            return _filtered;
        }

        /// <summary>当前勾选的卡（导出用，顺序就是列表顺序）。</summary>
        public static List<string> CheckedPaths()
        {
            var list = new List<string>();
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].check) list.Add(_items[i].path);
            return list;
        }


        // ---------------------------------------------------------------- 目录视图
        // 大图标模式要按「当前目录」取直属子目录 / 直属卡片。

        /// <summary>某个目录下的直属子目录名（按自然排序，来自已排序的 Items，顺序稳定）。</summary>
        public static void FoldersIn(string rel, List<string> into)
        {
            into.Clear();
            string prefix = string.IsNullOrEmpty(rel) ? "" : rel.TrimEnd('/') + "/";
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _items.Count; i++)
            {
                string r = _items[i].rel ?? "";
                if (r.Length <= prefix.Length) continue;
                if (!r.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                int cut = r.IndexOf('/', prefix.Length);
                string seg = cut >= 0 ? r.Substring(prefix.Length, cut - prefix.Length)
                                      : r.Substring(prefix.Length);
                if (seg.Length == 0) continue;
                if (seen.Add(seg)) into.Add(seg);
            }
        }

        /// <summary>某个目录下的直属卡片。</summary>
        public static void CardsIn(string rel, List<SceneEntry> into)
        {
            into.Clear();
            string key = rel ?? "";
            for (int i = 0; i < _items.Count; i++)
            {
                if (string.Equals(_items[i].rel ?? "", key, StringComparison.OrdinalIgnoreCase))
                    into.Add(_items[i]);
            }
        }

        /// <summary>搜索匹配的卡（拍平，忽略目录层级）。</summary>
        public static void Matching(string query, List<SceneEntry> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(query)) return;
            var idx = Filter(query);
            for (int i = 0; i < idx.Count; i++) into.Add(_items[idx[i]]);
        }

        /// <summary>某个目录（含子目录）的勾选统计，给文件夹图标上的方框用。</summary>
        public static void FolderStat(string rel, out int total, out int on)
        {
            total = 0; on = 0;
            string prefix = string.IsNullOrEmpty(rel) ? "" : rel.TrimEnd('/') + "/";
            for (int i = 0; i < _items.Count; i++)
            {
                string r = _items[i].rel ?? "";
                bool inside = string.IsNullOrEmpty(rel)
                    ? true
                    : (string.Equals(r, rel, StringComparison.OrdinalIgnoreCase)
                       || r.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                if (!inside) continue;
                total++;
                if (_items[i].check) on++;
            }
        }

        /// <summary>按路径找一张卡（恢复上次选中用）。</summary>
        public static SceneEntry Find(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            for (int i = 0; i < _items.Count; i++)
                if (string.Equals(_items[i].path, path, StringComparison.OrdinalIgnoreCase))
                    return _items[i];
            return null;
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

        /// <summary>full 相对 root 的路径（统一 "/"）；对不上就退回文件名，不抛异常。</summary>
        private static string RelativeTo(string root, string full)
        {
            try { root = Path.GetFullPath(root ?? ""); } catch (Exception) { }
            try { full = Path.GetFullPath(full ?? ""); } catch (Exception) { }
            string r = (root ?? "").Replace('\\', '/').TrimEnd('/');
            string f = (full ?? "").Replace('\\', '/');
            if (r.Length > 0 && f.Length > r.Length && f.StartsWith(r, StringComparison.OrdinalIgnoreCase))
                return f.Substring(r.Length).TrimStart('/');
            try { return Path.GetFileName(f); } catch (Exception) { return ""; }
        }

        /// <summary>相对路径统一成 "a/b/c"：反斜杠换正斜杠、去掉空段和首尾分隔符。</summary>
        public static string NormalizeRel(string rel)
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

        /// <summary>配置里的相对路径 → 绝对路径（相对游戏根目录）。</summary>
        public static string ResolveDir(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            try
            {
                return Path.IsPathRooted(p) ? p : Path.Combine(BepInEx.Paths.GameRootPath, p);
            }
            catch (Exception) { return p; }
        }
    }
}
