using System;
using System.Collections.Generic;
using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>
    /// 左栏场景卡列表：大图标（类似 Windows 资源管理器）。
    ///
    /// 一个目录一屏：先摆子文件夹（图标 + 名字 + 卡片数），再摆这个目录里的卡
    /// （缩略图 + 名字）。点文件夹进去，点卡片选中并勾选，左上角小方框单独切勾选。
    /// 原来下方那条「预览 + 时长说明」整块去掉了 —— 大图标本身就当预览用，
    /// 详细信息收到底部一行状态文本里。
    ///
    /// 状态（当前目录 / 滚动位置 / 上次选中的卡）都写进 cfg，重开面板接着看。
    /// </summary>
    public static partial class AutoExportPanel
    {
        private static string _sceneDir = "";          // 当前目录（相对场景根，"" = 根）
        private static string _selectedPath = "";      // 上次点开的卡（绝对路径）
        private static Vector2 _sceneScroll;
        private static string _search = "";
        private static float _gridH = 240f;

        private static readonly List<SceneEntry> _viewCards = new List<SceneEntry>();
        private static readonly List<string> _viewFolders = new List<string>();
        private static string _viewKey;
        private static bool _viewStateLoaded;
        private static string _pendingDir;      // 本帧点了文件夹：等这轮画完再切目录

        // ---------------------------------------------------------------- 左栏
        private static void DrawSceneColumn(float bodyH, float colW)
        {
            LoadViewStateOnce();
            RefreshSceneView();

            // 搜索 + 刷新
            GUILayout.BeginHorizontal();
            GUILayout.Label(L10n.T("搜索"), _label, GUILayout.Width(40));
            string q = GUILayout.TextField(_search ?? "", _field,
                GUILayout.ExpandWidth(true), GUILayout.Height(26));
            if (!string.Equals(q, _search, StringComparison.Ordinal))
            {
                _search = q;
                RefreshSceneView();
            }
            if (GUILayout.Button(L10n.T("刷新"), _btn, GUILayout.Width(64), GUILayout.Height(26)))
            {
                SceneCatalog.Scan();
                _selectedPath = "";
                RefreshSceneView();
                SaveViewState();
            }
            GUILayout.EndHorizontal();

            // 全选 / 清空 / 反选
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L10n.T("全选"), _btn, GUILayout.Width(64), GUILayout.Height(24)))
                SceneCatalog.SetAll(true);
            if (GUILayout.Button(L10n.T("清空"), _btn, GUILayout.Width(64), GUILayout.Height(24)))
                SceneCatalog.SetAll(false);
            if (GUILayout.Button(L10n.T("反选"), _btn, GUILayout.Width(64), GUILayout.Height(24)))
                SceneCatalog.InvertAll();
            GUILayout.FlexibleSpace();
            GUILayout.Label(L10n.T("已选 ") + SceneCatalog.CheckedCount + " / " + SceneCatalog.Count,
                _labelBold);
            GUILayout.EndHorizontal();

            // 路径导航：← 上级 · 根目录 · 当前路径
            GUILayout.BeginHorizontal();
            bool atRoot = string.IsNullOrEmpty(_sceneDir) || !string.IsNullOrEmpty(_search);
            bool oldEnabled = GUI.enabled;
            GUI.enabled = !atRoot;
            if (GUILayout.Button(L10n.T("← 上级"), _btn, GUILayout.Width(68), GUILayout.Height(24)))
                GoParentSceneDir();
            GUI.enabled = oldEnabled;

            if (GUILayout.Button(L10n.T("根目录"), _btn, GUILayout.Width(60), GUILayout.Height(24)))
            {
                _sceneDir = "";
                _search = "";
                RefreshSceneView();
                SaveViewState();
            }

            string where = !string.IsNullOrEmpty(_search)
                ? L10n.T("搜索结果：") + _search
                : (string.IsNullOrEmpty(_sceneDir) ? L10n.T("根目录") : _sceneDir);
            GUILayout.Label(" " + where + "  (" + _viewFolders.Count + L10n.T(" 个目录 / ")
                + _viewCards.Count + L10n.T(" 张卡)"), _labelSm, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            // 大图标网格
            // 上面三行控件 ≈ 100px，下面状态行 18px，剩下的都给网格
            _gridH = Mathf.Max(140f, bodyH - 118f);
            var gridRect = GUILayoutUtility.GetRect(colW, _gridH,
                GUILayout.Width(colW), GUILayout.Height(_gridH));
            DrawSceneGrid(gridRect);

            // 底部状态行（替代原来的预览块）
            GUILayout.Label(SceneStatusLine(), _labelSm, GUILayout.Height(18));
        }

        private static string SceneStatusLine()
        {
            var sel = SceneCatalog.Find(_selectedPath);
            if (sel == null)
                return L10n.T("点卡片 = 选中并勾选；点文件夹进入；Ctrl+左键文件夹 = 整个目录一起勾选");
            string tl;
            try { tl = sel.TlSeconds > 0.01f ? "  ·  Timeline " + sel.TlSeconds.ToString("0.##") + "s" : ""; }
            catch (Exception) { tl = ""; }
            return sel.name + "  ·  " + HumanSize(sel.size) + tl;
        }

        // ---------------------------------------------------------------- 数据
        private static void RefreshSceneView()
        {
            string key = (_search ?? "") + "\u0001" + _sceneDir + "\u0001" + SceneCatalog.Version;
            if (string.Equals(key, _viewKey, StringComparison.Ordinal)) return;
            _viewKey = key;

            if (!string.IsNullOrEmpty(_search))
            {
                _viewFolders.Clear();
                SceneCatalog.Matching(_search, _viewCards);
                _sceneScroll.y = 0f;
                return;
            }

            SceneCatalog.FoldersIn(_sceneDir, _viewFolders);
            SceneCatalog.CardsIn(_sceneDir, _viewCards);
        }

        private static void GoParentSceneDir()
        {
            if (string.IsNullOrEmpty(_sceneDir)) return;
            int idx = _sceneDir.LastIndexOf('/');
            _sceneDir = idx > 0 ? _sceneDir.Substring(0, idx) : "";
            RefreshSceneView();
            SaveViewState();
        }

        private static void EnterSceneFolder(string name)
        {
            _sceneDir = string.IsNullOrEmpty(_sceneDir) ? name : _sceneDir + "/" + name;
            _sceneScroll = Vector2.zero;
            RefreshSceneView();
            SaveViewState();
        }

        private static void SetFolderCheck(string rel, bool on)
        {
            string prefix = string.IsNullOrEmpty(rel) ? "" : rel.TrimEnd('/') + "/";
            var items = SceneCatalog.Items;
            for (int i = 0; i < items.Count; i++)
            {
                string r = items[i].rel ?? "";
                bool inside = string.Equals(r, rel, StringComparison.OrdinalIgnoreCase)
                              || r.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                if (inside) SceneCatalog.SetCheck(items[i], on);
            }
        }

        // ---------------------------------------------------------------- 网格
        private static void DrawSceneGrid(Rect view)
        {
            GUI.Box(view, GUIContent.none, _panel);

            int total = _viewFolders.Count + _viewCards.Count;
            if (total == 0)
            {
                GUI.Label(new Rect(view.x + 10f, view.y + 8f, view.width - 20f, 22f),
                    string.IsNullOrEmpty(_search)
                        ? (string.IsNullOrEmpty(SceneCatalog.Info) ? L10n.T("（没有场景卡）") : SceneCatalog.Info)
                        : L10n.T("没有匹配的卡。"), _labelSm);
                return;
            }

            float w = view.width - 16f;                       // 让出竖向滚动条
            var content = new Rect(0f, 0f, w, TileGrid.ContentHeight(total, w));
            _sceneScroll = GUI.BeginScrollView(view, _sceneScroll, content, false, true,
                GUIStyle.none, GUI.skin.verticalScrollbar);

            int cols = TileGrid.Columns(w);
            int firstRow = Mathf.Max(0, Mathf.FloorToInt((_sceneScroll.y - TileGrid.Gap) / (TileGrid.H + TileGrid.Gap)));
            int lastRow = Mathf.CeilToInt((_sceneScroll.y + view.height) / (TileGrid.H + TileGrid.Gap));
            int first = firstRow * cols;
            int last = Mathf.Min(total - 1, lastRow * cols + cols - 1);

            for (int i = first; i <= last; i++)
            {
                var tile = TileGrid.Cell(i, w);
                if (i < _viewFolders.Count) DrawSceneFolderTile(tile, _viewFolders[i]);
                else
                {
                    int ci = i - _viewFolders.Count;
                    if (ci >= 0 && ci < _viewCards.Count) DrawSceneCardTile(tile, _viewCards[ci]);
                }
            }

            GUI.EndScrollView();

            // 切目录要等循环走完：循环里换掉 _viewFolders/_viewCards 会让后面的下标错位
            if (_pendingDir != null)
            {
                string dir = _pendingDir;
                _pendingDir = null;
                EnterSceneFolder(dir);
            }
        }

        private static void DrawSceneFolderTile(Rect tile, string name)
        {
            string rel = string.IsNullOrEmpty(_sceneDir) ? name : _sceneDir + "/" + name;

            int totalCards, onCards;
            SceneCatalog.FolderStat(rel, out totalCards, out onCards);
            bool all = totalCards > 0 && onCards == totalCards;
            bool some = onCards > 0 && !all;

            var e = Event.current;
            bool hover = e != null && tile.Contains(e.mousePosition);
            if (hover) TileGrid.Fill(tile, new Color(1f, 1f, 1f, 0.07f));

            var box = TileGrid.ThumbRect(tile);
            TileGrid.DrawFolderIcon(box);

            // 目录里有卡片被勾选 → 给图标描一圈框（全选=绿，部分=橙），一眼能看出「这里面有选中的」
            if (all)
                TileGrid.DrawFrameColored(TileGrid.Grow(box, 3f), new Color(0.32f, 0.95f, 0.48f, 0.95f));
            else if (some)
                TileGrid.DrawFrameColored(TileGrid.Grow(box, 3f), new Color(1f, 0.70f, 0.22f, 0.95f));

            GUI.Label(TileGrid.NameRect(tile), name, _tileName);
            GUI.Label(TileGrid.SubRect(tile),
                some ? (onCards + "/" + totalCards + L10n.T(" 张")) : (totalCards + L10n.T(" 张")),
                all ? _tileSubOn : _tileSub);

            // 左键 = 进目录；Ctrl+左键 = 只切整个目录的勾选，**不进去**
            if (e != null && e.type == EventType.MouseDown && e.button == 0
                && tile.Contains(e.mousePosition))
            {
                if (e.control || e.command) SetFolderCheck(rel, !all);
                else _pendingDir = name;
                e.Use();
            }
        }

        private static void DrawSceneCardTile(Rect tile, SceneEntry e)
        {
            bool selected = string.Equals(_selectedPath, e.path, StringComparison.OrdinalIgnoreCase);

            if (GUI.Button(TileGrid.CheckRect(tile), e.check ? "■" : "□",
                    e.check ? _checkOn : _checkOff))
            {
                SceneCatalog.SetCheck(e, !e.check);
                if (!e.check && selected) { }
            }

            if (GUI.Button(tile, GUIContent.none, e.check ? _tileOn : _tile))
            {
                _selectedPath = e.path;
                SceneCatalog.SetCheck(e, !e.check);
                SaveViewState();
            }

            var box = TileGrid.ThumbRect(tile);
            TileGrid.DrawImage(box, ThumbCache.Get(e.path));
            if (e.check) TileGrid.DrawFrame(box);

            GUI.Label(TileGrid.NameRect(tile), e.name, _tileName);
            string sub = string.IsNullOrEmpty(e.rel) ? L10n.T("根目录") : e.rel;
            GUI.Label(TileGrid.SubRect(tile), sub, _tileSub);

            if (selected) TileGrid.DrawFrame(tile);
        }

        // ---------------------------------------------------------------- 状态记忆
        private static void LoadViewStateOnce()
        {
            if (_viewStateLoaded) return;
            _viewStateLoaded = true;
            try
            {
                _sceneDir = PathFix.Read(KKPEHeightLockPlugin.ExportTreeDir.Value) ?? "";
                _sceneScroll.y = Mathf.Max(0f, KKPEHeightLockPlugin.ExportTreeScroll.Value);
                _selectedPath = PathFix.Read(KKPEHeightLockPlugin.ExportTreeSel.Value) ?? "";
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("[界面] 读取场景卡列表记忆失败：" + e.Message);
            }
        }

        /// <summary>把「当前目录 / 滚动位置 / 选中的卡」写回 cfg（只在变了才写，避免每帧落盘）。</summary>
        internal static void SaveViewState()
        {
            try
            {
                string dir = _sceneDir ?? "";
                if (!string.Equals(dir, KKPEHeightLockPlugin.ExportTreeDir.Value ?? "", StringComparison.Ordinal))
                    KKPEHeightLockPlugin.ExportTreeDir.Value = dir;

                float y = Mathf.Max(0f, _sceneScroll.y);
                if (Math.Abs(KKPEHeightLockPlugin.ExportTreeScroll.Value - y) > 1f)
                    KKPEHeightLockPlugin.ExportTreeScroll.Value = y;

                string sel = PathFix.ToConfig(_selectedPath ?? "");
                if (!string.Equals(sel, KKPEHeightLockPlugin.ExportTreeSel.Value ?? "", StringComparison.Ordinal))
                    KKPEHeightLockPlugin.ExportTreeSel.Value = sel;
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("[界面] 保存场景卡列表记忆失败：" + e.Message);
            }
        }

        /// <summary>读卡片缩略图（解码在 Export/CardImages.cs，前后端共用）。</summary>
        internal static Texture2D LoadCardPng(string path)
        {
            return CardImages.Load(path);
        }
    }
}
