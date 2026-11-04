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
        internal static void OnGUI()
        {
            // ReShade 实时效果通路下，截图抓的是 present 之后的最终画面，
            // 本插件的 IMGUI 会被一起录进视频 —— 所以导出期间整个不画。
            if (AutoExport.SuppressGui) return;

            // 整体缩放(VR 用):矩阵缩放后,所有控件/文字等比放大
            float scale = Mathf.Max(0.5f, KKPEHeightLockPlugin.UIScale.Value);
            var oldMatrix = GUI.matrix;
            if (scale != 1f)
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            // 完全自研窗口交互:不用 GUI.Window 返回值(会与我们的修改冲突)。
            // 拖动/resize 都在窗口函数内用绝对坐标处理。
            if (_windowVisible)
                GUI.Window(WindowId, _windowRect, DrawWindow, L10n.T("场景卡套档工具箱"));

            // 批量导出窗口（同一套缩放矩阵，保证和主窗口比例一致）
            AutoExportPanel.OnGUI();

            // 选卡 / 选目录弹框必须**最后画、并且 depth 更低**：
            // 批量导出面板自己把 GUI.depth 压到 -2000 抢事件，弹框要是画在它前面，
            // 就会被整块盖住 —— 表现就是「点一下后面就被盖到下面去了」。
            if (_cardPickerVisible || _subdirPickerVisible)
            {
                int oldDepth = GUI.depth;
                GUI.depth = -2100;
                try
                {
                    if (_cardPickerVisible)
                        GUI.Window(CardPickWindowId, _cardPickerRect, DrawCardPicker,
                            L10n.T("选择角色卡"), AutoExportPanel.ThemeWindow);
                    if (_subdirPickerVisible)
                        GUI.Window(SubdirPickWindowId, _subdirPickerRect, DrawSubdirPicker,
                            L10n.T("选择子目录"), AutoExportPanel.ThemeWindow);
                }
                finally
                {
                    GUI.depth = oldDepth;
                }
            }

            GUI.matrix = oldMatrix;
        }

        private static void DrawWindow(int id)
        {
            // 背景图(如有)
            DrawWindowBackground();

            // 增强皮肤:深色背景上按钮/文字更醒目
            GUISkin oldSkin = GUI.skin;
            GUI.skin = BuildVRSkin(oldSkin);

            // 右上角关闭小按钮(绝对定位)
            var closeRect = new Rect(_windowRect.width - 30, 4, 24, 22);
            if (GUI.Button(closeRect, "×"))
                _windowVisible = false;

            // 内容可上下滚动(垂直滚动条),禁止水平滚动条(左右不滑)
            // 内容可上下滚动;滚动视图宽度=内容区宽度(内容本身不超宽,绝不出现水平滚动条)
            _windowScrollPos = GUILayout.BeginScrollView(_windowScrollPos, false, true, GUILayout.ExpandWidth(true), GUILayout.Height(_windowRect.height - 24));
            GUILayout.BeginVertical();

            // --- 免责声明 ---
            var oldStyle = GUI.skin.label;
            var warnStyle = new GUIStyle(GUI.skin.label);
            warnStyle.normal.textColor = Color.yellow;
            warnStyle.fontStyle = FontStyle.Bold;
            warnStyle.alignment = TextAnchor.MiddleCenter;
            GUILayout.Label(L10n.T("该插件为完全免费发布,倒狗和贩子全家死光。"), warnStyle);
            GUI.skin.label = oldStyle;
            GUILayout.Space(4);

            // --- 场景播放器(ScenePlayer 复刻,菜单最顶端) ---
            DrawSectionBox(L10n.T("场景播放器"), new Color(0.30f, 0.20f, 0.60f, 0.8f), new Color(0.55f, 0.30f, 0.80f, 0.95f));

            // 场景浏览行
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", GUILayout.Width(36), GUILayout.Height(32)))
                ScenePlayerModule.LoadPrevScene();
            if (GUILayout.Button(L10n.T("刷新"), GUILayout.Width(52), GUILayout.Height(32)))
                ScenePlayerModule.RefreshSceneList();
            if (GUILayout.Button(L10n.T("随机"), GUILayout.Width(52), GUILayout.Height(32)))
                ScenePlayerModule.RandomScene();
            GUILayout.Label(ScenePlayerModule.SceneLabel(), GUI.skin.textField, GUILayout.ExpandWidth(true), GUILayout.MaxWidth(280), GUILayout.Height(32));
            if (GUILayout.Button("▶", GUILayout.Width(36), GUILayout.Height(32)))
                ScenePlayerModule.LoadNextScene();
            GUILayout.EndHorizontal();

            // 场景子目录(点"选择"弹出子目录窗口)
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L10n.T("选择子目录"), GUILayout.Height(28)))
                OpenSubdirPicker(0);
            GUILayout.Label(L10n.T("场景: ") + ScenePlayerModule.SceneSubdirLabel(), GUI.skin.textField, GUILayout.ExpandWidth(true), GUILayout.MaxWidth(300), GUILayout.Height(28));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L10n.T("删除"), GUILayout.Width(52), GUILayout.Height(30)))
                ScenePlayerModule.DeleteCurrentScene(true);
            GUILayout.Label(L10n.T("场景播放器:上/下一场景,加载场景"), new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Italic }, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            // 随机替换角色行
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L10n.T("随机女"), GUILayout.Height(32)))
                ScenePlayerModule.RandomReplaceFemale();
            if (GUILayout.Button(L10n.T("随机男"), GUILayout.Height(32)))
                ScenePlayerModule.RandomReplaceMale();
            if (GUILayout.Button(L10n.T("随机全部"), GUILayout.Height(32)))
                ScenePlayerModule.RandomReplaceAll();
            GUILayout.EndHorizontal();

            // 随机子目录(点"选择"弹出子目录窗口)
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L10n.T("女卡目录"), GUILayout.Height(26)))
                OpenSubdirPicker(1);
            GUILayout.Label(ScenePlayerModule.RandomFemaleSubdirLabel(), GUI.skin.textField, GUILayout.ExpandWidth(true), GUILayout.MaxWidth(280), GUILayout.Height(26));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L10n.T("男卡目录"), GUILayout.Height(26)))
                OpenSubdirPicker(2);
            GUILayout.Label(ScenePlayerModule.RandomMaleSubdirLabel(), GUI.skin.textField, GUILayout.ExpandWidth(true), GUILayout.MaxWidth(280), GUILayout.Height(26));
            GUILayout.EndHorizontal();

            // FSS 子场景行
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("FSS", GUILayout.Width(44), GUILayout.Height(30)))
                ScenePlayerModule.RefreshFSS();
            if (GUILayout.Button("◀", GUILayout.Width(32), GUILayout.Height(30)))
                ScenePlayerModule.SwitchFSS(ScenePlayerModule.FssIndex - 1);
            GUILayout.Label(ScenePlayerModule.FssLabel(), GUI.skin.textField, GUILayout.ExpandWidth(true), GUILayout.MaxWidth(280), GUILayout.Height(30));
            if (GUILayout.Button("▶", GUILayout.Width(32), GUILayout.Height(30)))
                ScenePlayerModule.SwitchFSS(ScenePlayerModule.FssIndex + 1);
            GUILayout.EndHorizontal();

            // Timeline 控制行
            GUILayout.BeginHorizontal();
            bool tlPlaying = ScenePlayerModule.TimelinePlaying;
            if (GUILayout.Button(tlPlaying ? "⏸" : "▶", GUILayout.Width(36), GUILayout.Height(32)))
            {
                if (tlPlaying) ScenePlayerModule.TimelinePause();
                else ScenePlayerModule.TimelinePlay();
            }
            if (GUILayout.Button("⏹", GUILayout.Width(36), GUILayout.Height(32)))
                ScenePlayerModule.TimelineStop();
            // CAM 按钮:Timeline 有相机关键帧时显示,切换相机是否受控
            if (ScenePlayerModule.TimelineHasCameraKeyframe)
            {
                if (GUILayout.Button(ScenePlayerModule.TimelineCameraControlled ? "CAM●" : "CAM○", GUILayout.Width(52), GUILayout.Height(32)))
                    ScenePlayerModule.TimelineToggleCamera();
            }
            GUILayout.Label(ScenePlayerModule.TimelineLabel(), GUI.skin.textField, GUILayout.ExpandWidth(true), GUILayout.MaxWidth(280), GUILayout.Height(32));
            GUILayout.EndHorizontal();

            // 服装:预设套装(校服1/校服2/体操/泳装/社团/便服/睡衣),当前选中高亮
            DrawSectionBox(L10n.T("服装预设"), new Color(0.60f, 0.25f, 0.35f, 0.8f), new Color(0.85f, 0.40f, 0.50f, 0.95f));
            var editChara = ScenePlayerModule.CurrentEditChar();
            int curCoord = ScenePlayerModule.GetCoordinateType(editChara);
            string[] coordNames = { L10n.T("校服1"), L10n.T("校服2"), L10n.T("体操"), L10n.T("泳装"), L10n.T("社团"), L10n.T("便服"), L10n.T("睡衣") };
            // 每行 4 个套装按钮(避免一行 7 个超宽出水平滚动条)
            const int coordPerRow = 4;
            for (int ci = 0; ci < coordNames.Length; ci++)
            {
                if (ci % coordPerRow == 0)
                    GUILayout.BeginHorizontal();
                bool isCur = (curCoord == ci);
                if (isCur)
                {
                    // 选中:绿色亮色高亮(原生按钮纹理上染绿)
                    var oldBg = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.15f, 0.75f, 0.30f, 1f);
                    if (GUILayout.Button("● " + coordNames[ci], GUILayout.Width(110), GUILayout.Height(30)))
                        ScenePlayerModule.SetCoordinateType(editChara, ci);
                    GUI.backgroundColor = oldBg;
                }
                else
                {
                    if (GUILayout.Button(coordNames[ci], GUILayout.Width(110), GUILayout.Height(30)))
                        ScenePlayerModule.SetCoordinateType(editChara, ci);
                }
                if (ci % coordPerRow == coordPerRow - 1 || ci == coordNames.Length - 1)
                    GUILayout.EndHorizontal();
            }

            // 部件三态(收纳,默认收起)
            _clothesPartsExpanded = GUILayout.Toggle(_clothesPartsExpanded, L10n.T("部件三态(展开/收起)"));
            if (_clothesPartsExpanded)
            {
                DrawClothesPartRow(L10n.T("上衣"), 0);
                DrawClothesPartRow(L10n.T("下衣"), 1);
                DrawClothesPartRow(L10n.T("内衣"), 2);
                DrawClothesPartRow(L10n.T("内裤"), 3);
                DrawClothesPartRow(L10n.T("袜"), 10);
                DrawClothesPartRow(L10n.T("鞋"), 11);
            }

            GUILayout.Space(4);

            // --- 场景替换设置 ---
            DrawSectionBox(L10n.T("场景替换角色"), new Color(0.15f, 0.50f, 0.45f, 0.8f), new Color(0.30f, 0.75f, 0.60f, 0.95f));
            KKPEHeightLockPlugin.AutoReplaceOnLoad.Value = GUILayout.Toggle(KKPEHeightLockPlugin.AutoReplaceOnLoad.Value, L10n.T("加载场景后自动替换"));
            KKPEHeightLockPlugin.ReplaceFemaleOnLoad.Value = GUILayout.Toggle(KKPEHeightLockPlugin.ReplaceFemaleOnLoad.Value, L10n.T("替换女角色"));
            KKPEHeightLockPlugin.ReplaceMaleOnLoad.Value = GUILayout.Toggle(KKPEHeightLockPlugin.ReplaceMaleOnLoad.Value, L10n.T("替换男角色"));
            KKPEHeightLockPlugin.PreservePoseOnReplace.Value = GUILayout.Toggle(KKPEHeightLockPlugin.PreservePoseOnReplace.Value, L10n.T("替换后保留原姿势"));
            KKPEHeightLockPlugin.PreserveClothesOnReplace.Value = GUILayout.Toggle(KKPEHeightLockPlugin.PreserveClothesOnReplace.Value, L10n.T("替换后保留原服装"));

            GUILayout.BeginHorizontal();
            GUILayout.Label(L10n.T("女角色卡"), GUILayout.Width(60), GUILayout.ExpandWidth(false));
            GUILayout.Label(FitPath(KKPEHeightLockPlugin.FemaleCardPath.Value), GUI.skin.textField, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(L10n.T("选择"), GUILayout.Width(60), GUILayout.ExpandWidth(false)))
                PickCard(true);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(L10n.T("男角色卡"), GUILayout.Width(60), GUILayout.ExpandWidth(false));
            GUILayout.Label(FitPath(KKPEHeightLockPlugin.MaleCardPath.Value), GUI.skin.textField, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(L10n.T("选择"), GUILayout.Width(60), GUILayout.ExpandWidth(false)))
                PickCard(false);
            GUILayout.EndHorizontal();

            if (GUILayout.Button(L10n.T("立即替换场景角色")))
                SceneReplacer.ReplaceAllCharacters();

            GUILayout.Space(4);

            // --- 场景工具:去无用球体 / 去码(放大按钮 + 自动开关) ---
            DrawSectionBox(L10n.T("场景工具"), new Color(0.75f, 0.45f, 0.15f, 0.8f), new Color(0.95f, 0.65f, 0.30f, 0.95f));
            if (GUILayout.Button(L10n.T("一键消除无用球体"), GUILayout.Height(36)))
                SceneTools.RemoveUselessSpheres();
            if (GUILayout.Button(L10n.T("一键去马赛克"), GUILayout.Height(36)))
                SceneTools.DecensorScene();
            KKPEHeightLockPlugin.AutoRemoveSpheres.Value = GUILayout.Toggle(KKPEHeightLockPlugin.AutoRemoveSpheres.Value, L10n.T("加载场景时自动去无用球体"));
            KKPEHeightLockPlugin.AutoDecensor.Value = GUILayout.Toggle(KKPEHeightLockPlugin.AutoDecensor.Value, L10n.T("加载场景时自动去马赛克"));

            GUILayout.Space(8);

            // --- 身高/身材设置 ---
            DrawSectionBox(L10n.T("身高 / 身材锁定"), new Color(0.50f, 0.20f, 0.55f, 0.8f), new Color(0.75f, 0.40f, 0.80f, 0.95f));
            KKPEHeightLockPlugin.Enabled.Value = GUILayout.Toggle(KKPEHeightLockPlugin.Enabled.Value, L10n.T("启用锁定"));
            KKPEHeightLockPlugin.LockMode.Value = (BodyLockMode)GUILayout.SelectionGrid(
                (int)KKPEHeightLockPlugin.LockMode.Value,
                new[] { L10n.T("仅身高骨骼"), L10n.T("仅体型滑块"), L10n.T("全部体型") },
                3);

            GUILayout.Space(8);

            // --- UI 缩放(VR) ---
            DrawSectionBox(L10n.T("界面缩放(VR)"), new Color(0.25f, 0.35f, 0.55f, 0.8f), new Color(0.45f, 0.60f, 0.85f, 0.95f));
            GUILayout.BeginHorizontal();
            GUILayout.Label(L10n.T("缩放:"), GUILayout.Width(50));
            KKPEHeightLockPlugin.UIScale.Value = GUILayout.HorizontalSlider(KKPEHeightLockPlugin.UIScale.Value, 0.5f, 2.5f);
            GUILayout.Label($"{KKPEHeightLockPlugin.UIScale.Value:0.0}x", GUILayout.Width(40));
            GUILayout.EndHorizontal();
            GUILayout.Label(L10n.T("调整后界面立即生效"), new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Italic });

            GUILayout.Space(8);

            // --- 场景人物(链接工作室选中) ---
            DrawSectionBox(L10n.T("场景人物(点击选中)"), new Color(0.20f, 0.60f, 0.55f, 0.8f), new Color(0.35f, 0.80f, 0.70f, 0.95f));
            var vrChars = ScenePlayerModule.GetSceneCharacters();
            if (vrChars.Count == 0)
            {
                GUILayout.Label(L10n.T("场景中没有角色。"), new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Italic });
            }
            else
            {
                foreach (var c in vrChars)
                {
                    GUILayout.BeginHorizontal();
                    bool isSel = ScenePlayerModule.IsCharacterSelected(c);
                    if (GUILayout.Button(isSel ? L10n.T("● 选中") : L10n.T("○ 选中"), GUILayout.Width(76), GUILayout.Height(30)))
                    {
                        ScenePlayerModule.SelectCharacter(c);
                        isSel = true;
                    }
                    string cname = L10n.T("角色");
                    try
                    {
                        if (c.charInfo != null && c.charInfo.fileParam != null && !string.IsNullOrEmpty(c.charInfo.fileParam.fullname))
                            cname = c.charInfo.fileParam.fullname;
                    }
                    catch (Exception) { }
                    var nameStyle = new GUIStyle(GUI.skin.label);
                    if (isSel) { nameStyle.fontStyle = FontStyle.Bold; nameStyle.normal.textColor = new Color(0.4f, 1f, 0.7f); }
                    GUILayout.Label(cname, nameStyle, GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.Label(L10n.T("点击角色名左侧按钮,可在工作室中选中该角色(供第一人称/服装预设/替换使用)。"), new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Italic });

            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            // 恢复原皮肤
            GUI.skin = oldSkin;

            // 自研窗口交互:拖动 + 右下角 resize,绝对坐标,稳定跟手
            HandleWindowInteraction();
        }

        /// <summary>窗口交互(拖动 + 右下角 resize)。
        /// 在窗口函数末尾调用:此时按钮/Toggle 已消费它们的 MouseDown(事件类型变 Used),
        /// 所以这里只会响应"空白处"的按下,不会误拖控件。
        /// 鼠标坐标(e.mousePosition)与 _windowRect 都是全局屏幕坐标,直接用绝对坐标计算。</summary>
        private static void HandleWindowInteraction()
        {
            var e = Event.current;
            if (e == null) return;

            const float handleSize = 22f;
            var handleRect = new Rect(
                _windowRect.x + _windowRect.width - handleSize,
                _windowRect.y + _windowRect.height - handleSize,
                handleSize, handleSize);

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (handleRect.Contains(e.mousePosition))
                {
                    // 右下角:开始 resize,捕获鼠标(不 Use,保留事件给后续 MouseDrag)
                    _resizingWindow = true;
                    _draggingWindow = false;
                    _dragStartMouse = e.mousePosition;
                    _dragStartWinPos = new Vector2(_windowRect.width, _windowRect.height);
                    GUIUtility.hotControl = WindowId;
                }
                else if (_windowRect.Contains(e.mousePosition))
                {
                    // 窗口内空白处:开始拖动,捕获鼠标
                    _draggingWindow = true;
                    _resizingWindow = false;
                    _dragStartMouse = e.mousePosition;
                    _dragStartWinPos = new Vector2(_windowRect.x, _windowRect.y);
                    GUIUtility.hotControl = WindowId;
                }
            }
            else if (e.type == EventType.MouseDrag && (_draggingWindow || _resizingWindow))
            {
                if (GUIUtility.hotControl != WindowId) return;
                float scale = Mathf.Max(0.5f, KKPEHeightLockPlugin.UIScale.Value);
                if (_draggingWindow)
                {
                    // 绝对坐标移动:窗口左上 = 鼠标 - 按下时的偏移,完全跟手
                    _windowRect.x = _dragStartWinPos.x + (e.mousePosition.x - _dragStartMouse.x);
                    _windowRect.y = _dragStartWinPos.y + (e.mousePosition.y - _dragStartMouse.y);
                    // 防止完全拖出屏幕(至少保留标题栏在屏内);屏幕边界换算为逻辑坐标
                    _windowRect.x = Mathf.Clamp(_windowRect.x, -_windowRect.width + 200f, Screen.width / scale - 60f);
                    _windowRect.y = Mathf.Clamp(_windowRect.y, -20f, Screen.height / scale - 60f);
                }
                else if (_resizingWindow)
                {
                    // 绝对坐标 resize:右下角跟随鼠标
                    _windowRect.width = Mathf.Max(360f, _dragStartWinPos.x + (e.mousePosition.x - _dragStartMouse.x));
                    _windowRect.height = Mathf.Max(300f, _dragStartWinPos.y + (e.mousePosition.y - _dragStartMouse.y));
                }
            }
            else if (e.type == EventType.MouseUp)
            {
                _draggingWindow = false;
                _resizingWindow = false;
                GUIUtility.hotControl = 0;
            }

            // 画 resize 手柄提示(窗口局部坐标绘制)
            var handleLocal = new Rect(_windowRect.width - handleSize, _windowRect.height - handleSize, handleSize, handleSize);
            if (_resizingWindow || handleRect.Contains(e.mousePosition))
            {
                var oldColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
                GUI.DrawTexture(handleLocal, Texture2D.whiteTexture);
                GUI.color = oldColor;
            }
        }

        /// <summary>绘制配置窗口背景图(plugins/KKPEHeightLock/background.png,可选)。</summary>
        private static void DrawWindowBackground()
        {
            if (!_backgroundAttempted)
            {
                _backgroundAttempted = true;
                try
                {
                    var bgPath = System.IO.Path.Combine(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "KKPEHeightLock"), "background.png");
                    if (System.IO.File.Exists(bgPath))
                    {
                        _windowBackground = PngAssist.LoadTexture(bgPath);
                        if (_windowBackground != null)
                            KKPEHeightLockPlugin.Log.LogInfo("KKPEHeightLock: using window background image");
                    }
                }
                catch (Exception e)
                {
                    KKPEHeightLockPlugin.Log.LogWarning("KKPEHeightLock: failed to load background image: " + e.Message);
                }
            }

            if (_windowBackground != null)
            {
                GUI.DrawTexture(new Rect(0, 0, _windowRect.width, _windowRect.height), _windowBackground, ScaleMode.StretchToFill, true);
                // 极淡深色遮罩保证文字可读,但不发白
                GUI.color = new Color(0f, 0f, 0f, 0.35f);
                GUI.DrawTexture(new Rect(0, 0, _windowRect.width, _windowRect.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true);
                GUI.color = Color.white;
            }
            else
            {
                // 无背景图时:深色半透明底,避免刺眼白
                GUI.color = new Color(0.1f, 0.1f, 0.15f, 0.92f);
                GUI.DrawTexture(new Rect(0, 0, _windowRect.width, _windowRect.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true);
                GUI.color = Color.white;
            }
        }
    }
}
