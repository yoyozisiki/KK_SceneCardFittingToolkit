using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KKPEHeightLock
{
    /// <summary>
    /// 挡住「点插件界面 → 背后的 Studio 界面也跟着点」的穿透。
    ///
    /// 起因：插件的窗口是 IMGUI 画的，而工作室自己的按钮是 uGUI。两套系统各点各的 ——
    /// 鼠标按在插件面板上时，uGUI 的射线照样会打到面板**后面**那些按钮
    /// （最典型的就是右上角的「保存」，点一下场景卡的折叠箭头就顺手把场景存了）。
    ///
    /// 办法：在本插件窗口下面铺一层几乎全透明、但 raycastTarget = true 的 uGUI 图片，
    /// 排序层压到最高。它只覆盖**本插件窗口真正占的地方**，所以面板之外的点击
    /// 完全不受影响，Studio 照常能用。
    ///
    /// 透明度给 0.004 而不是 0：Unity 会把全透明的图元直接剔除（canvasRenderer.cull），
    /// 被剔除的图元不参与射线检测 —— 那样就白铺了。
    /// </summary>
    internal static class UiClickBlock
    {
        private const int MaxRects = 8;          // 同时最多挡这么多块（主窗/导出面板/两个弹框/下拉浮层…）
        private static readonly Color BlockColor = new Color(0f, 0f, 0f, 0.004f);

        private static GameObject _root;
        private static readonly List<RectTransform> _blocks = new List<RectTransform>();
        private static readonly List<Rect> _applied = new List<Rect>();   // 上一帧真正写进 RectTransform 的值
        private static bool _broken;

        /// <summary>每帧调用一次：传入要挡住的**屏幕像素**矩形（左上有原点）。空 = 全部撤掉。</summary>
        public static void Set(List<Rect> screenRects)
        {
            if (_broken) return;
            try
            {
                int n = (screenRects != null) ? Mathf.Min(screenRects.Count, MaxRects) : 0;
                if (n == 0) { Hide(); return; }

                if (!Ensure()) return;
                if (!_root.activeSelf) _root.SetActive(true);

                // 位置没变就不碰 RectTransform：每帧改一次会让画布每帧重建一次。
                bool same = (_applied.Count == n);
                if (same)
                    for (int i = 0; i < n; i++)
                        if (_applied[i] != screenRects[i]) { same = false; break; }

                for (int i = 0; i < _blocks.Count; i++)
                {
                    var rt = _blocks[i];
                    if (rt == null) continue;
                    if (i >= n) { SetActive(rt.gameObject, false); continue; }

                    Rect r = screenRects[i];
                    if (r.width < 2f || r.height < 2f) { SetActive(rt.gameObject, false); continue; }

                    if (!same)
                    {
                        // ScreenSpaceOverlay 画布的坐标系是「左下原点、像素」，要翻一下 Y
                        rt.sizeDelta = new Vector2(r.width, r.height);
                        rt.anchoredPosition = new Vector2(r.x, Screen.height - r.y - r.height);
                    }
                    SetActive(rt.gameObject, true);
                }

                _applied.Clear();
                for (int i = 0; i < n; i++) _applied.Add(screenRects[i]);
            }
            catch (Exception e)
            {
                _broken = true;
                KKPEHeightLockPlugin.Log.LogWarning("[界面] 点击拦截层创建失败（不影响使用，但点击可能穿透）：" + e);
            }
        }

        public static void Hide()
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
            _applied.Clear();
        }

        public static void Dispose()
        {
            try
            {
                if (_root != null) UnityEngine.Object.Destroy(_root);
            }
            catch (Exception) { }
            _root = null;
            _blocks.Clear();
            _broken = false;
        }

        private static void SetActive(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        private static bool Ensure()
        {
            if (_root != null) return true;

            var go = new GameObject("SceneCardFittingToolkit_ClickBlock");
            UnityEngine.Object.DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;      // 压在所有 uGUI 之上（short 上限 32767）
            canvas.overrideSorting = false;

            // 没有 Raycaster 的 Canvas 不参与射线检测，等于没挡
            go.AddComponent<GraphicRaycaster>();

            _root = go;
            _blocks.Clear();
            for (int i = 0; i < MaxRects; i++)
            {
                var child = new GameObject("Block" + i);
                child.transform.SetParent(go.transform, false);

                var rt = child.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.zero;
                rt.pivot = Vector2.zero;

                var img = child.AddComponent<Image>();
                img.color = BlockColor;
                img.raycastTarget = true;
                img.maskable = false;

                child.SetActive(false);
                _blocks.Add(rt);
            }

            KKPEHeightLockPlugin.Log.LogInfo("[界面] 点击拦截层已就绪（防止点插件时穿透到工作室界面）");
            return true;
        }
    }
}
