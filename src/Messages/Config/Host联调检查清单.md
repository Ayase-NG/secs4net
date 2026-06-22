# Host联调检查清单（按当前项目完成度）

> 目标：给 Fab/Host 联调使用，重点覆盖“报告定义、事件绑定、事件启用、作业下发（S16F15/S14F9）”。
> 说明：本文所有流程基于当前代码行为（含运行态内存缓存+快照恢复）。

---

## A. 联调前准备

- [ ] HSMS 网络可达（Host -> 本项目 `SecsListener:IpAddress/Port`）。
- [ ] `SecsListener:Enabled=true`，Host 与设备 Active/Passive 角色匹配。
- [ ] `DeviceId` 与 Host 一致（否则会出现 `Unrecognized Device Id`）。
- [ ] `VID.csv` 已包含联调所需变量（至少 `PORTID/LOTID/RFID/SLOTSLIST/RESULT/...`）。
- [ ] `CEID.csv` 已包含联调事件（当前含 `1001~1008,1010`）。
- [ ] gRPC 下游（EFEM）可用，`SecsDispatch:Grpc:CallTimeoutMs` 已设置。

---

## B. 报告定义 / 事件绑定 / 事件启用（核心）

> 这三步是 S6F11 能否真正发出的前置门禁。

### B1. S2F33 Define Report（定义 RPTID -> VID）

#### 示例 SML
```sml
S2F33 W
<L,2
  <U1 1>                        // DATAID
  <L,2
    <L,2
      <U4 1002>                 // RPTID
      <L,4
        <U4 1006>               // PORTID
        <U4 1000>               // LOTID
        <U4 1002>               // RFID
        <U4 1004>               // SLOTSLIST
      >
    >
    <L,2
      <U4 1010>                 // RPTID (CarrierRemoved)
      <L,3
        <U4 1006>               // PORTID
        <U4 1000>               // LOTID
        <U4 1002>               // RFID
      >
    >
  >
>.
```

#### 期望回包
- [ ] `S2F34`，`DRACK=0`

#### 写入内存后的状态
- [ ] RPTID->VID 定义立即进入运行态缓存（`IReportStorage`）。
- [ ] 同时写入快照文件，进程重启后会恢复。

---

### B2. S2F35 Link Event Report（绑定 CEID -> RPTID）

#### 示例 SML
```sml
S2F35 W
<L,2
  <U1 2>                        // DATAID
  <L,2
    <L,2
      <U4 1002>                 // CEID MappingCompleted
      <L,1 <U4 1002>>           // 绑定 RPTID=1002
    >
    <L,2
      <U4 1010>                 // CEID CarrierRemoved
      <L,1 <U4 1010>>           // 绑定 RPTID=1010
    >
  >
>.
```

#### 期望回包
- [ ] `S2F36`，`LRACK=0`

#### 写入内存后的状态
- [ ] CEID->RPTID 映射立即进入运行态缓存（`IEventLinkStorage`）。
- [ ] 同时写入快照文件，重启可恢复。

---

### B3. S2F37 Enable Event Report（启用 CEID）

#### 示例 SML（仅启用 1002/1010）
```sml
S2F37 W
<L,2
  <B 0x01>                      // CEED=Enable
  <L,2
    <U4 1002>
    <U4 1010>
  >
>.
```

#### 示例 SML（启用全部）
```sml
S2F37 W
<L,2
  <B 0x01>
  <L,0>
>.
```

#### 期望回包
- [ ] `S2F38`，`EAC=0`

#### 写入内存后的状态
- [ ] CEID 启用状态立即进入运行态缓存（`IEventEnableStorage`）。
- [ ] 同时写入快照文件，重启可恢复。

---

## C. 这些配置“什么时候生效”

- `S2F33/S2F35/S2F37` 成功回包后**立即生效**（无需重启）。
- 后续每次发送 S6F11，都会实时检查：
  1. CEID 是否启用
  2. CEID 是否已绑定 RPTID
  3. RPTID 是否已定义
- 任一不满足，S6F11 会被门禁拦截并打日志。

---

