using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECSdata
{
    /// <summary>
    /// 表示 S2F34 "Define Report Acknowledge" 消息的数据模型。
    /// 设备使用此消息回复主机的 S2F33 指令。
    /// SML样例：&lt;B 0x00&gt;。
    /// </summary>
    public class S2F34_data
    {
        /// <summary>
        /// 定义报告确认码 (DRACK)。
        /// 指示报告定义操作的结果。
        /// </summary>
        public byte DRACK { get; set; }

        /// <summary>
        /// 检查操作是否成功。DRACK 为 0 表示成功，其他值为失败。
        /// </summary>
        public bool IsSuccess => DRACK == 0;

        /// <summary>
        /// 时间戳，用于内部记录发送时间和打印日志，单位为UTC时间
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }
}
