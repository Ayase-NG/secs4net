# Equipment–Fab SECS/HSMS 通信协议

文档编号：GWM-SECS-FAB-ASCII-001
版本：V1.0（对接评审稿）
适用对象：Fab Host / EAP 与 Equipment
参数值编码：ASCII

## 1. 文档范围

本文规定 Equipment 与 Fab Host/EAP 之间的 HSMS 连接、通信状态、变量查询、事件报告配置、事件上报、远程命令、报警及配方查询接口。接口结构以当前设备软件的已实现路径为依据。

本文为双方接口评审及联调基线，不替代现场验收。未列入本文的 SF 不在本版本协议范围内。本版本不声明完整 SEMI GEM 标准符合性。

本版本使用 ASCII 参数配置：VID 字典中的所有业务变量值均按 A 编码，不按源 CSV 的 Format 列自动转换为 U4、List 等类型。设备事件源应使用普通字符串参数路径，或显式提供 ASCII 值，保持本协议编码一致。

ASCII 约束仅作用于业务变量值。DATAID、CEID、RPTID、VID、ALID、确认码及结构列表等协议字段仍按本文指定的 U1/U2/U4/B/L 编码，不得全部改为字符串。

### 1.1 文档确认信息

| 项目 | 确认内容 |
| --- | --- |
| Fab / 产线 | 现场填写 |
| 设备编号 / 型号 / 软件版本 | 以现场配置和 S1F2、S1F14 返回为准 |
| Host IP / Equipment IP | 现场填写 |
| HSMS 角色 / TCP 端口 / Device ID | 依第 2 节共同确认 |
| 报告定义及事件绑定 | 依第 5 节及附录确认 |
| 参数枚举、单位及时间字段含义 | 联调前由双方签认 |
| Fab 确认 / 设备方确认 / 日期 | 现场填写 |

### 1.2 当前业务实现边界（内部核对提示）

本协议中的变量和事件字典是接口定义，不等同于设备业务已全部接入。当前完成度及逐项代码依据见同目录 `Fab通信协议_ASCII版_内部实现核对.md`；内部核对版不作为对外验收承诺。

当前 EFEM 业务侧已接入 `MappingCompleted`（1006）、`CarrierProcessed`（1008）、`WorkerProcessed`（1014）的通用事件调用；其中 1008、1014 仍有字段或语义缺口。SECS 服务另在设备侧 local/remote 切换成功时尝试发送 `OnlineStateChanged`（1029），尚不能据此认定所有在线/离线变化均有事件。其余 CEID 仅有字典或通用发送能力，未核实到当前生产业务主动触发路径。

特别注意：工位结果当前传入 `SlotId`，未映射到 VID 3005 `WaferSlot`；`WaferId` 与 `WaferID` 的大小写差异可被现有映射兼容。`WaferResult` 当前为 ASCII `True/False`，尚未实现协议目标枚举 `OK/NG/Aborted`。不得把默认值、空值或历史缓存值视为本次业务实测值。

## 2. 通信连接与公共约定

### 2.1 HSMS 连接参数

| 参数 | 对接基线 | 说明 |
| --- | --- | --- |
| 传输方式 | HSMS over TCP/IP | 建立选中连接后交换 SECS 数据消息 |
| Equipment 角色 | Passive | 支持配置 Active；角色以现场签认为准 |
| Equipment IP | 现场填写 | 不将开发环境回环地址作为生产地址 |
| TCP 端口 | 5050 | 当前配置值，可调整 |
| Device ID | 6 | 当前配置值，双方一致 |
| T3 | 45000 ms | 等待 SECS 应答超时 |
| T5 | 10000 ms | 连接重试间隔 |
| T6 | 5000 ms | HSMS 控制事务超时 |
| T7 | 10000 ms | 未选中连接超时 |
| T8 | 5000 ms | 接收消息字节间隔超时 |

超时参数为监听器未显式配置时的软件默认值，现场部署可覆盖。建立 TCP 连接不等于设备已处于远程在线状态。

### 2.2 数据类型和表示法

| 表示 | 含义 |
| --- | --- |
| H→E / E→H | Host 发往 Equipment / Equipment 发往 Host |
| W | 主消息要求应答；本文请求与主动报告均置 W=1 |
| L[n] | 含 n 个子项的 SECS List |
| A | ASCII 字符串 |
| B | 二进制字节；确认码通常为单字节 |
| U1 / U2 / U4 | 1 / 2 / 4 字节无符号整数 |
| MDLN / SOFTREV | 设备型号 / 软件版本 |
| CEID / RPTID / VID | 事件编号 / 报告编号 / 变量编号 |

