# 更新日志

版本号在 `src/Core/KKPEHeightLock.cs` 的 `Version` 常量。

> **版本编号勘误（2026-11-24）**
>
> 历史记录里 2.4.1 ~ 2.6.8 这批编号（共 20 条，含重复的 2.6.8）**全部作废**：它们是 2.4.0
> 开发过程中临时递增的测试编号，从未作为独立版本发布，改动最终都并入了 2.4.0。
>
> 正式序列：… → 2.3.2 → **2.4.0** → **2.4.1（当前）**。
>
> 作废条目已归到文末「历史留档」，只用于查当时的实现细节，不要再引用其编号。

---

## 2.4.1

### 修复：全局皮肤误把连裤袜网格（cf_m_bodyteights）当成身体材质

`SkinBase()` 原来用 `StartsWith("cf_m_body")`，`cf_m_bodyteights`（nashi Bodytights 那套的连裤袜网格）
也会命中，被一起换成 `KKLT/Skin`，表现为**鞋子/袜子压到身体上，把鞋子槽关掉就"正常"**（其实只是那块
网格不再画，身体材质那侧还是错的）。

现在改成按 `cf_m_` 后面那个**英文词**判断，且词后只能跟数字 / 下划线：

- 算：`cf_m_body`、`cf_m_body_00`、`cf_m_body00`、`cf_m_face_00`
- 不算：`cf_m_bodyteights`、`cf_m_body_teights`、`cf_m_facemask`

缓存版本 12 → 13：旧缓存里 `cf_m_body` 那条可能就是连裤袜的记录，需重建。

### 调整：全局皮肤改成「只应用作者编辑记录」+ 补齐贴图平铺

之前的做法是把模板材质的**全部** shader 属性刷过去（584 个 float），连作者没碰过的
`_AlphaMaskMode`、`_Cutoff`、混合模式也一起套，但模板没有配套 AlphaMask 贴图，身体被错误镂空（破损）。

现在改成和 ShaderManagementTool 的预设一致的思路：

- 从 MaterialEditor 的 `CopyData`（作者实际编辑记录）构造快照，只写记录过的
  shader / float / color / 贴图，不再全量覆盖；
- 贴图字节从 `CopyData.MaterialTexturePropertyList` 取（`mat.GetTexture` 拿不到的运行时贴图也能拿到）；
- 贴图的 **offset / scale** 单独记录并应用 —— `normalMapDetail` 的 50×50 平铺属于这类值；
- 缓存版本 11，旧缓存自动重建。

### 修复：数字输入框打不进小数点

`FloatField` 每帧都用配置值重建文本，输 `0.` 时 `TryParse` 得到 0 又刷回 `0`，小数点被吃掉，
「头尾各多录」想填 0.5 秒只能得到 5。现在编辑期间保留用户原文、失焦才回读配置，
且以小数点结尾时暂不提交 —— 0.5 / 0.25 都能正常输入。

### 调整：全局皮肤暂时一张贴图都不复制

上一版只留 `MatCapTex` / `NormalMapDetail`，上半身仍然破洞，索性全部关掉：
`CopyTextures = false`，白色名单只在恢复时用。缓存版本 3 → 4，旧缓存自动失效重建。
提取日志会列出被跳过的贴图属性名，应用日志会列出换过的 shader（`旧 → 新`），便于定位。

### 修复：角色光照钳制被 Reflect() 盖回去

`LightCalc.Reflect()` 是按内部值重刷灯的，把钳制结果覆盖掉——日志出现过
「1 → 1（上限 0.9，已下调）」。现在 `Reflect()` 之后再直接压一次，日志按**最终值**输出，
压不住时明写「压制失败」。

### 调整：全局皮肤只复制 MatCapTex 与 NormalMapDetail

跨角色复制 `MainTex` / 遮罩类贴图会错位——它们是按角色自己的 UV 和体型画的，
换到别的角色身上就是胸腔消失、花纹串色、贴图糊。现在皮肤缓存只保留
`MatCapTex`（视角空间反射）和 `NormalMapDetail`（平铺细节法线）这两张与 UV 无关的贴图，
其余贴图一律不复制；颜色、Float/Range、Vector 参数照旧。
缓存格式版本 2 → 3，旧缓存自动失效并重新提取。

### 调整：ShadowPower 钳制只作用于 KKUSSitem 材质

2.4.0 的过曝抑制会遍历角色身上的**所有**材质，连皮肤一起抬 ShadowPower，
副作用是改掉作者调的明暗（肤色发暗、发灰、对比变硬）。现在只处理 shader 名含
`KKUSSitem` 的材质（衣物/饰品），皮肤与脸完全不碰。配置项 `MinShadowPower`
（默认 0.85，0 = 不处理），日志会打印扫到的材质数、抬回数量与命中的 shader 名。

### 调整：角色光照上限只在应用全局皮肤时生效

`ClampCharacterLighting` 原本无条件把角色灯压到 0.9，会连带压暗场景原本的布光。
现在只有配置了「全局 female 皮肤卡」时才执行（材质被换过才需要防过曝），
否则保留场景原亮度。配置项 `ClampCharLight`（默认 0.9，0 = 不处理）。

### 代码整理（无功能改动）

- **精简注释**：全仓注释占比 15% → 10.4%，导出流水线（`AutoExport*.cs`）21~23% → 12%。
  统一成「只写做什么」，删掉失败过程与版本对比的叙述，保留会防坑的关键约束
  （动骨自动吸附、ChangeChara 分帧 Reload、ffmpeg 定稿判据等）。
