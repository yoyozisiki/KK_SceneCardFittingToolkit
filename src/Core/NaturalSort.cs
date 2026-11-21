using System;

namespace KKPEHeightLock
{
    /// <summary>
    /// 自然排序：「场景10」排在「场景9」之后，而不是排到「场景1」旁边。
    ///
    /// 以前场景卡、随机换人、Timeline 读取各写了一份完全一样的实现，
    /// 现在统一到这里 —— 三处排序结果必须一致，否则同一个目录在两处显示的顺序会不一样。
    /// </summary>
    public static class NaturalSort
    {
        public static int Compare(string a, string b)
        {
            if (a == null) a = "";
            if (b == null) b = "";
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    long va = 0, vb = 0;
                    try
                    {
                        va = long.Parse(a.Substring(si, i - si));
                        vb = long.Parse(b.Substring(sj, j - sj));
                    }
                    catch (Exception) { }
                    if (va != vb) return va < vb ? -1 : 1;
                }
                else
                {
                    if (a[i] != b[j]) return a[i] < b[j] ? -1 : 1;
                    i++; j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
