using Secs4Net;
using SECSbuilder;
using SECSdata;
using SECShandler.Interfaces;

namespace SECSGrpcService.Services;

/// <summary>
/// 主动外发 SxFy 分发器实现。
/// 说明：统一承载设备主动上报消息的发送、会话可用性检查与基础重试。
/// </summary>
public sealed class ActiveSxFyDispatcher : IActiveSxFyDispatcher
{
    private readonly SecsGemContext _secsGemContext;
    private readonly ILogger<ActiveSxFyDispatcher> _logger;
    private readonly IReportStorage _reportStorage;
    private readonly IEventLinkStorage _eventLinkStorage;
    private readonly IEventEnableStorage _eventEnableStorage;
    private readonly ISecsInteractionHistoryStore _interactionHistoryStore;
    private readonly IConfiguration _configuration;
    private readonly IS6F11SpoolStorage _s6f11SpoolStorage;

    public ActiveSxFyDispatcher(
        SecsGemContext secsGemContext,
        ILogger<ActiveSxFyDispatcher> logger,
        IReportStorage reportStorage,
        IEventLinkStorage eventLinkStorage,
        IEventEnableStorage eventEnableStorage,
        ISecsInteractionHistoryStore interactionHistoryStore,
        IConfiguration configuration,
        IS6F11SpoolStorage s6f11SpoolStorage)
    {
        _secsGemContext = secsGemContext;
        _logger = logger;
        _reportStorage = reportStorage;
        _eventLinkStorage = eventLinkStorage;
        _eventEnableStorage = eventEnableStorage;
        _interactionHistoryStore = interactionHistoryStore;
        _configuration = configuration;
        _s6f11SpoolStorage = s6f11SpoolStorage;
    }

