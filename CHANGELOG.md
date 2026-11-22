# 更新日志

版本号在 `src/Core/KKPEHeightLock.cs` 的 `Version` 常量。

---

## 2.3.1

### 清理：删除旧的逐骨骼碰撞器忽略代码（衣服吸附验证通过）

2.3.0 里保留做对照的 `TurnOffDynamicBoneColliders`（逐骨骼补忽略名单）已确认衣服/头发
吸附同样被 `Enable New Dynamic Bones` 修复解决，旧代码全部删除：

- `Export/AutoExport.Scene.cs`：`TurnOffDynamicBoneColliders()` /
  `CollectAccessoryObjects()` / `AddIfAny()` 及相关注释；
- `Export/ExportOptions.cs`：`LegacyColliderIgnore` 字段；
- `Core/KKPEHeightLock.cs`：配置 `ExportLegacyColliderIgnore`；
- `Export/ExportRequest.cs`：对应的取值赋值。

现在碰撞体这块只剩 `Game/ColliderBindingFix.cs` 一条路径（换人前后各关一次
`Enable New Dynamic Bones`），没有配置开关、默认总是执行。

### 修复：隐藏的 female 不再参与角色卡替换

之前场景里被眼睛图标隐藏的 female 也会被换卡，既浪费卡又把主卡/次卡的分配顺序挤乱。
现在 `ReplaceFemale` / `ApplyClothes` 只处理**显示中**的角色：

- 判据：`charInfo.visibleAll`（眼睛图标 `OnVisible(false)` 时置 false），
  辅以 `treeNodeObject.visible` 兜底；
- 隐藏角色被跳过，主卡/次卡只在显示中的角色之间分配；
- 日志会报「跳过 N 个隐藏角色」。

## 2.3.0

### 清理：只留真正有效的那一条修复

`Enable New Dynamic Bones` 实测有效，其余尝试（补忽略名单 / 清空动骨碰撞器 /
停用碰撞体对象 / 重写饰品位移缩放 / 冻结饰品动骨）全部**无效**，已删除：

| 删掉的东西 | 曾经的用途 |
|---|---|
| `Game/AccessoryDynamics.cs` | 关闭饰品动骨、清空 `m_Colliders`、诊断输出 |
| `Game/AccessoryMoves.cs` | 按卡片重写饰品位移/缩放（会让头发乱飞，已确认无效） |
| `Game/SceneColliders.cs` | 停用场景碰撞体对象 |
| 面板两个开关 | 「修复碰撞体误吸附饰品」「关掉饰品动骨」 |
| 配置 `FixColliderBinding` / `KillAccessoryDynamics` / `DiagnoseAccessoryDynamics` / `ApplyAccessoryMoves` | — |

新增 `Game/ColliderBindingFix.cs`，只做一件事：换人前（和换人后）把 KKPE 所有
`CollidersEditor._addNewDynamicBonesAsDefault` 置 false。**不再有开关，默认总是执行**，
日志仍是 `[碰撞体] 已关闭 N 个碰撞体的「Enable New Dynamic Bones」`。

### 衣服/头发吸附：旧代码已默认关闭，等实测

原来那套「逐骨骼给碰撞体补忽略名单」（`TurnOffDynamicBoneColliders`）是给衣服/头发写的，
现在默认**不执行**（`LegacyColliderIgnore = false`，代码保留、随时能开回来），
用来验证「新开关是不是把衣服吸附也一起解决了」：

- 如果衣服现在也正常 → 这套旧代码可以彻底删掉；
- 如果衣服仍会被吸 → 把 `LegacyColliderIgnore` 设 `true` 做对照，再决定怎么处理。

## 2.2.14

### 找到社区标准答案：KKPE 碰撞体的「Enable New Dynamic Bones」

hongcha 这类场景换人后头发被吸进脸/身体，**不是我们之前猜的任何一项**，而是：
KKPE 的每个 Dynamic Bone Collider 上有个开关 **「Enable New Dynamic Bones」**，
开着的时候，角色一换、**新出现的动骨会被自动登记进它的影响列表** —— 新卡的饰品头发
瞬间变成碰撞对象，就被"吸"进去了。

