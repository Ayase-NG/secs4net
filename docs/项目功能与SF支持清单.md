# 项目功能与 SECS SF 支持清单

> 依据：当前工作区 `feature/gem-gwm` 分支的源码、主服务依赖注入注册、消息路由和实际调用链。
> 本文描述“代码实现现状”，不等同于 SEMI 全标准符合性声明，也不代表已通过 Host/EFEM 实机验收。
> 验证情况：整理时工作区生成成功；未执行 Host、EFEM、PLC、Nacos 或 MySQL 实机联调。

## 1. 项目定位

本项目主要承担 **Equipment 侧 SECS/GEM 与设备内部 gRPC 的桥接**：

- 接收 Host/EAP 的 SECS 消息，解析并转换为设备内部控制请求。
- 接收设备/EFEM 的 gRPC 事件和报警，转换为 SECS 消息向 Host 上报。
- 管理在线状态、报告定义、事件绑定、报警使能、端口上下文及作业计划。
- 记录报警历史、关键 SECS 交互和运行日志，提供调试窗口。

主要业务服务是 `src/SECSGrpcService`，目标框架为 `.NET 8 Windows`，同时使用 ASP.NET Core gRPC、WinForms 和 `BackgroundService`。仓库还包含 SECS 底层库、示例程序、硬件控制代码及测试工程，不能将这些工程中的所有消息定义直接视为主服务业务能力。

### 1.1 主要代码分层

| 目录/组件 | 当前职责 |
| --- | --- |
| `src/Secs4Net` | SECS-II 数据项、消息、HSMS 连接和请求/应答基础设施 |
| `src/Secs4Net.Sml` | SML 文本相关能力 |
| `src/Secs4Net.Json` | SECS 数据的 JSON 相关能力 |
| `src/Messages/Models/SECSdata` | 消息数据模型 |
| `src/Messages/Parsers/SECSparser` | 接收消息解析 |
| `src/Messages/Builders/SECSbuilder` | 发送/应答消息构建 |
| `src/Messages/Handlers/SECShandler/Handlers` | 按 SF 分类路由与处理编排 |
| `src/Messages/Handlers/SECShandler/Functions` | 具体 SF 共享处理逻辑 |
| `src/Messages/Config` | VID、CEID 等映射配置 |
| `src/SECSGrpcService` | 实际业务宿主、gRPC、状态、持久化、主动上报和界面 |
| `src/HardwareControl` | PLC、LoadPort、Robot、Aligner、轴和工位等硬件控制相关代码；不等于已接入主服务的完整控制流程 |
| `samples`、`test` | 示例、可视化工具、单元测试和基准工程 |

## 2. 已实现的主要功能

### 2.1 HSMS 通信与消息路由

- 支持配置 Active/Passive、IP、端口、Device ID、接收缓冲区及 T3/T5/T6/T7/T8。
- `SecsPrimaryMessageListenerService` 后台持续接收 Host Primary Message。
- 按 `(S, F)` 路由到 `IPrimaryMessageHandler`，启动时检查重复路由。
- 共享 `SecsGemContext`，供 gRPC 事件、报警及主动发送器使用同一 SECS 会话。
- 支持配置自动启动监听，也支持通过调试窗口启停。
- 未注册的 SF 当前仅记录“尚未实现”，不保证返回统一的拒绝或错误消息。

### 2.2 通信建立与在线状态

- 通信探测、设备型号及软件版本返回。
- 建立通信请求的基础结构校验。
- Host 请求设备离线、上线。
- 状态包含 `OffLine`、`OnLineLocal`、`OnLineRemote`。
- gRPC 支持本地在线与远程在线互切，并尝试上报在线状态变化事件。
- `S2F41`、`S16F15`、`S14F9` 含远程在线条件判断；不同 SF 的权限控制并不完全一致。

### 2.3 状态变量和设备常量查询

- 支持按指定列表或空列表查询全部已配置 SVID/ECID。
- 支持查询状态变量名称及设备常量定义。
- 变量值可来自运行时 VID 缓存、设备内存状态、CSV 默认值及可选 PLC 数据源。
- SVID 读取具备短 TTL 缓存、分组批量读取等基础实现。
- PLC 是可选依赖，主服务当前未启用 `PlcClient` 注册，不能据此认定实际 PLC 数据读取已接通。
- 设备常量当前以查询为主，未注册 `S2F15/S2F16` 常量修改业务。

### 2.4 Host 动态事件报告配置