下面消息模板中的字段名是占位说明，不是固定字符串。SML 示例以点号结束。

业务变量 A 值仅使用 ASCII 可表示字符。数字以十进制文本表示，例如 A "5"；不得仅因字面值为数字而编码成 U4。原 CSV 中 Format=List 的变量在本协议中仍为 A，具体文本结构、分隔符和元素顺序需按字段业务含义共同确认，不是 SECS List。

附录中的默认值是配置参考，不表示每次查询或上报必定返回该值。单位、结果枚举和未定义文本格式不得由 Host 自行推断。

### 2.3 本版本 SF 清单

| 请求 / 主消息 | 应答 | 方向 | 用途 |
| --- | --- | --- | --- |
| S1F1 | S1F2 | H→E | 通信探测 |
| S1F3 | S1F4 | H→E | 状态变量值查询 |
| S1F11 | S1F12 | H→E | 状态变量名称查询 |
| S1F13 | S1F14 | H→E | 建立通信 |
| S1F15 | S1F16 | H→E | 请求离线 |
| S1F17 | S1F18 | H→E | 请求上线 |
| S2F13 | S2F14 | H→E | 设备常量值查询 |
| S2F31 | S2F32 | H→E | Host 时间接收与记录 |
| S2F33 | S2F34 | H→E | 定义 / 删除报告 |
| S2F35 | S2F36 | H→E | 绑定 / 解除事件报告 |
| S2F37 | S2F38 | H→E | 启用 / 禁用事件 |
| S2F41 | S2F42 | H→E | 远程命令请求及处理确认 |
| S5F1 | S5F2 | E→H | 报警报告 |
| S5F3 | S5F4 | H→E | 报警发送使能 |
| S5F5 | S5F6 | H→E | 当前报警列表查询 |
| S6F11 | S6F12 | E→H | 事件报告 |
| S7F19 | S7F20 | H→E | 配方目录查询 |
| S7F25 | S7F26 | H→E | 格式化配方查询 |

## 3. 通信建立与在线控制

### 3.1 S1F1 / S1F2：通信探测

S1F1：H→E，W=1，无消息体。S1F2：E→H，W=0。

```text
S1F1 W .
S1F2
<L[2]
  <A "MDLN">
  <A "SOFTREV">
>.
```

S1F2 仅证明设备能够应答，不证明设备处于可远程控制状态。

### 3.2 S1F13 / S1F14：建立通信

S1F13：H→E，W=1，使用空 List。S1F14：E→H，W=0。

```text
S1F13 W <L[0]>.
S1F14
<L[2]
  <B 0x00>
  <L[2]
    <A "MDLN">
    <A "SOFTREV">
  >
>.
```

COMMACK 为 B：0=接受，1=请求载荷不符合接收条件。建立通信接受不自动将设备切换到远程在线。

### 3.3 S1F15 / S1F16：请求离线

S1F15：H→E，W=1，无消息体。S1F16：E→H，W=0，消息体为 B OFLACK。

OFLACK：0=接受离线；1=请求格式校验失败。设备已经离线时重复请求返回接受。

### 3.4 S1F17 / S1F18：请求上线

S1F17：H→E，W=1，无消息体。S1F18：E→H，W=0，消息体为 B ONLACK。

ONLACK：0=接受；1=拒绝。本实现从 OffLine 接受上线后进入 OnLineLocal。当前已经在线时重复请求返回拒绝。

远程命令使用 OnLineRemote 状态。由设备侧完成 Local/Remote 切换，Host 不应将 S1F18 的接受等同于获得远程控制权限。

## 4. 变量与常量查询

### 4.1 S1F3 / S1F4：查询状态变量值

S1F3：H→E，W=1，L[n]{U4 SVID...}。空 List 表示查询全部已配置 SVID。

S1F4：E→H，W=0，L[n]{A SV...}。返回顺序与请求顺序一致；查询全部时按编号升序。未配置编号返回空字符串占位。

```text
S1F3 W
<L[2]
  <U4 1001>
  <U4 1002>
>.
S1F4
<L[2]
  <A "online">
  <A "remote">
>.
```

示例值仅说明报文格式，实际状态文本以设备输出及枚举签认为准。S1F4 不在每个值旁附带 SVID。

### 4.2 S1F11 / S1F12：查询状态变量名称

