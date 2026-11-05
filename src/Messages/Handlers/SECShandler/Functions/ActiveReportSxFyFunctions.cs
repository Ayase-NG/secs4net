using Secs4Net;
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
        /// 构建通用事件上报场景的 S6F11 数据。
        /// 由上层传入 CEID/RPTID 与参数键值对。
        /// </summary>
        public static S6F11_data BuildGenericEventReport(
            byte dataId,
            uint ceid,
            uint rptId,
            IEnumerable<KeyValuePair<string, string>>? parameters,
            Func<string, (bool Found, ushort Vid)>? tryGetVid = null,
            Func<ushort, Item?>? tryGetTypedValue = null)
        {
            var values = new List<S6F11_parameter_data>();
            if (parameters is not null)
            {
                foreach (var kv in parameters)
                {
                    var parameter = CreateParam(kv.Key, kv.Value, tryGetVid);
                    // if 关键分支：按 VID 透传类型化值，缺失时沿用原 ASCII 文本。
                    var typedValue = tryGetTypedValue?.Invoke(parameter.VID);
                    if (typedValue is not null)
                    {
                        parameter.ValueItem = typedValue;
                    }

                    values.Add(parameter);
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
        /// 构建单个 S6F11 参数项。使用映射 VID。
        /// </summary>
        private static S6F11_parameter_data CreateParam(
            string cpName,
            object? value,
            Func<string, (bool Found, ushort Vid)>? tryGetVid)
        {
            // 先统一参数键名，确保 CARRIERID/SLOTSLIST/PORTID 的兼容映射一致。
            var normalizedName = NormalizeCpName(cpName);

            // 映射函数存在且命中时使用映射 VID。
            if (tryGetVid is not null)
            {
                var mapped = tryGetVid(normalizedName);
                if (mapped.Found)
                {
                    return new S6F11_parameter_data
                    {
                        VID = mapped.Vid,
                        CPName = normalizedName,
                        CPVal = value?.ToString() ?? string.Empty
                    };
                }
            }

            // 兜底分支：未命中映射时使用 0，保持与现有行为一致。
            return new S6F11_parameter_data
            {
                VID = 0,
                CPName = normalizedName,
                CPVal = value?.ToString() ?? string.Empty
            };
        }

        /// <summary>
        /// 统一参数键名到标准业务名。
        /// 作用：把同义字段别名归一化，减少 VID 映射失败概率。
        /// </summary>
        private static string NormalizeCpName(string cpName)
        {
            if (string.IsNullOrWhiteSpace(cpName))
            {
                return string.Empty;
            }

            var key = cpName.Trim();
            return key.ToUpperInvariant() switch
            {
                "RFID" => "CARRIERID",
                "SLOTS" => "SLOTSLIST",
                "PORT" => "PORTID",
                "LOADPORT" => "PORTID",
                _ => key
            };
        }
    }
}
