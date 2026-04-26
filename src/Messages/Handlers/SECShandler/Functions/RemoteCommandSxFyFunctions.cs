using Secs4Net;
using SECShandler.Interfaces;

namespace SECShandler.Functions
{
    /// <summary>
    /// 远程命令相关的 SxFy 处理函数集合。
    /// 当前包含 S2F41（Remote Command）解析与 S2F42 应答逻辑。
    /// </summary>
    public static class RemoteCommandSxFyFunctions
    {
        /// <summary>
        /// 处理 S2F41 Remote Command。
        /// 支持 RCMD：START、STOP、PAUSE、RESUME、PPSELECT。
        /// </summary>
        public static async Task HandleS2F41Async(
            PrimaryMessageWrapper primaryMessage,
            IMeasurementDispatcher measurementDispatcher,
            CancellationToken cancellationToken)
        {
            var msg = primaryMessage.PrimaryMessage;
            var root = msg.SecsItem;
            if (root is null || root.Format != SecsFormat.List || root.Count < 2)
            {
                await TryReplyS2F42Async(primaryMessage, hcack: 2, cancellationToken);
                return;
            }

            var rcmdItem = root[0];
            if (rcmdItem is null || rcmdItem.Format != SecsFormat.ASCII)
            {
                await TryReplyS2F42Async(primaryMessage, hcack: 2, cancellationToken);
                return;
            }

            var rcmd = (rcmdItem.GetString() ?? string.Empty).Trim();
            var paramsContainer = root[1];

            try
            {
                switch (rcmd.ToUpperInvariant())
                {
                    case "START":
                        {
                            var request = BuildStartMeasurementRequest(paramsContainer);
                            await measurementDispatcher.DispatchStartMeasurementAsync(request, cancellationToken);
                            break;
                        }
                    case "STOP":
                        {
                            var request = BuildWaferDispatchRequest(paramsContainer);
                            await measurementDispatcher.DispatchStopMeasurementAsync(request, cancellationToken);
                            break;
                        }
                    case "PAUSE":
                        {
                            var request = BuildWaferDispatchRequest(paramsContainer);
                            await measurementDispatcher.DispatchPauseMeasurementAsync(request, cancellationToken);
                            break;
                        }
                    case "RESUME":
                        {
                            var request = BuildWaferDispatchRequest(paramsContainer);
                            await measurementDispatcher.DispatchResumeMeasurementAsync(request, cancellationToken);
                            break;
                        }
                    case "PPSELECT":
                        {
                            var request = BuildRecipeDispatchRequest(paramsContainer);
                            await measurementDispatcher.DispatchProcessProgramSelectAsync(request, cancellationToken);
                            break;
                        }
                    default:
                        await TryReplyS2F42Async(primaryMessage, hcack: 1, cancellationToken, errorParam: "RCMD");
                        return;
                }

                await TryReplyS2F42Async(primaryMessage, hcack: 0, cancellationToken);
            }
            catch
            {
                await TryReplyS2F42Async(primaryMessage, hcack: 2, cancellationToken);
                throw;
            }
        }

        /// <summary>
        /// 回复 S2F42（Remote Command Acknowledge）。
        /// HCACK 约定：0=成功，1=命令不支持/参数问题，2=执行失败。
        /// </summary>
        private static async Task TryReplyS2F42Async(PrimaryMessageWrapper primaryMessage, byte hcack, CancellationToken cancellationToken, string? errorParam = null)
        {
            if (!primaryMessage.PrimaryMessage.ReplyExpected)
                return;

            var paramAcks = string.IsNullOrWhiteSpace(errorParam)
                ? Item.L()
                : Item.L(Item.L(Item.A(errorParam), Item.B((byte)1)));

            var s2f42 = new SecsMessage(2, 42, replyExpected: false)
            {
                Name = "RemoteCommandAcknowledge",
                SecsItem = Item.L(Item.B(hcack), paramAcks)
            };

            await primaryMessage.TryReplyAsync(s2f42, cancellationToken);
        }

