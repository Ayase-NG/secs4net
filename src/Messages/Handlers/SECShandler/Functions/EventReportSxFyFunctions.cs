using GY.PLC.Comm;
using Microsoft.Extensions.Logging;
using Secs4Net;
using SECShandler.Interfaces;
using SECSbuilder;
using SECSdata;
using SECSparser;
using System.Globalization;
using System.Threading;

namespace SECShandler.Functions
{
    /// <summary>
    /// 事件上报相关的 SxFy 处理函数集合。
    /// 包含 Host 下发的事件配置类消息处理（S2F33/S2F35/S2F37）
    /// 以及设备侧主动报警上报（S5F1）的发送重试能力。
    /// </summary>
    public static class EventReportSxFyFunctions
    {
        // 方法关键节点：缓存 SVID/VID 映射，避免每次 S1F3 都重复读盘。
        private static readonly Lazy<Dictionary<uint, SvidMapRow>> SvidMap = new(LoadSvidMapFromCandidates);

        // 方法关键节点：保证 PlcClient 的订阅初始化只执行一次。
        private static int _plcSubscriptionStarted;

        // 方法关键节点：SVID 值缓存（短 TTL），避免高频重复读取 PLC。
        private static readonly Dictionary<uint, SvidCacheEntry> SvidValueCache = new();

        // 方法关键节点：保护缓存并发访问的锁对象。
        private static readonly object SvidValueCacheLock = new();

        // 方法关键节点：缓存过期时长（最小骨架固定值，后续可配置化）。
        private static readonly TimeSpan SvidCacheTtl = TimeSpan.FromMilliseconds(300);