社区（F95zone / kknow，专门针对 hongcha 场景）给的解法原话：

> "it's due to the dynamic bone colliders. Select the bone collider under your character
> (usually under Part: Vagina), click on **Adv. Mode** in KKPE, and then just tick off
> **Enable New Dynamic Bones**. Now every time you switch the character it should be fine."

对应字段（用 Mono.Cecil 从 `KKSPE.dll` 读出来的）：

```
HSPE.AMModules.CollidersEditor
   F Boolean _addNewDynamicBonesAsDefault       ← 就是它
   F Boolean _addNewDynamicBonesAsDefaultSaved
   M Void SetIgnoreDynamicBone(...)
```

**实现**：`SceneColliders.DisableAutoAttachNewBones()` 反射遍历
`HSPE.PoseController._poseControllers` → 每个 `_collidersEditor` → 把
`_addNewDynamicBonesAsDefault` 置 `false`。调用时机两次：

1. **ChangeChara 之前**（关键：晚了新骨骼已经登记进去了）；
2. 换人 + 姿态应用之后（新角色可能带出新的编辑器）。

日志：`[碰撞体] 已关闭 N 个碰撞体的「Enable New Dynamic Bones」（换人后不再自动吸附新动骨）`。

跟 `FixColliderBinding` 开关一起走，默认开。之前那些办法（补忽略名单 / 清空 m_Colliders /
停用碰撞体对象）全部保留为兜底，但按社区经验，开关这一条才是根上的。

## 2.2.13

### 回滚：按卡片重写饰品位移/缩放（默认关）

#278 实测重写后头发乱飞、变光头 —— 读到的 addMove 不是这件饰品该用的值（
`nowCoordinate` 与实际渲染的坐标/配件不一定对齐），跟画面异常也无关。已改成
`ApplyAccessoryMoves = false`（默认关，只有明确需要时才开），不再调用。

### 新解法：直接清空「饰品动骨自己的碰撞器列表」

这才是「碰撞器错误绑定到饰品动骨」的正面解法，不依赖 KKPE 的忽略名单：

```
DynamicBone_Ver01.m_Colliders : List<...>
DynamicBone_Ver02.Colliders   : List<...>
```

对每个饰品动骨：读这个列表 → **Clear()** → 按版本重建粒子
（Ver01: `SetupParticles` / `InitTransforms` / `ResetParticlesPosition`；
Ver02: `InitLocalPosition` / `ResetParticlesPosition`）。

效果：碰撞器再也管不到饰品骨骼，但**动骨照常摆动**（不像 `enabled=false` 那样冻结）。
日志：`[饰品] 已解除 N 处碰撞器吸附（M 个饰品动骨，动骨本身保持启用）`。

这个开关跟着「修复碰撞体误吸附饰品」一起走，默认开。

## 2.2.12

### 修 2.2.11 反射读不到数据（现在用强类型接口）

用 Mono.Cecil 直接读 `Assembly-CSharp`，拿到确切结构：

```
ChaControl.nowCoordinate              : ChaFileCoordinate      ← 当前服装坐标
ChaFileCoordinate.accessory           : ChaFileAccessory
ChaFileAccessory.parts                : PartsInfo[]
ChaFileAccessory.PartsInfo.addMove    : Vector3[2, 3]
    [0,0]=位置 [0,1]=旋转 [0,2]=缩放     ← 当前值
    [1,0..2] = 原始值                    ← 「还原」用
```

2.2.11 里用 `chaFile.coordinate` + `addMove as Vector3[]` 去读 —— 前者在基类
`ChaFile` 上（`ChaFileControl` 自己只有两个字段），后者是 **二维** `Vector3[,]`，
所以一路返回 0，日志一条 `[饰品]` 都没有。现在改成直接用
`charInfo.nowCoordinate.accessory.parts[slot].addMove[0, 0..2]`，并 compare 后再写，
日志会打出每件饰品实际写入的值：

```
[饰品] 槽 01 ← pos(0,-4.2,2) rot(0,0,0) scl(1.28,1.28,1.31)
[饰品] 已按卡片重写 5 件饰品的位移/缩放
```

