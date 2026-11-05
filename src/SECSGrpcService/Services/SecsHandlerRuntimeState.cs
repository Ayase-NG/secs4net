using SECShandler.Interfaces;
using SECSdata;
using System.Diagnostics;
using System.Text.Json;

namespace SECSGrpcService.Services;

public sealed class SecsHandlerRuntimeState : IDevice, IReportStorage, IEventLinkStorage, IEventEnableStorage, IAlarmEnableStorage, IAlarmStateStorage, ITimeSyncStorage, IS6F11SpoolStorage, IPortContextStorage, IJobPlanStorage, IVidValueStorage
{
    private readonly VidMap _vidMap;
    private readonly object _stateLock = new();
    private readonly Dictionary<uint, List<uint>> _reports = new();
    private readonly Dictionary<uint, List<uint>> _eventLinks = new();
    private readonly HashSet<uint> _enabledEvents = new();
    private readonly HashSet<uint> _enabledAlarms = new();
    private readonly Dictionary<uint, S5F6_alarm_data> _activeAlarms = new();
    private volatile bool _allEventsEnabled;
    private volatile bool _allAlarmsEnabled = true;
    private readonly string _snapshotPath;
    private readonly FileSystemWatcher? _snapshotWatcher;
    private readonly object _snapshotIoLock = new();
    private volatile bool _suppressSnapshotReload;
    private DateTime? _lastHostTimeUtc;
    private string _lastHostTimeRaw = string.Empty;
    private DateTime? _equipmentTimeAnchorUtc;
    private long _equipmentTimeAnchorTimestamp;
    private readonly List<S6F11_data> _s6f11Spool = new();
    private readonly int _s6f11SpoolMaxCount;
    private readonly Dictionary<string, PortRuntimeContext> _portContexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProcessJobPlan> _processJobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ControlJobPlan> _controlJobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _vidValuesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ushort, string> _vidValuesByVid = new();
    private DeviceOnlineState _isOnline = DeviceOnlineState.OffLine;

    public SecsHandlerRuntimeState(VidMap vidMap)
    {
        _vidMap = vidMap;
        _s6f11SpoolMaxCount = 1000;

        // 统一快照文件路径，当前放在应用目录下，便于部署与排查。
        _snapshotPath = Path.Combine(AppContext.BaseDirectory, "secs-event-runtime-state.json");

        // 启动关键节点：进程启动时先尝试从快照恢复 S2F33/35/37 运行态。
        TryLoadSnapshotFromDisk("startup");

        // 启动关键节点：监听快照文件变化，实现热更新（外部修改文件后自动生效）。
        var directory = Path.GetDirectoryName(_snapshotPath);
        var fileName = Path.GetFileName(_snapshotPath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            _snapshotWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            _snapshotWatcher.Changed += OnSnapshotFileChanged;
            _snapshotWatcher.Created += OnSnapshotFileChanged;
            _snapshotWatcher.Renamed += OnSnapshotFileChanged;
        }

        // 启动关键节点：初始化在线状态相关动态值，供 Host 侧 VID 组合直接读取。
        UpdateOnlineDynamicValuesNoLock(_isOnline);
    }

