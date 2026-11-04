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
    public enum BodyLockMode
    {
        HeightOnly = 0,   // 只锁定身高骨骼(cf_n_height),不保留身材参数
        ShapeOnly = 1,    // 只保留体型滑块 shapeValueBody
        AllBody = 2       // 保留全部体型参数(滑块 + 乳量物理 + 骨架类型)
    }

    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("CharaStudio.exe")]
    public class KKPEHeightLockPlugin : BaseUnityPlugin
    {
        public const string GUID = "com.kkpeheightlock";
        public const string PluginName = "Scene Card Fitting Toolkit";
        public const string Version = "2.0.0";

        internal static ManualLogSource Log;
        internal static KKPEHeightLockPlugin Instance;
        internal static ConfigEntry<string> BoneName;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<BodyLockMode> LockMode;

        // 场景加载自动替换角色
        internal static ConfigEntry<bool> AutoReplaceOnLoad;
        internal static ConfigEntry<bool> ReplaceFemaleOnLoad;
        internal static ConfigEntry<bool> ReplaceMaleOnLoad;
        internal static ConfigEntry<string> FemaleCardPath;
        internal static ConfigEntry<string> MaleCardPath;
        internal static ConfigEntry<KeyboardShortcut> ReplaceKey;

        // 场景工具:去无用球体 / 去码
        internal static ConfigEntry<bool> AutoRemoveSpheres;
        internal static ConfigEntry<bool> AutoDecensor;

        // 场景播放器(ScenePlayer 复刻)
        internal static ConfigEntry<string> SceneDir;
        internal static ConfigEntry<string> RandomFemaleDir;
        internal static ConfigEntry<string> RandomMaleDir;
        internal static ConfigEntry<bool> RetainHeightOnRandom;

        // 替换角色时保留原场景状态
        internal static ConfigEntry<bool> PreservePoseOnReplace;
        internal static ConfigEntry<bool> PreserveClothesOnReplace;

        // UI 缩放
        internal static ConfigEntry<float> UIScale;

        // 界面语言
        internal static ConfigEntry<UiLang> UiLanguage;

        // ---------------------------------------------------------------- 批量导出
        internal static ConfigEntry<string> ExportSceneDir;
        internal static ConfigEntry<string> ExportFemaleCard;
        internal static ConfigEntry<string> ExportOutputDir;
        internal static ConfigEntry<bool> ExportReplaceFemale;
        internal static ConfigEntry<bool> ExportShapeFromScene;
        internal static ConfigEntry<bool> ExportBustFromScene;
        internal static ConfigEntry<bool> ExportBoneFromScene;
        internal static ConfigEntry<bool> ExportLockHeight;
        internal static ConfigEntry<bool> ExportDecensor;
        internal static ConfigEntry<bool> ExportRemoveSpheres;
        internal static ConfigEntry<float> ExportMinSeconds;
        internal static ConfigEntry<float> ExportFadeSeconds;
        internal static ConfigEntry<int> ExportFps;
        internal static ConfigEntry<int> ExportOutfit;
        internal static ConfigEntry<string> ExportClothesParts;
        internal static ConfigEntry<int> ExportResizeWidth;
        internal static ConfigEntry<int> ExportResizeHeight;
        internal static ConfigEntry<FxLevel> ExportVignette;
        internal static ConfigEntry<FxLevel> ExportBloom;
        internal static ConfigEntry<bool> ExportMerge;
        internal static ConfigEntry<bool> ExportKeepSegments;
        internal static ConfigEntry<bool> ExportUncapFps;
        internal static ConfigEntry<int> ExportStartupDelay;
        internal static ConfigEntry<float> ExportReplaceSettle;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            BoneName = Config.Bind("General", "BoneName", "cf_n_height",
                "要自动锁定缩放的身高骨骼名(在 cf_j_root 下)。变体如 cf_n_heightD / cf_n_heightE 也可填。");
            Enabled = Config.Bind("General", "Enabled", true,
                "是否启用本插件功能。");
            ToggleKey = Config.Bind("General", "ToggleKey", new KeyboardShortcut(KeyCode.F10),
                "在游戏内切换启用开关的快捷键。");
            LockMode = Config.Bind("General", "LockMode", BodyLockMode.HeightOnly,
                "身材锁定模式:\n" +
                "HeightOnly = 只锁定身高骨骼缩放(cf_n_height),不保留身材参数\n" +
                "ShapeOnly = 变更角色时只保留体型滑块(身高/三围等 shapeValueBody)\n" +
                "AllBody = 变更角色时保留全部体型参数(体型滑块 + 胸部软硬度 + 骨架类型)");

            AutoReplaceOnLoad = Config.Bind("SceneReplace", "AutoReplaceOnLoad", false,
                "加载场景卡后,自动把场景中的角色替换为指定角色卡。");
            ReplaceFemaleOnLoad = Config.Bind("SceneReplace", "ReplaceFemaleOnLoad", true,
                "自动替换时是否替换女角色(需 AutoReplaceOnLoad 开启)。");
            ReplaceMaleOnLoad = Config.Bind("SceneReplace", "ReplaceMaleOnLoad", true,
                "自动替换时是否替换男角色(需 AutoReplaceOnLoad 开启)。");
            FemaleCardPath = Config.Bind("SceneReplace", "FemaleCardPath", "",
                "女角色替换卡路径(角色卡 .png 文件)。留空 = 不替换女角色。");
            MaleCardPath = Config.Bind("SceneReplace", "MaleCardPath", "",
                "男角色替换卡路径(角色卡 .png 文件)。留空 = 不替换男角色。");
            ReplaceKey = Config.Bind("SceneReplace", "ReplaceKey", new KeyboardShortcut(KeyCode.F11),
                "在游戏内手动触发一次按性别替换角色的快捷键。");

            AutoRemoveSpheres = Config.Bind("SceneTools", "AutoRemoveSpheres", false,
                "加载场景后自动隐藏无用球体(灯光/阴影辅助球)。");
            AutoDecensor = Config.Bind("SceneTools", "AutoDecensor", false,
                "加载场景后自动去码(移除马赛克/圣光材质)。");

            SceneDir = Config.Bind("ScenePlayer", "SceneDir", "UserData/studio/scene",
                "场景播放器扫描的场景目录(默认 UserData/studio/scene)。");
            RandomFemaleDir = Config.Bind("ScenePlayer", "RandomFemaleDir", "",
                "随机替换女角色卡的目录。留空 = 用 UserData/chara/female。");
            RandomMaleDir = Config.Bind("ScenePlayer", "RandomMaleDir", "",
                "随机替换男角色卡的目录。留空 = 用 UserData/chara/male。");
            RetainHeightOnRandom = Config.Bind("ScenePlayer", "RetainHeightOnRandom", true,
                "随机替换角色时保留原身高(防穿模)。");

            PreservePoseOnReplace = Config.Bind("SceneReplace", "PreservePoseOnReplace", true,
                "替换角色后恢复原场景的姿势/骨骼变换(只换外观,姿势不变)。");
            PreserveClothesOnReplace = Config.Bind("SceneReplace", "PreserveClothesOnReplace", false,
                "替换角色后恢复原场景的服装(只换脸/身材,服装不变)。");

            UIScale = Config.Bind("General", "UIScale", 1.0f,
                "菜单界面和按钮的整体缩放倍率（桌面建议 1.0，觉得小可以调到 1.5）。");
            UiLanguage = Config.Bind("General", "Language", UiLang.Auto,
                "界面语言：Auto = 跟随系统，Chinese = 中文，English = English。"
                + "只影响界面文案，日志固定中文。改完立即生效。");
            UiLanguage.SettingChanged += delegate { L10n.Refresh(UiLanguage.Value); };
            L10n.Refresh(UiLanguage.Value);

            // ------------------------------------------------------------ 批量导出
            // 不再绑快捷键：F9 被 ReShade 截图占用，F8 / Ctrl+F8 又和别的插件撞。
            // 批量导出窗口只从工具栏按钮打开（Studio 左下角那个按钮），
            // 面板右上角有关闭按钮，所以不需要键盘。
            ExportSceneDir = Config.Bind("AutoExport", "SceneDir", "UserData/studio/scene",
                "批量导出扫描场景卡的目录（递归包含子目录）。");
            ExportFemaleCard = Config.Bind("AutoExport", "FemaleCard", "",
                "批量导出时用来替换 female 的角色卡（.png）。留空 = 不替换。");
            ExportOutputDir = Config.Bind("AutoExport", "OutputDir", "",
                "成片输出目录。留空 = UserData/VideoExport/AutoExport。");
            ExportReplaceFemale = Config.Bind("AutoExport", "ReplaceFemale", true,
                "导出前把所有 female 替换成上面那张角色卡。");
            ExportShapeFromScene = Config.Bind("AutoExport", "ShapeFromScene", false,
                "体型 44 个滑块取自场景原角色（关 = 用角色卡自己的）。");
            ExportBustFromScene = Config.Bind("AutoExport", "BustFromScene", false,
                "乳量（大小+乳摇物理）取自场景原角色（关 = 用角色卡自己的）。");
            ExportBoneFromScene = Config.Bind("AutoExport", "BoneFromScene", false,
                "骨架类型 typeBone 取自场景原角色（关 = 用角色卡自己的）。");
            ExportLockHeight = Config.Bind("AutoExport", "LockHeight", true,
                "锁定 cf_n_height 身高，不被姿势动画覆盖。");
            ExportDecensor = Config.Bind("AutoExport", "Decensor", true,
                "导出前去掉场景里的马赛克/圣光材质。");
            ExportRemoveSpheres = Config.Bind("AutoExport", "RemoveSpheres", true,
                "导出前隐藏场景里的无用辅助球。");
            ExportMinSeconds = Config.Bind("AutoExport", "MinSeconds", 5f,
                "时长下限（秒）。卡里的 Timeline 比这个长就按 Timeline 录；"
                + "没 Timeline 或 Timeline 比这个短，就录这么久（短卡循环补齐）。");
            ExportFadeSeconds = Config.Bind("AutoExport", "FadeSeconds", 1f,
                "头尾各多录多少秒用于淡入淡出（0 = 不淡入淡出）。");
            ExportReplaceSettle = Config.Bind("AutoExport", "ReplaceSettleSeconds", 3f,
                "替换 female 之后、开录之前等多少秒让动态骨骼（头发/裙子/胸部）稳下来。"
                + "旧脚本这一段是「等主线程闲下来 + 日志安静 1 秒」再加换装/去码，前后好几秒；"
                + "给太短成片开头头发会向上飞。");
            ExportFps = Config.Bind("AutoExport", "Fps", 60,
                "录制/导出帧率。");
            ExportOutfit = Config.Bind("AutoExport", "Outfit", -1,
                "切换到第几套预设服装（对应角色卡里的 coordinate 槽位，实测 0~6）。"
                + "-1 = 不改变。注意切套装会把体型冲掉，插件会自动写回。");
            ExportClothesParts = Config.Bind("AutoExport", "ClothesParts", "",
                "各部件脱衣状态，按 ChaFileDefine.ClothesKind 的顺序（KKS 实测 9 个："
                + "top,bot,bra,shorts,gloves,panst,socks,shoes_inner,shoes_outer），"
                + "逗号分隔。取值 -1 = 不改，0 = 穿，1/2 = 半脱，3 = 脱。"
                + "留空 = 全部不改。例：0,0,3,3,-1,-1,-1,-1,-1");
                        ExportResizeWidth = Config.Bind("AutoExport", "ResizeWidth", 0,
                "输出宽度（0 = 用游戏当前窗口尺寸；要和高度一起填）。");
            ExportResizeHeight = Config.Bind("AutoExport", "ResizeHeight", 0,
                "输出高度（0 = 用游戏当前窗口尺寸）。");
            ExportVignette = Config.Bind("AutoExport", "Vignette", FxLevel.Strong,
                "暗角强度（ReShade 实时做）。界面上已经不给开关了，要调就改这里或直接改"
                + " KK_BloomVignette.ini，然后重启游戏才生效。");
            ExportBloom = Config.Bind("AutoExport", "Bloom", FxLevel.Medium,
                "泛光强度（ReShade 实时做）。界面上已经不给开关了，要调就改这里或直接改"
                + " KK_BloomVignette.ini，然后重启游戏才生效。");
                                    ExportMerge = Config.Bind("AutoExport", "Merge", true,
                "全部录完后把各段合并成一个 mp4。");
            ExportKeepSegments = Config.Bind("AutoExport", "KeepSegments", true,
                "保留每张场景卡单独的片段。");
            ExportStartupDelay = Config.Bind("AutoExport", "StartupDelay", 60,
                "等 Studio 启动完成的**上限**（秒）。正常情况下插件一收到 KKAPI 的 "
                + "SceneLoad 事件就开跑，用不到这个值；只有拿不到事件时才等满再带警告继续。");
            ExportUncapFps = Config.Bind("AutoExport", "UncapFps", true,
                "逐帧录制时关掉垂直同步、放开帧率上限，让引擎尽快出帧。"
                + "关掉就按显示器刷新率走（更稳但更慢）。");


            // 逐个类打补丁：某个目标方法在这个版本里不存在（KK/KKS 的 PE 版本差异）
            // 只让那一组失效，不要把整个插件的补丁都带崩。
            var harmony = new Harmony(GUID);
            TryPatch(harmony, typeof(HeightLockPatches), "身高锁");
            TryPatch(harmony, typeof(BodyPreservePatches), "换人保留体型");

            Logger.LogInfo($"{PluginName} v{Version} loaded, bone: {BoneName.Value}, mode: {LockMode.Value}, enabled: {Enabled.Value}");
        }

        /// <summary>单独给一个补丁类打补丁，失败只影响这一组功能。</summary>
        private static void TryPatch(Harmony harmony, Type patchType, string label)
        {
            try
            {
                harmony.PatchAll(patchType);
                Log.LogInfo($"{PluginName}: 补丁已应用 —— {label}");
            }
            catch (Exception e)
            {
                Log.LogWarning($"{PluginName}: 补丁失败 —— {label}：{e.Message}");
            }
        }

        private void Start()
        {
            StudioSaveLoadApi.SceneLoad += OnSceneLoad;
            // 同一个事件也用来判断「Studio 启动完成」：它启动时会载入默认场景，
            // 那一次触发就代表初始化走完了。订阅要早，否则会漏掉。
            AutoExport.HookSceneLoadEvent();
            ConfigPanel.Init(this);
        }

        private void OnDestroy()
        {
            StudioSaveLoadApi.SceneLoad -= OnSceneLoad;
            ConfigPanel.Dispose();
        }

        private void Update()
        {
            if (ToggleKey.Value.IsDown())
            {
                Enabled.Value = !Enabled.Value;
                Logger.LogMessage($"{PluginName}: {(Enabled.Value ? "enabled" : "disabled")}");
            }

            if (ReplaceKey.Value.IsDown())
            {
                Logger.LogMessage($"{PluginName}: manual character replace triggered");
                SceneReplacer.ReplaceAllCharacters();
            }

            // 批量导出窗口没有快捷键了，只从工具栏按钮 / 本工具箱按钮打开
        }

        private void LateUpdate()
        {
            // VideoExport 的限长参数全是私有字段，这里定期兜底找它的实例
            AutoExport.TickVideoExport();
            // 加载场景期间把时间轴按住：它一开播就暂停，最多走一两帧
            AutoExport.TickTimelineHold();
            // 身高锁：动画写完骨骼之后、VideoExport 截帧之前压回去
            AutoExport.BodySnap.TickHeight();
        }

        private void OnGUI()
        {
            // 实时淡入淡出的黑场必须先画：它就是要留在画面上的东西
            AutoExport.DrawFadeOverlay();
            // 同上：ReShade 实时通路会把界面录进视频，导出期间一律不画
            if (AutoExport.SuppressGui) return;
            ConfigPanel.OnGUI();
        }

        private void OnSceneLoad(object sender, SceneLoadEventArgs args)
        {
            if (!Enabled.Value) return;
            if (args.Operation != SceneOperationKind.Load) return;

            // 场景工具:去无用球体 / 去码(独立开关,与角色替换互不影响)
            if (AutoRemoveSpheres.Value)
                SceneTools.RemoveUselessSpheres();
            if (AutoDecensor.Value)
                SceneTools.DecensorScene();

            // 刷新场景播放器列表(更新当前场景索引)
            ScenePlayerModule.RefreshSceneList();

            if (!AutoReplaceOnLoad.Value) return;

            // 场景加载后角色需要几帧才完全构建,延迟执行
            Logger.LogMessage($"{PluginName}: scene loaded, will replace characters by sex");
            SceneReplacer.ScheduleReplace(this, args.LoadedObjects);
        }
    }
}