## D. 作业下发（S16F15 / S14F9）联调

> 当前项目策略：
> - `S16F15`：缓存 ProcessJob（PJ），不立即执行。
> - `S14F9`：校验并缓存 ControlJob（CJ），建立 CJ-PJ 关联。
> - Carrier 到达（`ReportRFID`）时，根据 `PRPROCESSSTART` 决定是否自动触发 `PPSELECT+START`。

### D1. S16F15 Create Process Job（先下发）

#### 示例 SML（简化）
```sml
S16F15 W
<L,2
  <U4 10>                       // DATAID
  <L,1
    <L,6
      <A "PJ_0001">            // PJID
      <A "0D">                 // MF
      <L,1
        <L,2
          <A "RF100001">       // CarrierId
          <L,3 <U4 1> <U4 3> <U4 7>>
        >
      >
      <L,3 <A "-"> <A "RCP_A"> <A "-">>  // PROCESS_SPEC(RecipeId=RCP_A)
      <BOOLEAN TRUE>            // PRPROCESSSTART=true 自动开始
      <L,0>                     // PRPAUSEEVENT
    >
  >
>.
```

#### 期望回包
- [ ] `S16F16`，`ACK=0`

#### 写入内存后的状态
- [ ] PJ 缓存在运行态（含 CarrierId/Slots/Recipe/PRPROCESSSTART）。
- [ ] 写入快照，重启可恢复。
- [ ] **此时不会立即 START**。

---

### D2. S14F9 Create Control Job（后下发）

#### 示例 SML（简化）
```sml
S14F9 W
<L,3
  <A "Equipment">              // OBJSPEC
  <A "ControlJob">             // OBJTYPE
  <L,1
    <L,4
      <L,2 <A "ObjID"> <A "CJ_0001">>
      <L,2 <A "ProcessingCtrlSpec"> <L,1 <A "PJ_0001">>>
      <L,2 <A "CarrierInputSpec"> <L,1 <A "RF100001">>>
      <L,2 <A "StartMethod"> <BOOLEAN TRUE>>
    >
  >
>.
```

#### 期望回包
- [ ] `S14F10`，`ACK=0`

#### 校验规则（当前代码）
- [ ] `ProcessingCtrlSpec` 中 PJID 必须都已存在（已由 S16F15 缓存）。
- [ ] `CarrierInputSpec` 与关联 PJ 的 Carrier 集必须有交集。

#### 写入内存后的状态
- [ ] CJ 缓存成功后，进入“等待 Carrier 到达”状态。

---

### D3. Carrier 到达触发（ReportRFID）

当设备端上报 `ReportRFID(portId, lotId, RFID, slots)`：

1. 先发 Mapping 事件（通常 CEID=1002，需已完成 B 章节配置）。
2. 再尝试匹配缓存的 CJ/PJ：
   - [ ] 若 `PRPROCESSSTART=true`：自动复用现有链路下发 `PPSELECT` + `START`
   - [ ] 若 `PRPROCESSSTART=false`：仅记录等待，需 Host 后续显式 `S2F41 START`

---

## E. 最小联调通过标准（建议）

- [ ] `S2F33/S2F35/S2F37` 全部回成功，且生效后 S6F11 能正常发出。
- [ ] 一组 `S16F15 + S14F9` 能成功入缓存（ACK=0）。
- [ ] 对同一 Carrier：
  - `PRPROCESSSTART=true` 能自动触发 PPSELECT+START
  - `PRPROCESSSTART=false` 不会自动触发
- [ ] 进程重启后，报告定义/绑定/启用及作业缓存可恢复。

---

## F. 常见失败定位

1. `S2F36 LRACK=5`：RPTID 未先在 `S2F33` 定义。
2. `S2F38 EAC!=0`：CEID 无效或消息格式错误。
3. `S6F11 未发送`：先查是否完成定义/绑定/启用三步。
4. `S14F10 ACK=1`：PJ 不存在或 CarrierInputSpec 与 PJ Carrier 不匹配。
5. 自动启动未触发：检查 `PRPROCESSSTART` 是否为 `TRUE`、CarrierId 是否一致。
