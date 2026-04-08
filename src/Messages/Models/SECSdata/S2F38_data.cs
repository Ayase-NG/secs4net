using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECSdata
{
    /// <summary>
    /// S2F38 "Enable Event Report Acknowledge" 消息的数据模型。
    /// 设备使用此消息回复主机的 S2F37 (Enable Event Report) 指令。
    /// </summary>
    public class S2F38_data
    {
        /// <summary>
        /// 启用确认码 (EAC)，类型为 Binary (1 byte)。
        /// 指示启用/禁用操作的结果。
        /// </summary>
        public byte EAC { get; set; }

        /// <summary>
        /// 检查操作是否成功。EAC 为 0 表示成功，其他值为失败。
        /// </summary>
        public bool IsSuccess => EAC == 0;

        /// <summary>
        /// 时间戳，用于内部记录发送时间和打印日志，单位为UTC时间
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }
}
