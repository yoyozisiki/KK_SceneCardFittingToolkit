using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using HSPE.AMModules;
using KKAPI.Studio.SaveLoad;
using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>
    /// 自动把角色骨骼 cf_n_height 标记为 KKPE BonesEditor 的 dirty(scale),
    /// 使 KKPE 每帧强制写回该骨骼的缩放,从而锁定身高,不被动画/姿势覆盖。
    /// 仅在 LockMode == HeightOnly 时生效。
    /// </summary>
    [HarmonyPatch(typeof(BonesEditor), "ApplyBoneManualCorrection")]
    public static class HeightLockPatches
    {
        private static string BoneName => KKPEHeightLockPlugin.BoneName.Value;

        // 反射缓存
        private static FieldInfo _targetField;
        private static FieldInfo _dirtyBonesField;
        private static FieldInfo _typeField;
        private static FieldInfo _ociCharField;
        private static MethodInfo _setBoneScaleMethod;
        private static MethodInfo _dirtyBonesContainsKey;
        private static MethodInfo _dirtyBonesGetItem;
        private static FieldInfo _transformDataScaleField;
        private static PropertyInfo _editableValueValue;
        private static bool _reflectionReady;

        // 按角色缓存的骨骼查找(角色销毁后旧引用失效,会重新查找并清理)
        private static readonly System.Collections.Generic.Dictionary<Studio.OCIChar, Transform> _boneCache =
            new System.Collections.Generic.Dictionary<Studio.OCIChar, Transform>();

        // 每个角色的锁定基准缩放(首次 = 角色加载/首次见到时的值;用户在 KKPE 手动调整后同步)
        private static readonly System.Collections.Generic.Dictionary<Studio.OCIChar, Vector3> _lockedScales =
            new System.Collections.Generic.Dictionary<Studio.OCIChar, Vector3>();

        private static void EnsureReflection()
        {
            if (_reflectionReady) return;

            var bonesEditorType = typeof(BonesEditor);
            _targetField = AccessTools.Field(bonesEditorType, "_target");
            _dirtyBonesField = AccessTools.Field(bonesEditorType, "_dirtyBones");
            _setBoneScaleMethod = AccessTools.Method(bonesEditorType, "SetBoneScale", new[] { typeof(Transform), typeof(Vector3) });

            var targetType = AccessTools.TypeByName("HSPE.AMModules.GenericOCITarget");
            if (targetType != null)
            {
                _typeField = AccessTools.Field(targetType, "type");
                _ociCharField = AccessTools.Field(targetType, "ociChar");
            }

            // _dirtyBones 是 Dictionary<GameObject, TransformData>,反射取 ContainsKey / Item
            if (_dirtyBonesField != null)
            {
                var dirtyType = _dirtyBonesField.FieldType;
                _dirtyBonesContainsKey = dirtyType.GetMethod("ContainsKey", new[] { typeof(GameObject) });
                _dirtyBonesGetItem = dirtyType.GetMethod("get_Item", new[] { typeof(GameObject) });
            }

            // TransformData.scale(EditableValue<Vector3>) 与 EditableValue.get_value。
            // 嵌套类型必须从父类型上取 —— AccessTools.TypeByName("Outer/Nested") 在
            // 某些版本里取不到（KKS 的 KKPE 就取不到），这里直接用 GetNestedType。
            var tdType = bonesEditorType.GetNestedType("TransformData",
                BindingFlags.Public | BindingFlags.NonPublic);
            if (tdType == null)
                tdType = AccessTools.TypeByName("HSPE.AMModules.BonesEditor/TransformData");
            if (tdType != null)
            {
                _transformDataScaleField = tdType.GetField("scale",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var evType = AccessTools.TypeByName("HSPE.EditableValue`1");
                if (evType != null)
                {
                    var evVector3 = evType.MakeGenericType(typeof(Vector3));
                    _editableValueValue = evVector3.GetProperty("value");
                }
            }

            _reflectionReady = true;
            // 三个必需项缺一不可；TransformData 那两个只影响「用户在 KKPE 里手动改身高后
            // 同步锁定基准」这一条可选路径，缺了不影响锁定本身。
            if (_targetField == null || _dirtyBonesField == null || _setBoneScaleMethod == null ||
                _typeField == null || _ociCharField == null || _dirtyBonesContainsKey == null ||
                _dirtyBonesGetItem == null)
                KKPEHeightLockPlugin.Log.LogError("KKPEHeightLock: 身高锁反射缺项，身高锁不可用。targetField=" +
                    (_targetField != null) + " dirtyBonesField=" + (_dirtyBonesField != null) +
                    " setBoneScale=" + (_setBoneScaleMethod != null) + " typeField=" + (_typeField != null) +
                    " ociCharField=" + (_ociCharField != null) + " containsKey=" + (_dirtyBonesContainsKey != null) +
                    " getItem=" + (_dirtyBonesGetItem != null));
            else if (_transformDataScaleField == null || _editableValueValue == null)
                KKPEHeightLockPlugin.Log.LogInfo(
                    "KKPEHeightLock: 身高锁已启用（手动改身高后的基准同步不可用，其余正常）");
            else
                KKPEHeightLockPlugin.Log.LogInfo("KKPEHeightLock: 身高锁已启用（含手动改值同步）");
        }

        private static void Prefix(BonesEditor __instance)
        {
            if (_disabled) return;
            try
            {
                if (!KKPEHeightLockPlugin.Enabled.Value) return;
                if (KKPEHeightLockPlugin.LockMode.Value != BodyLockMode.HeightOnly) return;
                EnsureReflection();
                if (__instance == null || _targetField == null || _setBoneScaleMethod == null) return;

                var target = _targetField.GetValue(__instance);
                if (target == null || _typeField == null || _ociCharField == null) return;

                // 只处理角色(GenericOCITarget.Type.Character = 1)
                var typeVal = Convert.ToInt32(_typeField.GetValue(target));
                if (typeVal != 1) return;

                var ociChar = _ociCharField.GetValue(target) as Studio.OCIChar;
                if (ociChar == null || ociChar.charInfo == null) return;

                var bone = FindHeightBone(ociChar);
                if (bone == null) return;

                var dirtyBones = _dirtyBonesField.GetValue(__instance);
                var isDirty = (bool)_dirtyBonesContainsKey.Invoke(dirtyBones, new object[] { bone.gameObject });

                if (isDirty)
                {
                    // 已在 dirty 列表 → KKPE 每帧强制写回记录值,已锁定。
                    // 若用户手动调整过(KKPE 会更新 TransformData.scale),同步锁定基准。
                    // 注意:KK/KKS 的 EditableValue<T> 属性名不一定叫 hasValue,
                    // 取不到就跳过同步 —— 以前直接 .GetValue 会每帧抛 NRE 刷日志。
                    var td = _dirtyBonesGetItem.Invoke(dirtyBones, new object[] { bone.gameObject });
                    if (td != null && _transformDataScaleField != null && _editableValueValue != null)
                    {
                        var ev = _transformDataScaleField.GetValue(td);
                        if (ev != null)
                        {
                            var hasValue = ev.GetType().GetProperty("hasValue");
                            if (hasValue != null)
                            {
                                object has = hasValue.GetValue(ev, null);
                                if (has is bool && (bool)has)
                                    _lockedScales[ociChar] = (Vector3)_editableValueValue.GetValue(ev, null);
                            }
                        }
                    }
                    return;
                }

                // 不在 dirty → 用基准值(而非被动画污染的当前值)自动锁定 scale
                Vector3 lockedScale;
                if (!_lockedScales.TryGetValue(ociChar, out lockedScale))
                    lockedScale = bone.localScale; // 首次:记录当前缩放作为基准

                _setBoneScaleMethod.Invoke(__instance, new object[] { bone, lockedScale });
                _lockedScales[ociChar] = lockedScale;
                KKPEHeightLockPlugin.Log.LogDebug($"Locked scale of {bone.name} = {lockedScale}");
            }
            catch (Exception e)
            {
                // 这个 Prefix 每帧都会被调，出错不能每帧刷一条日志 —— 前 3 次记下来，
                // 连续失败到 20 次就干脆停用这一组，避免拖慢游戏、淹没日志。
                _failCount++;
                if (_failCount <= 3)
                    KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock patch error: " + e);
                if (_failCount == 20)
                {
                    _disabled = true;
                    KKPEHeightLockPlugin.Log.LogError(
                        "KKPEHeightLock: 身高锁补丁连续失败 20 次，已停用（其他功能不受影响）");
                }
            }
        }

        private static int _failCount;
        private static bool _disabled;

        private static Transform FindHeightBone(Studio.OCIChar ociChar)
        {
            // 清理已销毁角色的缓存条目
            var staleKeys = null as System.Collections.Generic.List<Studio.OCIChar>;
            foreach (var kv in _boneCache)
            {
                if (kv.Key == null || kv.Value == null)
                {
                    if (staleKeys == null)
                        staleKeys = new System.Collections.Generic.List<Studio.OCIChar>();
                    staleKeys.Add(kv.Key);
                }
            }
            if (staleKeys != null)
                foreach (var k in staleKeys)
                {
                    _boneCache.Remove(k);
                    _lockedScales.Remove(k);
                }

            // 缓存命中且仍有效
            Transform cached;
            if (_boneCache.TryGetValue(ociChar, out cached) && cached != null)
                return cached;

            var root = ociChar.charInfo.transform;
            if (root == null) return null;

            var bone = FindChildRecursive(root, BoneName);
            if (bone != null)
                _boneCache[ociChar] = bone;
            return bone;
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var result = FindChildRecursive(parent.GetChild(i), name);
                if (result != null) return result;
            }
            return null;
        }
    }
}