> 顺带留下一个可用的小工具：`temp/cecil/`（net46，引用 BepInEx 的 Mono.Cecil），
> 用 `./Dump.exe Assembly-CSharp.dll <类型名>` 就能列出任意游戏类型的字段/属性，
> 以后查游戏内部结构不用再猜。

## 2.2.11

### 真根因：KKS 换人后没把卡里的饰品 `addMove` 写上去

诊断数据（#278 + animfriend 07）：

```
[诊断] 01 ca_slot00 父级=cf_J_FaceUp_ty/a_n_headside/ca_slot00 动骨=26(启用0) 碰撞器=0 变换=(0,0,0)s(1,1,1)
```

而卡片里同一件饰品是：

```
01  addMove pos(0,-4.2,2.0)  rot(0,0,0)   scale(1.28,1.28,1.31)
02  addMove pos(0,-0.7,3.6)  rot(0,0,0)   scale(1.20,1.32,1.71)
03  addMove pos(0,-0.9,5.7)  rot(0,0,0)   scale(1.24,1.34,1.66)
04  addMove pos(0, 1.4,-3.3) rot(2,0,0)   scale(1.08,0.93,1.23)
```

也就是说：**饰品的位置/旋转/缩放根本没被应用**，全部堆在父节点 `a_n_headside` 原点 →
头发正好落在脸的坐标上。`UpdateAccessoryMoveAllFromInfo()` 在这张卡上救不回来。

新增 `src/Game/AccessoryMoves.cs`：直接读 `ChaFileControl.coordinate[i].accessory.parts[slot].addMove`
（反射，兼容 `Vector3[]` 与「结构体里 3 个 Vector3」两种排布），
把 pos/rot/scl 写到 `ca_slotNN` 的 local 变换上。换人后写一次，开录前再写一次。

日志：

```
[饰品] 槽 01 ← pos(0,-4.2,2) rot(0,0,0) scl(1.28,1.28,1.31)  [坐标 0]
[饰品] 已按卡片重写 5 件饰品的位移/缩放
```

### 2.2.10 补充

- 新增只读诊断 `[诊断]`：每件饰品的父级路径 / 动骨数 / 启用数 / 最大权重 / 碰撞器数 / 当前变换，
  `DiagnoseAccessoryDynamics` 默认开（排查完会关掉）。
- 结论：动骨停用（启用 0）且饰品无任何碰撞器时画面依旧异常 → 与物理无关。

## 2.2.9

### 正解：修「碰撞体误吸附饰品动骨」（不是删动骨）

**根因**：`TurnOffDynamicBoneColliders()` 收集要登记的骨骼时用的是
`ci.objAccessory` —— 这个数组在 KKS 里经常是 null（或只含当前可见槽），
于是**饰品骨骼一条都没进忽略名单**，名单里只有衣服和头发，碰撞器就一直抓着饰品头发链。

**修复**：

- 新增 `AccessoryDynamics.CollectObjects(ChaControl, List<GameObject>)`：
  按 `GetAccessoryComponent(slot)`（0–19）+ `ca_slot*` 名字兜底，把所有饰品根节点收全；
- 骨骼收集改成「三个类型都收」：`DynamicBone`、`DynamicBone_Ver02`，再加**按类型名兜底**
  （任何含 `DynamicBone` 且不含 `Collider` 的 `Behaviour`，类型不匹配时 Invoke 抛异常已被吞掉）；
- 每个角色会打一条日志：`[碰撞体] 角色动骨 N 个（饰品对象 X 个 / 头发 Y / 衣服 Z）`，
  可以直接确认饰品到底有没有被探测到。

### 开关拆分（不再"关了一个另一个还在改场景"）

| 开关 | 配置项 | 默认 | 作用 |
|---|---|---|---|
| 修复碰撞体误吸附饰品（推荐） | `FixColliderBinding` | **开** | 只补「忽略动骨」名单 + 停用补不上的碰撞体，**不动动骨** |
| 关掉饰品动骨 | `KillAccessoryDynamics` | **关** | 硬手段：把饰品动骨 `enabled=false`，冻结成绑定姿势 |

