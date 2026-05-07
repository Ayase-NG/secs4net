using SECShandler.Interfaces;

namespace SECSGrpcService.Services;

public sealed class SecsHandlerRuntimeState : IDevice, IReportStorage, IEventLinkStorage, IEventEnableStorage
{
    private readonly Dictionary<uint, List<uint>> _reports = new();
    private readonly Dictionary<uint, List<uint>> _eventLinks = new();
    private readonly HashSet<uint> _enabledEvents = new();
    private volatile bool _allEventsEnabled;

    // ===== IDevice =====
    public bool IsOnline { get; set; } = false;
    public string Mode { get; set; } = "01";
    public List<uint> SlotsList { get; set; } = new();
    public DeviceRunStatus RunStatus { get; set; } = DeviceRunStatus.Unknown;
    public string ModelNumber { get; set; } = "GWM-PW-20260407";
    public string SoftwareRevision { get; set; } = "V20260407";
    public string Status { get; set; } = string.Empty;

    public Task StartProcessAsync(string? lotId) => Task.CompletedTask;
    public Task StopProcessAsync() => Task.CompletedTask;
    public Task PauseProcessAsync() => Task.CompletedTask;
    public Task ResumeProcessAsync() => Task.CompletedTask;
    public Task AbortProcessAsync() => Task.CompletedTask;
    public Task PPSelectAsync() => Task.CompletedTask;
    public Task ChangeToLocalAsync() => Task.CompletedTask;
    public Task LoadCarrierAsync() => Task.CompletedTask;
    public Task UnLoadCarrierAsync() => Task.CompletedTask;

    // ===== IReportStorage =====
    public void AddOrUpdateReport(uint rptId, List<uint> vidList)
    {
        lock (_reports)
        {
            _reports[rptId] = vidList;
        }
    }

    public bool ContainsReport(uint rptId)
    {
        lock (_reports)
        {
            return _reports.ContainsKey(rptId);
        }
    }

    public void ClearAllReports()
    {
        lock (_reports)
        {
            _reports.Clear();
        }
    }

    public void RemoveReport(uint rptId)
    {
        lock (_reports)
        {
            _reports.Remove(rptId);
        }
    }

    // ===== IEventLinkStorage =====
    public bool IsCeidValid(uint ceid)
    {
        // 当前运行态默认认为 CEID 有效。
        return true;
    }

    public IReadOnlyList<uint> GetRptIdsForCeid(uint ceid)
    {
        lock (_eventLinks)
        {
            return _eventLinks.TryGetValue(ceid, out var list) ? list : Array.Empty<uint>();
        }
    }

    public void UpdateEventLinks(Dictionary<uint, List<uint>> links)
    {
        lock (_eventLinks)
        {
            _eventLinks.Clear();
            foreach (var kv in links)
            {
                _eventLinks[kv.Key] = kv.Value;
            }
        }
    }

    public void UnlinkEvent(uint ceid)
    {
        lock (_eventLinks)
        {
            _eventLinks.Remove(ceid);
        }
    }

    // ===== IEventEnableStorage =====
    public void EnableAllEvents()
    {
        _allEventsEnabled = true;
    }

    public void EnableEvent(uint ceid)
    {
        lock (_enabledEvents)
        {
            _enabledEvents.Add(ceid);
        }
    }

    public void DisableEvent(uint ceid)
    {
        lock (_enabledEvents)
        {
            _enabledEvents.Remove(ceid);
        }
    }

    public bool IsEventEnabled(uint ceid)
    {
        if (_allEventsEnabled) return true;

        lock (_enabledEvents)
        {
            return _enabledEvents.Contains(ceid);
        }
    }

    public IReadOnlyList<uint> GetAllEnabledEvents()
    {
        lock (_enabledEvents)
        {
            return _enabledEvents.ToList();
        }
    }
}
