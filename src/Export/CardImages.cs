using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>
    /// 角色卡 / 场景卡缩略图的解码（前后端共用的图片读取）。
    ///
    /// 这两类卡都是「PNG + IEND 之后追加 MessagePack」，游戏自带的 PngAssist 只认角色卡
    /// 那一套，场景卡会解不出来 —— 所以按 PNG 结构截到 IEND 再交给 Texture2D.LoadImage。
    /// </summary>
    public static class CardImages
    {
        public static Texture2D Load(string path)
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
        public static int PngEnd(byte[] d)
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

        public static void Destroy(ref Texture2D tex)
        {
            if (tex == null) return;
            try { UnityEngine.Object.Destroy(tex); } catch (Exception) { }
            tex = null;
        }
    }

    /// <summary>
    /// 缩略图缓存（大图标网格用）。
    ///
    /// 场景卡 PNG 常常是 1280x720（3.7MB 一张），直接缓存几百张能把显存吃穿，
    /// 所以加载后先缩到长边 192 再做 LRU。加载走队列、每帧限量，
    /// 免得一开面板就卡几秒。
    /// </summary>
    public static class ThumbCache
    {
        private const int MaxItems = 400;
        private const int MaxSide = 192;

        private static readonly Dictionary<string, Texture2D> _map =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> _lru = new List<string>();      // 老的在前
        private static readonly List<string> _queue = new List<string>();
        private static readonly HashSet<string> _queued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static int _frame = -1;

        /// <summary>取缩略图；还没加载好返回 null（下一帧再来）。</summary>
        public static Texture2D Get(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Texture2D tex;
            if (_map.TryGetValue(path, out tex))
            {
                Touch(path);
                return tex;
            }
            if (_queued.Add(path)) _queue.Add(path);
            return null;
        }

        /// <summary>每帧调用一次：最多加载 budget 张。</summary>
        public static void Tick(int budget)
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;

            int done = 0;
            while (_queue.Count > 0 && done < budget)
            {
                string path = _queue[0];
                _queue.RemoveAt(0);
                _queued.Remove(path);
                if (_map.ContainsKey(path)) continue;

                var tex = Shrink(CardImages.Load(path), MaxSide);
                if (tex == null) continue;
                _map[path] = tex;
                _lru.Add(path);
                done++;
            }

            while (_lru.Count > MaxItems)
            {
                string old = _lru[0];
                _lru.RemoveAt(0);
                Texture2D dead;
                if (_map.TryGetValue(old, out dead))
                {
                    _map.Remove(old);
                    try { UnityEngine.Object.Destroy(dead); } catch (Exception) { }
                }
            }
        }

        public static void Clear()
        {
            foreach (var kv in _map)
                try { UnityEngine.Object.Destroy(kv.Value); } catch (Exception) { }
            _map.Clear();
            _lru.Clear();
            _queue.Clear();
            _queued.Clear();
        }

        private static void Touch(string path)
        {
            int idx = _lru.IndexOf(path);
            if (idx < 0 || idx == _lru.Count - 1) return;
            _lru.RemoveAt(idx);
            _lru.Add(path);
        }

        /// <summary>等比缩到长边 max（小图原样返回）。走 RenderTexture，失败就退回原图。</summary>
        private static Texture2D Shrink(Texture2D src, int max)
        {
            if (src == null) return null;
            try
            {
                int w = src.width, h = src.height;
                if (w <= 0 || h <= 0) return src;
                if (w <= max && h <= max) return src;

                float s = Mathf.Min(max / (float)w, max / (float)h);
                int nw = Mathf.Max(1, Mathf.RoundToInt(w * s));
                int nh = Mathf.Max(1, Mathf.RoundToInt(h * s));

                var rt = RenderTexture.GetTemporary(nw, nh, 0);
                var prev = RenderTexture.active;
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var dst = new Texture2D(nw, nh, TextureFormat.RGB24, false);
                dst.ReadPixels(new Rect(0, 0, nw, nh), 0, 0);
                dst.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                dst.hideFlags = HideFlags.HideAndDontSave;
                try { UnityEngine.Object.Destroy(src); } catch (Exception) { }
                return dst;
            }
            catch (Exception)
            {
                return src;   // 缩不了就用原图，至少能显示
            }
        }
    }
}
