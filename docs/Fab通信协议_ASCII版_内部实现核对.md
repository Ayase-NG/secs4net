# Equipment–Fab SECS/HSMS 通信协议（ASCII 内部实现核对版）

文档编号：GWM-SECS-FAB-ASCII-001-INTERNAL  
版本：V1.0（代码静态核对稿，仅供内部）  
核对日期：2026-11-04  
对应协议：`Fab通信协议_ASCII版.md`  
代码基线：`efem` HEAD `80d8386`；`secs-gem` HEAD `5608f2f`，以核对时工作区实际文件为准（包含未提交文件）。

> 本文件标注当前实现差距，不是对外接口承诺，也不是设备实测或现场验收报告。用户提及的 CIED 在本文统一按协议术语 CEID（事件编号）处理。原协议的 SF 结构、ASCII 编码及连接约定仍见对应协议；本文件不得被解释为新增已实现功能。

## 1. 核对口径与结论

核对链路：EFEM 业务动作 → gRPC 请求 → 名称/编号映射 → VID 运行态缓存 → Host 动态报告绑定 → S6F11。仅有 CSV 定义、构造器、通用发送接口、注释或测试路径，不判定为生产业务已实现。

| 标记 | 判定口径 |
| --- | --- |
| 已接入 | 找到实际业务触发/写值路径；仍需满足远程模式、报告配置、使能及通信条件并完成实测 |
| 部分实现 | 有实际调用，但字段、值域、触发覆盖或业务语义存在缺口 |
| 静态/配置 | 能从设备内存常量或 CSV 返回值，但不代表有实时采集或业务同步 |
| 尚未实现 | 当前核对范围未找到生产业务触发或真实写值路径；不代表整个物理动作或 UI 功能不存在 |

### 1.1 CEID 汇总

- **已接入 1 项**：1006 `MappingCompleted`。
- **部分实现 3 项**：1008 `CarrierProcessed`、1014 `WorkerProcessed`、1029 `OnlineStateChanged`。
- **尚未实现 34 项**：其余事件，逐项见第 3 节。
- EFEM 侧 `ReportEventAsync` 只有三处有效调用：测量流程结束、自动 Mapping 结果、工位结果。SECS 服务的在线状态事件另有独立发送路径。

### 1.2 优先阻塞项

1. `SendResultReport` 发送 `SlotId`，字典要求 `WaferSlot`，VID 3005 尚未接入；映射不忽略词义差异，也没有已发现的别名转换。
2. VID 3007 `WaferResult` 实际为 `bool.ToString()` 的 `True/False`，与协议目标 `OK/NG/Aborted` 不符；`reason` 参数未随请求发送。
3. CEID 1008 在远程启动的一轮测量流程收尾时发送，缺少 Port/Carrier/时间/汇总字段，不是可靠的逐盒完成记录；停止后的收尾也可能走到此调用。
4. 工位结果和整轮结束上报均未等待 `ReportEventAsync` 的结果、未检查 `Success`；不能将“执行到调用处”视为 Host 已收到。
5. 通用上报只取首个绑定 RPTID；全局缓存可能携带其他端口/晶圆的旧值，尚未形成每次事件的完整快照。
6. 在线状态 VID 1002 有相互冲突的写值：运行态维护 `local/remote`，gRPC 又写 `Local/Remote`，S1F17 成功路径写 `Online`；枚举应统一。

## 2. 代码依据索引

以下路径分别相对于各仓库根目录，行号为本次核对时定位，后续以方法名为准。

