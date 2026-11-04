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
        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
        }

        /// <summary>按窗口宽度动态截断路径,保证不把"选择"按钮挤出窗口。
        /// 窗口宽 560 时 label 60 + button 60 + 边距,textField 可用约 400px,约 55 个字符。</summary>
        private static string FitPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return L10n.T("(未选择)");
            // 随窗口宽度自适应
            int maxChars = Mathf.Max(12, (int)((_windowRect.width - 170f) / 7.2f));
            if (path.Length <= maxChars) return path;
            return "..." + path.Substring(path.Length - (maxChars - 3));
        }
    }
}
