using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECSdata
{
    /// <summary>
    /// S2F37 "Enable Event Report" 消息的数据模型。
    /// 主机通过此消息启用或禁用指定的事件报告。
    /// 如果 CEID 列表为空，则操作所有事件。
    /// </summary>
    public class S2F37_data
    {
        /// <summary>
        /// 数据ID (DATAID)，类型为 U1。
        /// 用于匹配请求与响应。
        /// </summary>
        public byte DataId { get; set; }

        /// <summary>
        /// 要启用的事件 CEID 列表。
        /// 如果列表为空，表示操作所有事件。
        /// </summary>
        public List<uint> CeidList { get; set; } = new();

        /// <summary>
        /// 是否操作所有事件 (即 CeidList 为空)。
        /// </summary>
        public bool IsAllEvents => CeidList.Count == 0;

        /// <summary>
        /// 时间戳，用于内部记录发送时间和打印日志，单位为UTC时间
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }
}
