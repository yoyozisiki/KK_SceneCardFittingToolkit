using System;
using System.Collections.Generic;
using UnityEngine;

namespace KKPEHeightLock
{
    public static partial class AutoExport
    {
        // ======================================================== 体型快照
        /// <summary>一套体型数据（角色卡或场景原角色身上的快照）。只处理 female。</summary>
        public class BodySnap
        {
            public float[] shape;
            public float softness;
            public float weight;
            public int typeBone;

            /// <summary>乳量相关的滑块下标（4=胸サイズ，5~13 是位置/开合/角度/形状等）。</summary>
            private static readonly int[] BustIdx = { 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 };

            private class HeightEntry
            {
                public Studio.OCIChar oci;
                public Transform bone;
                public Vector3 scale;
            }

            private static readonly List<HeightEntry> _heights = new List<HeightEntry>();

            public static bool IsFemale(Studio.OCIChar oci)
            {
                try { return oci != null && oci.charInfo != null && oci.sex == 1; }
                catch (Exception) { return false; }
            }

            public static BodySnap Capture(Studio.OCIChar oci)
            {
                if (!IsFemale(oci)) return null;
                try
                {
                    var b = oci.charInfo.fileBody;
                    if (b == null) return null;
                    var d = new BodySnap();
                    if (b.shapeValueBody != null) d.shape = (float[])b.shapeValueBody.Clone();
                    d.softness = b.bustSoftness;
                    d.weight = b.bustWeight;
                    d.typeBone = b.typeBone;
                    return d;
                }
                catch (Exception e) { Log("读取体型失败：" + e.Message); return null; }
            }
            /// <summary>
            /// 把体型/乳量原样恢复成快照。换装不该改变体型，所以这里不走 Compose 的
            /// 来源开关，直接按快照写回。
            /// </summary>
            public static void RestoreAll(Studio.OCIChar oci, BodySnap snap)
            {
                if (!IsFemale(oci) || snap == null || snap.shape == null) return;
                try
                {
                    var b = oci.charInfo.fileBody;
                    if (b == null) return;

                    if (b.shapeValueBody != null && b.shapeValueBody.Length == snap.shape.Length)
                        Array.Copy(snap.shape, b.shapeValueBody, snap.shape.Length);
                    else
                        b.shapeValueBody = (float[])snap.shape.Clone();

                    b.bustSoftness = snap.softness;
                    b.bustWeight = snap.weight;

                    oci.charInfo.UpdateShapeBodyValueFromCustomInfo();
                    // SetCoordinateInfo 内部调过 UpdateBustSoftnessAndGravity()，这里再刷一次
                    try { oci.charInfo.UpdateBustSoftnessAndGravity(); } catch (Exception) { }
                }
                catch (Exception e) { Log("恢复体型失败：" + e.Message); }
            }

            /// <summary>
            /// 按三个开关把「角色卡」和「场景原角色」的体型合成一份写回：
            /// 先拿角色卡当底 → 按需整份换成场景的 → 再按需把乳量组盖上去。
            /// </summary>
            public static void Compose(Studio.OCIChar oci, BodySnap scene, BodySnap card, ExportOptions o)
            {
                if (!IsFemale(oci)) return;
                if (scene == null || card == null || card.shape == null) return;
                try
                {
                    var b = oci.charInfo.fileBody;
                    if (b == null) return;

                    float[] merged = (float[])card.shape.Clone();
                    if (o.ShapeFromScene && scene.shape != null && scene.shape.Length == merged.Length)
                        merged = (float[])scene.shape.Clone();

                    bool bustFromScene = o.BustFromScene;

                    if (bustFromScene)
                    {
                        if (scene.shape != null)
                            for (int i = 0; i < BustIdx.Length; i++)
                            {
                                int k = BustIdx[i];
                                if (k < merged.Length && k < scene.shape.Length) merged[k] = scene.shape[k];
                            }
                    }
                    else if (o.ShapeFromScene)
                    {
                        // 体型取场景、乳量取角色卡：整份是场景的，得把那十个换回卡的
                        for (int i = 0; i < BustIdx.Length; i++)
                        {
                            int k = BustIdx[i];
                            if (k < merged.Length && k < card.shape.Length) merged[k] = card.shape[k];
                        }
                    }

                    int n = b.shapeValueBody == null ? 0 : b.shapeValueBody.Length;
                    if (n == merged.Length) Array.Copy(merged, b.shapeValueBody, n);
                    else b.shapeValueBody = (float[])merged.Clone();

                    b.typeBone = o.BoneFromScene ? scene.typeBone : card.typeBone;
                    // 物理参数同样跟体型同源（bustFromScene 上面已经算过）
                    b.bustSoftness = bustFromScene ? scene.softness : card.softness;
                    b.bustWeight = bustFromScene ? scene.weight : card.weight;

                    oci.charInfo.UpdateShapeBodyValueFromCustomInfo();
                    oci.charInfo.UpdateBustSoftnessAndGravity();
                }
                catch (Exception e) { Log("体型合成失败：" + e.Message); }
            }

            public static void LockHeight(Studio.OCIChar oci)
            {
                if (!IsFemale(oci)) return;
                try
                {
                    string boneName = KKPEHeightLockPlugin.BoneName.Value;
                    var bone = FindBone(oci.charInfo.transform, boneName);
                    if (bone == null) { Log("找不到身高骨骼 " + boneName); return; }
                    HeightEntry e = null;
                    for (int i = 0; i < _heights.Count; i++)
                        if (_heights[i].oci == oci) { e = _heights[i]; break; }
                    if (e == null) { e = new HeightEntry(); e.oci = oci; _heights.Add(e); }
                    e.bone = bone;
                    e.scale = bone.localScale;
                }
                catch (Exception ex) { Log("锁定身高失败：" + ex.Message); }
            }

            /// <summary>每帧压回身高（在动画之后、VideoExport 截帧之前）。</summary>
            public static void TickHeight()
            {
                for (int i = _heights.Count - 1; i >= 0; i--)
                {
                    var e = _heights[i];
                    if (e.oci == null) { _heights.RemoveAt(i); continue; }
                    if (e.bone == null)
                    {
                        if (e.oci.charInfo == null) { _heights.RemoveAt(i); continue; }
                        e.bone = FindBone(e.oci.charInfo.transform, KKPEHeightLockPlugin.BoneName.Value);
                        if (e.bone == null) continue;
                    }
                    e.bone.localScale = e.scale;
                }
            }

            private static Transform FindBone(Transform parent, string name)
            {
                if (parent == null) return null;
                if (parent.name == name) return parent;
                for (int i = 0; i < parent.childCount; i++)
                {
                    var r = FindBone(parent.GetChild(i), name);
                    if (r != null) return r;
                }
                return null;
            }
        }
    }
}
