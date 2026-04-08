using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECShandler.Interfaces
{
    /// <summary>
    /// 事件启用状态存储接口。
    /// 用于管理哪些 CEID（收集事件 ID）当前处于启用状态。
    /// 主机通过 S2F37（启用）和 S2F39（禁用）指令控制这些状态。
    /// </summary>
    public interface IEventEnableStorage
    {
        /// <summary>
        /// 启用指定的事件。
        /// 当事件被启用后，设备在检测到该 CEID 触发时应执行上报（如果已链接报告）。
        /// </summary>
        /// <param name="ceid">要启用的事件 ID。</param>
        void EnableEvent(uint ceid);

        /// <summary>
        /// 启用所有设备支持的事件。
        /// 通常在主机发送的 CEID 列表为空时调用（S2F37 的语义）。
        /// </summary>
        void EnableAllEvents();

        /// <summary>
        /// 禁用指定的事件。
        /// 禁用后，即使该事件触发，设备也不会执行上报。
        /// </summary>
        /// <param name="ceid">要禁用的事件 ID。</param>
        void DisableEvent(uint ceid);

        /// <summary>
        /// 查询指定事件当前是否已启用。
        /// </summary>
        /// <param name="ceid">事件 ID。</param>
        /// <returns>如果已启用则返回 true，否则返回 false。</returns>
        bool IsEventEnabled(uint ceid);

        /// <summary>
        /// 获取所有已启用事件的列表（可选，用于调试或日志）。
        /// </summary>
        IReadOnlyList<uint> GetAllEnabledEvents();
    }
}