        /// <summary>
        /// 处理 S1F3（Selected Equipment Status Request）并回复 S1F4。
        /// 最小骨架：先从 SVID.csv 读取相关 SVID 元数据（含 SourceType），再通过统一解析入口整合数据源抽象、批量读取与缓存。
        /// </summary>
        public static async Task HandleS1F3ReplyAsync(SecsGem secsGem, IDevice device, PrimaryMessageWrapper primary, PlcClient? plcClient = null)
        {
            // 方法关键节点：先解析 S1F3 请求，拿到请求的 SVID 列表。
            var request = S1F3_parser.Parse(primary.PrimaryMessage);

            // if 关键分支：空请求（All SVID）时，使用当前映射表中的全部 SVID。
            var requestedSvids = request.IsAllSvid
                ? SvidMap.Value.Keys.OrderBy(x => x).ToList()
                : request.SVIDList;

            // 方法关键节点：统一调用解析入口，内部完成缓存命中、分组批量读取与结果合并。
            var resolved = await ResolveSvidValuesAsync(requestedSvids, device, plcClient).ConfigureAwait(false);

            var response = new S1F4_data
            {
                // for each 关键分支：按请求顺序生成返回项，保证主机侧索引对齐。
                StatusList = requestedSvids.Select(svid => new S1F4_status_data
                {
                    SVID = svid,
                    SV = resolved.TryGetValue(svid, out var value) ? value : string.Empty
                }).ToList()
            };

            // 方法关键节点：通过 builder 统一构建 S1F4 并回包。
            var reply = S1F4_builder.Build(response);

            try
            {
                await primary.TryReplyAsync(reply).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await secsGem.SendAsync(reply).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 处理 S2F33（Define Report）并回复 S2F34。
        /// DRACK 约定：0=成功，1=内部错误，2=格式错误。
        /// </summary>
        public static async Task HandleS2F33ReplyAsync(
            PrimaryMessageWrapper primary,
            IReportStorage reportStorage)
        {
            S2F33_data data;
            byte drack = 0;
            var primaryMsg = primary.PrimaryMessage;
            try
            {
                data = S2F33_parser.Parse(primaryMsg);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"S2F33 parse error: {ex.Message}");
                drack = 2;
                await primary.TryReplyAsync(S2F34_builder.Build(drack));
                return;
            }

            try
            {
                if (data.IsDeleteAll)
                {
                    reportStorage.ClearAllReports();
                }
                else
                {
                    foreach (var rptId in data.DeletedRptIds)
                    {
                        // 方法关键节点：打印删除的 RPTID，便于与 Host 下发内容对齐排查。
                        Console.WriteLine($"S2F33 delete report. RPTID={rptId}");
                        reportStorage.RemoveReport(rptId);
                    }

                    foreach (var kvp in data.DefinedReports)
                    {
                        // 方法关键节点：打印定义的 RPTID 与 VID 列表，便于确认运行态是否已生效。
                        var vidText = kvp.Value is { Count: > 0 }
                            ? string.Join(',', kvp.Value)
                            : "<none>";
                        Console.WriteLine($"S2F33 define report. RPTID={kvp.Key}, VIDs={vidText}");
                        reportStorage.AddOrUpdateReport(kvp.Key, kvp.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"S2F33 storage error: {ex.Message}");
                drack = 1;
                await primary.TryReplyAsync(S2F34_builder.Build(drack));
                return;
            }

            await primary.TryReplyAsync(S2F34_builder.Build(drack));
        }

        /// <summary>
        /// 处理 S2F35（Link Event Report）并回复 S2F36。
        /// LRACK 常见约定：0=成功，1=内部错误，2=格式错误，4=CEID无效，5=RPTID未定义。
        /// </summary>
        public static async Task HandleS2F35ReplyAsync(
            PrimaryMessageWrapper primary,
            IReportStorage reportStorage,
            IEventLinkStorage eventLinkStorage)
        {
            S2F35_data data;
            byte lrack = 0;
            uint failedRptId = 0;
            uint failedCeid = 0;
            var primaryMsg = primary.PrimaryMessage;
            try
            {
                data = S2F35_parser.Parse(primaryMsg);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"S2F35 parse error: {ex.Message}");
                lrack = 2;
                await primary.TryReplyAsync(S2F36_builder.Build(lrack));
                return;
            }

            try
            {
                // 方法关键节点：先打印当前已定义 RPTID 快照，用于排查 Host 认为已定义但运行态未命中的情况。
                var allDefinedRptIds = TryGetAllDefinedRptIds(reportStorage);
                Console.WriteLine($"S2F35 pre-check defined RPTIDs: {(allDefinedRptIds.Count > 0 ? string.Join(',', allDefinedRptIds) : "<none>")}");

                foreach (var kvp in data.Links)
                {
                    if (!eventLinkStorage.IsCeidValid(kvp.Key))
                    {
                        lrack = 4;
                        failedCeid = kvp.Key;
                        break;
                    }
                    foreach (var rptId in kvp.Value)
                    {
                        if (!reportStorage.ContainsReport(rptId))
                        {
                            lrack = 5;
                            failedCeid = kvp.Key;
                            failedRptId = rptId;
                            break;
                        }
                    }
                    if (lrack != 0) break;
                }

                if (lrack == 0)
                {
                    eventLinkStorage.UpdateEventLinks(data.Links);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"S2F35 storage error: {ex.Message}");
                lrack = 1;
                await primary.TryReplyAsync(S2F36_builder.Build(lrack));
                return;
            }

            // 方法关键节点：S2F35 失败时输出失败明细，便于 Host 侧定位具体 CEID/RPTID。
            if (lrack == 5)
            {
                Console.WriteLine($"S2F35 link rejected. LRACK=5, CEID={failedCeid}, RPTID={failedRptId} (undefined).");
            }
            else if (lrack == 4)
            {
                Console.WriteLine($"S2F35 link rejected. LRACK=4, CEID={failedCeid} (invalid).");
            }

            await primary.TryReplyAsync(S2F36_builder.Build(lrack));
        }

        /// <summary>
        /// 获取当前运行态中已定义的全部 RPTID。
        /// 作用：用于 S2F35 校验前打印快照，辅助定位 LRACK=5 的根因。
        /// </summary>
        private static List<uint> TryGetAllDefinedRptIds(IReportStorage reportStorage)
        {
            var result = new List<uint>();
            // 方法关键节点：RPTID 使用 U4，联调范围通常远小于 100000，顺序扫描开销可接受。
            for (uint rptId = 1; rptId <= 100000; rptId++)
            {
                if (reportStorage.ContainsReport(rptId))
                {
                    result.Add(rptId);
                }
            }

            return result;
        }

        /// <summary>
        /// 处理 S2F37（Enable/Disable Event Report）并回复 S2F38。
        /// EAC 常见约定：0=成功，1=CEID无效，2=格式或内部错误。
        /// </summary>
        public static async Task HandleS2F37ReplyAsync(
            PrimaryMessageWrapper primary,
            IEventLinkStorage eventLinkStorage,
            IEventEnableStorage eventEnableStorage,
            IReportStorage reportStorage)
        {
            var primaryMsg = primary.PrimaryMessage;
            S2F37_data data;
            byte eac = 0;

            try
            {
                data = S2F37_parser.Parse(primaryMsg);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"S2F37 parse error: {ex.Message}");
                eac = 2;
                await primary.TryReplyAsync(S2F38_builder.Build(eac));
                return;
            }

            try
            {
                if (data.IsAllEvents)
                {
                    eventEnableStorage.EnableAllEvents();
                }
                else
                {
                    foreach (var ceid in data.CEIDList)
                    {
                        if (!eventLinkStorage.IsCeidValid(ceid))
                        {
                            eac = 1;
                            break;
                        }
                        eventEnableStorage.EnableEvent(ceid);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"S2F37 storage error: {ex.Message}");
                eac = 2;
                await primary.TryReplyAsync(S2F38_builder.Build(eac));
                return;
            }

            // 方法关键节点：启用成功后打印当前生效事件映射（CEID -> RPTID -> VID），便于现场快速确认配置。
            if (eac == 0)
            {
                PrintEnabledEventMappings(data, eventLinkStorage, eventEnableStorage, reportStorage);
            }

            await primary.TryReplyAsync(S2F38_builder.Build(eac));
        }

        private static void PrintEnabledEventMappings(
            S2F37_data data,
            IEventLinkStorage eventLinkStorage,
            IEventEnableStorage eventEnableStorage,
            IReportStorage reportStorage)
        {
            IEnumerable<uint> ceidsToPrint = data.IsAllEvents
                ? eventEnableStorage.GetAllEnabledEvents()
                : data.CEIDList;

            Console.WriteLine("[S2F37] Enabled event mapping snapshot start");

            foreach (var ceid in ceidsToPrint.Distinct().OrderBy(x => x))
            {
                var rptIds = eventLinkStorage.GetRptIdsForCeid(ceid);
                if (rptIds is null || rptIds.Count == 0)
                {
                    Console.WriteLine($"[S2F37] CEID={ceid} (CEID) -> no linked RPTID");
                    continue;
                }

                foreach (var rptId in rptIds.Distinct().OrderBy(x => x))
                {
                    var vids = reportStorage.GetVidsForReport(rptId);
                    var vidText = vids.Count > 0
                        ? string.Join(",", vids.OrderBy(x => x).Select(v => $"{v}(VID)"))
                        : "<none>";

                    Console.WriteLine($"[S2F37] CEID={ceid}(CEID) -> RPTID={rptId}(RPTID) -> {vidText}");
                }
            }

            Console.WriteLine("[S2F37] Enabled event mapping snapshot end");
        }

        /// <summary>
        /// 发送 S5F1（Alarm Report Send），当未收到期望的 S5F2 或发生超时/异常时重试一次。
        /// </summary>
        /// <param name="secsGem">SECS/GEM 通信对象。</param>
        /// <param name="data">S5F1 业务数据。</param>
        /// <param name="logger">日志对象。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public static async Task SendS5F1WithRetryAsync(SecsGem secsGem, S5F1_data data, ILogger logger, CancellationToken cancellationToken)
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    var s5f1 = S5F1_builder.Build(data);
                    var reply = await secsGem.SendAsync(s5f1, cancellationToken).ConfigureAwait(false);

                    if (reply is not null && reply.S == 5 && reply.F == 2)
                    {
                        logger.LogInformation("S5F1 sent and S5F2 received. Attempt={Attempt}, ALID={ALID}", attempt, data.ALID);
                        return;
                    }

                    logger.LogWarning("S5F1 sent but S5F2 not returned (actual S{S}F{F}). Attempt={Attempt}, ALID={ALID}", reply?.S, reply?.F, attempt, data.ALID);
                }
                catch (SecsException ex)
                {
                    logger.LogWarning(ex, "S5F1 send timeout or SECS error. Attempt={Attempt}, ALID={ALID}", attempt, data.ALID);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "S5F1 send failed. Attempt={Attempt}, ALID={ALID}", attempt, data.ALID);
                }

