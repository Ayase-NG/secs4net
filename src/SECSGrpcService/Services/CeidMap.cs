namespace SECSGrpcService.Services;

/// <summary>
/// CEID 映射表（EventName -> CEID）。
/// 数据来源：CEID.csv。
/// </summary>
public sealed class CeidMap
{
    private readonly Dictionary<string, uint> _eventNameToCeid;
    private readonly Dictionary<uint, string> _ceidToEventName;

    public CeidMap(Dictionary<string, uint> eventNameToCeid)
    {
        _eventNameToCeid = new Dictionary<string, uint>(eventNameToCeid, StringComparer.OrdinalIgnoreCase);
        _ceidToEventName = new Dictionary<uint, string>();

        foreach (var kv in _eventNameToCeid)
        {
            _ceidToEventName[kv.Value] = kv.Key;
        }
    }

    /// <summary>
    /// 通过事件名查 CEID。
    /// </summary>
    public bool TryGetCeid(string eventName, out uint ceid)
    {
        ceid = 0;
        if (string.IsNullOrWhiteSpace(eventName))
            return false;

        return _eventNameToCeid.TryGetValue(eventName.Trim(), out ceid);
    }

    /// <summary>
    /// 通过 CEID 查事件名。
    /// </summary>
    public bool TryGetEventName(uint ceid, out string eventName)
    {
        return _ceidToEventName.TryGetValue(ceid, out eventName!);
    }

    /// <summary>
    /// 从 CSV 文件构建 CEID 映射。
    /// CSV 前两列：CEID,EventName。
    /// </summary>
    public static CeidMap LoadFromCsv(string csvPath, ILogger? logger = null)
    {
        var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(csvPath))
        {
            logger?.LogWarning("CEID.csv not found: {Path}", csvPath);
            return new CeidMap(map);
        }

        try
        {
            var lines = File.ReadAllLines(csvPath);
            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var line = raw.Trim();
                if (line.StartsWith("CEID,", StringComparison.OrdinalIgnoreCase))
                    continue;

                var parts = line.Split(',', 3);
                if (parts.Length < 2)
                    continue;

                var ceidText = parts[0].Trim();
                var eventName = parts[1].Trim();
                if (string.IsNullOrWhiteSpace(ceidText) || string.IsNullOrWhiteSpace(eventName))
                    continue;

                if (!uint.TryParse(ceidText, out var ceid))
                    continue;

                map[eventName] = ceid;
            }

            logger?.LogInformation("Loaded {Count} CEID mappings from {Path}", map.Count, csvPath);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to load CEID mappings from {Path}", csvPath);
        }

        return new CeidMap(map);
    }
}