- `S2F33` 定义、更新、删除报告及清空报告。
- `S2F35` 维护 CEID → RPTID 绑定，校验 CEID 与报告定义。
- `S2F37` 启用/禁用单个、多个或全部事件。
- `ReportEvent` 将事件名映射为 CEID，将参数名映射为 VID，构建 `S6F11`。
- 普通参数支持字符串；类型化参数支持 SECS 数值类型、布尔、二进制和递归列表。
- 同名参数优先使用类型化参数。
- 普通参数更新运行时 VID 值，可供后续查询及事件报告复用。
- 当前通用事件实时上报只选取 CEID 绑定的第一个 RPTID，尚非完整多报告实时展开。

### 2.5 主动事件发送、重试与补发

- `ActiveSxFyDispatcher` 负责发送 `S6F11`，等待 `S6F12`。
- 发送前检查事件使能、CEID 绑定、已绑定报告是否定义及是否存在值。
- 支持配置重试次数和重试间隔；默认最多尝试 2 次。
- 无会话或发送失败时缓存，`S6F11SpoolReplayService` 定时批量补发。
- 当前缓存上限初始化为 1000 条；不应将其等同于完整 SEMI 标准 Spooling 管理功能。
- 补发路径会按 Host 绑定展开报告，与实时通用上报路径的行为存在差异。

### 2.6 报警

- gRPC `ReportAlarm` 接收报警，更新内存状态并写入报警历史。
- 报警文本缺省时通过 `AlarmCode.csv` 补充。
- 存在 SECS 会话且报警允许上报时发送 `S5F1`，具备重试发送逻辑。
- Host 可通过 `S5F3` 配置报警使能。
- `S5F5` 当前查询运行时激活报警集合，而不是完整静态报警字典。
- `ClearAlarm`、`QueryActiveAlarms` 虽在外发 proto 中定义，但当前客户端封装未形成相应调用链。

### 2.7 远程命令与设备控制

已接入以下命令到 EFEM gRPC 的转换：

| RCMD | 对应设备调用 | 说明 |
| --- | --- | --- |
| `START` | `StartMeasurement` | 无参触发，当前忽略 START 参数 |
| `STOP` | `StopMeasurement` | 携带晶圆上下文 |
| `PAUSE` | `PauseMeasurement` | 携带晶圆上下文 |
| `RESUME` | `ResumeMeasurement` | 携带晶圆上下文 |
| `PPSELECT` | `RecipeSelect` | 传递配方、端口、批次、模式和槽位 |

- `S2F41` 支持命令规范化、远程在线检查、短时幂等以及参数级错误确认。
- `PPSELECT` 要求 `PORTID`，同时兼容 `PORT`、`LOADPORT` 参数名；LOTID 可从端口上下文补充。
- `S2F49` 已接入相同命令分发，但未完整复用 `S2F41` 的权限和幂等控制。
- EFEM 地址通过 Nacos 发现，支持备用地址及调用超时配置。
- 当前客户端会逐个调用解析到的地址；不是明确的单实例负载均衡/故障转移模型。
- 多个客户端发送方法会记录并吞掉 RPC 异常，且不将非零业务返回码转为失败，因此 Host 收到成功 ACK 不代表设备必然执行成功。

### 2.8 载具、Process Job 与 Control Job

- `S3F17` 已有解析、动作分派和 `S3F18` 应答路径，但当前不完整，详见 SF 表及限制。
- `S16F15` 解析 DATAID、PJID、MF、Carrier/槽位、Recipe、自动启动标记及暂停事件列表。
- 接受的 Process Job 会转为运行时计划缓存，不直接启动设备。
- `S14F9` 解析并校验 Control Job、关联 PJ 及 Carrier 交集后缓存计划。
- `S16F15`、`S14F9` 具备短时防重和最小接受/拒绝 ACK。
- 上述属于作业创建与关联的最小实现，不是完整 E40/E94 生命周期。
- 源码注释提到通过 `ReportRFID` 在载具到达时触发调度，但当前服务端 proto 与 `ReportGrpc` 未暴露该 RPC，不能认定自动调度链路已闭环。

### 2.9 配方读取

- `S7F19/S7F20` 返回配方目录下的一级文件夹名称。
- `S7F5/S7F6` 将指定配方目录递归打包为 ZIP，以二进制 PPBODY 返回。
- `S7F25/S7F26` 读取 JSON 并返回格式化配方。
- 基础路径校验防止 PPID 含路径片段。
- 普通配方/目录根路径为 `D:\GY-Recipes\Recipes`，格式化配方根路径为 `D:\GY\Recipes`，目前为硬编码且不一致。
- 未注册配方下载、删除等完整管理业务。

