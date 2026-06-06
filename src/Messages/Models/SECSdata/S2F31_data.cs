namespace SECSdata
{
    /// <summary>
    /// S2F31 Date and Time Set Request 数据模型。
    /// </summary>
    public sealed class S2F31_data
    {
        /// <summary>
        /// Host 下发时间的原始字符串（ASCII）。
        /// </summary>
        public string TimeText { get; set; } = string.Empty;

        /// <summary>
        /// 解析后的 UTC 时间。
        /// </summary>
        public DateTime HostTimeUtc { get; set; }
    }
}
