using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
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
                    // 换人只保证服装换掉，饰品槽的显隐会被原角色/AccStateSync 继承下来，
                    // 而不少卡把头发做成饰品（"Accessories: 01 lulum hair"）→ 成片光头。
                    // 所以换完先全部显示，卡片自己标了隐藏的再按卡还愿。
                    ShowAllAccessories(oci);
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

        /// <summary>
        /// 换人后把所有饰品槽恢复成「显示」。
        ///
        /// 为什么需要：不少角色卡直接拿饰品当头发用（卡片里写着 "Accessories: 01 lulum hair"），
        /// 而 ChangeChara 之后饰品槽的显隐会被原角色 / AccStateSync 这类插件继承下来 ——
        /// 原场景角色身上关掉的槽，新角色的同槽位也跟着关，发型饰品被一起藏掉，
        /// 成片里就是光头、只剩下衣服。
        ///
        /// 所以换完人默认全部打开；角色卡自己把这些槽标成「隐藏」的，再按卡还原回去，
        /// 保证「卡里想藏的还能藏，只是不再被场景/旧角色带着一起藏」。
        /// </summary>
        private static void ShowAllAccessories(Studio.OCIChar oci)
        {
            try
            {
                var ci = oci != null ? oci.charInfo : null;
                if (ci == null) return;

                var coord = ci.nowCoordinate;
                var acc = coord != null ? coord.accessory : null;
                var parts = acc != null ? acc.parts : null;

                // 1) 先全部显示（解决「饰品当头发」的卡换人后变光头）
                ci.SetAccessoryStateAll(true);

                // 2) 再按当前衣服状态收一遍：上衣半脱时挂在衣服上的饰品（蝴蝶结之类）
                //    要跟着藏起来，不能全亮着悬空。这一步交给 AccStateSync 自己算，
                //    它存着每个饰品跟哪件衣服、哪个状态绑定的规则。
                int synced = ApplyClothingStateToAccessories(ci);

                // 3) 卡片自己标了「隐藏」的槽，按卡还原
                int hidden = 0;
                if (parts != null)
                {
                    for (int i = 0; i < parts.Length; i++)
                    {
                        var part = parts[i];
                        if (part == null) continue;
                        if (Convert.ToInt32(part.hideCategory) == 0) continue;
                        try { ci.SetAccessoryState(i, false); hidden++; }
                        catch (Exception) { }
                    }
                }

                Log($"换人后饰品处理：全部显示 → 按衣服状态同步{(synced > 0 ? "（AccStateSync 生效）" : "（未找到 AccStateSync）")}"
                    + $" → 按卡还原隐藏 {hidden} 个（共 {(parts != null ? parts.Length : 0)} 槽）");
            }
            catch (Exception e) { Log("[提示] 显示全部饰品失败：" + e.Message); }
        }

        /// <summary>
        /// 让 AccStateSync 按「当前衣服状态」把所有饰品重算一遍显隐。
        ///
        /// 为什么需要：把饰品全部打开是为了救「饰品当头发」的卡（否则换人后光头），
        /// 但一刀切全开会把跟衣服联动的饰品也点亮 —— 上衣半脱时蝴蝶结还悬在半空。
        /// AccStateSync 里存着「哪个饰品跟哪件衣服的哪个状态绑定」的规则，
        /// 调用它的 SyncAllAccToggle 就等价于用户手动改衣服状态时它自动重算的那一下。
        ///
        /// 全部走反射：AccStateSync 是可选依赖，没装就只保留「全部显示」，
        /// 不影响导出。返回 1 表示成功重算。
        /// </summary>
        private static int ApplyClothingStateToAccessories(ChaControl ci)
        {
            if (ci == null) return 0;
            try
            {
                // ---- 1) 找 AccStateSync 的入口类型（先按全名，再按程序集扫）
                var t = HarmonyLib.AccessTools.TypeByName("AccStateSync.AccStateSync");
                if (t == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var an = asm.GetName().Name;
                        if (an == null || an.IndexOf("AccStateSync", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        t = asm.GetType("AccStateSync.AccStateSync");
                        if (t != null) break;
                    }
                }
                if (t == null)
                {
                    Log("[提示] 场景里没加载 AccStateSync（找不到 AccStateSync.AccStateSync 类型），饰品只能全部显示");
                    return 0;
                }

                // ---- 2) 拿这个角色的状态控制器
                object ctrl = null;
                var mGet = t.GetMethod("GetController",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (mGet != null)
                {
                    try { ctrl = mGet.Invoke(null, new object[] { ci }); }
                    catch (Exception e) { Log("[提示] AccStateSync.GetController 抛错：" + e.Message); }
                }
                else Log("[提示] AccStateSync 里没有 GetController 方法");

                // 兜底：控制器本身就是挂在角色身上的 MonoBehaviour，直接按类型名找（含子物体）
                if (ctrl == null)
                {
                    var comps = ci.GetComponentsInChildren<MonoBehaviour>(true);
                    for (int i = 0; i < comps.Length; i++)
                    {
                        var c = comps[i];
                        if (c == null) continue;
                        if (c.GetType().Name == "AccStateSyncController") { ctrl = c; break; }
                    }
                }
                if (ctrl == null)
                {
                    Log("[提示] 这个角色身上没有 AccStateSync 控制器（插件没对它生效），跳过按衣服状态同步");
                    return 0;
                }

                var ct = ctrl.GetType();
                const BindingFlags Inst =
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                // ---- 3) 触发系统没开的话同步是空转，先确保它是开的
                var fTrigger = ct.GetField("TriggerEnabled", Inst);
                if (fTrigger != null && fTrigger.FieldType == typeof(bool) &&
                    !(bool)fTrigger.GetValue(ctrl))
                {
                    fTrigger.SetValue(ctrl, true);
                    Log("[提示] AccStateSync 触发系统原本是关的，已临时打开");
                }

                // ---- 4) 直接按卡里的规则算，不再指望插件自己动
                // 前面试过 SyncAllAccToggle / SetGroupState / 制造衣服状态变化，
                // 都不动饰品（大概率它的钩子只处理面板交互那条路径）。这里改成
                // 读它的 TriggerPropertyList，自己按「当前每个衣服部件的状态」算每个
                // 饰品槽该不该显示：
                //   规则 = { Coordinate, Slot, RefKind, RefState, Visible, Priority }
                //     · RefKind 是衣服部件编号（0 上衣 / 1 下衣 / 2 胸罩 …）
                //     · RefState 是该部件的一个状态（0=on, 1=1/2, 2=off）
                //     · 当前 RefKind 的状态等于 RefState 时，这条规则生效；
                //       同一个槽有多条生效时取 Priority 最大的那条。
                var states = ci.fileStatus != null ? ci.fileStatus.clothesState : null;
                if (states == null) return 0;

                int curCoord = -1;
                try
                {
                    var fs = ci.fileStatus;
                    var pr = fs != null ? fs.GetType().GetProperty("coordinateType") : null;
                    if (pr != null) curCoord = Convert.ToInt32(pr.GetValue(fs, null));
                }
                catch (Exception) { }

                var fList = ct.GetField("TriggerPropertyList", Inst);
                var raw = fList != null ? fList.GetValue(ctrl) as IEnumerable : null;
                if (raw == null)
                {
                    Log("[提示] 拿不到 AccStateSync 的 TriggerPropertyList，饰品无法按衣服状态收");
                    return 0;
                }

                // 按槽位归堆（只留当前服装槽的规则）
                var bySlot = new Dictionary<int, List<object>>();
                int total = 0;
                foreach (var p in raw)
                {
                    if (p == null) continue;
                    total++;
                    try
                    {
                        var pt = p.GetType();
                        int coord = Convert.ToInt32(pt.GetProperty("Coordinate").GetValue(p, null));
                        if (curCoord >= 0 && coord != curCoord) continue;
                        int slot = Convert.ToInt32(pt.GetProperty("Slot").GetValue(p, null));
                        List<object> lst;
                        if (!bySlot.TryGetValue(slot, out lst)) { lst = new List<object>(); bySlot[slot] = lst; }
                        lst.Add(p);
                    }
                    catch (Exception) { }
                }

                int changed = 0, hiddenCount = 0;
                var sb = new System.Text.StringBuilder();
                foreach (var kv in bySlot)
                {
                    int slot = kv.Key;
                    object best = null;
                    int bestPri = int.MinValue;
                    foreach (var p in kv.Value)
                    {
                        try
                        {
                            var pt = p.GetType();
                            int refKind = Convert.ToInt32(pt.GetProperty("RefKind").GetValue(p, null));
                            int refState = Convert.ToInt32(pt.GetProperty("RefState").GetValue(p, null));
                            if (refKind < 0 || refKind >= states.Length) continue;
                            if (states[refKind] != refState) continue;      // 这条规则对当前状态不适用
                            int pri = Convert.ToInt32(pt.GetProperty("Priority").GetValue(p, null));
                            if (pri > bestPri) { bestPri = pri; best = p; }
                        }
                        catch (Exception) { }
                    }
                    if (best == null) continue;

                    bool visible = (bool)best.GetType().GetProperty("Visible").GetValue(best, null);
                    try
                    {
                        ci.SetAccessoryState(slot, visible);
                        changed++;
                        if (!visible) hiddenCount++;
                        // 只把前 10 个槽的结果写进日志，方便对面板
                        if (slot < 10) sb.Append(" 槽" + (slot + 1) + "=" + (visible ? "on" : "off"));
                    }
                    catch (Exception) { }
                }

                // 界面上那一排开关也对齐一下（纯 UI）
                var mSync = ct.GetMethod("SyncAllAccToggle", Inst);
                if (mSync != null)
                {
                    try { mSync.Invoke(ctrl, new object[] { "AutoExport" }); }
                    catch (Exception) { }
                }

                Log("[饰品] 规则表共 " + total + " 条，按当前衣服状态生效 " + changed
                    + " 个槽（藏起 " + hiddenCount + " 个，服装槽 #" + curCoord + "）"
                    + (sb.Length > 0 ? "：" + sb.ToString() : ""));
                return changed;
            }
            catch (Exception e)
            {
                Log("[提示] 按衣服状态同步饰品失败：" + e.GetType().Name + " " + e.Message);
                return 0;
            }
        }

        /// <summary>
        /// 关掉场景里「独立动态骨骼碰撞体」（[J694] Dynamic Bone Collider 这类）对
        /// **female 衣服 / 头发 / 饰品**的吸附效果。
        ///
        /// 手动操作是：选中 Dynamic Bone Collider → KKPE 高级模式 → 把 female 的
        /// 衣服 / 头发那一排 on 逐个取消。这里调 KKPE 的单点接口
        /// <c>CollidersEditor.SetIgnoreDynamicBone(collider, 角色控制器, 动骨, true)</c>，
        /// 只断 female 的这三类部件：
        ///   · male 的动骨、角色本体/胸部动骨一律不动 —— 之前用
        ///     SetIgnoreAllDynamicBones 一刀切，把 male 的节点也断掉了，这里改回来；
        ///   · 碰撞体只处理挂在道具上的独立碰撞体（_isLoneCollider）。
        /// </summary>
        private static int TurnOffDynamicBoneColliders()
        {
            int colliderCount = 0, boneCount = 0, noEditor = 0, scanned = 0;
            try
            {
                var pcType = HarmonyLib.AccessTools.TypeByName("HSPE.PoseController");
                var ceType = HarmonyLib.AccessTools.TypeByName("HSPE.AMModules.CollidersEditor");
                var targetType = HarmonyLib.AccessTools.TypeByName("HSPE.AMModules.GenericOCITarget");
                if (pcType == null || ceType == null || targetType == null)
                {
                    Log("[提示] 没找到 KKPE 的碰撞体模块，跳过关闭吸附");
                    return 0;
                }

                const BindingFlags Inst =
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                const BindingFlags Stat =
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

                var fControllers = pcType.GetField("_poseControllers", Stat);
                var fTarget = pcType.GetField("_target", Inst);
                var fEditor = pcType.GetField("_collidersEditor", Inst);
                var fOciItem = targetType.GetField("ociItem", Inst);
                var fOciChar = targetType.GetField("ociChar", Inst);
                var fCollider = ceType.GetField("_colliderTarget", Inst);
                var fLone = ceType.GetField("_isLoneCollider", Inst);
                var mIgnore = ceType.GetMethod("SetIgnoreDynamicBone", Inst);

                if (fControllers == null || fTarget == null || fEditor == null || fOciItem == null ||
                    fOciChar == null || fCollider == null || mIgnore == null)
                {
                    Log("[提示] KKPE 碰撞体接口字段对不上，跳过关闭吸附");
                    return 0;
                }

                var box = fControllers.GetValue(null) as IEnumerable;
                if (box == null) return 0;

                var editors = new List<object>();                        // 独立碰撞体的 CollidersEditor
                var colliders = new List<object>();                      // 一一对应的 DynamicBoneCollider
                var charPc = new Dictionary<Studio.OCIChar, object>();   // 角色 → 它的 PoseController

                foreach (var entry in box)
                {
                    object pc = entry;
                    // _poseControllers 可能是 List 也可能是 Dictionary，字典项取 Value
                    if (pc != null && pc.GetType().IsGenericType &&
                        pc.GetType().GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                        pc = pc.GetType().GetProperty("Value").GetValue(pc, null);
                    if (pc == null) continue;

                    scanned++;
                    var target = fTarget.GetValue(pc);
                    if (target == null) continue;

                    // 角色：先记下它的控制器，稍后按 female 逐个处理
                    var ociChar = fOciChar.GetValue(target) as Studio.OCIChar;
                    if (ociChar != null) { charPc[ociChar] = pc; continue; }

                    // 道具：只认「独立碰撞体」
                    if (fOciItem.GetValue(target) as Studio.OCIItem == null) continue;

                    var editor = fEditor.GetValue(pc);
                    if (editor == null) { noEditor++; continue; }

                    var collider = fCollider.GetValue(editor);
                    if (collider == null) continue;
                    if (fLone != null && !(bool)fLone.GetValue(editor)) continue;

                    editors.Add(editor);
                    colliders.Add(collider);
                    colliderCount++;
                }

                // 只断 female 的衣服 / 头发 / 饰品；male 与角色本体的动骨一律不动
                foreach (var kv in charPc)
                {
                    var oci = kv.Key;
                    if (oci == null || oci.sex != 1) continue;              // 1 = female
                    var ci = oci.charInfo;
                    if (ci == null) continue;

                    var parts = new List<GameObject>();
                    AddIfAny(parts, ci.objHair);
                    AddIfAny(parts, ci.objClothes);
                    AddIfAny(parts, ci.objAccessory);

                    var bones = new List<object>();
                    for (int i = 0; i < parts.Count; i++)
                    {
                        var go = parts[i];
                        if (go == null) continue;
                        foreach (var d in go.GetComponentsInChildren<DynamicBone>(true)) bones.Add(d);
                        foreach (var d in go.GetComponentsInChildren<DynamicBone_Ver02>(true)) bones.Add(d);
                    }
                    if (bones.Count == 0) { continue; }

                    for (int e = 0; e < editors.Count; e++)
                    {
                        for (int b = 0; b < bones.Count; b++)
                        {
                            try
                            {
                                mIgnore.Invoke(editors[e],
                                    new object[] { colliders[e], kv.Value, bones[b], true });
                                boneCount++;
                            }
                            catch (Exception) { }
                        }
                    }
                }

                Log("[碰撞体] 已关闭 " + colliderCount + " 个独立碰撞体对 female 的吸附"
                    + "（衣服/头发/饰品动骨 " + boneCount + " 处，扫了 " + scanned + " 个 KKPE 对象"
                    + (noEditor > 0 ? "，" + noEditor + " 个道具没生成 KKPE 编辑器" : "")
                    + "；male 与本体动骨未改动）");
            }
            catch (Exception e) { Log("[提示] 关闭动态骨骼碰撞体吸附失败：" + e.Message); }
            return boneCount;
        }

        /// <summary>把数组里非空的 GameObject 收进列表（objHair / objClothes / objAccessory 用）。</summary>
        private static void AddIfAny(List<GameObject> list, GameObject[] arr)
        {
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] != null) list.Add(arr[i]);
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

        // ============================================================ ReShade
        // 本插件**只读不写** ReShade 预设。理由见 CheckReshadePreset 的注释。

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
        /// 淡入淡出：一律在屏上盖黑遮罩，交给 ReShade 是**做不到**的。
        ///
        /// 试过让 shader 里加个 `Fade` uniform、导出时逐帧改写预设文件：
        /// 画质一点没变（ReShade.log 里也没有重新编译），逐帧改文件这条路是死的，
        /// 只能退回屏上遮罩。
        /// 注意：预设**不是**「只在启动时读一次」—— 改窗口尺寸会把 ReShade 的
        /// 运行环境整个重建、顺手重读预设（ReShade.log 里那串
        /// "Destroyed / Recreated runtime environment"）。所以别指望「写了不生效」，
        /// 写进去的值很可能下一刻就上屏 —— 这也是本插件不许碰预设文件的原因。
        /// </summary>
        private static void ApplyFade(float a)
        {
            SetFadeBlack(a);
        }

        // ---- ReShade 效果名 / technique 名（只用来读预设、写日志）
        private const string FxEffect = "KK_BloomVignette.fx";
        private const string FxTech = "KK_BloomVignette@" + FxEffect;

        /// <summary>
        /// 导出前**只读**看一眼 ReShade 预设：technique 开没开、当前四个参数是多少，打进日志。
        ///
        /// 这里以前是 <c>SyncReshadePreset()</c>，会把界面档位换算成参数**写回预设文件**
        /// （`[KK_BloomVignette.fx]` 整段删掉重建）。后果很直接：
        /// 用户在 ReShade 面板（Home 键）里手调好的泛光/暗角，一点「开始导出」就被
        /// 换成本插件配置里的默认档位（`Vignette=Strong / Bloom=Medium` →
        /// 阈值 0.62 / 强度 0.35 / 半径 4 / 暗角 0.87），
        /// 而且导出时 VideoExport 会改窗口尺寸，ReShade 借机重建运行环境、
        /// 重新读一遍预设 —— 于是手调的值当场失效，录出来的片子也是默认档位。
        /// 所以现在禁止本插件写任何 ReShade 文件：参数归用户，插件只读不写。
        /// </summary>
        public static void CheckReshadePreset()
        {
            try
            {
                string path = ResolveReshadePresetPath();
                if (!File.Exists(path))
                {
                    Log("[ReShade] 没找到预设文件：" + path
                        + "（泛光/暗角完全按 ReShade 当前状态走，本插件不改）");
                    return;
                }

                bool techOn = false, techSeen = false;
                string section = null;
                var vals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string raw in File.ReadAllLines(path))
                {
                    string t = raw.Trim();
                    if (t.Length == 0) continue;
                    if (t[0] == '[' && t[t.Length - 1] == ']')
                    {
                        section = t.Substring(1, t.Length - 2);
                        continue;
                    }
                    int eq = t.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = t.Substring(0, eq).Trim();
                    string v = t.Substring(eq + 1).Trim();

                    if (section == null || section.Length == 0)
                    {
                        if (k.Equals("Techniques", StringComparison.OrdinalIgnoreCase))
                        {
                            techSeen = true;
                            if (v.IndexOf(FxTech, StringComparison.OrdinalIgnoreCase) >= 0) techOn = true;
                        }
                    }
                    else if (section.Equals(FxEffect, StringComparison.OrdinalIgnoreCase))
                    {
                        vals[k] = v;
                    }
                }

                Log("[ReShade] 预设 " + path + "：technique " + (techOn ? "已开启" : "未开启")
                    + "；阈值=" + GetVal(vals, "BloomThreshold")
                    + " 泛光=" + GetVal(vals, "BloomIntensity")
                    + " 半径=" + GetVal(vals, "BloomRadius")
                    + " 暗角=" + GetVal(vals, "VignetteStrength")
                    + "（以上是 ReShade 面板里的当前值，本插件只读不写）");

                if (!techOn)
                    Log("[ReShade] 注意：KK_BloomVignette 没有勾上"
                        + (techSeen ? "" : "（预设里连 Techniques= 都没有）")
                        + "，成片不会有泛光/暗角 —— 按 Home 打开 ReShade 面板勾一下即可。");
            }
            catch (Exception e)
            {
                Log("[ReShade] 读预设失败：" + e.Message + "（不影响导出，泛光/暗角按 ReShade 当前状态走）");
            }
        }

        private static string GetVal(Dictionary<string, string> vals, string key)
        {
            string v;
            return vals.TryGetValue(key, out v) ? v : "?";
        }

        /// <summary>日志里那一行「后期：…」。现在只有 ReShade 实时一条路。</summary>
        private static string DescribeFx(ExportOptions o)
        {
            var bits = new List<string>();
            if (o.FadeSeconds > 0f) bits.Add("淡入淡出 " + F(o.FadeSeconds) + "s（直出，不后期）");
            bits.Add("ReShade 实时（泛光/暗角用 ReShade 面板里的当前值，插件不改预设）");
            bits.Add("截图=Reshade");
            bits.Add("后期=跳过（直接出片，不重编码）");
            return string.Join("，", bits.ToArray());
        }

        private static string F(float v)
        {
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