### 2.10 时间同步、追溯与调试

- `S2F31/S2F32` 接收并保存 Host 时间；当前不修改 Windows 系统时钟。
- EF Core + MySQL：`alarm_history` 保存报警历史，`secs_interaction_history` 保存关键消息与 ACK 信息。
- 关键入站白名单包含通信、时间、远程命令、载具、作业、事件配置、报警和配方请求；不是所有 SF 的全量审计。
- `S2F42`、`S2F50` 及 `S6F11` 有出站追溯调用。
- 支持按日期写文件日志，以及 SECS SML、连接状态和 gRPC 调试日志展示。
- `SecsHandlerRuntimeState` 使用 `secs-event-runtime-state.json` 快照，支持启动恢复和文件变更重载；运行态代码包含报告、事件、端口及作业等状态管理。
- 交互式运行显示 WinForms 调试窗口；非交互式运行不创建窗口。

## 3. SF 支持范围与口径

### 3.1 统计规则

以 `src/SECSGrpcService/Program.cs` 注册的 Handler 及其 `SupportedMessages` 为准：

- **22 类 Host 入站主消息**，对应 **22 组请求/应答**。
- **2 类设备主动上报主消息**：`S5F1`、`S6F11`，分别使用 `S5F2`、`S6F12` 确认链路。
- 请求、应答和上报合计涉及 **48 个不同 SF 编号**；这不是 48 个独立入站业务入口。
- “已接入”表示存在注册路由及处理逻辑，不代表所有边界、标准字段、权限或实机执行均已验证。
- “部分实现”表示已存在可达代码，但存在明确的闭环缺口。

### 3.2 Host → Equipment 请求/应答清单

下表主消息方向均为 Host → Equipment，应答方向均为 Equipment → Host。

| 请求 SF | 应答 SF | 功能 | 状态与当前边界 |
| --- | --- | --- | --- |
| `S1F1` | `S1F2` | 通信探测 Are You There | 已接入；返回型号和软件版本 |
| `S1F3` | `S1F4` | 查询设备状态变量 | 已接入；指定/全部 SVID，PLC 读取取决于实际依赖注入 |
| `S1F11` | `S1F12` | 查询状态变量名称 | 已接入；返回名称，单位目前为空 |
| `S1F13` | `S1F14` | 建立通信 | 已接入；实际接受依据主要为载荷合法性，注释中的在线/运行态条件未启用 |
| `S1F15` | `S1F16` | 请求离线 | 已接入；切换为 OffLine，重复离线接受 |
| `S1F17` | `S1F18` | 请求上线 | 已接入；当前仅从 OffLine 切换到 OnLineLocal，已在线则拒绝 |
| `S2F13` | `S2F14` | 查询设备常量值 | 已接入；运行值、设备属性或默认值 |
| `S2F29` | `S2F30` | 查询设备常量定义 | 已接入；范围和单位等元数据仍为空值 |
| `S2F31` | `S2F32` | Host 时间同步 | 最小实现；保存时间，不设置系统时钟 |
| `S2F33` | `S2F34` | 定义/删除报告 | 已接入；支持 RPTID → VID 配置，离线拒绝 |
| `S2F35` | `S2F36` | 绑定事件报告 | 已接入；校验 CEID、RPTID，离线拒绝 |
| `S2F37` | `S2F38` | 启用/禁用事件 | 已接入；支持全部/指定事件，离线拒绝 |
| `S2F41` | `S2F42` | 远程命令 | 已接入；START/STOP/PAUSE/RESUME/PPSELECT，远程在线门禁与短时防重 |
| `S2F49` | `S2F50` | 增强远程命令 | 部分实现；已分发上述命令，权限、防重和扩展语义未完整实现 |
| `S3F17` | `S3F18` | 载具动作请求 | 部分实现；部分动作能分发，但最终应答仍固定 NAK=1，未知动作直接返回 |
| `S5F3` | `S5F4` | 配置报警发送使能 | 已接入；支持指定报警或全部报警 |
| `S5F5` | `S5F6` | 报警列表查询 | 已接入；当前来源为运行时激活报警 |
| `S7F5` | `S7F6` | 请求配方内容 | 已接入；读取配方文件夹并返回 ZIP 二进制 |
| `S7F19` | `S7F20` | 请求配方目录 | 已接入；返回一级目录名称 |
| `S7F25` | `S7F26` | 请求格式化配方 | 已接入；从固定路径读取 JSON |
| `S14F9` | `S14F10` | 创建 Control Job | 最小实现；校验并缓存 CJ/PJ/Carrier 关联，不直接执行 |
| `S16F15` | `S16F16` | 创建 Process Job | 最小实现；解析关键字段并缓存计划，不直接执行 |

