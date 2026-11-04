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
    /// 左侧工具栏按钮(类似 KKPE/VNGE 的小方块)打开配置窗口:
    /// 选择女/男替换角色卡(工作室原生文件选择器)、开关、立即替换。
    /// </summary>
    public static partial class ConfigPanel
    {
        private const int WindowId = 891273;
        private const int CardPickWindowId = 891274;
        private const int SubdirPickWindowId = 891275;
        private static BaseUnityPlugin _plugin;
        private static bool _windowVisible;
        private static Rect _windowRect = new Rect(200, 100, 560, 660);
        private static Vector2 _windowScrollPos;
        private static bool _clothesPartsExpanded;
        private static KKAPI.Studio.UI.Toolbars.ToolbarControlBase _toolbarButton;
        private static KKAPI.Studio.UI.Toolbars.ToolbarControlBase _exportToolbarButton;

        // 子目录选择窗口状态(通用:场景/随机女/随机男)
        private static bool _subdirPickerVisible;
        private static int _subdirPickerMode; // 0=场景 1=随机女 2=随机男
        private static Rect _subdirPickerRect = new Rect(200, 120, 480, 420);
        private static Vector2 _subdirScroll;
        private static string _subdirPickerPath = ""; // 当前浏览的路径
        private static System.Collections.Generic.List<string> _subdirList = new System.Collections.Generic.List<string>();

        // 游戏内选卡窗口状态
        private static bool _cardPickerVisible;
        private static bool _cardPickerIsFemale;
        /// <summary>选卡结果写给谁：0 = 场景替换的配置，1 = 批量导出的角色卡。</summary>
        private static int _cardPickTarget;
        private static Rect _cardPickerRect = new Rect(150, 80, 640, 500);
        private static Vector2 _cardScroll;
        private static string _cardSearch = "";
        private static string _cardPickerCurrentDir = ""; // 相对目录,"" = 根目录
        private static System.Collections.Generic.List<CardEntry> _cardEntries;
        private static System.Collections.Generic.List<string> _cardSubFolders;
        private static int _cardsProcessedThisFrame;

        // 配置窗口背景图(外置文件,可替换)
        private static Texture2D _windowBackground;
        private static bool _backgroundAttempted;
        private static bool _resizingWindow;
        private static bool _draggingWindow;
        private static Vector2 _dragStartMouse;
        private static Vector2 _dragStartWinPos;

        private class CardEntry
        {
            public string path;
            public string name;     // 角色名(懒加载,初始为 null)
            public Texture2D texture; // 缩略图(懒加载)
            public bool metaLoaded; // 角色名是否已加载
        }

        public static void Init(BaseUnityPlugin plugin)
        {
            _plugin = plugin;
            try
            {
                var icon = CreateIcon();
                var btn = new KKAPI.Studio.UI.Toolbars.SimpleToolbarButton(
                    "KKPEHeightLock.Config",
                    L10n.T("场景卡套档工具箱 设置"),
                    () => icon,
                    plugin,
                    _ => { _windowVisible = !_windowVisible; });

                if (KKAPI.Studio.UI.Toolbars.ToolbarManager.AddLeftToolbarControl(btn))
                {
                    _toolbarButton = btn;
                    KKPEHeightLockPlugin.Log.LogInfo("KKPEHeightLock: toolbar button created");
                }
                else
                {
                    KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: toolbar button registration failed");
                }

                // 第二个工具栏按钮：直接打开「批量导出」（电影机图标）
                try
                {
                    var exportIcon = CreateExportIcon();
                    var exportBtn = new KKAPI.Studio.UI.Toolbars.SimpleToolbarButton(
                        "KKPEHeightLock.Export",
                        L10n.T("批量导出：场景卡 → 视频"),
                        () => exportIcon,
                        plugin,
                        _ => AutoExportPanel.Toggle());
                    if (KKAPI.Studio.UI.Toolbars.ToolbarManager.AddLeftToolbarControl(exportBtn))
                    {
                        _exportToolbarButton = exportBtn;
                        KKPEHeightLockPlugin.Log.LogInfo("KKPEHeightLock: export toolbar button created");
                    }
                }
                catch (Exception e)
                {
                    KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: export toolbar button failed: " + e.Message);
                }
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: failed to create toolbar button: " + e);
            }
        }

        public static void Dispose()
        {
            if (_toolbarButton != null)
            {
                try { _toolbarButton.Dispose(); } catch { }
                _toolbarButton = null;
            }
            if (_exportToolbarButton != null)
            {
                try { _exportToolbarButton.Dispose(); } catch { }
                _exportToolbarButton = null;
            }
        }
    }
}