之前这两个行为是绑在一起的，所以「关掉饰品动骨」之后场景照样被动过。
现在各自独立，两个都关 = 纯换人、不改任何物理设置。

---

## 2.2.8

### 停用「碰撞体对象」本体（等价于手动删掉它）

实测数据：2.2.6/2.2.7 里开录前日志已经是
`[检查] 开录前仍启用的饰品动骨 0 个`、`饰品变换 01..05 全是 (0,0,0)@(0,0,0)`，
但成片头发依旧不对；而手动在 Studio 里删掉 `[J694] Dynamic Bone Collider` 就正常。

说明问题不在「动骨组件」也不在「饰品变换」，而在**那个碰撞体对象本身**
（它没有 `DynamicBoneCollider` 组件 —— J694 插件已经不在本机，所以按组件找是 0 个）。

现在 `SceneColliders.Suppress()` 增加第二遍：按**名字**找
`Dynamic Bone Collider` / `DynamicBoneCollider` / `ダイナミックボーンコライダー`
这类对象，直接 `SetActive(false)`，导出结束 `Restore()` 原样打开。
日志：`[碰撞体] 已停用 N 个碰撞体对象（对象名…）`。

### 2.2.7 补充（同批）

- 换人后调用 `charInfo.UpdateAccessoryMoveAllFromInfo()`，按卡片重算饰品变换；
- 开录前再算一次 + 打印前 5 个饰品槽的 localPosition/localEulerAngles（`[检查] 饰品变换 #1：…`），
  方便对比“重算前 / 重算后”。

---

## 2.2.6

### 修：2.2.5 的「关饰品动骨」在应用姿态后失效

实测导出 #278 的成片，头发还是被拉进脸。查下来是执行时机问题：
`应用时间轴姿态` 那一步会让游戏把饰品动骨**重新挂上**，2.2.5 只在「换人后 / 换装后」关过两次，
等到真正开录时又活了。

现在关 **三次**，并且最后一道就在开录前：

1. 替换 female 收尾；
2. 换装收尾（切套装会重建饰品）；
3. 应用姿态 + 关碰撞体之后、`CaptureFrames` 之前（新增，带诊断日志）。

开录前那句日志能直接看出有没有残留：
`[检查] 开录前仍启用的饰品动骨 N 个 → 再关 M 个`。

### 新增：硬关场景里的独立动骨碰撞体

`[J694] Dynamic Bone Collider` 那类对象，原来的做法（KKPE 里逐个补 `ignoredDynamicBone`）
对手写路径列表根本补不干净 —— 只要有一根骨头没覆盖到，头发照样被吸。

新增 `src/Game/SceneColliders.cs`：导出时把所有**不属于角色**的 `DynamicBoneCollider\*`
组件直接 `enabled=false`（角色自己的衣服/头发/身体碰撞器不动），导出结束在
`ExitCinemaMode` 里原样还原。日志：`[碰撞体] 已硬关 N 个独立动骨碰撞体（对象名…）`。

> 软（补忽略条目）+ 硬（关碰撞体）+ 关动骨，三层一起上；哪一层生效都不会再出现
> “头发被吸进脸”。

---

## 2.2.5

### 新增：换人后自动关闭「饰品动骨」（默认开）

**问题**：从场景卡换上一张新角色卡后，01/02/03 这类**头发类饰品**（和部分耳朵饰品）
会被拉进脸里、拽变形。原因是饰品 prefab 自带的 DynamicBone：

- 换人时 `ChangeChara` 会重建整套饰品对象，动骨的骨骼引用/碰撞器按旧角色重建；
- 场景卡里往往还存着一份**按骨骼全路径写的「忽略动骨」名单**
  （例如 `[J694] Dynamic Bone Collider` 对象，由 KKSPE 的 `itemInfo` 保存，
  里面写着 `.../ca_slot00/.../_yure_hair_h50_01` 这种条目）；
- 新卡换上来的饰品骨链**名字/数量都不一样**，一条都不在这份名单里 →
  原本被豁免的链全速模拟 → 头发往脸里收。

删 J694 插件没用：名单不在插件里，在**场景对象**里。

