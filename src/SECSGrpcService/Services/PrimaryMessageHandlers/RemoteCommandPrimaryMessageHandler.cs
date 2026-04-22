using Secs4Net;

namespace SECSGrpcService.Services.PrimaryMessageHandlers;

public sealed class RemoteCommandPrimaryMessageHandler : IPrimaryMessageHandler
{
    private readonly ILogger<RemoteCommandPrimaryMessageHandler> _logger;
    private readonly IConfiguration _configuration;
    private readonly SecsEfemGrpc _secsEfemGrpc;

    public RemoteCommandPrimaryMessageHandler(
        ILogger<RemoteCommandPrimaryMessageHandler> logger,
        IConfiguration configuration,
        SecsEfemGrpc secsEfemGrpc)
    {
        _logger = logger;
        _configuration = configuration;
        _secsEfemGrpc = secsEfemGrpc;
    }

    public bool CanHandle(int s, int f) => (s, f) == (2, 41);

    public async Task HandleAsync(PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken)
    {
        _logger.LogInformation("进入S2F41分发处理。");
        Console.WriteLine("进入S2F41分发处理");

        var msg = primaryMessage.PrimaryMessage;
        var root = msg.SecsItem;
        if (root is null || root.Format != SecsFormat.List || root.Count < 2)
        {
            _logger.LogWarning("S2F41格式不合法，跳过处理。");
            await TryReplyS2F42Async(primaryMessage, hcack: 2, cancellationToken);
            return;
        }

        var rcmdItem = root[0];
        if (rcmdItem is null || rcmdItem.Format != SecsFormat.ASCII)
        {
            _logger.LogWarning("S2F41未携带合法RCMD，跳过处理。");
            await TryReplyS2F42Async(primaryMessage, hcack: 2, cancellationToken);
            return;
        }

        var rcmd = (rcmdItem.GetString() ?? string.Empty).Trim();
        _logger.LogInformation("S2F41 RCMD={RCMD}", rcmd);

        if (!string.Equals(rcmd, "START", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("S2F41 RCMD={RCMD}，当前仅处理 START。", rcmd);
            await TryReplyS2F42Async(primaryMessage, hcack: 1, cancellationToken, errorParam: "RCMD");
            return;
        }

        var startMessage = BuildStartMessageFromS2F41(root[1]);
        var targets = _configuration.GetSection("SecsListener:GrpcTargets").Get<string[]>() ?? Array.Empty<string>();
        if (targets.Length == 0)
        {
            _logger.LogWarning("未配置 SecsListener:GrpcTargets，无法触发 StartMeasurement。");
            await TryReplyS2F42Async(primaryMessage, hcack: 2, cancellationToken);
            return;
        }

        try
        {
            Console.WriteLine($"S2F41 START 触发 gRPC StartMeasurement，LotId:{startMessage.LotId}, PPID:{startMessage.PPID}");
            await _secsEfemGrpc.SendStartMeasurementToClientsAsync(targets, startMessage, cancellationToken);
            await TryReplyS2F42Async(primaryMessage, hcack: 0, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理S2F41 START并触发gRPC失败。");
            await TryReplyS2F42Async(primaryMessage, hcack: 2, cancellationToken);
        }
    }

    private static async Task TryReplyS2F42Async(PrimaryMessageWrapper primaryMessage, byte hcack, CancellationToken cancellationToken, string? errorParam = null)
    {
        if (!primaryMessage.PrimaryMessage.ReplyExpected)
        {
            return;
        }

        var paramAcks = string.IsNullOrWhiteSpace(errorParam)
            ? Item.L()
            : Item.L(
                Item.L(
                    Item.A(errorParam),
                    Item.B((byte)1)));

        var s2f42 = new SecsMessage(2, 42, replyExpected: false)
        {
            Name = "RemoteCommandAcknowledge",
            SecsItem = Item.L(
                Item.B(hcack),
                paramAcks)
        };

        await primaryMessage.TryReplyAsync(s2f42, cancellationToken);
    }

    private StartMessage BuildStartMessageFromS2F41(Item paramsContainer)
    {
        var startName = _configuration.GetValue<string>("SecsListener:StartName") ?? "SECS-S2F41";
        var startMessage = new StartMessage
        {
            Name = startName
        };

        if (paramsContainer.Format != SecsFormat.List)
        {
            return startMessage;
        }

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

            if (string.Equals(paramName, "LOTID", StringComparison.OrdinalIgnoreCase))
            {
                startMessage.LotId = TryReadAsString(valueItem) ?? startMessage.LotId;
                continue;
            }

            if (string.Equals(paramName, "PPID", StringComparison.OrdinalIgnoreCase))
            {
                startMessage.PPID = TryReadAsString(valueItem) ?? startMessage.PPID;
                continue;
            }

            if (string.Equals(paramName, "SLOTSLIST", StringComparison.OrdinalIgnoreCase)
                || string.Equals(paramName, "SLOTS", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var slot in ReadSlots(valueItem))
                {
                    startMessage.SlotsList.Add(slot);
                }
            }
        }

        return startMessage;
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
