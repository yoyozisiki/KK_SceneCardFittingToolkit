using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KKPEHeightLock
{
    public static partial class AutoExport
    {
        // ======================================================== 单张场景卡
        private delegate void StringSink(string value);

        private static IEnumerator ExportOne(ExportOptions o, string scenePath,
                                             string segRoot, int index, int attempt, StringSink done)
        {
            done(null);
            string tag = "s" + index.ToString("000") + "_" + SafeName(Path.GetFileNameWithoutExtension(scenePath));
            // 被暂停打断后重录时换个目录（_r2、_r3…）：VideoExport 收尾时还在用旧目录，
            // 不能删，换个新的最干净。预览不录制，不需要建目录。
            string segDir = null;
            if (!o.PreviewOnly)
            {
                segDir = Path.Combine(segRoot, attempt > 0 ? (tag + "_r" + (attempt + 1)) : tag);
                try { Directory.CreateDirectory(segDir); }
                catch (Exception e) { Log("建片段目录失败：" + e.Message); yield break; }
            }

            // 暂停中就先别开工（ESC 随时可能被按下）
            while (Paused && !CancelRequested) yield return null;
            if (CancelRequested) yield break;

            // ---- 1) 加载场景（手动驱动 Studio 原生加载协程）
            SetPhase("加载场景：" + CurrentSceneName);
            HookSceneLoadEvent();
            // 必须在加载前就按住时间轴：卡一读完 Timeline 会自动开播，
            // 事后再 Seek(0) 会让骨骼瞬移、动态骨骼炸开。
            HoldTimeline = true;
            bool loadOk = false;
            IEnumerator load = LoadScene(scenePath, delegate(bool ok) { loadOk = ok; });
            while (load.MoveNext()) yield return load.Current;
            if (!loadOk)
            {
                HoldTimeline = false;
                Log("[失败] 场景加载没成功，跳过这张");
                yield break;
            }

            // 加载协程返回 ≠ Studio 已经把卡吃下去了，等 KKAPI 的 SceneLoad 事件。
            // 事件 15 秒不来通常意味着这张卡没读进来，只告警不拦，靠后面的画面自证。
            SetPhase("等待场景载入…");
            float waitLoad = Time.realtimeSinceStartup + 15f;
            while (!_sceneLoadFired && Time.realtimeSinceStartup < waitLoad)
                yield return null;
            if (!_sceneLoadFired)
                Log("[警告] 15 秒内没收到 KKAPI 的 SceneLoad 事件，这张卡可能没真正载入");
            else
                Log("场景已载入，开始处理");

            // 事件之后贴图/模型还要一点时间上屏，固定给 2 秒，再等相机停稳
            yield return new WaitForSeconds(2f);
            IEnumerator settle = WaitCameraSettle(8f);
            while (settle.MoveNext()) yield return settle.Current;

            // 加载完把动画按住：换角色期间画面不动。这里不 Seek(0) —— hold 已经按在开头，
            // 再 Seek 一次就是把骨骼拉回去的瞬移。
            ScenePlayerModule.TimelinePause();
            for (int f = 0; f < 10; f++) yield return null;

            // ---- 1-c) 角色光照亮度封顶
            // 只压 CameraLightCtrl 里那盏角色灯（KKUSS/KKUTS 材质几乎只吃它），不动场景灯光。
            // 只在应用了全局皮肤卡时才做：材质被换过才需要防过曝，否则保留场景原亮度。
            bool clampLight = o.ClampCharLight > 0f && !string.IsNullOrEmpty(o.GlobalSkinCard);
            if (clampLight)
            {
                SetPhase("限制角色光照…");
                try { SceneTools.ClampCharacterLighting(o.ClampCharLight); }
                catch (Exception e) { Log("角色光照封顶失败：" + e.Message); }
                yield return null;
            }

            // ---- 2) 替换 female（含体型三维来源：体型滑块 / 乳量物理 / 骨架类型 / 身高）
            // 主卡换第一个显示角色，其余显示角色按次卡列表；隐藏角色一律用主卡。
            // 前后关系靠这个集合识别，替换和服装都用它（TimelineReader 有缓存）。
            HashSet<int> revealed = TimelineReader.ReadRevealedObjects(scenePath);
            bool hasMain = !string.IsNullOrEmpty(o.FemaleCard);
            bool hasSubs = o.SubCards != null && o.SubCards.Count > 0;
            bool didReplace = false;
            if (o.ReplaceFemale && (hasMain || hasSubs))
            {
                SetPhase("替换 female：主卡 " + (hasMain ? NameOf(o.FemaleCard, false) : "（无）")
                    + " / 次卡 " + (hasSubs ? o.SubCards.Count + " 张" : "（无）"));
                IEnumerator rep = ReplaceFemale(o, revealed);
                while (rep.MoveNext()) yield return rep.Current;
                didReplace = true;
            }
            else if (o.ReplaceFemale)
            {
                Log("[提示] 勾了「替换 female」但主卡和次卡都没选，跳过替换");
            }

            // 停止：替换阶段被中断后，后面这些准备步骤一律不再做
            if (CancelRequested) { HoldTimeline = false; yield break; }

            // ---- 2-b) 服装预设 / 部件状态（放在换人之后、体型已落定，所以先存后还）
            SetPhase("设置服装…");
            IEnumerator cloth = ApplyClothes(o);
            while (cloth.MoveNext()) yield return cloth.Current;

            // ---- 2-b2) 全局 female 皮肤（face + body 材质）
            // 全局皮肤放到最后一步应用（见 3-e），这里不再处理。

            // ---- 2-c) 人物全部弄完之后，才应用时间轴姿态 + 等物理稳定
            //
            // 顺序：换人、体型、服装、饰品每一项都会重建网格或改骨骼，所以姿态和等待
            // 必须放最后。ChangeChara 返回时骨架是角色卡的默认姿态，直接开录的话
            // Play 那一下骨骼瞬移到时间轴姿态，动态骨骼惯性会炸开。
            if (CancelRequested) { HoldTimeline = false; yield break; }

            if (didReplace)
            {
                SetPhase("应用时间轴姿态…");
                HoldTimeline = false;
                ScenePlayerModule.TimelineSeek(0f);
                ScenePlayerModule.TimelinePlay();
                for (int f = 0; f < 3; f++) yield return null;
                ScenePlayerModule.TimelinePause();
                HoldTimeline = true;

                Log($"[时序] 换人+换装完成 → 应用姿态"
                    + $"（时间轴 {ScenePlayerModule.TimelineTime:0.###}s，"
                    + $"播放中={Yn(ScenePlayerModule.TimelinePlaying)}）");
            }

            // ---- 2-d) 关闭 KKPE 碰撞体的「Enable New Dynamic Bones」
            // 换人后新饰品/衣服的动骨会被自动登记进碰撞体影响列表 → 头发被吸进脸。
            // 角色重建后可能又冒出新的，这里再补关一次。
            if (didReplace)
            {
                SetPhase("修复碰撞体误吸附饰品…");
                try { ColliderBindingFix.DisableAutoAttachNewBones(); }
                catch (Exception) { }
                yield return null;
            }

            // ---- 3) 去码 / 去球
            if (o.RemoveSpheres)
            {
                SetPhase("去无用球体…");
                try { SceneTools.RemoveUselessSpheres(); }
                catch (Exception e) { Log("去球体失败：" + e.Message); }
                yield return null;
            }
            if (o.Decensor)
            {
                SetPhase("去马赛克…");
                try { SceneTools.DecensorScene(); }
                catch (Exception e) { Log("去码失败：" + e.Message); }
                yield return null;

                // 去码只管材质，碰不到独立摆放的胶囊遮挡和 Studio Text 的文字对象，
                // 这两类同样会挡镜头，一起清掉。
                SetPhase("去遮挡物…");
                try { SceneTools.RemoveObstructions(); }
                catch (Exception e) { Log("去遮挡物失败：" + e.Message); }
                yield return null;
            }

            // ---- 3-c0) 角色光照再压一次：换人/换装后 CameraLightCtrl 会 Reflect() 把场景里
            // 保存的光照重新应用，之前压下去的值会被盖回 1.2 那种。
            if (clampLight)
            {
                SetPhase("再次限制角色光照…");
                try { SceneTools.ClampCharacterLighting(o.ClampCharLight); }
                catch (Exception e) { Log("角色光照再次封顶失败：" + e.Message); }
                yield return null;
            }

            // ---- 3-c) ShadowPower 过曝抑制：只作用于 shader 为 KKUSSitem 的衣物/饰品材质
            // 放在换人/换装之后：这时材质才是最终那套。不动 Addcolor（作者调过的配色）。
            if (o.MinShadowPower > 0f)
            {
                SetPhase("抑制过曝（ShadowPower）…");
                try { SceneTools.ClampKkussOverexposure(o.MinShadowPower); }
                catch (Exception e) { Log("过曝抑制失败：" + e.Message); }
                yield return null;
            }

            // ---- 3-d) male 半透明（身体 + 服装 + 饰品）
            // 放在去码之后：换 shader 会重置材质状态，先做完别的再动它。
            if (o.MaleAlpha > 0f && o.MaleAlpha < 0.999f)
            {
                SetPhase("male 半透明…");
                try { SceneTools.ApplyMaleTransparency(o.MaleAlpha); }
                catch (Exception e) { Log("male 半透明失败：" + e.Message); }
                yield return null;
            }

            // ---- 3-e) 全局皮肤：放在所有角色/材质操作的最后
            // 换人、换装、光照、过曝、male 半透明都会重建或改写材质，皮肤放前面写了也会被盖回去
            // （日志里出现过「写入 shader=KKLT/Skin、回读还是 Koikano/main_skin」）。
            if (!string.IsNullOrEmpty(o.GlobalSkinCard))
            {
                SetPhase("应用全局皮肤…");
                try { SkinMaterialCache.ApplyToAll(o.GlobalSkinCard); }
                catch (Exception e) { Log("应用全局皮肤失败：" + e.Message); }

                // ReloadNoAsync 和 MaterialEditor 的持久化材质会在后续帧重建材质，
                // 隔两帧再写一遍（跳过重建那几步），确保最终用的是模板材质。
                yield return null;
                yield return null;
                try { SkinMaterialCache.ApplyToAll(o.GlobalSkinCard, true); }
                catch (Exception e) { Log("应用全局皮肤（二次）失败：" + e.Message); }
                yield return null;
            }

            // ---- 3-b) 等物理稳定（所有准备工作之后、开录之前）
            // 上面每一步都会重建网格或改骨骼，统一收到这里：全部改完 → 让动骨静下来 → 开录。
            if (didReplace)
            {
                float settleSec = 3f;
                try { settleSec = Mathf.Max(0f, KKPEHeightLockPlugin.ExportReplaceSettle.Value); }
                catch (Exception) { }
                if (settleSec > 0f)
                {
                    SetPhase("等物理稳定 " + settleSec.ToString("0.#") + "s…");
                    yield return new WaitForSeconds(settleSec);
                }
                Log($"[时序] 全部准备完成（换人/换装/碰撞体/去码）→ 等物理稳定 {settleSec:0.#}s → 开录");
            }

            // ---- 3-f) 物理等待结束后再写一遍皮肤
            // 动骨稳定期间材质可能被 MaterialEditor 的持久化数据重建过一次，
            // 这里补最后一笔，保证开录时用的就是模板材质。
            if (!string.IsNullOrEmpty(o.GlobalSkinCard))
            {
                try { SkinMaterialCache.ApplyToAll(o.GlobalSkinCard, true); }
                catch (Exception e) { Log("应用全局皮肤（收尾）失败：" + e.Message); }
                yield return null;
                yield return null;
            }

            // ---- 预览到此为止：替换/换装/姿态/碰撞体/去码都做完了，直接交回画面。
            if (o.PreviewOnly)
            {
                HoldTimeline = false;                       // 放开时间轴，用户可以自己播着看
                try { ScenePlayerModule.TimelinePause(); } catch (Exception) { }
                Log("[预览] 场景已按导出流程完成替换（未录制）");
                SetPhase("预览完成：可以手动播放检查效果");
                done("preview");
                yield break;
            }

            // ---- 4) 算时长：content = max(卡里的 Timeline 秒数, MinSeconds)
            //   * 有 Timeline 且比下限长 → 按 Timeline 录
            //   * 没 Timeline / 比下限短 → 录 MinSeconds（短卡循环补齐）
            //
            // Timeline 秒数优先从场景卡文件读（不用加载场景也准），读不到再问运行时插件。
            // 只看运行时的 duration 不行：没有时间轴时它不是 0 而是默认 10 秒，
            // 会把静态卡录成 10 秒静止画面。
            float minSec = Mathf.Max(0.5f, o.MinSeconds);
            float tlSec = 0f;
            string tlFrom = null;

            var cardTl = TimelineReader.Read(scenePath);
            if (cardTl != null && cardTl.Seconds > 0.01f)
            {
                tlSec = cardTl.Seconds;
                tlFrom = "卡片文件";
            }
            else
            {
                float liveDur = ScenePlayerModule.TimelineDuration;
                bool tlHasContent = ScenePlayerModule.TimelineHasContent;
                float scale = Time.timeScale > 0.01f ? Time.timeScale : 1f;
                if (tlHasContent && liveDur > 0.01f)
                {
                    tlSec = liveDur / scale;
                    tlFrom = "运行时·倍速 " + scale.ToString("0.##");
                }
            }

            float content = Mathf.Max(tlSec, minSec);
            bool loop = tlSec > 0.01f && tlSec < minSec - 0.001f;

            // 头尾各多录 FadeSeconds 秒（时间轴照常往前跑），录的时候用黑场压掉，
            // 正片一秒不少、动作也不定格。
            float pad = o.FadePad;
            float total = content + pad;
            int expectFrames = Mathf.Max(1, Mathf.RoundToInt(total * o.Fps));
            string tail = (pad > 0 ? $" + 头尾各 {o.FadeSeconds:0.##}s 淡入淡出" : "")
                        + $" = {total:0.##}s / {expectFrames} 帧";
            if (tlSec <= 0.01f)
                Log($"没有 Timeline → 按 {content:0.##}s 录" + tail);
            else if (loop)
                Log($"Timeline {tlSec:0.##}s（{tlFrom}）短于下限 {minSec:0.##}s → 循环补齐到 "
                    + $"{content:0.##}s" + tail);
            else
                Log($"Timeline {tlSec:0.##}s（{tlFrom}）→ 正片 {content:0.##}s" + tail);

            // ---- 5) 注入 VideoExport 参数
            // 限长用「帧数」模式：我们自己逐帧定位时间轴，VideoExport 只管数够帧数收尾，
            // 这样成片帧数和时间轴严格对齐，不会因为掉帧而变速。
            //
            // 先等 VideoExport 空下来：上一张卡如果是被暂停打断的，ffmpeg 还在收尾，
            // 这时写参数/开录会被拒掉。
            if (VeCtl.IsRecording || VeCtl.IsGenerating)
            {
                SetPhase("等上一个片段收尾…");
                float busy = Time.realtimeSinceStartup + 60f;
                while ((VeCtl.IsRecording || VeCtl.IsGenerating) && Time.realtimeSinceStartup < busy)
                    yield return new WaitForSeconds(0.2f);
            }

            SetPhase("配置 VideoExport…");
            if (!VeCtl.Configure(o, segDir, expectFrames, loop))
            {
                Log("[失败] VideoExport 参数写入失败，跳过这张");
                yield break;
            }
            yield return null;
            LogCamera("开录前");
            if (CameraInsideCharacter())
                Log("[警告] 相机贴在角色身上（<0.35m），这张卡的相机可能没被应用，成片会糊满身体");

            // 开录前最后一次暂停检查：参数都写好了但还没按录制键，暂停在这里不损失任何东西
            while (Paused && !CancelRequested) yield return null;
            if (CancelRequested) { HoldTimeline = false; yield break; }

            // ---- 6) 开录 + 逐帧驱时间轴
            SetPhase("出帧中…");
            // 换场景/换人都会新建 Canvas 和组件，开录前再清一次
            if (o.RealtimeFx) EnforceCinemaMode();
            string before = VeCtl.SnapshotNewestMp4(segDir);
            bool rtFade = o.RealtimeFade;   // 淡入淡出在屏上做，不走 ffmpeg

            // 时间轴继续保持按住，开录后才由 CaptureFrames 放行；全程不做 Seek。
            ScenePlayerModule.TimelinePause();
            HoldTimeline = false;
            // 黑场必须在 Record() 之前压上：VideoExport 从按下录制那一帧就开始采
            if (rtFade) ApplyFade(1f);

            VeCtl.Record();
            // 等第一帧真正被采集（避开 ffmpeg 启动抖动）
            float waitStart = Time.realtimeSinceStartup;
            while (VeCtl.CurrentFrame < 1 && VeCtl.IsRecording
                   && Time.realtimeSinceStartup - waitStart < 15f)
                yield return new WaitForEndOfFrame();
            if (!VeCtl.IsRecording)
            {
                Log("[失败] 录制没有启动（VideoExport 拒绝开始）");
                yield break;
            }

            int fps = Mathf.Max(1, o.Fps);
            int fadeFrames = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(0f, o.FadeSeconds) * fps),
                                         0, Mathf.Max(0, expectFrames / 2));
            Log($"逐帧渲染：{total:0.##}s × {fps}fps = {expectFrames} 帧"
                + $"（正片 {content:0.##}s，时间轴 {tlSec:0.##}s{(loop ? "，循环补齐" : "")}，"
                + $"头尾各 {fadeFrames} 帧淡入淡出）");

            // 开录这一刻清掉暂停标记：等待期间按下的暂停已经在这里兑现了，
            // 之后再按 ESC 才是打断这张卡。
            _pauseInterrupt = false;
            IEnumerator cap = CaptureFrames(o, tlSec, loop, expectFrames, fadeFrames, rtFade);
            while (cap.MoveNext()) yield return cap.Current;

            if (o.RealtimeFx) RestoreImgui();   // 录完立刻还原，别拖累下一张卡

            // 暂停打断：视频是半截的，上层会丢掉它并重录这张卡
            if (_pauseInterrupt)
            {
                HoldTimeline = false;
                SetPhase("已暂停：按 ESC 继续");
                Log("[暂停] 这张卡没录完，半成品将被丢弃并重录");
                yield break;
            }

            int gotFrames = VeCtl.CurrentFrame;
            Log($"采集结束：{gotFrames} 帧（目标 {expectFrames}）");
            if (gotFrames < expectFrames)
                Log($"[警告] 少采了 {expectFrames - gotFrames} 帧，成片会比预期短"
                    + "（通常是渲染卡住或 VideoExport 提前收尾）");

            // ---- 7) 等 ffmpeg 把文件收尾
            // 判据是「mp4 结构完整 + 大小稳定」：VideoExport 的 _generatingVideo /
            // IsFFmpegProcessRunning 在编码刚开始时还是 false，靠它们会立刻返回。
            SetPhase("等待编码收尾…");
            float encStart = Time.realtimeSinceStartup;
            string raw = null;
            bool reported = false;
            long lastSize = -1;
            int stable = 0;
            while (Time.realtimeSinceStartup - encStart < 1800f)
            {
                raw = VeCtl.FindOutput(segDir, before);
                if (raw != null)
                {
                    long size = FileSize(raw);

                    if (size > 0 && size == lastSize) stable++;
                    else stable = 0;
                    lastSize = size;

                    // 首选判据：mp4 结构完整 + 大小 1 秒没变；兜底：大小 10 秒没变。
                    // 不用「ffmpeg 进程在不在」当兜底 —— faststart 收尾阶段进程已退出、
                    // 文件却还在重写。
                    bool finalized = IsMp4Finalized(raw);
                    if (size > 0 && finalized && stable >= 5) break;
                    if (size > 0 && !finalized && stable >= 50)
                    {
                        Log("[提示] 10 秒没有新数据且 mp4 结构不完整，按大小稳定判定编码结束");
                        break;
                    }
                }
                else if (!reported && Time.realtimeSinceStartup - encStart > 20f)
                {
                    reported = true;
                    Log("[诊断] 20 秒还没等到视频文件，片段目录内容：");
                    try
                    {
                        foreach (var f in Directory.GetFileSystemEntries(segDir))
                            Log("  · " + Path.GetFileName(f));
                    }
                    catch (Exception e) { Log("  （列不出来：" + e.Message + "）"); }
                }
                yield return new WaitForSeconds(0.2f);
            }

            // ---- 7-b) 定稿确认（并且重新锁定文件）
            // VideoExport 混流成功后会把原始文件删掉、只留 final_<时间>.mp4，
            // 所以每轮都要重新找当前最新的 mp4，不能抱着旧路径。
            float settleEnd = Time.realtimeSinceStartup + 20f;
            long settleLast = -1;
            int settleOk = 0;
            while (Time.realtimeSinceStartup < settleEnd)
            {
                var cur = VeCtl.FindOutput(segDir, before);
                if (!string.IsNullOrEmpty(cur) && File.Exists(cur))
                {
                    long sz = FileSize(cur);
                    bool ok = sz > 0 && IsMp4Finalized(cur) && sz == settleLast;
                    settleOk = ok ? settleOk + 1 : 0;
                    settleLast = sz;
                    raw = cur;
                    if (settleOk >= 4) break;
                }
                else
                {
                    settleOk = 0;
                    settleLast = -1;
                }
                yield return new WaitForSeconds(0.2f);
            }

            // 最后再锁一次：混流刚完成的一瞬间，文件名会从 <时间>.mp4 变成 final_<时间>.mp4
            var latest = VeCtl.FindOutput(segDir, before);
            if (!string.IsNullOrEmpty(latest) && File.Exists(latest) && FileSize(latest) > 0
                && !string.Equals(latest, raw, StringComparison.OrdinalIgnoreCase))
            {
                Log("已锁定混流后的文件：" + Path.GetFileName(latest)
                    + (string.IsNullOrEmpty(raw) ? "" : "（原 " + Path.GetFileName(raw) + " 已不在）"));
                raw = latest;
            }
            else if (!string.IsNullOrEmpty(latest) && File.Exists(latest) && FileSize(latest) > 0)
                raw = latest;

            if (string.IsNullOrEmpty(raw) || !File.Exists(raw))
            {
                Log("[失败] 没找到 VideoExport 的输出文件");
                yield break;
            }

            long rawLen = FileSize(raw);
            if (settleOk < 4)
                Log("[警告] 20 秒内文件一直没定稿（大小 " + rawLen + " 字节），后面可能出错");
            Log("原始片段：" + Path.GetFileName(raw) + "（" + HumanSize(rawLen) + "）");
            if (rawLen < 2048)
                Log("[警告] 原始片段异常小（" + rawLen + " 字节），VideoExport 可能没正常编码");

            // ---- 8) 改名成本段的最终文件
            // 不跑 ffmpeg 后期：泛光/暗角由 ReShade 实时出、淡入淡出是录制时盖的黑场，
            // 两样都已经在画面里，重编码只会掉画质。
            string finalPath = Path.Combine(segRoot, tag + ".mp4");
            TryMove(raw, finalPath);

            if (!File.Exists(finalPath))
            {
                Log("[失败] 片段文件没有生成：" + finalPath);
                yield break;
            }

            // 中间帧（BMP/PNG 序列）一张都几 MB，整段能吃掉几个 GB —— 成片出来就清掉
            CleanupFrames(segDir);

            done(finalPath);
        }


        // ======================================================== 场景加载
        private static bool _sceneLoadFired;

        /// <summary>本次进程里 SceneLoad 触发过几次。Studio 启动时会载入默认场景，
        /// 那一次也算，所以「> 0」就等于「Studio 已经把初始场景读完了」。</summary>
        private static int _sceneLoadCount;

        /// <summary>
        /// 订阅 KKAPI 的「场景加载完成」事件 —— Studio 自己认为这张卡读完了的唯一权威信号。
        /// </summary>
        internal static void HookSceneLoadEvent()
        {
            _sceneLoadFired = false;
            try
            {
                KKAPI.Studio.SaveLoad.StudioSaveLoadApi.SceneLoad -= OnKkapiSceneLoad;
                KKAPI.Studio.SaveLoad.StudioSaveLoadApi.SceneLoad += OnKkapiSceneLoad;
            }
            catch (Exception e) { Log("[场景] 订阅 SceneLoad 失败：" + e.Message); }
        }

        /// <summary>KKAPI 的 SceneLoad 回调。</summary>
        private static void OnKkapiSceneLoad(object sender, KKAPI.Studio.SaveLoad.SceneLoadEventArgs e)
        {
            _sceneLoadFired = true;
            _sceneLoadCount++;
        }

        private static IEnumerator LoadScene(string path, Action<bool> done)
        {
            done(false);
            Studio.Studio studio = null;
            try { studio = UnityEngine.Object.FindObjectOfType<Studio.Studio>(); }
            catch (Exception) { }
            if (studio == null)
            {
                Log("找不到 Studio 实例");
                yield break;
            }

            // 换场景前先把动画停掉：Timeline 一直跑会占着主线程，加载协程容易卡住
            ScenePlayerModule.TimelinePause();

            IEnumerator inner = null;
            try { inner = studio.LoadSceneCoroutine(path); }
            catch (Exception e) { Log("LoadSceneCoroutine 异常：" + e.Message); }

            bool failed = false;
            if (inner != null)
            {
                // 手动 MoveNext：加载协程里一旦有插件抛异常，交给 Unity 调度会把整条链掐掉，
                // 我们就永远等不到结束。
                while (true)
                {
                    bool moved;
                    try { moved = inner.MoveNext(); }
                    catch (Exception e)
                    {
                        Log("加载过程中异常：" + e.Message);
                        failed = true;
                        break;
                    }
                    if (!moved) break;
                    yield return inner.Current;
                }
            }
            else
            {
                try { studio.LoadScene(path); }
                catch (Exception e) { Log("LoadScene 异常：" + e.Message); failed = true; }
            }

            if (!failed)
            {
                // 这里不 Seek(0)：hold 已经把时间轴按在开头，再 Seek 反而会造成瞬移
                ScenePlayerModule.TimelinePause();
                done(true);
            }
        }
    }
}
