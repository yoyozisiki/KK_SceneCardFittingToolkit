using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>
    /// 角色卡目录树的一个节点（后端模型）。文件夹和卡片共用这个结构，
    /// 界面只要照着 <see cref="folder"/> 分两种行来画。
    /// </summary>
    public sealed class CardNode
    {
        public bool folder;
        public string name;      // 显示名：文件夹名 / 文件名（不带扩展名）
        public string path;      // 绝对路径（文件夹或卡片）
        public string rel;       // 相对扫描根的子目录（"a/b"，根为 ""）
        public int depth;
        public int total;        // 子树里的卡片总数（文件夹行右侧显示）

        public readonly List<CardNode> kids = new List<CardNode>();    // 子文件夹
        public readonly List<CardNode> cards = new List<CardNode>();   // 直属卡片

        // 卡片专属，懒加载
        public string roleName;   // 角色名（读卡里的 fullname）
        public bool metaTried;
    }

    /// <summary>
    /// 角色卡目录的扫描与懒加载（后端）。
    ///
    /// 以前选卡弹框只列「当前目录的直接子文件夹 + 直接卡片」，子文件夹要靠另一个
    /// 「选择子文件夹」弹框去钻 —— 那个弹框实际用起来进不去，而且一次只能看一层。
    /// 现在照场景卡列表的做法：整棵目录树一次扫出来，界面按行画「文件夹 / 卡片」，
    /// 文件夹行自己带展开箭头，点一下就在原地展开。
    /// </summary>
    public static class CardLibrary
    {
        private const string FemaleRoot = "chara/female";
        private const string MaleRoot = "chara/male";

        private static CardNode _root;
        private static bool _female = true;
        private static bool _scanned;
        private static int _version;
        private static int _cardCount;

        // 懒加载游标：每帧从上次停下的地方继续，避免一帧读几百张卡
        private static readonly List<CardNode> _lazy = new List<CardNode>();
        private static int _lazyCursor;
        private static int _lazyFrame = -1;    // OnGUI 一帧会进来好几次，别每次都补一批

        /// <summary>当前扫的是不是女卡（男卡池只用来给「替换男角色」用）。</summary>
        public static bool IsFemale { get { return _female; } }

        public static CardNode Root { get { return _root; } }

        public static int CardCount { get { return _cardCount; } }

        /// <summary>每次重扫 +1，界面用它判断要不要重建行。</summary>
        public static int Version { get { return _version; } }

        /// <summary>
        /// 角色卡根目录（**规范化后的绝对路径**）。
        ///
        /// 必须 GetFullPath：KKS 里 <c>UserData.Path</c> 长这样
        /// <c>.../CharaStudio_Data/../UserData</c>（带 ..），而 Directory.GetFiles 返回的是
        /// 规范化过的 <c>.../UserData/...</c>。两个串长度不一样，直接拿 root.Length 去
        /// Substring 就会算错 —— 表现为「卡只扫进来一半，剩下的全报 startIndex 越界」。
        /// </summary>
        public static string RootPath(bool female)
        {
            string p = Path.Combine(UserData.Path, female ? FemaleRoot : MaleRoot);
            try { return Path.GetFullPath(p); }
            catch (Exception) { return p; }
        }

        public static void Ensure(bool female)
        {
            if (_scanned && _female == female) return;
            Rescan(female);
        }

        /// <summary>重新扫描整个角色卡目录（递归）。thumb 纹理会先销毁，别在持有它的地方调。</summary>
        public static void Rescan(bool female)
        {
            _lazy.Clear();
            _lazyCursor = 0;

            _female = female;
            _scanned = true;
            _version++;
            _cardCount = 0;

            _root = new CardNode();
            _root.folder = true;
            _root.name = female ? L10n.T("女角色卡") : L10n.T("男角色卡");
            _root.path = RootPath(female);
            _root.rel = "";
            _root.depth = 0;

            try
            {
                if (!Directory.Exists(_root.path))
                {
                    KKPEHeightLockPlugin.Log.LogWarning("[角色卡库] 目录不存在：" + _root.path);
                    return;
                }

                var folders = new Dictionary<string, CardNode>(StringComparer.OrdinalIgnoreCase);
                folders[""] = _root;

                var files = Directory.GetFiles(_root.path, "*.png", SearchOption.AllDirectories);
                int skipped = 0;
                for (int i = 0; i < files.Length; i++)
                {
                    // 单张卡出问题（路径怪、文件被独占）不能让整次扫描中断 ——
                    // 之前这里一抛就只剩前面几十张，界面表现为「卡突然少了一大半」。
                    try
                    {
                        string full = Path.GetFullPath(files[i]);
                        string rel = RelativeTo(_root.path, full);
                        int cut = rel.LastIndexOf('/');
                        string dir = SceneCatalog.NormalizeRel(cut > 0 ? rel.Substring(0, cut) : "");

                        CardNode parent = Folder(folders, dir);
                        var card = new CardNode();
                        card.folder = false;
                        card.path = full;
                        card.rel = dir;
                        card.depth = parent.depth + 1;
                        card.name = Path.GetFileNameWithoutExtension(full);
                        parent.cards.Add(card);
                        _cardCount++;
                        _lazy.Add(card);
                    }
                    catch (Exception e)
                    {
                        skipped++;
                        if (skipped <= 3)
                            KKPEHeightLockPlugin.Log.LogWarning("[角色卡库] 跳过一张卡：" + files[i] + " —— " + e.Message);
                    }
                }
                if (skipped > 0)
                    KKPEHeightLockPlugin.Log.LogWarning("[角色卡库] 共跳过 " + skipped + " 张卡（其余已正常载入）");

                SortTree(_root);
                Count(_root);
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("[角色卡库] 扫描失败：" + e.Message);
            }

            KKPEHeightLockPlugin.Log.LogMessage($"[角色卡库] {(female ? "女" : "男")}卡：{_cardCount} 张");
        }


        /// <summary>
        /// full 相对 root 的相对路径（统一 "/"）。分隔符混用、大小写不同都能对上；
        /// 对不上（比如软链接指到别处）就退回文件名，绝不抛异常。
        /// </summary>
        private static string RelativeTo(string root, string full)
        {
            try { root = Path.GetFullPath(root ?? ""); } catch (Exception) { }
            try { full = Path.GetFullPath(full ?? ""); } catch (Exception) { }
            string r = (root ?? "").Replace('\\', '/').TrimEnd('/');
            string f = (full ?? "").Replace('\\', '/');
            if (r.Length > 0 && f.Length > r.Length && f.StartsWith(r, StringComparison.OrdinalIgnoreCase))
                return f.Substring(r.Length).TrimStart('/');
            try { return Path.GetFileName(f); }
            catch (Exception) { return ""; }
        }

        /// <summary>取（没有就建）rel 这条路径上的文件夹节点。</summary>
        private static CardNode Folder(Dictionary<string, CardNode> map, string rel)
        {
            CardNode node;
            if (map.TryGetValue(rel, out node)) return node;

            int cut = rel.LastIndexOf('/');
            string parentRel = cut >= 0 ? rel.Substring(0, cut) : "";
            string name = cut >= 0 ? rel.Substring(cut + 1) : rel;

            CardNode parent = Folder(map, parentRel);
            node = new CardNode();
            node.folder = true;
            node.name = name;
            node.rel = rel;
            node.path = Path.Combine(_root.path, rel.Replace('/', '\\'));
            node.depth = parent.depth + 1;
            parent.kids.Add(node);
            map[rel] = node;
            return node;
        }

        private static void SortTree(CardNode n)
        {
            n.kids.Sort(delegate(CardNode a, CardNode b)
            {
                return NaturalSort.Compare(a.name, b.name);
            });
            n.cards.Sort(delegate(CardNode a, CardNode b)
            {
                return NaturalSort.Compare(a.name, b.name);
            });
            for (int i = 0; i < n.kids.Count; i++) SortTree(n.kids[i]);
        }

        private static int Count(CardNode n)
        {
            int total = n.cards.Count;
            for (int i = 0; i < n.kids.Count; i++) total += Count(n.kids[i]);
            n.total = total;
            return total;
        }

        /// <summary>按关键词搜卡（拍平，忽略层级）。</summary>
        public static void Search(string query, List<CardNode> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(query) || _root == null) return;
            SearchNode(_root, query, into);
        }

        private static void SearchNode(CardNode n, string query, List<CardNode> into)
        {
            for (int i = 0; i < n.cards.Count; i++)
            {
                var c = n.cards[i];
                if (c.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || (c.rel ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || (!string.IsNullOrEmpty(c.roleName) && c.roleName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                    into.Add(c);
            }
            for (int i = 0; i < n.kids.Count; i++) SearchNode(n.kids[i], query, into);
        }

        /// <summary>某个文件夹（含子目录）里的全部卡片路径 —— 「整目录加入」用。</summary>
        public static List<string> AllCards(CardNode folder)
        {
            var list = new List<string>();
            CollectCards(folder, list);
            return list;
        }

        private static void CollectCards(CardNode n, List<string> into)
        {
            if (n == null) return;
            for (int i = 0; i < n.cards.Count; i++) into.Add(n.cards[i].path);
            for (int i = 0; i < n.kids.Count; i++) CollectCards(n.kids[i], into);
        }

        /// <summary>
        /// 每帧最多处理 max 张卡的元数据 / 缩略图（先读角色名，再解缩略图）。
        /// 返回 true 表示还有没处理完的（界面可以继续等下一帧）。
        /// </summary>
        public static bool TickLazy(int max)
        {
            // OnGUI 每帧会走 Layout / Repaint / 各个输入事件，这里只认每帧第一次
            if (_lazyFrame == Time.frameCount) return _lazyCursor < _lazy.Count;
            _lazyFrame = Time.frameCount;

            int done = 0;
            while (_lazyCursor < _lazy.Count && done < max)
            {
                var c = _lazy[_lazyCursor];
                if (c == null) { _lazyCursor++; continue; }

                if (!c.metaTried)
                {
                    c.roleName = ReadRoleName(c.path);
                    c.metaTried = true;
                    done++;
                }
                _lazyCursor++;    // 名字读完就过，缩略图走 ThumbCache 单独限流
            }
            return _lazyCursor < _lazy.Count;
        }

        /// <summary>读角色卡里的角色名（读不出来就退回文件名）。</summary>
        private static string ReadRoleName(string path)
        {
            try
            {
                var chaFile = new ChaFileControl();
                if (chaFile.LoadCharaFile(path, 255, true, true)
                    && chaFile.parameter != null && !string.IsNullOrEmpty(chaFile.parameter.fullname))
                    return chaFile.parameter.fullname;
            }
            catch (Exception) { }
            try { return Path.GetFileNameWithoutExtension(path); }
            catch (Exception) { return "?"; }
        }

        /// <summary>卡片行的显示名：优先角色名，其次文件名。</summary>
        public static string Display(CardNode card)
        {
            if (card == null) return "";
            if (!string.IsNullOrEmpty(card.roleName)) return card.roleName;
            return card.name;
        }
    }
}