S1F11：H→E，W=1，L[n]{U4 SVID...}；空 List 查询全部。

S1F12：E→H，W=0。

```text
L[n]
  L[3]
    U4 SVID
    A SVNAME
    A UNITS
```

UNITS 当前返回空字符串；未配置编号返回对应编号、空名称及空单位。本消息返回定义，不返回实时变量值。

### 4.3 S2F13 / S2F14：查询设备常量值

S2F13：H→E，W=1，L[n]{U4 ECID...}；空 List 查询全部已配置 ECID。

S2F14：E→H，W=0，L[n]{A ECV...}。返回顺序与请求一致；未知编号返回空字符串。常量编号见附录 A。

```text
S2F13 W <L[1] <U4 2001>>.
S2F14 <L[1] <A "Alpha-1.0.0">>.
```

上例软件版本仅为配置示例，以设备实际返回为准。

## 5. 动态报告配置

### 5.1 配置关系与顺序

报告关系：CEID → RPTID → 有序 VID 列表。Host 按 S2F33、S2F35、S2F37 顺序配置，并等待每一步返回成功后继续。

本版对接配置采用每个 CEID 绑定一个 RPTID。一个 RPTID 可包含多个 VID；不同 CEID 可复用同一报告。报告 VID 可引用附录 A 的变量编号，本实现不按 SVID/ECID/DVID 分类过滤报告值。

报告使用的是事件发生时设备提供的数据和已维护的运行时值，不应将报告绑定等同于触发一次硬件采集。事件所需字段应由设备事件源准备齐全。Host 与设备方应按事件确认必需字段，避免依赖未准备或过期的业务值。

设备离线时拒绝报告定义、绑定和使能操作。配置应在设备上线后进行。

### 5.2 S2F33 / S2F34：定义报告

S2F33：H→E，W=1。

```text
L[2]
  U1 DATAID
  L[n]
    L[2]
      U4 RPTID
      L[m]
        U4 VID
        ...
```

DATAID 使用 0～255；RPTID 使用非零 U4。VID 在本版本字典范围内，报告中的变量顺序由 VID 列表确定。

第二项为空 List：清空全部报告。单个 RPTID 的 VID 列表为空：删除该报告。重新定义同一 RPTID：更新报告内容。

S2F34：E→H，W=0，B DRACK。

| DRACK | 含义 |
| --- | --- |
| 0 | 处理成功 |
| 1 | 当前状态不允许或内部处理失败 |
| 2 | 请求格式错误 |

### 5.3 S2F35 / S2F36：事件报告绑定

S2F35：H→E，W=1。

```text
L[2]
  U1 DATAID
  L[n]
    L[2]
      U4 CEID
      L[m]
        U4 RPTID
        ...
```

CEID 使用附录 B 定义值，RPTID 应已通过 S2F33 定义。本版每个 CEID 的 RPTID 列表使用一项；列表为空表示解除该 CEID 的绑定。

S2F36：E→H，W=0，B LRACK。

| LRACK | 含义 |
| --- | --- |
| 0 | 绑定处理成功 |
| 1 | 当前状态不允许或内部错误 |
| 2 | 请求格式错误 |
| 4 | CEID 无效 |
| 5 | 引用的 RPTID 未定义 |

### 5.4 S2F37 / S2F38：事件使能

S2F37：H→E，W=1。

```text
L[2]
  B CEED
  L[n]
    U4 CEID
    ...
```

CEED：0=禁用，1=启用。CEID 列表为空表示全部事件。启用事件不替代报告定义与绑定。

S2F38：E→H，W=0，B EAC。

| EAC | 含义 |
| --- | --- |
| 0 | 处理成功 |
| 1 | CEID 无效 |
| 2 | 请求格式、当前状态或内部错误 |

## 6. S6F11 / S6F12：事件及检测数据报告

S6F11：E→H，W=1。S6F12：H→E，W=0。

### 6.1 本设备报文结构

```text
S6F11 W
L[3]
  U1 DATAID
  U4 CEID
  L[n]
    L[2]
      U4 RPTID
      L[m]
        L[2]
          U2 VID
          A VALUE
        ...
```

重要兼容约定：本设备每个变量项为 L[2]{U2 VID, A VALUE}，包含 VID，不是仅包含 VALUE 的列表。Fab/EAP 必须按此结构解析。报告中的 VID 编号范围为 0～65535，本协议实际使用附录编号。

