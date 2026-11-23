using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KKPEHeightLock
{
    /// <summary>
    /// 直接从场景卡文件（PNG 尾部的 MessagePack 扩展块）读 Timeline 的时长和
    /// 「会被取消隐藏的对象」，不用加载场景。
    /// XML 里 duration 是结束时间、timeScale 是倍速，实际秒数 = duration / timeScale。
    /// 不靠运行时插件判断时长：没加载的卡查不到，且没有时间轴时插件返回的是默认 10 秒。
    /// </summary>
    public static class TimelineReader
    {
        public class Info
        {
            public float duration;
            public float timeScale = 1f;
            public int keyframes;
            public float lastKey;
            /// <summary>卡内声明的 XML 长度比文件剩余字节多 —— 文件被截断。</summary>
            public bool truncated;

            /// <summary>换算成实际播放秒数（timeScale &lt; 1 表示放慢）。</summary>
            public float Seconds
            {
                get
                {
                    if (timeScale <= 0.0001f) return Math.Max(duration, 0f);
                    return Math.Max(duration, 0f) / timeScale;
                }
            }
        }

        // MessagePack 里 map 的键 "timeline"（长度 8）前面是 fixstr 头 0xa8
        private static readonly byte[] TimelineTag =
        {
            0xA8, (byte)'t', (byte)'i', (byte)'m', (byte)'e',
            (byte)'l', (byte)'i', (byte)'n', (byte)'e'
        };

        private static readonly byte[] SceneInfoTag =
        {
            (byte)'s', (byte)'c', (byte)'e', (byte)'n', (byte)'e',
            (byte)'I', (byte)'n', (byte)'f', (byte)'o'
        };

        private const int MaxScan = 32 * 1024 * 1024;   // 兜底扫描窗口
        private const int TailScan = 12 * 1024 * 1024;  // 先只读尾部这么多
        private const int NearScan = 2 * 1024 * 1024;   // 先试 2MB，命中率很高

        // ---------------------------------------------------------------- 缓存
        // 列表里滚动时会反复问同一张卡的时长，缓存住避免重复读盘。
        private static readonly Dictionary<string, Info> Cache = new Dictionary<string, Info>();
        // 同一张卡也会被反复问「哪些角色会被 timeline 取消隐藏」，同样缓存。
        private static readonly Dictionary<string, HashSet<int>> RevealCache = new Dictionary<string, HashSet<int>>();

        /// <summary>读一张卡的 Timeline；没有时间轴返回 null。结果会缓存。</summary>
        public static Info Read(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Info hit;
            if (Cache.TryGetValue(path, out hit)) return hit;

            Info info = null;
            try { info = ReadUncached(path); }
            catch (Exception) { info = null; }
            Cache[path] = info;
            return info;
        }

        private static Info ReadUncached(string path)
        {
            FileInfo fi = new FileInfo(path);
            long size = fi.Length;
            if (size <= 0) return null;

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                           FileShare.ReadWrite, 64 * 1024))
            {
                // 1) 先试尾部 2MB（绝大多数卡的时间轴就在这儿）
                if (size > NearScan)
                {
                    var tail = ReadAt(fs, size - NearScan, (int)NearScan);
                    var got = ParseBytes(tail);
                    if (got != null) return got;
                }

                // 2) 再试尾部 12MB
                if (size > TailScan)
                {
                    var tail = ReadAt(fs, size - TailScan, TailScan);
                    var got = ParseBytes(tail);
                    if (got != null) return got;
                }

                // 3) 整文件
                if (size > int.MaxValue) return null;
                var all = ReadAt(fs, 0, (int)size);
                return ParseBytes(all);
            }
        }

        // ---------------------------------------------------------------- 取消隐藏的对象
        /// <summary>
        /// 读一张卡里「会被 Timeline 取消隐藏」的对象编号集合，没有时间轴返回空集合。
        /// Timeline 靠 objectEnabled 通道写 treeNodeObject.visible，出现过 true 关键帧
        /// 就说明播放中会显示 —— 这类隐藏角色和上一个显示角色是前后关系（同一人的下一套造型）。
        /// 结果会缓存。
        /// </summary>
        public static HashSet<int> ReadRevealedObjects(string path)
        {
            var empty = new HashSet<int>();
            if (string.IsNullOrEmpty(path)) return empty;
            HashSet<int> hit;
            if (RevealCache.TryGetValue(path, out hit)) return hit;

            HashSet<int> set = empty;
            try { set = ReadRevealedUncached(path); }
            catch (Exception) { }
            RevealCache[path] = set;
            return set;
        }

        private static HashSet<int> ReadRevealedUncached(string path)
        {
            FileInfo fi = new FileInfo(path);
            long size = fi.Length;
            if (size <= 0) return new HashSet<int>();

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                           FileShare.ReadWrite, 64 * 1024))
            {
                if (size > NearScan)
                {
                    var tail = ReadAt(fs, size - NearScan, (int)NearScan);
                    var got = ParseRevealedBytes(tail);
                    if (got != null) return got;
                }
                if (size > TailScan)
                {
                    var tail = ReadAt(fs, size - TailScan, TailScan);
                    var got = ParseRevealedBytes(tail);
                    if (got != null) return got;
                }
                if (size > int.MaxValue) return new HashSet<int>();
                var all = ReadAt(fs, 0, (int)size);
                var full = ParseRevealedBytes(all);
                return full ?? new HashSet<int>();
            }
        }

        private static HashSet<int> ParseRevealedBytes(byte[] data)
        {
            int start = 0;
            while (start < data.Length)
            {
                int i = IndexOf(data, TimelineTag, start);
                if (i < 0) return null;
                string xml;
                bool truncated;
                if (TryXmlAfter(data, i, out xml, out truncated))
                    return ParseRevealedXml(xml);
                start = i + 1;
            }
            return null;
        }

        /// <summary>
        /// 从时间轴 XML 里提取「出现过 value=true 的 objectEnabled 通道」的对象编号。
        /// 注意：通道的 enabled 属性是「这条曲线本身启不启用」，跟 keyframe 的
        /// value（对象的显隐）是两回事，这里只看后者。
        /// </summary>
        private static HashSet<int> ParseRevealedXml(string xml)
        {
            var set = new HashSet<int>();
            const string channel = "id=\"objectEnabled\"";
            const string indexKey = "objectIndex=\"";
            // 必须带空格：<interpolableGroup .../> 也以 "<interpolable" 开头，
            // 但它没有 objectEnabled、还是自闭合的，误匹配会连带跳过紧随其后的真通道。
            const string openTag = "<interpolable ";

            int p = 0;
            while (true)
            {
                int k = xml.IndexOf(openTag, p, StringComparison.Ordinal);
                if (k < 0) break;
                int gt = xml.IndexOf('>', k);
                if (gt < 0) break;
                string attrs = xml.Substring(k, gt - k + 1);

                bool selfClosed = attrs.EndsWith("/>", StringComparison.Ordinal);
                int end = selfClosed ? -1 : xml.IndexOf("</interpolable>", gt, StringComparison.Ordinal);
                int next = end >= 0 ? end + "</interpolable>".Length : gt + 1;
                string body = end >= 0 ? xml.Substring(gt + 1, end - gt - 1) : "";

                if (attrs.Contains(channel))
                {
                    int oi = attrs.IndexOf(indexKey, StringComparison.Ordinal);
                    if (oi >= 0)
                    {
                        int vs = oi + indexKey.Length;
                        int ve = attrs.IndexOf('"', vs);
                        int idx;
                        if (ve > vs && int.TryParse(attrs.Substring(vs, ve - vs), out idx)
                            && HasTrueKeyframe(body))
                            set.Add(idx);
                    }
                }
                p = next;
            }
            return set;
        }

        /// <summary>通道正文里有没有 &lt;keyframe … value="true"&gt;（对象被显示出来的帧）。</summary>
        private static bool HasTrueKeyframe(string body)
        {
            int p = 0;
            while (true)
            {
                int k = body.IndexOf("<keyframe", p, StringComparison.Ordinal);
                if (k < 0) return false;
                int gt = body.IndexOf('>', k);
                if (gt < 0) return false;
                string attrs = body.Substring(k, gt - k + 1);
                // 只要外层 keyframe 的 value 是 true 就算——对象在这一帧被设为显示。
                // 内层 curveKeyframe 的 value 是插值数值，不带 true/false，不会误命中。
                if (attrs.Contains("value=\"true\"")) return true;
                p = gt + 1;
            }
        }

        private static byte[] ReadAt(FileStream fs, long offset, int count)
        {
            fs.Seek(offset, SeekOrigin.Begin);
            var buf = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = fs.Read(buf, read, count - read);
                if (n <= 0) break;
                read += n;
            }
            if (read == count) return buf;
            var shorter = new byte[read];
            Array.Copy(buf, shorter, read);
            return shorter;
        }

        // ---------------------------------------------------------------- 解析
        private static Info ParseBytes(byte[] data)
        {
            int start = 0;
            while (start < data.Length)
            {
                int i = IndexOf(data, TimelineTag, start);
                if (i < 0) return null;
                string xml;
                bool truncated;
                if (TryXmlAfter(data, i, out xml, out truncated))
                {
                    var info = ParseRoot(xml);
                    if (info != null) { info.truncated = truncated; return info; }
                }
                start = i + 1;
            }
            return null;
        }

        /// <summary>在 timeline 标记之后取出 sceneInfo 的 XML 文本。</summary>
        private static bool TryXmlAfter(byte[] data, int i, out string xml, out bool truncated)
        {
            xml = null;
            truncated = false;

            // 1) 首选：按 MessagePack 的长度前缀精确切，不用猜、也不会被超大 XML 骗到
            int j = IndexOf(data, SceneInfoTag, i, Math.Min(data.Length, i + 4096));
            if (j >= 0)
            {
                int body;
                int size;
                if (TryStrHead(data, j + SceneInfoTag.Length, out size, out body))
                {
                    if (size > 0 && size <= MaxScan && body < data.Length)
                    {
                        int avail = data.Length - body;
                        int take = Math.Min(size, avail);
                        if (take >= 5 && data[body] == (byte)'<' &&
                            data[body + 1] == (byte)'r')   // "<root"
                        {
                            xml = Encoding.UTF8.GetString(data, body, take);
                            truncated = take < size;
                            return true;
                        }
                    }
                }
            }

            // 2) 兜底：老卡可能没有 sceneInfo 这一层，直接扫 <root … </root>
            int k = IndexOf(data, RootOpen, i, Math.Min(data.Length, i + MaxScan));
            if (k < 0) return false;
            int m = IndexOf(data, RootClose, k, Math.Min(data.Length, k + MaxScan));
            if (m < 0) return false;
            int len = m + RootClose.Length - k;
            xml = Encoding.UTF8.GetString(data, k, len);
            return true;
        }

        private static readonly byte[] RootOpen = { (byte)'<', (byte)'r', (byte)'o', (byte)'o', (byte)'t' };
        private static readonly byte[] RootClose = { (byte)'<', (byte)'/', (byte)'r', (byte)'o', (byte)'o', (byte)'t', (byte)'>' };

        /// <summary>pos 处如果是 MessagePack 的 str 头，返回 (字节数, 正文起点)。</summary>
        private static bool TryStrHead(byte[] data, int pos, out int size, out int body)
        {
            size = 0; body = 0;
            if (pos < 0 || pos >= data.Length) return false;
            byte b = data[pos];
            if (b >= 0xA0 && b <= 0xBF)                  // fixstr
            {
                size = b & 0x1F; body = pos + 1; return true;
            }
            if (b == 0xD9 && pos + 1 < data.Length)      // str8
            {
                size = data[pos + 1]; body = pos + 2; return true;
            }
            if (b == 0xDA && pos + 2 < data.Length)      // str16
            {
                size = (data[pos + 1] << 8) | data[pos + 2]; body = pos + 3; return true;
            }
            if (b == 0xDB && pos + 4 < data.Length)      // str32
            {
                size = (data[pos + 1] << 24) | (data[pos + 2] << 16)
                     | (data[pos + 3] << 8) | data[pos + 4];
                body = pos + 5; return true;
            }
            return false;
        }

        private static Info ParseRoot(string xml)
        {
            int head = xml.IndexOf("<root", StringComparison.Ordinal);
            if (head < 0) return null;
            int gt = xml.IndexOf('>', head);
            if (gt < 0) return null;
            string attrs = xml.Substring(head + 5, gt - head - 5);

            var info = new Info();
            info.duration = Attr(attrs, "duration", 0f);
            float ts = Attr(attrs, "timeScale", 1f);
            info.timeScale = ts > 0.0001f ? ts : 1f;

            // 数关键帧、找最后一个时间点
            int count = 0;
            float last = 0f;
            int p = 0;
            while (true)
            {
                int k = xml.IndexOf("<keyframe", p, StringComparison.Ordinal);
                if (k < 0) break;
                int t = xml.IndexOf("time=\"", k, StringComparison.Ordinal);
                if (t < 0 || t > k + 400) { p = k + 9; continue; }
                int vs = t + 6;
                int ve = xml.IndexOf('"', vs);
                if (ve < 0) break;
                float v;
                if (float.TryParse(xml.Substring(vs, ve - vs), NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out v))
                {
                    count++;
                    if (v > last) last = v;
                }
                p = ve + 1;
            }
            info.keyframes = count;
            info.lastKey = last;

            // 极少见的格式：没有 <keyframe> 标签，退回宽匹配（会把 curveKeyframe 也数进来，
            // 数字偏大但不会是 0，有动画的卡不会被当成静态卡）。
            if (count == 0)
            {
                int q = 0;
                while (true)
                {
                    int t = xml.IndexOf("time=\"", q, StringComparison.Ordinal);
                    if (t < 0) break;
                    int vs = t + 6;
                    int ve = xml.IndexOf('"', vs);
                    if (ve < 0) break;
                    float v;
                    if (float.TryParse(xml.Substring(vs, ve - vs), NumberStyles.Float,
                                       CultureInfo.InvariantCulture, out v))
                    {
                        count++;
                        if (v > last) last = v;
                    }
                    q = ve + 1;
                }
                info.keyframes = count;
                info.lastKey = last;
            }

            if (info.duration <= 0f)
            {
                // 少数卡没有 duration 属性，退化成最后一个关键帧
                if (info.lastKey > 0f) info.duration = info.lastKey;
                else return null;
            }
            return info;
        }

        private static float Attr(string attrs, string name, float def)        {
            string key = name + "=\"";
            int i = attrs.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return def;
            int vs = i + key.Length;
            int ve = attrs.IndexOf('"', vs);
            if (ve < 0) return def;
            float v;
            if (float.TryParse(attrs.Substring(vs, ve - vs), NumberStyles.Float,
                               CultureInfo.InvariantCulture, out v)) return v;
            return def;
        }

        /// <summary>字节数组里找子串（Array.IndexOf 只支持单字节）。</summary>
        private static int IndexOf(byte[] data, byte[] pat, int start)
        {
            return IndexOf(data, pat, start, data.Length);
        }

        private static int IndexOf(byte[] data, byte[] pat, int start, int end)
        {
            if (start < 0) start = 0;
            if (end > data.Length) end = data.Length;
            int last = end - pat.Length;
            byte first = pat[0];
            for (int i = start; i <= last; i++)
            {
                if (data[i] != first) continue;
                int k = 1;
                while (k < pat.Length && data[i + k] == pat[k]) k++;
                if (k == pat.Length) return i;
            }
            return -1;
        }
    }
}
