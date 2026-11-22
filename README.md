[简体中文](README.md) | [English](README_EN.md)

# Scene Card Fitting Toolkit · 场景卡套档工具箱

Koikatsu Sunshine（KKS）CharaStudio 的插件。

场景卡自动录成视频：自动加载场景、换角色卡、换服装、按 Timeline 定时长、
开 ReShade 泛光暗角、录完自动合并。
---
| 功能 | 说明 |
|---|---|
| **批量录视频** | 勾选任意多张场景卡 → 自动录成 mp4，可选合并成一个长片 |
| **大图标场景列表** | 场景卡列表和角色卡选择器都是资源管理器式的大图标网格：**左键点文件夹进去、右键整目录勾选/加入**，卡片直接看 132px 缩略图 |
| **换角色卡** | 主角色卡只换第一个 female，其余按**次角色卡**（多选 / 整个文件夹，数字优先或随机）替换；体型 / 乳量 / 骨架 / 身高可分别选择「跟场景」还是「跟卡」 |
| **关掉饰品动骨** | 换人后自动把饰品的动态骨骼关掉（只饰品，不动头发/裙子/身体），防止新卡的发片被场景里那套「按骨骼路径写的忽略名单 + 动骨碰撞器」拉进脸里 |
| **换服装** | 导出前切服装预设（含**随机**：从角色卡自己有的套装里挑）+ 逐个部件设「穿 / 半脱 / 脱」 |
| **定时长** | 卡里有 Timeline 就按 Timeline 长度录；没有或太短就按你设的下限循环补齐 |
| **淡入淡出** | 多录 N 秒，屏上压黑场 |
| **导出中暂停** | 按 **ESC** 暂停 / 继续（暂停后可点停止），已录好的片段都保留 |
| **去码 / 去球** | 自动移除马赛克材质、隐藏灯光阴影辅助球 |
| **ReShade 实时后期** | 泛光 + 暗角在 GPU 上实时演算|
| **场景播放器** | 主窗口里直接上/下一张场景卡、随机场景、随机换人 |
## 代码结构

按「一个模块一个文件夹」拆开，界面和逻辑分开：

| 目录 | 职责 |
|---|---|
| `src/Core/` | 插件壳（配置入口 / Update / OnGUI 分发）、本地化、路径修复、自然排序 |
| `src/Game/` | 与游戏交互的后端：场景播放器、角色替换、去码去球、Timeline 读取 |
| `src/Export/` | 批量导出后端：录制流水线、导出参数组装、场景目录、角色卡池、角色卡目录树 |
| `src/Compat/` | 外部插件对接：KKPE 身高锁、换人保留体型的 Harmony 补丁 |
| `src/UI/` | 纯前端：窗口、控件、主题、大图标网格、各面板与弹框（只负责画和收事件） |

界面要的数据一律问后端（`SceneCatalog` / `CardPool` / `CardLibrary` / `AutoExport`），
界面自己不再持有业务状态。

## 交付目录 `output/`

`output/` 里是按游戏目录结构摆好的成品：插件 dll + VideoExport（含 ffmpeg）+ KKSAPI / KKSPE / Timeline
+ 改过的 `KKReshade.addon` + `KK_BloomVignette.fx` + 安装说明，整个覆盖进游戏根目录即可。
二进制不进 git（`output/.gitignore`），要发版就把整个目录打 zip 传到 GitHub Releases。

---

## 安装

1. 游戏需要已经装好KKS 的 64 位版本。
2. 把 `KK_SceneCardFittingToolkit.dll` 放进 `BepInEx/plugins/`。
3. 启动 **CharaStudio.exe**（不是游戏本体）。

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
2. 左栏是大图标的场景卡列表：**左键点文件夹进目录，右键点文件夹 = 整个目录勾选**；点卡片 = 选中并勾选（左上角小方框单独切勾选）。
3. 右栏选**主角色卡**（只换第一个 female，可不选）+ **次角色卡**（可多选 / 选文件夹，设好顺序），
   再设时长、帧率、服装。
4. 底栏确认输出目录 → 点 **开始导出**。导出中按 **ESC** 暂停 / 继续。
---
## 常见问题：换人后饰品头发穿脸

典型症状：换上某张卡后，01/02/03 这种**头发类饰品**糊在脸上、耳朵饰品被拽歪，
换回别的卡就正常。

原因不是 CA 数据、也不是缺 mod，而是**饰品自带 DynamicBone**：

1. 换人时饰品对象整套重建，动骨的骨骼引用按新角色重来；
2. 场景卡里常常还存着一份**按骨骼全路径**写的「忽略动骨」名单
   （比如 `[J694] Dynamic Bone Collider`，由 KKSPE 的 `itemInfo` 保存，
   条目长这样：`.../a_n_headside/ca_slot00/.../_yure_hair_h50_01`）；