- **清理无用代码**（全仓引用为 0，编译 0 错误）：
  - `ScenePlayerModule`：`ListSubdirs` / `SetSceneSubdir` / `GoParentDir`，
    字段 `_editChar` / `_fssMode` / `_timeScale`；
  - `ScenePlayerModule.Random`：`SetRandomFemaleSubdir` / `SetRandomMaleSubdir` /
    `ListRandomFemaleSubdirs` / `ListRandomMaleSubdirs`（子目录选择已统一走 `ApplySubdir` / `ListSubdirsAt`）；
  - `ScenePlayerModule.Edit`：`SetEditChar` / `SetClothesAll` / `ToggleKinematic`；
  - `ScenePlayerModule.Fss` / `.Timeline`：`FssCount` / `FssMode` / `TimelineSetTimeScale` / `TimelineTimeScale`；
  - `TimelineReader`：`ClearCache` / `Info.Describe`；
  - `UiTheme`：`ThemeSection` / `ThemeRow` / `ThemeRowOn` / `ThemeRowFolder` / `ThemeArrow`
    及底层 `_row` / `_rowOn` / `_rowFolder` / `_head`；
  - `SceneTools`：`GetColorAny` / `SetColorAny` / `AddColorNames` / `IsGenitalRenderer`；
  - `SceneCatalog`：`Roots` / `RootName`；`SkinMaterialCache.Invalidate`；
    `ConfigPanel.Truncate`；`PanelWindow.Show`；`TileGrid.CornerButtonRect`。
- `CollectFemales()` / `ApplyClothes()` 去掉未使用的 `revealed` 参数。

### 编译

`dotnet build -c Release` → **0 错误**（仅 3 条既有 MSB3274/MSB3275 警告：
`VRGIN_OpenXR` / `KKS_CharaStudioVR` 目标框架 4.6.2 高于项目的 4.6）。

---

## 2.4.0

> 正式版。开发期那批测试编号（2.4.1 ~ 2.6.8，已作废）的改动全部并入本版，
> 包括全局皮肤、角色光照上限 0.9、暂停/停止语义、批量导出 OOM 修复等；细节见文末留档。

### 修正：Timeline 里「会取消隐藏」的隐藏角色，按前后关系用上一个显示角色的卡

描述按实际语义重写：如果 Timeline 里有某个**隐藏角色**的动作帧，并且这些帧会把它
**取消隐藏**（播放中会显示出来），那它和现有角色卡（上一个显示的角色）是**前后关系**
—— 同一个人换了一套造型。这种角色必须替换成**现有角色卡（上一个显示角色的那张卡）**，
不额外消耗卡序号；真正全程隐藏的角色才跳过。

判定优先读 Timeline 插件运行时的真实数据（`Timeline.GetAllInterpolables(false)` 里
`objectEnabled` 通道绑定的 `oci`），绕开「objectIndex 下标 ↔ 运行时对象」的映射；
读不到才回退到场景卡文件解析 + 按 key 排序的下标映射。日志会逐个角色打印
`key / tlIndex / visible / willReveal`。

### 修复：暂停 / 停止的真实语义与失效问题

- **停止（停止按钮）= 立刻中断**：以前只设了个标志，替换 female 的循环里不检查，
  所以点了停止还会把剩下的角色全换完。现在替换、换装、各准备阶段都会检查，
  下一个检查点立刻退出；正在录制时让 VideoExport 收尾（管道不能硬切），
  当前这张丢弃，已完成的片段照常合并。
- **暂停（ESC / 暂停按钮）= 停在原地，随时继续**：现在在**角色与角色之间**、
  每张卡之间都能停住，不会把某个角色换到一半。正在录的那张卡丢弃重录。
- 从暂停态点停止不会卡住（会先清掉 Paused）。

### 新增：去遮挡物（跟去码一起执行）

去码原来只管材质（Pixelate / CensorEffectMat / blur_），碰不到两类独立遮挡：

- **「カプセル(通常)」胶囊遮挡道具**：部分场景卡把它当普通道具摆在镜头前
  （如 hongcha #06 的 `캡슐 (보통)`）。现在按树节点显示名匹配
  カプセル / 캡슐 / 胶囊 / Capsule，直接把道具隐藏。
- **Studio Text Plugin 的静态文字**：`-Text Plugin:xxx` 这类独立 GameObject
  （不在 dicObjectCtrl 里），按名字前缀隐藏根对象。

### 新增：预览按钮（导出旁）

点「预览」= 切到第一张勾选的场景，走完和导出**完全一样**的准备流程
（加载 → 替换 female → 换装 → 姿态 → 碰撞体 → 去码 → 去遮挡物 → 等物理稳定），
但**不调 VideoExport 录制**、**不隐藏界面**，方便正式导出前先看效果。
预览不需要 VideoExport 就绪。

### 新增：male 半透明

设置里新增「male 透明度」滑块（0 = 不处理，默认 0.35）：
- 不透明材质换成透明版 `Shader Forge/main_alpha`（贴图属性名一致，不会丢贴图）；
- 设 Alpha 混合（`_SrcBlend=SrcAlpha` / `_DstBlend=OneMinusSrcAlpha` / `_ZWrite=0`，
  renderQueue=3000），并把 `_Color` / `_Color2` / `_ShadowColor` / `_LineColor` 的 alpha 压低。
- 同时作用于 male 的身体、服装和饰品。

---

## 2.3.2

### 修正：Timeline「换装链」里的隐藏角色复用前一个显示角色的卡