| 索引 | 仓库、文件与位置 | 核对内容 |
| --- | --- | --- |
| E1 | `efem/GY.EFEM.UI/EFEMWindowViewModel.cs:2196–2202` | 远程来源的测量流程结束发送 `CarrierProcessed`，无业务参数 |
| E2 | `efem/GY.EFEM.UI/EFEMWindowViewModel.cs:4038–4217`，`ReportAutoMappingResultAsync` | 远程模式下监听 Mapping 信号上升沿，刷新槽位后发送 `MappingCompleted`，检查应答 |
| E3 | `efem/GY.EFEM.UI/EFEMWindowViewModel.cs:5851–5867`，`SendResultReport` | 远程模式下发送 `WorkerProcessed`，携带 `WorkStation/WaferResult/SlotId/WaferId` |
| E4 | `efem/GY.EFEM.UI/EFEMWindowViewModel.cs:4654,5439,5455,5472–5473` | Edge、正面、背面结果调用 `SendResultReport` |
| E5 | `efem/GY.EFEM.UI/EFEMWindowViewModel.cs:1164–1179`，`PreprocessAlarm` | 远程报警走 `ReportAlarmAsync`，不是报警 CEID 的通用事件调用 |
| E6 | `efem/GY.EFEM.UI/EFEMWindowViewModel.cs:6061,6076` | UI 请求 SECS 服务切换 remote/local |
| S1 | `secs-gem/src/SECSGrpcService/Services/ReportGrpc.cs:78–151`，`ReportEvent` | 参数先刷新缓存，再校验事件、查绑定，取首个 RPTID，发送 S6F11 并返回结果 |
| S2 | `secs-gem/src/SECSGrpcService/Services/ReportGrpc.cs:246–361` | `RequestOnlineStatus` 成功切换时调用 `TrySendOnlineStateChangedByRuntimeBindingAsync`，发送 CEID 1029 |
| S3 | `secs-gem/src/SECSGrpcService/Services/ReportGrpc.cs:369–435` | 按名称写缓存、按 RPTID 的 VID 列表读取缓存；缺值不等于已采集 |
| S4 | `secs-gem/src/SECSGrpcService/Services/VidMap.cs:10,51–59` | VID 名称匹配忽略大小写，但不把 `SlotId` 转为 `WaferSlot` |
| S5 | `secs-gem/src/SECSGrpcService/Services/SecsHandlerRuntimeState.cs:1019–1070` | 在线状态变化刷新 `OnlineStatus/RemoteStatus` |
| S6 | `secs-gem/src/Messages/Handlers/SECShandler/Functions/CommunicationSxFyFunctions.cs:181–188` | S1F17 上线时写 VID 1002 为 `Online`，与该 VID 的远程状态语义冲突 |
| S7 | `secs-gem/src/Messages/Handlers/SECShandler/Functions/EventReportSxFyFunctions.cs:243–280` | S2F13 优先运行态值，软件/硬件版本读设备内存，其余回退 CSV 默认值 |
| S8 | `secs-gem/src/SECSGrpcService/Services/SecsHandlerRuntimeState.cs:475–476` | 型号与软件版本当前具有代码初始化值，不等于自动提取部署版本 |
| S9 | `secs-gem/src/Messages/Config/CEID.csv`、`VID.csv` | 事件与变量定义全集；本文件覆盖 38 个 CEID、47 个 VID |
| S10 | `secs-gem/src/SECSGrpcService/Services/ReportGrpc.cs:155` 起，`ReportAlarm` | 报警入库、激活报警状态维护及 S5F1 路径；未发现写 VID 1004 或触发 1031/1032 |

## 3. CEID 实现状态（38 项）

“尚未实现”指尚未接入相应 SECS 业务事件；设备已有装载、对位、拍照等功能不能替代相应事件的主动上报。

