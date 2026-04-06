namespace Secs4Net.Flows;

/// <summary>
/// 与外部系统（如 gRPC）对齐的「逻辑」请求，尚未编码为 <see cref="SecsMessage"/>。
/// </summary>
/// <remarks>
/// 通用流程只约定最小字段；具体 Fab 的 SVID、Recipe 名、嵌套 List 等由 <see cref="IFabFlowProfile"/> 客制化组装。
/// </remarks>
public sealed record FlowSendRequest(
    byte Stream,
    byte Function,
    bool ReplyExpected,
    string? Name,
    /// <summary>可选：映射为单条 ASCII Item（<c>Item.A</c>）；复杂结构请在客制化 Profile 中忽略此字段并自行构建 Item 树。</summary>
    string? AsciiPayload);
