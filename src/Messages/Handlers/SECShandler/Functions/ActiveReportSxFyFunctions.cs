using SECSdata;

namespace SECShandler.Functions
{
    /// <summary>
    /// 设备主动外发上报的 SxFy 打包函数集合。
    /// 说明：当前聚焦 S6F11 的数据模型组装，发送动作由 Dispatcher 负责。
    /// </summary>
    public static class ActiveReportSxFyFunctions
    {
        /// <summary>
        /// 构建 RFID/Mapping 场景的 S6F11 数据。
        /// </summary>
        /// <param name="dataId">消息 DATAID。</param>
        /// <param name="portId">端口号。</param>
        /// <param name="lotId">批次号。</param>
        /// <param name="rfid">RFID。</param>
        /// <param name="slotsText">槽位文本（例如 "1,2,3"）。</param>
        /// <param name="tryGetVid">VID 映射函数；返回 false 时使用 0 兜底。</param>
        /// <returns>可用于发送的 S6F11_data。</returns>
        public static S6F11_data BuildRfidReport(
            byte dataId,
            string? portId,
            string? lotId,
            string? rfid,
            string? slotsText,
            Func<string, (bool Found, ushort Vid)>? tryGetVid = null)
        {
            // 方法关键节点：按既有约定组装 CEID/RPTID（RFID/Mapping）。
            return new S6F11_data
            {
                DATAID = dataId,
                CEID = 1002,
                Reports = new List<S6F11_report_data>
                {
                    new S6F11_report_data
                    {
                        RPTID = 1002,
                        Values = new List<S6F11_parameter_data>
                        {
                            CreateParam("PORTID", portId, tryGetVid),
                            CreateParam("LOTID", lotId, tryGetVid),
                            CreateParam("RFID", rfid, tryGetVid),
                            CreateParam("SLOTSLIST", slotsText, tryGetVid)
                        }
                    }
                }
            };
        }

        /// <summary>
        /// 构建通用事件上报场景的 S6F11 数据。
        /// 由上层传入 CEID/RPTID 与参数键值对。
        /// </summary>
        public static S6F11_data BuildGenericEventReport(
            byte dataId,
            uint ceid,
            uint rptId,
            IEnumerable<KeyValuePair<string, string>>? parameters,
            Func<string, (bool Found, ushort Vid)>? tryGetVid = null)
        {
            var values = new List<S6F11_parameter_data>();
            if (parameters is not null)
            {
                foreach (var kv in parameters)
                {
                    values.Add(CreateParam(kv.Key, kv.Value, tryGetVid));
                }
            }

            return new S6F11_data
            {
                DATAID = dataId,
                CEID = ceid,
                Reports = new List<S6F11_report_data>
                {
                    new S6F11_report_data
                    {
                        RPTID = rptId,
                        Values = values
                    }
                }
            };
        }

        /// <summary>
        /// 构建晶圆结果场景的 S6F11 数据。
        /// </summary>
        /// <param name="dataId">消息 DATAID。</param>
        /// <param name="waferId">晶圆 ID。</param>
        /// <param name="lotId">批次号。</param>
        /// <param name="slotId">槽位号。</param>
        /// <param name="result">检测结果。</param>
        /// <param name="tryGetVid">VID 映射函数；返回 false 时使用 0 兜底。</param>
        /// <returns>可用于发送的 S6F11_data。</returns>
        public static S6F11_data BuildResultReport(
            byte dataId,
            string? waferId,
            string? lotId,
            string? slotId,
            string? result,
            Func<string, (bool Found, ushort Vid)>? tryGetVid = null)
        {
            // 方法关键节点：按既有约定组装 CEID/RPTID（ResultReport）。
            return new S6F11_data
            {
                DATAID = dataId,
                CEID = 1001,
                Reports = new List<S6F11_report_data>
                {
                    new S6F11_report_data
                    {
                        RPTID = 1000,
                        Values = new List<S6F11_parameter_data>
                        {
                            CreateParam("WAFERID", waferId, tryGetVid),
                            CreateParam("LOTID", lotId, tryGetVid),
                            CreateParam("SLOTID", slotId, tryGetVid),
                            CreateParam("RESULT", result, tryGetVid)
                        }
                    }
                }
            };
        }

        /// <summary>
        /// 构建整盒晶圆检测完成场景的 S6F11 数据。
        /// 预设模板：CEID=1008，RPTID=1008（短期最小实现）。
        /// </summary>
        public static S6F11_data BuildLotCompletedReport(
            byte dataId,
            string? portId,
            string? lotId,
            string? status,
            Func<string, (bool Found, ushort Vid)>? tryGetVid = null)
        {
            return new S6F11_data
            {
                DATAID = dataId,
                CEID = 1003,
                Reports = new List<S6F11_report_data>
                {
                    new S6F11_report_data
                    {
                        RPTID = 1008,
                        Values = new List<S6F11_parameter_data>
                        {
                            CreateParam("PORTID", portId, tryGetVid),
                            CreateParam("LOTID", lotId, tryGetVid),
                            CreateParam("STATUS", status, tryGetVid)
                        }
                    }
                }
            };
        }

        /// <summary>
        /// 构建在线状态变更场景的 S6F11 数据。
        /// 预设模板：CEID=1004（OnlineStateChanged），RPTID=1004。
        /// </summary>
        /// <param name="dataId">消息 DATAID。</param>
        /// <param name="fromState">切换前状态。</param>
        /// <param name="toState">切换后状态。</param>
        /// <param name="trigger">触发来源，例如 RequestOnlineStatus。</param>
        /// <param name="tryGetVid">VID 映射函数；返回 false 时使用 0 兜底。</param>
        /// <returns>可用于发送的 S6F11_data。</returns>
        public static S6F11_data BuildOnlineStateChangedReport(
            byte dataId,
            string? fromState,
            string? toState,
            string? trigger,
            Func<string, (bool Found, ushort Vid)>? tryGetVid = null)
        {
            // 方法关键节点：按预设模板组装在线状态变更事件。
            return new S6F11_data
            {
                DATAID = dataId,
                CEID = 1004,    // 在线状态变更事件 CEID
                Reports = new List<S6F11_report_data>
                {
                    new S6F11_report_data
                    {
                        RPTID = 1004,
                        Values = new List<S6F11_parameter_data>
                        {
                            CreateParam("FROM_STATE", fromState, tryGetVid),
                            CreateParam("TO_STATE", toState, tryGetVid),
                            CreateParam("TRIGGER", trigger, tryGetVid)
                        }
                    }
                }
            };
        }

        /// <summary>
        /// 构建单个 S6F11 参数项。使用映射 VID。
        /// </summary>
        private static S6F11_parameter_data CreateParam(
            string cpName,
            object? value,
            Func<string, (bool Found, ushort Vid)>? tryGetVid)
        {
            // if 关键分支：映射函数存在且命中时使用映射 VID。
            if (tryGetVid is not null)
            {
                var mapped = tryGetVid(cpName);
                if (mapped.Found)
                {
                    return new S6F11_parameter_data
                    {
                        VID = mapped.Vid,
                        CPName = cpName,
                        CPVal = value?.ToString() ?? string.Empty
                    };
                }
            }

            // 兜底分支：未命中映射时使用 0，保持与现有行为一致。
            return new S6F11_parameter_data
            {
                VID = 0,
                CPName = cpName,
                CPVal = value?.ToString() ?? string.Empty
            };
        }
    }
}