| CEID | 事件名 | 状态 | 当前实现或缺口 |
| --- | --- | --- | --- |
| 1001 | CarrierArrived | 尚未实现 | 未发现载具放上时的生产事件调用；仅有字典定义 |
| 1002 | CarrierRemoved | 尚未实现 | 未发现载具取走时的事件调用 |
| 1003 | CarrierLoaded | 尚未实现 | 未发现装载完成事件调用；旧报告构造器不能当作此事件的生产接入依据 |
| 1004 | CarrierUnloaded | 尚未实现 | 未发现卸载完成事件调用 |
| 1005 | MappingStart | 尚未实现 | 未发现 Mapping 开始事件调用 |
| 1006 | MappingCompleted | 已接入 | E2：仅远程模式的自动 Mapping 信号路径；带 `PortID/CarrierID/WaferSlotList`，等待并检查应答；手动/本地 Mapping 不能据此认定已覆盖 |
| 1007 | CarrierProcessing | 尚未实现 | 未发现逐盒检测开始事件调用 |
| 1008 | CarrierProcessed | 部分实现 | E1：远程启动的一轮流程收尾发送，无参数，未等待结果；逐盒粒度、停止/异常语义及数据快照未补齐 |
| 1009 | WaferProcessing | 尚未实现 | 未发现单片开始事件调用 |
| 1010 | WaferProcessed | 尚未实现 | 未发现单片汇总完成事件调用；1014 工位完成不能替代它；原协议示例仅为目标结构 |
| 1011 | RobotProcessing | 尚未实现 | 未发现搬运开始事件调用 |
| 1012 | RobotProcessed | 尚未实现 | 未发现搬运结束事件调用 |
| 1013 | WorkerProcessing | 尚未实现 | 未发现工位检测开始事件调用 |
| 1014 | WorkerProcessed | 部分实现 | E3/E4：Edge/正面/背面结果上报已调用；槽位名不匹配、结果枚举不符，未等待应答，缺少端口/载具上下文 |
| 1015 | AlignmentCompleted | 尚未实现 | 未发现对位完成事件调用 |
| 1016 | WaferIdCameraCompleted | 尚未实现 | 未发现 ID 拍照完成事件调用；结果参数中带 WaferId 不等于触发本事件 |
| 1017 | NotchS1Completed | 尚未实现 | 未发现 Notch Step1 完成事件调用 |
| 1018 | NotchS2Completed | 尚未实现 | 未发现 Notch Step2 完成事件调用 |
| 1019 | EdgeProcessing | 尚未实现 | 未发现边缘拍照开始事件调用 |
| 1020 | EdgeProcessed | 尚未实现 | 未发现独立边缘拍照完成事件调用；现有 Edge 结果走 1014 |
| 1021 | SurfaceLightProcessing | 尚未实现 | 未发现表面光照拍照开始事件调用 |
| 1022 | SurfaceLightProcessed | 尚未实现 | 未发现表面光照拍照完成事件调用 |
| 1023 | SurfaceS1Processing | 尚未实现 | 未发现表面 Step1 开始事件调用 |
| 1024 | SurfaceS1Processed | 尚未实现 | 未发现表面 Step1 完成事件调用 |
| 1025 | SurfaceS2Processing | 尚未实现 | 未发现表面 Step2 开始事件调用 |
| 1026 | SurfaceS2Processed | 尚未实现 | 未发现表面 Step2 完成事件调用 |
| 1027 | SurfaceS3Processing | 尚未实现 | 未发现表面 Step3 开始事件调用 |
| 1028 | SurfaceS3Processed | 尚未实现 | 未发现表面 Step3 完成事件调用 |
| 1029 | OnlineStateChanged | 部分实现 | S2：设备侧 gRPC 的 local/remote 成功切换时尝试发送，带 ChangeTrigger；未发现覆盖所有 Host 上下线、断连等变化的统一事件入口 |
| 1030 | RemoteStateChanged | 尚未实现 | local/remote 变化目前复用 1029，未发现独立发送 1030 |
| 1031 | AlarmRaised | 尚未实现 | E5/S10 有 S5F1 报警通道，但未发现本 CEID 的 S6F11 触发 |
| 1032 | AlarmCleared | 尚未实现 | 未发现报警清除时本 CEID 的 S6F11 触发；与本地清除/报警表处理分开判定 |
| 1033 | EquipmentStateChanged | 尚未实现 | 未发现设备运行状态变化事件调用 |
| 1034 | DeviceStop | 尚未实现 | 停止控制已存在，但未发现对应事件调用 |
| 1035 | DeviceStart | 尚未实现 | 启动控制已存在，但未发现对应事件调用 |
| 1036 | DeviceReset | 尚未实现 | 复位控制已存在，但未发现对应事件调用 |
| 1037 | DevicePause | 尚未实现 | 未发现对应事件调用 |
| 1038 | DeviceResume | 尚未实现 | 未发现对应事件调用 |

