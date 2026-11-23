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
        // ============ 角色快速编辑 ============

        /// <summary>设置单个服装部件状态(0=穿 1=半脱 2=脱)。部件序:0上衣1下衣2内衣3内裤4泳衣上5泳衣下6泳衣上2 7泳衣下2 8裤袜9连裤袜10袜11鞋</summary>
        public static void SetClothesPart(Studio.OCIChar chara, int part, int state)
        {
            try { if (chara != null) chara.SetClothesState(part, (byte)state); }
            catch (Exception e) { KKPEHeightLockPlugin.Log.LogWarning(e.ToString()); }
        }

        /// <summary>切换预设套装(CoordinateType:0校服1 1校服2 2体操 3泳装 4社团 5便服 6睡衣)。</summary>
        public static void SetCoordinateType(Studio.OCIChar chara, int type)
        {
            try
            {
                if (chara == null || chara.charInfo == null) return;
                var ct = (ChaFileDefine.CoordinateType)type;
                chara.charInfo.ChangeCoordinateTypeAndReload(ct);
            }
            catch (Exception e) { KKPEHeightLockPlugin.Log.LogWarning(e.ToString()); }
        }

        /// <summary>当前预设套装索引(-1=无)。</summary>
        public static int GetCoordinateType(Studio.OCIChar chara)
        {
            try
            {
                if (chara == null || chara.charInfo == null) return -1;
                return (int)chara.charInfo.fileStatus.coordinateType;
            }
            catch (Exception) { return -1; }
        }

        /// <summary>设置角色可见性。</summary>
        public static void SetVisible(Studio.OCIChar chara, bool visible)
        {
            try
            {
                if (chara == null) return;
                chara.SetVisibleSimple(visible);
            }
            catch (Exception e) { KKPEHeightLockPlugin.Log.LogWarning(e.ToString()); }
        }
    }
}
