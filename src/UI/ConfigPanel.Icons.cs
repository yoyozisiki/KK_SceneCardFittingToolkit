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
public static partial class ConfigPanel
    {
        /// <summary>批量导出用的工具栏图标：深红底 + 白色胶片框（和主按钮的蓝色箭头区分开）。</summary>
        private static Texture2D CreateExportIcon()
        {
            try
            {
                var iconPath = System.IO.Path.Combine(
                    System.IO.Path.Combine(BepInEx.Paths.PluginPath, "KKPEHeightLock"), "export.png");
                if (System.IO.File.Exists(iconPath))
                {
                    var tex = PngAssist.LoadTexture(iconPath);
                    if (tex != null) return tex.width == 32 ? tex : ScaleTexture(tex, 32, 32);
                }
            }
            catch (Exception) { }

            const int size = 32;
            var fallback = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color c = new Color(0.62f, 0.12f, 0.16f, 1f);           // 深红底
                    bool frame = (x >= 5 && x <= 26 && y >= 9 && y <= 22) &&
                                 !(x >= 7 && x <= 24 && y >= 11 && y <= 20);
                    bool reel = (x >= 9 && x <= 22 && y >= 13 && y <= 18);
                    if (frame || reel) c = Color.white;
                    // 左下角留一个「播放三角」的缺口，视觉上更像导出
                    if (x >= 11 && x <= 16 && y >= 13 && y <= 18) c = new Color(0.62f, 0.12f, 0.16f, 1f);
                    fallback.SetPixel(x, y, c);
                }
            }
            fallback.Apply();
            return fallback;
        }

        /// <summary>加载工具栏图标:优先使用 BepInEx/plugins/KKPEHeightLock/icon.png(用户可替换),
        /// 找不到或加载失败则回退到程序化生成的蓝色箭头。</summary>
        private static Texture2D CreateIcon()
        {
            // 1) 尝试加载外部图标文件(自动缩放到 32x32)
            try
            {
                var iconPath = System.IO.Path.Combine(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "KKPEHeightLock"), "icon.png");
                if (System.IO.File.Exists(iconPath))
                {
                    var tex = PngAssist.LoadTexture(iconPath);
                    if (tex != null)
                    {
                        if (tex.width == 32 && tex.height == 32)
                        {
                            KKPEHeightLockPlugin.Log.LogInfo("KKPEHeightLock: using custom icon (32x32)");
                            return tex;
                        }
                        var scaled = ScaleTexture(tex, 32, 32);
                        KKPEHeightLockPlugin.Log.LogInfo($"KKPEHeightLock: using custom icon scaled {tex.width}x{tex.height} -> 32x32");
                        return scaled;
                    }
                }
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: failed to load custom icon, using default: " + e.Message);
            }

            // 2) 回退:程序化生成蓝色方块 + 白色箭头
            const int size = 32;
            var fallback = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color c = new Color(0.15f, 0.4f, 0.85f, 1f); // 蓝色底
                    // 画一个白色箭头(↑),适配 32x32
                    bool arrow = (x >= size / 2 - 3 && x <= size / 2 + 3 && y >= size / 2 - 5 && y <= size / 2 + 7) ||
                                 (y >= size / 2 - 5 && y <= size / 2 - 3 && x >= size / 2 - 8 && x <= size / 2 + 8);
                    if (arrow) c = Color.white;
                    fallback.SetPixel(x, y, c);
                }
            }
            fallback.Apply();
            return fallback;
        }

        /// <summary>高质量缩放 Texture2D(Catmull-Rom 双三次插值,比双线性清晰很多)。</summary>
        private static Texture2D ScaleTexture(Texture2D source, int newWidth, int newHeight)
        {
            var result = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false);
            int srcW = source.width;
            int srcH = source.height;
            float scaleX = (float)srcW / newWidth;
            float scaleY = (float)srcH / newHeight;

            for (int y = 0; y < newHeight; y++)
            {
                for (int x = 0; x < newWidth; x++)
                {
                    // 映射到源图坐标(像素中心)
                    float fx = (x + 0.5f) * scaleX - 0.5f;
                    float fy = (y + 0.5f) * scaleY - 0.5f;
                    result.SetPixel(x, y, SampleCatmullRom(source, fx, fy, srcW, srcH));
                }
            }
            result.Apply();
            return result;
        }

        /// <summary>Catmull-Rom 双三次采样(4x4 邻域加权)。</summary>
        private static Color SampleCatmullRom(Texture2D tex, float fx, float fy, int srcW, int srcH)
        {
            int x0 = (int)Mathf.Floor(fx);
            int y0 = (int)Mathf.Floor(fy);
            float tx = fx - x0;
            float ty = fy - y0;

            // 计算 4x4 权重
            float[] wx = CatmullRomWeights(tx);
            float[] wy = CatmullRomWeights(ty);

            Color result = new Color(0f, 0f, 0f, 0f);
            for (int j = 0; j < 4; j++)
            {
                int sy = Mathf.Clamp(y0 - 1 + j, 0, srcH - 1);
                for (int i = 0; i < 4; i++)
                {
                    int sx = Mathf.Clamp(x0 - 1 + i, 0, srcW - 1);
                    float w = wx[i] * wy[j];
                    if (w != 0f)
                        result += tex.GetPixel(sx, sy) * w;
                }
            }
            return result;
        }

        private static float[] CatmullRomWeights(float t)
        {
            // Catmull-Rom 基函数(标准 4 点形式)
            float t2 = t * t;
            float t3 = t2 * t;
            return new float[]
            {
                0.5f * (-t3 + 2f * t2 - t),        // w(-1)
                0.5f * (3f * t3 - 5f * t2 + 2f),   // w(0)
                0.5f * (-3f * t3 + 4f * t2 + t),   // w(1)
                0.5f * (t3 - t2)                    // w(2)
            };
        }
    }
}