## 4. VID 实现状态（47 项）

以下“值类型”均按本协议采用 ASCII A。CSV 原 Format 不作为本版自动转换依据。能查询到变量定义、默认值或缓存值，不意味着已获取当前设备实测值；S1F3 与事件报告的取值路径也不能等同。

### 4.1 SVID（24 项）

| VID | 参数名 | 状态 | 当前实现或缺口 |
| --- | --- | --- | --- |
| 1001 | OnlineStatus | 已接入 | S5：随设备在线状态刷新 `online/offline`；事件触发覆盖仍受 1029 的限制 |
| 1002 | RemoteStatus | 部分实现 | S5 维护 `local/remote`，S2 又写 `Local/Remote`，S6 写 `Online`；值域和语义冲突待统一 |
| 1003 | EquipmentStatus | 尚未实现 | 未发现 EFEM 实际设备状态同步写入；CSV 默认 `unknown` 不代表实时状态 |
| 1004 | AlarmCode | 尚未实现 | 有 S5F1/报警表不等于写入此 VID；未发现相应运行态写值，默认 `0` 不代表无报警 |
| 1005 | ChangeTrigger | 部分实现 | S2 在查到 1029 绑定后写 `Device`；尚非所有状态变化统一维护的触发来源 |
| 1006 | LP1CarrierID | 尚未实现 | E2 写通用 DVID 3015，不写本端口专属 SVID |
| 1007 | LP2CarrierID | 尚未实现 | 未发现端口专属载具 ID 同步 |
| 1008 | LP3CarrierID | 尚未实现 | 未发现端口专属载具 ID 同步；还须核实现场端口配置 |
| 1009 | LP4CarrierID | 尚未实现 | 未发现端口专属载具 ID 同步；还须核实现场端口配置 |
| 1010 | LP1CarAccessStatus | 尚未实现 | 未发现载具状态写入；CSV 默认 `10` 不是实时接入证明 |
| 1011 | LP2CarAccessStatus | 尚未实现 | 未发现载具状态写入 |
| 1012 | LP3CarAccessStatus | 尚未实现 | 未发现载具状态写入 |
| 1013 | LP4CarAccessStatus | 尚未实现 | 未发现载具状态写入 |
| 1014 | LP1MapSotList | 尚未实现 | E2 写 3006 的存在槽位号列表，不写本字段的逐槽 `1/0/2` 状态文本 |
| 1015 | LP2MapSotList | 尚未实现 | 未发现端口专属 Mapping 状态写入 |
| 1016 | LP3MapSotList | 尚未实现 | 未发现端口专属 Mapping 状态写入 |
| 1017 | LP4MapSotList | 尚未实现 | 未发现端口专属 Mapping 状态写入 |
| 1018 | SlotState | 尚未实现 | UI 槽位状态已有变化，但未发现同步为此 VID 的业务路径 |
| 1019 | SpoolCountActual | 尚未实现 | 原对外协议未收录，CSV 中有定义；虽有事件缓存机制，未发现缓存数量同步到本 VID，不应宣称实时可查 |
| 1020 | EdgeSlotId | 尚未实现 | E3 的 `SlotId` 未映射此字段，未发现独立写值 |
| 1021 | EdgeResult | 尚未实现 | Edge 当前走通用 3007，不写本字段 |
| 1022 | SurfaceSlotId | 尚未实现 | 未发现表面工位槽位号写值 |
| 1023 | FrontSurfaceResult | 尚未实现 | 正面当前走通用 3007，不写本字段 |
| 1024 | BackSurfaceResult | 尚未实现 | 背面当前走通用 3007，不写本字段 |

说明：本表保留配置中 `LPxMapSotList` 的原拼写，避免把未经实现的命名修订误认为已兼容。

### 4.2 ECID（7 项）