2.3.1 把隐藏的 female 一律跳过，但有一类隐藏角色其实会出片：初始被眼睛图标藏起来、
Timeline 的 `objectEnabled` 通道会在某一帧把它**取消隐藏**，它是前一个显示角色的下一套
造型（A/B 反相交替，如 hongcha `#17` 的 obj 12↔13）。一律跳过会导致换装前后变成两个人。

现在规则细化成三类：

- **显示中** → 按顺序拿主卡 / 次卡，消耗一个新卡序号；
- **隐藏但会被取消隐藏（换装链）** → 复用前一个显示角色的卡，**不消耗新序号**，
  也跟着用同一套服装 / 体型（`ApplyClothes` 同步）；
- **真隐藏**（Timeline 也不会显示）→ 照旧跳过。

判定不加载场景：`TimelineReader.ReadRevealedObjects(scenePath)` 直接解析场景卡尾部
Timeline XML 里带 `value="true"` 关键帧的 `objectEnabled` 通道，取 `objectIndex`
（= `dicObjectCtrl` 的对象编号），带缓存。日志会分别报显示 / 换装链 / 跳过的数量。

`ExportOne` 只读一次这个集合，同时喂给替换和服装两步。

### 2.3.1 的改动（保留）

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

---

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

---

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

---

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

---

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

---

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

---

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

---

# 历史留档（编号已作废）

> 以下条目全部是 **2.4.0 的开发测试版**，编号不作为版本引用。
> 标题前标了 `[作废]`，正文原样保留，方便回查当时的实现思路。

---

## [作废] 2.6.8

### 修复：勾了「乳量取自场景」导致胸腔塌陷

不是"场景卡被替换所以失效"，而是**参数不匹配**：

`bustSoftness` / `bustWeight` 是配**特定体型**的物理参数。之前 `BustFromScene` 和
`ShapeFromScene` 互相独立，于是可以出现「体型取自角色卡 + 乳量物理取自场景原角色」——
场景那个角色的参数套到角色卡的身体上，胸腔直接塌掉。

现在**乳量（大小和物理）必须跟体型同源**：

    bool bustFromScene = o.BustFromScene && o.ShapeFromScene;

只有「体型也取自场景」时，`BustFromScene` 才生效；体型用角色卡时，乳量也一律用角色卡。
`shape` 里的乳量维度和 `bustSoftness/bustWeight` 都走这个判断。

配置项说明也补了警告：`⚠ 只有「体型也取自场景」时才生效…`。

---

## [作废] 2.6.8

### 修复：胸腔消失 —— 是 bodyMask / AlphaMask 被跨角色复制了

社区的权威说明（巴哈 MaterialEditor 教学帖）：

> **bodyMask（身体遮罩）**：「穿緊身衣時…遊戲就會靠 **bodyMask 把胸部的部位強制指定為顯示衣服，
> 原胸部皮膚則直接消滅**！！衣服裡面變成空洞…你必須修改 bodyMask 才能解決這問題」

kknow 的 MaterialEditor 教程里也明确写：**复制 shader 之前要把 UV 相关的自定义贴图 Reset 掉**，
只复制 shader 和不依赖 UV 的参数（MatCap / Gloss / 颜色），否则贴图错位。

所以 2.6.6/2.6.7 把 `cf_m_body` 的**全部贴图**（含 `_AlphaMask` / bodyMask）一起复制过来，
遮罩按模板卡的身体 UV 画，套到目标角色身上就把胸腔整片挖掉了 —— 表现就是"胸没了 / 透明"。

修复：

- **缓存阶段就不存** 名字含 `mask` / `alpha` / `overlay` / `underlay` 的贴图；
- **应用阶段再挡一层**（同样关键词），即使旧缓存里有也不会写进去；
- 缓存格式版本 `1 → 2`，旧缓存自动失效重建；
- 已经手动清掉一次 `BepInEx/config/SkinCache/*.bin`。

保留下来的：`shader`、`MainTex`（身体 UV 是全局统一的，可以跨角色）、颜色、Float/Range
参数（Gloss、ShadowPower 之类）。

---

## [作废] 2.6.7

### 修复：皮肤缓存从没生成（预览路径漏了）

2.6.6 只把 `EnsureCache` 加在 `RunBatch`（批量）里，`RunPreview`（预览）没加 ——
所以点预览测试时缓存永远不存在，`ApplyToAll` 只会打 `皮肤缓存为空，跳过应用`，
`BepInEx/config/SkinCache/` 也一直是空的。

抽出 `EnsureSkinCache(o)`，`RunPreview` 和 `RunBatch` 都先调一次。

### 修复：换装把乳量物理又刷回去

`SetCoordinateInfo` 内部会 `UpdateBustSoftnessAndGravity()`，之后分帧重建也可能再刷一次，
只在末尾写一遍 `bustSoftness` / `bustWeight` 会被覆盖。
改成**写两遍**（间隔 3 帧），日志：`[体型] 换装后只补乳量物理（写两遍防覆盖）`。

---

## [作废] 2.6.6

### 全局皮肤改成「一次性提取 → 本地缓存 → 只读文件」

用户思路：**在导出最开始把模板卡的皮肤材质解析出来存到本地，之后加载本地文件**。
这样 `AddFemale` 只在第一次做一次，之后完全不碰场景 —— 从根上避免"重建场景冲掉
隐藏角色显隐 / timeline 索引"这类副作用。

新增 `SkinMaterialCache`：

- **缓存内容**：`cf_m_body` / `cf_m_face` 两个材质的
  `shader`、全部 float / color / vector 属性、贴图（`EncodeToPNG` 存字节）
  + 每张贴图的 offset / scale；
