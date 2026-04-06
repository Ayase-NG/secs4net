namespace Secs4Net;

/// <summary>
/// SECS消息（SECS-II Message）
/// </summary>
/// <remarks>
/// SECS-II消息结构：
/// ┌─────────────────────────────────────────────────────┐
/// │ Length (4B) │ Header (10B)              │ Data   │
/// └─────────────────────────────────────────────────────┘
/// 
/// Header结构：
/// ┌─────────────────────────────────────────────────────┐
/// │ DeviceID │ S|W │ F │ E │ Type │ System Bytes     │
/// │ (2B)     │ (1B)│   │   │(1B) │ (4B)             │
/// └─────────────────────────────────────────────────────┘
/// 
/// 示例：
/// - S1F1 (AreYouThere)：设备查询消息
/// - S1F2 (OnLineData)：设备在线数据
/// 
/// 使用示例：
/// <code>
/// // 发送S1F1消息
/// var s1f1 = new SecsMessage(1, 1, replyExpected: true)
/// {
///     Name = "AreYouThere"
/// };
/// var reply = await secsGem.SendAsync(s1f1);
///
/// // 发送带数据的S2F13消息
/// var s2f13 = new SecsMessage(2, 13)
/// {
///     Name = "DateTimeDataReq",
///     SecsItem = Item.L(
///         Item.U4(1),  // MDLN
///         Item.A("ABC")) // SOFTREV
/// };
/// </code>
/// </remarks>
public sealed class SecsMessage : IDisposable
{
    /// <summary>
    /// 获取消息的字符串表示
    /// </summary>
    /// <returns>格式：'S{S}F{F}' [W] {Name}</returns>
    /// <example>'S1F1 W' AreYouThere</example>
    public override string ToString() => $"'S{S}F{F}' {(ReplyExpected ? "W" : string.Empty)} {Name ?? string.Empty}";

    /// <summary>
    /// 消息流编号（Stream Number）
    /// </summary>
    /// <remarks>
    /// 范围：0-127（仅使用低7位）
    /// 
    /// 标准流定义：
    /// - S1: Equipment Status
    /// - S2: Equipment Control
    /// - S3: Recipe Management
    /// - S5: Alarm Management
    /// - S6: Equipment Data Collection
    /// - S7: Recipe Transfer
    /// - S8: Calendar Time Management
    /// - S9: Host Computer Interface
    /// - S10: Terminal Services
    /// - S11-S127: User Defined
    /// </remarks>
    public byte S { get; }

    /// <summary>
    /// 消息功能编号（Function Number）
    /// </summary>
    /// <remarks>
    /// 规则：
    /// - 奇数F：主消息（Primary Message），通常需要回复
    /// - 偶数F：次消息（Secondary Message），作为回复
    /// - F=0：保留用于错误响应
    /// 
    /// 示例：
    /// - S1F1 → 需要回复 S1F2
    /// - S1F3 → 需要回复 S1F4
    /// </remarks>
    public byte F { get; }

    /// <summary>
    /// 是否需要回复消息
    /// </summary>
    /// <remarks>
    /// 当为true时，发送消息后会等待对应ID的回复消息（T3超时控制）。
    /// 对应Header中的W-Bit（最高位）。
    /// </remarks>
    public bool ReplyExpected { get; internal set; }

    /// <summary>
    /// 消息名称（可选，用于日志和调试）
    /// </summary>
    /// <remarks>
    /// 通常与SML定义中的别名对应，例如：
    /// - "AreYouThere"
    /// - "OnLineData"
    /// - "TerminalDisplay"
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>
    /// 消息的数据体（根Item）
    /// </summary>
    /// <remarks>
    /// SECS-II数据由Item树结构表示：
    /// - List：容器，包含多个子Item
    /// - 原子类型：ASCII、Binary、数值等
    /// 
    /// S1F1等无数据消息的SecsItem为null。
    /// </remarks>
    public Item? SecsItem { get; set; }

    /// <summary>
    /// 构造SECS消息
    /// </summary>
    /// <param name="s">消息流编号（0-127）</param>
    /// <param name="f">消息功能编号（0-255）</param>
    /// <param name="replyExpected">是否需要回复（默认true）</param>
    /// <exception cref="ArgumentOutOfRangeException">当s大于127时抛出</exception>
    public SecsMessage(byte s, byte f, bool replyExpected = true)
    {
        if (s > 0b0111_1111)
        {
            throw new ArgumentOutOfRangeException(nameof(s), s, Resources.SecsMessageStreamNumberMustLessThan127);
        }

        S = s;
        F = f;
        ReplyExpected = replyExpected;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    /// <remarks>
    /// 递归释放SecsItem及其所有子Item占用的内存资源。
    /// 特别是对于大数据量Item（如使用MemoryOwner的Binary数据）。
    /// </remarks>
    public void Dispose()
    {
        SecsItem?.Dispose();
    }
}
