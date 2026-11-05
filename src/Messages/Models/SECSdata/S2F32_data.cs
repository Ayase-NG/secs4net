namespace SECSdata
{
    /// <summary>
    /// S2F32 Date and Time Set Acknowledge 数据模型。
    /// </summary>
    public sealed class S2F32_data
    {
        /// <summary>
        /// TIACK：0=完成，1=未完成；其他值由 SECS 保留，不用于格式或内部错误。
        /// </summary>
        public byte TIACK { get; set; }
    }
}
