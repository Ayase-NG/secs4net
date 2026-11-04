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
        // 缓存 SVID 映射，避免每次 S1F3 都重复读盘。
        private static readonly Lazy<Dictionary<uint, SvidMapRow>> SvidMap = new(() => LoadVidMapFromCandidates("SVID"));

        // 缓存 ECID 映射，避免每次 S2F13 都重复读盘。
        private static readonly Lazy<Dictionary<uint, SvidMapRow>> EcidMap = new(() => LoadVidMapFromCandidates("ECID"));

        // 缓存 DVID 映射，避免每次 S6F11 都重复读盘。
        private static readonly Lazy<Dictionary<uint, SvidMapRow>> DvidMap = new(() => LoadVidMapFromCandidates("DVID"));

        // 保证 PlcClient 的订阅初始化只执行一次。
        private static int _plcSubscriptionStarted;

        // SVID 值缓存（短 TTL），避免高频重复读取 PLC。
        private static readonly Dictionary<uint, SvidCacheEntry> SvidValueCache = new();

        // 保护缓存并发访问的锁对象。
        private static readonly object SvidValueCacheLock = new();

        // 缓存过期时长（最小骨架固定值，后续可配置化）。
        private static readonly TimeSpan SvidCacheTtl = TimeSpan.FromMilliseconds(300);

        /// <summary>
        /// 处理 S1F3（Selected Equipment Status Request）并回复 S1F4。
        /// 最小骨架：先从 SVID.csv 读取相关 SVID 元数据（含 SourceType），再通过统一解析入口整合数据源抽象、批量读取与缓存。
        /// </summary>
        public static async Task HandleS1F3ReplyAsync(SecsGem secsGem, IDevice device, PrimaryMessageWrapper primary, PlcClient? plcClient = null, IVidValueStorage? vidValueStorage = null)
        {
            // 先解析 S1F3 请求，拿到请求的 SVID 列表。
            var request = S1F3_parser.Parse(primary.PrimaryMessage);

            // 空请求（All SVID）时，使用当前映射表中的全部 SVID。
            var requestedSvids = request.IsAllSvid
                ? SvidMap.Value.Keys.OrderBy(x => x).ToList()
                : request.SVIDList;

            // 统一调用解析入口，内部完成缓存命中、分组批量读取与结果合并。
            // 异步调用说明：统一读取入口会融合缓存、运行时动态值、设备内存态与 PLC 数据源。
            var resolved = await ResolveSvidValuesAsync(requestedSvids, device, plcClient, vidValueStorage).ConfigureAwait(false);

            var response = new S1F4_data
            {
                // 按请求顺序生成返回项，保证主机侧索引对齐。
                StatusList = requestedSvids.Select(svid => new S1F4_status_data
                {
                    SVID = svid,
                    SV = resolved.TryGetValue(svid, out var value) ? value : string.Empty
                }).ToList()
            };

            // 通过 builder 统一构建 S1F4 并回包。
            var reply = S1F4_builder.Build(response);

            try
            {
                // 异步调用说明：优先沿当前 Primary 会话回包，保持请求-应答关联。
                await primary.TryReplyAsync(reply).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 异步调用说明：Primary 回包失败时使用 SecsGem 兜底发送，降低 Host 超时概率。
                await secsGem.SendAsync(reply).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 处理 S2F13（Equipment Constant Request）并回复 S2F14。
        /// 作用：根据请求 ECID 返回设备常量值，空请求时返回全部已配置 ECID。
        /// </summary>
        public static async Task HandleS2F13ReplyAsync(SecsGem secsGem, IDevice device, PrimaryMessageWrapper primary, IVidValueStorage? vidValueStorage = null)
        {
            // 方法主流程：先解析 S2F13 请求，识别是按列表查询还是全量查询。
            var request = S2F13_parser.Parse(primary.PrimaryMessage);

            // if 关键分支：空请求视为查询全部 ECID 常量。
            var requestedEcids = request.IsAllEcid
                ? EcidMap.Value.Keys.OrderBy(x => x).ToList()
                : request.ECIDList;

            var response = new S2F14_data
            {
                // for each 关键分支：按请求顺序返回 ECV，确保 Host 侧索引对齐。
                ConstantList = requestedEcids.Select(ecid =>
                {
                    var value = ResolveEcidValue(ecid, device, vidValueStorage);
                    return new S2F14_constant_data
                    {
                        ECID = ecid,
                        ECV = value
                    };
                }).ToList()
            };

            // 方法关键节点：统一通过 builder 构建 S2F14 回包。
            var reply = S2F14_builder.Build(response);

            try
            {
                // 异步调用说明：优先沿当前 Primary 会话回包，保持请求-应答关联。
                await primary.TryReplyAsync(reply).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 异步调用说明：Primary 回包失败时使用 SecsGem 兜底发送。
                await secsGem.SendAsync(reply).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 处理 S2F29（Equipment Constant Namelist Request）并回复 S2F30。
        /// 作用：根据请求 ECID 返回设备常量列表，空请求时返回全部已配置 ECID。
        /// </summary>
        public static async Task HandleS2F29ReplyAsync(SecsGem secsGem, PrimaryMessageWrapper primary)
        {
            // 方法主流程：先解析 S2F29 请求，识别是按列表查询还是全量查询。
            var request = S2F29_parser.Parse(primary.PrimaryMessage);

            // if 关键分支：空请求视为查询全部 ECID 常量。
            var requestedEcids = request.IsAllEcid
                ? EcidMap.Value.Keys.OrderBy(x => x).ToList()
                : request.ECIDList;

            var response = new S2F30_data
            {
                // for each 关键分支：按请求顺序返回常量定义，确保 Host 侧索引对齐。
                ConstantList = requestedEcids.Select(ecid =>
                {
                    // if 关键分支：已配置 ECID 返回 CSV 中的名称、格式和默认值。
                    if (EcidMap.Value.TryGetValue(ecid, out var row))
                    {
                        return new S2F30_constant_data
                        {
                            ECID = ecid,
                            ECNAME = row.Name,
                            ECMIN = string.Empty,
                            ECMAX = string.Empty,
                            ECDEF = row.DefaultValue,
                            UNITS = string.Empty,
                            Format = row.Format
                        };
                    }

                    // 兜底分支：未配置 ECID 返回空定义项，保持请求与响应索引对齐。
                    return new S2F30_constant_data
                    {
                        ECID = ecid
                    };
                }).ToList()
            };

            // 方法关键节点：统一通过 builder 构建 S2F30 回包。
            var reply = S2F30_builder.Build(response);

            try
            {
                // 异步调用说明：优先沿当前 Primary 会话回包，保持请求-应答关联。
                await primary.TryReplyAsync(reply).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 异步调用说明：Primary 回包失败时使用 SecsGem 兜底发送，降低 Host 超时概率。
                await secsGem.SendAsync(reply).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 处理 S1F11（Status Variable Namelist Request）并回复 S1F12。
        /// 作用：根据请求的 SVID 列表返回状态变量名称清单，空请求时返回全部已配置 SVID。
        /// </summary>
        public static async Task HandleS1F11ReplyAsync(SecsGem secsGem, PrimaryMessageWrapper primary)
        {
            // 方法主流程：先解析请求，识别是按列表查询还是全量查询。
            var request = S1F11_parser.Parse(primary.PrimaryMessage);

            // if 关键分支：空请求视为查询全部 SVID 名称。
            var requestedSvids = request.IsAllSvid
                ? SvidMap.Value.Keys.OrderBy(x => x).ToList()
                : request.SVIDList;

            var response = new S1F12_data
            {
                // for each 关键分支：按请求顺序输出 SVID 对应名称，未命中时返回空名称。
                StatusNameList = requestedSvids.Select(svid =>
                {
                    // vid为value
                    if (SvidMap.Value.TryGetValue(svid, out var row))
                    {
                        return new S1F12_status_data
                        {
                            SVID = svid,
                            SVNAME = row.Name,
                            UNITS = string.Empty
                        };
                    }

                    // 兜底分支：请求 SVID 未配置时返回空名称项，保持索引对齐。
                    return new S1F12_status_data
                    {
                        SVID = svid,
                        SVNAME = string.Empty,
                        UNITS = string.Empty
                    };
                }).ToList()
            };

            // 方法关键节点：统一通过 builder 构建 S1F12 回包。
            var reply = S1F12_builder.Build(response);

            try
            {
                // 异步调用说明：优先沿当前 Primary 会话回包，保持请求-应答关联。
                await primary.TryReplyAsync(reply).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 异步调用说明：Primary 回包失败时使用 SecsGem 兜底发送。
                await secsGem.SendAsync(reply).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 解析 ECID 常量值。
        /// 作用：优先返回运行态动态值，其次返回设备内存态，最后回退到 CSV 默认值。
        /// </summary>
        private static string ResolveEcidValue(uint ecid, IDevice device, IVidValueStorage? vidValueStorage)
        {
            // if 关键分支：ECID 未配置时返回空字符串。
            if (!EcidMap.Value.TryGetValue(ecid, out var row))
            {
                return string.Empty;
            }

            // 优先级 1：运行态动态值（按 VID 优先，Name 兜底）。
            if (vidValueStorage is not null)
            {
                if (vidValueStorage.TryGetByVid(row.VID, out var valueByVid) && !string.IsNullOrWhiteSpace(valueByVid))
                {
                    return valueByVid;
                }

                if (vidValueStorage.TryGetByName(row.Name, out var valueByName) && !string.IsNullOrWhiteSpace(valueByName))
                {
                    return valueByName;
                }
            }

            // 优先级 2：设备内存态常量。
            if (string.Equals(row.Name, "SoftwareVersion", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.Name, "SOFTREV", StringComparison.OrdinalIgnoreCase))
            {
                return device.SoftwareRevision ?? string.Empty;
            }

            if (string.Equals(row.Name, "HardwareVersion", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.Name, "MDLN", StringComparison.OrdinalIgnoreCase))
            {
                return device.ModelNumber ?? string.Empty;
            }

            // 兜底分支：回退到 CSV DefaultValue。
            return row.DefaultValue ?? string.Empty;
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
                        // 打印删除的 RPTID，便于与 Host 下发内容对齐排查。
                        Console.WriteLine($"S2F33 delete report. RPTID={rptId}");
                        reportStorage.RemoveReport(rptId);
                    }

                    foreach (var kvp in data.DefinedReports)
                    {
                        // 打印定义的 RPTID 与 VID 列表，便于确认运行态是否已生效。
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
                // 先打印当前已定义 RPTID 快照，用于排查 Host 认为已定义但运行态未命中的情况。
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

            // S2F35 失败时输出失败明细，便于 Host 侧定位具体 CEID/RPTID。
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
            // RPTID 使用 U4，联调范围通常远小于 100000，顺序扫描开销可接受。
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
                // CEED: 1=Enable, 0=Disable
                if (data.CEED == 1)
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
                else if (data.CEED == 0)
                {
                    if (data.IsAllEvents)
                    {
                        // 接口没有 DisableAll，这里按当前已启用列表逐个禁用
                        foreach (var ceid in eventEnableStorage.GetAllEnabledEvents().ToList())
                        {
                            eventEnableStorage.DisableEvent(ceid);
                        }
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

                            eventEnableStorage.DisableEvent(ceid);
                        }
                    }
                }
                else
                {
                    // 非法 CEED
                    eac = 2;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"S2F37 storage error: {ex.Message}");
                eac = 2;
                await primary.TryReplyAsync(S2F38_builder.Build(eac));
                return;
            }

            if (eac == 0 && data.CEED == 1)
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
            PlcClient? plcClient,
            IVidValueStorage? vidValueStorage)
        {
            var result = new Dictionary<uint, string>();
            var rowsToRead = new List<(uint Svid, SvidMapRow Row)>();

            // 先尝试缓存命中，未命中再进入读取队列。
            foreach (var svid in svids)
            {
                // 未在映射表中定义的 SVID 直接给空值。
                if (!SvidMap.Value.TryGetValue(svid, out var row))
                {
                    result[svid] = string.Empty;
                    continue;
                }

                // 缓存命中且未过期时直接使用。
                if (TryGetCacheValue(svid, out var cached))
                {
                    result[svid] = cached;
                    continue;
                }

                rowsToRead.Add((svid, row));
            }

            // 全部命中缓存时可直接返回。
            if (rowsToRead.Count == 0)
            {
                return result;
            }

            // 创建 provider 上下文并准备数据源实现。
            var context = new SvidResolveContext(device, plcClient, vidValueStorage);
            var providers = new ISvidValueProvider[]
            {
                new DeviceSvidValueProvider(),// 在线device
                //new PlcSvidValueProvider(),// 通过PLC读取
                new RuntimeVidSvidValueProvider(),// 运行时VID/SVID键值表
                //new WorkflowSvidValueProvider()// 工作流SVID
            };

            // 按 provider 分组批量读取并合并结果。
            foreach (var provider in providers)
            {
                var group = rowsToRead.Where(x => provider.CanHandle(x.Row)).ToList();

                // 当前 provider 没有可处理项则跳过。
                if (group.Count == 0)
                {
                    continue;
                }

                var batchResult = await provider.ReadBatchAsync(group, context).ConfigureAwait(false);

                // 写入结果并回填缓存。
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
            // 未注入 PlcClient 时返回空值，避免空引用。
            if (plcClient is null)
            {
                return string.Empty;
            }

            try
            {
                // 首次调用时启动订阅，后续调用跳过重复启动。
                if (Interlocked.Exchange(ref _plcSubscriptionStarted, 1) == 0)
                {
                    plcClient.StartSubZeromqChanged();
                }

                // 调用 GY.PLC.Comm 提供的 ReadHolding 读取寄存器。
                var (success, _, value) = await plcClient.ReadHolding(holdingAddress).ConfigureAwait(false);

                // 读取失败或无值时返回空字符串。
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
            public SvidResolveContext(IDevice device, PlcClient? plcClient, IVidValueStorage? vidValueStorage)
            {
                Device = device;
                PlcClient = plcClient;
                VidValueStorage = vidValueStorage;
            }

            public IDevice Device { get; }

            public PlcClient? PlcClient { get; }

            public IVidValueStorage? VidValueStorage { get; }
        }

        /// <summary>
        /// 运行时 VID 动态值数据源 provider。
        /// 说明：用于返回由业务链路实时维护的最新状态值（例如 OnlineStatus/RemoteStatus）。
        /// </summary>
        private sealed class RuntimeVidSvidValueProvider : ISvidValueProvider
        {
            public bool CanHandle(SvidMapRow row)
            {
                // 运行时动态值表可作为通用补充源，默认参与全部 SVID 解析。
                return true;
            }

            public Task<Dictionary<uint, string>> ReadBatchAsync(
                IReadOnlyList<(uint Svid, SvidMapRow Row)> rows,
                SvidResolveContext context)
            {
                var result = new Dictionary<uint, string>();

                // if 未注入动态值存储时直接返回空结果，不影响其他 provider。
                if (context.VidValueStorage is null)
                {
                    return Task.FromResult(result);
                }

                // 按 VID 优先、Name 兜底的策略读取动态值。
                foreach (var (svid, row) in rows)
                {
                    if (context.VidValueStorage.TryGetByVid(row.VID, out var valueByVid) && !string.IsNullOrWhiteSpace(valueByVid))
                    {
                        result[svid] = valueByVid;
                        continue;
                    }

                    if (context.VidValueStorage.TryGetByName(row.Name, out var valueByName) && !string.IsNullOrWhiteSpace(valueByName))
                    {
                        result[svid] = valueByName;
                    }
                }

                return Task.FromResult(result);
            }
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
                // 优先使用 SourceType=Property 路由到设备属性源。
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

                // 逐项返回设备内存态值。
                foreach (var (svid, row) in rows)
                {
                    // RECIPEID 返回 device.RECIPEID。
                    if (string.Equals(row.Name, "ONLINE_STATE", StringComparison.OrdinalIgnoreCase)
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
                // SourceType=PLC 时明确路由到 PLC。
                if (string.Equals(row.SourceType, "PLC", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // SourceType 已明确且非 PLC 时，不抢占其他 provider。
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

                // 逐项读取 HOLDING 寄存器值。
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
                // 仅处理 SourceType=Workflow 的项。
                return string.Equals(row.SourceType, "Workflow", StringComparison.OrdinalIgnoreCase);
            }

            public Task<Dictionary<uint, string>> ReadBatchAsync(
                IReadOnlyList<(uint Svid, SvidMapRow Row)> rows,
                SvidResolveContext context)
            {
                var result = new Dictionary<uint, string>();

                // 当前骨架阶段统一返回空字符串占位。
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
                // 缓存不存在则直接 miss。
                if (!SvidValueCache.TryGetValue(svid, out var entry))
                {
                    value = string.Empty;
                    return false;
                }

                // 缓存已过期则删除并返回 miss。
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
        /// VID.csv 行模型：用于 S1F3 中按 VID 反查 NAME/HOLDING/SourceType。
        /// </summary>
        private sealed class SvidMapRow
        {
            /// <summary>
            /// 变量编号（VID/SVID）。
            /// </summary>
            public ushort VID { get; init; }

            /// <summary>
            /// 变量类型（例如 SVID/DVID）。
            /// </summary>
            public string Type { get; init; } = string.Empty;

            /// <summary>
            /// 变量名称（NAME）。
            /// </summary>
            public string Name { get; init; } = string.Empty;

            /// <summary>
            /// SECS-II 数据格式（例如 A/U4/List）。
            /// </summary>
            public string Format { get; init; } = "A";

            /// <summary>
            /// 默认值（DefaultValue）。
            /// </summary>
            public string DefaultValue { get; init; } = string.Empty;

            /// <summary>
            /// 数据来源类型（SourceType），例如 Property/PLC/Workflow。
            /// </summary>
            public string SourceType { get; init; } = string.Empty;

            /// <summary>
            /// PLC Holding 地址（当 SourceType=PLC 时使用）。
            /// </summary>
            public ushort HoldingAddress { get; init; }
        }

        /// <summary>
        /// 规范化 SourceType。
        /// </summary>
        private static string NormalizeSourceType(string sourceTypeText, string name)
        {
            // 显式指定 Property 时直接返回规范值。
            if (string.Equals(sourceTypeText, "Property", StringComparison.OrdinalIgnoreCase))
            {
                return "Property";
            }

            // 显式指定 PLC 时直接返回规范值。
            if (string.Equals(sourceTypeText, "PLC", StringComparison.OrdinalIgnoreCase))
            {
                return "PLC";
            }

            // 显式指定 Workflow 时直接返回规范值。
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
        /// 读取并解析 VID.csv。默认表结构为：VID,Type,Name,Format,DefaultValue,Explain
        /// </summary>
        private static Dictionary<uint, SvidMapRow> LoadSvidMap(string csvPath,string vidType)
        {
            var map = new Dictionary<uint, SvidMapRow>();

            // 文件不存在时返回空映射。
            if (!File.Exists(csvPath))
            {
                return map;
            }

            // 逐行解析映射记录。
            foreach (var raw in File.ReadAllLines(csvPath))
            {
                // 过滤空行。
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var line = raw.Trim();

                // 跳过表头。
                if (line.StartsWith("VID,", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = line.Split(',');

                // 列数不足时跳过异常行。
                if (parts.Length < 3)
                {
                    continue;
                }

                // VID 无法解析时跳过。
                if (!ushort.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var vid))
                {
                    continue;
                }

                // VID Type.
                var type = parts[1].Trim();

                // VID Name.
                var name = parts[2].Trim();

                // VID Format.
                var format = parts.Length > 3 ? parts[3].Trim() : "A";

                // VID DefaultValue.
                var defaultValue = parts.Length > 4 ? parts[4].Trim() : string.Empty;

                // if S1F3 仅处理 SVID，非 SVID 行不进入状态查询映射。
                if (!string.Equals(type, vidType.ToUpper(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 兼容扩展列：SourceType/HoldingAddress 若不存在则使用回退策略。
                // 说明：当前第 5 列为 DefaultValue，第 6 列才是 Explain，因此 SourceType 从第 7 列开始读取。
                var sourceTypeText = parts.Length > 6 ? parts[6].Trim() : string.Empty;
                var sourceType = NormalizeSourceType(sourceTypeText, name);

                var holdingAddress = vid;
                if (parts.Length > 7
                    && ushort.TryParse(parts[7].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedHoldingAddress))
                {
                    holdingAddress = parsedHoldingAddress;
                }

                map[vid] = new SvidMapRow
                {
                    Name = name,
                    Type = type,
                    VID = vid,
                    Format = format,
                    DefaultValue = defaultValue,
                    SourceType = sourceType,
                    HoldingAddress = holdingAddress
                };
            }

            return map;
        }

        /// <summary>
        /// 按候选路径加载 VID 映射，优先 VID.csv，缺失时回退到旧路径。
        /// </summary>
        private static Dictionary<uint, SvidMapRow> LoadVidMapFromCandidates(string type)
        {
            var candidates = new[]
            {
                "VID.csv",
                Path.Combine("src", "Messages", "Config", "VID.csv"),
                Path.Combine("..", "Messages", "Config", "VID.csv")
            };

            foreach (var path in candidates)
            {
                var map = LoadSvidMap(path, type);
                if (map.Count > 0)
                {
                    return map;
                }
            }

            return new Dictionary<uint, SvidMapRow>();
        }
    }
}
