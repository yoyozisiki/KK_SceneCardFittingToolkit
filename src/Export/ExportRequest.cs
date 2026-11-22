using System;
using System.Collections.Generic;
using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>
    /// 把「界面上的设置」读成一份 <see cref="ExportOptions"/> 并启动导出（后端）。
    ///
    /// 以前这段写在 UI 的 StartExport() 里：界面代码里塞满了 config 取值、路径修复、
    /// 帧率夹取……想换个入口（快捷键、命令行、自动化）就得复制一遍。
    /// 现在界面只负责把「勾了哪些卡」递进来。
    /// </summary>
    public static class ExportRequest
    {
        /// <summary>按当前配置组一份导出参数。scenes = 场景卡绝对路径，顺序即成片顺序。</summary>
        public static ExportOptions Build(List<string> scenes)
        {
            var o = new ExportOptions();
            if (scenes != null) o.Scenes.AddRange(scenes);

            o.FemaleCard = CardPool.Main;
            o.SubCards = new List<string>(CardPool.Subs);
            o.SubRandom = CardPool.RandomOrder;
            o.ReplaceFemale = KKPEHeightLockPlugin.ExportReplaceFemale.Value;

            o.ShapeFromScene = KKPEHeightLockPlugin.ExportShapeFromScene.Value;
            o.BustFromScene = KKPEHeightLockPlugin.ExportBustFromScene.Value;
            o.BoneFromScene = KKPEHeightLockPlugin.ExportBoneFromScene.Value;
            o.LockHeight = KKPEHeightLockPlugin.ExportLockHeight.Value;

            o.Outfit = KKPEHeightLockPlugin.ExportOutfit.Value;
            o.ClothesParts = (int[])ClothesPartSettings.Values.Clone();

            o.Decensor = KKPEHeightLockPlugin.ExportDecensor.Value;
            o.RemoveSpheres = KKPEHeightLockPlugin.ExportRemoveSpheres.Value;
            o.MinSeconds = KKPEHeightLockPlugin.ExportMinSeconds.Value;
            o.FadeSeconds = KKPEHeightLockPlugin.ExportFadeSeconds.Value;
            o.Fps = Mathf.Clamp(KKPEHeightLockPlugin.ExportFps.Value, 1, 240);
            o.ResizeWidth = Mathf.Max(0, KKPEHeightLockPlugin.ExportResizeWidth.Value);
            o.ResizeHeight = Mathf.Max(0, KKPEHeightLockPlugin.ExportResizeHeight.Value);
            o.OutputDir = SceneCatalog.ResolveDir(PathFix.Read(KKPEHeightLockPlugin.ExportOutputDir.Value));
            o.Merge = KKPEHeightLockPlugin.ExportMerge.Value;
            o.KeepSegments = KKPEHeightLockPlugin.ExportKeepSegments.Value;
            o.UncapFps = KKPEHeightLockPlugin.ExportUncapFps.Value;

            // 后期只剩 ReShade 一条路：泛光/暗角在 present 之后加，
            // 只有 KKReshade 的共享内存通路能抓到（代价是用不了零拷贝）。
            // 强度不在这里传：ReShade 面板里是什么就是什么，插件不碰预设。
            o.RealtimeFx = true;
            o.ScreenshotPluginIndex = 2;
            return o;
        }

        /// <summary>组参数并开跑。返回 false = 没启动（没勾卡 / VideoExport 没就绪）。</summary>
        public static bool Start(List<string> scenes)
        {
            return AutoExport.Start(KKPEHeightLockPlugin.Instance, Build(scenes));
        }
    }
}
