using System.Collections.Generic;
using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>批量导出的一组参数。</summary>
    public class ExportOptions
    {
        /// <summary>要导出的场景卡（绝对路径），顺序即成片顺序。</summary>
        public List<string> Scenes = new List<string>();

        /// <summary>主角色卡：场景里第一个 female 换成这张。空 = 全部按次卡处理。</summary>
        public string FemaleCard = "";

        /// <summary>次角色卡列表，顺序即编号（1、2、3…）。主卡为空时第一个 female 也从这里取。</summary>
        public List<string> SubCards = new List<string>();

        /// <summary>次卡分配顺序：false = 数字优先，true = 随机。</summary>
        public bool SubRandom;

        public bool ReplaceFemale = true;

        // 体型四项：false = 取自角色卡，true = 取自场景原角色
        public bool ShapeFromScene;
        public bool BustFromScene;
        public bool BoneFromScene;
        public bool LockHeight = true;

        public bool Decensor = true;
        public bool RemoveSpheres = true;

        /// <summary>时长下限（秒）：正片 = max(Timeline 秒数, MinSeconds)，短卡循环补齐。</summary>
        public float MinSeconds = 5f;

        /// <summary>淡入淡出时长（秒，首尾各一份）。头尾各多录这么多秒并压黑场，0 = 不做。</summary>
        public float FadeSeconds = 1f;

        public int Fps = 60;

        /// <summary>切到第几套预设服装（coordinate 槽位）。-1 = 不改，-2 = 随机（只从卡里真有衣服的槽位挑）。</summary>
        public int Outfit = -1;

        /// <summary>各部件脱衣状态，按 ChaFileDefine.ClothesKind 顺序，-1 = 不改。0 穿 / 1、2 半脱 / 3 脱。</summary>
        public int[] ClothesParts;

        /// <summary>泛光/暗角走 ReShade 实时效果（截图因此固定用 Reshade 插件，用不了零拷贝）。</summary>
        public bool RealtimeFx = false;

        /// <summary>输出目录（绝对路径）。</summary>
        public string OutputDir = "";

        public bool Merge = true;
        public bool KeepSegments = true;

        /// <summary>输出分辨率；0 = 用游戏当前窗口尺寸。</summary>
        public int ResizeWidth;
        public int ResizeHeight;

        /// <summary>VideoExport 的截图插件序号：0 Screencap / 1 Built-in / 2 Reshade / 3 Win32。</summary>
        public int ScreenshotPluginIndex = 0;

        /// <summary>逐帧录制时放开帧率上限（关垂直同步）。</summary>
        public bool UncapFps = true;

        /// <summary>预览模式：走完准备流程但不录制、不隐藏界面。</summary>
        public bool PreviewOnly;

        /// <summary>全局 female 皮肤卡（绝对路径；空 = 不应用）。只套 face + body，体型另存另还原。</summary>
        public string GlobalSkinCard = "";

        /// <summary>male 透明度：0 = 不处理，0~1 = 越小越透。</summary>
        public float MaleAlpha;

        /// <summary>角色光照亮度上限；0 = 不处理，且只有应用了全局皮肤卡时才生效。</summary>
        public float ClampCharLight = 0.9f;

        /// <summary>KKUSSitem 材质的 ShadowPower 下限；0 = 不处理。</summary>
        public float MinShadowPower = 0.85f;

        /// <summary>淡入淡出总共额外录多少秒（首尾各 FadeSeconds）。</summary>
        public float FadePad
        {
            get { return Mathf.Max(0f, FadeSeconds) * 2f; }
        }

        /// <summary>淡入淡出是否在录制时直接画黑场（设了时长就走这条，不依赖 RealtimeFx）。</summary>
        public bool RealtimeFade
        {
            get { return FadeSeconds > 0f; }
        }
    }
}
