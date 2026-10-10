using Secs4Net;
using SECSbuilder;
using SECSdata;
using SECShandler.Interfaces;
using SECSparser;
using System.Text;

namespace SECShandler.Functions
{
    /// <summary>
    /// 远程命令相关的 SxFy 处理函数集合。
    /// 当前包含 S2F41（Remote Command）解析与 S2F42 应答逻辑，
    /// 以及最小实现的 S16F15/S16F16、S14F9/S14F10 请求应答处理。
    /// </summary>
    public static class RemoteCommandSxFyFunctions
    {
        /// <summary>
        /// 处理 S2F41 Remote Command。
        /// 支持 RCMD：START、STOP、PAUSE、RESUME、PPSELECT。
        /// </summary>
        public static async Task HandleS2F41Async(
            PrimaryMessageWrapper primary,
            IDevice device,
            ICommandDispatcher commandDispatcher,
            ISecsInteractionHistoryStore interactionHistoryStore,
            IIdempotencyGuard idempotencyGuard,
            CancellationToken cancellationToken)
        {
            S2F41_data data;
            var primaryMsg = primary.PrimaryMessage;
            try
            {
                data = S2F41_parser.Parse(primaryMsg);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"S2F41 parse error: {ex.Message}");
                await TryReplyS2F42Async(primary, hcack: 2, interactionHistoryStore, cancellationToken, rcmd: null, errorParam: "Parse");
                return;
            }

            var rcmd = (data.RCMD ?? string.Empty).Trim().ToUpperInvariant();

            // S2F41 最小幂等保护，短时间内相同 RCMD+参数只执行一次。
            var idempotencyKey = BuildIdempotencyKey(rcmd, data.Parameters);
            var s2f41Ttl = TimeSpan.FromSeconds(10);
            if (!idempotencyGuard.TryBegin("S2F41", idempotencyKey, s2f41Ttl))
            {
                await TryReplyS2F42Async(primary, hcack: 0, interactionHistoryStore, cancellationToken, rcmd);
                return;
            }

            // 只有 On-Line Remote 状态才允许远程命令执行。
            if (device.IsOnline is not DeviceOnlineState.OnLineRemote)
            {
                await TryReplyS2F42Async(primary, hcack: 2, interactionHistoryStore, cancellationToken, rcmd, errorParam: "NotOnlineState");
                return;
            }

            try
            {
                switch (rcmd)
                {
                    case "START":
                        await commandDispatcher.DispatchStartMeasurementAsync(data, cancellationToken);
                        break;
                    case "STOP":
                        await commandDispatcher.DispatchStopMeasurementAsync(data, cancellationToken);
                        break;
                    case "PAUSE":
                        await commandDispatcher.DispatchPauseMeasurementAsync(data, cancellationToken);
                        break;
                    case "RESUME":
                        await commandDispatcher.DispatchResumeMeasurementAsync(data, cancellationToken);
                        break;
                    case "PPSELECT":
                        await commandDispatcher.DispatchProcessProgramSelectAsync(data, cancellationToken);
                        break;
                    default:
                        // 不支持的 RCMD 返回 HCACK=1 + RCMD 参数ACK。
                        await TryReplyS2F42Async(primary, hcack: 1, interactionHistoryStore, cancellationToken, rcmd, errorParam: "RCMD");
                        return;
                }

                await TryReplyS2F42Async(primary, hcack: 0, interactionHistoryStore, cancellationToken, rcmd);
            }
            catch (Exception ex)
            {
                // 执行异常统一返回 HCACK=2。
                var errorParam = InferErrorParamFromException(ex);
                await TryReplyS2F42Async(primary, hcack: 2, interactionHistoryStore, cancellationToken, rcmd, errorParam);
                throw;
            }
        }

