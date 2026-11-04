# Scene Card Fitting Toolkit · 场景卡套档工具箱

Koikatsu Sunshine（KKS）CharaStudio 的 BepInEx 插件。

一句话：**把一堆场景卡自动录成视频** —— 自动加载场景、换角色卡、换服装、按 Timeline 定时长、
开 ReShade 泛光暗角、录完自动合并，你只要勾卡、点开始。

顺带把常用的场景操作（上一张 / 下一张、随机场景、随机女卡、批量换装、去码、去球）也做进同一个面板。

---

## 它能干什么

| 功能 | 说明 |
|---|---|
| **批量录视频** | 勾选任意多张场景卡 → 一张接一张自动录成 mp4，可选合并成一个长片 |
| **换角色卡** | 把场景里的 female 全换成你指定的角色卡；体型 / 乳量 / 骨架 / 身高可分别选择「跟场景」还是「跟卡」 |
| **换服装** | 导出前切服装预设 + 逐个部件设「穿 / 半脱 / 脱」 |
| **定时长** | 卡里有 Timeline 就按 Timeline 长度录；没有或太短就按你设的下限循环补齐 |
| **淡入淡出** | 头尾各多录 N 秒，屏上压黑场，直出成片不跑 ffmpeg |
| **去码 / 去球** | 自动移除马赛克材质、隐藏灯光阴影辅助球 |
| **ReShade 实时后期** | 泛光 + 暗角在 GPU 上实时做，VideoExport 出的 mp4 就是成品 |
| **场景播放器** | 主窗口里直接上/下一张场景卡、随机场景、随机换人 |
| **中英双语界面** | 自动跟随系统语言，也可以手动锁定 |

---

## 安装

1. 游戏需要已经装好 **BepInEx 5**（KKS 的 64 位版本）。
2. 把 `KK_SceneCardFittingToolkit.dll` 放进 `BepInEx/plugins/`。
3. 启动 **CharaStudio.exe**（不是游戏本体）。

可选：在 `BepInEx/plugins/KKPEHeightLock/` 下放 `icon.png` / `background.png`，
可以替换工具栏图标和面板背景图。

### 依赖

| 组件 | 是否必需 | 用途 |
|---|---|---|
| BepInEx 5 (x64) | **必需** | 插件加载 |
| VideoExport | **必需** | 实际录制 mp4，没有它导出按钮不可用 |
| KKAPI (KKSAPI) | **必需** | 工作室接口、场景载入事件、工具栏 |
| ReShade + `reshade/KK_BloomVignette.fx` | 可选 | 泛光 / 暗角 |
| Timeline | 可选 | 读片长；没有就按你设的下限录 |

---

## 快速上手

1. 进 CharaStudio，点左下角工具栏的**深红胶片图标**打开面板。
2. 左栏找到场景卡目录，勾选要录的卡（可以整目录勾）。
3. 右栏选角色卡、设时长、设帧率。
4. 底栏确认输出目录 → 点 **开始导出**。
5. 等着。运行中可以随时点 **停止**，当前这张录完就收尾。

> 没有全局快捷键 —— F9 常被 ReShade 截图占用，F8 / Ctrl+F8 又和别的插件撞，
> 所以入口只留工具栏图标。

---

## 界面说明

### 主窗口 · 场景播放器

- `◀ / ▶` 上一张 / 下一张场景卡，`刷新` 重扫目录，`随机` 随机跳一张
- `随机女 / 随机男 / 随机全部` 立刻给场景里的人换卡（不录视频，只是换人）
- `部件三态` 展开后可以逐个部件点「穿 / 半脱 / 脱」
- 底部是「场景替换角色」和「场景工具」（去球、去码）

### 批量导出窗口 · 左栏（场景卡树）

- 目录递归成树，`▼ / ▶` 折叠展开
- 点目录名 = 勾选 / 取消整个子树；点 `选` 列的方块只切自己
- 点卡片名 = 选中并在下方出预览（缩略图 + Timeline 秒数 + 预计帧数）
- `搜索` 同时匹配卡片名和目录名
- `全选 / 清空 / 反选` 作用于当前列表