### 3.3 Equipment → Host 主动上报清单

| 主消息 | Host 应答 | 功能 | 当前边界 |
| --- | --- | --- | --- |
| `S5F1` | `S5F2` | 报警发生/状态上报 | 由 `ReportAlarm` 触发；先存储，再受报警使能和会话可用性控制，具备重试逻辑 |
| `S6F11` | `S6F12` | 事件/状态/检测结果参数上报 | 由 `ReportEvent` 等触发；报告配置门禁、重试、缓存、后台补发 |

`S6F11` 发送器当前以应答头为 `S6F12` 判断成功，未进一步校验其 ACK 数据项是否为接受。

### 3.4 有基础文件但不代表已注册业务

例如仓库存在 `S1F1`、`S1F3`、`S1F13`、`S2F33`、`S2F37`、`S2F41` 的构建器，以及部分应答/上报解析器。这些可用于工具、示例或复用，不能据此扩大主服务的 SF 支持清单。

以下常见业务当前未在主服务注册对应入站处理：

- `S2F15/S2F16`：设备常量修改。
- `S5F7/S5F8`：启用报警列表查询。
- `S7F1/S7F2`、`S7F3/S7F4`、`S7F17/S7F18`：配方传输许可、下载、删除等。
- 完整的 Process Job / Control Job 查询、修改、取消及生命周期控制。
- 标准 Spooling 的完整 Host 管理接口。

## 4. 当前 gRPC 接口

### 4.1 对外提供的接收接口

实际编译的服务端协议文件是 `src/SECSGrpcService/Protos/server.proto`，服务为 `GY.SECSserver.ReportGrpcService`。

| RPC | 当前用途 |
| --- | --- |
| `ReportEvent` | EventName → CEID，参数 → VID，发送 `S6F11` |
| `ReportAlarm` | 保存报警、更新报警状态，并尝试发送 `S5F1` |
| `RequestOnlineStatus` | 通过 `source=remote/local` 在本地在线和远程在线间切换，并尝试发送状态事件 |

注意：源码注释提及的 `efem.proto` 不是当前项目实际引用的服务端文件；启动提示中的 `ResultReport` 也不是当前 proto 暴露的 RPC。检测结果需要按通用事件参数契约通过 `ReportEvent` 上报。

### 4.2 主动调用设备的接口

客户端协议为 `src/SECSGrpcService/Protos/secs.proto`，服务为 `GY.EFEM.EFEM`。

| RPC | 主服务调用状态 |
| --- | --- |
| `StartMeasurement` | 已接入远程命令 |
| `StopMeasurement` | 已接入远程命令 |
| `PauseMeasurement` | 已接入远程命令 |
| `ResumeMeasurement` | 已接入远程命令 |
| `RecipeSelect` | 已接入 PPSELECT |
| `TakeLoadPort` | 已接入部分 `S3F17` 装载/卸载动作 |
| `LoadCassette` | proto 已定义；当前装载分发实际使用 `TakeLoadPort` |
| `ClearAlarm` | proto 已定义；当前封装未接入调用 |
| `QueryActiveAlarms` | proto 已定义；当前封装未接入调用 |

## 5. 已配置的事件范围

`src/Messages/Config/CEID.csv` 当前定义 **38 个事件（1001～1038）**：

| CEID 范围 | 事件类别 |
| --- | --- |
| 1001～1004 | Carrier 到达、移除、装载、卸载 |
| 1005～1006 | Mapping 开始、完成 |
| 1007～1010 | 整盒及单片晶圆检测开始、完成 |
| 1011～1014 | 机械臂搬运、工位检测开始、完成 |
| 1015～1018 | 对位、Wafer ID、Notch Step1/Step2 拍照完成 |
| 1019～1028 | 边缘、表面光照及表面 Step1/Step2/Step3 拍照开始、完成 |
| 1029～1030 | 在线状态、远程状态变化 |
| 1031～1032 | 报警发生、清除 |
| 1033 | 设备运行状态变化 |
| 1034～1038 | 设备停止、启动、复位、暂停、恢复 |