3. 新卡的饰品 mod 不一样了，骨链名字对不上 → 一条都不在名单里 → 全速模拟 → 头发被收进脸。

本插件默认会在**换人后**和**换装后**各关一次饰品动骨（面板开关「关掉饰品动骨」）。
只想修单张卡时，也可以在 Maker 里把该槽的 Weight 设 0 或勾「不摇动」再存盘。

## ReShade 预设

预设和着色器分开放：

```
reshade_ini/          ← 所有预设（ReShade.ini 的 PresetPath 指向这里）
reshade-shaders/Shaders   ← 着色器 .fx
reshade-shaders/Textures  ← LUT / 噪点贴图
```

预设里 `Techniques=xxx@YYY.fx` 引用的 `.fx` 少一个，ReShade 就报「缺失必要文件」。
体检/修复脚本：`python_tools/7_reshade预设/check_presets.py`（`--trim` 会把缺的 technique
剔掉并备份）。详细说明见 `reshade_ini/说明.md`。

## 常见问题

**Q：录出来的开头头发往上飞 / 裙子炸开？**
A：换人后动态骨骼需要时间收敛。把「换人后等待」调大（3 → 5 秒）。

**Q：导出按钮是灰的 / 提示找不到 VideoExport？**
A：没装 VideoExport，或者它没初始化完。先确认 `BepInEx/plugins/` 下有 `VideoExport.dll`。

**Q：成片里有别的插件的界面？**
A：导出时会自动藏掉纯 UI 组件（Canvas / 纯 IMGUI）。但界面和逻辑写在一起的插件藏不掉 ——
这类只能自己先关掉那个面板。

**Q：一张卡里有好几段 Timeline，能都录吗？**
A：不能。插件里只维护一份 Timeline 状态，一个场景卡录一条。

**Q：想改泛光 / 暗角的强度？**
A：按 `Home` 打开 ReShade 面板直接拖滑块，`[OVERLAY] AutoSavePreset=1` 会自动存回预设。
本插件对 ReShade **只读不写** —— 导出时既不会覆盖你的参数，也不会替你开关效果；
日志里会打印当前参数和 technique 是否勾选，方便对片。
如果你把 `AutoSavePreset` 关了，改完记得在 ReShade 面板里按一下保存，否则导出时
ReShade 重建渲染环境会按文件里的旧值来。

**Q：想改配置文件？**
A：`BepInEx/config/com.kkpeheightlock.cfg`，每一项都有注释。界面能调的都建议在界面调。

---

## 依赖与致谢

本插件是站在一堆别人的成果上做出来的。下面把用到的每一个外部项目列清楚 ——
**本仓库不包含、也不重新分发它们的任何二进制文件**，请自行从各自官方渠道安装。
它们的版权归各自作者，遵循各自许可证；本仓库的 [LICENSE](LICENSE) 只覆盖本仓库自身的代码与文档。

### 运行时依赖（必须自行安装）

| 项目 | 作者 / 来源 | 在本项目中的作用 |
|---|---|---|
| **BepInEx 5** | BepInEx 团队 · [GitHub](https://github.com/BepInEx/BepInEx) | 插件加载框架、配置系统、日志 |
| **HarmonyX** (`0Harmony`) | BepInEx 团队 · [GitHub](https://github.com/BepInEx/HarmonyX) | 运行时方法补丁（身高锁、换人保留体型） |
| **IllusionModdingAPI / KKSAPI** | IllusionMods · [GitHub](https://github.com/IllusionMods/IllusionModdingAPI) | 工作室角色接口、`SceneLoad` 事件、工具栏按钮、角色卡读写 |
| **VideoExport** | 社区插件（DeathWeasel / KK_Plugins 系列） | 真正执行录制、编码 MP4、帧数限长 |
| **Timeline** | 社区插件 | 提供时间轴播放、暂停、定位、读取片长 |
| **KKSPE / HSPE** | 社区插件（`HSPE.AMModules` 命名空间） | 身高骨骼 `cf_n_height` 与骨骼编辑器的类型引用 |
| **ReShade** | crosire · [官网](https://reshade.me) | 实时后处理宿主（泛光 / 暗角运行在它的渲染管线中） |

### 宿主程序

Koikatsu Sunshine / CharaStudio © Illusion。本项目与其开发商、发行商**没有任何隶属关系**，也不包含游戏的任何资源文件。

## 许可 / License

**严格禁止商用。** 详见 [LICENSE](LICENSE)。

- ✅ 免费下载、免费使用、免费转发原版压缩包 / 原仓库链接
- ✅ 自己改着玩（改完只能自己用）
- ❌ 任何形式的售卖、付费下载、付费墙、会员专享、捆绑销售
- ❌ 上传到任何需要付费 / 看广告 / 攒积分才能下载的网盘或站点
- ❌ 拿去直播打赏、接单代做、当成商品的一部分

如果你是在付费渠道拿到这个插件的，你被骗了 —— 请退款并举报卖家。

---