- **缓存路径**：`BepInEx/config/SkinCache/skin_<hash>.bin`
  （hash = 卡路径 + 文件大小 + 修改时间，卡一改自动失效重建）；
- **生成时机**：`RunBatch` 开头、`WaitStudioReady` 之后（这时场景还是用户原场景），
  只有缓存不存在时才 `AddFemale` 一次；提取完立刻删临时角色 + 还原所有角色的显隐；
- **应用时机**：每张卡的 `2-b2`（换装后、姿态前），`SkinMaterialCache.ApplyToAll`
  读缓存 → 找同名材质 → 写 shader/属性/贴图 → `ReloadNoAsync` → `RestoreAll` 还原体型；
- **不取透明度**：`color` 类属性的 alpha 强制拉回 1，`renderQueue` 固定 2000 ——
  模板卡可能是半透明的，照搬会让胸腔透出来。

日志：

    [皮肤] 首次使用，正在从角色卡提取材质（会临时加载一次模板角色）…
    SceneTools: 皮肤材质已缓存 → skin_1a2b3c4d.bin（2 个材质）
    [皮肤] 已有本地缓存，跳过提取
    SceneTools: 已从本地缓存应用皮肤（cf_m_body/cf_m_face）给 场景 female，材质 2 个

---

## [作废] 2.6.5

### 修复：应用皮肤后胸腔塌成「空的 / 透明」

两个原因：

1. **体型还原不完整**：2.6.3 在 `ApplyGlobalSkin` 里用 `RestoreBustPhysics`（只补
   `bustSoftness` / `bustWeight`）。但 `ReloadNoAsync(noChangeBody: false)` 是**重建身体**，
   它会用 `custom.body.shapeValueBody` 重新生成 mesh —— 乳量那几个滑块如果还是卡里的原始值，
   胸腔就会塌下去、看着像空的。
   改成 **`RestoreAll`**（完整还原 shape + 乳量物理）。
   注意：这里和 `ApplyClothes` 不一样，`ApplyClothes` 的快照是「换装前」的所以只补物理；
   这里的快照是**当前正确的状态**，整份还原才对。

2. **把模板卡的透明属性带了过来**：`CopyMaterialProperties` 原来连 `renderQueue` 一起复制。
   模板卡的 `cf_m_body` 如果做过半透明（KKUSSalpha + renderQueue 3000 + `Color.a < 1`），
   照搬过来身体就透了。
   现在**不复制 renderQueue**，并把 `_Color` / `Color` / `_ShadowColor` / `Color2` 的
   **alpha 一律拉回 1**、`renderQueue = 2000`（不透明）。皮肤只取贴图和颜色 RGB。

---

## [作废] 2.6.4

### 修复：画面里多出一个「没有上半身」的角色

不是二次替换（日志可证：每张卡只有一次 `替换完成：N 个 female`）。
是 2.6.3 为了读材质而 `Studio.AddFemale` 临时加的那个皮肤角色：

- `AddFemale` / 之后的 `DeleteNode` 会把场景对象重建一遍，**隐藏角色的显隐状态被冲掉**，
  于是本该隐藏的 female 冒了出来；
- 临时角色在删除前也短暂可见，模型还没建全时就露出半个身子。

修复：

- `AddFemale` 之后立刻把临时角色 `charInfo.visibleAll = false` +
  `treeNodeObject.SetVisible(false)`；
- 复制前记录每个目标 female 的 `visibleAll`，删除临时角色后**原样还原**；
- 临时角色删除仍走 `Studio.Studio.DeleteNode` → `OnDelete` 双兜底。

### 修复：male 刘海透明、层次错（能透过刘海看到脑门）

2.4.x 起 male 半透明会把**所有**材质换成 `KKUSSalpha` 并把 `renderQueue` 设成 3000，
头发也被一起改了 —— 头发被排到不透明物体之后渲染，于是"先看到脑门、再透过刘海"。

现在**跳过头发**（renderer 名或材质名含 `hair` 一律不动），保持原来的不透明 shader。
日志新增「跳过头发 N」。

---

## [作废] 2.6.3

### 全局皮肤改成「MaterialEditor Copy Edits」的等价实现

之前走的是 `ChaFileFace` / `ChaFileBody` 的**字段**（skinId / 肤色 / 涂装…），
但用户手动做法是 **MaterialEditor → Copy Edits `cf_m_body` / `cf_m_face` → 粘到新角色同名材质**，
两套东西并不等价 —— 字段那套不会带上 shader 和材质属性，所以看起来"皮肤没换"。

MaterialEditor 的 `MaterialCopyEdits` 实际做的事是把 `shader + float/color/keyword/
texture 属性 + renderQueue` 存进静态 `CopyData`，再 `MaterialPasteEdits` 写回去。
所以这次直接复刻这个动作：

1. `Studio.AddFemale(皮肤卡路径)` —— 临时把模板卡加成场景里的一个角色
   （只有这样才能拿到**已经应用了 MaterialEditor 编辑**的材质对象）；
2. 等它建完模型（最多 600 帧），收集它的 `cf_m_body` / `cf_m_face`；
3. 逐个目标 female，找到**同名材质**，`CopyMaterialProperties` 整份复制：
   shader、Color/Float/Range/Vector/Texture 全属性、texture offset/scale、
   shaderKeywords、renderQueue、globalIlluminationFlags；
4. `ReloadNoAsync(noChangeClothes:true, …)` 重建，再 `RestoreBustPhysics` 补乳量；
5. `Studio.DeleteNode` 删掉临时角色。

