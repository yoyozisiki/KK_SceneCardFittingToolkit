using System;
using System.Collections;
using System.Collections.Generic;
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
    /// 场景工具：去无用球体 / 去遮挡物 / 去码 / 光照与过曝处理 / male 半透明。
    /// 直接改 Renderer 上的材质实例，不依赖 KK_MaterialEditor。
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

        /// <summary>
        /// 去掉场景里的遮挡物（只隐藏不删除，树里还能点回来）：
        ///   1. 「胶囊」遮挡道具（多语言显示名都认）；
        ///   2. Studio Text Plugin 生成的文字对象（"-Text Plugin:xxx"）。
        /// </summary>
        public static void RemoveObstructions()
        {
            int capsule = 0, text = 0;
            try
            {
                // ---- 1) 胶囊遮挡道具
                foreach (var item in CollectItems())
                {
                    try
                    {
                        // 道具没有名字字段，显示名在树节点上（就是 UI 里看到的「カプセル(通常)」）
                        string nm = null;
                        try { if (item.treeNodeObject != null) nm = item.treeNodeObject.textName; }
                        catch (Exception) { }
                        // 没有树节点就退回 renderer 资源名（部分道具名里带 capsule）
                        if (string.IsNullOrEmpty(nm))
                        {
                            try
                            {
                                var rs0 = item.objectItem != null
                                    ? item.objectItem.GetComponentsInChildren<Renderer>(true) : null;
                                if (rs0 != null && rs0.Length > 0 && rs0[0] != null) nm = rs0[0].name;
                            }
                            catch (Exception) { }
                        }
                        if (!IsCapsuleObstruction(nm)) continue;
                        if (!item.visible) continue;
                        // 走树节点才能同步眼睛图标；没有树节点就直接改 visible
                        if (item.treeNodeObject != null) item.treeNodeObject.SetVisible(false);
                        else item.visible = false;
                        capsule++;
                        KKPEHeightLockPlugin.Log.LogMessage(
                            $"SceneTools: 隐藏胶囊遮挡「{nm}」(group={SafeGroup(item)}, no={SafeNo(item)})");
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("SceneTools.RemoveObstructions(胶囊) error: " + e);
            }

            try
            {
                // ---- 2) Studio Text Plugin 的文字对象
                // 这些不是 OCIItem（不在 dicObjectCtrl 里），是插件自己建的 GameObject，
                // 名字形如 "-Text Plugin:pre-sex"，子对象是 "-Text Config"/"-Text Font:Arial"。
                // 隐藏根对象即可，子对象跟着一起消失。
                // Unity 5.6 没有 FindObjectsOfType<T>(bool)，用 Resources 版再过滤掉 prefab 资源
                var all = Resources.FindObjectsOfTypeAll<Transform>();
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        var t = all[i];
                        if (t == null) continue;
                        GameObject go;
                        try { go = t.gameObject; } catch (Exception) { continue; }
                        if (go == null) continue;
                        // 只要场景里的实例，不要 prefab/资源模板
                        try { if (!go.scene.IsValid()) continue; } catch (Exception) { }
                        string n = go.name;
                        if (string.IsNullOrEmpty(n)) continue;
                        if (!n.StartsWith("-Text Plugin", StringComparison.Ordinal)) continue;
                        if (!go.activeSelf) continue;
                        go.SetActive(false);
                        text++;
                        KKPEHeightLockPlugin.Log.LogMessage($"SceneTools: 隐藏文字遮挡「{n}」");
                    }
                }
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("SceneTools.RemoveObstructions(文字) error: " + e);
            }

            KKPEHeightLockPlugin.Log.LogMessage(
                $"SceneTools: 去遮挡物 胶囊 {capsule} 个 / 文字 {text} 个");
        }

        private static int SafeGroup(Studio.OCIItem item)
        {
            try { return item.itemInfo.group; } catch (Exception) { return -1; }
        }

        private static int SafeNo(Studio.OCIItem item)
        {
            try { return item.itemInfo.no; } catch (Exception) { return -1; }
        }

        /// <summary>
        /// 名字是不是「胶囊」遮挡道具。KKS 的显示名会随游戏语言变，所以四种语言都认：
        /// 日语 カプセル / 韩语 캡슐 / 中文 胶囊 / 英语 Capsule。
        /// </summary>
        private static bool IsCapsuleObstruction(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.IndexOf("カプセル", StringComparison.Ordinal) >= 0) return true;
            if (name.IndexOf("캡슐", StringComparison.Ordinal) >= 0) return true;
            if (name.IndexOf("胶囊", StringComparison.Ordinal) >= 0) return true;
            if (name.IndexOf("capsule", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>
        /// 把角色光照亮度压到 max 以内：只压 CameraLightCtrl 里那盏角色灯
        /// （KKUSS/KKUTS 材质几乎只吃它），不动场景灯光和环境光。
        /// LightCalc 是私有嵌套字段，走反射，并同步面板上的 slider / input。
        /// </summary>
        public static void ClampCharacterLighting(float max)
        {
            try
            {
                var studio = Singleton<Studio.Studio>.Instance;
                var ctrl = studio != null ? studio.cameraLightCtrl : null;
                if (ctrl == null)
                {
                    KKPEHeightLockPlugin.Log.LogMessage(
                        "SceneTools: 没找到角色光照控制器（CameraLightCtrl），跳过");
                    return;
                }

                const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                object calc = null;
                var fCalc = ctrl.GetType().GetField("lightChara", F);
                if (fCalc != null) calc = fCalc.GetValue(ctrl);

                Light light = null;
                if (calc != null)
                {
                    var fLight = calc.GetType().GetField("light", F);
                    if (fLight != null) light = fLight.GetValue(calc) as Light;
                }

                float before = light != null ? light.intensity : -1f;
                bool changed = false;
                if (light != null && light.intensity > max)
                {
                    light.intensity = max;
                    changed = true;
                }

                if (calc != null && changed)
                {
                    try
                    {
                        var fSlider = calc.GetType().GetField("sliderIntensity", F);
                        var slider = fSlider != null ? fSlider.GetValue(calc) as UnityEngine.UI.Slider : null;
                        if (slider != null && slider.value > max) slider.value = max;
                    }
                    catch (Exception) { }
                    try
                    {
                        var fInput = calc.GetType().GetField("inputIntensity", F);
                        var input = fInput != null ? fInput.GetValue(calc) as UnityEngine.UI.InputField : null;
                        if (input != null) input.text = max.ToString("0.##");
                    }
                    catch (Exception) { }
                    // 关键：LightCalc.UpdateUI() 会用内部值重刷 slider / input。
                    // 不调它的话，面板上还显示改之前的大数值（用户看到 1.2 就是这个原因）。
                    try
                    {
                        var mUi = calc.GetType().GetMethod("UpdateUI", F);
                        if (mUi != null) mUi.Invoke(calc, null);
                    }
                    catch (Exception) { }
                    try
                    {
                        var mRef = calc.GetType().GetMethod("Reflect", F);
                        if (mRef != null) mRef.Invoke(calc, null);
                    }
                    catch (Exception) { }
                }

                KKPEHeightLockPlugin.Log.LogMessage(
                    "SceneTools: 角色光照亮度 "
                    + (light != null ? (before.ToString("0.##") + " → " + light.intensity.ToString("0.##")) : "（没找到灯）")
                    + "（上限 " + max.ToString("0.##") + "，" + (changed ? "已下调" : "无需调整") + "）");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("SceneTools.ClampCharacterLighting error: " + e);
            }
        }

        /// <summary>
        /// KKUSS 材质过曝抑制：ShadowPower 越小越亮（默认 0.85，卡里常被调到 0.5），
        /// 这里把低于 minShadowPower 的抬回去。不动 Addcolor（作者调过的配色）。
        /// </summary>
        public static void ClampKkussOverexposure(float minShadowPower = 0.85f)
        {
            int scanned = 0, spChanged = 0;
            float spMin = 9f;
            try
            {
                var studio = Singleton<Studio.Studio>.Instance;
                if (studio == null || studio.dicObjectCtrl == null) return;

                foreach (var kv in studio.dicObjectCtrl)
                {
                    var oc = kv.Value as Studio.OCIChar;
                    if (oc == null || oc.charInfo == null) continue;
                    try
                    {
                        var renderers = oc.charInfo.GetComponentsInChildren<Renderer>(true);
                        if (renderers == null) continue;
                        for (int i = 0; i < renderers.Length; i++)
                        {
                            var r = renderers[i];
                            if (r == null) continue;
                            var mats = r.materials;
                            if (mats == null) continue;
                            for (int m = 0; m < mats.Length; m++)
                            {
                                var mat = mats[m];
                                if (mat == null || mat.shader == null) continue;
                                // 不按 shader 名过滤：凡是带 ShadowPower 属性的材质都处理，
                                // 原生 shader / KKUSS / xukmi 都可能暴露这个参数。
                                scanned++;

                                float sp;
                                if (GetFloatAny(mat, ShadowPowerNames, out sp))
                                {
                                    if (sp < spMin) spMin = sp;
                                    if (sp < minShadowPower - 0.001f)
                                    {
                                        SetFloatAny(mat, ShadowPowerNames, minShadowPower);
                                        spChanged++;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception) { }
                }
                KKPEHeightLockPlugin.Log.LogMessage(
                    "SceneTools: 过曝抑制 扫了 " + scanned + " 个材质"
                    + "，ShadowPower 抬回 0.85 的 " + spChanged + " 个"
                    + "（最低见到 " + (spMin < 9f ? spMin.ToString("0.##") : "无") + "）");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("SceneTools.ClampKkussOverexposure error: " + e);
            }
        }

        private static readonly string[] ShadowPowerNames = { "ShadowPower", "_ShadowPower", "shadowPower" };

        private static bool GetFloatAny(Material m, string[] names, out float value)
        {
            value = 0f;
            if (m == null || names == null) return false;
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    if (!m.HasProperty(names[i])) continue;
                    value = m.GetFloat(names[i]);
                    return true;
                }
                catch (Exception) { }
            }
            return false;
        }

        private static bool SetFloatAny(Material m, string[] names, float value)
        {
            if (m == null || names == null) return false;
            bool ok = false;
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    if (!m.HasProperty(names[i])) continue;
                    m.SetFloat(names[i], value);
                    ok = true;
                }
                catch (Exception) { }
            }
            return ok;
        }

        /// <summary>
        /// male 半透明：材质换 KKUSSalpha 并改 alpha。
        /// 换 shader 前必须先迁移贴图/颜色（_MainTex→MainTex、_Color→Color），否则贴图丢失变白。
        /// alpha: 0 = 不处理，1 = 不透明。
        /// </summary>
        public static void ApplyMaleTransparency(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            if (alpha >= 0.999f) return;

            try
            {
                var studio = Singleton<Studio.Studio>.Instance;
                if (studio == null || studio.dicObjectCtrl == null) return;

                Shader kkussAlpha = FindKkussAlphaShader();
                if (kkussAlpha == null)
                {
                    KKPEHeightLockPlugin.Log.LogWarning(
                        "SceneTools: 找不到 KKUSSalpha（KKUSS 包没装 / MaterialEditor 没加载），male 半透明跳过");
                    return;
                }

                int chars = 0, mats = 0, swapped = 0, unsupported = 0, failed = 0, genital = 0;
                string shaderLog = "";
                string bodyLog = "";
                string genitalLog = "";
                var bodyNames = new System.Collections.Generic.HashSet<string>();
                foreach (var kv in studio.dicObjectCtrl)
                {
                    var oc = kv.Value as Studio.OCIChar;
                    if (oc == null || oc.sex != 0 || oc.charInfo == null) continue;   // 0 = male
                    chars++;

                    // ---- 身体 / 衣服 / 饰品：全部走材质，保留原色只降 alpha。
                    // 不用 SetVisibleSimple —— 那会把身体换成纯色替身，不是半透明。
                    // 性器部件不透明化：按 renderer 名 + 材质名 + shader 多重匹配。
                    try
                    {
                        var renderers = oc.charInfo.GetComponentsInChildren<Renderer>(true);
                        if (renderers == null) continue;
                        for (int i = 0; i < renderers.Length; i++)
                        {
                            var r = renderers[i];
                            if (r == null) continue;

                            var rs = r.materials;
                            if (rs == null) continue;
                            for (int m = 0; m < rs.Length; m++)
                            {
                                var mat = rs[m];
                                if (mat == null) continue;

                                // 只把「身体/性器」相关名字记进日志，头发/脸太多会刷爆
                                string rn = CleanInstName(r.name);
                                string mn = CleanInstName(mat.name);
                                string shn = mat.shader != null ? CleanInstName(mat.shader.name) : "";
                                if (IsBodyKeyword(rn) || IsBodyKeyword(mn) || IsBodyKeyword(shn))
                                {
                                    string key = rn + "/" + mn;
                                    if (!bodyNames.Contains(key) && bodyLog.Length < 1500)
                                    {
                                        bodyNames.Add(key);
                                        bodyLog += key + ",";
                                    }
                                }

                                // 性器四重判定：父节点链 / renderer 名 / 材质名 / shader 名
                                if (IsUnderGenitalNode(r.transform)
                                    || IsGenitalMaterial(r.name)
                                    || IsGenitalMaterial(mat.name)
                                    || IsGenitalShader(mat.shader))
                                {
                                    genital++;
                                    if (genitalLog.Length < 600)
                                        genitalLog += GetNodePath(r.transform, 4) + "→" + mn + ",";
                                    continue;
                                }
                                if (MakeTransparent(mat, alpha, kkussAlpha,
                                        ref swapped, ref unsupported, ref shaderLog)) mats++;
                                else failed++;
                            }
                        }
                    }
                    catch (Exception) { }
                }
                KKPEHeightLockPlugin.Log.LogMessage(
                    "SceneTools: male 半透明 处理 " + chars + " 个角色 / " + mats + " 个材质"
                    + "（换 KKUSSalpha " + swapped + " / 失败 " + unsupported + " / 无 alpha 属性 " + failed
                    + " / 跳过性器材质 " + genital
                    + "，alpha=" + alpha.ToString("0.##") + "）");
                if (!string.IsNullOrEmpty(bodyLog))
                    KKPEHeightLockPlugin.Log.LogMessage("SceneTools: male 身体/性器 名单：" + bodyLog);
                if (!string.IsNullOrEmpty(genitalLog))
                    KKPEHeightLockPlugin.Log.LogMessage("SceneTools: male 跳过的性器节点：" + genitalLog);
                if (!string.IsNullOrEmpty(shaderLog))
                    KKPEHeightLockPlugin.Log.LogMessage("SceneTools: male 材质原 shader：" + shaderLog);
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("SceneTools.ApplyMaleTransparency error: " + e);
            }
        }

        /// <summary>
        /// 是不是男性性器相关的名字（这些不透明化）。
        /// 命名取自 abdata 里的真实节点：n_dankon / o_mnpb / o_dan_f / o_gomu，
        /// 不是 penis / kokan（kokan 是股间判定体）。
        /// </summary>
        private static readonly string[] GenitalKeys =
        {
            "dankon", "mnpb", "mnpa", "dan_f", "goma",
            "penis", "peni", "penetrat", "chimpo", "manko"
        };

        private static bool IsGenitalMaterial(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            for (int i = 0; i < GenitalKeys.Length; i++)
                if (n.IndexOf(GenitalKeys[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <summary>材质用的 shader 是不是男根专用（Shader Forge/mnpb 之类）。</summary>
        private static bool IsGenitalShader(Shader sh)
        {
            if (sh == null) return false;
            string n = sh.name ?? "";
            if (n.IndexOf("mnpb", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("dankon", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>
        /// 从 renderer 往上爬父节点，判断是不是挂在「股间」分组下。
        /// 性器的 renderer 名通常就是 cf_O_body，真正的标识在父节点链上
        /// （n_dankon / o_mnpb / o_gomu…）。
        /// </summary>
        private static bool IsUnderGenitalNode(Transform t)
        {
            int depth = 0;
            while (t != null && depth < 10)
            {
                string n = null;
                try { n = t.name; } catch (Exception) { }
                if (!string.IsNullOrEmpty(n))
                {
                    string low = n.ToLowerInvariant();
                    // 父节点链上任一段命中就算（dankon / mnpb / kokan / 股间）
                    for (int i = 0; i < GenitalKeys.Length; i++)
                        if (low.IndexOf(GenitalKeys[i], StringComparison.Ordinal) >= 0) return true;
                    if (low.IndexOf("kokan", StringComparison.Ordinal) >= 0) return true;
                }
                try { t = t.parent; } catch (Exception) { break; }
                depth++;
            }
            return false;
        }

        /// <summary>取节点的父链（最多 depth 层），日志里定位用。</summary>
        private static string GetNodePath(Transform t, int depth)
        {
            string path = "";
            int d = 0;
            while (t != null && d < depth)
            {
                string n = null;
                try { n = t.name; } catch (Exception) { }
                if (n != null) path = (path.Length > 0) ? (n + "/" + path) : n;
                try { t = t.parent; } catch (Exception) { break; }
                d++;
            }
            return path;
        }

        /// <summary>去掉材质名的 (Instance)/(Clone) 后缀，日志更短。</summary>
        private static string CleanInstName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("(Instance)", "").Replace("(Clone)", "").Trim();
        }

        /// <summary>是不是身体/性器相关的名字 —— 只把这些记进日志，避免被头发/脸刷爆。</summary>
        private static bool IsBodyKeyword(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string n = s.ToLowerInvariant();
            string[] keys =
            {
                "body", "dankon", "mnpb", "mnpa", "dan_f", "goma",
                "kokan", "penis", "skin", "cm_body", "hit_"
            };
            for (int i = 0; i < keys.Length; i++)
                if (n.IndexOf(keys[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <summary>
        /// 找 KKUSSalpha 这个 shader：依次试已加载的 Shader、Shader.Find、
        /// MaterialEditor 的 LoadedShaders（KKUSSalpha 平时没有材质引用，后者最稳）。
        /// </summary>
        private static Shader FindKkussAlphaShader()
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll<Shader>();
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        var sh = all[i];
                        if (sh == null) continue;
                        string n = sh.name ?? "";
                        if (n == "KKUSSalpha" || n.EndsWith("/KKUSSalpha", StringComparison.OrdinalIgnoreCase))
                            return sh;
                    }
                }
            }
            catch (Exception) { }

            var direct = Shader.Find("KKUSSalpha");
            if (direct != null) return direct;

            try
            {
                var t = Type.GetType("MaterialEditorAPI.MaterialEditorPluginBase, KKS_MaterialEditor");
                var f = t != null
                    ? t.GetField("LoadedShaders", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    : null;
                var dict = f != null ? f.GetValue(null) as System.Collections.IDictionary : null;
                if (dict != null && dict.Contains("KKUSSalpha"))
                {
                    var data = dict["KKUSSalpha"];
                    if (data != null)
                    {
                        var p = data.GetType().GetProperty("Shader");
                        var sh = p != null ? p.GetValue(data, null) as Shader : null;
                        if (sh != null) return sh;
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>
        /// 把一个材质换成 KKUSSalpha 并设透明度。返回是否真的动了它。
        ///
        /// 关键顺序：**先读旧贴图/颜色 → 再换 shader → 再写回**。
        /// 原生 shader 的属性名是 _MainTex/_Color，KKUSS 是 MainTex/Color，
        /// 换完再读旧属性就取不到了，衣服会变纯白。
        /// </summary>
        private static bool MakeTransparent(Material mat, float alpha, Shader kkussAlpha,
                                            ref int swapped, ref int unsupported, ref string logShaders)
        {
            if (mat == null) return false;
            string sn = mat.shader != null ? mat.shader.name : "";
            if (logShaders.Length < 700)
                logShaders += (logShaders.Length > 0 ? " | " : "") + (sn.Length > 0 ? sn : "?");

            if (kkussAlpha == null) { unsupported++; return false; }

            // --- 先捞旧值（换 shader 后旧属性就没了）
            Texture mainTex = GetTex(mat, "_MainTex");
            if (mainTex == null) mainTex = GetTex(mat, "MainTex");
            Texture normalMap = GetTex(mat, "_NormalMap");
            if (normalMap == null) normalMap = GetTex(mat, "NormalMap");
            Texture alphaMask = GetTex(mat, "_AlphaMask");
            if (alphaMask == null) alphaMask = GetTex(mat, "AlphaMask");
            Texture colorMask = GetTex(mat, "_ColorMask");
            if (colorMask == null) colorMask = GetTex(mat, "ColorMask");

            Color color = GetColorProp(mat, "_Color", GetColorProp(mat, "Color", Color.white));
            Color color2 = GetColorProp(mat, "_ShadowColor", GetColorProp(mat, "Color2", Color.white));
            Color lineColor = GetColorProp(mat, "_LineColor", GetColorProp(mat, "LineColor", Color.white));

            if (mat.shader != kkussAlpha)
            {
                try { mat.shader = kkussAlpha; swapped++; }
                catch (Exception) { unsupported++; return false; }
            }

            // --- 写回（KKUSS 属性名不带下划线）
            try { if (mainTex != null && mat.HasProperty("MainTex")) mat.SetTexture("MainTex", mainTex); } catch (Exception) { }
            try { if (normalMap != null && mat.HasProperty("NormalMap")) mat.SetTexture("NormalMap", normalMap); } catch (Exception) { }
            try { if (alphaMask != null && mat.HasProperty("AlphaMask")) mat.SetTexture("AlphaMask", alphaMask); } catch (Exception) { }
            try { if (colorMask != null && mat.HasProperty("ColorMask")) mat.SetTexture("ColorMask", colorMask); } catch (Exception) { }

            bool touched = false;
            // KKUSS 的 manifest 写的是不带下划线的 Color / Color2 / LineColor，
            // 但 Unity 侧实际属性名可能是 _Color / _Color2 / _LineColor —— 两种都写。
            try
            {
                color.a = alpha;
                if (mat.HasProperty("Color")) { mat.SetColor("Color", color); touched = true; }
                if (mat.HasProperty("_Color")) { mat.SetColor("_Color", color); touched = true; }
            }
            catch (Exception) { }
            try
            {
                color2.a = alpha;
                if (mat.HasProperty("Color2")) mat.SetColor("Color2", color2);
                if (mat.HasProperty("_Color2")) mat.SetColor("_Color2", color2);
            }
            catch (Exception) { }
            try
            {
                lineColor.a = alpha;
                if (mat.HasProperty("LineColor")) mat.SetColor("LineColor", lineColor);
                if (mat.HasProperty("_LineColor")) mat.SetColor("_LineColor", lineColor);
            }
            catch (Exception) { }

            // 用户指定：改 KKUSSalpha 的 alpha。属性名可能是 alpha / _alpha / _Alpha，
            // 不靠猜 —— 遍历 shader 属性，凡是名字含 alpha/opacity/transparen 的 Float/Range 都设一遍。
            int propCount = 0;
            try { propCount = mat.shader.GetPropertyCount(); } catch (Exception) { propCount = 0; }
            for (int pi = 0; pi < propCount; pi++)
            {
                string pn = null;
                try { pn = mat.shader.GetPropertyName(pi); } catch (Exception) { }
                if (string.IsNullOrEmpty(pn)) continue;
                string low = pn.ToLowerInvariant();
                if (low.IndexOf("alpha", StringComparison.Ordinal) < 0
                    && low.IndexOf("opacity", StringComparison.Ordinal) < 0
                    && low.IndexOf("transparen", StringComparison.Ordinal) < 0) continue;
                try
                {
                    var pt = mat.shader.GetPropertyType(pi);
                    if (pt == UnityEngine.Rendering.ShaderPropertyType.Float
                        || pt == UnityEngine.Rendering.ShaderPropertyType.Range)
                    {
                        mat.SetFloat(pn, alpha);
                        touched = true;
                    }
                }
                catch (Exception) { }
            }

            if (!touched)
            {
                // 兜底：常见写法逐个试
                string[] names = { "alpha", "_alpha", "Alpha", "_Alpha", "_Opacity", "Opacity", "_Transparency" };
                for (int i = 0; i < names.Length; i++)
                {
                    try
                    {
                        if (!mat.HasProperty(names[i])) continue;
                        mat.SetFloat(names[i], alpha);
                        touched = true;
                    }
                    catch (Exception) { }
                }
            }

            // 诊断：仍然没改到，就把属性名列出来，下次看日志能直接看到真名
            if (!touched && logShaders.Length < 1200)
            {
                string props = "";
                int n2 = 0;
                try { n2 = mat.shader.GetPropertyCount(); } catch (Exception) { n2 = 0; }
                for (int pi = 0; pi < n2 && props.Length < 200; pi++)
                {
                    try { props += mat.shader.GetPropertyName(pi) + ","; } catch (Exception) { }
                }
                logShaders += " [props:" + props + "]";
            }

            try { mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent; } catch (Exception) { }
            return touched;
        }

        private static Texture GetTex(Material m, string prop)
        {
            try { return (m != null && m.HasProperty(prop)) ? m.GetTexture(prop) : null; }
            catch (Exception) { return null; }
        }

        private static Color GetColorProp(Material m, string prop, Color def)
        {
            try { return (m != null && m.HasProperty(prop)) ? m.GetColor(prop) : def; }
            catch (Exception) { return def; }
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