Host 正常接收后发送 S6F12，消息体按 B ACKC6=0 应答。设备当前发送成功判定依据为收到 S6F12 消息头，Host 侧应仅在业务接收成功后返回该消息，不应依赖非零 ACKC6 实现可靠的负确认重发。

### 6.2 报告值与触发

报告值统一为 ASCII；例如 WaferSlot=5 编码为 A "5"。SVID、ECID、DVID 是变量分类，不改变此版本的值编码。

事件由设备业务发生后触发。附录 B 表示可映射的事件编号，不表示每个物理动作一定由通信中间件独立检测。报警 S5F1 与事件 S6F11 为独立通道，不应假定每条报警必然伴随一次报警类事件。

只有事件已使能、CEID 已绑定报告、绑定报告已定义且存在可上报值时，设备才尝试发送。现场对每个启用事件确认字段完整性与值顺序；Host 应检查每条报告实际包含的 VID。

当前通用事件路径仅使用 CEID 绑定列表中的首个 RPTID，尚未逐个发送全部绑定报告。未在本次参数中提供的 VID 可能从运行态缓存回填；没有可用值的字段可能缺席。配置报告不代表自动补齐业务数据源，也不保证形成同一次业务的完整快照。当前业务调用细节及待实现项见内部核对版。

### 6.3 重试与接收去重

当前部署配置：S6F11 最多尝试 2 次，重试间隔 200 ms；发送失败或无可用会话时可进入事件缓存，后台每 1000 ms 尝试补发，每批最多 50 条。

Host 应允许重发和延迟到达，对业务重复记录作幂等处理。DATAID 为 U1，可回绕，不应作为长期全局唯一键；建议结合 CEID、批次、载具、晶圆及业务时间字段识别同一业务事件。必要的业务时间应由事件参数明确提供。

### 6.4 完整配置及报告示例

以下示例把单片检测完成事件 1010 绑定到报告 5001，仅说明目标协议结构，不代表当前设备已经触发此事件。当前尚未找到 `WaferProcessed` 的业务上报调用，且 VID 3005、3007 仍有内部核对版列出的接入缺口，不得将以下报文作为当前业务实测结果。RPTID 5001 为 Host 自定义示例；当前可优先联调 1006 `MappingCompleted` 搭配 VID 3013、3015、3006。

```text
S2F33 W
<L[2]
  <U1 1>
  <L[1]
    <L[2]
      <U4 5001>
      <L[3] <U4 3004> <U4 3005> <U4 3007>>
    >
  >
>.
S2F34 <B 0x00>.

S2F35 W
<L[2]
  <U1 2>
  <L[1]
    <L[2] <U4 1010> <L[1] <U4 5001>>>
  >
>.
S2F36 <B 0x00>.

S2F37 W
<L[2] <B 0x01> <L[1] <U4 1010>>>.
S2F38 <B 0x00>.

S6F11 W
<L[3]
  <U1 3>
  <U4 1010>
  <L[1]
    <L[2]
      <U4 5001>
      <L[3]
        <L[2] <U2 3004> <A "WAFER001">>
        <L[2] <U2 3005> <A "5">>
        <L[2] <U2 3007> <A "OK">>
      >
    >
  >
>.
S6F12 <B 0x00>.
```

## 7. S2F41 / S2F42：远程命令

S2F41：H→E，W=1。命令执行前设备应处于 OnLineRemote。

```text
L[2]
  A RCMD
  L[n]
    L[2]
      A CPNAME
      A CPVAL
```

本版本命令参数使用 ASCII。命令名推荐大写；PPSELECT 参数名以表中名称为准。

### 7.1 命令表

| RCMD | 功能 | 参数约定 |
| --- | --- | --- |
| PPSELECT | 选择配方及运行上下文 | PPID、PORTID、LOTID、MODE、SLOTSLIST |
| START | 发起开始检测请求 | 无参数触发 |
| STOP | 发起停止请求<u>**（暂不支持）**</u> | 可传 WAFERID、LOTID、SLOTID、PPID、STATUS、RESULT |
| PAUSE | 发起暂停请求<u>**（暂不支持）**</u> | 可传 WAFERID、LOTID、SLOTID、PPID、STATUS、RESULT |
| RESUME | 发起恢复请求<u>**（暂不支持）**</u> | 可传 WAFERID、LOTID、SLOTID、PPID、STATUS、RESULT |