                if (attempt < 2)
                {
                    logger.LogWarning("未返回 S5F2，准备重发 S5F1。ALID={ALID}", data.ALID);
                }
            }

            logger.LogError("S5F1 retry exhausted and still no S5F2. ALID={ALID}", data.ALID);
        }

        /// <summary>
        /// 统一解析 SVID 值入口。
        /// 处理顺序：先查缓存 -> 再按数据源分组批量读取 -> 回填缓存。
        /// </summary>
        private static async Task<Dictionary<uint, string>> ResolveSvidValuesAsync(
            IReadOnlyList<uint> svids,
            IDevice device,
            PlcClient? plcClient)
        {
            var result = new Dictionary<uint, string>();
            var rowsToRead = new List<(uint Svid, SvidMapRow Row)>();

            // for each 关键分支：先尝试缓存命中，未命中再进入读取队列。
            foreach (var svid in svids)
            {
                // if 关键分支：未在映射表中定义的 SVID 直接给空值。
                if (!SvidMap.Value.TryGetValue(svid, out var row))
                {
                    result[svid] = string.Empty;
                    continue;
                }

                // if 关键分支：缓存命中且未过期时直接使用。
                if (TryGetCacheValue(svid, out var cached))
                {
                    result[svid] = cached;
                    continue;
                }

                rowsToRead.Add((svid, row));
            }

            // if 关键分支：全部命中缓存时可直接返回。
            if (rowsToRead.Count == 0)
            {
                return result;
            }

            // 方法关键节点：创建 provider 上下文并准备数据源实现。
            var context = new SvidResolveContext(device, plcClient);
            var providers = new ISvidValueProvider[]
            {
                new DeviceSvidValueProvider(),
                new PlcSvidValueProvider(),
                new WorkflowSvidValueProvider()
            };

            // for each 关键分支：按 provider 分组批量读取并合并结果。
            foreach (var provider in providers)
            {
                var group = rowsToRead.Where(x => provider.CanHandle(x.Row)).ToList();

                // if 关键分支：当前 provider 没有可处理项则跳过。
                if (group.Count == 0)
                {
                    continue;
                }

                var batchResult = await provider.ReadBatchAsync(group, context).ConfigureAwait(false);

                // for each 关键分支：写入结果并回填缓存。
                foreach (var kv in batchResult)
                {
                    result[kv.Key] = kv.Value;
                    SetCacheValue(kv.Key, kv.Value);
                }
            }

            // for each 兜底分支：仍未产生值的项统一填空，保证返回完整性。
            foreach (var (svid, _) in rowsToRead)
            {
                if (!result.ContainsKey(svid))
                {
                    result[svid] = string.Empty;
                    SetCacheValue(svid, string.Empty);
                }
            }

            return result;
        }

        /// <summary>
        /// 读取 PLC HOLDING 寄存器并返回字符串值。
        /// </summary>
        private static async Task<string> ReadHoldingValueAsync(PlcClient? plcClient, ushort holdingAddress)
        {
            // if 关键分支：未注入 PlcClient 时返回空值，避免空引用。
            if (plcClient is null)
            {
                return string.Empty;
            }

            try
            {
                // if 关键分支：首次调用时启动订阅，后续调用跳过重复启动。
                if (Interlocked.Exchange(ref _plcSubscriptionStarted, 1) == 0)
                {
                    plcClient.StartSubZeromqChanged();
                }

                // 方法关键节点：调用 GY.PLC.Comm 提供的 ReadHolding 读取寄存器。
                var (success, _, value) = await plcClient.ReadHolding(holdingAddress).ConfigureAwait(false);

                // if 关键分支：读取失败或无值时返回空字符串。
                if (!success || value is null)
                {
                    return string.Empty;
                }

                return value.Value.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                // 异常兜底分支：PLC 通信异常时返回空字符串，避免中断 S1F4 回复。
                return string.Empty;
            }
        }

        /// <summary>
        /// 缓存快照结构：保存值与过期时间。
        /// </summary>
        private sealed class SvidCacheEntry
        {
            /// <summary>
            /// 当前缓存值。
            /// </summary>
            public string Value { get; init; } = string.Empty;

            /// <summary>
            /// 过期时间（UTC）。
            /// </summary>
            public DateTime ExpiresAtUtc { get; init; }
        }

        /// <summary>
        /// SVID 解析上下文：向 provider 传递运行时依赖。
        /// </summary>
        private sealed class SvidResolveContext
        {
            public SvidResolveContext(IDevice device, PlcClient? plcClient)
            {
                Device = device;
                PlcClient = plcClient;
            }

            public IDevice Device { get; }

            public PlcClient? PlcClient { get; }
        }

        /// <summary>
        /// SVID 值提供者抽象。
        /// </summary>
        private interface ISvidValueProvider
        {
            /// <summary>
            /// 判断当前 provider 是否可处理指定 SVID 行。
            /// </summary>
            bool CanHandle(SvidMapRow row);

            /// <summary>
            /// 批量读取一组 SVID 值并返回结果字典。
            /// </summary>
            Task<Dictionary<uint, string>> ReadBatchAsync(
                IReadOnlyList<(uint Svid, SvidMapRow Row)> rows,
                SvidResolveContext context);
        }

        /// <summary>
        /// 设备内存态数据源 provider。
        /// </summary>
        private sealed class DeviceSvidValueProvider : ISvidValueProvider
        {
            public bool CanHandle(SvidMapRow row)
            {
                // if 关键分支：优先使用 SourceType=Property 路由到设备属性源。
                if (string.Equals(row.SourceType, "Property", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // 兼容分支：旧 CSV 未配置 SourceType 时，按已知设备属性项走属性源。
                return string.Equals(row.Name, "RECIPEID", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(row.Name, "ONLINE_STATE", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(row.Name, "ISONLINE", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(row.Name, "MODE", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(row.Name, "RUN_MODE", StringComparison.OrdinalIgnoreCase);
            }

            public Task<Dictionary<uint, string>> ReadBatchAsync(
                IReadOnlyList<(uint Svid, SvidMapRow Row)> rows,
                SvidResolveContext context)
            {
                var result = new Dictionary<uint, string>();

                // for each 关键分支：逐项返回设备内存态值。
                foreach (var (svid, row) in rows)
                {
                    // if 关键分支：RECIPEID 返回 device.RECIPEID。
                    if (string.Equals(row.Name, "RECIPEID", StringComparison.OrdinalIgnoreCase))
                    {
                        result[svid] = context.Device.RECIPEID ?? string.Empty;
                    }
                    else if (string.Equals(row.Name, "ONLINE_STATE", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(row.Name, "ISONLINE", StringComparison.OrdinalIgnoreCase))
                    {
                        result[svid] = context.Device.IsOnline.ToString();
                    }
                    else if (string.Equals(row.Name, "MODE", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(row.Name, "RUN_MODE", StringComparison.OrdinalIgnoreCase))
                    {
                        result[svid] = context.Device.Mode ?? string.Empty;
                    }
                    else
                    {
                        result[svid] = string.Empty;
                    }
                }

                return Task.FromResult(result);
            }
        }

        /// <summary>
        /// PLC HOLDING 数据源 provider。
        /// 说明：当前“批量”实现为逻辑批量（分组后逐项读），后续可替换为底层真批读。
        /// </summary>
        private sealed class PlcSvidValueProvider : ISvidValueProvider
        {
            public bool CanHandle(SvidMapRow row)
            {
                // if 关键分支：SourceType=PLC 时明确路由到 PLC。
                if (string.Equals(row.SourceType, "PLC", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // if 关键分支：SourceType 已明确且非 PLC 时，不抢占其他 provider。
                if (!string.IsNullOrWhiteSpace(row.SourceType))
                {
                    return false;
                }

                // 兼容分支：旧 CSV 未配置 SourceType 时，沿用原有默认行为。
                return !string.Equals(row.Name, "RECIPEID", StringComparison.OrdinalIgnoreCase);
            }

            public async Task<Dictionary<uint, string>> ReadBatchAsync(
                IReadOnlyList<(uint Svid, SvidMapRow Row)> rows,
                SvidResolveContext context)
            {
                var result = new Dictionary<uint, string>();

                // for each 关键分支：逐项读取 HOLDING 寄存器值。
                foreach (var (svid, row) in rows)
                {
                    var value = await ReadHoldingValueAsync(context.PlcClient, row.HoldingAddress).ConfigureAwait(false);
                    result[svid] = value;
                }

                return result;
            }
        }

        /// <summary>
        /// Workflow 数据源 provider 骨架。
        /// 说明：当前最小实现返回空值，后续可接工作流上下文或状态机。
        /// </summary>
        private sealed class WorkflowSvidValueProvider : ISvidValueProvider
        {
            public bool CanHandle(SvidMapRow row)
            {
                // if 关键分支：仅处理 SourceType=Workflow 的项。
                return string.Equals(row.SourceType, "Workflow", StringComparison.OrdinalIgnoreCase);
            }

            public Task<Dictionary<uint, string>> ReadBatchAsync(
                IReadOnlyList<(uint Svid, SvidMapRow Row)> rows,
                SvidResolveContext context)
            {
                var result = new Dictionary<uint, string>();

                // for each 关键分支：当前骨架阶段统一返回空字符串占位。
                foreach (var (svid, _) in rows)
                {
                    result[svid] = string.Empty;
                }

                return Task.FromResult(result);
            }
        }

        /// <summary>
        /// 从缓存读取 SVID 值。
        /// </summary>
        private static bool TryGetCacheValue(uint svid, out string value)
        {
            lock (SvidValueCacheLock)
            {
                // if 关键分支：缓存不存在则直接 miss。
                if (!SvidValueCache.TryGetValue(svid, out var entry))
                {
                    value = string.Empty;
                    return false;
                }

                // if 关键分支：缓存已过期则删除并返回 miss。
                if (entry.ExpiresAtUtc <= DateTime.UtcNow)
                {
                    SvidValueCache.Remove(svid);
                    value = string.Empty;
                    return false;
                }

                value = entry.Value;
                return true;
            }
        }

        /// <summary>
        /// 写入 SVID 值缓存。
        /// </summary>
        private static void SetCacheValue(uint svid, string value)
        {
            lock (SvidValueCacheLock)
            {
                SvidValueCache[svid] = new SvidCacheEntry
                {
                    Value = value,
                    ExpiresAtUtc = DateTime.UtcNow.Add(SvidCacheTtl)
                };
            }
        }

        /// <summary>
        /// SVID.csv 行模型：用于 S1F3 中按 SVID 反查 NAME/HOLDING/SourceType。
        /// </summary>
        private sealed class SvidMapRow
        {
            /// <summary>
            /// 变量名称（NAME）。
            /// </summary>
            public string Name { get; init; } = string.Empty;

            /// <summary>
            /// PLC Holding 地址（HOLDING）。
            /// </summary>
            public ushort HoldingAddress { get; init; }

            /// <summary>
            /// 数据来源类型（SourceType），例如 Property/PLC/Workflow。
            /// </summary>
            public string SourceType { get; init; } = string.Empty;
        }

        /// <summary>
        /// 规范化 SourceType。
        /// </summary>
        private static string NormalizeSourceType(string sourceTypeText, string name)
        {
            // if 关键分支：显式指定 Property 时直接返回规范值。
            if (string.Equals(sourceTypeText, "Property", StringComparison.OrdinalIgnoreCase))
            {
                return "Property";
            }

            // if 关键分支：显式指定 PLC 时直接返回规范值。
            if (string.Equals(sourceTypeText, "PLC", StringComparison.OrdinalIgnoreCase))
            {
                return "PLC";
            }

            // if 关键分支：显式指定 Workflow 时直接返回规范值。
            if (string.Equals(sourceTypeText, "Workflow", StringComparison.OrdinalIgnoreCase))
            {
                return "Workflow";
            }

            // 兼容分支：旧 CSV 未配置 SourceType 时，根据 NAME 回退推断。
            if (string.Equals(name, "RECIPEID", StringComparison.OrdinalIgnoreCase))
            {
                return "Property";
            }

            // 兼容分支：在线状态与运行模式按设备属性读取。
            if (string.Equals(name, "ONLINE_STATE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "ISONLINE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "MODE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "RUN_MODE", StringComparison.OrdinalIgnoreCase))
            {
                return "Property";
            }

            // 默认分支：其余项默认归类为 PLC。
            return "PLC";
        }

        /// <summary>
        /// 读取并解析 SVID.csv。
        /// 新推荐列头：SVID,NAME,HOLDING,SourceType,DESCRIPTION。
        /// 兼容旧格式：SVID,NAME,HOLDING,DESCRIPTION（未配置 SourceType 时自动回退）。
        /// </summary>
        private static Dictionary<uint, SvidMapRow> LoadSvidMap(string csvPath)
        {
            var map = new Dictionary<uint, SvidMapRow>();

            // if 关键分支：文件不存在时返回空映射，调用端自动走兜底逻辑。
            if (!File.Exists(csvPath))
            {
                return map;
            }

            // for each 关键分支：逐行解析映射记录。
            foreach (var raw in File.ReadAllLines(csvPath))
            {
                // if 关键分支：过滤空行。
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var line = raw.Trim();

                // if 关键分支：跳过表头（兼容 SVID.csv 与 VID.csv）。
                if (line.StartsWith("SVID,", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("VID,", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = line.Split(',', 5);

                // if 关键分支：列数不足时跳过异常行。
                if (parts.Length < 3)
                {
                    continue;
                }

                // if 关键分支：SVID 无法解析时跳过。
                if (!uint.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var svid))
                {
                    continue;
                }

                var name = parts[1].Trim();

                // 兼容分支：SVID.csv 第3列是 HOLDING；VID.csv 第3列是 Category（非数字）。
                var col3 = parts[2].Trim();
                var hasHolding = ushort.TryParse(col3, NumberStyles.Integer, CultureInfo.InvariantCulture, out var holding);

                // if 关键分支：SVID.csv 优先读取第4列 SourceType；VID.csv 走名称推断。
                var sourceTypeText = hasHolding && parts.Length >= 4 ? parts[3].Trim() : string.Empty;
                var sourceType = NormalizeSourceType(sourceTypeText, name);

                map[svid] = new SvidMapRow
                {
                    Name = name,
                    HoldingAddress = hasHolding ? holding : (ushort)0,
                    SourceType = sourceType
                };
            }

            return map;
        }

        /// <summary>
        /// 按候选路径加载 SVID/VID 映射，优先 SVID.csv，缺失时回退到 VID.csv。
        /// </summary>
        private static Dictionary<uint, SvidMapRow> LoadSvidMapFromCandidates()
        {
            var candidates = new[]
            {
                "SVID.csv",
                "VID.csv",
                Path.Combine("src", "Messages", "Config", "SVID.csv"),
                Path.Combine("src", "Messages", "Config", "VID.csv"),
                Path.Combine("..", "Messages", "Config", "SVID.csv"),
                Path.Combine("..", "Messages", "Config", "VID.csv")
            };

            foreach (var path in candidates)
            {
                var map = LoadSvidMap(path);
                if (map.Count > 0)
                {
                    return map;
                }
            }

            return new Dictionary<uint, SvidMapRow>();
        }
    }
}