这些是可映射的事件定义，不代表每个事件都由本项目自动检测并触发。设备通常需要调用 `ReportEvent`，并确保 Host 完成报告定义、事件绑定和使能。`ReportAlarm` 不会自动同时生成 AlarmRaised/AlarmCleared 的 `S6F11`。

## 6. 生产流程与当前完成程度

### 6.1 预期最小流程

1. 建立 HSMS 连接，完成通信与上线；设备切换为远程在线。
2. Host 通过 `S2F33` → `S2F35` → `S2F37` 配置事件报告。
3. 设备先报告 Carrier/Mapping 结果，通过 `ReportEvent` 转为 `S6F11`。
4. Host 按业务顺序下发 `S3F17` → `S16F15` → `S14F9` → `S2F41` 等控制消息。
5. 设备执行检测，使用 `ReportEvent` 携带检测过程、结果参数，经 `S6F11` 上报。
6. 异常通过 `ReportAlarm` 上报，保存历史并发送 `S5F1`。

### 6.2 当前判断

- 事件配置、通用上报、远程命令转换、配方读取和报警追溯具备主要代码路径。
- PJ/CJ 已具备创建解析及计划存储，但未形成完整自动执行状态机。
- `S3F17` 应答与实际动作不一致，不能作为已完成的载具控制闭环。
- Host ACK 与设备 RPC 执行结果之间尚未严格关联，仍需设备侧联调确认。

## 7. 部署及能力边界提醒

1. **载具分发依赖缺口**：`CarrierPrimaryMessageHandler` 声明了 `_commandDispatcher`，但当前没有构造函数注入赋值；载具调用存在空引用风险。
2. **载具应答缺口**：`ProceedWithCarrier2nd` 目前不执行动作；支持分支执行后仍返回 `S3F18` NAK=1，默认分支不应答。
3. **增强命令缺口**：`S2F49` 当前直接按原始 RCMD 分派，没有实际执行在线门禁或幂等保护。
4. **作业防重边界**：`S2F41`、`S16F15`、`S14F9` 的防重记录先于部分校验/执行；重复请求直接成功应答，不能视为可靠的执行结果缓存。
5. **多报告差异**：通用实时事件仅使用首个 RPTID，补发路径才重新展开多个报告。
6. **数据库依赖**：DbContext 工厂仅在配置 MySQL 连接串时注册，但报警/交互存储构造函数直接依赖该工厂；未配置时不能保证服务正常解析并启动。
7. **追溯分类边界**：当前有报警来源、SF、消息和时间等字段，但并非完整的 Equipment/Host/中间件三级分类审计模型。
8. **报警返回语义**：`ReportAlarm` 在报警禁发或 SECS 会话不可用时仍可返回 OK，表示接收处理，不是 Host 已确认报警。
9. **标准兼容性**：作业 ACK、配方 ZIP 内容和错误应答等采用当前项目约定，需要与 Host 确认结构及字段语义。
10. **权限并非统一**：监听入口没有统一拦截全部离线非通信消息，各 Handler 的权限策略需分别核验。

以上是本次文档盘点发现的现状，未在本次任务中修改业务实现。

## 8. 主要核对入口

- 服务注册：`src/SECSGrpcService/Program.cs`
- SECS 接收和路由：`src/SECSGrpcService/Services/SecsPrimaryMessageListenerService.cs`
- 入站 SF 声明：`src/Messages/Handlers/SECShandler/Handlers`
- 具体 SF 实现：`src/Messages/Handlers/SECShandler/Functions`
- gRPC 接收：`src/SECSGrpcService/Services/ReportGrpc.cs`
- 主动事件发送：`src/SECSGrpcService/Services/ActiveSxFyDispatcher.cs`
- 补发任务：`src/SECSGrpcService/Services/S6F11SpoolReplayService.cs`
- 命令转换：`src/SECSGrpcService/Services/GrpcMeasurementDispatcher.cs`
- EFEM 调用：`src/SECSGrpcService/Services/SecsEfemGrpc.cs`
- 运行时状态：`src/SECSGrpcService/Services/SecsHandlerRuntimeState.cs`
- 数据库模型：`src/SECSGrpcService/Services/TraceabilityDbContext.cs`
- 协议文件：`src/SECSGrpcService/Protos/server.proto`、`src/SECSGrpcService/Protos/secs.proto`

后续新增 SF 时，应同时更新路由、解析/构建、业务执行、权限、错误应答、追溯及本文支持状态；仅增加模型或解析器不应计为已支持。
