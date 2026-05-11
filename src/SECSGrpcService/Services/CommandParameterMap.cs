namespace SECSGrpcService.Services;

/// <summary>
/// 命令参数映射表（CPName -> VID）。
/// 数据来源：`CommandParameter.csv`。
/// </summary>
public sealed class CommandParameterMap
{
    private readonly Dictionary<string, ushort> _nameToVid;

    /// <summary>
    /// 使用已解析映射初始化。
    /// </summary>
    public CommandParameterMap(Dictionary<string, ushort> nameToVid)
    {
        _nameToVid = nameToVid;
    }

    /// <summary>
    /// 尝试根据 CPName 获取 VID。
    /// </summary>
    public bool TryGetVid(string cpName, out ushort vid)
    {
        vid = 0;
        if (string.IsNullOrWhiteSpace(cpName))
            return false;

        return _nameToVid.TryGetValue(cpName.Trim(), out vid);
    }

    /// <summary>
    /// 从 CSV 文件构建映射实例。
    /// CSV 格式要求前两列分别为：ID,Name。
    /// </summary>
    public static CommandParameterMap LoadFromCsv(string csvPath, ILogger? logger = null)
    {
        var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);

        // 关键分支：文件不存在时返回空映射。
        if (!File.Exists(csvPath))
        {
            logger?.LogWarning("CommandParameter.csv not found: {Path}", csvPath);
            return new CommandParameterMap(map);
        }

        try
        {
            var lines = File.ReadAllLines(csvPath);
            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var line = raw.Trim();

                // 关键分支：跳过表头。
                if (line.StartsWith("ID,", StringComparison.OrdinalIgnoreCase))
                    continue;

                // 关键分支：只需前两列（ID,Name），其余列忽略。
                var parts = line.Split(',', 3);
                if (parts.Length < 2)
                    continue;

                var idText = parts[0].Trim();
                var name = parts[1].Trim();
                if (string.IsNullOrWhiteSpace(idText) || string.IsNullOrWhiteSpace(name))
                    continue;

                // 关键分支：仅接受可解析为 ushort 的 VID（如 2XXX 这类占位值会被忽略）。
                if (!ushort.TryParse(idText, out var vid))
                    continue;

                map[name] = vid;
            }

            logger?.LogInformation("Loaded {Count} command parameter mappings from {Path}", map.Count, csvPath);
        }
        catch (Exception ex)
        {
            // 关键分支：CSV 解析失败时降级为空映射，避免影响主流程。
            logger?.LogError(ex, "Failed to load command parameter mappings from {Path}", csvPath);
        }

        return new CommandParameterMap(map);
    }
}
