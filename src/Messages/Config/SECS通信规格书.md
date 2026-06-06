# Fab 对接用 SECS/GEM 通信规格书（最小生产版）

**文档版本**：v1.0  
**项目**：secs-gem（Equipment↔Host 中间件）  
**适用范围**：检测设备最小生产通信闭环（Mapping → 控制命令 → 结果上报）

---

## 1. 目标与范围

本规格书用于 Fab Host 与本设备中间件进行 SECS/GEM 对接，覆盖以下最小生产能力：

1. 建链与基础通信（S1F13/S1F14、S1F1/S1F2）
2. 事件上报配置（S2F33/S2F35/S2F37）
3. 远程命令与作业下发（S2F41、S3F17、S16F15、S14F9）
4. 设备主动上报（S6F11/S6F12）
5. 报警控制与查询（S5F3/S5F4/S5F5/S5F6）
6. 时间同步（S2F31/S2F32）

> 说明：本版本不强制 recipe/PPID/workstation 参与结果上报。

---

## 2. 术语与职责

- **CEID**：设备侧定义的事件 ID（固定能力集合）
- **VID**：设备侧定义的变量 ID（固定能力集合）
- **RPTID**：Host 侧运行时定义与分配（非设备固定值）

**标准模式约定**：
- 设备固定提供 CEID/VID 能力集。
- Host 通过 S2F33 定义 RPTID 与 VID 列表，通过 S2F35 绑定 CEID↔RPTID，通过 S2F37 启用 CEID。
- 设备发送 S6F11 时按 Host 当前绑定动态展开 RPTID，不依赖设备端写死 RPTID。

---

## 3. 设备状态与权限

### 3.1 Online 状态
- `OffLine`
- `OnLineLocal`
- `OnLineRemote`

### 3.2 权限约束
- OffLine：仅通信类消息可用（如 S1F1/S1F13/S1F15/S1F17）。
- OnLineRemote：允许远程生产控制命令（S2F41/S16F15/S14F9 等）。

---

## 4. CEID 定义（设备固定能力）

| CEID | EventName | Category | Description |
|---|---|---|---|
| 1001 | WaferResultReported | Report | 单片检测结果上报 |
| 1002 | MappingCompleted | Report | 载具 Mapping 完成 |
| 1003 | LotCompleted | Report | 整盒晶圆检测完成 |
| 1004 | OnlineStateChanged | State | 在线状态变化 |
| 1005 | AlarmRaised | Alarm | 报警发生 |
| 1006 | AlarmCleared | Alarm | 报警恢复 |
| 1007 | EquipmentStateChanged | State | 设备运行状态变化 |
| 1008 | LotAborted | Exception | 批次中止 |

---

## 5. VID 定义（设备固定能力）

| VID | Name | 用途 |
|---|---|---|
| 1000 | LOTID | 批次号 |
| 1002 | RFID | 载具标签 |
| 1003 | SLOTID | 槽位号 |
| 1004 | SLOTSLIST | 槽位列表 |
| 1006 | PORTID | LoadPort 编号 |
| 1007 | RESULT | 结果（Pass/Fail/Error） |
| 2001 | MODE | 运行模式 |
| 3001 | FROM_STATE | 状态切换前 |
| 3002 | TO_STATE | 状态切换后 |
| 3003 | WORKSTATION | 工位状态（可选） |
| 3004 | TRIGGER | 触发来源 |
| 3005 | ONLINE_STATE | 当前在线状态（S1F3 查询） |

---

## 6. 消息与ACK定义（最小集合）

### 6.1 建链与通信
- Host→Eqp：S1F13（W）
- Eqp→Host：S1F14
  - `COMMACK=0` 接受
  - `COMMACK=1` 拒绝

### 6.2 报告配置
- S2F33 / S2F34：Define Report
  - `DRACK=0` 成功，`1` 内部错误，`2` 格式错误
- S2F35 / S2F36：Link Event Report
  - `LRACK=0` 成功，`1` 内部错误，`2` 格式错误，`4` CEID无效，`5` RPTID未定义
- S2F37 / S2F38：Enable Event Report
  - `EAC=0` 成功，`1` CEID无效，`2` 格式或内部错误

### 6.3 远程命令
- S2F41 / S2F42
  - `HCACK=0` 成功
  - `HCACK=1` 命令不支持/参数问题
  - `HCACK=2` 执行失败或状态不允许

### 6.4 报警与时间同步
- S5F3 / S5F4：报警使能
- S5F5 / S5F6：报警查询
- S2F31 / S2F32：时间同步
  - `TIACK=0` 成功，`TIACK=2` 格式/内部错误

### 6.5 主动上报
- Eqp→Host：S6F11（W）
- Host→Eqp：S6F12

---

## 7. 标准生产流程（最小闭环）

1. S1F13/S1F14 建链  
2. S2F33 定义报告  
3. S2F35 绑定 CEID↔RPTID  
4. S2F37 启用事件  
5. S2F41(PPSELECT) 运行前准备  
6. S3F17 下发 LOT+SlotMap  
7. S16F15/S14F9 下发作业  
8. S2F41(START) 启动  
9. 设备 S6F11 上报 Mapping  
10. 设备 S6F11 上报单片结果（循环）  
11. 设备 S6F11 上报整盒完成

---

## 8. S6F11 上报策略（实现约束）

1. **门禁规则**（必须满足）：
   - CEID 已启用（S2F37）
   - CEID 已绑定至少一个 RPTID（S2F35）
   - 绑定的 RPTID 均已定义（S2F33）

2. **RPTID 生成规则**：
   - 设备按 Host 运行态绑定动态展开 RPTID。
   - 非写死 RPTID 模式。

3. **断线保障**：
   - 会话不可用时 S6F11 入本地 spool 队列。
   - 后台服务自动补发（可配置批量与周期）。

---

## 9. 多 LoadPort 上下文规则

设备维护 Port 维度运行态上下文：
- `PortId`
- `CarrierId`
- `LotId`
- `SlotsList`

使用规则：
- Mapping 阶段写入上下文。
- 结果上报时优先使用请求字段，缺失时按 Port/Lot 上下文兜底。
- LotCompleted 后清理对应 Port 上下文，防止串批次。

---

## 10. Host 对接要求

1. S2F35 中 CEID/RPTID 建议使用 U4 类型（兼容 U1/U2/U4）。
2. 建议严格按 `S2F33 → S2F35 → S2F37` 顺序配置。
3. 若仅启用部分事件，Host 需确保对应 RPTID/VID 定义完整。
4. 本版本可不强制 PPID/Recipe/Workstation 字段。

---

## 11. 版本与变更

- CEID/VID 能力集由设备版本管理。
- RPTID 由 Host 运行时管理。
- 任何 CEID/VID 变更需同步更新：
  - `CEID.csv`
  - `VID.csv`
  - `流程.md`
  - 本规格书版本号

---

## 12. 附录

- CEID配置：`src/Messages/Config/CEID.csv`
- VID配置：`src/Messages/Config/VID.csv`
- 流程样例：`src/Messages/Config/流程.md`
