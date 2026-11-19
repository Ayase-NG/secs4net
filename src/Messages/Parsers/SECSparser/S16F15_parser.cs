using Secs4Net;
using SECSdata;
using System.Globalization;

namespace SECSparser
{
    /// <summary>
    /// S16F15 解析器，严格校验当前支持的 Carrier + Slot 作业结构。
    /// 任一 PJ 结构错误时拒绝整次解析，不跳过条目或使用默认值补齐必填字段。
    /// </summary>
    public static class S16F15_parser
    {
        /// <summary>
        /// 校验整批 PJ 的结构和用于选择物料结构的 MF，再返回最小数据模型；不校验设备业务状态。
        /// </summary>
        /// <remarks>
        /// S16F15 PRJobMultiCreate 的 Carrier + Slot 形式如下；各变长列表仅展开一个元素：
        /// <code>
        /// S16F15 W
        /// <L[2]
        ///   <U4[1] DATAID>
        ///   <L[n]                         // PROCESS_JOB_LIST：n 个 PJ
        ///     <L[6]
        ///       <A PRJOBID>               // [0] Process Job ID
        ///       <B[1] MF>                 // [1] Material Format
        ///       <L[m]                     // [2] MTRLSPEC：m 个 Carrier
        ///         <L[2]
        ///           <A CARRIERID>
        ///           <L[s]                 // s 个槽位，每项是单个整数
        ///             <U1[1] SLOTID>
        ///           >
        ///         >
        ///       >
        ///       <L[3]                     // [3] PROCESS_SPEC
        ///         <U1[1] PRRECIPEMETHOD>
        ///         <A RCPSPEC>             // 配方标识，映射到 RecipeId
        ///         <L[p]                   // p 个配方参数，无参数时为 L[0]
        ///           <L[2]
        ///             <A RCPPARNM>
        ///             <任意合法 Item RCPPARVAL>
        ///           >
        ///         >
        ///       >
        ///       <BOOLEAN[1] PRPROCESSSTART> // [4] 必须显式提供启动标志
        ///       <L[e]                     // [5] PRPAUSEEVENT，无事件时为 L[0]
        ///         <U4[1] CEID>
        ///       >
        ///     >
        ///   >
        /// >
        /// .
        /// </code>
        /// 此处仅描述当前支持的物料结构，不涵盖其他 MF 对应的物料定义。
        /// MF 仅支持 0D；除 B[1] 0x0D 外，还兼容旧 ASCII 写法 0D 和 0x0D。
        /// DATAID 和 SLOTID 兼容 U1/U2/U4/B 整数标量。
        /// PRPAUSEEVENT 和配方参数的结构合法，不代表设备支持对应的暂停或参数修改能力。
        /// </remarks>
        public static S16F15_data Parse(SecsMessage msg)
        {
            ArgumentNullException.ThrowIfNull(msg);
            // 先校验消息头，必须是 S16F15。
            if (msg.S != 16 || msg.F != 15)
            {
                throw new ArgumentException($"Invalid message type. Expected S16F15, but got S{msg.S}F{msg.F}.");
            }

            var root = msg.SecsItem;

            // 根必须且只能包含 DATAID 和 PROCESS_JOB_LIST。
            RequireList(root, "ROOT", 2);
            var dataId = ReadUnsignedScalar(root![0], "DATAID");
            var pjList = root[1];
            RequireList(pjList, "PROCESS_JOB_LIST");

            var data = new S16F15_data
            {
                DATAID = dataId,
                timeStamp = DateTime.UtcNow
            };
            // 完整解析所有 PJ 后才返回结果；异常时调用层无法取得部分解析结果。
            for (var index = 0; index < pjList.Count; index++)
            {
                var path = $"PROCESS_JOB_LIST[{index}]";
                var pjItem = pjList[index];
                RequireList(pjItem, path, 6);
                // 先确认 MF 支持 Carrier + Slot，再按此结构解析 MTRLSPEC。
                var pj = new S16F15_process_job_data
                {
                    PJID = ReadAscii(pjItem[0], $"{path}.PJID"),
                    MF = ReadMaterialFormat(pjItem[1], $"{path}.MF")
                };

                var carrierList = pjItem[2];
                RequireList(carrierList, $"{path}.MTRLSPEC");
                // 每个 Carrier 都必须是 { CarrierID, SlotList }，不能静默丢弃物料。
                for (var carrierIndex = 0; carrierIndex < carrierList.Count; carrierIndex++)
                {
                    var carrierPath = $"{path}.MTRLSPEC[{carrierIndex}]";
                    var carrierItem = carrierList[carrierIndex];
                    RequireList(carrierItem, carrierPath, 2);
                    var carrier = new S16F15_carrier_data
                    {
                        CarrierId = ReadAscii(carrierItem[0], $"{carrierPath}.CarrierID")
                    };
                    var slotList = carrierItem[1];
                    RequireList(slotList, $"{carrierPath}.Slots");
                    // 仅验证槽位的编码类型和数量，范围、重复及 Mapping 一致性由业务层校验。
                    for (var slotIndex = 0; slotIndex < slotList.Count; slotIndex++)
                        carrier.Slots.Add(ReadUnsignedScalar(slotList[slotIndex], $"{carrierPath}.Slots[{slotIndex}]"));

                    pj.Carriers.Add(carrier);
                }

                var processSpec = pjItem[3];
                RequireList(processSpec, $"{path}.PROCESS_SPEC", 3);
                // 当前支持单字节的配方方法编码，不将缺失或数组值当成默认方法。
                if (processSpec[0].Count != 1 || processSpec[0].Format is not (SecsFormat.U1 or SecsFormat.Binary))
                    throw Invalid($"{path}.PROCESS_SPEC.Method", "expected U1[1] or B[1]");

                pj.RecipeId = ReadAscii(processSpec[1], $"{path}.PROCESS_SPEC.RecipeID");
                var parameters = processSpec[2];
                RequireList(parameters, $"{path}.PROCESS_SPEC.Parameters");
                // 参数必须是 { 参数名, 参数值 }；仅验证结构，不增加配方参数修改能力。
                for (var parameterIndex = 0; parameterIndex < parameters.Count; parameterIndex++)
                {
                    var parameterPath = $"{path}.PROCESS_SPEC.Parameters[{parameterIndex}]";
                    RequireList(parameters[parameterIndex], parameterPath, 2);
                    ReadAscii(parameters[parameterIndex][0], $"{parameterPath}.Name");
                }

                // 启动标志必须明确提供单个 Boolean，不能沿用模型默认 true。
                var start = pjItem[4];
                if (start.Format != SecsFormat.Boolean || start.Count != 1)
                    throw Invalid($"{path}.PRPROCESSSTART", "expected Boolean[1]");
                pj.PRPROCESSSTART = start.FirstValue<bool>();

                var pauseEvents = pjItem[5];
                RequireList(pauseEvents, $"{path}.PRPAUSEEVENT");
                // 空列表结构合法；非空列表是否受设备支持由业务层另行判断。
                for (var eventIndex = 0; eventIndex < pauseEvents.Count; eventIndex++)
                {
                    var ceid = pauseEvents[eventIndex];
                    if (ceid.Format != SecsFormat.U4 || ceid.Count != 1)
                        throw Invalid($"{path}.PRPAUSEEVENT[{eventIndex}]", "expected U4[1]");
                    pj.PRPAUSEEVENT.Add(ceid.FirstValue<uint>());
                }

                data.ProcessJobs.Add(pj);
            }

            return data;
        }

