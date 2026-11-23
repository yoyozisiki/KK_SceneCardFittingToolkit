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
        // ======================================================== 角色槽位
        /// <summary>一个 female 对象，以及它在这一张卡里的角色（显示 / 前卡关系 / 真隐藏）。</summary>
        private sealed class FemaleSlot
        {
            public int Key;                 // dicObjectCtrl 的对象编号（运行时 key）
            public int TlIndex;             // 在「按 key 排序的对象列表」里的下标（仅诊断用）
            public Studio.OCIChar Oci;
            public bool Visible;            // 当前帧是否显示（眼睛图标）
            public bool InTimeline;         // 这个角色在 timeline 里出现过（任何通道）
        }

        /// <summary>
        /// 构建「运行时对象 → Timeline objectIndex」的映射（仅诊断/兜底用）。
        ///
        /// Timeline 卡里的 objectIndex 是
        /// <c>new SortedDictionary&lt;int, ObjectCtrlInfo&gt;(dicObjectCtrl).ToList()</c>
        /// 的下标，不是 dicObjectCtrl 的 key。这个映射只用来在日志里显示对照关系，
        /// 以及运行时数据读不到时做兜底。
        /// </summary>
        private static Dictionary<Studio.ObjectCtrlInfo, int> BuildTimelineIndexMap(Studio.Studio studio)
        {
            var map = new Dictionary<Studio.ObjectCtrlInfo, int>();
            if (studio == null || studio.dicObjectCtrl == null) return map;
            try
            {
                var sorted = new SortedDictionary<int, Studio.ObjectCtrlInfo>(studio.dicObjectCtrl);
                int i = 0;
                foreach (var kv in sorted) map[kv.Value] = i++;
            }
            catch (Exception e) { Log("构建 Timeline 对象下标映射失败：" + e.Message); }
            return map;
        }

        /// <summary>这个对象在 Timeline 里的 objectIndex；找不到返回 -1。</summary>
        private static int TimelineIndexOf(Dictionary<Studio.ObjectCtrlInfo, int> map, Studio.ObjectCtrlInfo oci)
        {
            int idx;
            return map != null && oci != null && map.TryGetValue(oci, out idx) ? idx : -1;
        }

        /// <summary>
        /// 从「当前已加载的 Timeline 插件」读出**在 timeline 里出现过的**真实对象（按 OCI 引用）。
        ///
        /// 为什么看「所有通道」而不是只看 objectEnabled：
        /// 换装卡把角色移进移出画面靠的是 guideObjectPos / guideObjectRot（位置动画），
        /// 甚至 bonePos / 表情曲线，不一定有 objectEnabled。hongcha #06 就是典型：
        /// 9 个 female 里只有 1 个有 objectEnabled 通道，其余全靠位置和骨骼动画。
        /// 只要某个 female 在 timeline 里有任何插值项，它就是换装链路的一环。
        ///
        /// 返回 null 表示读不到（Timeline 没装 / 没加载时间轴），调用方再回退到
        /// 文件解析 + 排序下标的方案。
        /// </summary>
        private static HashSet<Studio.ObjectCtrlInfo> GetTimelineOcis()
        {
            try
            {
                var all = global::Timeline.Timeline.GetAllInterpolables(false);
                if (all == null) return null;
                var set = new HashSet<Studio.ObjectCtrlInfo>();
                foreach (var item in all)
                {
                    var ip = item as global::Timeline.Interpolable;
                    if (ip == null || ip.oci == null) continue;
                    set.Add(ip.oci);
                }
                return set;
            }
            catch (Exception e) { Log("[诊断] 读 Timeline 运行时对象失败：" + e.Message); return null; }
        }

        /// <summary>
        /// 建一个「角色 → 是否在 timeline 里出现过」的判定器。优先用 Timeline 运行时数据
        /// （按对象引用，最准）；读不到再回退到场景卡文件解析出的下标集合 + 排序映射。
        /// </summary>
        private static Func<Studio.ObjectCtrlInfo, bool> BuildTimelinePredicate(HashSet<int> revealed)
        {
            var viaRuntime = GetTimelineOcis();
            if (viaRuntime != null)
            {
                Log($"[诊断] Timeline 里出现过的对象数={viaRuntime.Count}（按对象引用直接判定）");
                return delegate(Studio.ObjectCtrlInfo oci)
                {
                    return oci != null && viaRuntime.Contains(oci);
                };
            }
            var studio = UnityEngine.Object.FindObjectOfType<Studio.Studio>();
            var indexMap = BuildTimelineIndexMap(studio);
            Log($"[诊断] 回退到下标映射：timeline 下标=[{string.Join(",", revealed)}]，场景对象数={indexMap.Count}");
            return delegate(Studio.ObjectCtrlInfo oci)
            {
                int idx = TimelineIndexOf(indexMap, oci);
                return idx >= 0 && revealed.Contains(idx);
            };
        }

        // ======================================================== 替换 female
        /// <summary>
        /// 角色是不是「显示中」（工作室树里的眼睛图标开着）。
        /// 这只是「当前帧的可见状态」，不代表角色全程不出片：Timeline 会在某些帧把
        /// 隐藏角色取消隐藏（与上一个显示角色是前后关系），那类角色由 ReplaceFemale 按前卡规则处理。
        ///
        /// 判据（眼睛图标关掉时两条会同时变 false）：
        ///   · charInfo.visibleAll —— OCIChar.OnVisible(false) 里被置 false，最直接；
        ///   · treeNodeObject.visible —— 树节点眼睛图标本身的状态，兜底。
        /// 两条都拿不到时按「显示」处理，宁可多换也别误伤正常角色。
        /// </summary>
        private static bool IsFemaleVisible(Studio.OCIChar oc)
        {
            if (oc == null) return false;
            try
            {
                var ci = oc.charInfo;
                if (ci != null && !ci.visibleAll) return false;
            }
            catch (Exception) { }
            try
            {
                var node = oc.treeNodeObject;
                if (node != null && !node.visible) return false;
            }
            catch (Exception) { }
            return true;
        }
        private static IEnumerator ReplaceFemale(ExportOptions o, HashSet<int> revealed)
        {
            // **必须在换人之前**：KKPE 碰撞体的「Enable New Dynamic Bones」开着时，
            // 一旦角色重建，新出现的动骨会被自动加进碰撞体的影响列表 → 饰品头发被吸走。
            // 社区对 hongcha 场景的标准做法就是在 KKPE 高级模式里把这个开关取消掉。
            try { ColliderBindingFix.DisableAutoAttachNewBones(); }
            catch (Exception e) { Log("[提示] 关闭自动吸附新动骨失败：" + e.Message); }

            // 场景刚加载完时网格/材质还在构建，这时候 ChangeChara 会换不干净
            for (int i = 0; i < 6; i++) yield return null;

            Studio.Studio studio = null;
            try { studio = UnityEngine.Object.FindObjectOfType<Studio.Studio>(); }
            catch (Exception) { }
            if (studio == null) { Log("找不到 Studio 实例，跳过替换"); yield break; }

            // 按对象编号排序，保证「第一个 female」和「前一个显示角色」的顺序稳定 ——
            // 字典遍历顺序不保证，直接遍历会出现「这次换这张、下次换那张」。
            // 角色分三种，分开处理：
            //   ① 显示中                → 按顺序拿主卡 / 次卡（消耗一个新卡序号）；
            //   ② 隐藏但会被取消隐藏（前后关系）→ 用「上一个显示角色」的卡，不消耗新序号；
            //   ③ 真隐藏                → 跳过。
            var slots = new List<FemaleSlot>();
            // 先标记：哪些 female 在 timeline 里出现过（位置/骨骼/表情/显隐任意通道）。
            // 换装卡里隐藏的 female 几乎都是靠位置动画入场的，不能只看 objectEnabled。
            var isInTimeline = BuildTimelinePredicate(revealed);
            var diagMap = BuildTimelineIndexMap(studio);   // 仅用于诊断日志
            try
            {
                if (studio.dicObjectCtrl != null)
                    foreach (var kv in studio.dicObjectCtrl)
                    {
                        var oc = kv.Value as Studio.OCIChar;
                        if (oc == null || oc.sex != 1 || oc.charInfo == null) continue;
                        bool visible = IsFemaleVisible(oc);
                        bool inTl = isInTimeline(oc);
                        int tlIndex = TimelineIndexOf(diagMap, oc);
                        Log($"  角色 key={kv.Key} tlIndex={tlIndex} visible={visible} inTimeline={inTl}");
                        slots.Add(new FemaleSlot
                        {
                            Key = kv.Key, TlIndex = tlIndex, Oci = oc,
                            Visible = visible, InTimeline = inTl
                        });
                    }
            }
            catch (Exception e) { Log("枚举角色失败：" + e.Message); yield break; }
            slots.Sort(delegate(FemaleSlot a, FemaleSlot b) { return a.Key.CompareTo(b.Key); });

            int shownCount = 0, hiddenCount = 0, hiddenInTl = 0;
            for (int s = 0; s < slots.Count; s++)
            {
                if (slots[s].Visible) shownCount++;
                else { hiddenCount++; if (slots[s].InTimeline) hiddenInTl++; }
            }
            // 只剩隐藏角色也照样换：换装卡的第一帧可能就是隐藏态，主卡必须换上去，
            // 否则一播放就露出场景原角色。
            if (slots.Count == 0)
            {
                Log("[提示] 场景里没有 female，跳过替换");
                yield break;
            }

            // 主卡
            string main = string.IsNullOrEmpty(o.FemaleCard) ? null : o.FemaleCard;
            if (main != null && !File.Exists(main))
            {
                Log("[提示] 主角色卡文件不存在：" + main + "（当作没选）");
                main = null;
            }

            // 次卡池：丢掉不存在的路径，保持原顺序（顺序就是编号）
            var pool = new List<string>();
            if (o.SubCards != null)
                for (int i = 0; i < o.SubCards.Count; i++)
                {
                    string p = o.SubCards[i];
                    if (string.IsNullOrEmpty(p)) continue;
                    if (!File.Exists(p)) { Log("[提示] 次角色卡不存在，已跳过：" + p); continue; }
                    if (pool.Contains(p)) continue;
                    pool.Add(p);
                }

            if (main == null && pool.Count == 0)
            {
                Log("[提示] 主角色卡和次角色卡都没选，跳过替换");
                yield break;
            }

            Log($"替换方案：{shownCount} 个显示 female"
                + (hiddenCount > 0 ? $" + {hiddenCount} 个隐藏角色（其中 {hiddenInTl} 个在 timeline 里出现过）→ 一律用主卡" : "")
                + $"，主卡={(main != null ? NameOf(main) : "（无）")}，"
                + $"次卡 {pool.Count} 张，顺序={(o.SubRandom ? "随机" : "数字优先")}");

            int done = 0, byMain = 0, bySub = 0, byHidden = 0;
            var swapped = new List<Studio.OCIChar>();
            var scenes = new List<BodySnap>();
            var cards = new List<BodySnap>();
            var rng = new System.Random();
            int shownIndex = 0;      // 显示角色的卡序号（主/次卡分配用），只有显示角色才消耗
            string prevCard = null;  // 上一个显示角色用的卡；会取消隐藏的隐藏角色（前后关系）复用它

            foreach (var slot in slots)
            {
                // 停止：立刻中断，不再换后面的角色（否则点了停止还会继续替换）。
                if (CancelRequested) { Log("[停止] 中断替换，剩余角色不再处理"); yield break; }
                // 暂停：停在**角色与角色之间**等 ESC。不能停在一个角色换到一半的位置
                // （骨架/网格处于半重建状态，继续时容易出错）。
                // 只在「进入暂停」时写一次阶段和日志：SetPhase 每次都会 LogMessage，
                // 放在 while 里会每帧刷一条（实测一次暂停刷了 51 条，把日志冲没了）。
                if (Paused && !CancelRequested) SetPhase("已暂停：按 ESC 继续");
                while (Paused && !CancelRequested) yield return null;
                if (CancelRequested) { Log("[停止] 中断替换，剩余角色不再处理"); yield break; }

                string card;
                string tag;
                if (slot.Visible)
                {
                    card = PickCardForFemale(o, shownIndex, main, pool, rng, out tag);
                    shownIndex++;
                }
                else
                {
                    // 隐藏角色：换装卡里它们就是同一个人的其他造型，在 timeline 之后会被
                    // 移进画面/取消隐藏。**一律用主角色卡**（不消耗次卡序号），
                    // 这样换装前后是同一个人；主卡没选时退回上一个显示角色的卡，
                    // 再不行才从次卡池里取一张兜底。
                    if (!string.IsNullOrEmpty(main))
                    {
                        card = main;
                        tag = slot.InTimeline ? "主卡·前后关系" : "主卡·隐藏";
                    }
                    else if (!string.IsNullOrEmpty(prevCard))
                    {
                        card = prevCard;
                        tag = "前卡·前后关系";
                    }
                    else
                    {
                        card = PickCardForFemale(o, shownIndex, main, pool, rng, out tag);
                        shownIndex++;
                    }
                }

                if (string.IsNullOrEmpty(card)) continue;
                // 记录「上一个换掉的卡」，主卡为空时给后面的隐藏角色兜底。
                prevCard = card;

                try
                {
                    var oci = slot.Oci;
                    var sceneBody = BodySnap.Capture(oci);
                    oci.ChangeChara(card);
                    // 换人后 KKS 有时不会把卡里的 addMove（每件饰品的位置/旋转/缩放）应用上去：
                    // #278 实测卡里 01 是 pos(0,-4.2,2)/scale(1.28)，换完人节点还是 (0,0,0)/1，
                    // 于是所有饰品贴在父节点原点 → 头发直接糊在脸上。
                    // UpdateAccessoryMoveAllFromInfo() 也救不回来，这里按卡自己写一遍。
                    try { oci.charInfo.UpdateAccessoryMoveAllFromInfo(); }
                    catch (Exception e1) { Log("重算饰品变换失败：" + e1.Message); }
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
                    if (slot.Visible)
                    {
                        if (tag.StartsWith("主", StringComparison.Ordinal)) byMain++; else bySub++;
                    }
                    else byHidden++;
                    Log($"  female(obj {slot.Key}){(slot.Visible ? "" : (slot.InTimeline ? "·隐藏/在timeline" : "·隐藏"))} ← [{tag}] {NameOf(card)}");
                }
                catch (Exception e) { Log("替换出错：" + e.Message); }
                yield return null;
            }

            // ChangeChara 内部的 Reload 是分帧收尾的：立刻写回的身材会被新卡数据盖掉，
            // 所以换完再补写 3 轮（每轮隔 2 帧）。3 轮 × 2 帧是实测的稳定下界。
            for (int round = 0; round < 3; round++)
            {
                for (int i = 0; i < swapped.Count; i++)
                {
                    BodySnap.Compose(swapped[i], scenes[i], cards[i], o);
                }
                for (int f = 0; f < 2; f++) yield return null;
            }

            // 身材落定之后再记身高基准（骨架刚重建完读到的不是最终值）
            if (o.LockHeight)
                for (int i = 0; i < swapped.Count; i++) BodySnap.LockHeight(swapped[i]);

            Log($"替换完成：{done} 个 female（显示用主卡 {byMain} / 显示用次卡 {bySub} / 隐藏用主卡 {byHidden}）");
        }

        /// <summary>
        /// 给第 n 个 female（0 起）挑一张卡。tag 用来打日志：主 / 次1 / 次随机。
        /// 分配规则见 <see cref="ReplaceFemale"/> 的注释。
        /// </summary>
        private static string PickCardForFemale(ExportOptions o, int n, string main,
                                               List<string> pool, System.Random rng, out string tag)
        {
            tag = "";
            // 第一个 female 优先用主卡
            if (n == 0 && main != null) { tag = "主"; return main; }

            if (pool != null && pool.Count > 0)
            {
                // 有主卡时：第 2 个 female 对应次卡 #1；没主卡时第 1 个就对应次卡 #1
                int seq = (main != null) ? (n - 1) : n;
                int idx = o.SubRandom ? rng.Next(pool.Count) : (seq % pool.Count);
                tag = o.SubRandom ? ("次·随机" + (idx + 1)) : ("次" + (idx + 1));
                return pool[idx];
            }

            // 没有次卡：退回主卡（老行为），主卡也没有就返回空
            if (main != null) { tag = "主(兜底)"; return main; }
            return null;
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

        /// <summary>
        /// 收集要应用服装 / 体型的 female。
        ///
        /// **所有** female 都收，包括隐藏的：换装卡里隐藏角色靠位置动画入场，
        /// 是同一个人换的另一套造型，必须跟着穿同一套衣服、用同一套体型，
        /// 否则换装前后衣服/身材对不上。
        /// </summary>
        private static List<Studio.OCIChar> CollectFemales(HashSet<int> revealed)
        {
            var list = new List<Studio.OCIChar>();
            try
            {
                var studio = UnityEngine.Object.FindObjectOfType<Studio.Studio>();
                if (studio == null || studio.dicObjectCtrl == null) return list;
                foreach (var kv in studio.dicObjectCtrl)
                {
                    var oc = kv.Value as Studio.OCIChar;
                    if (oc == null || oc.sex != 1 || oc.charInfo == null) continue;
                    // 隐藏的 female 也收：换装卡里它们会入场，必须同一套衣服/体型。
                    list.Add(oc);
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
        private static IEnumerator ApplyClothes(ExportOptions o, HashSet<int> revealed)
        {
            bool wantOutfit = o.Outfit >= 0 || o.Outfit == -2;   // -2 = 随机
            bool wantParts = false;
            if (o.ClothesParts != null)
                for (int i = 0; i < o.ClothesParts.Length; i++)
                    if (o.ClothesParts[i] >= 0) { wantParts = true; break; }
            if (!wantOutfit && !wantParts) yield break;

            var chars = CollectFemales(revealed);
            if (chars.Count == 0)
            {
                Log("[提示] 场景里没有 female，跳过服装设置");
                yield break;
            }

            var snaps = new List<BodySnap>();
            for (int i = 0; i < chars.Count; i++) snaps.Add(BodySnap.Capture(chars[i]));

            // **等 ChangeChara 的 Reload 落地**。
            // ReplaceFemale 里每个角色都调过 ChangeChara，它的 Reload 是分帧执行的；
            // 这期间 fileStatus / chaFile.coordinate 还没就绪，这时调 SetCoordinateInfo，
            // 内部 ChangeCoordinateType() 会返回 false —— 于是**静默不换装**：
            // 没异常、没日志，看起来就是"随机服装失效"。
            // 实测等 12 帧（约 0.2s）足够让 10 个以内的角色全部就绪。
            for (int f = 0; f < 12; f++) yield return null;

            int nCoord = 0, nParts = 0, nCoordFail = 0;
            var picked = new List<string>();
            for (int i = 0; i < chars.Count; i++)
            {
                // 停止 / 暂停也在角色之间生效（和替换 female 同样的语义）
                if (CancelRequested) { Log("[停止] 中断换装，剩余角色不再处理"); yield break; }
                // 只在「进入暂停」时写一次阶段和日志：SetPhase 每次都会 LogMessage，
                // 放在 while 里会每帧刷一条（实测一次暂停刷了 51 条，把日志冲没了）。
                if (Paused && !CancelRequested) SetPhase("已暂停：按 ESC 继续");
                while (Paused && !CancelRequested) yield return null;
                if (CancelRequested) { Log("[停止] 中断换装，剩余角色不再处理"); yield break; }

                var oc = chars[i];
                if (wantOutfit)
                {
                    // 挑槽位（随机时只挑「卡片里真的有衣服」的槽，挑到空槽会变裸体）
                    int slot = o.Outfit;
                    string pickDesc = null;
                    try
                    {
                        if (slot == -2)
                        {
                            var slots = AvailableOutfits(oc.charInfo);
                            if (slots.Count == 0)
                                Log("[提示] 读不出可用服装槽位（角色卡可能还在加载），退回 #0 私服");
                            slot = (slots.Count > 0)
                                ? slots[UnityEngine.Random.Range(0, slots.Count)]
                                : 0;
                            pickDesc = "#" + slot + " " + OutfitName(slot);
                        }
                    }
                    catch (Exception e) { Log("挑选服装槽失败：" + e.Message); }

                    // 切槽。注意 yield 不能放在 try/catch 里（C# 限制），所以拆开写。
                    bool switched = true;
                    try { oc.SetCoordinateInfo((ChaFileDefine.CoordinateType)slot, true); }
                    catch (Exception e2) { Log("切换套装失败：" + e2.Message); switched = false; }
                    yield return null;

                    // 验证真的切过去了。ChangeCoordinateType 失败时**不抛异常**，
                    // 只能看 fileStatus.coordinateType 有没有变成目标槽位。
                    if (switched) switched = CoordinateIs(oc, slot);
                    if (!switched)
                    {
                        try { oc.SetCoordinateInfo((ChaFileDefine.CoordinateType)slot, true); }
                        catch (Exception) { }
                        yield return null;
                        switched = CoordinateIs(oc, slot);
                    }

                    if (!switched)
                    {
                        nCoordFail++;
                        Log("[提示] 服装槽 #" + slot + " 切换没生效（重试后仍失败）");
                    }
                    else
                    {
                        if (pickDesc != null) picked.Add(pickDesc);
                        nCoord++;
                    }
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

            if (nCoord > 0)
            {
                if (o.Outfit == -2)
                    Log("已随机切换服装预设（" + nCoord + " 个 female）：" + string.Join("、", picked.ToArray()));
                else
                    Log("已切换服装预设 #" + o.Outfit + "（" + nCoord + " 个 female）");
            }
            if (nParts > 0) Log("已设置部件状态 " + nParts + " 处");
            if (nCoordFail > 0)
                Log($"[提示] {nCoordFail} 个角色的服装槽切换失败（已重试一次，仍没生效）");

            // 换装会分帧重建网格，等收尾后按「换装前快照」还原体型 + 乳量。
            // 用 Compose（不用 RestoreBustPhysics）—— 后者不写 shapeValueBody，乳量大小回不去。
            // 写两遍，防止 SetCoordinateInfo 内部的刷新覆盖。
            for (int f = 0; f < 6; f++) yield return null;
            for (int i = 0; i < chars.Count; i++)
                if (snaps[i] != null) BodySnap.Compose(chars[i], snaps[i], snaps[i], o);

            for (int f = 0; f < 3; f++) yield return null;
            for (int i = 0; i < chars.Count; i++)
                if (snaps[i] != null) BodySnap.Compose(chars[i], snaps[i], snaps[i], o);

            Log("[体型] 换装后按换装前快照还原（shape + 乳量）");

        }



        /// <summary>角色当前的服装槽位是不是 slot。读不到时按 true 处理，避免误报失败。</summary>
        private static bool CoordinateIs(Studio.OCIChar oc, int slot)
        {
            try
            {
                if (oc == null || oc.charInfo == null || oc.charInfo.fileStatus == null) return true;
                return oc.charInfo.fileStatus.coordinateType == slot;
            }
            catch (Exception) { return true; }
        }

        /// <summary>
        /// 角色卡里「真的有衣服」的服装槽位。随机服装只在这些槽位里挑。
        ///
        /// 为什么不能直接 0~6 随机：卡片里的槽位可能是空的（用户只做了私服一套），
        /// 切到空槽等于把角色扒光 —— 成片是裸的，比不换还糟。
        /// 判据是「这套衣服里任意一个部件 id > 0」（id = 0 就是没穿这件）。
        /// </summary>
        public static List<int> AvailableOutfits(ChaControl ci)
        {
            var list = new List<int>();
            try
            {
                var chaFile = ci != null ? ci.chaFile : null;
                if (chaFile == null || chaFile.coordinate == null) return list;

                var coord = chaFile.coordinate;
                for (int i = 0; i < coord.Length; i++)
                {
                    var c = coord[i];
                    if (c == null || c.clothes == null || c.clothes.parts == null) continue;
                    var parts = c.clothes.parts;
                    for (int k = 0; k < parts.Length; k++)
                    {
                        if (parts[k].id != 0) { list.Add(i); break; }
                    }
                }
            }
            catch (Exception e)
            {
                Log("[提示] 读取服装槽位失败（随机服装退回默认槽）：" + e.Message);
            }
            return list;
        }

        /// <summary>服装槽位的显示名（和下拉框那套一致）。</summary>
        public static string OutfitName(int slot)
        {
            switch (slot)
            {
                case 0: return L10n.T("私服");
                case 1: return L10n.T("泳装");
                case 2: return L10n.T("睡衣");
                case 3: return L10n.T("浴衣");
                case 4: return L10n.T("校内");
                case 5: return L10n.T("体操");
                case 6: return L10n.T("社团");
                default: return "#" + slot;
            }
        }

        /// <summary>
        /// 主窗口「服装预设 → 随机」按钮用：给当前角色随机换一套**它自己有的**服装。
        /// 返回换到第几套，-1 = 没有可用槽位。
        /// </summary>
        public static int RandomOutfit(ChaControl ci)
        {
            var slots = AvailableOutfits(ci);
            if (slots.Count == 0)
            {
                Log("[提示] 这个角色的卡片里没有可用的服装槽位，随机服装跳过");
                return -1;
            }
            int pick = slots[UnityEngine.Random.Range(0, slots.Count)];
            try
            {
                ci.ChangeCoordinateTypeAndReload((ChaFileDefine.CoordinateType)pick);
                Log("随机服装 → #" + pick + " " + OutfitName(pick));
            }
            catch (Exception e)
            {
                Log("[提示] 随机换装失败：" + e.Message);
                return -1;
            }
            return pick;
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
                        + "（效果完全按 ReShade 当前状态走，本插件只读不写）");
                    return;
                }

                string techniques = null;
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
                        if (k.Equals("Techniques", StringComparison.OrdinalIgnoreCase)) techniques = v;
                    }
                    else if (section.Equals(FxEffect, StringComparison.OrdinalIgnoreCase))
                    {
                        vals[k] = v;
                    }
                }

                // 现在用户多半用自己的预设（reshade_ini 里那一堆），所以这里只报「开了哪些效果」，
                // 不再因为「本插件的 KK_BloomVignette 没勾」就报警 —— 那不是错。
                var names = new List<string>();
                if (!string.IsNullOrEmpty(techniques))
                {
                    foreach (var item in techniques.Split(','))
                    {
                        string it = item.Trim();
                        if (it.Length == 0) continue;
                        int at = it.IndexOf('@');
                        names.Add(at > 0 ? it.Substring(0, at) : it);
                    }
                }

                Log("[ReShade] 当前预设：" + Path.GetFileName(path)
                    + "，启用 " + names.Count + " 个效果"
                    + (names.Count > 0 ? "：" + JoinHead(names, 8) : "（空预设）"));

                bool techOn = !string.IsNullOrEmpty(techniques)
                              && techniques.IndexOf(FxTech, StringComparison.OrdinalIgnoreCase) >= 0;

                if (techOn)
                    Log("[ReShade] 本插件的 KK_BloomVignette 也开着：阈值=" + GetVal(vals, "BloomThreshold")
                        + " 泛光=" + GetVal(vals, "BloomIntensity")
                        + " 半径=" + GetVal(vals, "BloomRadius")
                        + " 暗角=" + GetVal(vals, "VignetteStrength")
                        + "（ReShade 面板里的当前值，本插件只读不写）");
                else
                    Log("[ReShade] 当前预设没启用 KK_BloomVignette（泛光/暗角），成片就按你在 ReShade 面板里"
                        + "勾上的效果录；想用插件的泛光/暗角，在 Home 面板里手动勾一下即可。");
            }
            catch (Exception e)
            {
                Log("[ReShade] 读预设失败：" + e.Message + "（不影响导出，效果按 ReShade 当前状态走）");
            }
        }

        /// <summary>把前 n 个效果名拼成一行（多了用「等」收尾），日志别太长。</summary>
        private static string JoinHead(List<string> names, int n)
        {
            var sb = new StringBuilder();
            int show = Math.Min(n, names.Count);
            for (int i = 0; i < show; i++)
            {
                if (i > 0) sb.Append("、");
                sb.Append(names[i]);
            }
            if (names.Count > show) sb.Append(" 等");
            return sb.ToString();
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
