using System;

namespace SECSdata
{
    public class S1F2_data
    {
        /// <summary>
        /// 设备型号,例如"GWM-PW-001"
        /// </summary>
        public string MDLN { get; set; } = string.Empty;

        /// <summary>
        /// 软件版本,作为设备统一标识,例如"V20260407"
        /// </summary>
        public string SOFTREV { get; set; } = string.Empty;

        /// <summary>
        /// 时间戳，用于内部记录发送时间和打印日志，单位为UTC时间
        /// </summary>
        public DateTime timeStamp { get; set; } = DateTime.UtcNow;
    }
}
