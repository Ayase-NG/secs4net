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
            IMeasurementDispatcher measurementDispatcher,
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

            // if 关键分支：S2F41 最小幂等保护，短时间内相同 RCMD+参数只执行一次。
            var idempotencyKey = BuildS2F41IdempotencyKey(rcmd, data.Parameters);
            var s2f41Ttl = TimeSpan.FromSeconds(10);
            if (!idempotencyGuard.TryBegin("S2F41", idempotencyKey, s2f41Ttl))
            {
                await TryReplyS2F42Async(primary, hcack: 0, interactionHistoryStore, cancellationToken, rcmd);
                return;
            }

            // if 关键分支：只有 On-Line Remote 状态才允许远程命令执行。
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
                        await measurementDispatcher.DispatchStartMeasurementAsync(data, cancellationToken);
                        break;
                    case "STOP":
                        await measurementDispatcher.DispatchStopMeasurementAsync(data, cancellationToken);
                        break;
                    case "PAUSE":
                        await measurementDispatcher.DispatchPauseMeasurementAsync(data, cancellationToken);
                        break;
                    case "RESUME":
                        await measurementDispatcher.DispatchResumeMeasurementAsync(data, cancellationToken);
                        break;
                    case "PPSELECT":
                        await measurementDispatcher.DispatchProcessProgramSelectAsync(data, cancellationToken);
                        break;
                    default:
                        // if 关键分支：不支持的 RCMD 返回 HCACK=1 + RCMD 参数ACK。
                        await TryReplyS2F42Async(primary, hcack: 1, interactionHistoryStore, cancellationToken, rcmd, errorParam: "RCMD");
                        return;
                }

                await TryReplyS2F42Async(primary, hcack: 0, interactionHistoryStore, cancellationToken, rcmd);
            }
            catch (Exception ex)
            {
                // if 关键分支：执行异常统一返回 HCACK=2。
                var errorParam = InferErrorParamFromException(ex);
                await TryReplyS2F42Async(primary, hcack: 2, interactionHistoryStore, cancellationToken, rcmd, errorParam);
                throw;
            }
        }

        /// <summary>
        /// 处理 S16F15 请求并回复 S16F16（增强最小实现）。
        /// ACK 约定：0=接受，1=拒绝。
        /// </summary>
        public static async Task HandleS16F15Async(
            SecsGem secsGem,
            PrimaryMessageWrapper primary,
            IDevice device,
            IMeasurementDispatcher measurementDispatcher,
            IIdempotencyGuard idempotencyGuard,
            IPortContextStorage portContextStorage,
            CancellationToken cancellationToken)
        {
            var ack = (byte)1;

            try
            {
                // 方法关键节点：先解析并校验 S16F15 消息结构。
                var request = S16F15_parser.Parse(primary.PrimaryMessage);

                // if 关键分支：S16F15 最小幂等保护，按 DATAID + PJID 集合防重。
                var pjKey = BuildS16F15IdempotencyKey(request);
                if (!idempotencyGuard.TryBegin("S16F15", pjKey, TimeSpan.FromSeconds(30)))
                {
                    ack = 0;
                    var duplicatedReply = S16F16_builder.Build(ack);
                    await primary.TryReplyAsync(duplicatedReply, cancellationToken).ConfigureAwait(false);
                    return;
                }

                // if 关键分支：仅 OnLineRemote 允许；且至少包含一个有效 PJ（PJID 非空）。
                var hasValidPj = request.ProcessJobs.Any(p => !string.IsNullOrWhiteSpace(p.PJID));
                ack = device.IsOnline == DeviceOnlineState.OnLineRemote && hasValidPj ? (byte)0 : (byte)1;

                // 方法关键节点：S16F15 接受后，复用现有 PPSELECT/START gRPC 链路执行首个有效 PJ。
                if (ack == 0)
                {
                    var selected = request.ProcessJobs.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.PJID));
                    if (selected is null)
                    {
                        ack = 1;
                    }
                    else
                    {
                        var plan = BuildExecutionPlanFromProcessJob(selected, portContextStorage);
                        if (plan is null)
                        {
                            ack = 1;
                        }
                        else
                        {
                            await ExecutePlanByReusingS2F41Async(measurementDispatcher, plan.Value, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch
            {
                // if 关键分支：解析失败时保持拒绝 ACK。
                ack = 1;
            }

            var reply = S16F16_builder.Build(ack);
            try
            {
                await primary.TryReplyAsync(reply, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
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
            CancellationToken cancellationToken)
        {
            var ack = (byte)1;

            try
            {
                // 方法关键节点：先解析并校验 S14F9 消息结构。
                var request = S14F9_parser.Parse(primary.PrimaryMessage);

                // if 关键分支：S14F9 最小幂等保护，按 ObjectDomain/ObjectType + ObjID 集合防重。
                var cjKey = BuildS14F9IdempotencyKey(request);
                if (!idempotencyGuard.TryBegin("S14F9", cjKey, TimeSpan.FromSeconds(30)))
                {
                    ack = 0;
                    var duplicatedReply = S14F10_builder.Build(ack);
                    await primary.TryReplyAsync(duplicatedReply, cancellationToken).ConfigureAwait(false);
                    return;
                }

                // if 关键分支：仅 OnLineRemote 允许；且至少包含一个有效 CJ（ObjID 非空）。
                var hasValidCj = request.ControlJobs.Any(j => !string.IsNullOrWhiteSpace(j.ObjID));
                ack = device.IsOnline == DeviceOnlineState.OnLineRemote && hasValidCj ? (byte)0 : (byte)1;

                // 方法关键节点：S14F9 仅做控制流管理，记录 StartMethod 对应的可启动上下文，不直接下发动作。
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

                        // for each 关键分支：按 CarrierInputSpec 预热 Port 上下文，便于后续 S16F15 选取执行目标。
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
                            managedCount++;
                        }
                    }

                    if (managedCount == 0)
                    {
                        ack = 1;
                    }
                }
            }
            catch
            {
                // if 关键分支：解析失败时保持拒绝 ACK。
                ack = 1;
            }

            var reply = S14F10_builder.Build(ack);
            try
            {
                await primary.TryReplyAsync(reply, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
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

            // 方法关键节点：统一错误参数确认码映射，减少 Host 侧歧义。
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

                // if 关键分支：优先定位 PORTID，便于 Host 快速排查端口上下文冲突。
                if (message.Contains("PORTID", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("PORT", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("LOADPORT", StringComparison.OrdinalIgnoreCase))
                {
                    return "PORTID";
                }

                // if 关键分支：其次定位 LOTID，覆盖批次号不一致场景。
                if (message.Contains("LOTID", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("LOT", StringComparison.OrdinalIgnoreCase))
                {
                    return "LOTID";
                }

                cursor = cursor.InnerException!;
            }

            return null;
        }

        private static async Task ExecutePlanByReusingS2F41Async(
            IMeasurementDispatcher measurementDispatcher,
            (string RecipeId, string LotId, string PortId, List<uint> Slots) plan,
            CancellationToken cancellationToken)
        {
            var ppSelect = new S2F41_data
            {
                RCMD = "PPSELECT",
                Parameters = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PPID"] = Item.A(plan.RecipeId),
                    ["LOTID"] = Item.A(plan.LotId),
                    ["PORTID"] = Item.A(plan.PortId),
                    ["MODE"] = Item.A("01")
                }
            };

            if (plan.Slots.Count > 0)
            {
                ppSelect.Parameters["SLOTSLIST"] = Item.A(string.Join(',', plan.Slots));
            }

            await measurementDispatcher.DispatchProcessProgramSelectAsync(ppSelect, cancellationToken).ConfigureAwait(false);

            var start = new S2F41_data
            {
                RCMD = "START",
                Parameters = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase)
                {
                    ["LOTID"] = Item.A(plan.LotId),
                    ["PORTID"] = Item.A(plan.PortId)
                }
            };

            await measurementDispatcher.DispatchStartMeasurementAsync(start, cancellationToken).ConfigureAwait(false);
        }

        private static (string RecipeId, string LotId, string PortId, List<uint> Slots)? BuildExecutionPlanFromProcessJob(
            S16F15_process_job_data processJob,
            IPortContextStorage portContextStorage)
        {
            if (processJob.Carriers.Count == 0)
            {
                return null;
            }

            foreach (var carrier in processJob.Carriers)
            {
                if (string.IsNullOrWhiteSpace(carrier.CarrierId))
                {
                    continue;
                }

                var matched = portContextStorage.GetAll()
                    .FirstOrDefault(x => string.Equals(x.CarrierId, carrier.CarrierId.Trim(), StringComparison.OrdinalIgnoreCase));

                if (matched is null || string.IsNullOrWhiteSpace(matched.PortId) || string.IsNullOrWhiteSpace(matched.LotId))
                {
                    continue;
                }

                var recipeId = string.IsNullOrWhiteSpace(processJob.RecipeId) ? "DEFAULT" : processJob.RecipeId.Trim();
                var slots = carrier.Slots
                    .Where(x => x > 0)
                    .Distinct()
                    .ToList();

                // if 关键分支：PJ 未给 slots 时，尝试复用 ReportRFID 上下文中的 slots。
                if (slots.Count == 0)
                {
                    slots = ParseSlotsFromText(matched.SlotsList);
                }

                return (recipeId, matched.LotId.Trim(), matched.PortId.Trim(), slots);
            }

            return null;
        }

        private static List<uint> ParseSlotsFromText(string? slotsText)
        {
            var result = new List<uint>();
            if (string.IsNullOrWhiteSpace(slotsText))
            {
                return result;
            }

            foreach (var token in slotsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (uint.TryParse(token, out var slot) && slot > 0)
                {
                    result.Add(slot);
                }
            }

            return result;
        }

        private static string BuildS2F41IdempotencyKey(string rcmd, Dictionary<string, Item> parameters)
        {
            var sb = new StringBuilder(rcmd);
            foreach (var kv in parameters.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append('|').Append(kv.Key).Append('=').Append(kv.Value?.ToString());
            }

            return sb.ToString();
        }

        private static string BuildS16F15IdempotencyKey(S16F15_data request)
        {
            var pjIds = request.ProcessJobs
                .Select(p => p.PJID?.Trim() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

            return $"{request.DATAID}:{string.Join(',', pjIds)}";
        }

        private static string BuildS14F9IdempotencyKey(S14F9_data request)
        {
            var objIds = request.ControlJobs
                .Select(p => p.ObjID?.Trim() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

            return $"{request.ObjectDomain}:{request.ObjectType}:{string.Join(',', objIds)}";
        }
    }
}
