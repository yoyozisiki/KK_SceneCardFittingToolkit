using System;

namespace KKPEHeightLock
{
    /// <summary>
    /// 「各部件脱衣状态」的读写（后端）。
    ///
    /// 配置里存的是一串 "0,0,3,..."，界面每帧都要读；直接每帧解析再写回
    /// 等于每帧一次磁盘 IO（ConfigEntry 一变就落盘），所以这里缓存一份，
    /// 配置变了才重新解析。
    /// </summary>
    public static class ClothesPartSettings
    {
        private static int[] _values;
        private static string _loaded;

        public static int[] Values
        {
            get
            {
                string cfg = KKPEHeightLockPlugin.ExportClothesParts.Value ?? "";
                int n = AutoExport.ClothesKindNames.Length;
                if (_values == null || _values.Length != n || !string.Equals(_loaded, cfg, StringComparison.Ordinal))
                {
                    _loaded = cfg;
                    _values = AutoExport.ParseClothesParts(cfg, n);
                }
                return _values;
            }
        }

        /// <summary>改一个部件（-1 = 不改，0 穿 / 1 半脱 / 2 半脱2 / 3 脱）。</summary>
        public static void Set(int slot, int value)
        {
            var arr = Values;
            if (slot < 0 || slot >= arr.Length) return;
            arr[slot] = value;
            string text = AutoExport.FormatClothesParts(arr);
            _loaded = text;
            KKPEHeightLockPlugin.ExportClothesParts.Value = text;
        }
    }
}
