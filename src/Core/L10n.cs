using System;
using System.Collections.Generic;
using System.Globalization;

namespace KKPEHeightLock
{
    public enum UiLang
    {
        Auto = 0,
        Chinese = 1,
        English = 2,
    }

    /// <summary>
    /// 极简文案表：key 就是中文原文，查不到原样回退中文（漏翻不会变空白）。
    /// 英文表见 L10n.Table.cs。只翻界面文案，日志保持中文方便排查。
    /// </summary>
    public static partial class L10n
    {
        /// <summary>当前是否输出英文（由 UiLang + 系统语言决定）。</summary>
        public static bool IsEnglish { get; private set; }

        /// <summary>每次切换语言 +1。缓存了译文数组的地方用它判断要不要重建。</summary>
        public static int Version { get; private set; }

        /// <summary>按配置刷新语言。Auto = 跟随系统 UI 语言。</summary>
        public static void Refresh(UiLang lang)
        {
            Version++;
            switch (lang)
            {
                case UiLang.Chinese:
                    IsEnglish = false;
                    break;
                case UiLang.English:
                    IsEnglish = true;
                    break;
                default:
                    IsEnglish = !SystemIsChinese();
                    break;
            }
        }

        private static bool SystemIsChinese()
        {
            try
            {
                string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                return string.Equals(two, "zh", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return true;   // 读不到就当中文，至少原文能看懂
            }
        }

        /// <summary>翻译一条界面文案。查不到就原样返回。</summary>
        public static string T(string zh)
        {
            if (!IsEnglish || string.IsNullOrEmpty(zh)) return zh;
            string en;
            return Table.TryGetValue(zh, out en) ? en : zh;
        }

        /// <summary>语言下拉框的显示名（本身不翻译）。</summary>
        public static readonly string[] LanguageNames = { "自动 / Auto", "中文", "English" };
    }
}