**完全不碰** headId / shapeValueFace / shapeValueBody / typeBone / 五官 —— 头不会变大。
日志：`已按 MaterialEditor CopyEdits 方式应用皮肤（cf_m_body/cf_m_face）给 N 个 female（成功 N 个材质）`。

---

## [作废] 2.6.2

### 修复：全局皮肤导致「头异常大、比例不对」

`ChaFileFace.Copy` / `ChaFileBody.Copy` 会把整份数据带过来，其中包含决定头大小和比例的字段：

- `headId` —— 头模（决定头的绝对大小）
- `shapeValueFace` —— 脸型滑块
- `shapeValueBody` —— 体型
- `typeBone` —— 骨架类型
- 五官（眼睛/眉毛/鼻子/嘴）的形状 id

只保留 `headId` + `shapeValueFace` 写回还不够，所以上一版头还是大。

现在**完全不用 Copy**，改成逐字段只写「皮肤」：

- **face**：`skinId` / `detailId` / `detailPower` / `cheekGlossPower`
- **body**：`skinId` / `detailId` / `detailPower` / `skinMainColor` / `skinSubColor` /
  `skinGlossPower` / 涂装（paintId/Color/LayoutId/Layout，数组 Clone）/ 晒痕 /
  乳晕 / 体毛 / 指甲 / `drawAddLine`

**头型、脸型、体型、骨架类型、五官形状一律不动。**

`Reload` 之后只补乳量物理（`RestoreBustPhysics`），不写 shape。

---

## [作废] 2.6.1

### 修复：场景卡网格右侧一大块空隙

`TileGrid.Cell` 原来用固定的 `W`（= Thumb+20）排列，窗口宽 1380 时 3 列只占 612px，
右边空出 148px。现在**列宽按可用宽度均分**：

    float cellW = (width - Gap) / cols;
    return new Rect(Gap + col * cellW, ..., cellW - Gap, H);

`ThumbRect` / `NameRect` / `SubRect` 也从固定的 `W` 改成 `tile.width`，
缩略图仍然居中。卡片选择器一起受益。

### 修复：换装后「脸和身体完全不一样」

2.6.0 在 `ApplyClothes` 末尾用 `RestoreAll` 把 shape **和**乳量一起写回换装前的快照。
但 `OCICharFemale.SetCoordinateInfo` 走的是
`Reload(noChangeClothes:false, noChangeHead:true, noChangeHair:true, noChangeBody:true)`
—— **身体根本没重建**，shape 还是换人后那一套；再写旧快照的 shape，等于把
`ReplaceFemale` 配好的体型又改回去，看起来就是换了个人。

新增 `BodySnap.RestoreBustPhysics`：换装后**只补 `bustSoftness` / `bustWeight`**
（这两个会被 `UpdateBustSoftnessAndGravity()` 重置），**不动 shapeValueBody**。
日志：`[体型] 换装后只补乳量物理，体型保持换人后的状态（不覆盖 shape）`。

### 修复：全局皮肤"日志说应用了但画面没变"

两个原因：

1. **替换了引用**：`chaFile.custom.face = new ChaFileFace()` 这种写法，`ChaControl`
   内部缓存了 `fileFace` / `fileBody` 的引用，换掉 `custom.face` 后它仍指着旧对象，
   `Reload` 也换不过来。改成 **`dst.Copy(src)` 复制进现有对象**。
2. **时序**：原来放在换装之后、姿态之前。现在明确放在
   **换装之后、姿态之前**（`2-b2`），皮肤 Reload 重建的骨骼由后面的时间轴 seek 重新打姿态，
   不会录成 T-pose；再往后的去码/过曝/male 只改材质，不碰脸和身体。

---

## [作废] 2.6.0

### 调整：角色光照上限 1.0 → 0.9

`ClampCharacterLighting(0.9f)`，换人前后各压一次（两处都改）。

### 新增：全局 female 皮肤（face + body）

替换完 female、换完装之后，把一张指定角色卡的 **face + body** 套给场景里所有 female。

- 配置项 `AutoExport / GlobalSkinCard`，默认 `chara/female/151579669_p3.png`
  （相对 UserData；也可填绝对路径，留空 = 不应用）；
- 只复制 face / body，不动 hair 和 coordinate；
- **体型不会被覆盖**：应用前拍体型快照、`ReloadNoAsync(true, false, true, false)` 之后
  用 `BodySnap.RestoreAll` 还原；
- `noChangeClothes: true` 保证已经换好的衣服不被冲掉。

日志：`已应用全局皮肤（face+body）给 N 个 female ← 文件名`。

### 修复：乳量物理被改回角色卡的值

根因在 `ApplyClothes` 末尾：它用「换装前快照」当 `Compose` 的 scene 参数，而那份快照在
`ReplaceFemale` 阶段已经被按 `BustFromScene` 处理过一次；再走一次 Compose 的
`BustFromScene` 分支，等于拿这份快照当「场景值」二次覆盖。

新增 `BodySnap.RestoreAll(oci, snap)`：**按快照原样还原** shape + `bustSoftness` +
`bustWeight`（并调 `UpdateBustSoftnessAndGravity` 刷物理），完全不走来源配置。
换装本身不该改变体型，这才是正确语义。日志会打
`[体型] 换装后已按换装前状态还原（体型 + 乳量物理）`。

### 调整：场景卡缩略图放大

`TileGrid.Thumb` 从 `const 132` 改成可变的 `static float`（`W`/`H` 由它算出来）。
场景卡网格绘制时切到 **176**，画完恢复 132 —— 卡片选择器尺寸不变。