PPSELECT：PORTID 必填；建议同时传 PPID 和 LOTID，避免依赖历史上下文。MODE 的业务编码以设备运行模式表为准。SLOTSLIST 支持逗号分隔的十进制槽位文本，例如 "1,2,3"；槽位合法范围由设备配置确认。START 当前不使用请求参数来选择晶圆或端口，应先完成配方及上下文准备。

### 7.2 S2F42 确认结构

```text
L[2]
  B HCACK
  L[n]
    L[2]
      A CPNAME
      B CPACK
```

| HCACK | 含义 |
| --- | --- |
| 0 | 请求处理确认；不作为设备动作完成证明 |
| 1 | 命令不支持 |
| 2 | 请求解析、状态条件或处理失败 |

| CPACK | 本项目含义 |
| --- | --- |
| 1 | 不支持或未分类参数问题 |
| 2 | 请求解析问题 |
| 3 | 状态条件、端口或批次上下文问题 |

错误项名称可能为 RCMD、Parse、NotOnlineState、PORTID、LOTID。成功时参数确认列表通常为空。

相同命令和参数在 10 秒窗口内受防重处理，重复请求可能直接返回 HCACK=0；该确认不是前一次动作成功结果的保证。Host 应采用设备状态和业务事件核验实际执行结果，不应仅凭 S2F42 判定检测已启动、停止或完成。

### 7.3 PPSELECT 示例

```text
S2F41 W
<L[2]
  <A "PPSELECT">
  <L[5]
    <L[2] <A "PPID"> <A "DefaultRecipe">>
    <L[2] <A "PORTID"> <A "1">>
    <L[2] <A "LOTID"> <A "LOT001">>
    <L[2] <A "MODE"> <A "00">>
    <L[2] <A "SLOTSLIST"> <A "1,2,3">>
  >
>.
S2F42 <L[2] <B 0x00> <L[0]>>.
S2F41 W <L[2] <A "START"> <L[0]>>.
```

MODE="00" 仅为字符串格式示例，须按双方确认的模式编码替换，不将此示例定义为通用模式枚举。

## 8. 报警通信

### 8.1 S5F1 / S5F2：报警报告

S5F1：E→H，W=1。

```text
L[3]
  B ALCD
  U4 ALID
  A ALTX
```

ALID 为报警编号，ALCD 为设备提供的原始单字节报警代码，ALTX 为设备提供的报警文本。现场报警编号、代码及文本应由设备方提供并签认；Host 不得仅由 ALID 推导严重级别。ALTX 可能含代码前缀，例如 "代码:文本"。

Host 接收后返回 S5F2（W=0），以 B ACKC5=0 表示正常接收。当前设备采用收到 S5F2 消息头确认发送闭环，可能因超时重发一次；Host 按来源业务约定处理重复报警。

报警报文的 ALTX 使用 ASCII；中文描述仅用于本协议阅读，不作为 ASCII 报文文本直接发送。

### 8.2 S5F3 / S5F4：报警发送使能

S5F3：H→E，W=1。

```text
L[2]
  B ALED
  L[n]
    U4 ALID
    ...
```

ALED：0=禁用，推荐 1=启用；本设备按非零判定启用。ALID 列表为空表示配置全部报警。S5F4：E→H，W=0，B ACKC5；0=处理成功，2=格式或内部错误。

禁用影响后续报警发送，不等于消除设备报警或清除设备故障。

### 8.3 S5F5 / S5F6：报警列表查询

S5F5：H→E，W=1，L[n]{U4 ALID...}；空 List 查询全部当前激活报警。

S5F6：E→H，W=0。

```text
L[n]
  L[3]
    B ALCD
    U4 ALID
    A ALTX
```

本接口返回运行时报警状态集合，不作为全部静态报警定义字典。空列表表示当前没有返回项；请求解析失败时设备同样可能返回空列表，Host 应使用合法请求格式。

## 9. 配方查询

### 9.1 S7F19 / S7F20：配方目录

S7F19：H→E，W=1，无消息体。S7F20：E→H，W=0，L[n]{A PPID...}。

PPID 为设备配方目录中的配方名称。空列表可能表示没有可返回目录或目录读取失败，不应仅凭空列表认定设备配方已全部删除。

### 9.2 S7F25 / S7F26：格式化配方读取

S7F25：H→E，W=1，A PPID。PPID 仅传配方名，不包含路径。

S7F26：E→H，W=0。

```text
L[4]
  A PPID
  A MDLN
  A SOFTREV
  L[n]
    L[2]
      U2 CCODE
      L[m]
        A PPARM
        ...
```

