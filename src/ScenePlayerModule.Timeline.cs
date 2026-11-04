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
    public static partial class ScenePlayerModule
    {
        // ============ Timeline 控制 ============

        public static bool TimelinePlaying { get { try { return Timeline.Timeline.isPlaying; } catch (Exception) { return false; } } }
        public static float TimelineDuration { get { try { return Timeline.Timeline.duration; } catch (Exception) { return 0f; } } }

        /// <summary>
        /// 当前时间轴里到底有没有内容。
        ///
        /// 为什么需要这个：Timeline 插件在「没加载到任何时间轴」时 duration 不是 0，
        /// 而是一个默认值（实测 10 秒）。直接拿 duration 当片长，会把一张根本
        /// 没有动画的静态卡录成 10 秒的静止画面。
        /// 判据用插值项数量：有相机/角色/道具动画就一定 ≥1，空的就一定是 0。
        /// </summary>
        public static bool TimelineHasContent
        {
            get
            {
                try
                {
                    var all = Timeline.Timeline.GetAllInterpolables(false);
                    if (all == null) return false;
                    foreach (var item in all) return true;   // 只要有一个就算有
                }
                catch (Exception) { }
                return false;
            }
        }

        public static float TimelineTime { get { try { return Timeline.Timeline.playbackTime; } catch (Exception) { return 0f; } } }

        public static void TimelinePlay() { try { Timeline.Timeline.Play(); } catch (Exception) { } }
        public static void TimelinePause() { try { Timeline.Timeline.Pause(); } catch (Exception) { } }
        public static void TimelineStop() { try { Timeline.Timeline.Stop(); } catch (Exception) { } }
        public static void TimelineSeek(float t) { try { Timeline.Timeline.Seek(t); } catch (Exception) { } }
        public static void TimelineSetTimeScale(float s)
        {
            _timeScale = s;
            try { UnityEngine.Time.timeScale = s; } catch (Exception) { }
        }
        public static float TimelineTimeScale { get { return _timeScale; } }

        // Timeline 相机插值项名(同 sceneplayer.py)
        private static readonly string[] TL_CAMERA_INTERPOLABLE_NAME =
        {
            "Camera Zoom", "Camera FOV", "Camera Position", "Camera Rotation",
            "Camera Origin Position", "Camera Origin Rotation"
        };

        private static System.Collections.Generic.List<Timeline.Interpolable> GetCameraInterpolables()
        {
            var result = new System.Collections.Generic.List<Timeline.Interpolable>();
            try
            {
                // false = 包含已禁用的插值项(否则 CAM 关闭后 enabled=false 就找不到了)
                var all = Timeline.Timeline.GetAllInterpolables(false);
                if (all == null) return result;
                foreach (var item in all)
                {
                    var ip = item as Timeline.Interpolable;
                    if (ip == null) continue;
                    foreach (var camName in TL_CAMERA_INTERPOLABLE_NAME)
                    {
                        if (ip.name == camName) { result.Add(ip); break; }
                    }
                }
            }
            catch (Exception) { }
            return result;
        }

        /// <summary>是否有 Timeline 相机关键帧。</summary>
        public static bool TimelineHasCameraKeyframe
        {
            get { return GetCameraInterpolables().Count > 0; }
        }

        /// <summary>Timeline 相机是否受控(任一相机插值项 enabled)。</summary>
        public static bool TimelineCameraControlled
        {
            get
            {
                foreach (var ip in GetCameraInterpolables())
                    if (ip.enabled) return true;
                return false;
            }
        }

        /// <summary>切换 Timeline 相机控制(同 sceneplayer.py tlToggleCamera)。</summary>
        public static void TimelineToggleCamera()
        {
            try
            {
                var list = GetCameraInterpolables();
                if (list.Count == 0) return;
                bool newState = true;
                foreach (var ip in list)
                    if (ip.enabled) { newState = false; break; }
                foreach (var ip in list)
                    ip.enabled = newState;
                KKPEHeightLockPlugin.Log.LogMessage($"ScenePlayer: timeline camera {(newState ? "controlled" : "free")}");
            }
            catch (Exception e)
            {
                KKPEHeightLockPlugin.Log.LogWarning("ScenePlayer.TimelineToggleCamera error: " + e);
            }
        }
    }
}
