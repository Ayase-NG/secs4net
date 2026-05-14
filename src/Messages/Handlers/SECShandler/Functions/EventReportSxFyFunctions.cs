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
        // 方法关键节点：缓存 SVID.csv 映射，避免每次 S1F3 都重复读盘。
        private static readonly Lazy<Dictionary<uint, SvidMapRow>> SvidMap = new(() => LoadSvidMap("SVID.csv"));

        // 方法关键节点：保证 PlcClient 的订阅初始化只执行一次。
        private static int _plcSubscriptionStarted;

        /// <summary>
        /// 处理 S1F3（Selected Equipment Status Request）并回复 S1F4。
        /// 新逻辑：通过 SVID.csv 反查 NAME 与 HOLDING，后续由 PLC 读取 HOLDING 寄存器返回值。
        /// </summary>
        public static async Task HandleS1F3ReplyAsync(SecsGem secsGem, IDevice device, PrimaryMessageWrapper primary, PlcClient? plcClient = null)
        {
            // 方法关键节点：先解析 S1F3 请求，拿到请求的 SVID 列表。
            var request = S1F3_parser.Parse(primary.PrimaryMessage);
            var response = new S1F4_data();

            // if 关键分支：当前未请求具体 SVID 时，保持最小行为（返回空列表）。
            if (!request.IsAllSvid)
            {
                // for each 关键分支：按请求顺序逐项处理，保证与 S1F3 请求顺序一致。
                foreach (var svid in request.SVIDList)
                {
                    // if 关键分支：先通过 SVID.csv 反查 NAME/HOLDING；未命中则返回空值。
                    if (!SvidMap.Value.TryGetValue(svid, out var row))
                    {
                        response.StatusList.Add(new S1F4_status_data
                        {
                            SVID = svid,
                            SV = string.Empty
                        });
                        continue;
                    }

                    string svValue;

                    // if 关键分支：当 SVID 反查 NAME 为 RECIPEID 时，直接返回设备实时 RECIPEID。
                    if (string.Equals(row.Name, "RECIPEID", StringComparison.OrdinalIgnoreCase))
                    {
                        svValue = device.RECIPEID ?? string.Empty;
                    }
                    else
                    {
                        // else 关键分支：非 RECIPEID 变量继续走 PLC HOLDING 实读。
                        svValue = await ReadHoldingValueAsync(plcClient, row.HoldingAddress).ConfigureAwait(false);
                    }

                    response.StatusList.Add(new S1F4_status_data
                    {
                        SVID = svid,
                        SV = svValue
                    });
                }
            }

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
                        reportStorage.RemoveReport(rptId);
                    }

                    foreach (var kvp in data.DefinedReports)
                    {
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
                foreach (var kvp in data.Links)
                {
                    if (!eventLinkStorage.IsCeidValid(kvp.Key))
                    {
                        lrack = 4;
                        break;
                    }
                    foreach (var rptId in kvp.Value)
                    {
                        if (!reportStorage.ContainsReport(rptId))
                        {
                            lrack = 5;
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

            await primary.TryReplyAsync(S2F36_builder.Build(lrack));
        }

        /// <summary>
        /// 处理 S2F37（Enable/Disable Event Report）并回复 S2F38。
        /// EAC 常见约定：0=成功，1=CEID无效，2=格式或内部错误。
        /// </summary>
        public static async Task HandleS2F37ReplyAsync(
            PrimaryMessageWrapper primary,
            IEventLinkStorage eventLinkStorage,
            IEventEnableStorage eventEnableStorage)
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

            await primary.TryReplyAsync(S2F38_builder.Build(eac));
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
        /// SVID.csv 行模型：用于 S1F3 中按 SVID 反查 NAME/HOLDING。
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
        }

        /// <summary>
        /// 读取并解析 SVID.csv。
        /// CSV 头要求：SVID,NAME,HOLDING,DESCRIPTION。
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

                // if 关键分支：跳过表头。
                if (line.StartsWith("SVID,", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = line.Split(',', 4);

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

                // if 关键分支：HOLDING 无法解析时跳过。
                if (!ushort.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var holding))
                {
                    continue;
                }

                map[svid] = new SvidMapRow
                {
                    Name = name,
                    HoldingAddress = holding
                };
            }

            return map;
        }
    }
}