CCODE 是配方命令码，PPARM 是 ASCII 参数。具体命令码和参数含义由该配方定义共同确认。读取失败时可返回空的格式化配方，Host 应检查 PPID 及命令内容，不将空内容当作有效配方。

## 10. S2F31 / S2F32：Host 时间记录

S2F31：H→E，W=1，A TIME。推荐 UTC 文本 yyyyMMddHHmmss，例如 "20260101080000"。也可使用明确带 Z 的 ISO 8601 文本。

S2F32：E→H，W=0，B TIACK；0=解析并记录成功，2=格式或存储失败。本接口记录 Host 时间，不表示操作系统时钟已被修改，不能代替现场统一时钟同步措施。

## 11. 联调流程及验收项

### 11.1 推荐流程

1. 签认现场 HSMS 角色、地址、端口、Device ID 及超时。
2. 建立 HSMS 选中连接，通过 S1F1、S1F13 验证通信。
3. 使用 S1F17 上线，由设备侧确认进入远程在线后再进行远程操作。
4. 以 S1F11、S1F3、S2F13 核对变量名称及 ASCII 值。
5. Host 按 S2F33 → S2F35 → S2F37 配置每个需上报的 CEID；确认每步 ACK。
6. 设备触发约定事件，验证 S6F11 的 CEID、RPTID、VID、值和 S6F12 接收闭环。
7. 在安全联调条件下验证 PPSELECT、START、PAUSE、RESUME、STOP 请求与实际状态事件。
8. 验证报警上报、使能配置、当前报警查询及接收确认。
9. 验证配方目录与格式化配方读取。

### 11.2 双方验收检查表

| 检查项 | 通过条件 | 结果 / 签认 |
| --- | --- | --- |
| 连接参数 | HSMS 角色、端口和 Device ID 一致 | 现场填写 |
| ASCII 编码 | 变量值使用 A，数值不误解为 U4，列表文本不误解为 L | 现场填写 |
| 事件结构 | 按 L[2]{U2 VID,A VALUE} 解析每个变量 | 现场填写 |
| 事件配置 | CEID、RPTID、VID 关系和字段顺序一致 | 现场填写 |
| 字段值 | 必需字段齐全，单位、枚举、时间及列表文本结构已签认 | 现场填写 |
| 在线控制 | 能区别通信建立、本地在线及远程在线 | 现场填写 |
| 命令确认 | 不将 S2F42 等同于设备动作完成 | 现场填写 |
| 重发接收 | 相同事件或报警重发不重复计数 | 现场填写 |
| 报警映射 | ALID、ALCD、ALTX 定义与 ASCII 文本一致 | 现场填写 |
| 配方读取 | PPID、CCODE、参数序列符合配方约定 | 现场填写 |

## 附录 A：VID 字典（ASCII 对接配置）

下表摘录当前 VID.csv 中本版对接使用的变量定义。所有行的对接值类型统一为 A；原 Format 列不作为本版报文转换依据。默认值仅作配置参考；空白表示未配置默认值，不能推断为数值 0。

完成度按内部代码核对结果标注：已接入＝存在真实业务写值路径；部分实现＝已有路径但存在字段、值域或覆盖缺口；静态/配置＝仅有内存常量或配置返回值；尚未实现＝未找到真实业务写值路径。已接入不等于已通过现场验收，具体限制见 `Fab通信协议_ASCII版_内部实现核对.md`。

