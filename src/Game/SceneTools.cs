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
    /// 场景工具:去无用球体 + 去码。
    /// 逻辑参照 KK_SphereRemover v1.0.0 与 KK_StudioDecensor，但**不依赖
    /// KK_MaterialEditor**（本机没装）：直接改 Renderer 上那份渲染用的材质实例，
    /// 效果一样，少一个程序集依赖。
    /// </summary>
    public static class SceneTools
    {
        /// <summary>KK 的球体资源名。</summary>
        private const string SphereRendererKk = "item_O_Sphere";
        /// <summary>KKS 的球体资源名（道具表 group=14 / no=1002 → p_koi_stu_glow00_00）。
        /// 同一族还有 glow01~glow08（円柱/輪/リング/ペンライト），那些是正常装饰，不能一起藏。</summary>
        private const string SphereRendererKks = "p_koi_stu_glow00_00";
        private const int SphereGroup = 14;
        private const int SphereNo = 1002;

        private static bool IsSphereRenderer(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.StartsWith(SphereRendererKk, StringComparison.Ordinal)) return true;
            if (name.StartsWith(SphereRendererKks, StringComparison.Ordinal)) return true;
            return name.Contains("O_Sphere");
        }

        private static bool IsSphereItemInfo(Studio.OCIItem item)
        {
            try
            {
                if (item.itemInfo == null) return false;
                return item.itemInfo.group == SphereGroup && item.itemInfo.no == SphereNo;
            }
            catch (Exception) { return false; }
        }

        /// <summary>场景里所有道具（dicInfo 和 dicObjectCtrl 各取一次，去重）。</summary>
        private static System.Collections.Generic.List<Studio.OCIItem> CollectItems()
        {
            var list = new System.Collections.Generic.List<Studio.OCIItem>();
            var seen = new System.Collections.Generic.HashSet<Studio.OCIItem>();
            var studio = Singleton<Studio.Studio>.Instance;
            if (studio == null) return list;

            try
            {
                if (studio.dicInfo != null)
                {
                    foreach (var kv in studio.dicInfo)
                    {
                        var it = kv.Value as Studio.OCIItem;
                        if (it == null || it.objectItem == null) continue;
                        if (seen.Add(it)) list.Add(it);
                    }
                }
            }
            catch (Exception) { }

            try
            {
                if (studio.dicObjectCtrl != null)
                {
                    foreach (var kv in studio.dicObjectCtrl)
                    {
                        var it = kv.Value as Studio.OCIItem;
                        if (it == null || it.objectItem == null) continue;
                        if (seen.Add(it)) list.Add(it);
                    }
                }
            }
            catch (Exception) { }

            return list;
        }

        /// <summary>隐藏工作室里的无用球体(灯光/阴影辅助球)。只隐藏不删除，树里还能点回来。</summary>
        public static void RemoveUselessSpheres()
        {
            try
            {
                var items = CollectItems();
                int removed = 0, scanned = 0, hit = 0, already = 0;
                foreach (var item in items)
                {
                    scanned++;
                    try
                    {
                        var rs = item.objectItem.GetComponentsInChildren<Renderer>(true);
                        if (rs == null || rs.Length == 0) continue;
                        var r0 = rs[0];
                        if (r0 == null) continue;

                        bool byId = IsSphereItemInfo(item);
                        if (!byId && !IsSphereRenderer(r0.name)) continue;
                        hit++;

                        if (!item.visible) { already++; continue; }
                        // 必须走树节点的 SetVisible(false)：它会设 m_Visible → 回调 onVisible
                        // → 才真正把 OCIItem.visible 置 false 并更新树上的眼睛图标。
                        if (item.treeNodeObject != null) item.treeNodeObject.SetVisible(false);
                        else item.visible = false;
                        removed++;
                    }
                    catch (Exception) { }
                }
                KKPEHeightLockPlugin.Log.LogMessage(
                    $"SceneTools: 去无用球体 扫了 {scanned} 个道具，认出球体 {hit} 个"
                    + $"（已隐藏 {already}），这次藏了 {removed} 个");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("SceneTools.RemoveUselessSpheres error: " + e);
            }
        }

        /// <summary>去码:移除马赛克/圣光材质。参照 KK_StudioDecensor。</summary>
        public static void DecensorScene()
        {
            try
            {
                var items = CollectItems();
                Shader alphaShader = null;
                int fixedCount = 0;

                foreach (var oci in items)
                {
                    try
                    {
                        var renderers = oci.objectItem.GetComponentsInChildren<Renderer>(true);
                        if (renderers == null) continue;
                        foreach (var r in renderers)
                        {
                            if (r == null) continue;
                            var mats = r.materials;
                            if (mats == null) continue;
                            foreach (var mat in mats)
                            {
                                if (mat == null) continue;
                                // 材质名去掉 (Instance) 后缀再 Trim
                                string name = mat.name.Replace("(Instance)", "").Trim();

                                // 1) 像素马赛克:shader 是 Custom/Pixelate → PixelSize 设为 1
                                if (mat.shader != null && mat.shader.name == "Custom/Pixelate")
                                {
                                    mat.SetFloat("PixelSize", 1f);
                                    fixedCount++;
                                }
                                // 2) 圣光/模糊:材质名以 CensorEffectMat 或 blur_ 开头 → 换透明 shader 并 alpha=0
                                else if (name.StartsWith("CensorEffectMat") || name.StartsWith("blur_"))
                                {
                                    if (alphaShader == null)
                                        alphaShader = Shader.Find("Shader Forge/main_item_studio_alpha");
                                    if (alphaShader != null) mat.shader = alphaShader;
                                    mat.SetFloat("alpha", 0f);
                                    fixedCount++;
                                }
                            }
                        }
                    }
                    catch (Exception) { }
                }
                KKPEHeightLockPlugin.Log.LogMessage($"SceneTools: 去码 改了 {fixedCount} 个材质");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("SceneTools.DecensorScene error: " + e);
            }
        }
    }
}