**处理**：新增配置 `[AutoExport] KillAccessoryDynamics`（默认 `true`），
面板上对应「关掉饰品动骨（防头发被拉进脸）」勾选框。

- 替换 female 之后关一次；
- 如果导出时还切换了服装预设（饰品会整套重建、动骨复活），换装之后再关一次。
- 只动 `ca_slotNN` 子树里的 `DynamicBone*` 组件（`DynamicBoneCollider` 保留），
  本体头发 / 裙子 / 胸部动骨完全不动。
- 关掉后饰品保持绑定姿势：稳定、可复现，批量导出不会因为「这张卡动骨崩了」废片。

新增 `src/Game/AccessoryDynamics.cs`（枚举 `GetAccessoryComponent(slot)` + 按 `ca_slot*` 名字兜底扫描）。

> 只想修某张卡、不导出时：Maker/Studio 里用 Dynamic Bone Editor 把该槽 Weight 设 0，
> 或打开饰品的「不摇动」（卡里的 `noShake`），存盘后对所有场景生效。

---

## 2.2.4

### 文件夹手势改成「Ctrl + 左键」（左键不再被右键行为抢）

- 之前是右键点文件夹 = 整个目录一起勾选。但 Unity IMGUI 的 `GUI.Button` 对右键也会响应，
  于是「右键全选」同时触发了「进入文件夹」——两个动作打架。
- 现在：**左键 = 进目录；Ctrl + 左键 = 只切整个目录的勾选，不进去**；右键不再做任何事。
  文件夹格子改成自己判 `Event.current`（`MouseDown` + `button == 0` + `control`），
  不再用 `GUI.Button` 的返回值，所以不会再出现「点一下就进目录」的意外。
- 角色卡选择器同步：**Ctrl + 左键文件夹 = 整目录加入次卡**（原来也是右键）。

### 文件夹上显示「里面有选中的卡」

- 目录里只要有卡片被勾选，就给文件夹图标描一圈框：**全选 = 绿框，部分选中 = 橙框**。
- 角色卡选择器同理：目录里已经有卡加入次卡时描框，副标题从 `+N` 变成 `已加入/总数`
  （例如 `3/12 张`）。统计结果按「目录树版本 + 次卡数量」缓存，不会每帧递归整棵树。

### 关于「点导出时 ReShade 预设刷新」

根因不是本插件写 ReShade 配置（插件对预设**只读**），而是：

1. 导出第一步要加载场景卡 → CharaStudio 在场景加载时会重建交换链
   （日志实测：`Redirecting IDXGISwapChain::ResizeBuffers(Width=2560, Height=1440)`）；
2. ReShade 收到 resize 就重建整个 effect runtime → **把 151 个效果全部重新编译**
   （日志实测：导出那一分钟出现 302 条 `Successfully compiled`）+ 重新应用预设；
3. `PresetTransitionDuration=1000` 让这个过程变成「1 秒的渐变」，看起来就是预设被刷新、画面不稳。

已在本机 `ReShade.ini` 调整（不属于插件改动，但配套写进文档）：

```ini
PresetTransitionDuration=0        ; 不再做 1 秒过渡，重建瞬间完成、不闪
SkipLoadingDisabledEffects=1      ; 只编译预设里启用的效果（3~10 个），别每次编译 151 个
```

---

## 2.2.3

### 大图标再放大一档

- 缩略图 96 → **132**，tile 116×142 → **152×196**（默认 1380 宽的面板一屏 4 列、4 行）。
  名字字号 12 → 13。

### 文件夹：左键进目录，右键整目录勾选

- 场景卡列表里文件夹图标上的小方框去掉了 —— **左键只负责进去**，
  不再有「点歪一点就变成勾选」的问题。
- **右键文件夹 = 整个目录一起勾选 / 取消**（已经全选时右键取消）。文件夹下面那行
  `5/12 张` 就是勾选进度，全选时变绿。
- 角色卡选择器同理：**右键文件夹 = 整目录加入次卡**（原来右上角那个「+N」小按钮去掉，
  它压在图标上不好点）。两个界面的手势统一成「左键进入 / 右键整目录」。

