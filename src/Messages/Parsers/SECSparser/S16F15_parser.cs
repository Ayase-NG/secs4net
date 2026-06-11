using Secs4Net;
using SECSdata;

namespace SECSparser
{
    /// <summary>
    /// S16F15 消息解析器（增强最小实现）。
    /// 当前按常见结构 L[2] { DATAID, PROCESS_JOB_LIST } 解析关键字段。
    /// </summary>
    public static class S16F15_parser
    {
        /// <summary>
        /// 将原始 SecsMessage 解析为 S16F15_data 对象。
        /// </summary>
        public static S16F15_data Parse(SecsMessage msg)
        {
            // if 关键分支：先校验消息头，必须是 S16F15。
            if (msg.S != 16 || msg.F != 15)
            {
                throw new ArgumentException($"Invalid message type. Expected S16F15, but got S{msg.S}F{msg.F}.");
            }

            var root = msg.SecsItem;

            // if 关键分支：消息体必须为根 List 且至少包含 DATAID 和作业列表两项。
            if (root is null || root.Format != SecsFormat.List || root.Count < 2)
            {
                throw new InvalidOperationException("Invalid S16F15 message: root list missing or invalid.");
            }

            // if 关键分支：解析 DATAID，支持 U1/U2/U4/B 等常见整数格式。
            var dataIdItem = root[0];
            var dataId = dataIdItem.Format switch
            {
                SecsFormat.U1 => dataIdItem.FirstValueOrDefault<byte>(0),
                SecsFormat.U2 => dataIdItem.FirstValueOrDefault<ushort>(0),
                SecsFormat.U4 => dataIdItem.FirstValueOrDefault<uint>(0),
                SecsFormat.Binary => dataIdItem.FirstValueOrDefault<byte>(0),
                _ => throw new InvalidOperationException("Invalid S16F15 message: DATAID type error.")
            };

            // if 关键分支：第二项必须是 Process Job 列表。
            var pjList = root[1];
            if (pjList.Format != SecsFormat.List)
            {
                throw new InvalidOperationException("Invalid S16F15 message: PROCESS_JOB_LIST missing or invalid.");
            }

            var data = new S16F15_data
            {
                DATAID = dataId,
                timeStamp = DateTime.UtcNow
            };
            // 遍历每一个Process Job
            foreach (var pjItem in pjList.Items)
            {
                // if 关键分支：单个 Process Job 至少应包含 PJID、MTRL_ORDER、PROCESS_SPEC。
                if (pjItem.Format != SecsFormat.List || pjItem.Count < 4)
                    continue;

                var pj = new S16F15_process_job_data();

                // 0.E40 固定字段结构：PJID
                if (pjItem[0].Format == SecsFormat.ASCII)
                {
                    pj.PJID = pjItem[0].GetString() ?? string.Empty;
                }

                //  1.E40 固定字段结构：MF
                if (pjItem[1].Format == SecsFormat.ASCII)
                {
                    // 目前暂不处理 MF 字段，但可以在此处添加相关解析逻辑。
                    pj.MF = pjItem[1].GetString() ?? string.Empty;
                }

                // 2.E40 固定字段结构： MTRLSPEC: L[2] { CARRIER_ID, CARRIER_LIST }
                var carrierList = pjItem[2];
                if (carrierList.Format == SecsFormat.List)
                {
                    foreach (var carrierItem in carrierList.Items)
                    {
                        if (carrierItem.Format != SecsFormat.List || carrierItem.Count < 2)
                            continue;

                        var carrier = new S16F15_carrier_data();
                        if (carrierItem[0].Format == SecsFormat.ASCII)
                        {
                            carrier.CarrierId = carrierItem[0].GetString() ?? string.Empty;
                        }

                        var slotList = carrierItem[1];
                        if (slotList.Format == SecsFormat.List)
                        {
                            foreach (var slotItem in slotList.Items)
                            {
                                try
                                {
                                    var slot = slotItem.Format switch
                                    {
                                        SecsFormat.U1 => slotItem.FirstValueOrDefault<byte>(0),
                                        SecsFormat.U2 => slotItem.FirstValueOrDefault<ushort>(0),
                                        SecsFormat.U4 => slotItem.FirstValueOrDefault<uint>(0),
                                        SecsFormat.Binary => slotItem.FirstValueOrDefault<byte>(0),
                                        _ => 0u
                                    };
                                    if (slot != 0)
                                    {
                                        carrier.Slots.Add(slot);
                                    }
                                }
                                catch
                                {
                                    // 兜底分支：单个槽位异常时忽略，继续解析其余槽位。
                                }
                            }
                        }

                        pj.Carriers.Add(carrier);
                    }
                }

                // 3.E40 固定字段结构：PROCESS_SPEC: L[3] { ..., RecipeId, ... }
                var processSpec = pjItem[3];
                if (processSpec.Format == SecsFormat.List && processSpec.Count >= 2 && processSpec[1].Format == SecsFormat.ASCII)
                {
                    pj.RecipeId = processSpec[1].GetString() ?? string.Empty;
                }

                // 4.E40 固定字段结构：True or False，表示是否自动作业。
                if (pjItem.Count > 4)
                {
                    var prProcessStart = pjItem[4];
                    if (prProcessStart.Format == SecsFormat.Boolean)
                    {
                        pj.PRPROCESSSTART = prProcessStart.FirstValueOrDefault<bool>();
                    }
                }

                // 5.E40 固定字段结构：存入CEID列表，定义停止触发事件。
                if (pjItem.Count > 5)
                {
                    var prPauseEvent = pjItem[5];
                    if (prPauseEvent.Format == SecsFormat.List)
                    {
                        foreach (var ceidItem in prPauseEvent.Items)
                        {
                            if (ceidItem.Format == SecsFormat.U4)
                            {
                                pj.PRPAUSEEVENT.Add(ceidItem.FirstValueOrDefault<uint>(0));
                            }
                        }
                    }
                }

                data.ProcessJobs.Add(pj);
            }

            return data;
        }
    }
}
