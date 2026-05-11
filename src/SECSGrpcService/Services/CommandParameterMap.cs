namespace SECSGrpcService.Services;

/// <summary>
/// 命令参数映射表（CPName -> ID）。
/// 数据来源：`SecsMappings/CommandParameter.csv`。
/// </summary>
public sealed class CommandParameterMap
{
    private readonly Dictionary<string, string> _nameToId;

    /// <summary>
    /// 使用已解析映射初始化。
    /// </summary>
    public CommandParameterMap(Dictionary<string, string> nameToId)
    {
        _nameToId = nameToId;
    }

    /// <summary>
    /// 根据 CPName 获取对应 ID；未命中时返回原始名称。
    /// </summary>
    public string GetIdOrName(string cpName)
    {
        if (string.IsNullOrWhiteSpace(cpName))
            return string.Empty;

        // 关键分支：映射命中则返回 ID，否则保留原始字段名以保证兼容。
        return _nameToId.TryGetValue(cpName.Trim(), out var id)
            ? id
            : cpName.Trim();
    }

    /// <summary>
    /// 从 CSV 文件构建映射实例。
    /// CSV 格式要求前两列分别为：ID,Name。
    /// </summary>
    public static CommandParameterMap LoadFromCsv(string csvPath, ILogger? logger = null)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

                var id = parts[0].Trim();
                var name = parts[1].Trim();
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                    continue;

                map[name] = id;
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
