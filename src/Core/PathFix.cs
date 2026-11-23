using System;
using System.Text;

namespace KKPEHeightLock
{
    /// <summary>
    /// BepInEx 读 cfg 时会把 `\a \b \t \n` 之类当转义序列，磁盘路径里的这种组合
    /// （如 `\chara\female\...` 里的 `\a`）读回来会变成控制字符，导致路径失效、
    /// Path.GetFileName 抛 Illegal characters in path。
    /// 对策：读时把控制字符还原成 `\a` 原文；写时统一存成 `/`，以后不会再被转义。
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
