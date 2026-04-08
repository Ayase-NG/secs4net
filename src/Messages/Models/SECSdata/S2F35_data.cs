using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECSdata
{
    /// <summary>
    /// S2F35 "Link Event Report" 消息的数据模型。
    /// 主机通过此消息将报告(RPTID)与事件(CEID)进行绑定。
    /// </summary>
    public class S2F35_data
    {
        /// <summary>
        /// 数据ID，用于关联请求与响应。
        /// </summary>
        public byte DATAID { get; set; }

        /// <summary>
        /// 存储链接配置的核心字典。
        /// Key: CEID (事件ID)
        /// Value: 与该CEID绑定的RPTID列表
        /// </summary>
        public Dictionary<uint, List<uint>> Links { get; set; } = new();

        /// <summary>
        /// 时间戳，用于内部记录发送时间和打印日志，单位为UTC时间
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }
}
