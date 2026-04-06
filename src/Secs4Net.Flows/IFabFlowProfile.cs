using Secs4Net;

namespace Secs4Net.Flows;

/// <summary>
/// Fab 客制化入口：把「业务无关」的 <see cref="FlowSendRequest"/> 组装为实际 SECS-II 主消息。
/// </summary>
/// <remarks>
/// 通用库只定义接口与默认实现；不同 Fab 工程（例如带 SGRS 标识的项目）通过继承或全新实现注入 DI。
/// </remarks>
public interface IFabFlowProfile
{
    /// <summary>配置标识，用于日志与多租户路由。</summary>
    string ProfileId { get; }

    /// <summary>
    /// 根据请求构造主消息；调用方负责在发送后 <see cref="SecsMessage.Dispose"/>（通常由 <see cref="SecsFlowDispatcher"/> 处理）。
    /// </summary>
    SecsMessage BuildPrimaryMessage(FlowSendRequest request);
}