| VID | 分类 | 参数名 | 值类型 | 配置默认值 | 含义 | 完成度 |
| --- | --- | --- | --- | --- | --- | --- |
| 1001 | SVID | OnlineStatus | A | offline | 在线状态 | 已接入 |
| 1002 | SVID | RemoteStatus | A | local | 远程状态、控制状态 | 部分实现 |
| 1003 | SVID | EquipmentStatus | A | unknown | 设备状态：Idle/Running/initialized/unknown 等 | 尚未实现 |
| 1004 | SVID | AlarmCode | A | 0 | 当前报警代码 | 尚未实现 |
| 1005 | SVID | ChangeTrigger | A | device | 在线状态触发来源 | 部分实现 |
| 1006 | SVID | LP1CarrierID | A |  | LoadPort 1 载具编号 | 尚未实现 |
| 1007 | SVID | LP2CarrierID | A |  | LoadPort 2 载具编号 | 尚未实现 |
| 1008 | SVID | LP3CarrierID | A |  | LoadPort 3 载具编号 | 无需实现 |
| 1009 | SVID | LP4CarrierID | A |  | LoadPort 4 载具编号 | 无需实现 |
| 1010 | SVID | LP1CarAccessStatus | A |  | LoadPort 1 载具状态 | 尚未实现 |
| 1011 | SVID | LP2CarAccessStatus | A |  | LoadPort 2 载具状态 | 尚未实现 |
| 1012 | SVID | LP3CarAccessStatus | A |  | LoadPort 3 载具状态 | 无需实现 |
| 1013 | SVID | LP4CarAccessStatus | A |  | LoadPort 4 载具状态 | 无需实现 |
| 1014 | SVID | LP1MapSotList | A |  | LoadPort 1 Mapping 结果文本 | 尚未实现 |
| 1015 | SVID | LP2MapSotList | A |  | LoadPort 2 Mapping 结果文本 | 尚未实现 |
| 1016 | SVID | LP3MapSotList | A |  | LoadPort 3 Mapping 结果文本 | 无需实现 |
| 1017 | SVID | LP4MapSotList | A |  | LoadPort 4 Mapping 结果文本 | 无需实现 |
| 1018 | SVID | SlotState | A |  | 晶圆状态：Notstate、Resource、Aborted 等 | 尚未实现 |
| 1020 | SVID | EdgeSlotId | A |  | Edge 工位晶圆槽位号 | 尚未实现 |
| 1021 | SVID | EdgeResult | A |  | Edge 工位检测结果 | 尚未实现 |
| 1022 | SVID | SurfaceSlotId | A |  | 表面工位晶圆槽位号 | 尚未实现 |
| 1023 | SVID | FrontSurfaceResult | A |  | 正面检测结果 | 尚未实现 |
| 1024 | SVID | BackSurfaceResult | A |  | 背面检测结果 | 尚未实现 |
| 2001 | ECID | SoftwareVersion | A | Alpha-1.0.0 | 软件版本 | 静态/配置 |
| 2002 | ECID | HardwareVersion | A | GWM-300-CW | 硬件版本 | 静态/配置 |
| 2003 | ECID | MaxThroughput | A | 70 | 最大吞吐量；单位须签认 | 静态/配置 |
| 2004 | ECID | DefaultRecipeName | A |  | 默认配方名称 | 尚未实现 |
| 2005 | ECID | FfuSpeed | A |  | FFU 风机转速；单位须签认 | 尚未实现 |
| 2006 | ECID | AirPressure | A |  | 气压；单位须签认 | 尚未实现 |
| 2007 | ECID | UPS | A |  | UPS 数据；取值含义须签认 | 尚未实现 |
| 3001 | DVID | ProcessStartTime | A |  | 当前料盒开始检测时间；格式与时区须签认 | 尚未实现 |
| 3002 | DVID | ProcessEndTime | A |  | 当前料盒结束检测时间；格式与时区须签认 | 尚未实现 |
| 3003 | DVID | TotalProcessDuration | A |  | 当前料盒总检测时长；文本结构与单位须签认 | 尚未实现 |
| 3004 | DVID | WaferID | A |  | 当前晶圆编号 | 已接入 |
| 3005 | DVID | WaferSlot | A |  | 当前晶圆槽位 | 尚未实现 |
| 3006 | DVID | WaferSlotList | A |  | 当前晶圆盒槽位列表文本 | 已接入 |
| 3007 | DVID | WaferResult | A |  | 当前晶圆检测结果：OK/NG/Aborted | 部分实现 |
| 3008 | DVID | DefectScratchCount | A |  | 划痕缺陷数量 | 尚未实现 |
| 3009 | DVID | DefectParticleCount | A |  | 颗粒缺陷数量 | 尚未实现 |
| 3010 | DVID | DefectCloudCount | A |  | 云点缺陷数量 | 尚未实现 |
| 3011 | DVID | WorkStation | A |  | 当前晶圆检测工位 | 已接入 |
| 3012 | DVID | DetectionMode | A |  | 当前检测模式 | 尚未实现 |
| 3013 | DVID | PortID | A |  | 当前加载端口编号 | 已接入 |
| 3014 | DVID | LotID | A |  | 当前批次号 | 尚未实现 |
| 3015 | DVID | CarrierID | A |  | 当前晶圆盒编号 | 已接入 |
| 3016 | DVID | RecipeName | A |  | 当前配方名称 PPID | 尚未实现 |

## 附录 B：事件字典

事件名是设备事件源使用的映射名称；Fab 根据 CEID 配置报告。表中描述只定义事件含义，不指定默认 RPTID 或默认 VID 列表。启用前按双方签认的事件数据集完成报告配置。

