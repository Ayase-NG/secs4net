using CommunityToolkit.HighPerformance.Buffers;
using Microsoft.Extensions.Options;
using PooledAwait;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

namespace Secs4Net;

/// <summary>
/// HSMS（High-Speed SECS Message Services）连接管理器
/// </summary>
/// <remarks>
/// HSMS是SECS-II协议的传输层实现，基于TCP/IP协议栈。
/// 
/// 连接架构：
/// ┌─────────────┐         ┌─────────────┐
/// │   Active    │◄───────►│  Passive    │
/// │   (Client)  │  TCP    │  (Server)   │
/// └─────────────┘         └─────────────┘
/// 
/// 连接状态机：
/// ┌─────────┐   Connect   ┌───────────┐   Select    ┌──────────┐
/// │ Not     │────────────►│ Connected │────────────►│ Selected │
/// │Connected│             │  (T7)     │             │          │
/// └─────────┘             └───────────┘             └──────────┘
///      ▲                      │                       │
///      │                      │                       │
///      │                      │                       ▼
///      │                      │                 ┌──────────┐
///      └──────────────────────┴────────────────►│  Retry   │
///                   Reconnect                   └──────────┘
/// 
/// 超时定时器：
/// - T5: 连接间隔时间（Connect Separation Time）
/// - T6: 控制会话超时（Control Transaction Timeout）
/// - T7: 连接空闲超时（Not Selected Timeout）
/// - T8: 网络超时（Network Intercharacter Timeout）
/// 
/// 数据流：
/// 发送：SecsMessage → EncodeMessage() → SendAsync() → Socket
/// 接收：Socket → ReceiveAsync() → Pipe → PipeDecoder → SecsMessage
/// </remarks>
#if NET
[UnsupportedOSPlatform("browser")]
#endif
public sealed class HsmsConnection : ISecsConnection, IAsyncDisposable
{
    /// <summary>
    /// 连接状态变更事件
    /// </summary>
    public event EventHandler<ConnectionState>? ConnectionChanged;

    /// <summary>
    /// T5: 连接间隔时间（Connect Separation Time）
    /// </summary>
    /// <remarks>Active模式下，两次连接尝试之间的最小间隔</remarks>
    public int T5 { get; }

    /// <summary>
    /// T6: 控制会话超时（Control Transaction Timeout）
    /// </summary>
    /// <remarks>控制消息（如SelectRequest）的回复超时</remarks>
    public int T6 { get; }

    /// <summary>
    /// T7: 连接空闲超时（Not Selected Timeout）
    /// </summary>
    /// <remarks>连接后未完成Select的空闲超时</remarks>
    public int T7 { get; }

    /// <summary>
    /// T8: 网络超时（Network Intercharacter Timeout）
    /// </summary>
    /// <remarks>同一消息内两个字符之间的最大间隔</remarks>
    public int T8 { get; }

    /// <summary>
    /// 心跳检测间隔（毫秒）
    /// </summary>
    public int LinkTestInterval { get; }

