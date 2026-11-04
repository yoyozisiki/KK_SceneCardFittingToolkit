using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>
    /// 左栏场景卡列表：按目录递归成树，目录行可折叠、可整目录勾选，
    /// 卡片行点一下选中并显示下方预览。
    ///
    /// 行是按「索引 × RowH」算绝对坐标画的，不用 GUILayout 自动布局 ——
    /// 自动布局会在行之间插间距，行高就不等于 RowH，滚动条位置和行会越走越偏。
    /// </summary>
    public static partial class AutoExportPanel
    {
        private const float RowH = 24f;
        private const float ColCheck = 32f;
        private const float ColSize = 92f;
        private const float Indent = 16f;
        private const float ArrowW = 22f;

        private class DirNode
        {
            public string name;      // 显示名（只显示最后一段）
            public string path;      // 唯一键（含扫描根前缀）
            public int depth;
            public readonly List<DirNode> kids = new List<DirNode>();
            public readonly List<int> cards = new List<int>();   // 直属卡片（_scenes 下标）
            public int total, on;    // 子树统计，用于目录行显示 3/28
        }

        private class Row
        {
            public bool folder;
            public DirNode node;
            public int card;
            public int depth;
        }

        private static DirNode _root;
        private static readonly List<Row> _rows = new List<Row>();
        private static readonly HashSet<string> _collapsed = new HashSet<string>();
        private static bool _treeDirty = true;
        private static bool _checkDirty = true;

        private static Vector2 _sceneScroll;
        private static float _listH = 200f;
        private static int _selectedIdx = -1;
        private static Texture2D _sceneTex;
        private static string _sceneTexPath;

        // ---------------------------------------------------------------- 建树
        private static void BuildTree()
        {
            _treeDirty = false;
            _root = new DirNode();
            var map = new Dictionary<string, DirNode>(StringComparer.OrdinalIgnoreCase);
            map[""] = _root;

            // 多个互不包含的根目录时，每个根目录单独挂一层，同名子目录才不会串在一起
            bool split = _roots.Count > 1;

            var idx = Filtered();
            for (int k = 0; k < idx.Count; k++)
            {
                int si = idx[k];
                var e = _scenes[si];

                DirNode baseNode = _root;
                string prefix = "";
                if (split)
                {
                    string key = "\u0002" + (e.root ?? "");
                    if (!map.TryGetValue(key, out baseNode))
                    {
                        baseNode = new DirNode();
                        baseNode.name = RootName(e.root);
                        baseNode.path = key;
                        baseNode.depth = 1;
                        _root.kids.Add(baseNode);
                        map[key] = baseNode;
                    }
                    prefix = key;
                }

                DirNode n = CreatePath(map, e.rel ?? "", baseNode, prefix);
                n.cards.Add(si);
            }

            _checkDirty = true;
            FlattenAll();
        }

        /// <summary>
        /// 建出 rel 这条路径上的所有节点，返回最深那个。
        /// 已建过的直接复用 —— 少了这一步，同一目录会被每张卡各建一次，
        /// 列表里就出现一排重名的文件夹。
        /// </summary>
        private static DirNode CreatePath(Dictionary<string, DirNode> map, string rel,
                                          DirNode baseNode, string prefix)
        {
            if (string.IsNullOrEmpty(rel)) return baseNode;

            string key = prefix + "\u001f" + rel;
            DirNode exist;
            if (map.TryGetValue(key, out exist)) return exist;

            int cut = rel.LastIndexOf('/');
            string parentRel = cut >= 0 ? rel.Substring(0, cut) : "";
            string name = cut >= 0 ? rel.Substring(cut + 1) : rel;

            DirNode parent = CreatePath(map, parentRel, baseNode, prefix);
            var n = new DirNode();
            n.name = name;
            n.path = key;
            n.depth = parent.depth + 1;
            parent.kids.Add(n);
            map[key] = n;
            return n;
        }

        /// <summary>把树按折叠状态展开成扁平行列表（子目录在前、直属卡片在后）。</summary>
        private static void FlattenAll()
        {
            _rows.Clear();
            if (_root != null) Flatten(_root);
            ClampScroll();
        }

        private static void Flatten(DirNode n)
        {
            for (int i = 0; i < n.kids.Count; i++)
            {
                var k = n.kids[i];
                var r = new Row();
                r.folder = true;
                r.node = k;
                r.depth = k.depth;
                _rows.Add(r);
                if (!_collapsed.Contains(k.path)) Flatten(k);
            }
            for (int i = 0; i < n.cards.Count; i++)
            {
                var r = new Row();
                r.card = n.cards[i];
                r.depth = n.depth + 1;
                _rows.Add(r);
            }
        }

        /// <summary>折叠目录后行数变少，滚动位置要跟着收回来，否则会停在空白处。</summary>
        private static void ClampScroll()
        {
            float maxY = Mathf.Max(0f, _rows.Count * RowH - _listH);
            if (_sceneScroll.y > maxY) _sceneScroll.y = maxY;
            if (_sceneScroll.y < 0f) _sceneScroll.y = 0f;
        }

        private static void UpdateCounts()
        {
            if (!_checkDirty || _root == null) return;
            _checkDirty = false;
            CountNode(_root);
        }

        private static void CountNode(DirNode n)
        {
            int total = n.cards.Count, on = 0;
            for (int i = 0; i < n.cards.Count; i++)
                if (_scenes[n.cards[i]].check) on++;
            for (int i = 0; i < n.kids.Count; i++)
            {
                CountNode(n.kids[i]);
                total += n.kids[i].total;
                on += n.kids[i].on;
            }
            n.total = total;
            n.on = on;
        }

        private static void SetSubtree(DirNode n, bool on)
        {
            for (int i = 0; i < n.cards.Count; i++)
            {
                var e = _scenes[n.cards[i]];
                if (e.check == on) continue;
                e.check = on;
                _checkedCount += on ? 1 : -1;
            }
            for (int i = 0; i < n.kids.Count; i++) SetSubtree(n.kids[i], on);
            _checkDirty = true;
        }

        // ---------------------------------------------------------------- 左栏
        private static void DrawSceneColumn(float bodyH, float colW)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(L10n.T("搜索"), _label, GUILayout.Width(40));
            string q = GUILayout.TextField(_search ?? "", _field,
                GUILayout.ExpandWidth(true), GUILayout.Height(28));
            if (q != _search) { _search = q; _filterKey = null; _treeDirty = true; }
            if (GUILayout.Button(L10n.T("刷新"), _btn, GUILayout.Width(76), GUILayout.Height(28)))
                ScanScenes();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L10n.T("全选"), _btn, GUILayout.Width(76), GUILayout.Height(26))) SetAll(true);
            if (GUILayout.Button(L10n.T("清空"), _btn, GUILayout.Width(76), GUILayout.Height(26))) SetAll(false);
            if (GUILayout.Button(L10n.T("反选"), _btn, GUILayout.Width(76), GUILayout.Height(26))) InvertAll();
            GUILayout.FlexibleSpace();
            GUILayout.Label(L10n.T("已选 ") + _checkedCount + L10n.T(" 个"), _labelBold);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal(GUILayout.Height(22));
            GUILayout.Space(6f + ArrowW);
            GUILayout.Label(L10n.T("场景 / 目录"), _head, GUILayout.ExpandWidth(true));
            GUILayout.Label(L10n.T("选"), _head, GUILayout.Width(ColCheck));
            GUILayout.Label(L10n.T("大小"), _head, GUILayout.Width(ColSize));
            GUILayout.Space(16f);   // 让开滚动条，和下面的行对齐
            GUILayout.EndHorizontal();

            if (_treeDirty) BuildTree();
            UpdateCounts();

            _listH = Mathf.Max(120f, bodyH - 272f);
            var listRect = GUILayoutUtility.GetRect(colW, _listH,
                GUILayout.Width(colW), GUILayout.Height(_listH));
            DrawList(listRect);

            GUILayout.Space(4);
            DrawScenePreview();
        }

        private static void DrawList(Rect view)
        {
            GUI.Box(view, GUIContent.none, _panel);

            if (_rows.Count == 0)
            {
                GUI.Label(new Rect(view.x + 8f, view.y + 8f, view.width - 16f, 22f),
                    string.IsNullOrEmpty(_scanInfo) ? L10n.T("（没有场景卡）") : _scanInfo, _labelSm);
                return;
            }

            float w = view.width - 16f;                       // 让出竖向滚动条
            float contentH = Mathf.Max(view.height, _rows.Count * RowH);
            var content = new Rect(0f, 0f, w, contentH);

            _sceneScroll = GUI.BeginScrollView(view, _sceneScroll, content, false, true,
                GUIStyle.none, GUI.skin.verticalScrollbar);

            int first = Mathf.Max(0, Mathf.FloorToInt(_sceneScroll.y / RowH));
            int last = Mathf.Min(_rows.Count - 1,
                                 Mathf.CeilToInt((_sceneScroll.y + view.height) / RowH));
            for (int i = first; i <= last; i++)
            {
                var r = new Rect(0f, i * RowH, w, RowH);
                if (_rows[i].folder) DrawFolderRow(_rows[i], r);
                else DrawCardRow(_rows[i], r);
            }
            GUI.EndScrollView();

            ClampScroll();
        }

        private static void DrawFolderRow(Row row, Rect r)
        {
            var n = row.node;
            bool all = n.total > 0 && n.on == n.total;
            bool some = n.on > 0 && !all;
            bool collapsed = _collapsed.Contains(n.path);

            float x = 6f + n.depth * Indent;

            if (GUI.Button(new Rect(r.x + x, r.y, ArrowW, RowH),
                    collapsed ? "▶" : "▼", _arrow))
            {
                if (collapsed) _collapsed.Remove(n.path); else _collapsed.Add(n.path);
                FlattenAll();
            }

            float nameX = x + ArrowW;
            float nameW = Mathf.Max(60f, r.width - nameX - ColCheck - ColSize);
            if (GUI.Button(new Rect(r.x + nameX, r.y, nameW, RowH), " " + n.name,
                    all ? _rowOn : _rowFolder))
                SetSubtree(n, !all);

            float checkX = r.width - ColCheck - ColSize;
            if (GUI.Button(new Rect(r.x + checkX, r.y, ColCheck, RowH),
                    (all || some) ? "■" : "□", all ? _checkOn : _checkOff))
                SetSubtree(n, !all);

            GUI.Label(new Rect(r.x + r.width - ColSize, r.y, ColSize, RowH),
                n.on + "/" + n.total, _labelSm);
        }

        private static void DrawCardRow(Row row, Rect r)
        {
            var e = _scenes[row.card];

            float x = 6f + row.depth * Indent;

            // 点名字 = 选中并预览，顺手切勾选。不能用 Event.current 判点击，
            // Button 会把 MouseDown 标成 Used。
            float nameX = x + ArrowW;
            float nameW = Mathf.Max(60f, r.width - nameX - ColCheck - ColSize);
            if (GUI.Button(new Rect(r.x + nameX, r.y, nameW, RowH), " " + e.name,
                    e.check ? _rowOn : _row))
            {
                _selectedIdx = row.card;
                e.check = !e.check;
                _checkedCount += e.check ? 1 : -1;
                _checkDirty = true;
            }

            float checkX = r.width - ColCheck - ColSize;
            if (GUI.Button(new Rect(r.x + checkX, r.y, ColCheck, RowH),
                    e.check ? "■" : "□", e.check ? _checkOn : _checkOff))
            {
                e.check = !e.check;
                _checkedCount += e.check ? 1 : -1;
                _checkDirty = true;
            }

            GUI.Label(new Rect(r.x + r.width - ColSize, r.y, ColSize, RowH),
                HumanSize(e.size), _labelSm);
        }

        // ---------------------------------------------------------------- 预览
        private static void DrawScenePreview()
        {
            GUILayout.BeginVertical(_panel, GUILayout.Height(132));
            GUILayout.BeginHorizontal();

            const int PW = 196, PH = 110;
            var r = GUILayoutUtility.GetRect(PW, PH, GUILayout.Width(PW), GUILayout.Height(PH));
            GUI.DrawTexture(r, _texField);

            SceneEntry sel = (_selectedIdx >= 0 && _selectedIdx < _scenes.Count)
                ? _scenes[_selectedIdx] : null;

            if (sel != null)
            {
                if (_sceneTexPath != sel.path)
                {
                    if (_sceneTex != null) { UnityEngine.Object.Destroy(_sceneTex); _sceneTex = null; }
                    _sceneTexPath = sel.path;
                    try { _sceneTex = LoadCardPng(sel.path); }
                    catch (Exception) { _sceneTex = null; }
                }
                if (_sceneTex != null)
                {
                    float ar = _sceneTex.width / (float)_sceneTex.height;
                    float w = PW, h = PH;
                    if (ar > PW / (float)PH) h = PW / ar; else w = PH * ar;
                    GUI.DrawTexture(new Rect(r.x + (PW - w) * 0.5f, r.y + (PH - h) * 0.5f, w, h),
                                    _sceneTex, ScaleMode.ScaleToFit);
                }
                else GUI.Label(r, L10n.T("  无法预览"), _labelSm);
            }
            else GUI.Label(r, L10n.T("  点任意一行看预览"), _labelSm);

            GUILayout.Space(8);
            GUILayout.BeginVertical();
            if (sel == null)
            {
                GUILayout.Label(L10n.T("未选中场景卡"), _labelBold);
            }
            else
            {
                GUILayout.Label(sel.name, _labelBold);
                GUILayout.Label((string.IsNullOrEmpty(sel.rel) ? L10n.T("根目录") : sel.rel)
                    + " · " + HumanSize(sel.size), _labelSm);
                GUILayout.Space(2);

                float tl = sel.TlSeconds;
                if (sel.tl != null) GUILayout.Label(sel.tl.Describe(), _labelSm);
                else GUILayout.Label(L10n.T("无 Timeline"), _labelSm);

                float minSec = Mathf.Max(0.5f, KKPEHeightLockPlugin.ExportMinSeconds.Value);
                float content = Mathf.Max(tl, minSec);
                float fade = Mathf.Max(0f, KKPEHeightLockPlugin.ExportFadeSeconds.Value);
                float total = content + fade * 2f;
                int fps = Mathf.Clamp(KKPEHeightLockPlugin.ExportFps.Value, 1, 240);
                string note = (tl > 0.01f && tl < minSec - 0.001f) ? L10n.T("（补齐）")
                            : (tl <= 0.01f ? L10n.T("（按补齐时长）") : "");
                GUILayout.Label(string.Format(
                    L10n.T("导出 {0:0.##}s{1} + 头尾 {2:0.##}s = {3:0.##}s / {4} 帧 @ {5}fps"),
                    content, note, fade, total, Mathf.RoundToInt(total * fps), fps), _labelSm);
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        // ---------------------------------------------------------------- PNG
        /// <summary>
        /// 读卡片缩略图。场景卡/角色卡都是「PNG + IEND 之后追加 MessagePack」，
        /// 游戏自带的 PngAssist 只认角色卡那一套，所以这里按 PNG 结构截到 IEND 再解码。
        /// </summary>
        internal static Texture2D LoadCardPng(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                byte[] raw = File.ReadAllBytes(path);
                int end = PngEnd(raw);
                if (end <= 8) return null;

                var png = new byte[end];
                Array.Copy(raw, png, end);

                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!tex.LoadImage(png)) { UnityEngine.Object.Destroy(tex); return null; }
                tex.hideFlags = HideFlags.HideAndDontSave;
                return tex;
            }
            catch (Exception) { return null; }
        }

        /// <summary>返回 PNG 数据到 IEND 为止的字节数；不是合法 PNG 返回 0。</summary>
        private static int PngEnd(byte[] d)
        {
            if (d == null || d.Length < 16) return 0;
            if (d[0] != 0x89 || d[1] != 0x50 || d[2] != 0x4E || d[3] != 0x47) return 0;

            int p = 8;
            while (p + 12 <= d.Length)
            {
                int len = (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3];
                if (len < 0 || p + 12L + len > d.Length) return 0;
                bool iend = d[p + 4] == (byte)'I' && d[p + 5] == (byte)'E'
                         && d[p + 6] == (byte)'N' && d[p + 7] == (byte)'D';
                p += 12 + len;
                if (iend) return p;
            }
            return 0;
        }
    }
}
