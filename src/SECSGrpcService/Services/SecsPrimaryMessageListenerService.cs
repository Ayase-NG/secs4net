using Microsoft.Extensions.Options;
using Microsoft.Extensions.Options;
using Secs4Net;
using Secs4Net.Sml;
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
    private readonly SecsGemContext _secsGemContext;
    private readonly ISecsInteractionHistoryStore _interactionHistoryStore;
    private readonly SemaphoreSlim _listenGate = new(1, 1);
    private CancellationToken _hostToken;
    private CancellationTokenSource? _listenCancellation;
    private Task? _listenTask;
    private HsmsConnection? _connector;
    private SecsGem? _secsGem;

    /// <summary>连接状态变化时通知测试界面。</summary>
    public event Action<string>? ConnectionStatusChanged;

    /// <summary>收发 SECS 消息时通知测试界面。</summary>
    public event Action<string>? SmlLogged;

    /// <summary>当前是否有 SECS 监听任务。</summary>
    public bool IsListening => _listenTask is { IsCompleted: false };

    /// <summary>供新打开的窗体读取当前连接状态。</summary>
    public string ConnectionStatus => _connector?.State.ToString() ?? (IsListening ? "Starting" : "Stopped");

    public SecsPrimaryMessageListenerService(
        ILogger<SecsPrimaryMessageListenerService> logger,
        IConfiguration configuration,
        IEnumerable<IPrimaryMessageHandler> handlers,
        SecsGemContext secsGemContext,
        ISecsInteractionHistoryStore interactionHistoryStore)
    {
        _logger = logger;
        _configuration = configuration;
        _handlerRoutes = BuildRoutes(handlers);
        _secsGemContext = secsGemContext;
        _interactionHistoryStore = interactionHistoryStore;
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
        _hostToken = stoppingToken;

        // if 关键分支：未启用自动监听时仍保持后台服务运行，允许窗体稍后启动。
        var section = _configuration.GetSection("SecsListener");
        var enabled = section.GetValue<bool>("Enabled", false);
        if (enabled)
        {
            // 异步调用说明：按文件配置自动启动唯一的 SECS 监听任务。
            await StartListeningAsync(CreateOptions(section)).ConfigureAwait(false);
        }

        try
        {
            // 异步调用说明：保持后台服务存活，以便窗体随时启动或停止 HSMS。
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // if 关键分支：宿主关闭时停止唯一的监听任务。
        }
        finally
        {
            // 异步调用说明：宿主退出前完成监听清理，避免占用端口。
            await StopListeningAsync().ConfigureAwait(false);
        }
    }

    /// <summary>从配置生成 HSMS 参数，供自动启动与窗体初始值共用。</summary>
    public static SecsGemOptions CreateOptions(IConfiguration section)
    {
        return new SecsGemOptions
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
        };
    }

    /// <summary>用当前界面参数启动唯一的 SECS 监听；已启动时拒绝重复绑定。</summary>
    public async Task StartListeningAsync(SecsGemOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        // 异步调用说明：串行化启动/停止，避免多次点击导致端口重复绑定。
        await _listenGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // if 关键分支：现有监听尚未清理时不能创建第二个连接。
            if (IsListening)
            {
                throw new InvalidOperationException("SECS listener is already running. Stop it before changing connection settings.");
            }

            _listenCancellation?.Dispose();
            _listenCancellation = CancellationTokenSource.CreateLinkedTokenSource(_hostToken);
            var token = _listenCancellation.Token;
            _listenTask = Task.Run(() => StartListenAsync(Options.Create(options), token), token);
        }
        finally
        {
            _listenGate.Release();
        }
    }

    /// <summary>取消监听并等待 HSMS 和 SECS/GEM 资源释放，不停止 gRPC 宿主。</summary>
    public async Task StopListeningAsync()
    {
        // 异步调用说明：等待当前启动或停止动作完成后再取消监听。
        await _listenGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // if 关键分支：未启动时直接返回，重复点击停止是安全的。
            if (_listenCancellation is null)
            {
                return;
            }

            _listenCancellation.Cancel();
            if (_listenTask is not null)
            {
                try
                {
                    // 异步调用说明：等待连接接收循环结束并解绑共享 SecsGem。
                    await _listenTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_listenCancellation.IsCancellationRequested)
                {
                    // if 关键分支：尚未运行就取消的任务没有资源需要释放。
                }
            }

            _listenCancellation.Dispose();
            _listenCancellation = null;
            _listenTask = null;
        }
        finally
        {
            _listenGate.Release();
        }
    }

    /// <summary>
    /// 使用指定的 SECS 配置启动监听，负责连接创建、消息接收及退出时的资源释放。
    /// </summary>
    /// <param name="options">HSMS 连接及 SECS/GEM 通讯参数。</param>
    /// <param name="cancellationToken">停止监听时用于取消连接和接收循环的令牌。</param>
    private async Task StartListenAsync(IOptions<SecsGemOptions> options, CancellationToken cancellationToken)
    {
        try
        {
            // if 关键分支：连接与上下文必须由同一后台服务创建，不能在窗体重复创建。
            var adapter = new SecsGemLoggerAdapter(_logger, line => SmlLogged?.Invoke(line));
            _connector = new HsmsConnection(options, adapter);
            _secsGem = new SecsGem(options, _connector, adapter);
            _secsGemContext.Attach(_secsGem);

            // 连接状态变化时向窗口发布状态，不在连接线程操作 WinForms 控件。
            _connector.ConnectionChanged += (_, state) =>
            {
                _logger.LogInformation("SECS connection state changed: {State}", state);
                ConnectionStatusChanged?.Invoke(state.ToString());
            };

            _logger.LogInformation("Starting SECS primary message listener. IsActive={IsActive}, Address={Ip}:{Port}, DeviceId={DeviceId}",
                options.Value.IsActive, options.Value.IpAddress, options.Value.Port, options.Value.DeviceId);

            // 启动 HSMS 连接。
            _connector.Start(cancellationToken);

            // 持续读取对端发送的 PrimaryMessage，并交给分发器处理。
            // 异步调用说明：读取 Host 主消息，直到连接结束或收到停止指令。
            await foreach (var primaryMessage in _secsGem.GetPrimaryMessageAsync(cancellationToken))
            {
                var msg = primaryMessage.PrimaryMessage;
                _logger.LogInformation("收到 PrimaryMessage: S{S}F{F}, ReplyExpected={ReplyExpected}", msg.S, msg.F, msg.ReplyExpected);

                // 入站关键消息先做追溯落库，便于生产问题追踪。
                // 异步调用说明：记录关键入站消息，便于生产问题追溯。
                await SaveInboundInteractionIfKeyMessageAsync(msg, cancellationToken).ConfigureAwait(false);

                try
                {
                    // 异步调用说明：通过已注册的处理器处理当前 SECS 主消息。
                    await DispatchPrimaryMessageAsync(_secsGem, primaryMessage, cancellationToken).ConfigureAwait(false);
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
                // 异步调用说明：停止 HSMS 连接并释放其网络资源。
                await _connector.DisposeAsync().ConfigureAwait(false);
            }

            if (_secsGem is not null)
            {
                _secsGemContext.Detach(_secsGem);
            }

            _secsGem?.Dispose();
            _connector = null;
            _secsGem = null;
            ConnectionStatusChanged?.Invoke("Stopped");
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

        // 设备处于 OffLine 时，仅放行通信相关消息（S1F1/S1F13/S1F15/S1F17）。
        if (msg.S == 1 && msg.F is 1 or 13 or 15 or 17)
        {
            await handler.HandleAsync(secsGem, primaryMessage, cancellationToken);
            return;
        }

        // 其他消息权限由各 Handler/SxFyFunction 负责，并向 Host 返回明确应答。
        await handler.HandleAsync(secsGem, primaryMessage, cancellationToken);
    }

    /// <summary>
    /// 对关键入站消息做最小追溯落库。
    /// </summary>
    private async Task SaveInboundInteractionIfKeyMessageAsync(SecsMessage msg, CancellationToken cancellationToken)
    {
        if (!IsKeyInboundMessage(msg.S, msg.F))
        {
            return;
        }

        try
        {
            await _interactionHistoryStore
                .SaveInteractionAsync($"S{msg.S}F{msg.F}", msg.ToString(), null, DateTime.UtcNow, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist inbound interaction. S{S}F{F}", msg.S, msg.F);
        }
    }

    /// <summary>
    /// 关键入站消息白名单（P0 最小集合）。
    /// </summary>
    private static bool IsKeyInboundMessage(int s, int f)
    {
        return (s, f) is
            (1, 13) or  // 建连请求
            (2, 31) or  // 时间同步
            (2, 41) or  // 远程命令（含 PPSELECT/START）
            (3, 17) or  // LOT+SlotMap
            (14, 9) or  // Create Control Job
            (16, 15) or // Create Process Job
            (2, 33) or  // Define Report
            (2, 35) or  // Link Event Report
            (2, 37) or  // Enable/Disable Event
            (5, 3) or   // Enable/Disable Alarm
            (5, 5) or
            (7, 5) or
            (7, 19) or
            (7, 25);     // List Alarm
    }

    // handlers全进程统一，在service中build时就会将自身的SF注册进SupportedMessages中。
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
        private readonly Action<string> _onSml;

        /// <summary>同时向现有日志与测试窗体提供 SECS 收发内容。</summary>
        public SecsGemLoggerAdapter(ILogger logger, Action<string> onSml)
        {
            _logger = logger;
            _onSml = onSml;
        }

        /// <summary>记录入站消息并向窗体发送 SML 文本。</summary>
        public void MessageIn(SecsMessage msg, int id)
        {
            _logger.LogDebug("<-- [0x{Id:X8}] {Message}", id, msg);
            _onSml($"<-- [0x{id:X8}] {msg.ToSml()}");
        }

        /// <summary>记录出站消息并向窗体发送 SML 文本。</summary>
        public void MessageOut(SecsMessage msg, int id)
        {
            _logger.LogDebug("--> [0x{Id:X8}] {Message}", id, msg);
            _onSml($"--> [0x{id:X8}] {msg.ToSml()}");
        }
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
