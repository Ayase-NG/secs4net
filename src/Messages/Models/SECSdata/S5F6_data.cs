namespace SECSdata
{
    /// <summary>
    /// S5F6 List Alarm Reply 数据模型。
    /// </summary>
    public sealed class S5F6_data
    {
        /// <summary>
        /// 报警列表。
        /// </summary>
        public List<S5F6_alarm_data> Alarms { get; set; } = new();
    }

    /// <summary>
    /// S5F6 单条报警数据。
    /// </summary>
    public sealed class S5F6_alarm_data
    {
        public byte ALCD { get; set; }
        public uint ALID { get; set; }
        public string ALTX { get; set; } = string.Empty;
    }
}
