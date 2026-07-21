# FaceSlapper 技术方案（基于 plan.txt 整理与改进）

## 1. 项目概述

- **游戏类型**：基于真实物理引擎的多人对战派对游戏（类猛兽派对）
- **视角**：3D 俯视角第三人称相机
- **联机方案**：底层当前使用 FishNet Pro 4.7.2，但**全部网络调用都封装在自研抽象层之后**，
  更换为 Mirror / Netcode for GameObjects 只需修改一行代码（见第 5 节）。
- **初版范围（里程碑 M1）**：局域网房间（Host/Join）→ 开始游戏 → 玩家生成 → 移动 → 拾取武器 → 攻击 → 击飞。除 GM 调试命令行外不接任何 UI。

## 2. 技术栈与项目现状

| 项 | 版本/说明 |
|---|---|
| Unity | 2022.3.62f3 |
| 网络 | 自研抽象层 `FaceSlapper.Networking`，当前后端 FishNet Pro 4.7.2（Tugboat UDP） |
| 渲染 | URP 14 |
| 输入 | 旧版 Input Manager（`activeInputHandler: 0`），代码全部用 `KeyCode` 轮询，不依赖 InputManager.asset 轴配置 |
| 场景 | 初版单场景 `Main.unity`（大厅+竞技场一体） |

## 3. 总体架构

```
GameEntry（场景引导器，Awake 时创建一切）
 └─ GameManager（全局唯一单例 MonoBehaviour，跨场景不销毁）
     ├─ 组件注册表 Dictionary<Type, IGameComponent>，Get<T>() 访问任意组件
     ├─ InputComponent ── InputHandler（纯 C# 轮询）
     ├─ EventBus / PoolComponent / TimeComponent / LogComponent
     ├─ SceneManagementComponent ── SceneHandler
     ├─ GMComponent + GMConsole（IMGUI 命令行）
     ├─ NetworkComponent ──┐
     ├─ TickComponent ─────┤
     └─ RoomHandler ───────┤
                           ▼
              ┌─────────────────────────────┐
              │  Net 门面（唯一网络入口）      │  ★ 游戏代码只允许调用这里
              │  Net.Server/Client/Events    │
              └─────────────┬───────────────┘
                            │ INetBackend
              ┌─────────────▼───────────────┐
              │  FishNetBackend（当前实现）   │  全部 FishNet 引用收口于此 +
              │  FishNetNetObjectBridge      │  FishNetNetObjectBridge 两个文件
              └─────────────────────────────┘

游戏对象上的网络组件（全部后端无关）：
 ├─ NetObject（身份/所有权/生命周期事件/RPC 与 NetVar 路由）
 ├─ NetBehaviour（需要 RPC/NetVar 的逻辑基类）
 ├─ NetTransformSync（位置旋转同步，20Hz 不可靠通道 + 接收端插值）
 └─ [后端附加] FishNet NetworkObject + FishNetNetObjectBridge（由搭建工具自动添加）
```

## 4. 目录结构（全部位于 `Assets/Scripts/`）

```
Assets/Scripts/
├─ Core/            GameManager, GameEntry, IGameComponent, Singleton, EventBus, Events,
│                   Observer(Subscriber/Notifier), Pool/, Log/, Time/, Scene/, GM/
├─ Input/           InputHandler, InputComponent, InputSnapshot
├─ Networking/      ★ 联机抽象层（后端无关）
│   ├─ Net.cs              静态门面（含 ★一行切换后端的工厂）
│   ├─ INetBackend.cs      后端接口（连接/生成/查找/所有权/事件/场景/编辑器钩子）
│   ├─ NetObject.cs        网络对象标识组件 + RPC/NetVar 路由
│   ├─ NetBehaviour.cs     网络逻辑基类（SendServerRpc/ObserversRpc/TargetRpc）
│   ├─ NetRpcAttribute.cs  [NetRpc] 标记 + 反射派发（带类型缓存）
│   ├─ NetVar.cs           NetVar<T> / NetList<T>（服务器权威同步变量/列表）
│   ├─ NetSerializer.cs    二进制序列化（基元/Vector/枚举/INetSerializable）
│   ├─ NetTransformSync.cs 位置旋转同步（替代 NetworkTransform）
│   └─ FishNetImpl/        ★ 当前后端（全项目仅此处引用 FishNet）
│       ├─ FishNetBackend.cs          INetBackend 实现 + 事件绑定 Runner
│       └─ FishNetNetObjectBridge.cs  NetworkBehaviour 桥接（RPC/NetVar/Transform 通道）
├─ Network/         NetworkComponent（Host/Join/Stop 封装）、TickComponent
├─ Room/            RoomComponent, RoomHandler, RoomPlayerInfo
├─ Camera/          TopDownCamera
├─ Battle/          NetworkIdentity, Movement, PlayerSpawnPoints,
│                   Abilities/(IAbility, AbilityComponent, Hitback, PickWeapon, SpeedUp)
├─ Weapon/          IWeapon, WeaponBase, SlapperWeapon
└─ Editor/          FaceSlapperSetup（后端感知的一键搭建）
```

