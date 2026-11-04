using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace KKPEHeightLock
{
    public static partial class AutoExport
    {
        // ======================================================== 替换 female
        private static IEnumerator ReplaceFemale(ExportOptions o)
        {
            // 场景刚加载完时网格/材质还在构建，这时候 ChangeChara 会换不干净
            for (int i = 0; i < 6; i++) yield return null;

            Studio.Studio studio = null;
            try { studio = UnityEngine.Object.FindObjectOfType<Studio.Studio>(); }
            catch (Exception) { }
            if (studio == null) { Log("找不到 Studio 实例，跳过替换"); yield break; }

            var chars = new List<Studio.OCIChar>();
            try
            {
                if (studio.dicObjectCtrl != null)
                    foreach (var kv in studio.dicObjectCtrl)
                    {
                        var oc = kv.Value as Studio.OCIChar;
                        if (oc != null) chars.Add(oc);
                    }
            }
            catch (Exception e) { Log("枚举角色失败：" + e.Message); yield break; }

            int done = 0;
            var swapped = new List<Studio.OCIChar>();
            var scenes = new List<BodySnap>();
            var cards = new List<BodySnap>();

            foreach (var oci in chars)
            {
                try
                {
                    if (oci == null || oci.charInfo == null) continue;
                    if (oci.sex != 1) continue;                 // 只换女角色
                    var sceneBody = BodySnap.Capture(oci);
                    oci.ChangeChara(o.FemaleCard);
                    var cardBody = BodySnap.Capture(oci);
                    BodySnap.Compose(oci, sceneBody, cardBody, o);
                    swapped.Add(oci);
                    scenes.Add(sceneBody);
                    cards.Add(cardBody);
                    done++;
                }
                catch (Exception e) { Log("替换出错：" + e.Message); }
                yield return null;
            }

            // ChangeChara 内部的 Reload 是分帧收尾的：立刻写回的身材会被新卡数据盖掉，
            // 所以换完再补写 3 轮（每轮隔 2 帧）。3 轮 × 2 帧是实测的稳定下界。
            for (int round = 0; round < 3; round++)
            {
                for (int i = 0; i < swapped.Count; i++)
                    BodySnap.Compose(swapped[i], scenes[i], cards[i], o);
                for (int f = 0; f < 2; f++) yield return null;
            }

            // 身材落定之后再记身高基准（骨架刚重建完读到的不是最终值）
            if (o.LockHeight)
                for (int i = 0; i < swapped.Count; i++) BodySnap.LockHeight(swapped[i]);

            Log($"替换完成：{done} 个 female");
        }

        private static string[] _clothesKindNames;

        /// <summary>
        /// ChaFileDefine.ClothesKind 的成员名，顺序就是 SetClothesState 的 kind 参数。
        /// KKS 是 9 个：top, bottom, bra, shorts, gloves, pantyhose, socks, shoes, shoes_inner。
        /// 用 Enum.GetNames 取而不是写死 —— KK 只有 8 个，写死会越界。
        /// </summary>
        public static string[] ClothesKindNames
        {
            get
            {
                if (_clothesKindNames == null)
                {
                    try { _clothesKindNames = Enum.GetNames(typeof(ChaFileDefine.ClothesKind)); }
                    catch (Exception) { _clothesKindNames = new string[0]; }
                }
                return _clothesKindNames;
            }
        }

        /// <summary>部件名的中文显示（认不出来的就用原名）。
        /// 注意 KKS 的枚举名和 KK 不一样，实测是：
        /// top / bot / bra / shorts / gloves / panst / socks / shoes_inner / shoes_outer，
        /// 别照 KK 的 `bottom` / `pantyhose` / `shoes` 猜。</summary>
        public static string ClothesKindLabel(int i)
        {
            var names = ClothesKindNames;
            if (i < 0 || i >= names.Length) return "#" + i;
            switch (names[i].ToLowerInvariant())
            {
                case "top": return L10n.T("上衣");
                case "bot":
                case "bottom": return L10n.T("下衣");
                case "bra": return L10n.T("胸罩");
                case "shorts": return L10n.T("内裤");
                case "gloves": return L10n.T("手套");
                case "panst":
                case "pantst":
                case "pantyhose": return L10n.T("连裤袜");
                case "socks": return L10n.T("袜子");
                case "shoes": return L10n.T("鞋");
                case "shoes_inner": return L10n.T("室内鞋");
                case "shoes_outer": return L10n.T("鞋");
                default: return names[i];
            }
        }

        /// <summary>
        /// 某个部件能选哪些状态字节。不同部位不一样 —— 内裤多一个「半脱2」，
        /// 手套/袜子/鞋只有「穿 / 脱」。和 4_AutoSceneExport 的 PART_CHOICES 一致。
        /// </summary>
        public static int[] ClothesKindStates(int i)
        {
            var names = ClothesKindNames;
            string n = (i >= 0 && i < names.Length) ? names[i].ToLowerInvariant() : "";
            switch (n)
            {
                case "shorts":
                    return new[] { 0, 1, 2, 3 };
                case "gloves":
                case "socks":
                case "shoes":
                case "shoes_inner":
                case "shoes_outer":
                    return new[] { 0, 3 };
                default:
                    return new[] { 0, 1, 3 };
            }
        }

        /// <summary>状态字节 → 中文。0 穿 / 1 半脱 / 2 半脱2 / 3 脱。</summary>
        public static string ClothesStateLabel(int state)
        {
            switch (state)
            {
                case 0: return L10n.T("穿");
                case 1: return L10n.T("半脱");
                case 2: return L10n.T("半脱2");
                case 3: return L10n.T("脱");
                default: return "?";
            }
        }

        /// <summary>把配置里那串 "0,0,3,..." 解析成定长数组，缺的补 -1（不改）。</summary>
        public static int[] ParseClothesParts(string s, int count)
        {
            var arr = new int[Mathf.Max(0, count)];
            for (int i = 0; i < arr.Length; i++) arr[i] = -1;
            if (string.IsNullOrEmpty(s)) return arr;
            var bits = s.Split(',');
            for (int i = 0; i < bits.Length && i < arr.Length; i++)
            {
                int v;
                if (int.TryParse(bits[i].Trim(), out v) && v >= -1 && v <= 3) arr[i] = v;
            }
            return arr;
        }

        /// <summary>数组序列化回配置串。</summary>
        public static string FormatClothesParts(int[] arr)
        {
            if (arr == null || arr.Length == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(arr[i]);
            }
            return sb.ToString();
        }

        private static List<Studio.OCIChar> CollectFemales()
        {
            var list = new List<Studio.OCIChar>();
            try
            {
                var studio = UnityEngine.Object.FindObjectOfType<Studio.Studio>();
                if (studio == null || studio.dicObjectCtrl == null) return list;
                foreach (var kv in studio.dicObjectCtrl)
                {
                    var oc = kv.Value as Studio.OCIChar;
                    if (oc != null && oc.sex == 1 && oc.charInfo != null) list.Add(oc);
                }
            }
            catch (Exception e) { Log("枚举角色失败：" + e.Message); }
            return list;
        }

        /// <summary>
        /// 应用服装预设 + 部件脱衣状态。
        ///
        /// 顺序有讲究：SetCoordinateInfo 会把 shapeValueBody 冲回该套装的默认值，
        /// 所以先存一份身材快照，设完服装再写回去。写回用 Compose(snap, snap) ——
        /// 场景/角色卡都传同一份，不管三个来源开关怎么选，结果都等于快照本身。
        ///
        /// 另外部件状态要**在一个循环里批量设**：SetClothesState 每调一次都会让
        /// 角色重建一遍网格，9 个部件分 9 次设就是 9 轮重建，实测能吃掉好几秒。
        /// </summary>
        private static IEnumerator ApplyClothes(ExportOptions o)
        {
            bool wantOutfit = o.Outfit >= 0;
            bool wantParts = false;
            if (o.ClothesParts != null)
                for (int i = 0; i < o.ClothesParts.Length; i++)
                    if (o.ClothesParts[i] >= 0) { wantParts = true; break; }
            if (!wantOutfit && !wantParts) yield break;

            var chars = CollectFemales();
            if (chars.Count == 0)
            {
                Log("[提示] 场景里没有 female，跳过服装设置");
                yield break;
            }

            var snaps = new List<BodySnap>();
            for (int i = 0; i < chars.Count; i++) snaps.Add(BodySnap.Capture(chars[i]));

            int nCoord = 0, nParts = 0;
            for (int i = 0; i < chars.Count; i++)
            {
                var oc = chars[i];
                if (wantOutfit)
                {
                    try
                    {
                        oc.SetCoordinateInfo((ChaFileDefine.CoordinateType)o.Outfit, true);
                        nCoord++;
                    }
                    catch (Exception e) { Log("切换套装失败：" + e.Message); }
                }
                if (wantParts)
                {
                    for (int p = 0; p < o.ClothesParts.Length; p++)
                    {
                        int st = o.ClothesParts[p];
                        if (st < 0) continue;
                        try
                        {
                            oc.SetClothesState(p, (byte)st);
                            nParts++;
                        }
                        catch (Exception e) { Log("设置部件状态失败：" + e.Message); }
                    }
                }
                yield return null;
            }

            if (nCoord > 0) Log("已切换服装预设 #" + o.Outfit + "（" + nCoord + " 个 female）");
            if (nParts > 0) Log("已设置部件状态 " + nParts + " 处");

            // 套装切换/换装是分帧重建网格的，等它收尾再把身材写回去
            for (int f = 0; f < 6; f++) yield return null;
            for (int i = 0; i < chars.Count; i++)
                if (snaps[i] != null) BodySnap.Compose(chars[i], snaps[i], snaps[i], o);
        }



        private static IEnumerator ConcatVideos(List<string> files, string output, Action<bool> done)
        {
            done(false);
            string ffmpeg = FfmpegPath();
            if (string.IsNullOrEmpty(ffmpeg)) { Log("找不到 ffmpeg，无法合并"); yield break; }

            string listPath = Path.Combine(Path.GetDirectoryName(output),
                "concat_" + DateTime.Now.ToString("HHmmss") + ".txt");
            var sb = new StringBuilder();
            for (int i = 0; i < files.Count; i++)
            {
                if (!File.Exists(files[i])) continue;
                sb.AppendLine("file '" + files[i].Replace("'", "'\\''") + "'");
            }
            try { File.WriteAllText(listPath, sb.ToString(), new UTF8Encoding(false)); }
            catch (Exception e) { Log("写合并列表失败：" + e.Message); yield break; }

            string args = "-y -hide_banner -loglevel error -f concat -safe 0 -i " + Q(listPath)
                        + " -c copy -movflags +faststart " + Q(output);
            bool ok = false;
            IEnumerator run = RunProcess(ffmpeg, args, delegate(bool r) { ok = r; });
            while (run.MoveNext()) yield return run.Current;

            // 各段参数不一致（改过窗口尺寸 / 中途换过编码设置）时 -c copy 会失败，
            // 退一步重编码，慢但一定能合上。
            if (!ok || !File.Exists(output) || FileSize(output) == 0)
            {
                Log("直接拼接失败，改用重编码合并…");
                try { if (File.Exists(output)) File.Delete(output); } catch (Exception) { }
                string args2 = "-y -hide_banner -loglevel error -f concat -safe 0 -i " + Q(listPath)
                             + " -c:v libx264 -preset medium -crf 16 -pix_fmt yuv420p"
                             + " -movflags +faststart " + Q(output);
                bool ok2 = false;
                IEnumerator run2 = RunProcess(ffmpeg, args2, delegate(bool r) { ok2 = r; });
                while (run2.MoveNext()) yield return run2.Current;
                ok = ok2;
            }

            try { File.Delete(listPath); } catch (Exception) { }
            done(ok && File.Exists(output));
        }

        /// <summary>
        /// 拼 ffmpeg 的 -vf 串：泛光 → 暗角 → 淡入淡出（顺序和 6_视频后期 一致）。
        /// RealtimeFx 打开时 EffectiveBloom/EffectiveVignette 会返回 Off，
        /// 因为这两样已经由 ReShade 在 GPU 上实时做完了，这里只剩淡入淡出。
        /// </summary>

        /// <summary>
        /// 开了实时效果之后，把下拉框选的强度写进 ReShade 预设
        /// （`&lt;游戏根目录&gt;/KK_BloomVignette.ini`），这样界面选「中」就真的是中，
        /// 不用再开 ReShade 面板手调。ReShade 会监视预设文件，改完自己重载。
        /// 映射关系照 KK_BloomVignette.fx 里的注释：
        ///   暗角 VignetteStrength = (1 - cos(PI/n)^4) / 0.85
        /// 其中 n 就是 ffmpeg vignette 那个 PI/n 的 n，所以两边曲线是同一族。
        /// </summary>
        // ---- ReShade 预设路径（SyncReshadePreset 用）
        private static string _rsPath;

        /// <summary>
        /// ReShade 到底会读哪个预设文件。`ReShade.ini` 里的 `PresetPath` 才是
        /// 真正生效的那一个。
        ///
        /// 踩过的坑：以前硬写 `&lt;根目录&gt;/KK_BloomVignette.ini`，但 ReShade.ini 的
        /// `PresetPath` 指着 `ReShadePreset.ini`（那个文件根本不存在），于是
        /// 我们写进去的泛光/暗角一条都没生效 —— 成片里既没暗角也没泛光，
        /// 打开 ReShade 面板看，technique 是**没勾上**的。所以必须先问
        /// ReShade.ini 要路径。
        /// </summary>
        private static string ResolveReshadePresetPath()
        {
            string root = BepInEx.Paths.GameRootPath;
            string fallback = Path.Combine(root, "KK_BloomVignette.ini");
            try
            {
                string ini = Path.Combine(root, "ReShade.ini");
                if (!File.Exists(ini)) return fallback;
                string[] lines = File.ReadAllLines(ini);
                for (int i = 0; i < lines.Length; i++)
                {
                    string t = lines[i].Trim();
                    if (!t.StartsWith("PresetPath=", StringComparison.OrdinalIgnoreCase)) continue;
                    string v = t.Substring("PresetPath=".Length).Trim();
                    if (v.Length == 0) break;
                    if (v.StartsWith(".\\", StringComparison.Ordinal)) v = v.Substring(2);
                    else if (v.StartsWith("./", StringComparison.Ordinal)) v = v.Substring(2);
                    if (!Path.IsPathRooted(v)) v = Path.Combine(root, v);
                    return v;
                }
            }
            catch (Exception) { }
            return fallback;
        }

        /// <summary>
        /// 把泛光/暗角参数并进当前预设。**不能整文件覆盖** —— 用户预设里
        /// 可能还有别的效果，覆盖掉等于把人家的配置删了。
        /// 做法：保留原文，只保证 `Techniques=` 里有我们这一条，
        /// 再把 `[KK_BloomVignette.fx]` 这一段替换成新值。
        /// </summary>
        private static void WriteReshadePreset(string path, float thr, float intensity,
                                               float radius, float vig)
        {
            const string Effect = "KK_BloomVignette.fx";
            const string Tech = "KK_BloomVignette@" + Effect;

            var lines = new List<string>();
            if (File.Exists(path))
            {
                try { lines.AddRange(File.ReadAllLines(path)); }
                catch (Exception) { }
            }

            bool hasTech = false, hasSort = false;
            for (int i = 0; i < lines.Count; i++)
            {
                string s = lines[i];
                if (s.StartsWith("Techniques=", StringComparison.OrdinalIgnoreCase))
                {
                    if (s.IndexOf(Tech, StringComparison.OrdinalIgnoreCase) < 0) lines[i] = s + "," + Tech;
                    hasTech = true;
                }
                else if (s.StartsWith("TechniqueSorting=", StringComparison.OrdinalIgnoreCase))
                {
                    if (s.IndexOf(Tech, StringComparison.OrdinalIgnoreCase) < 0) lines[i] = s + "," + Tech;
                    hasSort = true;
                }
            }
            if (!hasTech) lines.Add("Techniques=" + Tech);
            if (!hasSort) lines.Add("TechniqueSorting=" + Tech);

            int start = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Trim().Equals("[" + Effect + "]", StringComparison.OrdinalIgnoreCase))
                { start = i; break; }
            }
            if (start >= 0)
            {
                int end = start + 1;
                while (end < lines.Count)
                {
                    string t = lines[end].Trim();
                    if (t.Length > 2 && t[0] == '[' && t[t.Length - 1] == ']') break;
                    end++;
                }
                lines.RemoveRange(start, end - start);
            }

            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0)
                lines.RemoveAt(lines.Count - 1);
            lines.Add("");
            lines.Add("[" + Effect + "]");
            lines.Add("BloomThreshold=" + thr.ToString("0.000000", CultureInfo.InvariantCulture));
            lines.Add("BloomIntensity=" + intensity.ToString("0.000000", CultureInfo.InvariantCulture));
            lines.Add("BloomRadius=" + radius.ToString("0.000000", CultureInfo.InvariantCulture));
            lines.Add("VignetteStrength=" + vig.ToString("0.000000", CultureInfo.InvariantCulture));

            File.WriteAllText(path, string.Join("\r\n", lines.ToArray()) + "\r\n",
                new UTF8Encoding(false));
        }

        /// <summary>
        /// 淡入淡出：一律在屏上盖黑遮罩，交给 ReShade 是**做不到**的。
        ///
        /// 试过让 shader 里加个 `Fade` uniform、导出时逐帧改写预设文件：
        /// 实测 ReShade 6.8 **只在启动时读一次预设**，运行时改文件不会重载
        /// （改完 Fade=1 再截图，画面一点没变，ReShade.log 里也没有重新编译）。
        /// 所以逐帧改文件这条路是死的，只能退回屏上遮罩。
        /// </summary>
        private static void ApplyFade(float a)
        {
            SetFadeBlack(a);
        }

        public static void SyncReshadePreset(ExportOptions o)
        {
            _rsPath = null;
            if (!o.RealtimeFx) return;
            string path = null;
            try
            {
                path = ResolveReshadePresetPath();

                float thr = 1f, intensity = 0f, radius = 0f;
                switch (o.Bloom)
                {
                    case FxLevel.Weak: thr = 0.72f; intensity = 0.22f; radius = 3f; break;
                    case FxLevel.Medium: thr = 0.62f; intensity = 0.35f; radius = 4f; break;
                    case FxLevel.Strong: thr = 0.52f; intensity = 0.50f; radius = 6f; break;
                }

                float vig = 0f;
                switch (o.Vignette)
                {
                    case FxLevel.Weak: vig = 0.44f; break;   // n=6.6 → 四角留 62%
                    case FxLevel.Medium: vig = 0.62f; break; // n=5.3 → 四角留 47%
                    case FxLevel.Strong: vig = 0.87f; break; // n=4.05 → 四角留 26%
                }

                WriteReshadePreset(path, thr, intensity, radius, vig);
                _rsPath = path;

                Log($"[实时效果] 已同步 ReShade 预设：泛光={FxName(o.Bloom)}（阈值 {F(thr)}/强度 {F(intensity)}/半径 {F(radius)}）"
                    + $"，暗角={FxName(o.Vignette)}（强度 {F(vig)}）"
                    + "　文件=" + path);
                Log("[实时效果] ReShade 只在启动时读预设，这次改的强度要下次开游戏才生效");
            }
            catch (Exception e)
            {
                Log("[实时效果] 写 ReShade 预设失败：" + e.Message + (path != null ? "（" + path + "）" : ""));
            }
        }

        /// <summary>日志里那一行「后期：…」。现在只有 ReShade 实时一条路。</summary>
        private static string DescribeFx(ExportOptions o)
        {
            var bits = new List<string>();
            if (o.FadeSeconds > 0f) bits.Add("淡入淡出 " + F(o.FadeSeconds) + "s（直出，不后期）");
            var live = new List<string>();
            if (o.Vignette != FxLevel.Off) live.Add("暗角=" + FxName(o.Vignette));
            if (o.Bloom != FxLevel.Off) live.Add("泛光=" + FxName(o.Bloom));
            bits.Add("ReShade 实时" + (live.Count > 0 ? "（" + string.Join("、", live.ToArray()) + "）" : "（无）"));
            bits.Add("截图=Reshade");
            bits.Add("后期=跳过（直接出片，不重编码）");
            return string.Join("，", bits.ToArray());
        }

        private static string FxName(FxLevel l)
        {
            switch (l)
            {
                case FxLevel.Weak: return "弱";
                case FxLevel.Medium: return "中";
                case FxLevel.Strong: return "强";
                default: return "关";
            }
        }

        private static string F(float v)
        {
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
