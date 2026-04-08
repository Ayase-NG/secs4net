using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECSdata
{
    /// <summary>
    /// S2F36 "Link Event Report Acknowledge" 消息的数据模型。
    /// 设备使用此消息回复主机的 S2F35 (Link Event Report) 指令。
    /// </summary>
    public class S2F36_data
    {
        /// <summary>
        /// 链接确认码 (LRACK)，类型为 Binary (1 byte)。
        /// 指示链接操作的结果。
        /// </summary>
        public byte LRACK { get; set; }

        /// <summary>
        /// 检查链接操作是否成功。LRACK 为 0 表示成功，其他值为失败。
        /// </summary>
        public bool IsSuccess => LRACK == 0;

        /// <summary>
        /// 时间戳，用于内部记录发送时间和打印日志，单位为UTC时间
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }
}
