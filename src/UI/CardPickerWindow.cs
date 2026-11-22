using System;
using System.Collections.Generic;
using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>
    /// 选卡弹框（前端）：大图标 + 文件夹，和 Windows 资源管理器一个用法。
    ///
    /// 一个目录一屏：先摆子文件夹（图标 + 名字 + 卡片数），再摆这个目录里的卡
    /// （缩略图 + 角色名 + 文件名）。点文件夹进去、点「← 上级」回来，顶部显示当前路径。
    /// 数据全部来自后端的 <see cref="CardLibrary"/> / <see cref="CardPool"/>。
    ///
    /// 多选（次角色卡）时：点卡片 = 加入/移出，左上角数字 = 替换顺序；
    /// 文件夹图标右上角的「+N」= 整目录加入。
    /// </summary>
    public static partial class ConfigPanel
    {
        private static CardNode _cardDir;
        private static Vector2 _cardScroll;
        private static string _cardSearch = "";
        private static float _cardGridH = 380f;
        private static readonly List<CardNode> _cardView = new List<CardNode>();
        private static string _cardViewKey;
        private static int _cardDirVersion = -1;   // 重扫后 Root 是新对象，旧 _cardDir 要丢掉

        private static void DrawCardPicker(int id)
        {
            var stBtn = AutoExportPanel.ThemeButton;
            var stBtnSm = AutoExportPanel.ThemeButtonSm;
            var stLabel = AutoExportPanel.ThemeLabel;
            var stLabelSm = AutoExportPanel.ThemeLabelSm;
            var stBold = AutoExportPanel.ThemeLabelBold;
            var stField = AutoExportPanel.ThemeField;

            bool multi = (_cardPickTarget == 2);

            // 后端：确保扫过 + 每帧补几张角色名（缩略图由 ThumbCache 另算）
            CardLibrary.Ensure(_cardPickerIsFemale);
            CardLibrary.TickLazy(6);
            if (_cardDir == null || _cardDirVersion != CardLibrary.Version)
            {
                _cardDir = CardLibrary.Root;
                _cardDirVersion = CardLibrary.Version;
                _cardViewKey = null;
            }

            // 右上角关闭
            if (GUI.Button(new Rect(_cardPickerRect.width - 30f, 4f, 24f, 22f), "×", stBtnSm))
                _cardPickerVisible = false;

            RefreshCardView();

            GUILayout.BeginVertical();

            // ---- 工具条：上级 / 根目录 / 刷新 / 搜索
            GUILayout.BeginHorizontal();
            bool atRoot = _cardDir == null || _cardDir == CardLibrary.Root
                          || !string.IsNullOrEmpty(_cardSearch);
            bool oldEnabled = GUI.enabled;
            GUI.enabled = !atRoot;
            if (GUILayout.Button(L10n.T("← 上级"), stBtn, GUILayout.Width(68), GUILayout.Height(26)))
                GoParentCardDir();
            GUI.enabled = oldEnabled;

            if (GUILayout.Button(L10n.T("根目录"), stBtn, GUILayout.Width(62), GUILayout.Height(26)))
            {
                _cardDir = CardLibrary.Root;
                _cardSearch = "";
                _cardViewKey = null;
            }
            if (GUILayout.Button(L10n.T("刷新"), stBtn, GUILayout.Width(60), GUILayout.Height(26)))
            {
                ThumbCache.Clear();
                CardLibrary.Rescan(_cardPickerIsFemale);
                _cardDir = CardLibrary.Root;
                _cardViewKey = null;
            }
            GUILayout.Label(L10n.T("搜索"), stLabel, GUILayout.Width(38));
            string q = GUILayout.TextField(_cardSearch ?? "", stField, GUILayout.Height(26));
            if (!string.Equals(q, _cardSearch, StringComparison.Ordinal))
            {
                _cardSearch = q;
                _cardViewKey = null;
            }
            GUILayout.EndHorizontal();

            // ---- 路径 + 统计
            string where = !string.IsNullOrEmpty(_cardSearch)
                ? L10n.T("搜索结果：") + _cardSearch
                : (_cardDir == null || _cardDir == CardLibrary.Root ? L10n.T("根目录") : _cardDir.rel);
            GUILayout.Label(" " + where + "  (" + _cardView.Count + L10n.T(" 项)")
                + (multi ? "    " + L10n.T("点卡片加入/移出（左上角数字=替换顺序）；Ctrl+左键文件夹 = 整目录加入") : ""), stLabelSm);

            // ---- 大图标网格
            _cardGridH = Mathf.Max(200f, _cardPickerRect.height - (multi ? 152f : 130f));
            var gridRect = GUILayoutUtility.GetRect(10f, _cardGridH,
                GUILayout.Height(_cardGridH), GUILayout.ExpandWidth(true));
            DrawCardGrid(gridRect, multi);

            // ---- 底栏
            GUILayout.BeginHorizontal();
            if (multi)
            {
                if (GUILayout.Button(L10n.T("完成"), stBtn, GUILayout.Width(80), GUILayout.Height(26)))
                    _cardPickerVisible = false;
                if (GUILayout.Button(L10n.T("清空"), stBtn, GUILayout.Width(70), GUILayout.Height(26)))
                    CardPool.ClearSubs();
                GUILayout.Label(L10n.T("已选 ") + CardPool.Count, stBold, GUILayout.ExpandWidth(true));
            }
            else
            {
                if (GUILayout.Button(L10n.T("关闭"), stBtn, GUILayout.Width(80), GUILayout.Height(26)))
                    _cardPickerVisible = false;
                GUILayout.Label(CardLibrary.CardCount + L10n.T(" 张卡"), stBold, GUILayout.ExpandWidth(true));
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        // ---------------------------------------------------------------- 数据
        /// <summary>当前目录的可见项：文件夹在前、卡片在后（搜索时只有卡片）。</summary>
        private static void RefreshCardView()
        {
            string key = (_cardSearch ?? "") + "\u0001"
                       + (_cardDir != null ? _cardDir.path : "") + "\u0001" + CardLibrary.Version;
            if (string.Equals(key, _cardViewKey, StringComparison.Ordinal)) return;
            _cardViewKey = key;
            _cardView.Clear();

            if (!string.IsNullOrEmpty(_cardSearch))
            {
                CardLibrary.Search(_cardSearch, _cardView);
                _cardScroll.y = 0f;
                return;
            }
            if (_cardDir == null) return;
            for (int i = 0; i < _cardDir.kids.Count; i++) _cardView.Add(_cardDir.kids[i]);
            for (int i = 0; i < _cardDir.cards.Count; i++) _cardView.Add(_cardDir.cards[i]);
        }

        private static void GoParentCardDir()
        {
            if (_cardDir == null || _cardDir == CardLibrary.Root) return;
            var target = FindParent(CardLibrary.Root, _cardDir);
            _cardDir = target ?? CardLibrary.Root;
            _cardScroll = Vector2.zero;
            _cardViewKey = null;
        }

        private static CardNode FindParent(CardNode node, CardNode child)
        {
            if (node == null || child == null) return null;
            for (int i = 0; i < node.kids.Count; i++)
                if (node.kids[i] == child) return node;
            for (int i = 0; i < node.kids.Count; i++)
            {
                var r = FindParent(node.kids[i], child);
                if (r != null) return r;
            }
            return null;
        }

        // ---------------------------------------------------------------- 网格
        private static void DrawCardGrid(Rect view, bool multi)
        {
            GUI.Box(view, GUIContent.none, AutoExportPanel.ThemePanel);

            if (_cardView.Count == 0)
            {
                GUI.Label(new Rect(view.x + 10f, view.y + 8f, view.width - 20f, 22f),
                    string.IsNullOrEmpty(_cardSearch) ? L10n.T("这个目录下没有角色卡") : L10n.T("没有匹配的卡。"),
                    AutoExportPanel.ThemeLabelSm);
                return;
            }

            float w = view.width - 16f;
            var content = new Rect(0f, 0f, w, TileGrid.ContentHeight(_cardView.Count, w));
            _cardScroll = GUI.BeginScrollView(view, _cardScroll, content, false, true,
                GUIStyle.none, GUI.skin.verticalScrollbar);

            int cols = TileGrid.Columns(w);
            int firstRow = Mathf.Max(0, Mathf.FloorToInt((_cardScroll.y - TileGrid.Gap) / (TileGrid.H + TileGrid.Gap)));
            int lastRow = Mathf.CeilToInt((_cardScroll.y + view.height) / (TileGrid.H + TileGrid.Gap));
            int first = firstRow * cols;
            int last = Mathf.Min(_cardView.Count - 1, lastRow * cols + cols - 1);

            for (int i = first; i <= last; i++)
            {
                var tile = TileGrid.Cell(i, w);
                var node = _cardView[i];
                if (node.folder) DrawCardFolderTile(tile, node, multi);
                else DrawCardItemTile(tile, node, multi);
            }

            GUI.EndScrollView();
        }

        // 文件夹的「已加入/总数」每帧都要画，逐帧递归太费 → 按版本缓存
        private static int _statVerLib = -1, _statVerPool = -1;
        private static readonly Dictionary<CardNode, int[]> _statCache = new Dictionary<CardNode, int[]>();

        private static void PoolStat(CardNode node, out int inPool, out int total)
        {
            if (_statVerLib != CardLibrary.Version || _statVerPool != CardPool.Count)
            {
                _statCache.Clear();
                _statVerLib = CardLibrary.Version;
                _statVerPool = CardPool.Count;
            }
            int[] v;
            if (!_statCache.TryGetValue(node, out v))
            {
                var all = CardLibrary.AllCards(node);
                int n = 0;
                for (int i = 0; i < all.Count; i++)
                    if (CardPool.Contains(all[i])) n++;
                v = new int[] { n, all.Count };
                _statCache[node] = v;
            }
            inPool = v[0];
            total = v[1];
        }

        private static void DrawCardFolderTile(Rect tile, CardNode node, bool multi)
        {
            var e = Event.current;
            bool hover = e != null && tile.Contains(e.mousePosition);
            if (hover) TileGrid.Fill(tile, new Color(1f, 1f, 1f, 0.07f));

            var box = TileGrid.ThumbRect(tile);
            TileGrid.DrawFolderIcon(box);

            // 多选模式下：目录里已经有卡加进次卡 → 描框（全加=绿，部分=橙）
            int inPool = 0, inTotal = node.total;
            if (multi && node.total > 0)
            {
                PoolStat(node, out inPool, out inTotal);
                if (inPool > 0)
                {
                    bool full = inPool >= inTotal;
                    TileGrid.DrawFrameColored(TileGrid.Grow(box, 3f),
                        full ? new Color(0.32f, 0.95f, 0.48f, 0.95f)
                             : new Color(1f, 0.70f, 0.22f, 0.95f));
                }
            }

            GUI.Label(TileGrid.NameRect(tile), node.name, AutoExportPanel.ThemeTileName);
            GUI.Label(TileGrid.SubRect(tile),
                node.total + L10n.T(" 张")
                    + (multi ? (inPool > 0 ? "   " + inPool + "/" + inTotal : "   +" + node.total) : ""),
                AutoExportPanel.ThemeTileSub);

            // 左键 = 进目录；Ctrl+左键 = 整目录加入次卡（多选模式），**不进去**
            if (e != null && e.type == EventType.MouseDown && e.button == 0
                && tile.Contains(e.mousePosition))
            {
                if (multi && (e.control || e.command)) CardPool.AddFolder(node.path);
                else
                {
                    _cardDir = node;
                    _cardScroll = Vector2.zero;
                    _cardSearch = "";
                    _cardViewKey = null;
                }
                e.Use();
            }
        }

        private static void DrawCardItemTile(Rect tile, CardNode node, bool multi)
        {
            int num = multi ? CardPool.Number(node.path) : 0;
            bool selected = num > 0;

            if (GUI.Button(tile, GUIContent.none,
                    selected ? AutoExportPanel.ThemeTileOn : AutoExportPanel.ThemeTile))
                PickOrToggleCard(node, multi);

            var box = TileGrid.ThumbRect(tile);
            TileGrid.DrawImage(box, ThumbCache.Get(node.path));
            if (selected) TileGrid.DrawFrame(box);

            if (selected)
                TileGrid.DrawBadge(new Rect(box.x + 2f, box.y + 2f, 24f, 19f), num.ToString());

            GUI.Label(TileGrid.NameRect(tile), CardLibrary.Display(node), AutoExportPanel.ThemeTileName);
            GUI.Label(TileGrid.SubRect(tile), node.name, AutoExportPanel.ThemeTileSub);
        }

        // ---------------------------------------------------------------- 交互
        private static void PickOrToggleCard(CardNode node, bool multi)
        {
            if (node == null) return;
            if (multi)
            {
                CardPool.ToggleSub(node.path);
                return;
            }
            ApplyCardPick(node.path);
        }

        /// <summary>单选：把结果写回对应的配置并关窗。</summary>
        private static void ApplyCardPick(string path)
        {
            if (_cardPickTarget == 1) CardPool.SetMain(path);
            else if (_cardPickerIsFemale) KKPEHeightLockPlugin.FemaleCardPath.Value = path;
            else KKPEHeightLockPlugin.MaleCardPath.Value = path;

            KKPEHeightLockPlugin.Log.LogMessage(
                $"KKPEHeightLock: {(_cardPickerIsFemale ? "female" : "male")} card set to {path}");
            _cardPickerVisible = false;
        }
    }
}