### 关于工作区树「一栏两个选框」

查过本插件全部代码：只有 `item.treeNodeObject.SetVisible(false)`（去遮挡物）和读
`treeNodeObject.textName`，**没有任何地方改树的 UI 结构**，也没有 patch Studio 的树绘制。

截图里的 `hip1` / `juice` 不是 Koikatsu 原生节点名（原生是 `cf_O_*` / `cf_J_*`），
是**插件生成**的节点（动态骨骼 / 碰撞体一类），每个自带可见性图标 —— 所以看起来
"一栏变成两个"。建议从 `BepInEx/plugins/StarPlugins/` 里的 `AAAAAAAAAAAA.dll`
（含 `Fix_DynamicBones`）开始逐个排除。

---

## [作废] 2.5.0

### 修复：批量导出内存泄漏 / 32G 用满闪退

**根因**：整个项目**从来没有释放过旧场景的资源**。Studio 的 `LoadSceneCoroutine`
只销毁场景里的对象，**不会**卸载 asset bundle 里的贴图 / 网格 / 材质实例 ——
Unity 只有 `Resources.UnloadUnusedAssets()` 才会真正回收。

一张 hongcha 卡解出来就是几十 MB 贴图 + 上千个材质实例（去码、过曝抑制、
male 半透明都会遍历所有 renderer 并访问 `.materials`，每次访问会实例化材质），
连做十几张就线性累积到 32G，最后在**加载下一张卡时** OOM 闪退 ——
和日志末尾停在 `SSS: scene_loaded1` 的现象完全吻合。

修复：

- 新增 `CleanupAfterCard()`：**每张卡录完**就
  `Resources.UnloadUnusedAssets()`（等它跑完）+ `GC.Collect()`；
- 新增 `LogMemory(tag)`：用 `Profiler.GetTotalAllocatedMemoryLong` /
  `GetTotalReservedMemoryLong` / `GC.GetTotalMemory` 分别打 Unity 侧和托管堆；
- 日志会在三处打内存：`批量开始`、每张 `开卡前`、`卡间清理后` ——
  直接能看出是"每张涨一截"还是"某一张突然暴涨"。

日志示例：

    [内存] 批量开始：Unity 已用 3200MB / 保留 4100MB，托管堆 120MB
    [内存] 开卡前：Unity 已用 3560MB / 保留 4400MB，托管堆 130MB
    [内存] 卡间清理后：Unity 已用 3300MB / 保留 4100MB，托管堆 95MB

如果「卡间清理后」仍然一张比一张高，说明泄漏不在场景资源，而在
VideoExport / ReShade 的截图缓冲（那部分本插件控制不了，只能分批导出）。

---

## [作废] 2.4.9

### 修复：服装随机"失效"（其实是静默失败）

根因不在随机逻辑，而在**时序**：

`ReplaceFemale` 里每个角色都调过 `ChangeChara`，它的 `Reload` 是**分帧**执行的。
`ApplyClothes` 紧接着就调 `SetCoordinateInfo(slot, true)`，而 `OCIChar.SetCoordinateInfo` 是：

    if ((_force || ...) && charInfo.ChangeCoordinateType(_type))
        charInfo.Reload(noChangeClothes: false, ...);

`ChangeCoordinateType` 在角色卡还没就绪时会**返回 false** —— 于是整段什么都不做：
不抛异常、不打日志，看起来就是「随机服装没生效」。日志里仍会出现
「已随机切换服装预设 #6 社团」，但画面根本没换。

修复：

- `ApplyClothes` 开头先等 **12 帧**，让 `ChangeChara` 的 Reload 全部落地；
- 切槽后**验证** `fileStatus.coordinateType` 是否等于目标槽位，不对就重试一次；
- 重试后仍不对，打 `[提示] 服装槽 #N 切换没生效（重试后仍失败）`，并计入
  `nCoordFail`，结尾汇总；
- `AvailableOutfits` 返回空时打提示（说明角色卡还在加载），不再静默退回 #0。

### 修复：暂停时日志被刷屏（一次暂停 51 条）

`while (Paused)` 循环里每帧调 `SetPhase`，而 `SetPhase` 每次都 `LogMessage` ——
一次暂停就刷了 51 条「已暂停：按 ESC 继续」，把真正的日志冲没了。
现在只在**进入暂停**时写一次。

---

## [作废] 2.4.8

### 关键修正：性器要按「父节点链」判断，不能只看 renderer 自己的名字

2.4.7 用的是 dankon / mnpb 关键词，但 `跳过性器材质` 仍是 0 —— 因为**男性性器的
renderer 名字就是 `cf_O_body`**，真正标识在它的**父节点**上（Studio 工作区树里
「股间」那一组）：

    cf_01/cf_o_root/n_body/n_dankon/o_dankon/o_gomu
    cf_01/cf_o_root/n_body/o_body_a/n_mnpb/o_mnpb

现在新增 `IsUnderGenitalNode(Transform)`：从 renderer **往上爬最多 10 层父节点**，
任一段命中 dankon / mnpb / mnpa / dan_f / goma / kokan 就判定为性器，跳过不透明化。

判定升级为四重：**父节点链** + renderer 名 + 材质名 + shader 名。
日志新增 `male 跳过的性器节点：父链→材质, …`，可直接看到跳过了哪些节点。

---

## [作废] 2.4.7

### 定位到性器真实命名：dankon / mnpb（不是 penis / kokan）

