using Secs4Net;
using Secs4Net.Flows;

namespace Secs4Net.Flows.Sgrs;

/// <summary>
/// SGRS 相关 Fab 的客制化装配示例：在默认行为之上增加可追溯标识与可扩展挂钩点。
/// </summary>
/// <remarks>
/// <para>
/// 本类名称中的「Sgrs」表示该程序集专门承载 SGRS 产线/客户的差异逻辑，与通用库 <c>Secs4Net.Flows</c> 解耦。
/// 实际项目可将此类拆分为多个 partial class，或按 Stream 分文件维护。
/// </para>
/// <para>
/// 若需完全不同的报文结构，可不再继承 <see cref="DefaultFabFlowProfile"/>，直接实现 <see cref="IFabFlowProfile"/>。
/// </para>
/// </remarks>
public sealed class SgrsFabFlowProfile : DefaultFabFlowProfile
{
    /// <inheritdoc />
    public override string ProfileId => "sgrs";

    /// <inheritdoc />
    public override SecsMessage BuildPrimaryMessage(FlowSendRequest request)
    {
        var message = base.BuildPrimaryMessage(request);

        // SGRS：在 Name 上附加标记，便于线边日志与 Host 侧过滤（不改变 S/F 与 Item 二进制结构）。
        message.Name = string.IsNullOrEmpty(message.Name)
            ? "SGRS"
            : $"{message.Name} [SGRS]";

        // SGRS：在此可加入 ECID/SVID 映射、Recipe 名规则、超时策略等（示例略）。
        return message;
    }
}
