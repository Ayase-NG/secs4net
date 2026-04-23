using Microsoft.Extensions.Options;
using Secs4Net;
using SECShandler.Interfaces;

namespace SECSGrpcService.Services;

/// <summary>
/// 持续监听 EAP/Host 发送的 SECS PrimaryMessage。
/// 与 gRPC 服务并行运行，不阻塞 gRPC 收发。
/// </summary>
public sealed class SecsPrimaryMessageListenerService : BackgroundService
{
    private readonly ILogger<SecsPrimaryMessageListenerService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IReadOnlyDictionary<(int S, int F), IPrimaryMessageHandler> _handlerRoutes;
    private HsmsConnection? _connector;
    private SecsGem? _secsGem;

    public SecsPrimaryMessageListenerService(
        ILogger<SecsPrimaryMessageListenerService> logger,
        IConfiguration configuration,
        IEnumerable<IPrimaryMessageHandler> handlers)
    {
        _logger = logger;
        _configuration = configuration;
        _handlerRoutes = BuildRoutes(handlers);
    }

    /// <summary>
    /// 后台持续执行入口：
    /// 1) 读取 <c>SecsListener</c> 配置并判断是否启用监听；
    /// 2) 创建 HSMS 连接与 SecsGem 实例；
    /// 3) 启动连接并通过 <c>GetPrimaryMessageAsync</c> 持续接收 PrimaryMessage；
    /// 4) 将收到的消息按 (S,F) 分发到已注册的处理器；
    /// 5) 结束时释放连接与资源。
    /// </summary>
    /// <param name="stoppingToken">宿主停止令牌，用于优雅退出监听循环。</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 读取监听总开关；未启用则直接返回，不启动 SECS 监听。
        var section = _configuration.GetSection("SecsListener");
        var enabled = section.GetValue<bool>("Enabled", false);
        if (!enabled)
        {
            _logger.LogInformation("SecsListener is disabled. Set `SecsListener:Enabled=true` to enable primary message listening.");
            return;
        }

        // 从配置构建 SecsGemOptions（含 Active/Passive 模式、网络参数与超时参数）。
        var options = Options.Create(new SecsGemOptions
        {
            IsActive = section.GetValue<bool>("IsActive", false),
            IpAddress = section.GetValue<string>("IpAddress", "127.0.0.1") ?? "127.0.0.1",
            Port = section.GetValue<int>("Port", 5000),
            DeviceId = (ushort)section.GetValue<int>("DeviceId", 0),
            SocketReceiveBufferSize = section.GetValue<int>("SocketReceiveBufferSize", 8192),
            T3 = section.GetValue<int>("T3", 45000),
            T5 = section.GetValue<int>("T5", 10000),
            T6 = section.GetValue<int>("T6", 5000),
            T7 = section.GetValue<int>("T7", 10000),
            T8 = section.GetValue<int>("T8", 5000)
        });

        // 初始化 HSMS 连接与 SECS/GEM 通讯对象。
        _connector = new HsmsConnection(options, new SecsGemLoggerAdapter(_logger));
        _secsGem = new SecsGem(options, _connector, new SecsGemLoggerAdapter(_logger));

        // 连接状态变化日志。
        _connector.ConnectionChanged += (_, state) =>
        {
            _logger.LogInformation("SECS connection state changed: {State}", state);
            Console.WriteLine($"SECS连接状态: {state}");
        };

        _logger.LogInformation("Starting SECS primary message listener. IsActive={IsActive}, Address={Ip}:{Port}, DeviceId={DeviceId}",
            options.Value.IsActive, options.Value.IpAddress, options.Value.Port, options.Value.DeviceId);
        Console.WriteLine($"SECS持续监听已启动，模式:{(options.Value.IsActive ? "Active" : "Passive")}, 地址:{options.Value.IpAddress}:{options.Value.Port}, DeviceId:{options.Value.DeviceId}");

        try
        {
            // 启动 HSMS 连接。
            _connector.Start(stoppingToken);

            // 持续读取对端发送的 PrimaryMessage，并交给分发器处理。
            await foreach (var primaryMessage in _secsGem.GetPrimaryMessageAsync(stoppingToken))
            {
                var msg = primaryMessage.PrimaryMessage;
                _logger.LogInformation("收到 PrimaryMessage: S{S}F{F}, ReplyExpected={ReplyExpected}", msg.S, msg.F, msg.ReplyExpected);
                Console.WriteLine($"进入SECS持续监听，收到 PrimaryMessage: S{msg.S}F{msg.F}");

                try
                {
                    await DispatchPrimaryMessageAsync(_secsGem, primaryMessage, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理 PrimaryMessage 失败: S{S}F{F}", msg.S, msg.F);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("SECS primary message listener canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SECS primary message listener crashed.");
        }
        finally
        {
            // 确保资源释放，避免后台重启或进程退出时连接泄漏。
            if (_connector is not null)
            {
                await _connector.DisposeAsync();
            }
            _secsGem?.Dispose();
            _connector = null;
            _secsGem = null;
        }
    }

    private async Task DispatchPrimaryMessageAsync(SecsGem secsGem, PrimaryMessageWrapper primaryMessage, CancellationToken cancellationToken)
    {
        var msg = primaryMessage.PrimaryMessage;

        if (!_handlerRoutes.TryGetValue((msg.S, msg.F), out var handler))
        {
            _logger.LogInformation("尚未实现的 SF: S{S}F{F}", msg.S, msg.F);
            return;
        }

        await handler.HandleAsync(secsGem, primaryMessage, cancellationToken);
    }

    private static IReadOnlyDictionary<(int S, int F), IPrimaryMessageHandler> BuildRoutes(IEnumerable<IPrimaryMessageHandler> handlers)
    {
        var routes = new Dictionary<(int S, int F), IPrimaryMessageHandler>();

        foreach (var handler in handlers)
        {
            foreach (var sf in handler.SupportedMessages)
            {
                if (routes.TryGetValue(sf, out var existed))
                {
                    throw new InvalidOperationException($"Duplicate SECS handler route registered for S{sf.S}F{sf.F}: {existed.GetType().Name} and {handler.GetType().Name}.");
                }

                routes[sf] = handler;
            }
        }

        return routes;
    }

    private sealed class SecsGemLoggerAdapter : ISecsGemLogger
    {
        private readonly ILogger _logger;

        public SecsGemLoggerAdapter(ILogger logger)
        {
            _logger = logger;
        }

        public void MessageIn(SecsMessage msg, int id) => _logger.LogDebug("<-- [0x{Id:X8}] {Message}", id, msg);
        public void MessageOut(SecsMessage msg, int id) => _logger.LogDebug("--> [0x{Id:X8}] {Message}", id, msg);
        public void Debug(string msg) => _logger.LogDebug("{Message}", msg);
        public void Info(string msg) => _logger.LogInformation("{Message}", msg);
        public void Warning(string msg) => _logger.LogWarning("{Message}", msg);
        public void Error(string msg) => _logger.LogError("{Message}", msg);
        public void Error(string msg, Exception ex) => _logger.LogError(ex, "{Message}", msg);
        public void Error(string msg, SecsMessage? message, Exception? ex)
        {
            if (ex is null)
            {
                _logger.LogError("{Message} {SecsMessage}", msg, message);
                return;
            }

            _logger.LogError(ex, "{Message} {SecsMessage}", msg, message);
        }
    }
}
