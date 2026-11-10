# 更新日志

版本号在 `src/KKPEHeightLock.cs` 的 `Version` 常量。

---

## 2.0.2

### 修复

- **面板全黑 / 角色卡与场景卡全都加载不出来**（真机复现过）：
  BepInEx 读 `.cfg` 时会把 `\a \b \f \n \r \t \v` 当转义序列，于是配置里的
  `D:\...\chara\female\animfriend 07.png` 读回来变成 `...female` + BEL(0x07) + `nimfriend 07.png`。
  后果有两个：`Path.GetFileNameWithoutExtension` 抛
  `ArgumentException: Illegal characters in path`（每帧都抛，整个导出面板画不出来 → 全黑），
  路径本身也不再指向真实文件（卡加载失败）。
  现在读写两侧都堵住：
  - 新增 `PathFix`：读配置时把控制字符还原成 `\a` 原文，写配置时统一存成 `/`；
  - 启动时自愈：`FemaleCard` / `OutputDir` / `SceneDir` 三项若被转义吃掉字符，
    自动还原并写回（日志里能看到 `[路径修复] …`）；
  - 选卡、输出目录输入框等所有写入点都过 `PathFix.ToConfig`。

### 加固

- 面板按块 `try/catch`（左栏 / 右栏 / 底栏 / 运行状态）：某一块出错只在该块显示错误，
  不再因为一个异常把整个窗口刷成黑板；同一错误只记一次日志。
- **右栏滚动区高度改成实测**：原来写死 `bodyH-244`，角色卡区一变高（换行 / 缩放 / 语言）
  就算不准，滚动视口伸到底栏下面 —— 最后一行「合并成一个视频 / 保留分段视频」
  永远被底栏盖住、拖滚动条也翻不出来。现在用布局游标量出「已用高度」，剩下的才给滚动区，
  视口底边一定停在底栏之上。
- 删掉右栏里关于 ReShade 的两行说明小字（参数归 ReShade 面板，界面不用再念一遍）。
- 删掉右栏「导出设置」标题行，语言下拉并入滚动区（以前被钉在滚动区外，不跟着滚）。
- 右栏内容压缩到一屏放得下：去掉两条分隔线，"合并成一个视频 / 保留分段视频"
  不再被底栏挡住。
- 设置列表支持滚轮滚动：这个游戏里 IMGUI 收不到 ScrollWheel（被 Studio 相机缩放吃掉），
  改为自己读输入，鼠标在设置区里就滚。

---

## 2.0.1

### 修复

- **导出会覆盖 ReShade 里手调的滤镜参数**：每次点「开始导出」，`SyncReshadePreset()`
  都会把配置里的默认档位（`Vignette=Strong / Bloom=Medium` → 阈值 0.62 / 泛光 0.35 /
  半径 4 / 暗角 0.87）写回 `KK_BloomVignette.ini`，`[KK_BloomVignette.fx]` 整段删掉重建；
  而导出时 VideoExport 会改窗口尺寸，ReShade 借 `ResizeBuffers` 重建运行环境并重读预设，
  于是用户在 Home 面板里调好的泛光 / 暗角当场失效，成片也是默认档位。
  现在**整体删除写预设的代码**，插件对 ReShade 只读不写，参数完全归用户。

### 变更

- 删除 `SyncReshadePreset()` / `WriteReshadePreset()` / `ResolveReshadePresetPath()`
  里的写入逻辑（路径解析保留，供只读检查用）。
- 删除配置项 `[AutoExport] Vignette` / `Bloom`、`ExportOptions.Vignette` / `Bloom`
  与 `FxLevel` 枚举 —— 强度不再由插件配。（旧 cfg 里的这两行会变成没人读的残留，可手动删掉。）
- 新增导出前的**只读**日志：打印预设路径、technique 开没开、当前四个参数值；
  没勾选时明确提示「成片不会有泛光 / 暗角」，不再偷偷替用户开。
- 界面提示改为「强度在 ReShade 面板（Home）里调，本插件不改」。

---

## 2.0.0

首个公开源码版本。相对上一版（1.3.0）的变化：

### 新增

- **中英双语界面**：`L10n` + `L10n.Table`，key 就是中文原文，查不到自动回退中文。
  设置里可切「自动 / 中文 / English」，改完立即生效。日志固定中文。
- **换人后等待**（`ReplaceSettleSeconds`，默认 3 秒，面板可调）：换人 + 换装完成后
  等动态骨骼（头发 / 裙子 / 胸部）稳定，解决成片开头头发向上飞的问题。
- 选卡 / 选目录弹框右上角加 **×** 关闭按钮。

### 修复

- **换人等待时序错位**：原来等待排在换装之前，`BodySnap.Compose()` 写回身材时会把
  刚稳住的骨骼状态冲掉，等于白等。现在顺序是「换人 → 换装 → 应用姿态 → 等待」。
- **换人后姿态瞬移**：`Seek(0)` + `Play` 3 帧 + `Pause`，先把时间轴姿态压到最终骨架上，
  再进入等待，避免动态骨骼惯性归零后重新受力炸开。
- **弹框被面板盖住**：选卡 / 选目录窗口改为最后绘制且 `GUI.depth = -2100`，
  比批量导出面板的 `-2000` 更低（IMGUI 里 depth 越小越靠上），点一下不会再被盖下去。
- 导出循环中每张卡结束兜底 `HoldTimeline = false` —— 中途异常退出时不再让时间轴永久冻住。

### 变更

- **移除界面上的「截图方式」选项**：导出固定走 ReShade 通路（`ScreenshotPluginIndex = 2`），
  暴露这个下拉框只会让人以为能选。
- 体型来源提示文案改为「选中 = 场景原角色，未选中 = 角色卡」（原来是「勾 = …」，
  但控件是 Toggle 按钮，显示的是选中态）。

### 清理

- 删除未使用的代码：`AnyFfmpegRunning`、`IsFileClosed`、`MakeGradientTexture`、
  `MakeRoundedGradientTexture`、`MakeSolidTexture`、`_solidTexCache`、`DrawStepRow`、
  `ToggleWristMenu`、`_windowContentHeight`。
- 删除临时的 F9 单卡导出测试入口。
- README 拆成用户向（`README.md`）与开发者向（`docs/DEVELOPMENT.md`）。

---

## 1.3.0 及更早

内部迭代版本，未公开。主要能力：批量导出、换角色卡、服装预设与部件三态、
Timeline 定时长与循环补齐、ReShade 实时泛光暗角、直出淡入淡出、去码去球、场景播放器。
