using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>
    /// 皮肤材质缓存：把模板卡的 cf_m_body / cf_m_face 一次性提取并序列化到本地，
    /// 之后应用皮肤只读文件 —— 避免 AddFemale 重建场景。
    /// 文件：BepInEx/config/SkinCache/skin_&lt;hash&gt;.bin
    /// </summary>
    public static class SkinMaterialCache
    {
        private const string Magic = "KKSKIN01";
        private const int Version = 2;   // v2: 不缓存 mask/alpha/overlay（按 UV 定制，跨角色会挖洞）

        private class TexSnap
        {
            public string name;
            public Vector2 offset;
            public Vector2 scale;
            public byte[] png;
        }

        private class MatSnap
        {
            public string name;
            public string shader;
            public int renderQueue;
            public List<KeyValuePair<string, float>> floats = new List<KeyValuePair<string, float>>();
            public List<KeyValuePair<string, Color>> colors = new List<KeyValuePair<string, Color>>();
            public List<KeyValuePair<string, Vector4>> vectors = new List<KeyValuePair<string, Vector4>>();
            public List<TexSnap> textures = new List<TexSnap>();
        }

        private static List<MatSnap> _cache;
        private static string _cacheKey;

        // ============================================================ 路径
        public static string CachePathFor(string cardPath)
        {
            try
            {
                var fi = new FileInfo(cardPath);
                long len = fi.Exists ? fi.Length : 0;
                long ticks = fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0;
                string key = cardPath.ToLowerInvariant() + "|" + len + "|" + ticks;
                // 简单稳定 hash（不引 MD5，避免额外的 using）
                uint h = 2166136261u;
                for (int i = 0; i < key.Length; i++) { h ^= key[i]; h *= 16777619u; }
                string dir = Path.Combine(BepInEx.Paths.ConfigPath, "SkinCache");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "skin_" + h.ToString("x8") + ".bin");
            }
            catch (Exception) { return null; }
        }

        public static bool HasCache(string cardPath)
        {
            var p = CachePathFor(cardPath);
            return !string.IsNullOrEmpty(p) && File.Exists(p);
        }

        // ============================================================ 提取（协程）
        /// <summary>
        /// 确保缓存存在。第一次会 AddFemale 临时加载模板卡并把材质存盘，之后直接返回。
        /// </summary>
        public static IEnumerator EnsureCache(string cardPath)
        {
            if (string.IsNullOrEmpty(cardPath) || !File.Exists(cardPath)) yield break;
            if (HasCache(cardPath)) yield break;       // 已缓存，什么都不做

            var studio = Singleton<Studio.Studio>.Instance;
            if (studio == null || studio.dicObjectCtrl == null) yield break;

            // 记下现有角色，用于排除临时角色 / 还原显隐
            var existing = new List<Studio.OCIChar>();
            var visAll = new List<bool>();
            foreach (var kv in studio.dicObjectCtrl)
            {
                var oc = kv.Value as Studio.OCIChar;
                if (oc == null || oc.charInfo == null) continue;
                existing.Add(oc);
                bool v = true;
                try { v = oc.charInfo.visibleAll; } catch (Exception) { }
                visAll.Add(v);
            }

            try { studio.AddFemale(cardPath); }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("SceneTools: 缓存皮肤时 AddFemale 失败：" + e.Message);
                yield break;
            }

            // 立刻隐藏临时角色
            for (int f = 0; f < 3; f++) yield return null;
            try
            {
                foreach (var kv in studio.dicObjectCtrl)
                {
                    var oc = kv.Value as Studio.OCIChar;
                    if (oc == null || existing.Contains(oc) || oc.charInfo == null) continue;
                    try { oc.charInfo.visibleAll = false; } catch (Exception) { }
                    try { if (oc.treeNodeObject != null) oc.treeNodeObject.SetVisible(false); } catch (Exception) { }
                }
            }
            catch (Exception) { }

            // 等模型建好
            Studio.OCICharFemale temp = null;
            for (int f = 0; f < 600; f++)
            {
                yield return null;
                try
                {
                    foreach (var kv in studio.dicObjectCtrl)
                    {
                        var oc = kv.Value as Studio.OCICharFemale;
                        if (oc == null || oc.charInfo == null || existing.Contains(oc)) continue;
                        var rs = oc.charInfo.GetComponentsInChildren<Renderer>(true);
                        if (rs != null && rs.Length > 0) { temp = oc; break; }
                    }
                }
                catch (Exception) { }
                if (temp != null) break;
            }

            if (temp != null)
            {
                for (int f = 0; f < 5; f++) yield return null;
                var snaps = ExtractFrom(temp);
                if (snaps.Count > 0) Save(cardPath, snaps);
                else KKPEHeightLockPlugin.Log.LogWarning("SceneTools: 模板卡里没找到 cf_m_body / cf_m_face");
            }
            else
            {
                KKPEHeightLockPlugin.Log.LogWarning("SceneTools: 缓存皮肤时模板角色没能加载出来");
            }

            // 删临时角色 + 还原显隐
            try
            {
                if (temp != null)
                {
                    var node = temp.treeNodeObject;
                    if (node != null) Studio.Studio.DeleteNode(node);
                    else temp.OnDelete();
                }
            }
            catch (Exception) { try { if (temp != null) temp.OnDelete(); } catch (Exception) { } }

            for (int i = 0; i < existing.Count && i < visAll.Count; i++)
            {
                try { existing[i].charInfo.visibleAll = visAll[i]; } catch (Exception) { }
            }
        }

        private static List<MatSnap> ExtractFrom(Studio.OCIChar src)
        {
            var list = new List<MatSnap>();
            try
            {
                var rs = src.charInfo.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i];
                    if (r == null) continue;
                    var ms = r.materials;
                    if (ms == null) continue;
                    for (int m = 0; m < ms.Length; m++)
                    {
                        var mat = ms[m];
                        if (mat == null || mat.shader == null) continue;
                        string key = CleanName(mat.name).ToLowerInvariant();
                        if (key != "cf_m_body" && key != "cf_m_face") continue;
                        var snap = Snapshot(mat);
                        if (snap != null) list.Add(snap);
                    }
                }
            }
            catch (Exception e) { KKPEHeightLockPlugin.Log.LogWarning("SceneTools: 提取皮肤材质失败：" + e.Message); }
            return list;
        }

        private static MatSnap Snapshot(Material mat)
        {
            try
            {
                var s = new MatSnap
                {
                    name = CleanName(mat.name),
                    shader = mat.shader.name,
                    renderQueue = mat.renderQueue
                };

                int cnt = mat.shader.GetPropertyCount();
                for (int i = 0; i < cnt; i++)
                {
                    string pn = null;
                    UnityEngine.Rendering.ShaderPropertyType pt;
                    try { pn = mat.shader.GetPropertyName(i); pt = mat.shader.GetPropertyType(i); }
                    catch (Exception) { continue; }
                    if (string.IsNullOrEmpty(pn)) continue;

                    try
                    {
                        switch (pt)
                        {
                            case UnityEngine.Rendering.ShaderPropertyType.Float:
                            case UnityEngine.Rendering.ShaderPropertyType.Range:
                                s.floats.Add(new KeyValuePair<string, float>(pn, mat.GetFloat(pn)));
                                break;
                            case UnityEngine.Rendering.ShaderPropertyType.Color:
                                s.colors.Add(new KeyValuePair<string, Color>(pn, mat.GetColor(pn)));
                                break;
                            case UnityEngine.Rendering.ShaderPropertyType.Vector:
                                var v = mat.GetVector(pn);
                                s.vectors.Add(new KeyValuePair<string, Vector4>(pn, v));
                                break;
                            case UnityEngine.Rendering.ShaderPropertyType.Texture:
                                // 遮罩/overlay 类是 UV 定制的，跨角色复制会把身体挖出洞，直接不缓存
                                string lpn = pn.ToLowerInvariant();
                                if (lpn.Contains("mask") || lpn.Contains("alpha")
                                    || lpn.Contains("overlay") || lpn.Contains("underlay"))
                                    break;
                                var tex = mat.GetTexture(pn) as Texture2D;
                                byte[] png = null;
                                if (tex != null)
                                {
                                    try { png = tex.EncodeToPNG(); } catch (Exception) { }
                                }
                                s.textures.Add(new TexSnap
                                {
                                    name = pn,
                                    offset = mat.GetTextureOffset(pn),
                                    scale = mat.GetTextureScale(pn),
                                    png = png
                                });
                                break;
                        }
                    }
                    catch (Exception) { }
                }
                return s;
            }
            catch (Exception) { return null; }
        }

        // ============================================================ 存 / 读
        private static void Save(string cardPath, List<MatSnap> snaps)
        {
            var path = CachePathFor(cardPath);
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                using (var w = new BinaryWriter(fs))
                {
                    w.Write(Magic);
                    w.Write(Version);
                    w.Write(snaps.Count);
                    for (int i = 0; i < snaps.Count; i++)
                    {
                        var s = snaps[i];
                        w.Write(s.name ?? "");
                        w.Write(s.shader ?? "");
                        w.Write(s.renderQueue);

                        w.Write(s.floats.Count);
                        for (int k = 0; k < s.floats.Count; k++)
                        {
                            w.Write(s.floats[k].Key ?? "");
                            w.Write(s.floats[k].Value);
                        }
                        w.Write(s.colors.Count);
                        for (int k = 0; k < s.colors.Count; k++)
                        {
                            w.Write(s.colors[k].Key ?? "");
                            var c = s.colors[k].Value;
                            w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a);
                        }
                        w.Write(s.vectors.Count);
                        for (int k = 0; k < s.vectors.Count; k++)
                        {
                            w.Write(s.vectors[k].Key ?? "");
                            var v = s.vectors[k].Value;
                            w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w);
                        }
                        w.Write(s.textures.Count);
                        for (int k = 0; k < s.textures.Count; k++)
                        {
                            var t = s.textures[k];
                            w.Write(t.name ?? "");
                            w.Write(t.offset.x); w.Write(t.offset.y);
                            w.Write(t.scale.x); w.Write(t.scale.y);
                            if (t.png != null) { w.Write(t.png.Length); w.Write(t.png); }
                            else w.Write(0);
                        }
                    }
                }
                KKPEHeightLockPlugin.Log.LogMessage(
                    "SceneTools: 皮肤材质已缓存 → " + Path.GetFileName(path)
                    + "（" + snaps.Count + " 个材质）");
            }
            catch (Exception e) { KKPEHeightLockPlugin.Log.LogWarning("SceneTools: 写皮肤缓存失败：" + e.Message); }
        }

        private static List<MatSnap> Load(string cardPath)
        {
            var path = CachePathFor(cardPath);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var r = new BinaryReader(fs))
                {
                    if (r.ReadString() != Magic) return null;
                    if (r.ReadInt32() != Version) return null;
                    int n = r.ReadInt32();
                    var list = new List<MatSnap>(n);
                    for (int i = 0; i < n; i++)
                    {
                        var s = new MatSnap();
                        s.name = r.ReadString();
                        s.shader = r.ReadString();
                        s.renderQueue = r.ReadInt32();

                        int fc = r.ReadInt32();
                        for (int k = 0; k < fc; k++) s.floats.Add(new KeyValuePair<string, float>(r.ReadString(), r.ReadSingle()));
                        int cc = r.ReadInt32();
                        for (int k = 0; k < cc; k++) s.colors.Add(new KeyValuePair<string, Color>(r.ReadString(), new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle())));
                        int vc = r.ReadInt32();
                        for (int k = 0; k < vc; k++) s.vectors.Add(new KeyValuePair<string, Vector4>(r.ReadString(), new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle())));
                        int tc = r.ReadInt32();
                        for (int k = 0; k < tc; k++)
                        {
                            var t = new TexSnap();
                            t.name = r.ReadString();
                            t.offset = new Vector2(r.ReadSingle(), r.ReadSingle());
                            t.scale = new Vector2(r.ReadSingle(), r.ReadSingle());
                            int len = r.ReadInt32();
                            if (len > 0) t.png = r.ReadBytes(len);
                            s.textures.Add(t);
                        }
                        list.Add(s);
                    }
                    return list;
                }
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("SceneTools: 读皮肤缓存失败（会重新生成）：" + e.Message);
                return null;
            }
        }

        // ============================================================ 应用
        /// <summary>把缓存的皮肤材质应用到场景里所有 female。不碰场景、不加角色。</summary>
        public static void ApplyToAll(string cardPath)
        {
            if (string.IsNullOrEmpty(cardPath)) return;
            if (_cache == null || _cacheKey != cardPath)
            {
                _cache = Load(cardPath);
                _cacheKey = cardPath;
            }
            if (_cache == null || _cache.Count == 0)
            {
                KKPEHeightLockPlugin.Log.LogWarning("SceneTools: 皮肤缓存为空，跳过应用");
                return;
            }

            var studio = Singleton<Studio.Studio>.Instance;
            if (studio == null || studio.dicObjectCtrl == null) return;

            int n = 0, fail = 0;
            foreach (var kv in studio.dicObjectCtrl)
            {
                var oc = kv.Value as Studio.OCIChar;
                if (oc == null || oc.sex != 1 || oc.charInfo == null) continue;
                try
                {
                    var snap = AutoExport.BodySnap.Capture(oc);
                    var rs = oc.charInfo.GetComponentsInChildren<Renderer>(true);
                    for (int i = 0; i < rs.Length; i++)
                    {
                        var r = rs[i];
                        if (r == null) continue;
                        var ms = r.materials;
                        if (ms == null) continue;
                        for (int m = 0; m < ms.Length; m++)
                        {
                            var mat = ms[m];
                            if (mat == null) continue;
                            string key = CleanName(mat.name).ToLowerInvariant();
                            MatSnap s = null;
                            for (int k = 0; k < _cache.Count; k++)
                            {
                                if (string.Equals(_cache[k].name, key, StringComparison.OrdinalIgnoreCase)) { s = _cache[k]; break; }
                            }
                            if (s == null) continue;
                            ApplySnap(s, mat);
                            n++;
                        }
                    }
                    oc.charInfo.ReloadNoAsync(true, false, true, false);
                    if (snap != null) AutoExport.BodySnap.RestoreAll(oc, snap);
                }
                catch (Exception e)
                {
                    fail++;
                    KKPEHeightLockPlugin.Log.LogWarning("SceneTools: 应用皮肤失败：" + e.Message);
                }
            }

            KKPEHeightLockPlugin.Log.LogMessage(
                "SceneTools: 已从本地缓存应用皮肤（cf_m_body/cf_m_face）给 "
                + (n > 0 ? "场景 female" : "0 个") + "，材质 " + n + " 个"
                + (fail > 0 ? "（失败 " + fail + "）" : ""));
        }

        private static void ApplySnap(MatSnap s, Material dst)
        {
            try
            {
                var sh = FindShader(s.shader);
                if (sh != null)
                {
                    // **别把透明版 shader 套到身体上**。
                    // 模板卡的 cf_m_body 可能是 KKUSSalpha（半透明版），照搬过来即使
                    // Color.a=1，shader 内部仍是 SrcAlpha/OneMinusSrcAlpha + ZWrite Off，
                    // 胸腔会透出去/看起来"消失"。名称里带 alpha 的一律换成不透明版。
                    string sn = s.shader ?? "";
                    if (sn.IndexOf("alpha", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string opaqueName = sn
                            .Replace("Alpha", "").Replace("alpha", "")
                            .Replace("_ALPHA", "").Replace("_alpha", "");
                        var os = FindShader(opaqueName);
                        if (os != null) sh = os;
                    }
                    dst.shader = sh;
                }

                // 强制不透明混合（能设就设，设不了就算了）
                try
                {
                    dst.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    dst.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    dst.SetInt("_ZWrite", 1);
                }
                catch (Exception) { }

                for (int i = 0; i < s.floats.Count; i++)
                {
                    var kv = s.floats[i];
                    try { if (dst.HasProperty(kv.Key)) dst.SetFloat(kv.Key, kv.Value); } catch (Exception) { }
                }
                for (int i = 0; i < s.colors.Count; i++)
                {
                    var kv = s.colors[i];
                    try
                    {
                        if (!dst.HasProperty(kv.Key)) continue;
                        var c = kv.Value;
                        // 皮肤不取透明度：模板卡可能是半透明的，照搬会让胸腔透出来
                        string low = kv.Key.ToLowerInvariant();
                        if (low.Contains("color")) c.a = 1f;
                        dst.SetColor(kv.Key, c);
                    }
                    catch (Exception) { }
                }
                for (int i = 0; i < s.vectors.Count; i++)
                {
                    var kv = s.vectors[i];
                    try { if (dst.HasProperty(kv.Key)) dst.SetVector(kv.Key, kv.Value); } catch (Exception) { }
                }
                for (int i = 0; i < s.textures.Count; i++)
                {
                    var t = s.textures[i];
                    try
                    {
                        if (!dst.HasProperty(t.name)) continue;

                        // **跳过 UV 相关的遮罩贴图**。
                        //
                        // 这是"胸腔消失"的根因：Koikatsu 的身体材质带 AlphaMask / BodyMask，
                        // 用来把穿模区域（比如胸、股间）从皮肤上"挖掉"，只显示衣服。
                        // 这些遮罩是按**角色自己的 UV/体型**画的，跨角色复制过去就是一堆错位的洞
                        // —— 表现就是胸腔直接没了。
                        //
                        // 社区的做法（kknow 的 MaterialEditor 教程）也是：复制 shader 之前
                        // 要把 UV 相关的自定义贴图 Reset 掉，只复制 shader 和不依赖 UV 的参数。
                        string lp = t.name.ToLowerInvariant();
                        if (lp.Contains("mask") || lp.Contains("alpha")
                            || lp.Contains("overlay") || lp.Contains("underlay"))
                            continue;
                        if (t.png != null && t.png.Length > 0)
                        {
                            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                            if (tex.LoadImage(t.png)) dst.SetTexture(t.name, tex);
                            else UnityEngine.Object.Destroy(tex);
                        }
                        dst.SetTextureOffset(t.name, t.offset);
                        dst.SetTextureScale(t.name, t.scale);
                    }
                    catch (Exception) { }
                }

                try { dst.renderQueue = 2000; } catch (Exception) { }
            }
            catch (Exception) { }
        }

        private static Shader FindShader(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                var all = Resources.FindObjectsOfTypeAll<Shader>();
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        var sh = all[i];
                        if (sh != null && sh.name == name) return sh;
                    }
                }
            }
            catch (Exception) { }
            try { return Shader.Find(name); } catch (Exception) { return null; }
        }

        private static string CleanName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("(Instance)", "").Replace("(Clone)", "").Trim();
        }
    }
}