### ReShade 预设相关

- 导出前的预设检查不再「没启用 KK_BloomVignette 就报警」：
  现在只报「当前预设叫什么、启用了几个效果、分别是哪些」，用了自己的预设也不会被刷警告。

---

## 2.2.2

### 改成大图标视图（场景卡列表 + 角色卡选择器）

- 两处都换成类似 Windows 资源管理器的大图标网格：**文件夹图标 + 卡片缩略图**，
  一个目录一屏，点文件夹进去、点「← 上级」回来，顶部路径栏显示当前位置。
- **左栏场景卡的「预览 + 时长说明」那一整块去掉了** —— 大图标本身就是预览，
  详细信息收到底部一行状态文本（名字 / 大小 / Timeline 秒数），又省了 130 多像素高度。
- 勾选还是照旧：点卡片 = 选中并勾选，左上角小方框单独切勾选；文件夹图标左上角的方框
  = 整个目录一起勾（半选时显示 ■）。
- 角色卡选择器多选模式：点卡片加入/移出（左上角数字 = 替换顺序），
  文件夹图标右上角的「+N」= 整目录加入。
- 当前目录 / 滚动位置 / 上次点开的卡继续写进 cfg（`TreeDir` / `TreeScroll` / `TreeSelected`）。
- 新增 `ThumbCache`：缩略图加载后先缩到长边 192 再进 LRU 缓存（上限 400 张），
  每帧只补 3 张 —— 否则几千张场景卡（每张 1280x720）能把显存吃穿、开面板卡几秒。

### 次角色卡：替换顺序改成显式开关

- 原来是藏在「次角色卡」标题右边的一个小下拉框，很容易看漏（反馈说「没有切换」）。
  现在是「替换顺序」后面的两个分段按钮：**数字优先 / 随机**，当前生效的那个刷成绿色，
  下面还有一行说明当前是哪种行为。

### 交付目录

- 仓库新增 `output/`：编译好的 dll + VideoExport（含 ffmpeg）+ KKSAPI / KKSPE / Timeline
  + 改过的 `KKReshade.addon` + `KK_BloomVignette.fx` + 安装说明，按游戏目录结构摆好，
  整个覆盖进游戏根目录就能用。二进制不进 git（`output/.gitignore`），发版打 zip 传 Releases。

### 修复

- **大图标网格里点文件夹会报 `ArgumentOutOfRangeException`**：点击时当帧就换了目录、
  列表长度变了，而循环还按旧的行数在走。现在切换目录延后到这一轮画完再执行，
  卡片下标也加了兜底。

---

## 2.2.1

### 重构（前后端分离 + 按模块分目录）

```
src/Core/     插件壳、配置、本地化、路径修复、自然排序
src/Game/     与游戏/工作室打交道：场景播放器、替换、场景工具、Timeline 读取
src/Export/   批量导出后端：录制流水线、参数组装、场景目录、角色卡池、角色卡库
src/Compat/   外部插件对接：KKPE 身高锁 / 换人保留体型 的 Harmony 补丁
src/UI/       纯前端：窗口、控件、主题、各种面板与弹框
```

- **后端不再住在 UI 里**。以前「扫描了哪些场景卡」「勾了几张」「次角色卡是哪几张」
  「导出参数怎么组装」全写在 `AutoExportPanel` 这个 UI partial 里，现在拆成四个后端类：
  - `SceneCatalog`：场景目录扫描 / 过滤 / 勾选计数（原 `UI/SceneLibrary.cs`）；
  - `CardPool`：主角色卡 + 次角色卡列表、编号、整目录导入；
  - `CardLibrary`：角色卡目录树（新，选卡弹框的数据源）；
  - `ExportRequest`：按配置组 `ExportOptions` 并开跑（原 `StartExport` 里的那坨）；
  - 另加 `CardImages`（PNG 截到 IEND 再解码）和 `ClothesPartSettings`（部件状态缓存）。
- 三份一模一样的「自然排序」合并成 `Core/NaturalSort.cs`。
- `csproj` 改成 `src\**\*.cs` 递归收，新加文件不用改工程。

