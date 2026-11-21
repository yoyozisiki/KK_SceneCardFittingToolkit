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
    /// 变更角色(OCIChar.ChangeChara)时保留身材:
    /// ShapeOnly = 只写回体型滑块 shapeValueBody
    /// AllBody = 写回 shapeValueBody + bustSoftness/bustWeight + typeBone
    /// 替换后刷新模型身材与胸部物理。
    /// </summary>
    [HarmonyPatch(typeof(Studio.OCIChar), "ChangeChara")]
    public static class BodyPreservePatches
    {
        // 反射缓存
        private static PropertyInfo _shapeValueBodyProp;
        private static PropertyInfo _bustSoftnessProp;
        private static PropertyInfo _bustWeightProp;
        private static PropertyInfo _typeBoneProp;
        private static MethodInfo _updateShapeMethod;
        private static MethodInfo _updateBustMethod;
        private static bool _reflectionReady;

        // 保存替换前的身材(深拷贝)
        private static float[] _savedShapeValues;
        private static float _savedBustSoftness;
        private static float _savedBustWeight;
        private static int _savedTypeBone;

        // 保存替换前的姿势(骨骼名 → 局部变换)
        private static System.Collections.Generic.Dictionary<string, PoseData> _savedPose;

        // 保存替换前的服装(字节序列化)
        private static byte[] _savedCoordinateBytes;

        private class PoseData
        {
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
        }

        private static void EnsureReflection()
        {
            if (_reflectionReady) return;
            _reflectionReady = true;

            var bodyType = AccessTools.TypeByName("ChaFileBody");
            if (bodyType != null)
            {
                _shapeValueBodyProp = bodyType.GetProperty("shapeValueBody");
                _bustSoftnessProp = bodyType.GetProperty("bustSoftness");
                _bustWeightProp = bodyType.GetProperty("bustWeight");
                _typeBoneProp = bodyType.GetProperty("typeBone");
            }

            var chaControlType = AccessTools.TypeByName("ChaControl");
            if (chaControlType != null)
            {
                _updateShapeMethod = AccessTools.Method(chaControlType, "UpdateShapeBodyValueFromCustomInfo");
                _updateBustMethod = AccessTools.Method(chaControlType, "UpdateBustSoftnessAndGravity");
            }

            if (_shapeValueBodyProp == null || _updateShapeMethod == null)
                KKPEHeightLockPlugin.Log.LogError("KKPEHeightLock: BodyPreserve reflection incomplete. shapeValueBody=" +
                    (_shapeValueBodyProp != null) + " bustSoftness=" + (_bustSoftnessProp != null) +
                    " bustWeight=" + (_bustWeightProp != null) + " typeBone=" + (_typeBoneProp != null) +
                    " updateShape=" + (_updateShapeMethod != null) + " updateBust=" + (_updateBustMethod != null));
        }

        private static void Prefix(Studio.OCIChar __instance)
        {
            try
            {
                if (!KKPEHeightLockPlugin.Enabled.Value) return;
                var mode = KKPEHeightLockPlugin.LockMode.Value;

                // 防御:清空上一次可能残留的状态(若上次 ChangeChara 中途异常,Postfix 未清)
                _savedShapeValues = null;
                _savedPose = null;
                _savedCoordinateBytes = null;

                // 姿势保留(独立于身材模式)
                if (KKPEHeightLockPlugin.PreservePoseOnReplace.Value)
                    SavePose(__instance);

                // 服装保留(独立于身材模式)
                if (KKPEHeightLockPlugin.PreserveClothesOnReplace.Value)
                    SaveCoordinate(__instance);

                if (mode == BodyLockMode.HeightOnly) return;

                EnsureReflection();
                if (__instance == null || __instance.charInfo == null || _shapeValueBodyProp == null) return;

                var body = __instance.charInfo.fileBody;
                if (body == null) return;

                // 深拷贝体型滑块数组(新卡会替换整个数组,必须复制)
                var arr = _shapeValueBodyProp.GetValue(body, null) as float[];
                _savedShapeValues = arr != null ? (float[])arr.Clone() : null;

                if (mode == BodyLockMode.AllBody && _bustSoftnessProp != null && _bustWeightProp != null && _typeBoneProp != null)
                {
                    _savedBustSoftness = (float)_bustSoftnessProp.GetValue(body, null);
                    _savedBustWeight = (float)_bustWeightProp.GetValue(body, null);
                    _savedTypeBone = (int)_typeBoneProp.GetValue(body, null);
                }
                KKPEHeightLockPlugin.Log.LogDebug($"BodyPreserve: saved {(_savedShapeValues != null ? _savedShapeValues.Length : 0)} shape values, mode={mode}");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock BodyPreserve prefix error: " + e);
            }
        }

        /// <summary>保存角色所有 FK 骨骼的局部变换(姿势快照)。</summary>
        private static void SavePose(Studio.OCIChar ociChar)
        {
            _savedPose = new System.Collections.Generic.Dictionary<string, PoseData>();
            if (ociChar == null || ociChar.listBones == null) return;

            foreach (var boneInfo in ociChar.listBones)
            {
                try
                {
                    if (boneInfo == null) continue;
                    var go = boneInfo.gameObject;
                    if (go == null) continue;
                    var t = go.transform;
                    if (t == null) continue;
                    _savedPose[go.name] = new PoseData
                    {
                        localPosition = t.localPosition,
                        localRotation = t.localRotation,
                        localScale = t.localScale
                    };
                }
                catch (Exception) { }
            }
            KKPEHeightLockPlugin.Log.LogDebug($"BodyPreserve: saved {_savedPose.Count} bone transforms (pose)");
        }

        /// <summary>保存当前服装(坐标),用字节序列化深拷贝。</summary>
        private static void SaveCoordinate(Studio.OCIChar ociChar)
        {
            _savedCoordinateBytes = null;
            if (ociChar == null || ociChar.charInfo == null) return;
            var coord = ociChar.charInfo.nowCoordinate;
            if (coord != null)
            {
                try
                {
                    _savedCoordinateBytes = coord.SaveBytes();
                    KKPEHeightLockPlugin.Log.LogDebug($"BodyPreserve: saved coordinate bytes ({_savedCoordinateBytes.Length})");
                }
                catch (Exception e)
                {
                    KKPEHeightLockPlugin.Log.LogWarning("BodyPreserve: failed to save coordinate: " + e.Message);
                }
            }
        }

        private static void Postfix(Studio.OCIChar __instance)
        {
            try
            {
                if (!KKPEHeightLockPlugin.Enabled.Value) return;
                var mode = KKPEHeightLockPlugin.LockMode.Value;

                // 恢复服装(独立于身材模式,替换后立即恢复)
                if (KKPEHeightLockPlugin.PreserveClothesOnReplace.Value && _savedCoordinateBytes != null)
                    RestoreCoordinate(__instance);

                // 恢复姿势:骨架刚重建,延迟几帧再写回
                if (KKPEHeightLockPlugin.PreservePoseOnReplace.Value && _savedPose != null)
                {
                    var pose = _savedPose;
                    _savedPose = null;
                    SchedulePoseRestore(__instance, pose);
                }

                if (mode == BodyLockMode.HeightOnly || _savedShapeValues == null) return;

                EnsureReflection();
                if (__instance == null || __instance.charInfo == null) return;

                var body = __instance.charInfo.fileBody;
                if (body == null) return;

                // 写回体型滑块
                _shapeValueBodyProp.SetValue(body, _savedShapeValues, null);

                if (mode == BodyLockMode.AllBody && _bustSoftnessProp != null && _bustWeightProp != null && _typeBoneProp != null)
                {
                    _bustSoftnessProp.SetValue(body, _savedBustSoftness, null);
                    _bustWeightProp.SetValue(body, _savedBustWeight, null);
                    _typeBoneProp.SetValue(body, _savedTypeBone, null);
                }

                // 刷新模型身材
                if (_updateShapeMethod != null)
                    _updateShapeMethod.Invoke(__instance.charInfo, null);
                if (mode == BodyLockMode.AllBody && _updateBustMethod != null)
                    _updateBustMethod.Invoke(__instance.charInfo, null);

                KKPEHeightLockPlugin.Log.LogDebug($"BodyPreserve: restored shape values (mode={mode})");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock BodyPreserve postfix error: " + e);
            }
            finally
            {
                // 无论成功失败,清空保存状态,避免误应用到下一次替换
                _savedShapeValues = null;
                _savedCoordinateBytes = null;
            }
        }

        /// <summary>恢复服装(坐标):把保存的字节数据赋给新角色。</summary>
        private static void RestoreCoordinate(Studio.OCIChar ociChar)
        {
            try
            {
                if (ociChar == null || ociChar.charInfo == null || _savedCoordinateBytes == null) return;
                var newCoord = new ChaFileCoordinate();
                if (newCoord.LoadBytes(_savedCoordinateBytes, ChaFileDefine.ChaFileCoordinateVersion))
                {
                    ociChar.charInfo.SetNowCoordinate(newCoord);
                    KKPEHeightLockPlugin.Log.LogDebug("BodyPreserve: restored clothes");
                }
                else
                {
                    KKPEHeightLockPlugin.Log.LogWarning("BodyPreserve: LoadBytes failed, clothes not restored");
                }
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("BodyPreserve: restore coordinate error: " + e.Message);
            }
        }

        /// <summary>延迟恢复姿势:通过协程等几帧,让新骨架完全构建后写回骨骼变换。
        /// pose 字典作为参数传入,避免多角色同帧替换时静态状态串扰。</summary>
        private static void SchedulePoseRestore(Studio.OCIChar ociChar, System.Collections.Generic.Dictionary<string, PoseData> pose)
        {
            try
            {
                var host = KKPEHeightLockPlugin.Instance;
                if (host == null) return;
                host.StartCoroutine(PoseRestoreRoutine(ociChar, pose));
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("BodyPreserve: schedule pose restore error: " + e.Message);
            }
        }

        private static System.Collections.IEnumerator PoseRestoreRoutine(Studio.OCIChar ociChar, System.Collections.Generic.Dictionary<string, PoseData> pose)
        {
            // 等 5 帧,让 ChangeChara 的骨架重建和 FK 初始化完成
            for (int i = 0; i < 5; i++)
                yield return null;

            if (pose == null || ociChar == null || ociChar.listBones == null) yield break;

            int restored = 0;
            foreach (var boneInfo in ociChar.listBones)
            {
                try
                {
                    if (boneInfo == null) continue;
                    var go = boneInfo.gameObject;
                    if (go == null) continue;
                    PoseData data;
                    if (!pose.TryGetValue(go.name, out data)) continue;
                    var t = go.transform;
                    if (t == null) continue;
                    t.localPosition = data.localPosition;
                    t.localRotation = data.localRotation;
                    t.localScale = data.localScale;
                    restored++;
                }
                catch (Exception) { }
            }
            KKPEHeightLockPlugin.Log.LogMessage($"BodyPreserve: restored {restored} bone transforms (pose)");
        }
    }
}