上一版日志 `跳过性器材质 0`，而且名单被 900 字截断在眼睛。这次直接从
`abdata/chara/oo_base.unity3d`（男性基础模型）里挖出真实节点结构：

    cf_01/cf_o_root/n_body/n_dankon/o_dankon/o_gomu     ← 男根（dankon）
    cf_01/cf_o_root/n_body/o_body_a/n_mnpb/o_mnpb       ← mnpb
    cf_01/cf_o_root/n_cm_body/n_dankon 1/o_dan_f        ← 男根另一种布局
    Shader Forge/mnpb                                    ← 男根专用 shader

所以关键词改为 `dankon / mnpb / mnpa / dan_f / goma`（保留 penis/peni/penetrat/chimpo/manko），
并且做**三重判定**：renderer 名 + 材质名 + **shader 名**（`Shader Forge/mnpb`）。

名单日志也改了：只记身体/性器相关的名字（含 body/dankon/mnpb/…），
并去掉 (Instance) 后缀去重 —— 上次就是因为头发/脸太多把 body 挤出了 900 字窗口。

---

## [作废] 2.4.6

### 修正：male 性器仍被透明（2.4.5 的名字匹配一个都没命中）

日志可证：`跳过性器 renderer 0`。说明 Koikatsu 男性性器的 renderer 名字里
**没有** penis/kokan 这些关键字（通常是 `body`，性器只是 body 的一个独立材质槽）。

改动：
- 判据从「只看 renderer 名」改为 **renderer 名 + 材质名**双重匹配
  （`penis` / `peni` / `penetrat` / `kokan` / `chimpo` / `inko` / `manko` / `sex_`）；
- 撤回上一版按 `objParts` 的排除 —— 查过 `ChaInfo`，`objParts` 是**服装子部件**
  （`ClothesSubKind`），不是性器，用它会把部分衣服也留成不透明；
- 每次导出会把 male 的 **renderer/材质完整名单**打进日志（`male renderer/材质 名单：…`），
  下次能直接看出性器到底是哪个材质名，按需补关键词。

---

## [作废] 2.4.5

### 修复：角色光照在换人后被 Reflect() 覆盖回 1.2

2.4.4 的日志能证明改成功过（`1.26 → 1`），但面板又变回 1.2：**换人 / 换装之后
`CameraLightCtrl` 会 `Reflect()` 把场景里保存的光照重新应用一遍**，把之前压下去的值覆盖掉。
现在在角色全部处理完之后（去遮挡物之后、ShadowPower 之前）**再压一次**，之后没有东西再改它。
两次都会打日志：`角色光照亮度 X → Y`，可以直接看第二次那行。

### 修改：不再动 Addcolor

按要求去掉 `Addcolor` 的 clamp —— 那是角色作者调过的配色，不能改。
过曝抑制现在**只抬 ShadowPower**（< 0.85 → 0.85）。

### 修正：male 半透明不用「単色」，改回材质并保留原色

2.4.4 用的 `SetVisibleSimple(true)` 是 Studio 的「単色 / 简单显示」——
它把正常身体换成 `S_SimpleBody` **纯色替身**，所以看起来是「完全透明只剩白色轮廓」，
不是带颜色的半透明。现在：

- **去掉 SetVisibleSimple / SetSimpleColor**，全部走材质；
- 材质换成 `KKUSSalpha`，**迁移原材质的贴图和颜色**（`_MainTex`→`MainTex`、`_Color`→`Color`），
  只把 `Color.a` 降到 alpha —— 保留角色原本的肤色/衣服颜色；
- `Color` / `_Color`、`Color2` / `_Color2`、`LineColor` / `_LineColor` 两种命名都写；
- 再用 `Shader.GetPropertyCount()` 遍历属性找 alpha/opacity 类 Float 兜底；
- **跳过性器 renderer**（名字含 penis / penetrat / kokan / peni），它们保持不透明。

日志新增「跳过性器 renderer N」。

---

## [作废] 2.4.4

### 过曝根因确认：ShadowPower 被调到 0.55（默认 0.85）

KKUSS 的 `ShadowPower` **越小越亮**。角色卡把它设成 0.55，画面就整体过曝、浅色衣服
白成一片。现在导出时（换人换装之后）遍历所有角色材质：

- `ShadowPower < 0.85` → 抬回 **0.85**（属性名同时试 `ShadowPower` / `_ShadowPower` / `shadowPower`）；
- `Addcolor` RGB > 1 → clamp 到 1；
- 不再只限 KKUSS 名，凡是暴露 `ShadowPower` 属性的材质都处理。

日志会报「ShadowPower 抬回 0.85 的 N 个（最低见到 0.55）」，能直接确认。

### 修正：角色光照改了但面板还显示 1.2

2.4.3 已经能把 `light.intensity` 从 1.26 压到 1（日志可证），但 Light Controller 面板
上的输入框仍显示旧值 —— 因为没调 `LightCalc.UpdateUI()`。现在改完会：
同步 `sliderIntensity` / `inputIntensity` → 调 `UpdateUI()` → 调 `Reflect()`。

### male 半透明改用游戏原生实现（主游戏 H 场景同款）

不再靠猜 shader 属性。Studio 的「単色 / 简单显示」就是主游戏 H 场景男主半透明的实现：

- `OCIChar.SetVisibleSimple(true)` → 隐藏正常身体，改用模型自带的 `S_SimpleBody` 替身；
- `OCIChar.SetSimpleColor(Color)` → 设替身的 `_Color`（**alpha 就是透明度**）。

身体走这条路；衣服/饰品仍走材质（替身只覆盖身体），并且改成**遍历 shader 属性**
找所有名字含 alpha/opacity/transparen 的 Float/Range 来设值，不再硬编码 `"alpha"`。
仍改不到时会把该 shader 的属性名打进日志，下次能直接看到真名。

