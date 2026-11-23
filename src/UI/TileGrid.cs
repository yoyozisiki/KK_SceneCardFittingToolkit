using System;
using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>
    /// 大图标网格的几何与图元（场景卡列表 / 角色卡选择器共用）。
    ///
    /// 一个 tile = 缩略图 + 下面两行名字，尺寸固定，所以位置能直接用
    /// 「列号 × (宽+间距)」算出来 —— 不用 GUILayout 自动布局，
    /// 那样行高不固定，滚动位置会和内容错位。
    /// </summary>
    internal static class TileGrid
    {
        /// <summary>
        /// 缩略图边长。**可变** —— 场景卡列表想比卡片选择器大一点，
        /// 绘制前把 Thumb 设大、画完恢复默认即可（W/H 会跟着算）。
        /// </summary>
        public static float Thumb = DefaultThumb;
        public const float DefaultThumb = 132f;

        public static float W { get { return Thumb + 20f; } }     // tile 宽
        public static float H { get { return Thumb + 64f; } }     // tile 高（缩略图 + 名字两行 + 间隙）
        public const float Gap = 8f;

        /// <summary>把缩略图边长切到指定值；传 0 恢复默认。</summary>
        public static float SetThumb(float thumb)
        {
            float old = Thumb;
            Thumb = thumb > 0f ? thumb : DefaultThumb;
            return old;
        }

        public static int Columns(float width)
        {
            if (width < W) return 1;
            return Mathf.Max(1, Mathf.FloorToInt((width - Gap) / (W + Gap)));
        }

        /// <summary>
        /// 第 index 个 tile 的矩形（相对滚动内容原点，已含外边距）。
        ///
        /// 列宽**按可用宽度均分**，不再固定 W —— 固定宽度时最后一列右边会剩一大块空白
        /// （窗口 1380 时 3 列只占 612，右边空 148）。
        /// </summary>
        public static Rect Cell(int index, float width)
        {
            int cols = Columns(width);
            if (cols <= 0) cols = 1;
            float cellW = (width - Gap) / cols;
            if (cellW < 40f) cellW = 40f;
            int col = index % cols;
            int row = index / cols;
            return new Rect(Gap + col * cellW, Gap + row * (H + Gap), cellW - Gap, H);
        }

        public static float ContentHeight(int count, float width)
        {
            if (count <= 0) return H + Gap * 2f;
            int cols = Columns(width);
            int rows = (count + cols - 1) / cols;
            return rows * (H + Gap) + Gap;
        }

        /// <summary>缩略图区域（tile 内居中靠上）。</summary>
        public static Rect ThumbRect(Rect tile)
        {
            return new Rect(tile.x + (tile.width - Thumb) * 0.5f, tile.y + 6f, Thumb, Thumb);
        }

        public static Rect NameRect(Rect tile)
        {
            return new Rect(tile.x + 2f, tile.y + 6f + Thumb + 4f, tile.width - 4f, 34f);
        }

        public static Rect SubRect(Rect tile)
        {
            return new Rect(tile.x + 2f, tile.y + 6f + Thumb + 38f, tile.width - 4f, 16f);
        }

        /// <summary>左上角的小方框（勾选/全选），返回它占的矩形。</summary>
        public static Rect CheckRect(Rect tile)
        {
            return new Rect(tile.x + 2f, tile.y + 2f, 20f, 20f);
        }

        /// <summary>把纹理等比画进正方形框里（居中）。</summary>
        public static void DrawImage(Rect box, Texture2D tex)
        {
            var old = GUI.color;
            GUI.color = new Color(0.06f, 0.06f, 0.06f, 1f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = old;

            if (tex == null) return;
            float ar = tex.width / (float)Mathf.Max(1, tex.height);
            float w = box.width, h = box.height;
            if (ar > 1f) h = box.width / ar; else w = box.height * ar;
            GUI.DrawTexture(new Rect(box.x + (box.width - w) * 0.5f,
                                     box.y + (box.height - h) * 0.5f, w, h), tex);
        }

        /// <summary>画一个「文件夹」图标（两个矩形拼的，不依赖贴图资源）。</summary>
        public static void DrawFolderIcon(Rect box)
        {
            var old = GUI.color;
            GUI.color = new Color(0.10f, 0.10f, 0.10f, 1f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);

            float pad = box.width * 0.14f;
            float bodyY = box.y + box.height * 0.34f;
            float bodyH = box.height * 0.42f;
            var tab = new Rect(box.x + pad, box.y + box.height * 0.24f, box.width * 0.42f, box.height * 0.14f);
            var body = new Rect(box.x + pad, bodyY, box.width - pad * 2f, bodyH);

            GUI.color = new Color(0.86f, 0.66f, 0.26f, 1f);
            GUI.DrawTexture(tab, Texture2D.whiteTexture);
            GUI.color = new Color(0.94f, 0.76f, 0.36f, 1f);
            GUI.DrawTexture(body, Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>选中的 tile 描一圈亮边。</summary>
        public static void DrawFrame(Rect r)
        {
            var tex = AutoExportPanel.ThemeAccentTex;
            if (tex == null) return;
            var old = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2f), tex);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 2f, r.width, 2f), tex);
            GUI.DrawTexture(new Rect(r.x, r.y, 2f, r.height), tex);
            GUI.DrawTexture(new Rect(r.xMax - 2f, r.y, 2f, r.height), tex);
            GUI.color = old;
        }

        /// <summary>把矩形往外扩 d 像素（描边要比图标大一圈才好看）。</summary>
        public static Rect Grow(Rect r, float d)
        {
            return new Rect(r.x - d, r.y - d, r.width + d * 2f, r.height + d * 2f);
        }

        /// <summary>半透明填充（hover 高亮）。</summary>
        public static void Fill(Rect r, Color color)
        {
            var tex = AutoExportPanel.ThemeAccentTex;
            if (tex == null) return;
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, tex);
            GUI.color = old;
        }

        /// <summary>彩色描边 —— 文件夹里「有卡片被选中」时的强调框。</summary>
        public static void DrawFrameColored(Rect r, Color color)
        {
            var tex = AutoExportPanel.ThemeAccentTex;
            if (tex == null) return;
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2f), tex);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 2f, r.width, 2f), tex);
            GUI.DrawTexture(new Rect(r.x, r.y, 2f, r.height), tex);
            GUI.DrawTexture(new Rect(r.xMax - 2f, r.y, 2f, r.height), tex);
            GUI.color = old;
        }

        /// <summary>彩色的编号角标（次角色卡的替换顺序）。</summary>
        public static void DrawBadge(Rect box, string text)
        {
            GUI.Box(box, text, AutoExportPanel.ThemeBadge);
        }
    }
}