完成度沿用附录 A 的内部核对口径，按业务事件的实际触发路径判定；存在事件字典或通用发送接口不等于已接入，已有 S5F1 报警也不等于已实现报警类 S6F11 事件。部分实现的具体缺口见内部核对版。

| CEID | 事件名 | 含义 | 完成度 |
| --- | --- | --- | --- |
| 1001 | CarrierArrived | 载具放上 | 尚未实现 |
| 1002 | CarrierRemoved | 载具取走 | 尚未实现 |
| 1003 | CarrierLoaded | 载具装载 | 尚未实现 |
| 1004 | CarrierUnloaded | 载具卸载 | 尚未实现 |
| 1005 | MappingStart | Mapping 开始 | 尚未实现 |
| 1006 | MappingCompleted | Mapping 完成 | 已接入 |
| 1007 | CarrierProcessing | 整盒检测开始 | 尚未实现 |
| 1008 | CarrierProcessed | 整盒检测完成 | 部分实现 |
| 1009 | WaferProcessing | 单片检测开始 | 尚未实现 |
| 1010 | WaferProcessed | 单片检测完成 | 尚未实现 |
| 1011 | RobotProcessing | 机械臂搬运开始 | 尚未实现 |
| 1012 | RobotProcessed | 机械臂搬运结束 | 尚未实现 |
| 1013 | WorkerProcessing | 工位检测开始 | 尚未实现 |
| 1014 | WorkerProcessed | 工位检测结束 | 部分实现 |
| 1015 | AlignmentCompleted | 对位完成 | 尚未实现 |
| 1016 | WaferIdCameraCompleted | 晶圆 ID 拍照完成 | 尚未实现 |
| 1017 | NotchS1Completed | Notch Step1 拍照完成 | 尚未实现 |
| 1018 | NotchS2Completed | Notch Step2 拍照完成 | 尚未实现 |
| 1019 | EdgeProcessing | 边缘拍照开始 | 尚未实现 |
| 1020 | EdgeProcessed | 边缘拍照完成 | 尚未实现 |
| 1021 | SurfaceLightProcessing | 表面光照拍照开始 | 尚未实现 |
| 1022 | SurfaceLightProcessed | 表面光照拍照完成 | 尚未实现 |
| 1023 | SurfaceS1Processing | 表面 Step1 拍照开始 | 尚未实现 |
| 1024 | SurfaceS1Processed | 表面 Step1 拍照完成 | 尚未实现 |
| 1025 | SurfaceS2Processing | 表面 Step2 拍照开始 | 尚未实现 |
| 1026 | SurfaceS2Processed | 表面 Step2 拍照完成 | 尚未实现 |
| 1027 | SurfaceS3Processing | 表面 Step3 拍照开始 | 尚未实现 |
| 1028 | SurfaceS3Processed | 表面 Step3 拍照完成 | 尚未实现 |
| 1029 | OnlineStateChanged | 在线状态变化 | 部分实现 |
| 1030 | RemoteStateChanged | 远程状态变化 | 尚未实现 |
| 1031 | AlarmRaised | 报警发生事件 | 尚未实现 |
| 1032 | AlarmCleared | 报警清除事件 | 尚未实现 |
| 1033 | EquipmentStateChanged | 设备运行状态变化 | 尚未实现 |
| 1034 | DeviceStop | 设备停止 | 尚未实现 |
| 1035 | DeviceStart | 设备启动 | 尚未实现 |
| 1036 | DeviceReset | 设备复位 | 尚未实现 |
| 1037 | DevicePause | 设备暂停 | 尚未实现 |
| 1038 | DeviceResume | 设备恢复 | 尚未实现 |

## 附录 C：术语

| 术语 | 说明 |
| --- | --- |
| Host / EAP | Fab 主机或设备自动化系统 |
| Equipment | 设备侧通信软件及设备业务系统 |
| SVID | 状态变量编号；可由 S1F3 查询 |
| ECID | 设备常量编号；可由 S2F13 查询 |
| DVID | 事件相关数据变量编号 |
| RPTID | Host 定义的报告编号 |
| CEID | 设备事件编号 |
| RCMD | 远程命令名 |
| PPID | 配方标识 |
| ALID | 报警标识 |

本版本发布前，应由双方完成第 1.1 节和第 11.2 节签认，并确认设备事件源使用本协议规定的 ASCII 参数配置。
