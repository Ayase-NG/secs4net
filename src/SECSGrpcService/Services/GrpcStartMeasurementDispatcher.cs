using SECShandler.Interfaces;

namespace SECSGrpcService.Services;

public sealed class GrpcStartMeasurementDispatcher : IStartMeasurementDispatcher
{
    private readonly IConfiguration _configuration;
    private readonly SecsEfemGrpc _secsEfemGrpc;
    private readonly ILogger<GrpcStartMeasurementDispatcher> _logger;

    public GrpcStartMeasurementDispatcher(
        IConfiguration configuration,
        SecsEfemGrpc secsEfemGrpc,
        ILogger<GrpcStartMeasurementDispatcher> logger)
    {
        _configuration = configuration;
        _secsEfemGrpc = secsEfemGrpc;
        _logger = logger;
    }

    public async Task DispatchStartMeasurementAsync(StartMeasurementDispatchRequest request, CancellationToken cancellationToken)
    {
        var targets = _configuration.GetSection("SecsListener:GrpcTargets").Get<string[]>() ?? Array.Empty<string>();
        if (targets.Length == 0)
        {
            throw new InvalidOperationException("未配置 SecsListener:GrpcTargets，无法触发 StartMeasurement。");
        }

        var fallbackName = _configuration.GetValue<string>("SecsListener:StartName") ?? "SECS-S2F41";
        var startMessage = new StartMessage
        {
            Name = string.IsNullOrWhiteSpace(request.Name) ? fallbackName : request.Name,
            LotId = request.LotId ?? string.Empty,
            PPID = request.PPID ?? string.Empty
        };
        if (request.Slots is { Count: > 0 })
        {
            startMessage.SlotsList.AddRange(request.Slots);
        }

        var slots = startMessage.SlotsList.Count > 0 ? string.Join(",", startMessage.SlotsList) : "<empty>";
        _logger.LogInformation(
            "S2F41 START -> StartMeasurement payload. Name={Name}, LotId={LotId}, PPID={PPID}, Slots=[{Slots}]",
            startMessage.Name,
            startMessage.LotId,
            startMessage.PPID,
            slots);
        Console.WriteLine($"S2F41 START 触发 gRPC StartMeasurement，Name:{startMessage.Name}, LotId:{startMessage.LotId}, PPID:{startMessage.PPID}, Slots:[{slots}]");

        await _secsEfemGrpc.SendStartMeasurementToClientsAsync(targets, startMessage, cancellationToken);
    }
}