### 批量导出窗口 · 右栏（导出设置）

| 项 | 说明 |
|---|---|
| 替换女性角色卡 | 选一张卡，导出时替换掉场景里的所有 female |
| 语言 | 自动 / 中文 / English，改完立即生效 |
| 输出尺寸 | `0 × 0` = 用游戏窗口原尺寸 |
| 帧率 | 默认 60 |
| 时长 | 片长下限。Timeline 更长就按 Timeline，更短或没有就录这么久（循环补齐） |
| 头尾各多录 | 淡入淡出用的额外秒数，`0` = 不淡入淡出 |
| 换人后等待 | 换完人 + 换完装之后等多少秒让头发 / 裙子物理稳定（默认 3 秒） |
| 体型来源 | 选中 = 用场景原角色的体型 / 乳量 / 骨架，未选中 = 用角色卡自己的 |
| 服装预设 | 切到第几套预设，`不改` = 保留场景原服装 |
| 去马赛克 / 去无用球体 | 导出前自动清一遍 |
| 合并成一个视频 / 保留分段 | 全部录完后是否用 ffmpeg 拼成一个长片 |

### 片长怎么算

```
正片   = max(Timeline 秒数, 时长下限)
总秒数 = 正片 + 头尾各多录 × 2
总帧数 = 总秒数 × 帧率
```

多录的那几秒时间轴照常往前走（画面在动，只是被黑场盖住），所以不会出现「开头定格」。

---

## 常见问题

**Q：录出来的开头头发往上飞 / 裙子炸开？**
A：换人后动态骨骼需要时间收敛。把「换人后等待」调大（3 → 5 秒）。这一项要排在
换装之后才有效，插件已经按这个顺序做了。

**Q：导出按钮是灰的 / 提示找不到 VideoExport？**
A：没装 VideoExport，或者它没初始化完。先确认 `BepInEx/plugins/` 下有 `VideoExport.dll`。

**Q：泛光 / 暗角强度改了没反应？**
A：ReShade 只在**启动时**读一次预设。改 `KK_BloomVignette.ini` 之后要重启游戏。

**Q：成片里有别的插件的界面？**
A：导出时会自动藏掉纯 UI 组件（Canvas / 纯 IMGUI）。但界面和逻辑写在一起的插件藏不掉 ——
这类只能自己先关掉那个面板。

**Q：一张卡里有好几段 Timeline，能都录吗？**
A：不能。插件里只维护一份 Timeline 状态，一个场景卡录一条。

**Q：想改配置文件？**
A：`BepInEx/config/com.kkpeheightlock.cfg`，每一项都有注释。界面能调的都建议在界面调。

---

## English (short)

**Scene Card Fitting Toolkit** is a BepInEx plugin for Koikatsu Sunshine CharaStudio that
batch-renders scene cards to video: it loads each scene, swaps the female character card,
applies outfits, times the clip to the scene's Timeline, records via VideoExport, and
optionally merges all segments into one mp4. Bloom / vignette are done live in ReShade, so
the mp4 VideoExport writes is already final — no ffmpeg post-processing.

- Install: drop `KK_SceneCardFittingToolkit.dll` into `BepInEx/plugins/`, launch `CharaStudio.exe`.
- Requires BepInEx 5 (x64), VideoExport, KKAPI. ReShade + Timeline optional.
- Open the panel from the dark-red film icon in the bottom toolbar.
- UI language follows your system locale; you can force 中文 / English in the settings.

Full usage notes are in Chinese above; the UI itself is fully translated.

---

## 依赖与致谢

本插件是站在一堆别人的成果上做出来的。下面把用到的每一个外部项目列清楚 ——
**本仓库不包含、也不重新分发它们的任何二进制文件**，请自行从各自官方渠道安装。
它们的版权归各自作者，遵循各自许可证；本仓库的 [LICENSE](LICENSE) 只覆盖本仓库自身的代码与文档。

### 运行时依赖（必须自行安装）

