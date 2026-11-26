using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>
    /// 全局皮肤：把模板卡作者在 MaterialEditor 里做过的编辑缓存下来，再整份套到场景里所有 female 的身体/脸材质。
    ///
    /// 数据来源和 MaterialEditor 的 Copy Edits 一致（控制器里的 CopyData），因此只写作者改过的
    /// shader / 浮点 / 颜色 / 贴图（含贴图平铺），不碰作者没动过的 _AlphaMaskMode、_Cutoff、混合模式，
    /// 身体不会被错误镂空。缓存文件：BepInEx/config/SkinCache/skin_&lt;hash&gt;.bin
    /// </summary>
    public static class SkinMaterialCache
    {
        private const string Magic = "KKSKIN01";
        private const int Version = 13;    // 缓存格式/取材质规则变了就 +1，旧文件自动重建

        private class TexSnap
        {
            public string name;
            public Vector2 offset, scale;
            public bool hasOffset, hasScale;   // 记录里没这个值就别写，免得把 tiling 写成 0
            public byte[] png;
        }

        private class MatSnap
        {
            public string name;
            public string shader;
            public int renderQueue;
            public List<KeyValuePair<string, float>> floats = new List<KeyValuePair<string, float>>();
            public List<KeyValuePair<string, Color>> colors = new List<KeyValuePair<string, Color>>();
            public List<TexSnap> textures = new List<TexSnap>();
        }

        private static List<MatSnap> _cache;
        private static string _cacheKey;

        // ======================================================== 缓存路径

        public static string CachePathFor(string cardPath)
        {
            try
            {
                var fi = new FileInfo(cardPath);
                long len = fi.Exists ? fi.Length : 0;
                long ticks = fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0;
                string key = cardPath.ToLowerInvariant() + "|" + len + "|" + ticks;
                uint h = 2166136261u;                       // FNV-1a，够用且不引 MD5
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
            if (string.IsNullOrEmpty(p) || !File.Exists(p)) return false;
            try
            {
                using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read))
                using (var r = new BinaryReader(fs))
                    return r.ReadString() == Magic && r.ReadInt32() == Version;
            }
            catch (Exception) { return false; }
        }

        // ======================================================== 提取（协程）

        /// <summary>确保缓存存在：第一次临时加载模板卡把材质存盘，之后直接返回。</summary>
        public static IEnumerator EnsureCache(string cardPath)
        {
            if (string.IsNullOrEmpty(cardPath) || !File.Exists(cardPath)) yield break;
            if (HasCache(cardPath)) yield break;

            var studio = Singleton<Studio.Studio>.Instance;
            if (studio == null || studio.dicObjectCtrl == null) yield break;

            // 记下现有角色：既要排除临时角色，也要还原显隐
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

            // 立刻藏起来，别让玩家看到临时角色
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
                else KKPEHeightLockPlugin.Log.LogWarning("SceneTools: 模板卡里没找到 cf_m_body / cf_m_face 的材质编辑记录");
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
                var go = src.charInfo != null ? src.charInfo.gameObject : null;
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
                        if (mat == null) continue;
                        string key = SkinBase(mat.name);
                        if (key == null) continue;

                        Me.CopyEdits(go, mat);                 // 把该材质的编辑抓进 ME 的 CopyData
                        var snap = Me.MakeSnap(key);           // 只取作者改过的属性
                        if (snap != null && Find(list, key) == null) list.Add(snap);
                    }
                }
            }
            catch (Exception e) { KKPEHeightLockPlugin.Log.LogWarning("SceneTools: 提取皮肤材质失败：" + e.Message); }
            return list;
        }

        private static MatSnap Find(List<MatSnap> list, string key)
        {
            if (key == null) return null;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].name, key, StringComparison.OrdinalIgnoreCase)) return list[i];
            return null;
        }

        // ======================================================== 存 / 读

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
                        w.Write(s.textures.Count);
                        for (int k = 0; k < s.textures.Count; k++)
                        {
                            var t = s.textures[k];
                            w.Write(t.name ?? "");
                            w.Write(t.offset.x); w.Write(t.offset.y);
                            w.Write(t.scale.x); w.Write(t.scale.y);
                            w.Write(t.hasOffset); w.Write(t.hasScale);
                            if (t.png != null) { w.Write(t.png.Length); w.Write(t.png); }
                            else w.Write(0);
                        }
                    }
                }

                int tex = 0, withPng = 0;
                for (int i = 0; i < snaps.Count; i++)
                    for (int k = 0; k < snaps[i].textures.Count; k++)
                    {
                        tex++;
                        var png = snaps[i].textures[k].png;
                        if (png != null && png.Length > 0) withPng++;
                    }
                KKPEHeightLockPlugin.Log.LogMessage(
                    "SceneTools: 皮肤材质已缓存 → " + Path.GetFileName(path)
                    + "（" + snaps.Count + " 个材质，" + tex + " 张贴图，其中 " + withPng + " 张有数据）");
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
                    if (r.ReadString() != Magic || r.ReadInt32() != Version) return null;
                    int n = r.ReadInt32();
                    var list = new List<MatSnap>(n);
                    for (int i = 0; i < n; i++)
                    {
                        var s = new MatSnap();
                        s.name = r.ReadString();
                        s.shader = r.ReadString();
                        s.renderQueue = r.ReadInt32();

                        int fc = r.ReadInt32();
                        for (int k = 0; k < fc; k++)
                            s.floats.Add(new KeyValuePair<string, float>(r.ReadString(), r.ReadSingle()));

                        int cc = r.ReadInt32();
                        for (int k = 0; k < cc; k++)
                        {
                            string key = r.ReadString();
                            s.colors.Add(new KeyValuePair<string, Color>(key,
                                new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle())));
                        }

                        int tc = r.ReadInt32();
                        for (int k = 0; k < tc; k++)
                        {
                            var t = new TexSnap();
                            t.name = r.ReadString();
                            t.offset = new Vector2(r.ReadSingle(), r.ReadSingle());
                            t.scale = new Vector2(r.ReadSingle(), r.ReadSingle());
                            t.hasOffset = r.ReadBoolean();
                            t.hasScale = r.ReadBoolean();
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
                KKPEHeightLockPlugin.Log.LogWarning("SceneTools: 读皮肤缓存失败：" + e.Message);
                return null;
            }
        }

        // ======================================================== 应用

        /// <summary>
        /// 把缓存的皮肤材质应用到场景里所有 female。reapply=true 时跳过重建材质那几步，
        /// 只重写数值（用于隔帧补最后一笔，压过 ME 持久化材质的重建）。
        /// </summary>
        public static void ApplyToAll(string cardPath, bool reapply = false)
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

            int mats = 0, failed = 0;
            _shaderLog = "";
            _copiedTex.Clear();

            foreach (var kv in studio.dicObjectCtrl)
            {
                var oc = kv.Value as Studio.OCIChar;
                if (oc == null || oc.sex != 1 || oc.charInfo == null) continue;
                try
                {
                    var body = AutoExport.BodySnap.Capture(oc);

                    if (!reapply)
                    {
                        // 顺序很重要：先让角色把材质刷成自己的、铺好身体/脸基础材质，再写快照。
                        // 这三步都会重建材质，放在写快照之后就会把改动盖回去。
                        //
                        // noChangeHead 必须是 true：Reload 会顺手重建整颗头（FaceBlendShape 重造），
                        // 口型/表情的 openRate 不会回填，Timeline 的表情轨也会指向旧组件。
                        // 关掉重建头，只刷身体/脸的贴图，脸部基础材质由下面的 SetFaceBaseMaterial 负责。
                        oc.charInfo.ReloadNoAsync(true, true, true, false);
                        if (body != null) AutoExport.BodySnap.RestoreAll(oc, body);

                        var cc = oc.charInfo as ChaControl ?? oc.charInfo.GetComponent<ChaControl>();
                        if (cc != null)
                        {
                            cc.SetBodyBaseMaterial();
                            cc.SetFaceBaseMaterial();
                        }
                    }

                    var go = oc.charInfo.gameObject;
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
                            var snap = Find(_cache, SkinBase(mat.name));
                            if (snap == null) continue;
                            ApplyOne(go, mat, snap);
                            mats++;
                        }
                    }
                    // 写完不要再调 Reload / SetBodyBaseMaterial：会重建材质把改动盖掉。
                }
                catch (Exception e)
                {
                    failed++;
                    KKPEHeightLockPlugin.Log.LogWarning("SceneTools: 应用皮肤失败：" + e.Message);
                }
            }

            KKPEHeightLockPlugin.Log.LogMessage(
                "SceneTools: 全局皮肤应用 " + mats + " 个材质"
                + (failed > 0 ? "（失败 " + failed + "）" : "")
                + (_shaderLog.Length > 0 ? "；shader：" + _shaderLog : "")
                + (_copiedTex.Count > 0 ? "；贴图：" + string.Join(", ", _copiedTex.ToArray()) : ""));
        }

        private static void ApplyOne(GameObject go, Material mat, MatSnap s)
        {
            if (Me.Apply(go, mat, s)) return;

            // 控制器没能换 shader（少数材质 ME 不收），退一步直接赋 Shader 对象
            var sh = Me.FindShader(s.shader);
            if (sh == null) return;
            NoteShader((mat.shader != null ? mat.shader.name : "?") + " → " + (sh.name ?? "?"));
            mat.shader = sh;
        }

        // ======================================================== 小工具

        private static string _shaderLog = "";
        private static readonly List<string> _copiedTex = new List<string>();

        private static void NoteShader(string pair)
        {
            if (string.IsNullOrEmpty(pair) || _shaderLog.Length > 300) return;
            if (_shaderLog.IndexOf(pair, StringComparison.OrdinalIgnoreCase) >= 0) return;
            _shaderLog += (_shaderLog.Length > 0 ? " | " : "") + pair;
        }

        /// <summary>
        /// 只认 cf_m_ 后面那个英文词正好是 body / face 的材质：
        /// cf_m_body、cf_m_body_00、cf_m_body00 → body；cf_m_bodyteights（连裤袜）→ 不是。
        /// </summary>
        private static string SkinBase(string matName)
        {
            string n = CleanName(matName).ToLowerInvariant();
            string word = MatKind(n);
            if (word == "body") return "cf_m_body";
            if (word == "face") return "cf_m_face";
            return null;
        }

        /// <summary>取 "cf_m_" 之后连续的一串英文字母，且后面只能是数字 / 下划线（或直接结尾）。</summary>
        private static string MatKind(string n)
        {
            const string prefix = "cf_m_";
            if (!n.StartsWith(prefix)) return null;

            int i = prefix.Length, start = i;
            while (i < n.Length && n[i] >= 'a' && n[i] <= 'z') i++;
            if (i == start) return null;

            // 字母段之后只允许 cf_m_body_00 / cf_m_body00 这种，跟了别的字母（bodyteights、teights）就不算皮肤
            for (int k = i; k < n.Length; k++)
            {
                char c = n[k];
                if (c != '_' && (c < '0' || c > '9')) return null;
            }
            return n.Substring(start, i - start);
        }

        private static string CleanName(string s)
        {
            return string.IsNullOrEmpty(s)
                ? ""
                : s.Replace("(Instance)", "").Replace(" Instance", "").Trim();
        }

        // ======================================================== MaterialEditor 桥

        /// <summary>
        /// 反射调用 MaterialEditor 的角色控制器（MaterialEditorCharaController）。
        /// 它的 CopyData 里存着作者的全部编辑，贴图字节也在里面（mat.GetTexture 拿不到）。
        /// </summary>
        private static class Me
        {
            private const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            private const BindingFlags MS = BindingFlags.Public | BindingFlags.Instance;

            private static bool _init;
            private static object _charEnum;
            private static MethodInfo _copyEdits, _setShader, _setQueue, _setFloat, _setColor,
                                      _setTexture, _setTexOffset, _setTexScale;
            private static object _copyData;
            private static IDictionary _loadedShaders;
            private static FieldInfo _shaderField;

            private static void Init(MonoBehaviour ctrl)
            {
                if (_init || ctrl == null) return;
                _init = true;
                try
                {
                    var t = ctrl.GetType();
                    foreach (var m in t.GetMethods(MS))
                    {
                        int n = m.GetParameters().Length;
                        var ps = m.GetParameters();
                        if (m.Name == "MaterialCopyEdits" && n == 4) _copyEdits = m;
                        else if (m.Name == "SetMaterialShader" && n == 6) _setShader = m;
                        else if (m.Name == "SetMaterialShaderRenderQueue" && n == 6) _setQueue = m;
                        else if (m.Name == "SetMaterialFloatProperty" && n == 7) _setFloat = m;
                        else if (m.Name == "SetMaterialColorProperty" && n == 7) _setColor = m;
                        else if (m.Name == "SetMaterialTexture" && n == 6 && ps[4].ParameterType == typeof(byte[])) _setTexture = m;
                        else if (m.Name == "SetMaterialTextureOffset" && n == 7 && ps[4].ParameterType == typeof(Vector2)) _setTexOffset = m;
                        else if (m.Name == "SetMaterialTextureScale" && n == 7 && ps[4].ParameterType == typeof(Vector2)) _setTexScale = m;
                    }
                    if (_setShader != null)
                    {
                        var ot = _setShader.GetParameters()[1].ParameterType;
                        if (ot.IsEnum) _charEnum = Enum.Parse(ot, "Character");
                    }

                    // 静态 CopyData / LoadedShaders
                    var baseType = t.Assembly.GetType("MaterialEditorAPI.MaterialEditorPluginBase", false);
                    if (baseType != null)
                    {
                        var cd = baseType.GetField("CopyData", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                        _copyData = cd != null ? cd.GetValue(null) : null;
                        var ls = baseType.GetField("LoadedShaders", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                        _loadedShaders = ls != null ? ls.GetValue(null) as IDictionary : null;
                    }

                    KKPEHeightLockPlugin.Log.LogMessage("SceneTools: MaterialEditor 接口已就绪"
                        + "（CopyEdits=" + (_copyEdits != null) + "，SetShader=" + (_setShader != null)
                        + "，SetTexture=" + (_setTexture != null) + "，LoadedShaders="
                        + (_loadedShaders != null ? _loadedShaders.Count.ToString() : "无") + "）");
                }
                catch (Exception e)
                {
                    KKPEHeightLockPlugin.Log.LogWarning("SceneTools: 反射 MaterialEditor 失败：" + e.Message);
                }
            }

            private static GameObject _lastGo;
            private static MonoBehaviour _lastCtrl;

            private static MonoBehaviour Controller(GameObject go)
            {
                if (go == null) return null;
                if (_lastGo == go) return _lastCtrl;
                _lastGo = go;
                _lastCtrl = null;
                var mbs = go.GetComponents<MonoBehaviour>();
                for (int i = 0; i < mbs.Length; i++)
                    if (mbs[i] != null && mbs[i].GetType().Name == "MaterialEditorCharaController") { _lastCtrl = mbs[i]; break; }
                if (_lastCtrl != null) Init(_lastCtrl);
                return _lastCtrl;
            }

            /// <summary>把源材质的编辑抓进 CopyData（等价 MaterialEditor 的 Copy Edits）。</summary>
            public static void CopyEdits(GameObject go, Material mat)
            {
                var ctrl = Controller(go);
                if (ctrl == null || _copyEdits == null || _charEnum == null) return;
                try { _copyEdits.Invoke(ctrl, new object[] { 0, _charEnum, mat, go }); }
                catch (Exception) { }
            }

            /// <summary>读 CopyData → MatSnap。没有记录返回 null。</summary>
            public static MatSnap MakeSnap(string name)
            {
                if (_copyData == null) return null;
                try
                {
                    var t = _copyData.GetType();
                    var s = new MatSnap { name = name };

                    var shaders = Get(t, "MaterialShaderList");
                    if (shaders != null)
                        foreach (var it in shaders)
                        {
                            var ft = it.GetType();
                            var fn = ft.GetField("ShaderName");
                            var fr = ft.GetField("RenderQueue");
                            s.shader = fn != null ? fn.GetValue(it) as string : null;
                            var rq = fr != null ? fr.GetValue(it) : null;
                            if (rq is int) s.renderQueue = (int)rq;
                            break;
                        }

                    var floats = Get(t, "MaterialFloatPropertyList");
                    if (floats != null)
                        foreach (var it in floats)
                        {
                            var ft = it.GetType();
                            var fp = ft.GetField("Property");
                            var fv = ft.GetField("Value");
                            if (fp == null || fv == null) continue;
                            var p = fp.GetValue(it) as string;
                            if (string.IsNullOrEmpty(p)) continue;
                            s.floats.Add(new KeyValuePair<string, float>(p, (float)fv.GetValue(it)));
                        }

                    var colors = Get(t, "MaterialColorPropertyList");
                    if (colors != null)
                        foreach (var it in colors)
                        {
                            var ft = it.GetType();
                            var fp = ft.GetField("Property");
                            var fv = ft.GetField("Value");
                            if (fp == null || fv == null) continue;
                            var p = fp.GetValue(it) as string;
                            if (string.IsNullOrEmpty(p)) continue;
                            s.colors.Add(new KeyValuePair<string, Color>(p, (Color)fv.GetValue(it)));
                        }

                    var texs = Get(t, "MaterialTexturePropertyList");
                    if (texs != null)
                        foreach (var it in texs)
                        {
                            var ft = it.GetType();
                            var fp = ft.GetField("Property");
                            var fd = ft.GetField("Data");
                            var fo = ft.GetField("Offset");
                            var fs = ft.GetField("Scale");
                            if (fp == null) continue;
                            var p = fp.GetValue(it) as string;
                            if (string.IsNullOrEmpty(p)) continue;
                            var tx = new TexSnap { name = p, png = fd != null ? fd.GetValue(it) as byte[] : null };
                            var o = fo != null ? fo.GetValue(it) : null;
                            var sc = fs != null ? fs.GetValue(it) : null;
                            if (o is Vector2) { tx.offset = (Vector2)o; tx.hasOffset = true; }
                            if (sc is Vector2) { tx.scale = (Vector2)sc; tx.hasScale = true; }
                            s.textures.Add(tx);
                        }

                    if (string.IsNullOrEmpty(s.shader) && s.floats.Count == 0
                        && s.colors.Count == 0 && s.textures.Count == 0) return null;
                    return s;
                }
                catch (Exception) { return null; }
            }

            private static IEnumerable Get(Type t, string field)
            {
                var f = t.GetField(field, F);
                return f != null ? f.GetValue(_copyData) as IEnumerable : null;
            }

            /// <summary>把快照写到目标材质；返回 shader 是否已生效。</summary>
            public static bool Apply(GameObject go, Material mat, MatSnap s)
            {
                var ctrl = Controller(go);
                if (ctrl == null || _setShader == null || _charEnum == null) return false;
                try
                {
                    string before = mat.shader != null ? mat.shader.name : "?";

                    if (!string.IsNullOrEmpty(s.shader))
                        _setShader.Invoke(ctrl, new object[] { 0, _charEnum, mat, s.shader, go, true });

                    if (_setQueue != null)
                        try { _setQueue.Invoke(ctrl, new object[] { 0, _charEnum, mat, s.renderQueue, go, true }); }
                        catch (Exception) { }

                    for (int i = 0; i < s.floats.Count; i++)
                        try { _setFloat.Invoke(ctrl, new object[] { 0, _charEnum, mat, s.floats[i].Key, s.floats[i].Value, go, true }); }
                        catch (Exception) { }

                    for (int i = 0; i < s.colors.Count; i++)
                        try
                        {
                            var c = s.colors[i].Value;
                            if (s.colors[i].Key.IndexOf("color", StringComparison.OrdinalIgnoreCase) >= 0) c.a = 1f;
                            _setColor.Invoke(ctrl, new object[] { 0, _charEnum, mat, s.colors[i].Key, c, go, true });
                        }
                        catch (Exception) { }

                    for (int i = 0; i < s.textures.Count; i++)
                    {
                        var tx = s.textures[i];
                        if (tx.png != null && tx.png.Length > 0)
                            try
                            {
                                _setTexture.Invoke(ctrl, new object[] { 0, _charEnum, mat, tx.name, tx.png, go });
                                if (!_copiedTex.Contains(tx.name)) _copiedTex.Add(tx.name);
                            }
                            catch (Exception) { }
                        if (tx.hasOffset && _setTexOffset != null)
                            try { _setTexOffset.Invoke(ctrl, new object[] { 0, _charEnum, mat, tx.name, tx.offset, go, true }); }
                            catch (Exception) { }
                        if (tx.hasScale && _setTexScale != null)
                            try { _setTexScale.Invoke(ctrl, new object[] { 0, _charEnum, mat, tx.name, tx.scale, go, true }); }
                            catch (Exception) { }
                    }

                    bool ok = mat.shader != null && string.Equals(mat.shader.name, s.shader, StringComparison.OrdinalIgnoreCase);
                    if (ok) NoteShader(before + " → " + (mat.shader != null ? mat.shader.name : "?"));
                    return ok;
                }
                catch (Exception e)
                {
                    KKPEHeightLockPlugin.Log.LogWarning("SceneTools: 写材质失败：" + e.Message);
                    return false;
                }
            }

            /// <summary>从 MaterialEditor 的 LoadedShaders 取真实 Shader 对象（zipmod shader 用 Shader.Find 找不到）。</summary>
            public static Shader FindShader(string name)
            {
                if (string.IsNullOrEmpty(name) || _loadedShaders == null) return null;
                try
                {
                    foreach (DictionaryEntry e in _loadedShaders)
                    {
                        string key = e.Key as string;
                        if (key == null || key.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var data = e.Value;
                        if (data == null) continue;
                        if (_shaderField == null) _shaderField = data.GetType().GetField("Shader");
                        var sh = _shaderField != null ? _shaderField.GetValue(data) as Shader : null;
                        if (sh != null) return sh;
                    }
                }
                catch (Exception) { }
                return null;
            }
        }
    }
}