### 重写选卡弹框（角色卡目录树）

- 以前的弹框只列**当前目录**的卡，子文件夹要靠另一个「选择子文件夹」弹框钻进去，
  实际用起来进不去 —— 打开只能看到根目录那几张卡。
- 现在照左栏场景卡列表的做法：整棵目录树一次扫出来，一个列表里画
  「文件夹行（▶/▼ + 卡片数）+ 卡片行（缩略图 + 角色名 + 文件名/子目录）」，
  文件夹点箭头或名字原地展开；多选模式下文件夹行右边还有「整目录加入」。
- 卡片左上角的数字角标 = 次角色卡替换顺序，选中描亮边。
- 顶部有「全部展开 / 全部收起 / 刷新 / 搜索」，行按绝对坐标画，滚动位置不会和行错位。

### 修复

- **角色卡只扫进来一半**（v2.2.0 遗留）：KKS 的 `UserData.Path` 带 `../`，
  而 `Directory.GetFiles` 返回规范化路径，两者长度不同，用 `root.Length` 去 Substring
  直接越界 —— 表现为扫到几十张就中断（52 张只进来 39 张）。
  现在根目录先 `GetFullPath`，相对路径计算也加了分隔符/大小写不敏感的兜底，
  单张卡出错只跳过它自己，不再整次扫描中断。
- **暂停时删中间目录会和 VideoExport 抢文件**：以前暂停后立刻删 `segments/s001_xxx`，
  VideoExport 收尾时找不到目录，日志一串 `DirectoryNotFound` / `Video file not found for muxing`。
  现在不删，重录时换一个新目录（`_r2`、`_r3`…），成片名不变。

---

## 2.2.0

### 新增

- **主角色卡 / 次角色卡**：右栏角色卡区拆成两块。
  - **主角色卡**（单选）：只换场景里**第一个** female；不选就跳过它，全部交给次卡。
  - **次角色卡**（多选）：弹框里点「加入」即可，缩略图左上角写着编号（1、2、3…），
    编号就是替换顺序；也可以点「文件夹…」把整个角色卡文件夹一次性加进来。
  - **顺序**可选「数字优先」或「随机」。主卡为空时，第一个 female 也从次卡里取。
  - 没有次卡时行为退回老版本：主卡换掉所有 female。
- **服装预设 → 随机**：`ExportOutfit = -2`。每个 female 从**自己卡片里真的有衣服**的
  套装槽位里随机挑一套（空槽不参与，避免切到空槽变成裸体）。
  主窗口「服装预设」下面也加了一个「随机服装」按钮，直接给当前选中角色换一套。
- **ESC 暂停导出**：导出中按 ESC 暂停，再按一次继续；运行中界面也有「暂停（ESC）」按钮。
  暂停时不再藏界面（ReShade 通路也露出来），可以点「停止」。
  正在录的那张卡会被丢弃并**重录**（ffmpeg 管道已开，半截视频接不回去），
  已经录好的片段全部保留。
- **场景卡列表记住上次的位置**：折叠起来的目录、滚动位置、上次点开预览的卡
  都写进 cfg（`TreeCollapsed` / `TreeScroll` / `TreeSelected`），
  关面板、重启游戏后接着看。

### 修复

- **点插件界面会穿透到后面的工作室界面**：插件窗口是 IMGUI，工作室是 uGUI，
  两套各点各的 —— 点场景卡的 ▼/▶ 折叠箭头时，鼠标会同时点到面板背后那个
  「保存」按钮，顺手就把场景存了。
  现在在本插件窗口下面铺了一层几乎全透明、但吃射线的 uGUI 图片
  （`UiClickBlock`，排序层 32000），只覆盖窗口真正占的地方，面板外的点击照常可用。
- **点折叠箭头时左栏整块报错**（v2.2.0 冒烟测试抓到）：
  折叠/展开会在这一帧当场重建行列表，行数变少后循环还按旧的末行继续走 →
  `ArgumentOutOfRangeException`，异常一抛左栏就画不全（点击也就更容易掉到后面的界面）。
  循环加了行数上界，卡片行也补了下标兜底。

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
