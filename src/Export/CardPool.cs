using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KKPEHeightLock
{
    /// <summary>
    /// 导出的角色卡池（后端）：主角色卡 1 张 + 次角色卡一张有序列表。
    ///
    /// 界面只做「选/清空/调顺序」这些动作，存储格式、编号、去重、读文件夹
    /// 全在这里 —— 以前这些塞在 UI 的 partial class 里，UI 一改就得连着测数据。
    ///
    /// 存法：cfg 里 `|` 分隔的绝对路径串，顺序就是编号顺序（1、2、3…）。
    /// </summary>
    public static class CardPool
    {
        private static readonly List<string> _subs = new List<string>();
        private static string _subsRaw;

        /// <summary>主角色卡（只替换第一个 female）。空 = 不区分主次。</summary>
        public static string Main
        {
            get { return PathFix.Read(KKPEHeightLockPlugin.ExportFemaleCard.Value); }
        }

        public static void SetMain(string path)
        {
            KKPEHeightLockPlugin.ExportFemaleCard.Value = PathFix.ToConfig(path ?? "");
        }

        public static void ClearMain()
        {
            KKPEHeightLockPlugin.ExportFemaleCard.Value = "";
        }

        /// <summary>次角色卡（顺序 = 编号）。返回的是内部列表，别改它，要改走下面的方法。</summary>
        public static List<string> Subs
        {
            get
            {
                string raw = KKPEHeightLockPlugin.ExportSubCards.Value ?? "";
                if (!string.Equals(raw, _subsRaw, StringComparison.Ordinal))
                {
                    _subsRaw = raw;
                    _subs.Clear();
                    if (raw.Length > 0)
                        foreach (var p in raw.Split('|'))
                        {
                            string s = PathFix.Read(p.Trim());
                            if (s.Length > 0) _subs.Add(s);
                        }
                }
                return _subs;
            }
        }

        public static int Count { get { return Subs.Count; } }

        /// <summary>次卡替换顺序：false = 数字优先，true = 随机。</summary>
        public static bool RandomOrder
        {
            get { return KKPEHeightLockPlugin.ExportSubRandom.Value; }
            set { KKPEHeightLockPlugin.ExportSubRandom.Value = value; }
        }

        public static void SetSubs(List<string> list)
        {
            var sb = new StringBuilder();
            if (list != null)
                for (int i = 0; i < list.Count; i++)
                {
                    if (string.IsNullOrEmpty(list[i])) continue;
                    if (sb.Length > 0) sb.Append('|');
                    sb.Append(PathFix.ToConfig(list[i]));
                }
            _subsRaw = null;      // 下次访问重新解析
            KKPEHeightLockPlugin.ExportSubCards.Value = sb.ToString();
        }

        public static void ClearSubs()
        {
            SetSubs(null);
        }

        /// <summary>这张卡在次卡里的编号（1 起）；0 = 没被选中。</summary>
        public static int Number(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            var list = Subs;
            for (int i = 0; i < list.Count; i++)
                if (SamePath(list[i], path)) return i + 1;
            return 0;
        }

        /// <summary>选中/取消一张次卡（多选弹框里点一下就调这个）。</summary>
        public static void ToggleSub(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var list = new List<string>(Subs);
            int idx = -1;
            for (int i = 0; i < list.Count; i++)
                if (SamePath(list[i], path)) { idx = i; break; }
            if (idx >= 0) list.RemoveAt(idx);
            else list.Add(path);
            SetSubs(list);
        }

        /// <summary>
        /// 把一个角色卡文件夹（含子目录）里的卡全部加进次卡列表，去重、保持原顺序。
        /// dir 可以是绝对路径，也可以是相对 UserData/chara/female 的路径（"" = 根）。
        /// 返回真正新加的张数。
        /// </summary>
        public static int AddFolder(string dir)
        {
            int added = 0;
            try
            {
                string root = Path.Combine(UserData.Path, "chara", "female");
                string full = string.IsNullOrEmpty(dir) ? root
                            : (Path.IsPathRooted(dir) ? dir : Path.Combine(root, dir));

                if (!Directory.Exists(full))
                {
                    KKPEHeightLockPlugin.Log.LogWarning("[角色卡池] 文件夹不存在：" + full);
                    return 0;
                }

                var files = new List<string>(Directory.GetFiles(full, "*.png", SearchOption.AllDirectories));
                files.Sort(delegate(string a, string b)
                {
                    return NaturalSort.Compare(Path.GetFileName(a), Path.GetFileName(b));
                });

                var list = new List<string>(Subs);
                for (int i = 0; i < files.Count; i++)
                {
                    bool dup = false;
                    for (int k = 0; k < list.Count; k++)
                        if (SamePath(list[k], files[i])) { dup = true; break; }
                    if (dup) continue;
                    list.Add(files[i]);
                    added++;
                }
                SetSubs(list);
                KKPEHeightLockPlugin.Log.LogMessage(
                    $"[角色卡池] 从文件夹加入 {added} 张（{full}），次卡共 {list.Count} 张");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("[角色卡池] 读文件夹失败：" + e.Message);
            }
            return added;
        }

        /// <summary>路径比较：斜杠方向不敏感、大小写不敏感。</summary>
        public static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(a.Replace('\\', '/'), b.Replace('\\', '/'),
                                 StringComparison.OrdinalIgnoreCase);
        }
    }
}
