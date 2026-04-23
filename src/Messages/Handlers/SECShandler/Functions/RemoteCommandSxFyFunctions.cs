using Secs4Net;
using SECShandler.Interfaces;

namespace SECShandler.Functions
{
    public static class RemoteCommandSxFyFunctions
    {
        public static async Task HandleS2F41Async(
            PrimaryMessageWrapper primaryMessage,
            IStartMeasurementDispatcher startMeasurementDispatcher,
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
            if (!string.Equals(rcmd, "START", StringComparison.OrdinalIgnoreCase))
            {
                await TryReplyS2F42Async(primaryMessage, hcack: 1, cancellationToken, errorParam: "RCMD");
                return;
            }

            var request = BuildStartMeasurementRequest(root[1]);

            try
            {
                await startMeasurementDispatcher.DispatchStartMeasurementAsync(request, cancellationToken);
                await TryReplyS2F42Async(primaryMessage, hcack: 0, cancellationToken);
            }
            catch
            {
                await TryReplyS2F42Async(primaryMessage, hcack: 2, cancellationToken);
                throw;
            }
        }

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
