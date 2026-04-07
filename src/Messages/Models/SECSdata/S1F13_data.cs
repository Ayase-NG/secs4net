using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECSdata
{
    /// <summary>
    /// 理论上S1F13 "Date and Time Request"不需要携带数据内容，但为了方便记录发送时间和打印日志，设置一个时间戳属性。
    /// 且考虑设备主动发送S1F13请求链接，也可以携带设备型号和软件版本等信息，作为设备的统一标识，方便后续维护和管理，可设置为空，在解析时需注意。
    /// </summary>
    public class S1F13_data
    {
        /// <summary>
        /// 设备型号,例如"GWM-PW-001"
        /// </summary>
        public string MDLN { get; set; }

        /// <summary>
        /// 软件版本,作为设备统一标识,例如"V20260407"
        /// </summary>
        public string SOFTREV { get; set; }

        /// <summary>
        /// 时间戳，用于内部记录发送时间和打印日志，单位为UTC时间
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }
}
