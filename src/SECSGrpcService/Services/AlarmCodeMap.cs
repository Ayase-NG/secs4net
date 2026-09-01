namespace SECSGrpcService.Services;

/// <summary>
/// 报警文本映射表，数据来源为 AlarmCode.csv。
/// </summary>
public sealed class AlarmCodeMap
{
    private readonly IReadOnlyDictionary<int, string> _alarmTexts;

    /// <summary>
    /// 使用已解析的报警文本映射初始化查询服务。
    /// </summary>
    public AlarmCodeMap(IReadOnlyDictionary<int, string> alarmTexts)
    {
        _alarmTexts = alarmTexts;
    }

    /// <summary>
    /// 按报警 ID 查询对应的报警文本。
    /// </summary>
    public bool TryGetAlarmText(int alarmId, out string alarmText)
    {
        return _alarmTexts.TryGetValue(alarmId, out alarmText!);
    }

    /// <summary>
    /// 从 AlarmCode.csv 加载报警 ID 与报警文本映射。
    /// </summary>
    public static AlarmCodeMap LoadFromCsv(string csvPath, ILogger? logger = null)
    {
        var alarmTexts = new Dictionary<int, string>();
        if (!File.Exists(csvPath))
        {
            // 文件不存在时保留空映射，由调用方使用默认报警文本。
            logger?.LogWarning("AlarmCode.csv not found: {Path}", csvPath);
            return new AlarmCodeMap(alarmTexts);
        }

        try
        {
            foreach (string rawLine in File.ReadLines(csvPath))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                string line = rawLine.Trim();
                if (line.StartsWith("Code,", StringComparison.OrdinalIgnoreCase))
                {
                    // 跳过 CSV 表头。
                    continue;
                }

                string[] columns = line.Split(',', 3);
                if (columns.Length < 2
                    || !int.TryParse(columns[0].Trim(), out int alarmId)
                    || string.IsNullOrWhiteSpace(columns[1]))
                {
                    // 跳过格式不完整或报警 ID 无效的数据行。
                    continue;
                }

                alarmTexts[alarmId] = columns[1].Trim();
            }

            logger?.LogInformation("Loaded {Count} alarm text mappings from {Path}", alarmTexts.Count, csvPath);
        }
        catch (Exception ex)
        {
            // CSV 读取失败时记录错误并保留已成功解析的映射。
            logger?.LogError(ex, "Failed to load alarm text mappings from {Path}", csvPath);
        }

        return new AlarmCodeMap(alarmTexts);
    }
}
