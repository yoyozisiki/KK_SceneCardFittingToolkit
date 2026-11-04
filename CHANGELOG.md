# 更新日志

版本号在 `src/KKPEHeightLock.cs` 的 `Version` 常量。

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
