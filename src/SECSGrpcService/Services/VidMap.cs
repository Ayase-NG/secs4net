namespace SECSGrpcService.Services;

/// <summary>
/// VID 映射表（CPName -> VID）。
/// 数据来源：`VID.csv`。
/// </summary>
public sealed class VidMap
{
    private readonly object _lock = new();
    private readonly Dictionary<string, ushort> _nameToVid = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ushort, string> _vidToName = new();
    private readonly string? _csvPath;
    private readonly ILogger? _logger;
    private readonly FileSystemWatcher? _watcher;

    /// <summary>
    /// 使用已解析映射初始化。
    /// </summary>
    public VidMap(Dictionary<string, ushort> nameToVid)
    {
        ReplaceMappings(nameToVid);
    }

    private VidMap(string csvPath, ILogger? logger)
    {
        _csvPath = csvPath;
        _logger = logger;

        // 启动关键节点：先加载一次文件映射。
        var loaded = ParseCsvToMap(csvPath, logger);
        ReplaceMappings(loaded);

        // 开启文件监听，支持 VID.csv 热更新。
        var dir = Path.GetDirectoryName(csvPath);
        var name = Path.GetFileName(csvPath);
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
        {
            _watcher = new FileSystemWatcher(dir, name)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };

            _watcher.Changed += OnCsvChanged;
            _watcher.Created += OnCsvChanged;
            _watcher.Renamed += OnCsvChanged;
        }
    }

    /// <summary>
    /// 尝试根据 CPName 获取 VID。
    /// </summary>
    public bool TryGetVid(string cpName, out ushort vid)
    {
        vid = 0;
        if (string.IsNullOrWhiteSpace(cpName))
            return false;

        lock (_lock)
        {
            return _nameToVid.TryGetValue(cpName.Trim(), out vid);
        }
    }

    /// <summary>
    /// 尝试根据 VID 获取 CPName。
    /// </summary>
    public bool TryGetName(ushort vid, out string name)
    {
        lock (_lock)
        {
            return _vidToName.TryGetValue(vid, out name!);
        }
    }

    /// <summary>
    /// 更新或新增单条映射，并持久化到 CSV。
    /// </summary>
    public bool Upsert(string cpName, ushort vid)
    {
        if (string.IsNullOrWhiteSpace(cpName))
            return false;

        Dictionary<string, ushort> snapshot;
        lock (_lock)
        {
            _nameToVid[cpName.Trim()] = vid;

            // if 关键分支：同 VID 旧名称被覆盖时，后写入生效。
            _vidToName[vid] = cpName.Trim();
            snapshot = _nameToVid.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
        }

        return PersistSnapshot(snapshot);
    }

    /// <summary>
    /// 删除单条映射，并持久化到 CSV。
    /// </summary>
    public bool Remove(string cpName)
    {
        if (string.IsNullOrWhiteSpace(cpName))
            return false;

        Dictionary<string, ushort> snapshot;
        lock (_lock)
        {
            if (!_nameToVid.Remove(cpName.Trim(), out var vid))
                return false;

            if (_vidToName.TryGetValue(vid, out var existed) && string.Equals(existed, cpName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                _vidToName.Remove(vid);
            }

            snapshot = _nameToVid.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
        }

        return PersistSnapshot(snapshot);
    }

    /// <summary>
    /// 从 CSV 文件构建映射实例。
    /// CSV 格式要求前两列分别为：ID,Name。
    /// </summary>
    public static VidMap LoadFromCsv(string csvPath, ILogger? logger = null)
    {
        // 关键分支：通过带路径构造创建实例，使其支持热更新与可持久化更新。
        return new VidMap(csvPath, logger);
    }

    private static Dictionary<string, ushort> ParseCsvToMap(string csvPath, ILogger? logger = null)
    {
        var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);

        // 关键分支：文件不存在时返回空映射。
        if (!File.Exists(csvPath))
        {
            logger?.LogWarning("VID.csv not found: {Path}", csvPath);
            return map;
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

        return map;
    }

    private void ReplaceMappings(Dictionary<string, ushort> map)
    {
        lock (_lock)
        {
            _nameToVid.Clear();
            _vidToName.Clear();

            foreach (var kv in map)
            {
                _nameToVid[kv.Key] = kv.Value;
                _vidToName[kv.Value] = kv.Key;
            }
        }
    }

    private void OnCsvChanged(object? sender, FileSystemEventArgs e)
    {
        if (_csvPath is null)
            return;

        // 兜底分支：热更新失败不影响当前内存映射。
        try
        {
            var loaded = ParseCsvToMap(_csvPath, _logger);
            ReplaceMappings(loaded);
            _logger?.LogInformation("VID map hot reloaded from {Path}", _csvPath);
        }
        catch
        {
        }
    }

    private bool PersistSnapshot(Dictionary<string, ushort> map)
    {
        if (string.IsNullOrWhiteSpace(_csvPath))
            return false;

        try
        {
            var lines = new List<string> { "ID,Name,Category,Format,Description" };
            foreach (var kv in map.OrderBy(k => k.Value))
            {
                lines.Add($"{kv.Value},{kv.Key},,,,".TrimEnd(','));
            }

            var tmp = _csvPath + ".tmp";
            File.WriteAllLines(tmp, lines);
            File.Copy(tmp, _csvPath, true);
            File.Delete(tmp);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to persist VID map to {Path}", _csvPath);
            return false;
        }
    }
}