    /// <summary>
    /// 是否启用心跳检测
    /// </summary>
    public bool LinkTestEnabled
    {
        get => _linkTestEnable;
        set
        {
            if (_linkTestEnable == value)
            {
                return;
            }

            _linkTestEnable = value;
            if (_linkTestEnable)
            {
                _timerLinkTest.Change(0, LinkTestInterval);
            }
            else
            {
                _timerLinkTest.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
    }
    private bool _linkTestEnable;

    /// <summary>
    /// 当前连接状态
    /// </summary>
    public ConnectionState State { get; private set; }

    /// <summary>
    /// 是否为主动模式（Active/Client）
    /// </summary>
    /// <remarks>
    /// - true: Active模式，主动连接对端
    /// - false: Passive模式，监听并接受连接
    /// </remarks>
    public bool IsActive { get; }

    /// <summary>
    /// 目标IP地址
    /// </summary>
    public IPAddress IpAddress { get; }

    /// <summary>
    /// 端口号
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// 对端设备IP地址
    /// </summary>
    /// <remarks>
    /// Active模式：返回配置的IpAddress
    /// Passive模式：返回已连接对端的IP地址
    /// </remarks>
    public string DeviceIpAddress
        => IsActive
        ? IpAddress.ToString()
        : ((IPEndPoint?)_socket?.RemoteEndPoint)?.Address?.ToString() ?? "NA";

    private Socket? _socket;
    private const int DisposalNotStarted = 0;
    private const int DisposalComplete = 1;
    private int _disposeStage;

    public bool IsDisposed
        => Interlocked.CompareExchange(ref _disposeStage, DisposalComplete, DisposalComplete) == DisposalComplete;

    private readonly Func<CancellationToken, Task> _startImpl;
    private readonly Action _stopImpl;
    private readonly Timer _timer7;
    private readonly Timer _timer8;
    private readonly Timer _timerLinkTest;
    private readonly ConcurrentDictionary<int, ValueTaskCompletionSource<MessageType>> _replyExpectedMsgs = new();
    private readonly int _socketReceiveBufferSize;
#if !NET
    private readonly byte[] _socketReceiveBuffer;
#endif
    private readonly ISecsGemLogger _logger;
    private readonly PipeDecoder _pipeDecoder;
    private readonly Pipe _pipe;
    private readonly SemaphoreSlim _sendLock = new(initialCount: 1);

    private CancellationToken _stoppingToken;
    private CancellationTokenSource? _cancellationTokenSourceForPipeDecoder;
    // 控制启动/连接循环任务生命周期的取消令牌源。
    // 当请求重连时，会先取消之前的循环再启动新的循环，避免因多个重试任务同时存在而导致资源累积和泄漏。
    private CancellationTokenSource? _startLoopCts;
    private readonly CancellationTokenSource _cancellationSourceForControlMessageProcessing = new();

    /// <summary>
    /// 构造HSMS连接
    /// </summary>
    /// <param name="secsGemOptions">SECS/GEM配置选项</param>
    /// <param name="logger">日志记录器</param>
    /// <remarks>
    /// 初始化流程：
    /// 1. 创建Pipe用于流式消息解码
    /// 2. 启动控制消息处理任务
    /// 3. 初始化各定时器（T7、T8、LinkTest）
    /// 4. 根据IsActive设置连接启动/停止实现
    /// 
    /// Active vs Passive模式：
    /// - Active: 创建Socket并尝试连接，失败后等待T5重试
    /// - Passive: 创建ServerSocket监听，接受连接后进入Connected状态
    /// </remarks>
    public HsmsConnection(IOptions<SecsGemOptions> secsGemOptions, ISecsGemLogger logger)
    {
        // 创建Pipe用于消息解码（生产者-消费者模式）
        var pipe = new Pipe(new PipeOptions(useSynchronizationContext: true));
        _pipeDecoder = new PipeDecoder(pipe.Reader, pipe.Writer);
        _pipe = pipe;
        _logger = logger;
        
        // 从配置读取超时参数
        var options = secsGemOptions.Value;
        T5 = options.T5;
        T6 = options.T6;
        T7 = options.T7;
        T8 = options.T8;
        LinkTestInterval = options.LinkTestInterval;
        IpAddress = IPAddress.Parse(options.IpAddress);
        Port = options.Port;
        IsActive = options.IsActive;
        _socketReceiveBufferSize = options.SocketReceiveBufferSize;
#if !NET
        _socketReceiveBuffer = new byte[_socketReceiveBufferSize];
#endif

        // 启动控制消息处理任务
        Task.Run(() => HandleControlMessagesAsync(_cancellationSourceForControlMessageProcessing.Token), _cancellationSourceForControlMessageProcessing.Token);

        // T7定时器：连接空闲超时
        _timer7 = new Timer(delegate
        {
            _logger.Error($"T7 Timeout: {T7 / 1000} sec.");
            CommunicationStateChanging(ConnectionState.Retry);
        }, null, Timeout.Infinite, Timeout.Infinite);

        // T8定时器：网络字符间隔超时
        _timer8 = new Timer(delegate
        {
            _logger.Error($"T8 Timeout: {T8 / 1000} sec.");
            CommunicationStateChanging(ConnectionState.Retry);
        }, null, Timeout.Infinite, Timeout.Infinite);

        // LinkTest定时器：心跳检测
        _timerLinkTest = new Timer(delegate
        {
#if !DISABLE_TIMER
            if (State == ConnectionState.Selected)
            {
                _ = SendLinkTestAsync();
            }

            async FireAndForget SendLinkTestAsync() => await SendControlMessage(MessageType.LinkTestRequest, MessageIdGenerator.NewId()).ConfigureAwait(false);
#endif
        }, null, Timeout.Infinite, Timeout.Infinite);

        // 根据模式设置连接实现
        if (IsActive)
        {
            // Active模式：主动连接
            _startImpl = async cancellation =>
            {
                var connected = false;
                do
                {
                    if (IsDisposed)
                    {
                        return;
                    }

                    CommunicationStateChanging(ConnectionState.Connecting);
                    try
                    {
                        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                        {
                            Blocking = false,
                            ReceiveBufferSize = _socketReceiveBufferSize,
                        };
#if NET
                        await socket.ConnectAsync(IpAddress, Port, cancellation).ConfigureAwait(false);
#else
                        await socket.ConnectAsync(IpAddress, Port).WithCancellation(cancellation).ConfigureAwait(false);
#endif

                        _socket = socket;
                        CommunicationStateChanging(ConnectionState.Connected);
                        connected = true;
                    }
                    catch (Exception ex) when (!IsDisposed)
                    {
                        _logger.Error(ex.Message);
                        _logger.Info($"Start T5 Timer: {T5 / 1000} sec.");
                        await Task.Delay(T5, cancellation).ConfigureAwait(false);
                    }
                } while (!connected);

                // 连接成功后发送SelectRequest建立会话
                await SendControlMessage(MessageType.SelectRequest, MessageIdGenerator.NewId(), cancellation).ConfigureAwait(false);
            };

            _stopImpl = delegate { };
        }
        else
        {
            // Passive模式：被动监听
            var server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                Blocking = false,
            };
            server.Bind(new IPEndPoint(IpAddress, Port));
            server.Listen(0);

            _startImpl = async cancellation =>
            {
                var connected = false;
                do
                {
                    if (IsDisposed)
                    {
                        return;
                    }

                    CommunicationStateChanging(ConnectionState.Connecting);
                    try
                    {
#if NET
                        _socket = await server.AcceptAsync(cancellation).ConfigureAwait(false);
#else
                        _socket = await server.AcceptAsync().WithCancellation(cancellation).ConfigureAwait(false);
#endif
                        _socket.Blocking = false;
                        _socket.ReceiveBufferSize = _socketReceiveBufferSize;
                        CommunicationStateChanging(ConnectionState.Connected);
                        connected = true;
                    }
                    catch (Exception ex) when (!IsDisposed)
                    {
                        _logger.Error(ex.Message);
                        await Task.Delay(2000, cancellation).ConfigureAwait(false);
                    }
                } while (!connected);
            };

            _stopImpl = delegate
            {
                if (IsDisposed)
                {
                    server.Dispose();
                }
            };
        }
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    /// <remarks>
    /// 断开流程：
    /// 1. 停止T7、T8定时器
    /// 2. 停止Pipe解码器
    /// 3. 调用_stopImpl清理资源（如关闭ServerSocket）
    /// 4. 关闭并释放Socket
    /// </remarks>
    private void Disconnect()
    {
        _timer7.Change(Timeout.Infinite, Timeout.Infinite);
        _timer8.Change(Timeout.Infinite, Timeout.Infinite);
        StopPipeDecoder(ref _cancellationTokenSourceForPipeDecoder);
        _stopImpl.Invoke();

        if (_socket is null)
        {
            return;
        }

        if (_socket.Connected)
        {
            _socket.Shutdown(SocketShutdown.Both);
        }

        _socket.Dispose();
        _socket = null;
    }

    /// <summary>
    /// 启动连接
    /// </summary>
    /// <param name="cancellation">取消令牌</param>
    /// <remarks>
    /// Active模式：开始尝试连接对端
    /// Passive模式：开始监听连接请求
    /// </remarks>
    public void Start(CancellationToken cancellation)
    {
        _stoppingToken = cancellation;

        // 取消之前的启动循环，确保只有一个连接循环在运行。
        try
        {
            if (_startLoopCts != null)
            {
                try { _startLoopCts.Cancel(); } catch { }
                try { _startLoopCts.Dispose(); } catch { }
                _startLoopCts = null;
            }

            // 创建一个链接的取消令牌源，这样调用方的取消也会取消内部循环。
            _startLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var token = _startLoopCts.Token;
            Task.Run(() => _startImpl(token), token);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to start connection loop: " + ex.Message);
        }
    }

    /// <summary>
    /// Pipe解码器的消费者：从Pipe读取数据并解码
    /// </summary>
    /// <remarks>
    /// 与StartPipeDecoderProducerAsync配合形成生产者-消费者模式
    /// Producer从Socket读取数据写入Pipe
    /// Consumer从Pipe读取数据并调用PipeDecoder解码
    /// </remarks>
    private async Task StartPipeDecoderConsumerAsync(CancellationToken cancellation)
    {
        try
        {
            await _pipeDecoder.StartAsync(cancellation).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (cancellation.IsCancellationRequested)
            {
                return;
            }
            _logger.Error("Unexpected exception on StartAsyncStreamDecoderAsync", ex);
            Reconnect();
        }
    }


    /// <summary>
    /// Pipe解码器的生产者：从Socket接收数据并写入Pipe
    /// </summary>
    /// <remarks>
    /// 数据流：
    /// Socket ReceiveAsync → Pipe → DecodeLoopAsync
    /// 
    /// 职责：
    /// 1. 持续从Socket读取数据
    /// 2. 将数据写入Pipe的Writer
    /// 3. 当Socket断开时触发重连
    /// 
    /// 这是接收数据的第一道处理，将网络层的字节流转换为Pipeline管道数据流
    /// </remarks>
    private async Task StartPipeDecoderProducerAsync(CancellationToken cancellation)
    {
        var decoderInput = _pipeDecoder.Input;
        try
        {
            while (true)
            {
                Debug.Assert(_socket != null);
#if NET
                // 从Socket接收数据到Pipe的内存中
                var memory = decoderInput.GetMemory(_socketReceiveBufferSize);
                var count = await _socket!.ReceiveAsync(memory, SocketFlags.None, cancellation).ConfigureAwait(false);
                // 告诉Pipe已写入多少数据
                decoderInput.Advance(count);
                // 刷新Pipe，使数据可被消费者读取
                await decoderInput.FlushAsync(cancellation).ConfigureAwait(false);
#else
                var count = await _socket!.ReceiveAsync(new ArraySegment<byte>(_socketReceiveBuffer), SocketFlags.None).WithCancellation(cancellation).ConfigureAwait(false);
                if (count > 0)
                {
                    await decoderInput.WriteAsync(_socketReceiveBuffer.AsMemory()[..count], cancellation).ConfigureAwait(false);
                }
#endif
                // count=0 表示Socket已断开
                if (count == 0)
                {
                    Reconnect();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            if (cancellation.IsCancellationRequested)
            {
                return;
            }
            _logger.Error("Unhandled exception occurred on PipeDecoder producer", ex);
            Reconnect();
        }
    }

    /// <summary>
    /// 停止Pipe解码器
    /// </summary>
    /// <remarks>
    /// 清理流程：
    /// 1. 取消解码任务
    /// 2. 完成Pipe的读写
    /// 3. 重置Pipe状态
    /// </remarks>
    private void StopPipeDecoder(ref CancellationTokenSource? cancellationTokenSource)
    {
        if (cancellationTokenSource is { IsCancellationRequested: false })
        {
            cancellationTokenSource.Cancel();
            cancellationTokenSource.Dispose();
            cancellationTokenSource = null;
            _pipe.Reader.Complete();
            _pipe.Writer.Complete();
            _pipe.Reset();
        }
    }

    /// <summary>
    /// 请求重连
    /// </summary>
    /// <remarks>将状态切换为Retry，触发断开并重新连接</remarks>
    public void Reconnect()
        => CommunicationStateChanging(ConnectionState.Retry);

    /// <summary>
    /// 处理连接状态变更
    /// </summary>
    /// <param name="newState">新的连接状态</param>
    /// <remarks>
    /// 状态变更处理：
    /// - Selected: 停止T7定时器（已成功建立会话）
    /// - Connected: 启动T7定时器，启动Pipe解码器的生产者和消费者
    /// - Retry: 断开连接并重新启动
    /// </remarks>
    private void CommunicationStateChanging(ConnectionState newState)
    {
        State = newState;
        ConnectionChanged?.Invoke(this, State);

        switch (State)
        {
            case ConnectionState.Selected:
#if !DISABLE_TIMER
                _timer7.Change(Timeout.Infinite, Timeout.Infinite);
                _logger.Info("Stop T7 Timer");
#endif
                break;
            case ConnectionState.Connected:
#if !DISABLE_TIMER
                _cancellationTokenSourceForPipeDecoder = new CancellationTokenSource();
                Task.Run(() => StartPipeDecoderConsumerAsync(_cancellationTokenSourceForPipeDecoder.Token));
                Task.Run(() => StartPipeDecoderProducerAsync(_cancellationTokenSourceForPipeDecoder.Token));
                _logger.Info($"Start T7 Timer: {T7 / 1000} sec.");
                _timer7.Change(T7, Timeout.Infinite);
#endif
                break;
            case ConnectionState.Retry:
                if (IsDisposed)
                {
                    return;
                }

                Disconnect();
                Start(_stoppingToken);
                break;
        }
    }

    /// <summary>
    /// 处理控制消息的异步循环
    /// </summary>
    /// <remarks>
    /// 从PipeDecoder获取控制消息，逐个处理。
    /// 控制消息包括：SelectRequest/Response、LinkTestRequest/Response、SeparateRequest
    /// </remarks>
    private async Task HandleControlMessagesAsync(CancellationToken cancellation)
    {
        try
        {
            await foreach (var item in _pipeDecoder.GetControlMessages(cancellation).WithCancellation(cancellation).ConfigureAwait(false))
            {
                await ProcessControlMessageAsync(item, cancellation).ConfigureAwait(continueOnCapturedContext: false);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    /// <summary>
    /// 处理单个控制消息
    /// </summary>
    /// <remarks>
    /// 控制消息类型处理：
    /// - SelectRequest: 发送SelectResponse，切换到Selected状态
    /// - SelectResponse: 根据F值判断结果（F=0成功，其他失败）
    /// - LinkTestRequest: 发送LinkTestResponse
    /// - SeparateRequest: 切换到Retry状态
    /// 
    /// SelectResponse F值含义：
    /// - F=0: 成功，切换到Selected
    /// - F=1: 通信已激活
    /// - F=2: 连接未就绪
    /// - F=3: 连接耗尽
    /// </remarks>
    private async Task ProcessControlMessageAsync(MessageHeader header, CancellationToken cancellation)
    {
        try
        {
            // 检查是否是回复消息（偶数类型需要匹配请求）
            if ((byte)header.MessageType % 2 == 0)
            {
                if (_replyExpectedMsgs.TryGetValue(header.Id, out var ar))
                {
                    ar.TrySetResult(header.MessageType);
                }
                else
                {
                    _logger.Warning("Received Unexpected Control Message: " + header.MessageType);
                    return;
                }
            }

            _logger.Info("Receive Control message: " + header.MessageType);
            switch (header.MessageType)
            {
                case MessageType.SelectRequest:
                    await SendControlMessage(MessageType.SelectResponse, header.Id, cancellation).ConfigureAwait(false);
                    CommunicationStateChanging(ConnectionState.Selected);
                    break;
                case MessageType.SelectResponse:
                    switch (header.F)
                    {
                        case 0:
                            CommunicationStateChanging(ConnectionState.Selected);
                            break;
                        case 1:
                            _logger.Error("Communication Already Active.");
                            break;
                        case 2:
                            _logger.Error("Connection Not Ready.");
                            break;
                        case 3:
                            _logger.Error("Connection Exhaust.");
                            break;
                        default:
                            _logger.Error("Connection Status Is Unknown.");
                            break;
                    }
                    break;
                case MessageType.LinkTestRequest:
                    await SendControlMessage(MessageType.LinkTestResponse, header.Id, cancellation).ConfigureAwait(false);
                    break;
                case MessageType.SeparateRequest:
                    CommunicationStateChanging(ConnectionState.Retry);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Unhandled exception occurred when processing control message: " + header.ToString(), ex);
        }
    }

    // 控制消息固定长度为10字节
    private static readonly ReadOnlyMemory<byte> ControlMessageLengthBytes = new byte[] { 0, 0, 0, 10 };

    /// <summary>
    /// 发送HSMS控制消息
    /// </summary>
    /// <param name="msgType">控制消息类型</param>
    /// <param name="id">消息ID</param>
    /// <param name="cancellation">取消令牌</param>
    /// <remarks>
    /// 控制消息格式：Length(4B) + Header(10B) = 14字节
    /// 
    /// 需要回复的控制消息：
    /// - SelectRequest (1): 等待SelectResponse
    /// - LinkTestRequest (5): 等待LinkTestResponse
    /// 
    /// 不需要回复的：
    /// - SeparateRequest (9)
    /// - SelectResponse/LinkTestResponse (偶数类型)
    /// 
    /// T6超时控制适用于需要回复的消息
    /// </remarks>
    private async Task SendControlMessage(MessageType msgType, int id, CancellationToken cancellation = default)
    {
        var token = ValueTaskCompletionSource<MessageType>.Create();
        // 奇数消息类型且非SeparateRequest需要等待回复
        if ((byte)msgType % 2 == 1 && msgType != MessageType.SeparateRequest)
        {
            _replyExpectedMsgs[id] = token;
        }

        try
        {
            var buffer = EncodeControlMessage(msgType, id);
            // 发送信息核心方法
            await Unsafe.As<ISecsConnection>(this).SendAsync(buffer, cancellation).ConfigureAwait(false);

            _logger.Info("Sent Control Message: " + msgType);
            // 等待回复（T6超时）
            if (_replyExpectedMsgs.ContainsKey(id))
            {
#if NET
                await token.Task.WaitAsync(TimeSpan.FromMilliseconds(T6), cancellation).ConfigureAwait(false);
#else
                if (await Task.WhenAny(token.Task, Task.Delay(T6, cancellation)).ConfigureAwait(false) != token.Task)
                {
                    _logger.Error($"T6 Timeout[id=0x{id:X8}]: {T6 / 1000} sec.");
                    CommunicationStateChanging(ConnectionState.Retry);
                }
#endif
            }
        }
#if NET
        catch (TimeoutException)
        {
            _logger.Error($"T6 Timeout[id=0x{id:X8}]: {T6 / 1000} sec.");
            CommunicationStateChanging(ConnectionState.Retry);
        }
#endif
        catch (Exception ex)
        {
            _logger.Error($"Unknown exception occurred when send control messages", ex);
            CommunicationStateChanging(ConnectionState.Retry);
        }
        finally
        {
            _replyExpectedMsgs.TryRemove(id, out _);
        }

        // 编码控制消息为二进制格式
        static ReadOnlyMemory<byte> EncodeControlMessage(MessageType msgType, int id)
        {
            var buffer = new MemoryBufferWriter<byte>(new byte[14]);
            buffer.Write(ControlMessageLengthBytes.Span);
            new MessageHeader
            {
                DeviceId = 0xFFFF, // 控制消息使用特殊DeviceId
                MessageType = msgType,
                Id = id
            }.EncodeTo(buffer);
            return buffer.WrittenMemory;
        }
    }

    /// <summary>
    /// 停止T8定时器
    /// </summary>
    /// <remarks>用于字符间隔超时检测</remarks>
    private void StopT8Timer()
    {
        _logger.Debug($"Stop T8 Timer: {T8 / 1000} sec.");
        _timer8.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// 启动T8定时器
    /// </summary>
    /// <remarks>开始检测同一消息内字符间隔</remarks>
    private void StartT8Timer()
    {
        _logger.Debug($"Start T8 Timer: {T8 / 1000} sec.");
        _timer8.Change(T8, Timeout.Infinite);
    }

    /// <summary>
    /// 异步释放资源
    /// </summary>
    /// <remarks>
    /// 释放流程：
    /// 1. 如果处于Selected状态，发送SeparateRequest断开会话
    /// 2. 断开Socket连接
    /// 3. 取消并释放所有定时器
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStage, DisposalComplete) != DisposalNotStarted)
        {
            return;
        }

        ConnectionChanged = null;
        // 如果已建立会话，发送SeparateRequest断开
        if (State == ConnectionState.Selected)
        {
            await SendControlMessage(MessageType.SeparateRequest, MessageIdGenerator.NewId()).ConfigureAwait(false);
        }

        Disconnect();
        // 取消正在运行的启动/连接循环
        try
        {
            if (_startLoopCts is { IsCancellationRequested: false })
            {
                _startLoopCts.Cancel();
            }
            _startLoopCts?.Dispose();
            _startLoopCts = null;
        }
        catch { }
        _cancellationSourceForControlMessageProcessing.Cancel();
        _cancellationSourceForControlMessageProcessing.Dispose();
        _timer7.Dispose();
        _timer8.Dispose();
        _timerLinkTest.Dispose();
    }

    /// <summary>
    /// 实现ISecsConnection.SendAsync接口
    /// </summary>
    Task ISecsConnection.SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellation)
        => SendAsync(buffer, cancellation);

    /// <summary>
    /// 通过Socket发送数据的核心方法
    /// </summary>
    /// <param name="buffer">要发送的二进制数据</param>
    /// <param name="cancellation">取消令牌</param>
    /// <remarks>
    /// 数据流终点：这里是将数据写入操作系统的TCP发送缓冲区
    /// 
    /// 发送流程：
    /// 1. 获取发送锁（_sendLock），确保同一时间只有一个发送操作
    /// 2. 循环调用Socket.SendAsync，直到所有数据发送完成
    /// 3. 释放发送锁
    /// 
    /// 注意：Socket.SendAsync可能不会一次性发送所有数据，
    /// 因此需要循环直到buffer.IsEmpty
    /// </remarks>
#if NET
    private async Task SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellation)
    {
        // 1. 获取发送锁，防止并发发送导致消息交错
        await _sendLock.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            // 2. 循环发送直到所有数据发送完成
            do
            {
                Debug.Assert(_socket != null);
                // Socket.SendAsync返回实际发送的字节数
                var length = await _socket.SendAsync(buffer, SocketFlags.None, cancellation).ConfigureAwait(false);
                Debug.WriteLine($"Socket sent {length} bytes.");
                // 更新buffer，跳过已发送的部分
                buffer = buffer[length..];
            } while (!buffer.IsEmpty);
        }
        finally
        {
            // 3. 释放发送锁
            _sendLock.Release();
        }
    }
#else
    private async Task SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellation)
    {
        if (!System.Runtime.InteropServices.MemoryMarshal.TryGetArray(buffer, out var arr))
        {
            throw new InvalidOperationException();
        }
        await _sendLock.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            do
            {
                Debug.Assert(_socket != null);
                var length = await _socket.SendAsync(arr, SocketFlags.None).WithCancellation(cancellation).ConfigureAwait(false);
                arr = new ArraySegment<byte>(arr.Array, arr.Offset + length, arr.Count - length);
                Debug.WriteLine($"Socket sent {length} bytes.");
            } while (arr.Count > 0);
        }
        finally
        {
            _sendLock.Release();
        }
    }
#endif

    IAsyncEnumerable<(MessageHeader header, Item? rootItem)> ISecsConnection.GetDataMessages(CancellationToken cancellation)
        => _pipeDecoder.GetDataMessages(cancellation);
}
