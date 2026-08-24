namespace SECSdata
{
    /// <summary>
    /// S7F26 格式化配方响应数据。
    /// </summary>
    public sealed class S7F26_data
    {
        public string recipeName { get; set; } = string.Empty;

        public string modelName { get; set; } = string.Empty;

        public string softwareRevision { get; set; } = string.Empty;

        public List<S7F26Command_data> commands { get; set; } = [];
    }

    /// <summary>
    /// S7F26 中的一条格式化配方命令。
    /// </summary>
    public sealed class S7F26Command_data
    {
        public ushort commandCode { get; set; }

        public List<string> parameters { get; set; } = [];
    }
}