        /// <summary>
        /// 验证列表类型及固定字段数，错误信息包含具体字段路径。
        /// </summary>
        private static void RequireList(Item? item, string path, int? count = null)
        {
            // 可变长列表只校验类型；是否允许空集合属于业务约束。
            if (item is null || item.Format != SecsFormat.List || (count.HasValue && item.Count != count.Value))
                throw Invalid(path, count.HasValue ? $"expected L[{count.Value}]" : "expected List");
        }

        /// <summary>
        /// 验证 ASCII 字段并保留原始文本，非空等业务约束由调用方处理。
        /// </summary>
        private static string ReadAscii(Item item, string path)
        {
            if (item.Format != SecsFormat.ASCII)
                throw Invalid(path, "expected ASCII");
            return item.GetString();
        }

        /// <summary>
        /// 校验整数标量，保留项目 DATAID/Slot 对 U1/U2/U4/B 的兼容性。
        /// </summary>
        private static uint ReadUnsignedScalar(Item item, string path)
        {
            if (item.Count != 1)
                throw Invalid(path, "expected exactly one integer value");

            return item.Format switch
            {
                SecsFormat.U1 => item.FirstValue<byte>(),
                SecsFormat.U2 => item.FirstValue<ushort>(),
                SecsFormat.U4 => item.FirstValue<uint>(),
                SecsFormat.Binary => item.FirstValue<byte>(),
                _ => throw Invalid(path, "expected U1[1], U2[1], U4[1] or B[1]")
            };
        }

        /// <summary>
        /// 校验 MF 编码及受支持的 0D 值，保留十六进制文本或原始 ASCII 文本。
        /// </summary>
        private static string ReadMaterialFormat(Item item, string path)
        {
            // 先校验编码，再区分合法编码中的不支持值。
            var value = item.Format switch
            {
                SecsFormat.Binary when item.Count == 1 => item.FirstValue<byte>().ToString("X2", CultureInfo.InvariantCulture),
                SecsFormat.ASCII => item.GetString(),
                _ => throw Invalid(path, "expected B[1] or legacy ASCII MF")
            };

            // ASCII 兼容写法只在比较时去除空白、忽略大小写。
            var materialFormat = value.Trim();
            if (!string.Equals(materialFormat, "0D", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(materialFormat, "0x0D", StringComparison.OrdinalIgnoreCase))
                throw Invalid(path, $"unsupported MF '{materialFormat}'; expected 0D (Carrier + Slot)");

            return value;
        }

        /// <summary>
        /// 创建带字段路径的统一结构异常，供调用层生成拒绝应答和诊断日志。
        /// </summary>
        private static InvalidOperationException Invalid(string path, string reason)
        {
            return new InvalidOperationException($"Invalid S16F15 message at {path}: {reason}.");
        }
    }
}
