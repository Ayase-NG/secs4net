using Secs4Net;
using SECSdata;

namespace SECSparser
{
    /// <summary>
    /// S14F9 消息解析器（增强最小实现）。
    /// 当前按常见结构解析对象域、对象类型与 ControlJob 关键字段。
    /// </summary>
    public static class S14F9_parser
    {
        /// <summary>
        /// 将原始 SecsMessage 解析为 S14F9_data 对象。
        /// </summary>
        public static S14F9_data Parse(SecsMessage msg)
        {
            // if 关键分支：先校验消息头，必须是 S14F9。
            if (msg.S != 14 || msg.F != 9)
            {
                throw new ArgumentException($"Invalid message type. Expected S14F9, but got S{msg.S}F{msg.F}.");
            }

            var root = msg.SecsItem;

            // if 关键分支：消息体必须为根 List 且至少包含两个 ASCII 项。
            if (root is null || root.Format != SecsFormat.List || root.Count < 2)
            {
                throw new InvalidOperationException("Invalid S14F9 message: root list missing or invalid.");
            }

            // 前两项分别为对象规范和对象类型。
            var objectDomainItem = root[0];
            var objectTypeItem = root[1];

            // if 关键分支：前两项按 ASCII 解析对象域和对象类型。
            if (objectDomainItem.Format != SecsFormat.ASCII || objectTypeItem.Format != SecsFormat.ASCII)
            {
                throw new InvalidOperationException("Invalid S14F9 message: object domain/type should be ASCII.");
            }

            var data = new S14F9_data
            {
                OBJSPEC = objectDomainItem.GetString() ?? string.Empty,
                OBJTYPE = objectTypeItem.GetString() ?? string.Empty,
                timeStamp = DateTime.UtcNow
            };

            // if 关键分支：第 3 项若为列表则作为对象明细列表。
            if (root.Count < 3 || root[2].Format != SecsFormat.List)
            {
                return data;
            }

            // 遍历对象各属性。
            foreach (var jobItem in root[2].Items)
            {
                // if 关键分支：单个对象必须是属性列表。
                if (jobItem.Format != SecsFormat.List)
                    continue;

                var job = new S14F9_control_job_data();

                // 遍历属性列表，按键值对解析关键字段。第一个必定为属性名，第二个为属性值。
                foreach (var attr in jobItem.Items)
                {
                    // if 关键分支：属性项格式应为 L[2] { key, value }。
                    if (attr.Format != SecsFormat.List || attr.Count < 2)
                        continue;

                    var keyItem = attr[0];
                    var valueItem = attr[1];
                    if (keyItem?.Format != SecsFormat.ASCII || valueItem is null)
                        continue;

                    var key = (keyItem.GetString() ?? string.Empty).Trim();
                    // 实际属性区分，后续可能会更改为映射表或反射方式以适应更多属性。
                    switch (key)
                    {
                        // 对象唯一标识符
                        case "ObjID":
                            if (valueItem.Format == SecsFormat.ASCII)
                            {
                                job.ObjID = valueItem.GetString() ?? string.Empty;
                            }
                            break;
                        // 关联ProcessJob的List，即关联S16F15中的ProcessJobSpec列表
                        case "ProcessingCtrlSpec":
                            if(valueItem.Format == SecsFormat.List)
                            {
                                foreach (var ctrl in valueItem.Items)
                                {
                                    if (ctrl.Format == SecsFormat.ASCII)
                                    {
                                        var ctrlSpec = ctrl.GetString();
                                        if (!string.IsNullOrWhiteSpace(ctrlSpec))
                                        {
                                            job.ProcessingCtrlSpec.Add(ctrlSpec.Trim());
                                        }
                                    }
                                }
                            }
                            break;
                        // 关联Carrier的List，当Carrier抵达是判断广联的ControlJob
                        case "CarrierInputSpec":
                            if (valueItem.Format == SecsFormat.List)
                            {
                                foreach (var carrier in valueItem.Items)
                                {
                                    if (carrier.Format == SecsFormat.ASCII)
                                    {
                                        var carrierId = carrier.GetString();
                                        if (!string.IsNullOrWhiteSpace(carrierId))
                                        {
                                            job.CarrierInputSpec.Add(carrierId.Trim());
                                        }
                                    }
                                }
                            }
                            break;
                        case "StartMethod":
                            if (valueItem.Format == SecsFormat.Boolean)
                            {
                                job.StartMethod = valueItem.FirstValueOrDefault<bool>(false);
                            }
                            break;
                    }
                }

                data.ControlJobs.Add(job);
            }

            return data;
        }
    }
}
