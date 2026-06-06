namespace SECSdata
{
    /// <summary>
    /// S5F5 List Alarm Request 数据模型。
    /// 常见结构：L[1] { ALIDLIST }；为空表示查询全部。
    /// </summary>
    public sealed class S5F5_data
    {
        /// <summary>
        /// 查询的报警 ID 列表；为空表示查询全部报警。
        /// </summary>
        public List<uint> ALIDList { get; set; } = new();

        /// <summary>
        /// 是否查询全部报警。
        /// </summary>
        public bool IsAllAlarms => ALIDList.Count == 0;
    }
}