| VID | 参数名 | 状态 | 当前实现或缺口 |
| --- | --- | --- | --- |
| 2001 | SoftwareVersion | 静态/配置 | S7/S8：S2F13 可读设备 `SoftwareRevision`，初始化 `Alpha-1.0.0`；未核实自动与 EFEM 发布版本同步 |
| 2002 | HardwareVersion | 静态/配置 | S7/S8：S2F13 可读设备 `ModelNumber`，初始化 `GWM-300-CW`；不是硬件自动识别 |
| 2003 | MaxThroughput | 静态/配置 | S7：CSV 默认 `70` 可回退返回；不是实时吞吐统计，单位仍需签认 |
| 2004 | DefaultRecipeName | 尚未实现 | 默认值为空，未发现当前默认配方同步到此 VID；PPSELECT 生效不等于已维护本字段 |
| 2005 | FfuSpeed | 尚未实现 | 未发现 FFU 实测值写入，默认值为空 |
| 2006 | AirPressure | 尚未实现 | 未发现气压实测值写入，默认值为空 |
| 2007 | UPS | 尚未实现 | 未发现 UPS 数据写入，默认值为空 |

### 4.3 DVID（16 项）

| VID | 参数名 | 状态 | 当前实现或缺口 |
| --- | --- | --- | --- |
| 3001 | ProcessStartTime | 尚未实现 | 未发现逐盒开始时间参数写入；格式/时区也未落地 |
| 3002 | ProcessEndTime | 尚未实现 | E1 无参数，未写结束时间 |
| 3003 | TotalProcessDuration | 尚未实现 | 未发现逐盒时长写入；单位和文本结构待定义 |
| 3004 | WaferID | 已接入 | E3 写 `WaferId=slot.SlotWaferId`，S4 忽略大小写可映射；实际非空和 OCR 质量仍需联调 |
| 3005 | WaferSlot | 尚未实现 | E3 写 `SlotId`，不匹配字典 `WaferSlot`，当前没有接入本 VID |
| 3006 | WaferSlotList | 已接入 | E2：将 `SlotResultMode.Unknown` 的槽位 Id 以逗号拼接，如 `1,3,5`；不等于逐槽状态或 PPSELECT 待测选择列表 |
| 3007 | WaferResult | 部分实现 | E3 写 ASCII `True/False`；尚未实现 `OK/NG/Aborted`，当前表达单工位结果而非单片汇总结果 |
| 3008 | DefectScratchCount | 尚未实现 | 未发现缺陷统计写入；UI/数据库有统计不等于已接入此 VID |
| 3009 | DefectParticleCount | 尚未实现 | 未发现颗粒数量写入 |
| 3010 | DefectCloudCount | 尚未实现 | 未发现云点数量写入 |
| 3011 | WorkStation | 已接入 | E3/E4：实际值为 `Edge`、`FrontSurface`、`BackSurface` |
| 3012 | DetectionMode | 尚未实现 | UI/PPSELECT 有 Mode，但未发现写入此 VID |
| 3013 | PortID | 已接入 | E2：Mapping 时写 1-based 十进制字符串；E1/E3 未显式带当前端口，其他事件可能仅读到旧缓存 |
| 3014 | LotID | 尚未实现 | 未发现真实批次号写入；不能以 CarrierID 替代 |
| 3015 | CarrierID | 已接入 | E2：Mapping 时取 CassetteId，空值回退 `CassetteDefaultId`；不是每次结果事件都显式携带 |
| 3016 | RecipeName | 尚未实现 | PPSELECT/槽位配方切换已有业务处理，但未发现当前配方写入本 VID |

## 5. 已接入事件的真实参数清单

| 事件 | 业务请求实际参数 | 可映射 VID | 限制 |
| --- | --- | --- | --- |
| 1006 MappingCompleted | `PortID`、`CarrierID`、`WaferSlotList` | 3013、3015、3006 | 远程自动 Mapping 路径；CarrierID 可能是默认值；等待并检查 ReportReply |
| 1008 CarrierProcessed | 无 | 本次无新增业务 VID | 只可能复用历史缓存；没有对应报告可用值时不能据此认为会发送有效 S6F11 |
| 1014 WorkerProcessed | `WorkStation`、`WaferResult`、`SlotId`、`WaferId` | 3011、3007、3004 | `SlotId` 不映射 3005；结果为 True/False；未检查应答 |
| 1029 OnlineStateChanged | 服务内部提供 `ChangeTrigger=Device`；在线/远程值由运行态维护 | 1005，按绑定可读取 1001/1002 | 仅 local/remote 切换成功路径，且需已有绑定；1002 值域不一致 |

