namespace SECSdata
{
    /// <summary>
    /// S5F3 Enable/Disable Alarm Send 数据模型。
    /// 常见结构：L[2] { ALED, ALIDLIST }。
    /// </summary>
    public sealed class S5F3_data
    {
        /// <summary>
        /// 报警使能代码：0=禁用，1=启用。
        /// </summary>
        public byte ALED { get; set; }

        /// <summary>
        /// 报警 ID 列表；为空表示对全部报警生效。
        /// </summary>
        public List<uint> ALIDList { get; set; } = new();

        /// <summary>
        /// 是否对全部报警生效。
        /// </summary>
        public bool IsAllAlarms => ALIDList.Count == 0;
    }
}
