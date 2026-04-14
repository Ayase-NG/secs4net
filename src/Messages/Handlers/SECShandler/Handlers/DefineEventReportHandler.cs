using Secs4Net;
using SECSdata;
using SECSparser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SECSbuilder;
using SECShandler.Interfaces;

namespace SECShandler.Handlers
{
    /// <summary>
    /// 处理 S2F33 Define Report 消息的处理器。
    /// 主机通过此消息定义报告（RPTID 与 VID 的映射关系）。
    /// 设备需要存储这些定义，并在后续的事件上报中使用。
    /// </summary>
    public class DefineEventReportHandler
    {
        private readonly SecsGem _secsGem;
        private readonly IReportStorage _reportStorage;             // 管理 RPTID -> VID 列表
        private readonly IEventLinkStorage _eventLinkStorage;       // 管理 CEID -> RPTID 列表
        private readonly IEventEnableStorage _eventEnableStorage;   // 管理 CEID 是否启用

        /// <summary>
        /// 构造函数，通过依赖注入获取通信引擎和报告存储接口。
        /// </summary>
        /// <param name="secsGem">Secs4Net 消息引擎，用于发送回复。</param>
        /// <param name="reportStorage">报告存储接口，用于保存/更新报告定义。</param>
        public DefineEventReportHandler(
            SecsGem secsGem,
            IReportStorage reportStorage,
            IEventLinkStorage eventLinkStorage,
            IEventEnableStorage eventEnableStorage)
        {
            _secsGem = secsGem;
            _reportStorage = reportStorage;
            _eventLinkStorage = eventLinkStorage;
            _eventEnableStorage = eventEnableStorage;
        }

        /// <summary>
        /// 处理 S2F33 消息。并回复 S2F34 消息确认报告定义的结果。
        /// </summary>
        /// <param name="primary">收到的原始 S2F33 消息。</param>
        public async Task HandleS2F33ReplyAsync(PrimaryMessageWrapper primary)
        {
            S2F33_data data;
            byte drack = 0; // 默认成功
            var primaryMsg=primary.PrimaryMessage;
            try
            {
                // 1. 解析消息
                data = S2F33_parser.Parse(primaryMsg);
            }
            catch (Exception ex)
            {
                // 解析失败：格式错误
                Console.WriteLine($"S2F33 parse error: {ex.Message}");
                drack = 2; // DRACK = 2 表示格式无效
                // 回复时使用原Message，避免SystemBytes不匹配导致主机无法识别回复
                await primary.TryReplyAsync(S2F34_builder.Build(drack));
                return;
            }

            // 2. 根据解析结果更新报告存储
            try
            {
                if (data.IsDeleteAll)
                {
                    // 删除所有报告
                    _reportStorage.ClearAllReports();
                }
                else
                {
                    // 处理删除特定报告（VID列表为空的情况）
                    foreach (var rptId in data.DeletedRptIds)
                    {
                        _reportStorage.RemoveReport(rptId);
                    }

                    // 处理定义或更新报告
                    foreach (var kvp in data.DefinedReports)
                    {
                        _reportStorage.AddOrUpdateReport(kvp.Key, kvp.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"S2F33 storage error: {ex.Message}");
                drack = 1; // 其他错误，如内存不足
                await primary.TryReplyAsync(S2F34_builder.Build(drack));
                return;
            }

            // 3. 发送成功确认
            await primary.TryReplyAsync(S2F34_builder.Build(drack));
        }

        /// <summary>
        /// 处理 S2F35 消息。并回复 S2F36 消息确认链接关系的定义结果。
        /// </summary>
        /// <param name="primaryMsg">收到的原始 S2F35 消息。</param>
        public async Task HandleS2F35ReplyAsync(PrimaryMessageWrapper primary)
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
                lrack = 2; // 格式无效
                await primary.TryReplyAsync(S2F36_builder.Build(lrack));
                return;
            }

            try
            {
                // 验证 CEID 和 RPTID 是否存在
                foreach (var kvp in data.Links)
                {
                    if (!_eventLinkStorage.IsCeidValid(kvp.Key))
                    {
                        lrack = 4; // CEID 不存在
                        break;
                    }
                    foreach (var rptId in kvp.Value)
                    {
                        // 遍历所有链接的 RPTID，确保它们都已定义
                        if (!_reportStorage.ContainsReport(rptId))
                        {
                            lrack = 5; // RPTID 未定义
                            break;
                        }
                    }
                    if (lrack != 0) break;
                }

                if (lrack == 0)
                {
                    // 更新链接关系
                    _eventLinkStorage.UpdateEventLinks(data.Links);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"S2F35 storage error: {ex.Message}");
                lrack = 1; // 其他错误
                await primary.TryReplyAsync(S2F36_builder.Build(lrack));
                return;
            }

            await primary.TryReplyAsync(S2F36_builder.Build(lrack));
        }

        public async Task HandleS2F37ReplyAsync(PrimaryMessageWrapper primary)
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
                eac = 2; // 格式无效
                await primary.TryReplyAsync(S2F38_builder.Build(eac));
                return;
            }

            try
            {
                if (data.IsAllEvents)
                {
                    _eventEnableStorage.EnableAllEvents();
                }
                else
                {
                    foreach (var ceid in data.CeidList)
                    {
                        if (!_eventLinkStorage.IsCeidValid(ceid))
                        {
                            eac = 1; // 无效 CEID
                            break;
                        }
                        _eventEnableStorage.EnableEvent(ceid);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"S2F37 storage error: {ex.Message}");
                eac = 2; // 其他错误
                await primary.TryReplyAsync(S2F38_builder.Build(eac));
                return;
            }

            await primary.TryReplyAsync(S2F38_builder.Build(eac));
        }
    }
}