## 5. 联机抽象层设计（后端可替换）

### 5.1 更换后端的方法（一行代码）

```csharp
// Assets/Scripts/Networking/Net.cs
// ★★★ 切换联机后端只需修改这一行 ★★★
private static INetBackend CreateBackend() => new FishNetImpl.FishNetBackend();
// private static INetBackend CreateBackend() => new MirrorImpl.MirrorBackend();   // Mirror
// private static INetBackend CreateBackend() => new NetcodeImpl.NetcodeBackend(); // Netcode for GameObjects
```

新后端需要实现：
1. `INetBackend`（约 20 个方法/事件，直接映射到底层库 API）；
2. 一个挂在网络对象上的桥接组件（实现 `INetObjectBridge`：5 个 RPC 通道 + NetVar 转发 + Transform 转发 + 生命周期转发）；
3. 编辑器钩子：给 Prefab 添加底层库组件、注册可生成对象、放置网络管理器。

之后运行一次 `FaceSlapper/Setup All` 重建 Prefab/场景即可。

### 5.2 抽象层的关键决策

| 决策 | 说明 |
|---|---|
| 自研 NetVar/NetList | 用可靠 RPC 通道传输（值序列化为字节流），不依赖各库的 SyncVar 代码生成，三个后端写法完全一致；客户端 OnStartClient 时向服务器拉全量状态，解决迟加入/竞态 |
| 自研 RPC 派发 | `[NetRpc]` 标记 + 方法名 + 类型标签参数序列化 + 反射缓存派发；语义三件套：SendServerRpc / SendObserversRpc / SendTargetRpc(clientId) |
| 自研 NetTransformSync | 替代 NetworkTransform：控制端（Owner 或无主时的服务器）20Hz 不可靠广播，接收端 100ms 延迟插值；接收端刚体运动学，杜绝双重物理模拟 |
| 身份用 int NetId | 对象查找 `Net.Server/Client.TryGetObject(netId)`，不暴露底层对象类型 |
| 所有权抽象 | `NetObject.IsOwner/IsController/OwnerClientId` + `Net.Server.Give/RemoveOwnership` |

### 5.3 网络同步模型

| 数据 | 权威方 | 手段 |
|---|---|---|
| 房间状态/玩家列表/开始游戏/生成销毁 | **服务器** | NetVar/NetList + ServerRpc |
| 玩家移动/旋转/受击飞位移 | **客户端 Owner** | 本地 Rigidbody 模拟 + NetTransformSync 广播 |
| 命中判定与击飞校验 | 攻击方检测 → **服务器校验** → 受害者 Owner 执行 | ServerRpc → 距离校验 → TargetRpc |
| 武器归属/拾取 | **服务器** | ServerRpc 申请 → NetVar HolderNobId + 所有权转移 |

升级路径：后续可迁移到服务器权威物理（客户端预测 + 和解），`TickComponent` 已预留网络 Tick 挂载点。

## 6. 其余模块设计要点