        /// <summary>
        /// 从 S2F41 参数列表构建 StartMeasurement 分发请求。
        /// 支持参数名：NAME、LOTID、PPID、SLOTSLIST/SLOTS。
        /// </summary>
        private static StartMeasurementDispatchRequest BuildStartMeasurementRequest(Item paramsContainer)
        {
            var request = new StartMeasurementDispatchRequest();

            if (paramsContainer.Format != SecsFormat.List)
                return request;

            foreach (var paramItem in paramsContainer.Items)
            {
                if (paramItem.Format != SecsFormat.List || paramItem.Count < 2)
                    continue;

                var nameItem = paramItem[0];
                var valueItem = paramItem[1];
                if (nameItem is null || nameItem.Format != SecsFormat.ASCII)
                    continue;

                var paramName = (nameItem.GetString() ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(paramName))
                    continue;

                if (string.Equals(paramName, "NAME", StringComparison.OrdinalIgnoreCase))
                {
                    request.Name = TryReadAsString(valueItem) ?? request.Name;
                    continue;
                }

                if (string.Equals(paramName, "LOTID", StringComparison.OrdinalIgnoreCase))
                {
                    request.LotId = TryReadAsString(valueItem) ?? request.LotId;
                    continue;
                }

                if (string.Equals(paramName, "PPID", StringComparison.OrdinalIgnoreCase))
                {
                    request.PPID = TryReadAsString(valueItem) ?? request.PPID;
                    continue;
                }

                if (string.Equals(paramName, "SLOTSLIST", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(paramName, "SLOTS", StringComparison.OrdinalIgnoreCase))
                {
                    request.Slots.AddRange(ReadSlots(valueItem));
                }
            }

            return request;
        }

        /// <summary>
        /// 从 S2F41 参数列表构建 Wafer 相关分发请求（STOP/PAUSE/RESUME）。
        /// 支持参数名：SLOTID、WAFERID、LOTID、STATUS、PPID、RESULT。
        /// </summary>
        private static WaferDispatchRequest BuildWaferDispatchRequest(Item paramsContainer)
        {
            var request = new WaferDispatchRequest();
            if (paramsContainer.Format != SecsFormat.List)
                return request;

            foreach (var paramItem in paramsContainer.Items)
            {
                if (paramItem.Format != SecsFormat.List || paramItem.Count < 2)
                    continue;

                var nameItem = paramItem[0];
                var valueItem = paramItem[1];
                if (nameItem is null || nameItem.Format != SecsFormat.ASCII)
                    continue;

                var paramName = (nameItem.GetString() ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(paramName))
                    continue;

                var strValue = TryReadAsString(valueItem) ?? string.Empty;
                if (string.Equals(paramName, "SLOTID", StringComparison.OrdinalIgnoreCase))
                {
                    request.SlotId = strValue;
                    continue;
                }
                if (string.Equals(paramName, "WAFERID", StringComparison.OrdinalIgnoreCase))
                {
                    request.WaferId = strValue;
                    continue;
                }
                if (string.Equals(paramName, "LOTID", StringComparison.OrdinalIgnoreCase))
                {
                    request.LotId = strValue;
                    continue;
                }
                if (string.Equals(paramName, "STATUS", StringComparison.OrdinalIgnoreCase))
                {
                    request.Status = strValue;
                    continue;
                }
                if (string.Equals(paramName, "PPID", StringComparison.OrdinalIgnoreCase))
                {
                    request.PPID = strValue;
                    continue;
                }
                if (string.Equals(paramName, "RESULT", StringComparison.OrdinalIgnoreCase))
                {
                    request.Result = strValue;
                    continue;
                }
            }

            return request;
        }

        /// <summary>
        /// 从 S2F41 参数列表构建 Recipe 分发请求（PPSELECT）。
        /// 支持参数名：PPNAME、PPID、WAFERID、LOTID。
        /// </summary>
        private static RecipeDispatchRequest BuildRecipeDispatchRequest(Item paramsContainer)
        {
            var request = new RecipeDispatchRequest();
            if (paramsContainer.Format != SecsFormat.List)
                return request;

            foreach (var paramItem in paramsContainer.Items)
            {
                if (paramItem.Format != SecsFormat.List || paramItem.Count < 2)
                    continue;

                var nameItem = paramItem[0];
                var valueItem = paramItem[1];
                if (nameItem is null || nameItem.Format != SecsFormat.ASCII)
                    continue;

                var paramName = (nameItem.GetString() ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(paramName))
                    continue;

                var strValue = TryReadAsString(valueItem) ?? string.Empty;
                if (string.Equals(paramName, "PPNAME", StringComparison.OrdinalIgnoreCase))
                {
                    request.PPName = strValue;
                    continue;
                }
                if (string.Equals(paramName, "PPID", StringComparison.OrdinalIgnoreCase))
                {
                    request.PPID = strValue;
                    continue;
                }
                if (string.Equals(paramName, "WAFERID", StringComparison.OrdinalIgnoreCase))
                {
                    request.WaferId = strValue;
                    continue;
                }
                if (string.Equals(paramName, "LOTID", StringComparison.OrdinalIgnoreCase))
                {
                    request.LotId = strValue;
                    continue;
                }
            }

            return request;
        }

        /// <summary>
        /// 尝试将 Item 转换为字符串（支持 ASCII 与常见整型）。
        /// </summary>
        private static string? TryReadAsString(Item item)
        {
            if (item is null) return null;

            if (item.Format == SecsFormat.ASCII)
                return item.GetString();

            return item.Format switch
            {
                SecsFormat.U1 => item.FirstValueOrDefault<byte>(0).ToString(),
                SecsFormat.U2 => item.FirstValueOrDefault<ushort>(0).ToString(),
                SecsFormat.U4 => item.FirstValueOrDefault<uint>(0).ToString(),
                SecsFormat.I1 => item.FirstValueOrDefault<sbyte>(0).ToString(),
                SecsFormat.I2 => item.FirstValueOrDefault<short>(0).ToString(),
                SecsFormat.I4 => item.FirstValueOrDefault<int>(0).ToString(),
                _ => null
            };
        }

        /// <summary>
        /// 解析槽位列表，支持：
        /// 1) List 数字集合；
        /// 2) ASCII 逗号分隔字符串；
        /// 3) 单个数值。
        /// </summary>
        private static IEnumerable<uint> ReadSlots(Item item)
        {
            if (item is null)
                yield break;

            if (item.Format == SecsFormat.List)
            {
                foreach (var child in item.Items)
                {
                    if (TryReadUInt(child, out var slot))
                        yield return slot;
                }
                yield break;
            }

            if (item.Format == SecsFormat.ASCII)
            {
                var raw = item.GetString() ?? string.Empty;
                foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (uint.TryParse(token, out var slot))
                        yield return slot;
                }
                yield break;
            }

            if (TryReadUInt(item, out var singleSlot))
                yield return singleSlot;
        }

        /// <summary>
        /// 将常见整型 Item 转换为 uint。
        /// 对有符号负数返回 false。
        /// </summary>
        private static bool TryReadUInt(Item item, out uint value)
        {
            value = 0;

            switch (item.Format)
            {
                case SecsFormat.U1:
                    value = item.FirstValueOrDefault<byte>(0);
                    return true;
                case SecsFormat.U2:
                    value = item.FirstValueOrDefault<ushort>(0);
                    return true;
                case SecsFormat.U4:
                    value = item.FirstValueOrDefault<uint>(0);
                    return true;
                case SecsFormat.I1:
                    {
                        var v = item.FirstValueOrDefault<sbyte>(0);
                        if (v < 0) return false;
                        value = (uint)v;
                        return true;
                    }
                case SecsFormat.I2:
                    {
                        var v = item.FirstValueOrDefault<short>(0);
                        if (v < 0) return false;
                        value = (uint)v;
                        return true;
                    }
                case SecsFormat.I4:
                    {
                        var v = item.FirstValueOrDefault<int>(0);
                        if (v < 0) return false;
                        value = (uint)v;
                        return true;
                    }
                default:
                    return false;
            }
        }
    }
}
