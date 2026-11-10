using System;
using System.Text;

namespace KKPEHeightLock
{
    /// <summary>
    /// BepInEx 的 cfg 读取器把 `\a \b \t \n \v \f \r` 当**转义序列**处理。
    /// 磁盘路径里只要出现这种组合（比如
    /// `...\chara\female\animfriend 07.png` 里的 `\a`），读回来就变成 BEL(0x07)：
    ///
    ///   * `Path.GetFileNameWithoutExtension` 直接抛
    ///     `ArgumentException: Illegal characters in path`，
    ///     IMGUI 里表现就是**整个批量导出面板全黑**（每帧都在抛，画不到任何控件）；
    ///   * 路径本身也不存在了，角色卡 / 场景卡全都「加载失败」。
    ///
    /// 对策两条，读和写都堵上：
    ///   * 读：<see cref="Read"/> 把控制字符还原成 `\a` 这样的原文，路径就恢复原样；
    ///   * 写：<see cref="ToConfig"/> 统一存成 `/`，天然不含转义序列，以后读回来也不会错。
    /// </summary>
    internal static class PathFix
    {
        /// <summary>修复从配置里读出来的路径（控制字符 → 反斜杠转义原文）。</summary>
        public static string Read(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";

            bool dirty = false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < ' ') { dirty = true; break; }
            }
            if (!dirty) return s;

            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= ' ') { sb.Append(c); continue; }
                char letter = EscapeLetter(c);
                if (letter != '\0') { sb.Append('\\').Append(letter); }
                // 认不出来的控制字符直接丢掉，别让它再污染路径
            }
            return sb.ToString();
        }

        /// <summary>写进配置前调用：先修复，再把反斜杠统一成 `/`（不会再被当转义）。</summary>
        public static string ToConfig(string s)
        {
            string r = Read(s);
            if (string.IsNullOrEmpty(r)) return "";
            // UNC 路径（//server/share）不能被压成单斜杠
            bool unc = r.StartsWith("\\\\", StringComparison.Ordinal) || r.StartsWith("//", StringComparison.Ordinal);
            string body = unc ? r.Substring(2) : r;
            body = body.Replace('\\', '/');
            return unc ? "//" + body : body;
        }

        private static char EscapeLetter(char c)
        {
            switch (c)
            {
                case '\a': return 'a';
                case '\b': return 'b';
                case '\t': return 't';
                case '\n': return 'n';
                case '\v': return 'v';
                case '\f': return 'f';
                case '\r': return 'r';
                default: return '\0';
            }
        }
    }
}
