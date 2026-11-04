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
        /// <summary>构建高对比皮肤:深色背景上,按钮亮色底+白字,文字白色,更醒目。</summary>
        /// <summary>原生皮肤 + 字体放大(不改任何颜色/背景,保留 Unity 默认样式)。</summary>
        private static GUISkin BuildVRSkin(GUISkin baseSkin)
        {
            var skin = UnityEngine.Object.Instantiate(baseSkin);
            int add = 3; // 字号放大

            // 按钮:保持原生浅色立体样式(醒目),文字深色,不加任何染色
            var btn = new GUIStyle(skin.button);
            btn.fontSize = Mathf.Max(14, baseSkin.button.fontSize + add);
            skin.button = btn;

            // 标签:白色(深色背景上清晰)
            var lbl = new GUIStyle(skin.label);
            lbl.fontSize = Mathf.Max(14, baseSkin.label.fontSize + add);
            lbl.normal.textColor = Color.white;
            skin.label = lbl;

            // Toggle:白色
            var tog = new GUIStyle(skin.toggle);
            tog.fontSize = Mathf.Max(14, baseSkin.toggle.fontSize + add);
            tog.normal.textColor = Color.white;
            skin.toggle = tog;

            // 文本框:白字
            var tf = new GUIStyle(skin.textField);
            tf.fontSize = Mathf.Max(14, baseSkin.textField.fontSize + add);
            tf.normal.textColor = Color.white;
            skin.textField = tf;

            // 框:白字
            var box = new GUIStyle(skin.box);
            box.fontSize = Mathf.Max(14, baseSkin.box.fontSize + add);
            box.normal.textColor = Color.white;
            skin.box = box;

            // 窗口:白字标题
            var win = new GUIStyle(skin.window);
            win.fontSize = Mathf.Max(14, baseSkin.window.fontSize + add);
            win.normal.textColor = Color.white;
            skin.window = win;

            return skin;
        }

        /// <summary>功能区标题:原生 box 样式(忽略配色参数,保持原生)。</summary>
        private static void DrawSectionBox(string title, Color top, Color bottom)
        {
            GUILayout.Label(title, GUI.skin.box);
        }

        /// <summary>服装部件三态行(参照 sceneplayer):穿/半脱/脱。</summary>
        private static void DrawClothesPartRow(string partName, int partIndex)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(partName, GUILayout.Width(44));
            var chara = ScenePlayerModule.CurrentEditChar();
            if (GUILayout.Button(L10n.T("穿"), GUILayout.Width(48), GUILayout.Height(28)))
                ScenePlayerModule.SetClothesPart(chara, partIndex, 0);
            if (GUILayout.Button(L10n.T("半脱"), GUILayout.Width(48), GUILayout.Height(28)))
                ScenePlayerModule.SetClothesPart(chara, partIndex, 1);
            if (GUILayout.Button(L10n.T("脱"), GUILayout.Width(48), GUILayout.Height(28)))
                ScenePlayerModule.SetClothesPart(chara, partIndex, 2);
            GUILayout.EndHorizontal();
        }
    }
}
