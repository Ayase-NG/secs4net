namespace SECSdata
{
    /// <summary>
    /// S5F4 Enable/Disable Alarm Acknowledge 数据模型。
    /// </summary>
    public sealed class S5F4_data
    {
        /// <summary>
        /// 报警使能确认码：0=成功，1=拒绝，2=格式/内部错误。
        /// </summary>
        public byte ACKC5 { get; set; }
    }
}