    /// <summary>
    /// 发送 S6F11（Event Report Send）。
    /// 策略：会话可用时最多尝试 2 次，失败则返回 false。
    /// </summary>
    public async Task<bool> SendS6F11Async(S6F11_data data, CancellationToken cancellationToken)
    {
        // if 关键分支：参数为空时快速失败，避免后续空引用。
        if (data is null)
        {
            return false;
        }

        // if 关键分支：S6F11 上报前必须通过 Host 配置门禁（CEID 启用、CEID 已链接 RPTID、RPTID 已定义）。
        if (!IsS6F11AllowedByHostConfig(data, out var denyReason))
        {
            _logger.LogWarning("S6F11 blocked by Host config. CEID={CEID}, Reason={Reason}", data.CEID, denyReason);
            return false;
        }

        // 方法关键节点：按 Host 运行态配置（S2F33/35）动态展开 RPTID，不依赖设备端固定 RPTID。
        var hostResolvedData = BuildHostResolvedS6F11Data(data);

        // if 关键分支：会话不可用时先缓存，等待后台补发。
        if (!_secsGemContext.TryGet(out var currentGem) || currentGem is null)
        {
            _s6f11SpoolStorage.Enqueue(data);
            _logger.LogWarning("SECS session unavailable. S6F11 enqueued to spool. CEID={CEID}, SpoolCount={SpoolCount}", data.CEID, _s6f11SpoolStorage.Count);
            return false;
        }

        // 方法关键节点：统一从配置读取超时/重试参数，避免现场手改代码。
        var retrySection = _configuration.GetSection("SecsDispatch:S6F11");
        var maxAttempts = Math.Max(1, retrySection.GetValue<int>("MaxAttempts", 2));
        var retryDelayMs = Math.Max(0, retrySection.GetValue<int>("RetryDelayMs", 0));

        // for 关键分支：执行最多 N 次发送尝试（N 来自配置）。
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // if 关键分支：当前无活动会话则直接失败，不进入发送。
            if (!_secsGemContext.TryGet(out var secsGem) || secsGem is null)
            {
                _s6f11SpoolStorage.Enqueue(data);
                _logger.LogWarning("SECS session not available during retry. S6F11 enqueued. CEID={CEID}, Attempt={Attempt}, SpoolCount={SpoolCount}", data.CEID, attempt, _s6f11SpoolStorage.Count);
                return false;
            }

            try
            {
                // 方法关键节点：由 builder 统一编码 S6F11 消息体。
                var s6f11 = S6F11_builder.Build(hostResolvedData);
                var reply = await secsGem.SendAsync(s6f11, cancellationToken).ConfigureAwait(false);

                // if 关键分支：收到 S6F12 视为发送闭环成功。
                if (reply is not null && reply.S == 6 && reply.F == 12)
                {
                    _logger.LogInformation("S6F11 sent and S6F12 received. CEID={CEID}, Attempt={Attempt}", data.CEID, attempt);

                    // 方法关键节点：关键出站消息闭环成功后记录追溯。
                    await SaveOutboundS6F11HistoryAsync(hostResolvedData, hcack: 0, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                // 非严格分支：未收到 S6F12 也记录告警，允许进入下一次重试。
                _logger.LogWarning("S6F11 sent but S6F12 not returned (actual S{S}F{F}). CEID={CEID}, Attempt={Attempt}", reply?.S, reply?.F, data.CEID, attempt);
            }
            catch (SecsException ex)
            {
                _logger.LogWarning(ex, "S6F11 send timeout or SECS error. CEID={CEID}, Attempt={Attempt}", data.CEID, attempt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "S6F11 send failed. CEID={CEID}, Attempt={Attempt}", data.CEID, attempt);
            }

            // if 关键分支：仅在还有剩余次数时记录重试提示。
            if (attempt < maxAttempts)
            {
                _logger.LogWarning("S6F11 send will retry. CEID={CEID}", data.CEID);

                // if 关键分支：配置了重试等待时间时执行延迟。
                if (retryDelayMs > 0)
                {
                    await Task.Delay(retryDelayMs, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        // 方法兜底：重试耗尽仍失败。
        _logger.LogError("S6F11 retry exhausted. CEID={CEID}", data.CEID);

        // 方法兜底：重试耗尽后也缓存，等待后续补发。
        _s6f11SpoolStorage.Enqueue(data);

        // 方法兜底：重试耗尽失败也记录追溯，便于排查 Host 未回 S6F12 等问题。
        await SaveOutboundS6F11HistoryAsync(hostResolvedData, hcack: 1, cancellationToken).ConfigureAwait(false);
        return false;
    }

    /// <summary>
    /// 尝试补发断线期间缓存的 S6F11。
    /// </summary>
    public async Task<int> FlushS6F11SpoolAsync(int maxCount, CancellationToken cancellationToken)
    {
        // if 关键分支：会话不可用时直接返回，等待下次触发。
        if (!_secsGemContext.TryGet(out var secsGem) || secsGem is null)
        {
            return 0;
        }

        var batch = _s6f11SpoolStorage.PeekBatch(maxCount);
        if (batch.Count == 0)
        {
            return 0;
        }

        var successCount = 0;

        foreach (var data in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // if 关键分支：补发前依旧要走 Host 配置门禁。
            if (!IsS6F11AllowedByHostConfig(data, out var denyReason))
            {
                _logger.LogWarning("S6F11 spool item blocked by Host config. CEID={CEID}, Reason={Reason}", data.CEID, denyReason);
                successCount++;
                continue;
            }

            try
            {
                var hostResolvedData = BuildHostResolvedS6F11Data(data);
                var s6f11 = S6F11_builder.Build(hostResolvedData);
                var reply = await secsGem.SendAsync(s6f11, cancellationToken).ConfigureAwait(false);
                if (reply is not null && reply.S == 6 && reply.F == 12)
                {
                    successCount++;
                    await SaveOutboundS6F11HistoryAsync(hostResolvedData, hcack: 0, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                break;
            }
            catch
            {
                break;
            }
        }

        if (successCount > 0)
        {
            _s6f11SpoolStorage.AckBatch(successCount);
        }

        return successCount;
    }

    private async Task SaveOutboundS6F11HistoryAsync(S6F11_data data, byte hcack, CancellationToken cancellationToken)
    {
        try
        {
            var sxFy = "S6F11";
            var secsMessage = S6F11_builder.Build(data).ToString();
            await _interactionHistoryStore
                .SaveInteractionAsync(sxFy, secsMessage, hcack, DateTime.UtcNow, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist outbound S6F11 interaction history. CEID={CEID}", data.CEID);
        }
    }

    /// <summary>
    /// S6F11 发送前的 Host 配置门禁检查。
    /// 规则：CEID 必须启用；CEID 必须已链接至少一个 RPTID；消息中的 RPTID 必须在链接中且已被定义。
    /// </summary>
    private bool IsS6F11AllowedByHostConfig(S6F11_data data, out string reason)
    {
        reason = string.Empty;

        // if 关键分支：未启用事件时拒绝上报。
        if (!_eventEnableStorage.IsEventEnabled(data.CEID))
        {
            reason = "CEID not enabled";
            return false;
        }

        var linkedRptIds = _eventLinkStorage.GetRptIdsForCeid(data.CEID);

        // if 关键分支：CEID 未绑定任何 RPTID 时拒绝上报。
        if (linkedRptIds is null || linkedRptIds.Count == 0)
        {
            reason = "CEID has no linked RPTID";
            return false;
        }

        // if 关键分支：消息体没有参数数据时拒绝上报。
        if (data.Reports is null || data.Reports.Count == 0 || data.Reports.All(r => r.Values is null || r.Values.Count == 0))
        {
            reason = "S6F11 has no values";
            return false;
        }

        // for each 关键分支：逐个校验 CEID 链接的 RPTID 是否均已定义。
        foreach (var rptId in linkedRptIds)
        {
            if (!_reportStorage.ContainsReport(rptId))
            {
                reason = $"RPTID {rptId} is not defined";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 按 Host 运行态定义展开 S6F11 报告：
    /// - RPTID 取 CEID 绑定结果；
    /// - VID 按对应 RPTID 定义顺序生成；
    /// - 缺失值填空字符串。
    /// </summary>
    private S6F11_data BuildHostResolvedS6F11Data(S6F11_data source)
    {
        var linkedRptIds = _eventLinkStorage.GetRptIdsForCeid(source.CEID);
        var valueMap = new Dictionary<ushort, S6F11_parameter_data>();

        if (source.Reports is not null)
        {
            foreach (var value in source.Reports.SelectMany(r => r.Values ?? []))
            {
                valueMap[value.VID] = value;
            }
        }

        var reports = new List<S6F11_report_data>();
        foreach (var rptId in linkedRptIds.Distinct())
        {
            var definedVids = _reportStorage.GetVidsForReport(rptId);
            if (definedVids.Count == 0)
            {
                continue;
            }

            var values = new List<S6F11_parameter_data>();
            foreach (var vidU32 in definedVids)
            {
                var vid = (ushort)vidU32;
                if (valueMap.TryGetValue(vid, out var value))
                {
                    values.Add(new S6F11_parameter_data
                    {
                        VID = value.VID,
                        CPName = value.CPName,
                        CPVal = value.CPVal
                    });
                }
                else
                {
                    values.Add(new S6F11_parameter_data
                    {
                        VID = vid,
                        CPName = string.Empty,
                        CPVal = string.Empty
                    });
                }
            }

            reports.Add(new S6F11_report_data
            {
                RPTID = rptId,
                Values = values
            });
        }

        return new S6F11_data
        {
            DATAID = source.DATAID,
            CEID = source.CEID,
            timeStamp = source.timeStamp,
            Reports = reports
        };
    }
}
