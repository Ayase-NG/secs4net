using System.Text.Json;
using Microsoft.Extensions.Logging;
using SECShandler.Interfaces;

namespace SECShandler.Functions;

/// <summary>
/// 统一的事件驱动执行流程：确认 Carrier、Mapping，领取执行权，选择配方、启动并标记 Processing。
/// </summary>
public sealed class ProcessJobExecutionCoordinator : IProcessJobExecutionCoordinator
{
    private readonly IJobPlanStorage _jobs;
    private readonly IProcessJobMaterialSource _materials;
    private readonly IProcessJobExecutionClient _client;
    private readonly IDevice _device;
    private readonly ISecsInteractionHistoryStore _history;
    private readonly ILogger<ProcessJobExecutionCoordinator> _logger;

    /// <summary>
    /// 注入共享活动作业存储及物料、执行适配器，使各触发点使用同一个执行流程。
    /// </summary>
    public ProcessJobExecutionCoordinator(IJobPlanStorage jobs, IProcessJobMaterialSource materials,
        IProcessJobExecutionClient client, IDevice device, ISecsInteractionHistoryStore history,
        ILogger<ProcessJobExecutionCoordinator> logger)
    {
        _jobs = jobs;
        _materials = materials;
        _client = client;
        _device = device;
        _history = history;
        _logger = logger;
    }

    /// <summary>
    /// 每次触发最多启动一个 Carrier；手动启动计划、未就绪物料和设备忙碌时不下发任何命令。
    /// </summary>
    public async Task<bool> TryExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_device.IsOnline != DeviceOnlineState.OnLineRemote || _device.RunStatus != DeviceRunStatus.Idel)
            return false;

        foreach (var plan in _jobs.GetAllProcessJobs().Where(plan => plan.AutoStart))
        {
            foreach (var carrier in plan.Carriers)
            {
                // 只处理未执行任务，Processing、终态及待核实状态均不能被重复启动。
                if (carrier.State is not (ProcessJobCarrierState.Waiting or ProcessJobCarrierState.Ready))
                    continue;
                var port = FindReadyPort(_materials.GetPorts(), carrier);
                if (port is null)
                    continue;
                var previousState = carrier.State;
                if (!_jobs.TryBeginCarrierExecution(plan.PJID, carrier.CarrierId))
                    return false;

                try
                {
                    // 领取后再确认物料和远程状态；尚未调用设备时可以安全退回原状态。
                    var current = FindReadyPort(_materials.GetPorts(), carrier);
                    if (_device.IsOnline != DeviceOnlineState.OnLineRemote || _device.RunStatus != DeviceRunStatus.Idel
                        || current is null || current.PortId != port.PortId)
                    {
                        _jobs.TryUpdateCarrierState(plan.PJID, carrier.CarrierId, ProcessJobCarrierState.Starting, previousState);
                        return false;
                    }
                    var request = new ProcessJobExecutionRequest(plan.PJID, carrier.CarrierId, current.PortId,
                        plan.RecipeId, _device.Mode, current.LotId, carrier.Slots.ToArray());
                    // 异步记录设备启动前的关键上下文，便于跨 gRPC 和 PJ 追溯。
                    await RecordAsync(request, "Starting", cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    // 异步在同一目标实例依次选择配方和启动；异常或超时不可假定设备未执行。
                    await _client.SelectRecipeAndStartAsync(request, cancellationToken).ConfigureAwait(false);
                    // 仅明确成功后更新 Processing，不能覆盖完成事件已经写入的状态。
                    if (!_jobs.TryUpdateCarrierState(plan.PJID, carrier.CarrierId,
                        ProcessJobCarrierState.Starting, ProcessJobCarrierState.Processing))
                    {
                        _logger.LogWarning("PJ {PJID}, Carrier {CarrierID} started but its state was changed by another event.", plan.PJID, carrier.CarrierId);
                        return false;
                    }
                    // 异步记录启动成功；追溯失败不重复下发命令。
                    await RecordAsync(request, "Processing", cancellationToken).ConfigureAwait(false);
                    return true;
                }
                catch (Exception ex)
                {
                    // 保留占用等待核实，避免超时后在下一触发点再次启动；服务取消也不能清除不确定结果。
                    _jobs.TryUpdateCarrierState(plan.PJID, carrier.CarrierId,
                        ProcessJobCarrierState.Starting, ProcessJobCarrierState.StartUncertain);
                    _logger.LogError(ex, "PJ {PJID}, Carrier {CarrierID} execution requires reconciliation.", plan.PJID, carrier.CarrierId);
                    // 异步保存中间件侧失败原因，不将执行错误当作第二条 S16F16 创建应答。
                    await RecordAsync(new { plan.PJID, carrier.CarrierId, Error = ex.Message }, "StartUncertain", CancellationToken.None).ConfigureAwait(false);
                    if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                        throw;
                    return false;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Carrier 必须唯一匹配一个端口，Mapping 完整无故障，且计划中所有槽位均实际有片。
    /// </summary>
    private static ProcessJobPortSnapshot? FindReadyPort(IReadOnlyList<ProcessJobPortSnapshot> ports, ProcessJobCarrierPlan carrier)
    {
        var matches = ports.Where(port => string.Equals(port.CarrierId.Trim(), carrier.CarrierId.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1)
            return null;
        var port = matches[0];
        // VID.csv 约定 1=有片、0=空、2=故障；未知值、缺项及重复选槽均不能启动。
        if (port.MapSlotList.Count != 25 || port.MapSlotList.Any(value => value > 1)
            || carrier.Slots.Count == 0 || carrier.Slots.Distinct().Count() != carrier.Slots.Count
            || carrier.Slots.Any(slot => slot is < 1 or > 25 || port.MapSlotList[(int)slot - 1] != 1))
            return null;
        return port;
    }

    /// <summary>
    /// 将设备启动及中间件执行日志写入既有追溯存储，隔离日志故障，不改变设备调用结果。
    /// </summary>
    private async Task RecordAsync(object data, string stage, CancellationToken cancellationToken)
    {
        _logger.LogInformation("PJ execution {Stage}: {Data}", stage, JsonSerializer.Serialize(data));
        try
        {
            // 异步保存到现有数据库追溯表；不冒用任何 SxFy 或 HCACK。
            await _history.SaveInteractionAsync("PJExecution", JsonSerializer.Serialize(new { Stage = stage, Data = data }),
                null, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist PJ execution stage {Stage}.", stage);
        }
    }
}
