using Secs4Net;
using SECSdata;

namespace SECShandler.Interfaces
{
    /// <summary>
    /// 设备主动外发 SxFy 分发接口。
    /// 说明：用于承载设备侧主动上报（例如 S6F11/S5F1 等）的统一发送链路。
    /// </summary>
    public interface IActiveSxFyDispatcher
    {
        /// <summary>
        /// 发送 S6F11（Event Report Send）消息。
        /// </summary>
        /// <param name="data">待发送的 S6F11 数据模型。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>发送结果，true=发送成功，false=发送失败或会话不可用。</returns>
        Task<bool> SendS6F11Async(S6F11_data data, CancellationToken cancellationToken);
    }
}
