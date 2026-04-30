using Microsoft.Extensions.Logging;
using Secs4Net;
using SECShandler.Interfaces;
using SECSbuilder;
using SECSdata;
using SECSparser;

namespace SECShandler.Functions
{
    /// <summary>
    /// 事件上报相关的 SxFy 处理函数集合。
    /// 包含 Host 下发的事件配置类消息处理（S2F33/S2F35/S2F37）
    /// 以及设备侧主动报警上报（S5F1）的发送重试能力。
    /// </summary>
    public static class EventReportSxFyFunctions
    {
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
    }
}
