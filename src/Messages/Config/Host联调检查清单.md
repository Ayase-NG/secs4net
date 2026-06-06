# Host联调检查清单（当前项目版本）

> 目标：用于现场联调逐项勾选，快速定位失败点。
> 范围：当前项目已实现链路（不含 recipe 上报）。

---

## A. 联调前准备

- [ ] Host 与设备（本项目）网络互通（HSMS 端口、gRPC 端口）。
- [ ] `SecsListener:Enabled=true`，HSMS 角色（Active/Passive）双方匹配。
- [ ] `DeviceId` 与 Host 配置一致。
- [ ] `VID.csv` 至少包含：`LOTID/WAFERID/RFID/SLOTID/SLOTSLIST/PORTID/RESULT/STATUS/FROM_STATE/TO_STATE/TRIGGER`。
- [ ] `S2F33/S2F35/S2F37` 计划使用的 `CEID/RPTID/VID` 已梳理。
- [ ] gRPC 配置有效：`SecsDispatch:Grpc:CallTimeoutMs`、`SecsDispatch:S6F11:MaxAttempts/RetryDelayMs`。

---

## B. 协议主流程检查（逐步勾选）

## 1) 建链
- [ ] Host -> `S1F13`
- [ ] 设备回 `S1F14`
- 期望：`COMMACK=0`
- 失败排查：
  - `COMMACK=1`：设备状态不允许或消息结构异常
  - 检查在线状态是否 OffLine、运行状态是否异常

## 2) 报告定义
- [ ] Host -> `S2F33`
- [ ] 设备回 `S2F34`
- 期望：`DRACK=0`
- 失败排查：
  - `DRACK=2`：消息格式问题
  - `DRACK=1`：存储/内部错误

## 3) 事件绑定
- [ ] Host -> `S2F35`
- [ ] 设备回 `S2F36`
- 期望：`LRACK=0`
- 失败排查：
  - `LRACK=4`：CEID 无效
  - `LRACK=5`：RPTID 未定义

## 4) 事件启用
- [ ] Host -> `S2F37`
- [ ] 设备回 `S2F38`
- 期望：`EAC=0`
- 失败排查：
  - `EAC=1/2`：CEID 无效或格式/内部错误

## 5) 运行前准备（建议先做）
- [ ] Host -> `S2F41(PPSELECT)`，带 `MODE/LOTID/SLOTSLIST`
- [ ] 设备回 `S2F42`
- 期望：`HCACK=0`
- 失败排查：
  - `HCACK=2`：状态不允许（需 OnLineRemote）或 gRPC 下游失败
  - 幂等命中：重复请求会回成功但不重复执行

## 6) Mapping信息下发
- [ ] Host -> `S3F17(LOTID+SlotMap)`
- [ ] 设备回 `S3F18`
- 期望：`ACK=0`

## 7) 作业下发（最小实现）
- [ ] Host -> `S16F15(Create Process Job)`
- [ ] 设备回 `S16F16`
- [ ] Host -> `S14F9(Create Control Job)`
- [ ] 设备回 `S14F10`
- 期望：`ACK=0`
- 失败排查：
  - 关键字段为空（PJID/ObjID）
  - 非 OnLineRemote
  - 幂等窗口内重复请求

## 8) 启动执行
- [ ] Host -> `S2F41(START)`
- [ ] 设备回 `S2F42`
- 期望：`HCACK=0`
- 失败排查：
  - 启动前状态检查失败（StatusReport 失败或 RunStatus=running/jam）

## 9) 主动上报（S6F11）
- [ ] 设备上报 Mapping 结果 `S6F11(CEID=1002)`
- [ ] Host 回 `S6F12`
- [ ] 设备上报单片结果 `S6F11(CEID=1001)`（N次）
- [ ] Host 每次回 `S6F12`
- [ ] 设备上报整盒完成 `S6F11(CEID=1003)`
- [ ] Host 回 `S6F12`
- 失败排查：
  - 若设备未发 S6F11：优先检查 `S2F33/35/37` 是否配置并启用
  - 若发了但失败：检查会话可用性、重试配置、Host 是否回 S6F12

---

## C. 关键验收项（上线前最小通过标准）

- [ ] 全流程连续跑通 3 轮以上（无人工重启）。
- [ ] 每轮都能收到完整 `S6F11 -> S6F12` 闭环。
- [ ] 重发场景验证通过：S2F41/S14F9/S16F15 重复请求不会重复执行。
- [ ] OffLine/OnLineLocal/OnLineRemote 权限符合预期。
- [ ] 关键交互在追溯表中可查（入站+出站）。

---

## D. 现场快速定位（建议顺序）

1. 先看设备在线状态（是否 OnLineRemote）。
2. 再看 `S2F33/35/37` 配置是否完整（定义/绑定/启用）。
3. 再看 `S2F41` 回包 HCACK。
4. 再看 gRPC 下游调用日志（PPSELECT/START/StatusReport）。
5. 最后看 S6F11 门禁日志与重试日志。

---

## E. 当前策略说明

- recipe 相关上报暂不开放（例如结果上报不带 PPID）。
- MODE 在 `PPSELECT` 阶段优先下发（运行前准备）。