    // ===== IPortContextStorage =====
    public void Upsert(PortRuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var key = (context.PortId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        lock (_stateLock)
        {
            _portContexts[key] = new PortRuntimeContext
            {
                PortId = key,
                CarrierId = context.CarrierId ?? string.Empty,
                LotId = context.LotId ?? string.Empty,
                SlotsList = context.SlotsList ?? string.Empty,
                UpdatedAtUtc = DateTime.UtcNow
            };
        }

        PersistSnapshotNoThrow();
    }

    public bool TryGetByPortId(string portId, out PortRuntimeContext context)
    {
        context = new PortRuntimeContext();
        var key = (portId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        lock (_stateLock)
        {
            if (!_portContexts.TryGetValue(key, out var value))
            {
                return false;
            }

            context = ClonePortContext(value);
            return true;
        }
    }

    public bool TryGetByLotId(string lotId, out PortRuntimeContext context)
    {
        context = new PortRuntimeContext();
        var key = (lotId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        lock (_stateLock)
        {
            var hit = _portContexts.Values.FirstOrDefault(x => string.Equals(x.LotId, key, StringComparison.OrdinalIgnoreCase));
            if (hit is null)
            {
                return false;
            }

            context = ClonePortContext(hit);
            return true;
        }
    }

    public void RemoveByPortId(string portId)
    {
        var key = (portId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        lock (_stateLock)
        {
            _portContexts.Remove(key);
        }

        PersistSnapshotNoThrow();
    }

    public IReadOnlyList<PortRuntimeContext> GetAll()
    {
        lock (_stateLock)
        {
            return _portContexts.Values.Select(ClonePortContext).ToList();
        }
    }

    // ===== IJobPlanStorage =====
    public void UpsertProcessJob(ProcessJobPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var key = (plan.PJID ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        lock (_stateLock)
        {
            _processJobs[key] = CloneProcessJobPlan(plan);
            _processJobs[key].UpdatedAtUtc = DateTime.UtcNow;
        }

        PersistSnapshotNoThrow();
    }

    public bool TryGetProcessJob(string pjId, out ProcessJobPlan plan)
    {
        plan = new ProcessJobPlan();
        var key = (pjId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        lock (_stateLock)
        {
            if (!_processJobs.TryGetValue(key, out var value))
            {
                return false;
            }

            plan = CloneProcessJobPlan(value);
            return true;
        }
    }

    public IReadOnlyList<ProcessJobPlan> GetAllProcessJobs()
    {
        lock (_stateLock)
        {
            return _processJobs.Values.Select(CloneProcessJobPlan).ToList();
        }
    }

    public void UpsertControlJob(ControlJobPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var key = (plan.CJID ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        lock (_stateLock)
        {
            _controlJobs[key] = CloneControlJobPlan(plan);
            _controlJobs[key].UpdatedAtUtc = DateTime.UtcNow;
        }

        PersistSnapshotNoThrow();
    }

    public bool TryGetControlJob(string cjId, out ControlJobPlan plan)
    {
        plan = new ControlJobPlan();
        var key = (cjId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        lock (_stateLock)
        {
            if (!_controlJobs.TryGetValue(key, out var value))
            {
                return false;
            }

            plan = CloneControlJobPlan(value);
            return true;
        }
    }

    public IReadOnlyList<ControlJobPlan> GetAllControlJobs()
    {
        lock (_stateLock)
        {
            return _controlJobs.Values.Select(CloneControlJobPlan).ToList();
        }
    }

    // ===== IVidValueStorage =====
    public void UpsertByName(string cpName, string value)
    {
        UpsertValue(cpName, value);
    }

    public void UpsertByVid(ushort vid, string value)
    {
        UpsertValue(vid, value);
    }

    public bool TryGetByName(string cpName, out string value)
    {
        value = string.Empty;
        var key = (cpName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        lock (_stateLock)
        {
            return _vidValuesByName.TryGetValue(key, out value!);
        }
    }

    public bool TryGetByVid(ushort vid, out string value)
    {
        lock (_stateLock)
        {
            return _vidValuesByVid.TryGetValue(vid, out value!);
        }
    }

    /// <summary>
    /// 通过参数名写入动态值。
    /// 作用：入口层按 Name->VID 映射转换后，统一写入 Name 与 VID 两侧缓存。
    /// </summary>
    public void UpsertValue(string cpName, string value)
    {
        var key = (cpName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        lock (_stateLock)
        {
            if (_vidMap.TryGetVid(key, out var vid))
            {
                UpsertNoLock(key, vid, value ?? string.Empty);
            }
            else
            {
                // 兜底分支：Name 未映射到 VID 时先写 Name 侧，避免值丢失。
                _vidValuesByName[key] = value ?? string.Empty;
            }
        }
    }

    /// <summary>
    /// 通过 VID 写入动态值。
    /// 作用：入口层按 VID->Name 映射转换后，统一写入 Name 与 VID 两侧缓存。
    /// </summary>
    public void UpsertValue(ushort vid, string value)
    {
        lock (_stateLock)
        {
            if (_vidMap.TryGetName(vid, out var name) && !string.IsNullOrWhiteSpace(name))
            {
                UpsertNoLock(name.Trim(), vid, value ?? string.Empty);
            }
            else
            {
                // 兜底分支：VID 未映射到 Name 时先写 VID 侧，避免值丢失。
                _vidValuesByVid[vid] = value ?? string.Empty;
            }
        }
    }

    public void MarkProcessJobAutoStarted(string pjId, string portId, string lotId)
    {
        var key = (pjId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        lock (_stateLock)
        {
            if (!_processJobs.TryGetValue(key, out var plan))
            {
                return;
            }

            plan.AutoStarted = true;
            plan.AutoStartedPortId = portId ?? string.Empty;
            plan.AutoStartedLotId = lotId ?? string.Empty;
            plan.UpdatedAtUtc = DateTime.UtcNow;
        }

        PersistSnapshotNoThrow();
    }

    // ===== ITimeSyncStorage =====
    /// <summary>
    /// 原子设置设备逻辑时钟及同步记录；使用单调计时，避免后续系统校时导致跳变。
    /// </summary>
    public void UpdateHostTime(DateTime hostTimeUtc, string rawTimeText)
    {
        // 存储层只接受明确的 UTC，禁止隐式时区转换。
        if (hostTimeUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Host time must be UTC.", nameof(hostTimeUtc));
        ArgumentNullException.ThrowIfNull(rawTimeText);

        lock (_stateLock)
        {
            _equipmentTimeAnchorUtc = hostTimeUtc;
            _equipmentTimeAnchorTimestamp = Stopwatch.GetTimestamp();
            _lastHostTimeUtc = hostTimeUtc;
            _lastHostTimeRaw = rawTimeText;
        }

        PersistSnapshotNoThrow();
    }

    /// <summary>
    /// 读取 Host 设置值加单调经过时间；重启后需重新同步，历史快照不作为时钟锚点。
    /// </summary>
    public DateTime GetEquipmentTimeUtc()
    {
        lock (_stateLock)
        {
            // 未收到本次进程的同步请求时，使用系统 UTC 而非快照中的历史时间。
            return _equipmentTimeAnchorUtc is { } anchor
                ? anchor.Add(Stopwatch.GetElapsedTime(_equipmentTimeAnchorTimestamp))
                : DateTime.UtcNow;
        }
    }

    public DateTime? GetLastHostTimeUtc()
    {
        lock (_stateLock)
        {
            return _lastHostTimeUtc;
        }
    }

    public string GetLastHostTimeRaw()
    {
        lock (_stateLock)
        {
            return _lastHostTimeRaw;
        }
    }

    // ===== IS6F11SpoolStorage =====
    public void Enqueue(S6F11_data data)
    {
        ArgumentNullException.ThrowIfNull(data);

        lock (_stateLock)
        {
            _s6f11Spool.Add(CloneS6F11(data));

            // 超过上限时丢弃最旧项，避免缓存无限增长。
            if (_s6f11Spool.Count > _s6f11SpoolMaxCount)
            {
                var removeCount = _s6f11Spool.Count - _s6f11SpoolMaxCount;
                _s6f11Spool.RemoveRange(0, removeCount);
            }
        }

        PersistSnapshotNoThrow();
    }

    public IReadOnlyList<S6F11_data> PeekBatch(int maxCount)
    {
        if (maxCount <= 0)
        {
            return Array.Empty<S6F11_data>();
        }

        lock (_stateLock)
        {
            return _s6f11Spool
                .Take(maxCount)
                .Select(CloneS6F11)
                .ToList();
        }
    }

    public void AckBatch(int count)
    {
        if (count <= 0)
        {
            return;
        }

        lock (_stateLock)
        {
            var actual = Math.Min(count, _s6f11Spool.Count);
            if (actual > 0)
            {
                _s6f11Spool.RemoveRange(0, actual);
            }
        }

        PersistSnapshotNoThrow();
    }

    public int Count
    {
        get
        {
            lock (_stateLock)
            {
                return _s6f11Spool.Count;
            }
        }
    }

    // ===== IDevice =====
    public DeviceOnlineState IsOnline
    {
        get
        {
            lock (_stateLock)
            {
                return _isOnline;
            }
        }
        set
        {
            lock (_stateLock)
            {
                _isOnline = value;

                // 在线状态变更时同步刷新 OnlineStatus/RemoteStatus 动态值。
                UpdateOnlineDynamicValuesNoLock(value);
            }
        }
    }
    public string Mode { get; set; } = "Auto";
    public DeviceRunStatus RunStatus { get; set; } = DeviceRunStatus.Unknown;
    public string ModelNumber { get; set; } = "GWM-300-CW";
    public string SoftwareRevision { get; set; } = "Alpha-1.0.0";
    public Task StartProcessAsync(string? lotId) => Task.CompletedTask;

    // ===== IReportStorage =====
    public void AddOrUpdateReport(uint rptId, List<uint> vidList)
    {
        lock (_stateLock)
        {
            // 空列表按空定义处理，避免 null 传播。
            _reports[rptId] = vidList ?? [];
        }

        PersistSnapshotNoThrow();
    }

    public bool ContainsReport(uint rptId)
    {
        lock (_stateLock)
        {
            return _reports.ContainsKey(rptId);
        }
    }

    public IReadOnlyList<uint> GetVidsForReport(uint rptId)
    {
        lock (_stateLock)
        {
            if (_reports.TryGetValue(rptId, out var vids))
            {
                return vids.ToList();
            }

            return Array.Empty<uint>();
        }
    }

    public void ClearAllReports()
    {
        lock (_stateLock)
        {
            _reports.Clear();
        }

        PersistSnapshotNoThrow();
    }

    public void RemoveReport(uint rptId)
    {
        lock (_stateLock)
        {
            _reports.Remove(rptId);
        }

        PersistSnapshotNoThrow();
    }

    // ===== IEventLinkStorage =====
    public bool IsCeidValid(uint ceid)
    {
        // 当前运行态默认认为 CEID 有效。
        return true;
    }

    public IReadOnlyList<uint> GetRptIdsForCeid(uint ceid)
    {
        lock (_stateLock)
        {
            // 返回副本，避免外部误改内部状态。
            return _eventLinks.TryGetValue(ceid, out var list) ? list.ToList() : Array.Empty<uint>();
        }
    }

    public void UpdateEventLinks(Dictionary<uint, List<uint>> links)
    {
        lock (_stateLock)
        {
            _eventLinks.Clear();

            // 空输入按清空处理。
            if (links is not null)
            {
                foreach (var kv in links)
                {
                    _eventLinks[kv.Key] = kv.Value ?? [];
                }
            }
        }

        PersistSnapshotNoThrow();
    }

    public void UnlinkEvent(uint ceid)
    {
        lock (_stateLock)
        {
            _eventLinks.Remove(ceid);
        }

        PersistSnapshotNoThrow();
    }

    // ===== IEventEnableStorage =====
    public void EnableAllEvents()
    {
        _allEventsEnabled = true;
        PersistSnapshotNoThrow();
    }

    public void EnableEvent(uint ceid)
    {
        lock (_stateLock)
        {
            _enabledEvents.Add(ceid);
        }

        PersistSnapshotNoThrow();
    }

    public void DisableEvent(uint ceid)
    {
        lock (_stateLock)
        {
            _enabledEvents.Remove(ceid);
        }

        PersistSnapshotNoThrow();
    }

    public bool IsEventEnabled(uint ceid)
    {
        if (_allEventsEnabled) return true;

        lock (_stateLock)
        {
            return _enabledEvents.Contains(ceid);
        }
    }

    public IReadOnlyList<uint> GetAllEnabledEvents()
    {
        lock (_stateLock)
        {
            return _enabledEvents.ToList();
        }
    }

    // ===== IAlarmEnableStorage =====
    public void SetAlarmEnabled(uint alid, bool enabled)
    {
        lock (_stateLock)
        {
            if (enabled)
            {
                _enabledAlarms.Add(alid);
            }
            else
            {
                _enabledAlarms.Remove(alid);
            }
        }

        PersistSnapshotNoThrow();
    }

    public void SetAllAlarmsEnabled(bool enabled)
    {
        _allAlarmsEnabled = enabled;
        PersistSnapshotNoThrow();
    }

    public bool IsAlarmEnabled(uint alid)
    {
        if (_allAlarmsEnabled)
        {
            return true;
        }

        lock (_stateLock)
        {
            return _enabledAlarms.Contains(alid);
        }
    }

    public IReadOnlyList<uint> GetEnabledAlarmIds()
    {
        lock (_stateLock)
        {
            return _enabledAlarms.ToList();
        }
    }

    // ===== IAlarmStateStorage =====
    public void UpsertAlarm(uint alid, byte alcd, string altx)
    {
        lock (_stateLock)
        {
            _activeAlarms[alid] = new S5F6_alarm_data
            {
                ALID = alid,
                ALCD = alcd,
                ALTX = altx ?? string.Empty
            };
        }

        PersistSnapshotNoThrow();
    }

    public void ClearAlarm(uint alid)
    {
        lock (_stateLock)
        {
            _activeAlarms.Remove(alid);
        }

        PersistSnapshotNoThrow();
    }

    public IReadOnlyList<S5F6_alarm_data> GetActiveAlarms(IReadOnlyCollection<uint>? alidFilter)
    {
        lock (_stateLock)
        {
            if (alidFilter is null || alidFilter.Count == 0)
            {
                return _activeAlarms.Values
                    .Select(x => new S5F6_alarm_data { ALID = x.ALID, ALCD = x.ALCD, ALTX = x.ALTX })
                    .ToList();
            }

            return _activeAlarms
                .Where(kv => alidFilter.Contains(kv.Key))
                .Select(kv => new S5F6_alarm_data { ALID = kv.Value.ALID, ALCD = kv.Value.ALCD, ALTX = kv.Value.ALTX })
                .ToList();
        }
    }

    // 快照文件变化时触发热加载。
    private void OnSnapshotFileChanged(object sender, FileSystemEventArgs e)
    {
        // 仅处理目标文件，避免同目录其他文件干扰。
        if (!string.Equals(Path.GetFullPath(e.FullPath), Path.GetFullPath(_snapshotPath), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 忽略进程内持久化触发的文件事件，避免把最新内存状态回滚为旧快照。
        if (_suppressSnapshotReload)
        {
            return;
        }

        TryLoadSnapshotFromDisk("hot-reload");
    }

    // 将当前 S2F33/35/37 运行态落盘，供重启恢复与热更新使用。
    private void PersistSnapshotNoThrow()
    {
        RuntimeSnapshot snapshot;
        lock (_stateLock)
        {
            snapshot = new RuntimeSnapshot
            {
                Reports = _reports.ToDictionary(k => k.Key, v => v.Value.ToList()),
                EventLinks = _eventLinks.ToDictionary(k => k.Key, v => v.Value.ToList()),
                EnabledEvents = _enabledEvents.ToList(),
                AllEventsEnabled = _allEventsEnabled,
                EnabledAlarms = _enabledAlarms.ToList(),
                AllAlarmsEnabled = _allAlarmsEnabled,
                ActiveAlarms = _activeAlarms.ToDictionary(k => k.Key, v => new RuntimeAlarmSnapshot
                {
                    ALCD = v.Value.ALCD,
                    ALTX = v.Value.ALTX
                }),
                LastHostTimeUtc = _lastHostTimeUtc,
                LastHostTimeRaw = _lastHostTimeRaw,
                S6F11Spool = _s6f11Spool.Select(CloneS6F11).ToList(),
                PortContexts = _portContexts.Values.Select(ClonePortContext).ToList(),
                ProcessJobs = _processJobs.Values.Select(CloneProcessJobPlan).ToList(),
                ControlJobs = _controlJobs.Values.Select(CloneControlJobPlan).ToList()
            };
        }

        try
        {
            // 持久化期间临时屏蔽热加载，避免 FileSystemWatcher 读到中间版本并覆盖当前内存态。
            lock (_snapshotIoLock)
            {
                _suppressSnapshotReload = true;

                var directory = Path.GetDirectoryName(_snapshotPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                var tempPath = _snapshotPath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Copy(tempPath, _snapshotPath, overwrite: true);
                File.Delete(tempPath);
            }
        }
        catch
        {
            // 兜底分支：持久化失败不影响主流程，避免阻塞协议处理。
        }
        finally
        {
            // finally 无论持久化是否成功都恢复热加载能力，避免后续外部修改失效。
            _suppressSnapshotReload = false;
        }
    }

    // 从快照文件恢复运行态，用于启动恢复和热更新。
    private void TryLoadSnapshotFromDisk(string source)
    {
        try
        {
            if (!File.Exists(_snapshotPath))
            {
                return;
            }

            var json = File.ReadAllText(_snapshotPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var snapshot = JsonSerializer.Deserialize<RuntimeSnapshot>(json);
            if (snapshot is null)
            {
                return;
            }

            lock (_stateLock)
            {
                _reports.Clear();
                if (snapshot.Reports is not null)
                {
                    foreach (var kv in snapshot.Reports)
                    {
                        _reports[kv.Key] = kv.Value ?? [];
                    }
                }

                _eventLinks.Clear();
                if (snapshot.EventLinks is not null)
                {
                    foreach (var kv in snapshot.EventLinks)
                    {
                        _eventLinks[kv.Key] = kv.Value ?? [];
                    }
                }

                _enabledEvents.Clear();
                if (snapshot.EnabledEvents is not null)
                {
                    foreach (var ceid in snapshot.EnabledEvents)
                    {
                        _enabledEvents.Add(ceid);
                    }
                }

                _allEventsEnabled = snapshot.AllEventsEnabled;

                _enabledAlarms.Clear();
                if (snapshot.EnabledAlarms is not null)
                {
                    foreach (var alid in snapshot.EnabledAlarms)
                    {
                        _enabledAlarms.Add(alid);
                    }
                }

                _allAlarmsEnabled = snapshot.AllAlarmsEnabled;

                _activeAlarms.Clear();
                if (snapshot.ActiveAlarms is not null)
                {
                    foreach (var kv in snapshot.ActiveAlarms)
                    {
                        _activeAlarms[kv.Key] = new S5F6_alarm_data
                        {
                            ALID = kv.Key,
                            ALCD = kv.Value.ALCD,
                            ALTX = kv.Value.ALTX ?? string.Empty
                        };
                    }
                }

                _lastHostTimeUtc = snapshot.LastHostTimeUtc;
                _lastHostTimeRaw = snapshot.LastHostTimeRaw ?? string.Empty;

                _s6f11Spool.Clear();
                if (snapshot.S6F11Spool is not null)
                {
                    _s6f11Spool.AddRange(snapshot.S6F11Spool.Select(CloneS6F11));
                }

                _portContexts.Clear();
                if (snapshot.PortContexts is not null)
                {
                    foreach (var ctx in snapshot.PortContexts)
                    {
                        var key = (ctx.PortId ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            _portContexts[key] = ClonePortContext(ctx);
                        }
                    }
                }

                _processJobs.Clear();
                if (snapshot.ProcessJobs is not null)
                {
                    foreach (var plan in snapshot.ProcessJobs)
                    {
                        var key = (plan.PJID ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            _processJobs[key] = CloneProcessJobPlan(plan);
                        }
                    }
                }

                _controlJobs.Clear();
                if (snapshot.ControlJobs is not null)
                {
                    foreach (var plan in snapshot.ControlJobs)
                    {
                        var key = (plan.CJID ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            _controlJobs[key] = CloneControlJobPlan(plan);
                        }
                    }
                }
            }
        }
        catch
        {
            // 兜底分支：热更新/恢复失败时保留当前内存态，避免影响在线通信。
        }
    }

    private sealed class RuntimeSnapshot
    {
        public Dictionary<uint, List<uint>> Reports { get; set; } = new();
        public Dictionary<uint, List<uint>> EventLinks { get; set; } = new();
        public List<uint> EnabledEvents { get; set; } = new();
        public bool AllEventsEnabled { get; set; }
        public List<uint> EnabledAlarms { get; set; } = new();
        public bool AllAlarmsEnabled { get; set; } = true;
        public Dictionary<uint, RuntimeAlarmSnapshot> ActiveAlarms { get; set; } = new();
        public DateTime? LastHostTimeUtc { get; set; }
        public string LastHostTimeRaw { get; set; } = string.Empty;
        public List<S6F11_data> S6F11Spool { get; set; } = new();
        public List<PortRuntimeContext> PortContexts { get; set; } = new();
        public List<ProcessJobPlan> ProcessJobs { get; set; } = new();
        public List<ControlJobPlan> ControlJobs { get; set; } = new();
    }

    private sealed class RuntimeAlarmSnapshot
    {
        public byte ALCD { get; set; }
        public string ALTX { get; set; } = string.Empty;
    }

    private static S6F11_data CloneS6F11(S6F11_data source)
    {
        return new S6F11_data
        {
            DATAID = source.DATAID,
            CEID = source.CEID,
            timeStamp = source.timeStamp,
            Reports = source.Reports
                .Select(r => new S6F11_report_data
                {
                    RPTID = r.RPTID,
                    Values = r.Values
                        .Select(v => new S6F11_parameter_data
                        {
                            VID = v.VID,
                            CPName = v.CPName,
                            CPVal = v.CPVal,
                            EncodedValue = v.EncodedValue?.ToArray()
                        })
                        .ToList()
                })
                .ToList()
        };
    }

    private static PortRuntimeContext ClonePortContext(PortRuntimeContext source)
    {
        return new PortRuntimeContext
        {
            PortId = source.PortId,
            CarrierId = source.CarrierId,
            LotId = source.LotId,
            SlotsList = source.SlotsList,
            UpdatedAtUtc = source.UpdatedAtUtc
        };
    }

    private static ProcessJobPlan CloneProcessJobPlan(ProcessJobPlan source)
    {
        return new ProcessJobPlan
        {
            PJID = source.PJID,
            RecipeId = source.RecipeId,
            AutoStart = source.AutoStart,
            PauseEvents = source.PauseEvents.ToList(),
            Carriers = source.Carriers
                .Select(x => new ProcessJobCarrierPlan
                {
                    CarrierId = x.CarrierId,
                    Slots = x.Slots.ToList()
                })
                .ToList(),
            AutoStarted = source.AutoStarted,
            AutoStartedPortId = source.AutoStartedPortId,
            AutoStartedLotId = source.AutoStartedLotId,
            UpdatedAtUtc = source.UpdatedAtUtc
        };
    }

    private static ControlJobPlan CloneControlJobPlan(ControlJobPlan source)
    {
        return new ControlJobPlan
        {
            CJID = source.CJID,
            ProcessingCtrlSpec = source.ProcessingCtrlSpec.ToList(),
            CarrierInputSpec = source.CarrierInputSpec.ToList(),
            StartMethod = source.StartMethod,
            UpdatedAtUtc = source.UpdatedAtUtc
        };
    }

    /// <summary>
    /// 根据在线状态刷新动态值表中的 OnlineStatus 与 RemoteStatus。
    /// 作用：让通信状态切换可直接被 RPTID/VID 动态上报链路复用。
    /// </summary>
    private void UpdateOnlineDynamicValuesNoLock(DeviceOnlineState state)
    {
        // OnlineRemote 时在线状态标记为 online，远程状态也标记为 remote。
        if (state == DeviceOnlineState.OnLineRemote)
        {
            UpsertByNameNoLock("OnlineStatus", "online");
            UpsertByNameNoLock("RemoteStatus", "remote");
            return;
        }

        // OnLineLocal 时标记为 online/local。
        if (state == DeviceOnlineState.OnLineLocal)
        {
            UpsertByNameNoLock("OnlineStatus", "online");
            UpsertByNameNoLock("RemoteStatus", "local");
            return;
        }

        // 兜底分支：OfflineLocal 标记为 offline/local。

        UpsertByNameNoLock("OnlineStatus", "offline");
        UpsertByNameNoLock("RemoteStatus", "local");
    }

    /// <summary>
    /// 统一写值核心方法。
    /// 作用：在同一锁上下文中同时更新 Name/VID 两侧缓存，确保值一致。
    /// </summary>
    private void UpsertNoLock(string name, ushort vid, string value)
    {
        _vidValuesByName[name] = value;
        _vidValuesByVid[vid] = value;
    }

    /// <summary>
    /// 通过 Name 在锁内写值。
    /// 作用：供内部流程（如在线状态同步）复用，自动尝试同步 VID 侧缓存。
    /// </summary>
    private void UpsertByNameNoLock(string name, string value)
    {
        if (_vidMap.TryGetVid(name, out var vid))
        {
            UpsertNoLock(name, vid, value);
            return;
        }

        _vidValuesByName[name] = value;
    }
}
