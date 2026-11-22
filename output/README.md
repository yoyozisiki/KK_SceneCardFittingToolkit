# 交付目录（output）

这里放的是**能直接覆盖进游戏根目录**的成品，不用再从别处东拼西凑。

版本：**Scene Card Fitting Toolkit 2.2.3**（`BepInEx/plugins/kk_scenecardfittingtoolkit.dll`）

## 怎么装

把本目录里的东西按原结构覆盖进游戏根目录（`CharaStudio.exe` 所在目录）：

```
<游戏根目录>\
  BepInEx\plugins\kk_scenecardfittingtoolkit.dll   ← 本插件
  BepInEx\plugins\VideoExport.dll                  ← 录制，必需
  BepInEx\plugins\VideoExport\                     ← ffmpeg / gifski / 零拷贝模块
  BepInEx\plugins\KKSAPI.dll (+ .NativeHelper)      ← 工作室接口，必需
  BepInEx\plugins\KKSPE.dll                        ← 身高锁 / 碰撞体控制用
  BepInEx\plugins\Timeline.dll                     ← 读时间轴，必需
  KKReshade.addon                                  ← 改造版：ReShade 共享内存截图（导出效果要靠它）
  reshade\KK_BloomVignette.fx                      ← 泛光 + 暗角着色器
  reshade-shaders\Shaders\                         ← 效果链要用的着色器（见下表）
  reshade-shaders\Textures\                        ← 对应贴图
```

`安装说明.txt` 里有更细的依赖版本要求和排错说明。

## 里面每个东西是干什么的

| 文件 | 说明 |
|---|---|
| `kk_scenecardfittingtoolkit.dll` | 本插件，由 `KK_SceneCardFittingToolkit/` 源码编译（`dotnet build -c Release`） |
| `VideoExport.dll` / `VideoExport/` | 逐帧录制 + 编码。`ffmpeg` 必须是完整版（带 H.264/AMF 编码器） |
| `KKSAPI.dll` | Koikatsu Sunshine Modding API，场景载入事件、工具栏按钮都靠它 |
| `KKSPE.dll` | 身高骨骼锁、关闭动态骨骼碰撞体吸附用到的类型 |
| `Timeline.dll` | 读场景卡时间轴、逐帧驱动播放 |
| `KKReshade.addon` | 改造版 addon：把 ReShade 处理后的画面通过共享内存递给截图插件（导出时泛光/暗角才能进成片）。原版备份在游戏目录的 `KKReshade.addon.orig-20261121` |
| `reshade/KK_BloomVignette.fx` | 泛光 + 暗角效果，被 `reshade_ini/KK_BloomVignette.ini` 预设引用 |
| `reshade-shaders/Shaders/` | `KK_BloomVignette.fx`（泛光暗角）、`AMD_Neural_Feed.fx` + `MartysMods_LAUNCHPAD.fx`（AMD 降噪链的运动矢量）、iMMERSE 免费版 6 个（MXAO / SMAA / SOLARIS / FILMGRAIN / SHARPEN）及 `MartysMods/*.fxh` 依赖 |

## 关于 git

这个目录里的二进制（dll / addon / ffmpeg，约 120MB）**不进 git**：
`output/.gitignore` 已经把内容全忽略，只保留本 README 和 `安装说明.txt`。
要发版就走 GitHub Releases，把整个 `output` 打 zip 传上去。
