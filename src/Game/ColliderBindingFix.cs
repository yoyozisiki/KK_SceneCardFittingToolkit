using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace KKPEHeightLock
{
    /// <summary>
    /// 关掉 KKPE 每个 Dynamic Bone Collider 的「Enable New Dynamic Bones」。
    ///
    /// 开关开着时，换人后新出现的动骨会被自动登记进碰撞体影响列表，
    /// 饰品头发的骨链瞬间变成碰撞对象 → 被吸进脸/身体。
    /// 字段：HSPE.AMModules.CollidersEditor._addNewDynamicBonesAsDefault。
    /// 必须在 ChangeChara 之前关，否则新骨骼已经登记进去了。
    /// </summary>
    internal static class ColliderBindingFix
    {
        /// <summary>关掉所有碰撞体的「自动吸附新动骨」，返回改动的碰撞体个数。</summary>
        internal static int DisableAutoAttachNewBones()
        {
            int n = 0;
            try
            {
                var pcType = AccessTools.TypeByName("HSPE.PoseController");
                var ceType = AccessTools.TypeByName("HSPE.AMModules.CollidersEditor");
                if (pcType == null || ceType == null) return 0;

                const BindingFlags Inst =
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                const BindingFlags Stat =
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

                var fControllers = pcType.GetField("_poseControllers", Stat);
                var fEditor = pcType.GetField("_collidersEditor", Inst);
                var fFlag = ceType.GetField("_addNewDynamicBonesAsDefault", Inst);
                if (fControllers == null || fEditor == null || fFlag == null) return 0;

                var box = fControllers.GetValue(null) as System.Collections.IEnumerable;
                if (box == null) return 0;

                foreach (var entry in box)
                {
                    object pc = entry;
                    // _poseControllers 可能是 List 也可能是 Dictionary，字典项取 Value
                    if (pc != null && pc.GetType().IsGenericType &&
                        pc.GetType().GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                        pc = pc.GetType().GetProperty("Value").GetValue(pc, null);
                    if (pc == null) continue;

                    var ed = fEditor.GetValue(pc);
                    if (ed == null) continue;

                    try
                    {
                        if ((bool)fFlag.GetValue(ed))
                        {
                            fFlag.SetValue(ed, false);
                            n++;
                        }
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning(
                    "[碰撞体] 关闭「Enable New Dynamic Bones」失败：" + e.Message);
            }

            if (n > 0)
                KKPEHeightLockPlugin.Log.LogMessage("[碰撞体] 已关闭 " + n
                    + " 个碰撞体的「Enable New Dynamic Bones」（换人后不再自动吸附新动骨）");
            return n;
        }
    }
}
