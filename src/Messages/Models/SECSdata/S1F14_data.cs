using System;

namespace SECSdata
{
    /// <summary>
    /// S1F14 建立通信应答数据模型。
    /// SML样例：&lt;L [2] &lt;B 0x00&gt; &lt;L [2] &lt;A "GWM-PW-001"&gt; &lt;A "V20260407"&gt; &gt; &gt;。
    /// </summary>
    public class S1F14_data
    {
        /// <summary>
        /// 建立通信确认代码 ，"0"表示通信建立成功，其他值表示通信建立失败的原因，例如"1"表示通信版本不兼容，"2"表示设备忙等。例:"B: 1 0x00"
        /// </summary>
        public byte COMMACK { get; set; }

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