### 5.1 当前可优先联调的配置

Host 可先用 S2F33 定义一个仅含 VID `[3013,3015,3006]` 的报告，再用 S2F35 绑定 CEID 1006，并用 S2F37 启用。在远程模式触发自动 Mapping，核对实际返回的 `(VID, A值)`、槽位列表和 ReportReply；此配置只是建议，不是设备默认报告。

CEID 1014 可核对 3011、3007、3004 当前值，但须按现有 `True/False` 记录差距，不得据此宣布目标结果枚举验收通过。不要将 3005 当作已支持项加入验收预期。

### 5.2 公共链路限制

- `ReportEvent` 在事件映射、绑定、发送成功前就刷新运行态值；缓存最新值不代表该事件成功送达。
- 同一 CEID 只处理首个绑定 RPTID，多报告配置能力不等于多报告均会发送。
- 报告值可能来自先前请求；现有缓存不是按 Port/Carrier/Wafer 隔离的事务快照。缺值可能导致字段缺席，不应盲目按固定位置或固定数量解析。
- 业务模式限制与 SECS 通信在线状态不是同一概念；当前 E2/E3 受 `IsRemoteMode` 限制，E1 由启动来源分支限制。
- S5F1 报警和 S6F11 事件独立；已有报警上报不意味着 1031、1032 或 VID 1004 已接入。
- 已有事件缓存/补发机制不意味着 VID 1019 已维护，也不意味着完全离线、缺绑定或业务侧未调用时仍可补出业务事件。
- 所有已接入项仍需在部署环境验证 Host 定义、事件绑定、事件使能、通信会话、重试及字段实际值；本次只做静态代码核对。

## 6. 内部补齐顺序与验收清单

| 优先级 | 待补齐内容 | 验收目标 |
| --- | --- | --- |
| P0 | 对齐 `SlotId` → `WaferSlot`；统一结果枚举 | 1014 中稳定出现正确 3005；3007 满足协议目标且有异常/中止口径 |
| P0 | 每次结果显式携带端口、载具、晶圆、槽位、配方等上下文，形成事件快照 | 双端口及正背面并行时不串盒、不串片、不复用其他业务旧值 |
| P0 | 结果和完成上报等待应答、检查失败并记录可追溯上下文 | 被门禁拒绝或发送失败时不能记录为成功上报 |
| P0 | 统一 VID 1002 值域并修正 S1F17 写错语义 | 各查询和事件都只返回签认的 local/remote 值 |
| P1 | 逐盒开始/结束、逐片开始/汇总完成以及时间/时长 | 1007–1010 按正确业务粒度触发，正常完成与停止/异常区分明确 |
| P1 | 独立在线/远程/设备状态事件和对应 VID | Host 上下线、设备切换及断连行为有明确覆盖，不将 1029 等同于 1030 |
| P1 | 载具、Mapping 开始、搬运、对位和各拍照阶段事件 | 第 3 节各尚未实现事件有明确触发点、字段及重复触发控制 |
| P1 | 报警发生/清除事件及 VID 1004 同步 | S5F1 与 1031/1032 的关系明确，报警代码与清除状态可核验 |
| P1 | 多 RPTID 全量发送、缓存快照和缺值策略 | Host 绑定多个报告时逐个验证；缺字段不得悄悄用错误历史值替代 |
| P2 | 端口专属 SVID、工位 SVID、缺陷计数、LotID/Recipe/Mode、设备采集常量 | 第 4 节尚未实现项有真实数据源及明确空值、单位、格式定义 |
| P2 | 维护 SpoolCountActual | 断连、入队、重发、清空时 VID 1019 与实际缓存数量一致 |

完成代码补齐后，应更新本文件状态和证据，并保留联调报文/日志；未完成动态验证前不得将“已接入”改写成“验收通过”。
