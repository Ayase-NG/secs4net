namespace SECSdata
{
    /// <summary>
    /// S2F32 Date and Time Set Acknowledge 数据模型。
    /// </summary>
    public sealed class S2F32_data
    {
        /// <summary>
        /// TIACK：0=成功，1=拒绝，2=格式/内部错误。
        /// </summary>
        public byte TIACK { get; set; }
    }
}