        /// <summary>
        /// 先搭建S2F49链路，目前实际效果与S2F41一致，后续如有需要，补充参数。
        /// </summary>
        /// <param name="primary"></param>
        /// <param name="device"></param>
        /// <param name="commandDispatcher"></param>
        /// <param name="interactionHistoryStore"></param>
        /// <param name="idempotencyGuard"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static async Task HandleS2F49Async(
            PrimaryMessageWrapper primary,
            IDevice device,
            ICommandDispatcher commandDispatcher,
            ISecsInteractionHistoryStore interactionHistoryStore,
            IIdempotencyGuard idempotencyGuard,
            CancellationToken cancellationToken)
        {
            S2F49_data data;
            var primaryMsg = primary.PrimaryMessage;
            try
            {
                data = S2F49_parser.Parse(primaryMsg);
                switch (data.RCMD)
                {
                    case "START":
                        await commandDispatcher.DispatchStartMeasurementAsync(data, cancellationToken);
                        break;
                    case "STOP":
                        await commandDispatcher.DispatchStopMeasurementAsync(data, cancellationToken);
                        break;
                    case "PAUSE":
                        await commandDispatcher.DispatchPauseMeasurementAsync(data, cancellationToken);
                        break;
                    case "RESUME":
                        await commandDispatcher.DispatchResumeMeasurementAsync(data, cancellationToken);
                        break;
                    case "PPSELECT":
                        await commandDispatcher.DispatchProcessProgramSelectAsync(data, cancellationToken);
                        break;
                    default:
                        // 不支持的 RCMD 返回 HCACK=1 + RCMD 参数ACK。
                        await TryReplyS2F50Async(primary, hcack: 1, interactionHistoryStore, cancellationToken, data.RCMD, errorParam: "RCMD");
                        return;
                }

                await TryReplyS2F50Async(primary, hcack: 0, interactionHistoryStore, cancellationToken, data.RCMD);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"S2F49 parse error: {ex.Message}");
                await TryReplyS2F50Async(primary, hcack: 2, interactionHistoryStore, cancellationToken, rcmd: null, errorParam: "Parse");
                return;
            }

            var rcmd = (data.RCMD ?? string.Empty).Trim().ToUpperInvariant();
            // S2F41 最小幂等保护，短时间内相同 RCMD+参数只执行一次。
            var idempotencyKey = BuildIdempotencyKey(rcmd, data.Parameters);
        }
        /// <summary>
        /// 处理 S16F15 请求并回复 S16F16（增强最小实现）。
        /// ACK 约定：0=接受，1=拒绝。
        /// </summary>
        public static async Task HandleS16F15Async(
            SecsGem secsGem,
            PrimaryMessageWrapper primary,
            IDevice device,
            ICommandDispatcher commandDispatcher,
            IIdempotencyGuard idempotencyGuard,
            IPortContextStorage portContextStorage,
            IJobPlanStorage jobPlanStorage,
            CancellationToken cancellationToken)
        {
            var ack = (byte)1;

            try
            {
                // 先解析并校验 S16F15 消息结构。
                var request = S16F15_parser.Parse(primary.PrimaryMessage);

                // S16F15 最小幂等保护，按 DATAID + PJID 集合防重。
                var pjKey = BuildS16F15IdempotencyKey(request);
                if (!idempotencyGuard.TryBegin("S16F15", pjKey, TimeSpan.FromSeconds(30)))
                {
                    ack = 0;
                    var duplicatedReply = S16F16_builder.Build(ack);
                    // 命中幂等后立即异步回复 S16F16，告知 Host 本次请求已被接收过且无需重复处理。
                    await primary.TryReplyAsync(duplicatedReply, cancellationToken).ConfigureAwait(false);
                    return;
                }

                // 仅 OnLineRemote 允许；且至少包含一个有效 PJ（PJID 非空）。
                var hasValidPj = request.ProcessJobs.Any(p => !string.IsNullOrWhiteSpace(p.PJID));
                ack = device.IsOnline == DeviceOnlineState.OnLineRemote && hasValidPj ? (byte)0 : (byte)1;

                // S16F15 仅做计划缓存，不直接执行；等待 S14F9 关联与 Carrier 到达触发。
                if (ack == 0)
                {
                    var validJobs = request.ProcessJobs.Where(p => !string.IsNullOrWhiteSpace(p.PJID)).ToList();
                    if (validJobs.Count == 0)
                    {
                        ack = 1;
                    }
                    else
                    {
                        foreach (var processJob in validJobs)
                        {
                            // 将 PJ 关键字段落入运行态缓存，供后续 S14F9 关联和 Carrier 到达调度使用。
                            var plan = new ProcessJobPlan
                            {
                                PJID = processJob.PJID.Trim(),
                                RecipeId = processJob.RecipeId?.Trim() ?? string.Empty,
                                AutoStart = processJob.PRPROCESSSTART,
                                PauseEvents = processJob.PRPAUSEEVENT.Distinct().ToList(),
                                Carriers = processJob.Carriers
                                    .Where(c => !string.IsNullOrWhiteSpace(c.CarrierId))
                                    .Select(c => new ProcessJobCarrierPlan
                                    {
                                        CarrierId = c.CarrierId.Trim(),
                                        Slots = c.Slots.Where(x => x > 0).Distinct().ToList()
                                    })
                                    .ToList(),
                                UpdatedAtUtc = DateTime.UtcNow
                            };

                            jobPlanStorage.UpsertProcessJob(plan);
                        }
                    }
                }
            }
            catch
            {
                // 解析失败时保持拒绝 ACK。
                ack = 1;
            }

            var reply = S16F16_builder.Build(ack);
            try
            {
                // 优先使用 Primary 通道异步回发 S16F16，确保与当前事务上下文一一对应。
                await primary.TryReplyAsync(reply, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 兜底分支：Primary 回包失败时通过 SecsGem 异步发送，尽量保证 Host 能收到 ACK。
                await secsGem.SendAsync(reply, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 处理 S14F9 请求并回复 S14F10（增强最小实现）。
        /// ACK 约定：0=接受，1=拒绝。
        /// </summary>
        public static async Task HandleS14F9Async(
            SecsGem secsGem,
            PrimaryMessageWrapper primary,
            IDevice device,
            IIdempotencyGuard idempotencyGuard,
            IPortContextStorage portContextStorage,
            IJobPlanStorage jobPlanStorage,
            CancellationToken cancellationToken)
        {
            var ack = (byte)1;

            try
            {
                // 先解析并校验 S14F9 消息结构。
                var request = S14F9_parser.Parse(primary.PrimaryMessage);

                // S14F9 最小幂等保护，按 ObjectDomain/ObjectType + ObjID 集合防重。
                var cjKey = BuildS14F9IdempotencyKey(request);
                if (!idempotencyGuard.TryBegin("S14F9", cjKey, TimeSpan.FromSeconds(30)))
                {
                    ack = 0;
                    var duplicatedReply = S14F10_builder.Build(ack);
                    // 命中幂等后立即异步回复 S14F10，避免 Host 重发导致控制流重复入库。
                    await primary.TryReplyAsync(duplicatedReply, cancellationToken).ConfigureAwait(false);
                    return;
                }

                // 仅 OnLineRemote 允许；且至少包含一个有效 CJ（ObjID 非空）。
                var hasValidCj = request.ControlJobs.Any(j => !string.IsNullOrWhiteSpace(j.ObjID));
                ack = device.IsOnline == DeviceOnlineState.OnLineRemote && hasValidCj ? (byte)0 : (byte)1;

                // S14F9 进行 CJ-PJ 关联校验并缓存，不直接下发动作。
                if (ack == 0)
                {
                    var managedCount = 0;
                    foreach (var cj in request.ControlJobs)
                    {
                        if (string.IsNullOrWhiteSpace(cj.ObjID))
                        {
                            continue;
                        }

                        if (cj.CarrierInputSpec.Count == 0)
                        {
                            continue;
                        }

                        // ProcessingCtrlSpec 未携带 PJID 时不接受该 CJ。
                        if (cj.ProcessingCtrlSpec.Count == 0)
                        {
                            continue;
                        }

                        var relatedPjPlans = new List<ProcessJobPlan>();
                        foreach (var pjId in cj.ProcessingCtrlSpec.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
                        {
                            if (!jobPlanStorage.TryGetProcessJob(pjId, out var pjPlan))
                            {
                                relatedPjPlans.Clear();
                                break;
                            }

                            relatedPjPlans.Add(pjPlan);
                        }

                        // 任一 PJ 不存在则该 CJ 校验失败。
                        if (relatedPjPlans.Count == 0)
                        {
                            continue;
                        }

                        // 校验 CarrierInputSpec 与关联 PJ 的 Carrier 集是否有交集。
                        var carrierSetFromPj = relatedPjPlans
                            .SelectMany(p => p.Carriers)
                            .Select(c => c.CarrierId)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x.Trim())
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        var carrierSetFromCj = cj.CarrierInputSpec
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x.Trim())
                            .ToList();

                        if (carrierSetFromCj.Count == 0 || carrierSetFromCj.All(x => !carrierSetFromPj.Contains(x)))
                        {
                            continue;
                        }

                        // 按 CarrierInputSpec 预热 Port 上下文，便于后续 S16F15 选取执行目标。
                        foreach (var carrierId in cj.CarrierInputSpec)
                        {
                            if (string.IsNullOrWhiteSpace(carrierId))
                            {
                                continue;
                            }

                            var matched = portContextStorage.GetAll()
                                .FirstOrDefault(x => string.Equals(x.CarrierId, carrierId.Trim(), StringComparison.OrdinalIgnoreCase));

                            if (matched is null)
                            {
                                continue;
                            }

                            matched.UpdatedAtUtc = DateTime.UtcNow;
                            portContextStorage.Upsert(matched);
                        }

                        // CJ 校验通过后落库，等待 Carrier 到达时由 ReportRFID 触发调度。
                        jobPlanStorage.UpsertControlJob(new ControlJobPlan
                        {
                            CJID = cj.ObjID.Trim(),
                            ProcessingCtrlSpec = cj.ProcessingCtrlSpec
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Select(x => x.Trim())
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList(),
                            CarrierInputSpec = carrierSetFromCj,
                            StartMethod = cj.StartMethod,
                            UpdatedAtUtc = DateTime.UtcNow
                        });

                        managedCount++;
                    }

                    if (managedCount == 0)
                    {
                        ack = 1;
                    }
                }
            }
            catch
            {
                // 解析失败时保持拒绝 ACK。
                ack = 1;
            }

            var reply = S14F10_builder.Build(ack);
            try
            {
                // 优先走当前 Primary 会话异步回复 S14F10，保持请求-应答关联。
                await primary.TryReplyAsync(reply, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 兜底分支：Primary 回包异常时通过 SecsGem 异步发送 ACK，降低丢应答风险。
                await secsGem.SendAsync(reply, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 回复 S2F42（Remote Command Acknowledge）并记录数据库追溯日志。
        /// HCACK 约定：0=成功，1=命令不支持/参数问题，2=执行失败。
        /// </summary>
        private static async Task TryReplyS2F42Async(
            PrimaryMessageWrapper primaryMessage,
            byte hcack,
            ISecsInteractionHistoryStore interactionHistoryStore,
            CancellationToken cancellationToken,
            string? rcmd = null,
            string? errorParam = null)
        {
            if (!primaryMessage.PrimaryMessage.ReplyExpected)
                return;

            // 统一错误参数确认码映射，减少 Host 侧歧义。
            var parameterAcks = new Dictionary<string, byte>();
            if (!string.IsNullOrWhiteSpace(errorParam))
            {
                parameterAcks[errorParam] = MapCpAck(errorParam);
            }

            var s2f42 = S2F42_builder.Build(hcack, parameterAcks.Count == 0 ? null : parameterAcks);

            await primaryMessage.TryReplyAsync(s2f42, cancellationToken);

            var sxFy = $"S{s2f42.S}F{s2f42.F}";
            var secsMessage = s2f42.ToString();
            await interactionHistoryStore.SaveInteractionAsync(sxFy, secsMessage, hcack, DateTime.UtcNow, cancellationToken);
        }

        /// <summary>
        /// 回复 S2F50（Remote Command Acknowledge）并记录数据库追溯日志。
        /// HCACK 约定：0=成功，1=命令不支持/参数问题，2=执行失败。
        /// </summary>
        private static async Task TryReplyS2F50Async(
            PrimaryMessageWrapper primaryMessage,
            byte hcack,
            ISecsInteractionHistoryStore interactionHistoryStore,
            CancellationToken cancellationToken,
            string? rcmd = null,
            string? errorParam = null)
        {
            if (!primaryMessage.PrimaryMessage.ReplyExpected)
                return;

            // 统一错误参数确认码映射，减少 Host 侧歧义。
            var parameterAcks = new Dictionary<string, byte>();
            if (!string.IsNullOrWhiteSpace(errorParam))
            {
                parameterAcks[errorParam] = MapCpAck(errorParam);
            }

            var s2f50 = S2F50_builder.Build(hcack, parameterAcks.Count == 0 ? null : parameterAcks);

            await primaryMessage.TryReplyAsync(s2f50, cancellationToken);

            var sxFy = $"S{s2f50.S}F{s2f50.F}";
            var secsMessage = s2f50.ToString();
            await interactionHistoryStore.SaveInteractionAsync(sxFy, secsMessage, hcack, DateTime.UtcNow, cancellationToken);
        }

        /// <summary>
        /// 参数级确认码映射。
        /// </summary>
        private static byte MapCpAck(string errorParam)
        {
            var key = (errorParam ?? string.Empty).Trim();
            return key.ToUpperInvariant() switch
            {
                "RCMD" => 1,
                "PARSE" => 2,
                "NOTONLINESTATE" => 3,
                "PORTID" => 3,
                "LOTID" => 3,
                _ => 1
            };
        }

        private static string? InferErrorParamFromException(Exception ex)
        {
            var cursor = ex;
            while (cursor is not null)
            {
                var message = cursor.Message ?? string.Empty;

                // 优先定位 PORTID，便于 Host 快速排查端口上下文冲突。
                if (message.Contains("PORTID", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("PORT", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("LOADPORT", StringComparison.OrdinalIgnoreCase))
                {
                    return "PORTID";
                }

                // 其次定位 LOTID，覆盖批次号不一致场景。
                if (message.Contains("LOTID", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("LOT", StringComparison.OrdinalIgnoreCase))
                {
                    return "LOTID";
                }

                cursor = cursor.InnerException!;
            }

            return null;
        }

        /// <summary>
        /// 构建 S2F41 幂等键。
        /// 作用：将 RCMD 与参数按稳定顺序拼接，避免短时间重复命令被重复执行。
        /// </summary>
        private static string BuildIdempotencyKey(string rcmd, Dictionary<string, Item> parameters)
        {
            var sb = new StringBuilder(rcmd);
            foreach (var kv in parameters.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append('|').Append(kv.Key).Append('=').Append(kv.Value?.ToString());
            }

            return sb.ToString();
        }

        /// <summary>
        /// 构建 S16F15 幂等键。
        /// 作用：按 DATAID 与 PJID 集合标识同一批次任务，支持重复请求去重。
        /// </summary>
        private static string BuildS16F15IdempotencyKey(S16F15_data request)
        {
            var pjIds = request.ProcessJobs
                .Select(p => p.PJID?.Trim() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

            return $"{request.DATAID}:{string.Join(',', pjIds)}";
        }

        /// <summary>
        /// 构建 S14F9 幂等键。
        /// 作用：按对象规范/类型与 ObjID 集合标识同一组 ControlJob，避免重复入库。
        /// </summary>
        private static string BuildS14F9IdempotencyKey(S14F9_data request)
        {
            var objIds = request.ControlJobs
                .Select(p => p.ObjID?.Trim() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

            return $"{request.OBJSPEC}:{request.OBJTYPE}:{string.Join(',', objIds)}";
        }
    }
}
