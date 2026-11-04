using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KKPEHeightLock
{
    /// <summary>后期效果强度（对应 python_tools/6_视频后期 的档位表）。</summary>
    public enum FxLevel
    {
        Off = 0,
        Weak = 1,
        Medium = 2,
        Strong = 3
    }

    /// <summary>
    /// 批量导出的一组参数。等价于 autoexport/recorder.py 里的 ExportOptions，
    /// 只是从「外部脚本 + 热键模拟」变成了「游戏内直接调 API」。
    /// </summary>
    public class ExportOptions
    {
        /// <summary>要导出的场景卡（绝对路径），顺序就是成片顺序。</summary>
        public List<string> Scenes = new List<string>();

        /// <summary>替换所有 female 用的角色卡；空 = 不替换。</summary>
        public string FemaleCard = "";
        public bool ReplaceFemale = true;

        // 体型三个维度各自取自哪里：false = 角色卡，true = 场景原角色
        public bool ShapeFromScene;
        public bool BustFromScene;
        public bool BoneFromScene;
        public bool LockHeight = true;

        public bool Decensor = true;
        public bool RemoveSpheres = true;

        /// <summary>
        /// 时长下限（秒）。规则只有一条：
        ///   * 卡里有 Timeline 且比这个长 → 按 Timeline 录
        ///   * 卡里没 Timeline，或 Timeline 比这个短 → 录这么久（短卡循环补齐）
        /// 等价于 content = max(timeline 秒数, MinSeconds)。
        /// 和 4_AutoSceneExport 的「按 Timeline + 短卡循环补齐到」是同一个意思，
        /// 只是把两个开关收成了一个数字，省得配出「两个都关」这种没意义的状态。
        /// </summary>
        public float MinSeconds = 5f;

        /// <summary>
        /// 淡入淡出时长（秒，首尾各一份）。和 4_AutoSceneExport 一样：
        /// 头尾各**多录**这么多秒（时间轴照常往前跑，不是定格），
        /// 后期再用 ffmpeg 的 fade 把多出来的部分压成黑场，正片一秒不少。
        /// 设 0 = 不录多余、也不淡。
        /// </summary>
        public float FadeSeconds = 1f;

        public int Fps = 60;

        /// <summary>
        /// 切到第几套预设服装（对应 chaFile.coordinate 槽位）。-1 = 不改变。
        /// 注意：切套装会把 shapeValueBody 冲掉，所以必须在体型合成之前应用。
        /// </summary>
        public int Outfit = -1;

        /// <summary>
        /// 各部件脱衣状态，按 ChaFileDefine.ClothesKind 的顺序，-1 = 不改。
        /// 字节语义（游戏原值，不是三档）：0 = 穿，1/2 = 半脱，3 = 脱。
        /// </summary>
        public int[] ClothesParts;

        public FxLevel Vignette = FxLevel.Off;
        public FxLevel Bloom = FxLevel.Off;

        /// <summary>
        /// 效果在哪做。
        ///   * false（默认）= 后期在 ffmpeg 里烤进去（泛光/暗角/淡入淡出）
        ///   * true = 泛光/暗角交给 ReShade 实时做（KK_BloomVignette.fx），
        ///     截图强制走 Reshade 插件，ffmpeg 后期只留淡入淡出。
        /// 为什么要这个：实测后期里 90% 的时间花在滤镜上（泛光 10.9s + 暗角 9.0s，
        /// 而 x264 编码只要 7.1s）。ReShade 在 GPU 上实时做是白送的，
        /// 关掉这两个滤镜等于每段省 15 秒左右。
        /// 代价：Reshade 截图是 CPU 回读共享内存，用不了零拷贝。
        /// </summary>
        public bool RealtimeFx = false;

        /// <summary>输出目录（绝对路径）。</summary>
        public string OutputDir = "";

        public bool Merge = true;
        public bool KeepSegments = true;

        /// <summary>输出分辨率；0 = 用游戏当前窗口尺寸。</summary>
        public int ResizeWidth;
        public int ResizeHeight;

        /// <summary>
        /// VideoExport 用哪个截图插件，值 = UI 上按钮的序号：
        /// 0 = Screenshot Manager（KKS_Screencap，抓相机 RenderTexture，
        ///     只有它能开零拷贝）、1 = Built-in、2 = Reshade、3 = Win32。
        /// 实测 0 就是 Screencap，日志里 [ZeroCopy] eligible 那行 rtCapture=True 可证。
        /// </summary>
        public int ScreenshotPluginIndex = 0;

        /// <summary>逐帧录制时放开帧率上限（关垂直同步）。</summary>
        public bool UncapFps = true;

        /// <summary>淡入淡出总共要额外录多少秒（首尾各 FadeSeconds）。</summary>
        public float FadePad
        {
            get { return Mathf.Max(0f, FadeSeconds) * 2f; }
        }

        /// <summary>
        /// 淡入淡出是不是在录制时直接画上去（直出）。
        /// 只要设了时长就走这条路 —— 不依赖 RealtimeFx，也不需要 ffmpeg 后期。
        /// </summary>
        public bool RealtimeFade
        {
            get { return FadeSeconds > 0f; }
        }

    }
}