| 项目 | 作者 / 来源 | 在本项目中的作用 | 引用方式 |
|---|---|---|---|
| **BepInEx 5** | BepInEx 团队 · <https://github.com/BepInEx/BepInEx> | 插件加载框架、配置系统、日志 | 编译期引用 `BepInEx.dll`，运行时依赖 |
| **HarmonyX** (`0Harmony`) | BepInEx 团队 · <https://github.com/BepInEx/HarmonyX> | 运行时方法补丁（身高锁、换人保留体型） | 编译期引用，运行时依赖 |
| **IllusionModdingAPI / KKSAPI** | IllusionMods · <https://github.com/IllusionMods/IllusionModdingAPI> | 工作室角色接口、`SceneLoad` 事件、工具栏按钮、角色卡读写 | 编译期引用 `KKSAPI.dll`，运行时依赖 |
| **VideoExport** | 社区插件（DeathWeasel / KK_Plugins 系列） | 真正执行录制、编码 mp4、帧数限长 | **纯反射调用**，未引用其程序集，未打包 |
| **Timeline** | 社区插件 | 提供时间轴播放 / 暂停 / 定位、读取片长 | **纯反射调用**，未打包 |
| **KKSPE / HSPE** | 社区插件（`HSPE.AMModules` 命名空间） | 身高骨骼 `cf_n_height` 与骨骼编辑器的类型引用 | 编译期引用 `KKSPE.dll`，运行时依赖 |
| **ReShade** | crosire · <https://reshade.me> | 实时后处理宿主（泛光 / 暗角跑在它的管线里） | 外部程序，用户自行安装 |

> 为什么不打包 VideoExport / Timeline：它们的版本更新频繁，跟着我打包会锁死版本、
> 也会给原作者添麻烦。请用你整合包里那份，或者去各自的官方发布页拿最新的。
> 上面几个社区插件的仓库地址随作者迁移会变（KK_Plugins 系列曾在 GitHub、现部分在
> gitgoon.dev），所以这里只写项目名、不写死链接，避免给错地方。

### 本项目自带

| 文件 | 说明 |
|---|---|
| `reshade/KK_BloomVignette.fx` | 泛光 + 暗角着色器，为本项目编写。参数与 ffmpeg 的 `vignette` / `colorlevels` + `boxblur` + `screen` 滤镜链对齐，方便和旧脚本产物直接对比 |

放到 `reshade-shaders/Shaders/` 下即可被 ReShade 加载。它依赖 ReShade 自带的
`ReShade.fxh` / `ReShadeUI.fxh`（BSD 3-Clause，随 ReShade 分发）。

### 技术思路上的参考

- 换人 / 换装 / 等待物理稳定的时序，参考了同项目早期 Python 自动化脚本
  （`python_tools/4_AutoSceneExport`）的实际行为，逐条对齐后才把「开头头发乱飞」修掉。
- 「头尾各多录 N 秒压黑场」的做法沿用旧脚本，只是改成屏上遮罩直出，不跑 ffmpeg 后期。

### 宿主程序

Koikatsu Sunshine / CharaStudio © Illusion。本项目与其开发商、发行商**没有任何隶属关系**，
也不包含游戏的任何资源文件。

---

## 许可 / License

**严格禁止商用。** 详见 [LICENSE](LICENSE)。

- ✅ 免费下载、免费使用、免费转发原版压缩包 / 原仓库链接
- ✅ 自己改着玩（改完只能自己用）
- ❌ 任何形式的售卖、付费下载、付费墙、会员专享、捆绑销售
- ❌ 上传到任何需要付费 / 看广告 / 攒积分才能下载的网盘或站点
- ❌ 拿去直播打赏、接单代做、当成商品的一部分

如果你是在付费渠道拿到这个插件的，你被骗了 —— 请退款并举报卖家。

---

## 源码

```
src/                插件源码（C#，BepInEx 5 / net46）
docs/DEVELOPMENT.md 开发者文档：构建方式、源码结构、导出时序、踩过的坑
reshade/            自带的泛光暗角着色器
CHANGELOG.md        更新日志
LICENSE             非商业许可协议
```

构建：`dotnet build -c Release`，产物 `bin/KK_SceneCardFittingToolkit.dll`。
