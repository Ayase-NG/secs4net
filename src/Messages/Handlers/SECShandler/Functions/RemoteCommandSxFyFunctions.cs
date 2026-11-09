using Secs4Net;
using SECSbuilder;
using SECSdata;
using SECShandler.Interfaces;
using SECSparser;
using System.IO;
using System.Text;
using System.Text.Json;

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
        /// 处理 S16F15 并回复包含 PJID、Boolean ACKA 和错误明细的 S16F16。
        /// 整批业务校验通过后才保存；相同 PJ 重传不覆盖执行状态，配方参数修改暂不限制。
        /// RecipeId 必须匹配配方根目录下的一级子文件夹名，默认根目录为 D:\GY-Recipes\Recipes。
        /// 拒绝时在回包前释放本次占用的去重记录，成功时保留短期防重。
        /// </summary>
        public static async Task HandleS16F15Async(
            SecsGem secsGem,
            PrimaryMessageWrapper primary,
            IDevice device,
            ICommandDispatcher commandDispatcher,
            IIdempotencyGuard idempotencyGuard,
            IPortContextStorage portContextStorage,
            IJobPlanStorage jobPlanStorage,
            CancellationToken cancellationToken,
            string recipeRootPath = @"D:\GY-Recipes\Recipes",
            ISecsInteractionHistoryStore? interactionHistoryStore = null)
        {
            var replyData = new S16F16_data();
            string? pjKey = null;
            var ownershipToken = Guid.Empty;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                // 先解析并校验 S16F15 消息结构。
                var request = S16F15_parser.Parse(primary.PrimaryMessage);

                // 创建阶段只要求远程在线，不要求物料已到达或设备当前空闲。
                if (device.IsOnline != DeviceOnlineState.OnLineRemote)
                    throw new InvalidOperationException("S16F15 rejected: device must be OnLineRemote.");

                var plans = ValidateS16F15Plans(request);
                ValidateS16F15Recipes(plans, recipeRootPath);
                var newPlans = new List<ProcessJobPlan>();
                // 先检查整批已有 PJ 冲突，防止后面的无效 PJ 导致前面的有效 PJ 已被写入。
                foreach (var plan in plans)
                {
                    if (jobPlanStorage.TryGetProcessJob(plan.PJID, out var existing))
                    {
                        // 相同定义视为重传，不能用新对象重置 AutoStarted 等运行信息。
                        if (!HasSameProcessJobDefinition(existing, plan))
                            throw new InvalidOperationException($"S16F15 rejected: PJID '{plan.PJID}' already exists with a different definition.");
                        replyData.PRJOBIDs.Add(plan.PJID);
                    }
                    else
                    {
                        newPlans.Add(plan);
                    }
                }

                // S16F15 最小幂等保护，按 DATAID + PJID 集合防重。
                pjKey = BuildS16F15IdempotencyKey(request);
                if (!idempotencyGuard.TryBegin("S16F15", pjKey, TimeSpan.FromSeconds(30), out ownershipToken))
                {
                    // 只根据已保存的计划确认成功，不能把另一请求尚未结束的占用当成创建完成。
                    if (newPlans.Count != 0)
                        throw new InvalidOperationException("Process Job creation is already being processed; not all requested jobs are confirmed.");
                }

                // 整批校验通过后仅保存新 PJ，不直接下发配方或 START。
                foreach (var plan in newPlans)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    jobPlanStorage.UpsertProcessJob(plan);
                    replyData.PRJOBIDs.Add(plan.PJID);
                }
                replyData.ACKA = true;
            }
            catch (OperationCanceledException)
            {
                // 取消必须传播；仅释放本次占用，不将取消伪装成正常业务应答。
                if (pjKey is not null && ownershipToken != Guid.Empty)
                    idempotencyGuard.TryRemove("S16F15", pjKey, ownershipToken);
                throw;
            }
            catch (Exception ex)
            {
                // 结构、业务校验或保存失败统一拒绝，并保留具体字段原因供诊断。
                Console.WriteLine($"S16F15 rejected: {ex.Message}");
                replyData.ACKA = false;
                replyData.Errors.Add(new S16F16_error_data { ERRTEXT = ex.Message });
            }
            // 失败释放只针对本次取得的令牌；不能删除重复请求或过期后新请求的记录。
            if (!replyData.ACKA && pjKey is not null && ownershipToken != Guid.Empty)
            {
                idempotencyGuard.TryRemove("S16F15", pjKey, ownershipToken);
            }
            // 无 W-bit 时仍完成创建处理，但不发送 SecondaryMessage。
            if (!primary.PrimaryMessage.ReplyExpected)
                return;

            replyData.timeStamp = DateTime.UtcNow;
            var reply = S16F16_builder.Build(replyData);
            // 异步通过原事务返回应答；发送异常传播，不能以新 System Bytes 重发 SecondaryMessage。
            var sent = await primary.TryReplyAsync(reply, cancellationToken).ConfigureAwait(false);
            // 只记录本次实际发送的设备侧应答，不将 ACKA 写入专用于 HCACK 的数据库字段。
            if (sent && interactionHistoryStore is not null)
            {
                try
                {
                    // 异步保存完整确认数据用于追溯，与 Host 入站 S16F15 记录按消息类型区分。
                    await interactionHistoryStore.SaveInteractionAsync("S16F16", JsonSerializer.Serialize(replyData),
                        null, replyData.timeStamp, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // 保留服务停止语义，但不撤销已经完成的创建和已发送应答。
                    throw;
                }
                catch (Exception ex)
                {
                    // 追溯故障不能造成业务重试或发送第二次应答。
                    Console.WriteLine($"Failed to persist S16F16 acknowledgement: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 校验整批 PJ 的创建参数及 1～25 槽位范围并规范化计划，不查询 Mapping，不限制预留的配方参数修改。
        /// </summary>
        private static List<ProcessJobPlan> ValidateS16F15Plans(S16F15_data request)
        {
            if (request.ProcessJobs.Count == 0)
                throw new InvalidOperationException("S16F15 requires at least one Process Job.");

            var plans = new List<ProcessJobPlan>();
            var jobIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // 检查整批定义，不能过滤掉错误 PJ 后接受剩余部分。
            foreach (var job in request.ProcessJobs)
            {
                // pjId验证
                var pjId = job.PJID.Trim();
                if (string.IsNullOrWhiteSpace(pjId) || !jobIds.Add(pjId))
                    throw new InvalidOperationException($"Invalid or duplicate PJID '{pjId}'.");

                // recipe验证
                var recipeId = job.RecipeId.Trim();
                if (string.IsNullOrWhiteSpace(recipeId))
                    throw new InvalidOperationException($"PJ '{pjId}' requires a RecipeId.");

                // MF验证。当前仅支持 Carrier + Slot（0D）；兼容 ASCII 的 0D/0x0D 表示。
                var materialFormat = job.MF.Trim();
                if (!string.Equals(materialFormat, "0D", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(materialFormat, "0x0D", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"PJ '{pjId}' has unsupported MF '{materialFormat}'. Expected 0D.");

                // 当前设备无暂停能力，不能接受暂停要求后静默忽略。
                if (job.PRPAUSEEVENT.Count != 0)
                    throw new InvalidOperationException($"PJ '{pjId}' requests unsupported PRPAUSEEVENT.");
                if (job.Carriers.Count == 0)
                    throw new InvalidOperationException($"PJ '{pjId}' requires at least one Carrier.");

                var plan = new ProcessJobPlan
                {
                    PJID = pjId,
                    RecipeId = recipeId,
                    AutoStart = job.PRPROCESSSTART,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                var carrierIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var carrier in job.Carriers)
                {
                    var carrierId = carrier.CarrierId.Trim();
                    if (string.IsNullOrWhiteSpace(carrierId) || !carrierIds.Add(carrierId))
                        throw new InvalidOperationException($"PJ '{pjId}' has an invalid or duplicate CarrierID '{carrierId}'.");
                    if (carrier.Slots.Count == 0)
                        throw new InvalidOperationException($"PJ '{pjId}', Carrier '{carrierId}' requires at least one Slot.");

                    var slots = new HashSet<uint>();
                    // 创建阶段检查固定物理范围及重复；实际占片和 Mapping 一致性由 Mapping 完成事件处理。
                    foreach (var slot in carrier.Slots)
                    {
                        if (slot is < 1 or > 25)
                            throw new InvalidOperationException($"PJ '{pjId}', Carrier '{carrierId}' has out-of-range Slot '{slot}'. Expected 1 through 25.");
                        if (!slots.Add(slot))
                            throw new InvalidOperationException($"PJ '{pjId}', Carrier '{carrierId}' has duplicate Slot '{slot}'.");
                    }

                    plan.Carriers.Add(new ProcessJobCarrierPlan
                    {
                        CarrierId = carrierId,
                        Slots = carrier.Slots.ToList()
                    });
                }

                plans.Add(plan);
            }

            return plans;
        }

        /// <summary>
        /// 一次读取配方根目录下的一级文件夹，以文件夹名校验整批 PPID，不接受文件或嵌套路径。
        /// </summary>
        private static void ValidateS16F15Recipes(IEnumerable<ProcessJobPlan> plans, string recipeRootPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(recipeRootPath);
            // 枚举失败直接交由处理层拒绝，不能在目录缺失或权限不足时跳过配方校验。
            var recipeIds = Directory.EnumerateDirectories(recipeRootPath, "*", SearchOption.TopDirectoryOnly)
                .Select(directory => new DirectoryInfo(directory).Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 只比较已枚举的文件夹名，不把 Host 提供的 PPID 拼接为文件路径。
            foreach (var plan in plans)
            {
                if (!recipeIds.Contains(plan.RecipeId))
                    throw new InvalidOperationException($"PJ '{plan.PJID}' references missing recipe '{plan.RecipeId}' under '{recipeRootPath}'.");
            }
        }

        /// <summary>
        /// 比较当前保存的 PJ 定义，不比较执行标志和时间；相同重传不得重置运行状态。
        /// </summary>
        private static bool HasSameProcessJobDefinition(ProcessJobPlan existing, ProcessJobPlan requested)
        {
            if (!string.Equals(existing.RecipeId, requested.RecipeId, StringComparison.Ordinal)
                || existing.AutoStart != requested.AutoStart
                || existing.PauseEvents.Count != 0
                || existing.Carriers.Count != requested.Carriers.Count)
                return false;

            // Carrier 与 Slot 顺序不影响重复识别，但数量和完整集合必须一致。
            foreach (var carrier in requested.Carriers)
            {
                var original = existing.Carriers.FirstOrDefault(c =>
                    string.Equals(c.CarrierId, carrier.CarrierId, StringComparison.OrdinalIgnoreCase));
                if (original is null || !original.Slots.OrderBy(s => s).SequenceEqual(carrier.Slots.OrderBy(s => s)))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 处理 S14F9 请求并回复 S14F10（增强最小实现）。
        /// ACK 约定：0=接受，1=拒绝。
        /// 拒绝时在回包前释放本次占用的去重记录，不因成功应答发送失败而释放。
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
            string? cjKey = null;
            var ownershipToken = Guid.Empty;

            try
            {
                // 先解析并校验 S14F9 消息结构。
                var request = S14F9_parser.Parse(primary.PrimaryMessage);

                // S14F9 最小幂等保护，按 ObjectDomain/ObjectType + ObjID 集合防重。
                cjKey = BuildS14F9IdempotencyKey(request);
                if (!idempotencyGuard.TryBegin("S14F9", cjKey, TimeSpan.FromSeconds(30), out ownershipToken))
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

            // 在构建和发送拒绝应答前释放本次记录，让后续请求可以重新尝试。
            if (ack != 0 && cjKey is not null && ownershipToken != Guid.Empty)
            {
                idempotencyGuard.TryRemove("S14F9", cjKey, ownershipToken);
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