---

## [作废] 2.4.3

### 修正：male 半透明改用 KKUSSalpha（衣服不透明 + 头发变白）

2.4.2 把所有材质换成 `main_alpha`，结果衣服还是不透明、头发还变白。按实际测试改：

- 统一换成 **`KKUSSalpha`**（KKUSS 包里的透明版 shader，
  manifest：`Shader Name="KKUSSalpha" AssetBundle="chara/kkussalpha.unity3d"`），
  然后直接改它的 **`alpha`** 属性（小写、无下划线）。
- **换 shader 之前先迁移属性**：原生 shader 是 `_MainTex`/`_Color`，KKUSS 是
  `MainTex`/`Color`。不先读旧值就换，贴图会全部丢掉 —— 头发变白就是这个原因。
- KKUSSalpha 平时没有材质引用、`Shader.Find` 拿不到，所以补了一条
  MaterialEditor 的 `LoadedShaders` 字典兜底（zipmod manifest 注册的 shader 都在里面）。

### 修正：只限制「角色光照」，不是全局光照

2.4.2 把所有 `Light` + `RenderSettings.ambientIntensity` + 所有灯光道具都压到 1.0，
范围过大。实际上 Koikatsu 的 KKUSS/KKUTS 几乎只吃 `CameraLightCtrl` 里那盏角色灯
（`LightCalc.light`），场景地图灯影响很小。现在只 clamp 这一盏：

- 反射取 `CameraLightCtrl.lightChara`（LightCalc）→ `light`（Light），
  亮度 > 1.0 就压到 1.0；
- 同步面板上的 `sliderIntensity` / `inputIntensity`，UI 不会还显示旧值；
- **不再动**场景灯、环境光、灯光道具。

### 新增：KKUSS 过曝抑制（Addcolor clamp）

浅色衣物整片白掉的另一个直接来源是 KKUSS 材质的 `Addcolor`（加色，默认 1,1,1,1）。
角色卡常把它调到 1.2~2，配上强光就糊成一片。现在导出时把所有 KKUSS 系材质的
`Addcolor` 的 RGB clamp 到 1.0（只压不抬，不动 Color / MainTex）。

---

## [作废] 2.4.2

### 修复：male 半透明把皮肤/衣服换成了道具 shader（像换了个人）

2.4.1 的 shader 查找是「优先 main_item_alpha」，结果**所有材质**（包括皮肤、衣服）
都被换成 `main_item_alpha` —— 那是道具用的 shader，没有皮肤阴影、腮红、衣服图案，
所以看起来像换了一个人。现在按原 shader 类型映射：

- 原 shader 名含 `item` → `main_item_alpha`
- 其它（skin / clothes / hair）→ `main_alpha`
- 找不到对应透明版就**不换 shader**（只尝试改 alpha/混合），宁可不够透也不破坏外观

日志会打印「换 shader N / 无对应透明版 N / 无颜色属性 N」以及每个 male 材质的原 shader 名，
方便下次直接看出映射对不对。

### 新增：场景全局光照一律封顶 1.0

场景卡经常把灯强度调到 2~8，配上 KKUSS 这类对光照敏感的 shader，浅色衣服/帽子/袜子
会整片过曝。现在在**换人之前**统一封顶：

- 场景里所有 `Light`（地图 P-MODMAP 灯、相机灯、Character Lighting 的灯）；
- `RenderSettings.ambientIntensity`（Unity 环境光）；
- `OCILight` 的保存值 `lightInfo.intensity`（同步树面板上的滑块）。

只压不抬，已经很暗的灯保持原样。日志会报「Light N 个 / 环境光 N / 灯光道具 N 个」。

---

## [作废] 2.4.1

### 修正：换装卡的隐藏 female 一律用主角色卡（2.4.0 的判定对 #06 不生效）

2.4.0 只处理「objectEnabled 通道里 value=true」的隐藏角色。但实际换装卡不是这么做的：
hongcha #06 有 9 个 female，timeline 里只有 **1 个** objectEnabled 通道，
其余 7 个隐藏 female 的入场靠的是 `guideObjectPos`/`guideObjectRot`（位置动画），
甚至 `bonePos`/表情曲线 —— 所以 2.4.0 一个都识别不到，日志永远是
「跳过 8 个真隐藏角色」。

现在改成两步，且都在替换之前完成：

1. **先标记**：读 Timeline 运行时数据，凡是某个 female 有**任意**插值项
   （位置/旋转/骨骼/表情/显隐），就标记为「在 timeline 里出现过」；
2. **再替换**：显示中的 female 照旧按主/次卡顺序分配；**隐藏的 female 一律用主角色卡**
   （不消耗次卡序号），主卡没选时退回上一个显示角色的卡，再不行用次卡兜底。

这样换装前后是同一个人，也不会再出现「隐藏角色被全部跳过」。
日志会打印每个角色的 `inTimeline`，替换完成时会分别统计
「显示用主卡 / 显示用次卡 / 隐藏用主卡」。

### 修复：male 半透明找不到透明 shader

`Shader.Find("Shader Forge/main_alpha")` 在 KKS 里返回 null —— 因为 main_alpha 平时
没有被任何材质引用，没加载进显存。现在改用
`Resources.FindObjectsOfTypeAll<Shader>()` 在**已加载的全部 shader** 里挑
（优先 `main_item_alpha` / `main_alpha`，其次任何带 alpha 的），
再退回常见名字，最后用 Unity 内置透明 shader 兜底。