- **GameManager/GameEntry**：组件注册表 + 统一生命周期分发（IUpdatable/IFixedUpdatable/ILateUpdatable）。
- **EventBus**：泛型 struct 事件，零装箱；典型事件：LocalPlayerSpawnedEvent、RoomStateChangedEvent、PlayerHitEvent。
- **Input**：InputHandler（纯 C# KeyCode 轮询）→ InputSnapshot；InputComponent 每帧派发；GM 命令行聚焦时输出空快照。
- **Camera**：TopDownCamera 固定偏航俯视角、滚轮缩放（联动俯仰角）、SmoothDamp 跟随本地玩家。
- **武器"虚拟挂载"**：不用网络父子同步；持有者 Owner 每帧对齐手部挂点，其他端插值。
- **GM 命令行**：`` ` `` 呼出，`/gm func MethodName(arg1,arg2)` 反射调用 GMComponent（参数支持 int/float/bool/string）。内置：Host / Join(ip) / Stop / StartGame / SpawnWeapon(x,z) / ListPlayers / SetTeam / Log。
- **按键**：WASD 移动、鼠标左键攻击、E 拾取/放下、Q 耳光、Shift 加速、滚轮缩放。

## 7. 编辑器一键搭建

`FaceSlapper/Setup All`（后端感知，换后端无需改它）：
1. 生成 `Player.prefab` / `SlapperWeapon.prefab`（物理组件 + 游戏脚本 + NetObject + NetTransformSync + 后端组件）；
2. 注册可生成对象（后端钩子，当前写入 DefaultPrefabObjects.asset，并自动清理失效条目）；
3. 创建 `Main.unity`：网络管理器（后端钩子）+ GameEntry + Room（场景网络对象）+ 竞技场 + 4 出生点 + 相机 + 灯光，加入 Build Settings。

## 8. 美术资产（程序化生成）

主角形象：球形体（半径 0.6）+ 米白底黑豆豆眼 + 两侧低模卡通手掌。全部资产由 `FaceSlapper/Generate Art Assets`（也被 Setup All 调用）程序化生成，无需外部建模工具：

| 资产 | 说明 |
|---|---|
| `Assets/Art/Shaders/ToonLit.shader` | URP 卡通 Shader：Ramp 分阶光照（含主光源阴影衰减）+ 反向壳描边 + 阴影投射 |
| `Assets/Art/Shaders/ToonDecal.shader` | 通用贴花 Shader：**形状由贴图 Alpha 决定、与面片形状无关**（任意 Quad 可用）。无光照、透明混合、不写深度 + 深度偏移防 Z-Fighting；支持硬边裁剪（默认）/柔边混合两种模式、剔除模式可调 |
| `Textures/EyeDotTex.png` + `Materials/dot_eye.mat` | 透明底黑椭圆贴图（羽化边缘，Clamp）+ 贴花材质；眼睛用普通 Quad 贴在球面上（法线沿球心朝外） |
| `Textures/FaceDots.png` | 256×256 脸部贴图：纯色米白底 + 两个黑色竖椭圆豆豆眼 |
| `Textures/ToonRamp.png` | 128×8 三阶硬边 Ramp（暗 0.3 / 中 0.6 / 亮 1.0，Point 采样） |
| `Meshes/HandRelaxed_R/L.asset` 等 | 低模手掌网格：掌心 + 四指两节 + 拇指两节（复用内置立方体拓扑拼装），放松/握持两种弯曲参数，右手镜像生成左手 |
| `Materials/M_ToonBody/Face/Hand/Weapon.mat` | ToonLit 材质实例 |

玩家 Prefab 结构：SphereCollider + Body（球） + EyeL/EyeR（贴花豆豆眼） + Hands/HandR|HandL（Relaxed+Grip 两套网格） + HandSocket（右手掌心）。`HandVisuals` 脚本在持有武器时自动把右手从放松切换为握持；`NetworkIdentity` 按玩家序号给身体与双手染色。

## 9. 实现与验证状态

- 全部脚本已实现；在临时工程批处理验证：**编译 0 错误（含 FishNet IL 织入检查）**；
- `SetupAll` 已在批处理模式实际执行成功，产物（Prefabs/Materials/Main.unity/DefaultPrefabObjects/EditorBuildSettings + 全部 .meta）已回拷到本工程；
- 注意：NetVar 采用"客户端生成时拉全量"模型，服务器在对象生成瞬间的变量广播若与生成消息竞态，可能产生少量无害的时序日志，随后由全量拉取收敛。

## 9. 后续扩展路线

1. 实现 MirrorImpl / NetcodeImpl 后端（改 Net.cs 一行 + Setup All 一次）；
2. 服务器权威物理（客户端预测 + 和解）替换客户端权威移动；
3. Lobby/Game 场景拆分（SceneManagementComponent 已就绪）；
4. 互联网联机（NAT 穿透或 Relay）；
5. 队伍/积分/回合玩法、UI 接入（EventBus 已就绪）；
6. 命中特效/音效走 PoolComponent + ObserversRpc。
