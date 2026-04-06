using CommunityToolkit.HighPerformance.Buffers;
using Microsoft.Extensions.Options;
using PooledAwait;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Secs4Net;

public interface ISecsGem
{
    ushort DeviceId { get; }

    /// <summary>
    /// Get primary messages from async-stream
    /// </summary>
    /// <param name="cancellation"></param>
    IAsyncEnumerable<PrimaryMessageWrapper> GetPrimaryMessageAsync(CancellationToken cancellation = default);

    /// <summary>
    /// Send a message to device asynchronously and get reply message.
    /// </summary>
    /// <param name="message">primary message</param>
    /// <returns>Secondary message, or null if <paramref name="message" />'s <see cref="SecsMessage.ReplyExpected"/> is <see langword="false" /> </returns>
    Task<SecsMessage> SendAsync(SecsMessage message, CancellationToken cancellation = default);
}

public sealed class SecsGem : ISecsGem, IDisposable
{
    private const int DisposalNotStarted = 0;
    private const int DisposalComplete = 1;
    private int _disposeStage;
    private readonly ISecsGemLogger _logger;
    private readonly ISecsConnection _hsmsConnector;

    public ushort DeviceId { get; }
    public int T3 { get; }

    private readonly Channel<PrimaryMessageWrapper> _primaryMessageChannel = Channel
        .CreateUnbounded<PrimaryMessageWrapper>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });

    private readonly ConcurrentDictionary<int, (string? messageName, ValueTaskCompletionSource<SecsMessage> completeSource)> _replyExpectedMessages = new();
    private readonly CancellationTokenSource _cancellationSourceForDataMessageProcessing = new();
    private int _recentlyMaxEncodedByteLength;

    public SecsGem(IOptions<SecsGemOptions> secsGemOptions, ISecsConnection hsmsConnector, ISecsGemLogger logger)
    {
        var options = secsGemOptions.Value;
        DeviceId = options.DeviceId;
        T3 = options.T3;
        _recentlyMaxEncodedByteLength = options.EncodeBufferInitialSize;

        _hsmsConnector = hsmsConnector;
        _logger = logger;

        Task.Run(async () =>
        {
            var cancellationToken = _cancellationSourceForDataMessageProcessing.Token;
            await foreach (var (header, rootItem) in _hsmsConnector.GetDataMessages(cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                await ProcessDataMessageAsync(header, rootItem, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            }
        });
    }

    /// <summary>
    /// 发送SECS数据消息的核心方法
    /// </summary>
    /// <param name="message">要发送的SECS消息</param>
    /// <param name="id">消息ID（System Bytes），用于匹配回复消息</param>
    /// <param name="cancellation">取消令牌</param>
    /// <returns>如果需要回复，返回回复消息；否则返回null</returns>
    /// <remarks>
    /// 数据流：
    /// 1. 检查连接状态是否为Selected（已建立会话）
    /// 2. 如果需要回复，将messageId和CompletionSource存入字典，用于后续匹配回复
    /// 3. 使用ArrayPoolBufferWriter分配缓冲区
    /// 4. 调用EncodeMessage()将SecsMessage编码为二进制格式
    /// 5. 调用_hsmsConnector.SendAsync()通过Socket发送
    /// 6. 记录日志
    /// 7. 如果需要回复，等待T3超时时间内收到回复
    /// </remarks>
    internal async Task<SecsMessage> SendDataMessageAsync(SecsMessage message, int id, CancellationToken cancellation)
    {
        // 1. 检查连接状态是否为Selected
        if (_hsmsConnector.State != ConnectionState.Selected)
        {
            throw new SecsException("Device is not selected");
        }

        // 2. 创建CompletionSource用于异步等待回复
        var token = ValueTaskCompletionSource<SecsMessage>.Create();
        // 如果消息需要回复，将(id -> (messageName, CompletionSource))存入字典
        // 当收到对应id的回复时，会从此字典中取出并完成等待
        if (message.ReplyExpected)
        {
            _replyExpectedMessages[id] = (message.Name, token);
        }

        try
        {
            // 3. 使用ArrayPoolBufferWriter分配缓冲区，避免频繁GC
            using (var buffer = new ArrayPoolBufferWriter<byte>(initialCapacity: _recentlyMaxEncodedByteLength))
            {
                // 4. 编码消息：Length(4字节) + Header(10字节) + Item数据
                EncodeMessage(message, id, DeviceId, buffer);
                // 5. 通过HSMS连接发送二进制数据
                await _hsmsConnector.SendAsync(buffer.WrittenMemory, cancellation).ConfigureAwait(false);

                // 动态调整缓冲区大小以优化性能
                if (buffer.WrittenCount > _recentlyMaxEncodedByteLength)
                {
                    _recentlyMaxEncodedByteLength = buffer.WrittenCount;
                }
            }

            // 6. 记录发送日志
            _logger.MessageOut(message, id);

            // 如果不需要回复，直接返回
            if (!message.ReplyExpected)
            {
                return null!;
            }

            // 7. 等待回复，设置T3超时
#if NET
            return await token.Task.WaitAsync(TimeSpan.FromMilliseconds(T3), cancellation).ConfigureAwait(false);
#else
            if (await Task.WhenAny(token.Task, Task.Delay(T3, cancellation)).ConfigureAwait(false) != token.Task)
            {
                throw new SecsException(message, Resources.T3Timeout);
            }
            return token.Task.Result;
#endif
        }
        catch (SocketException)
        {
            // Socket异常时尝试重连
            _hsmsConnector.Reconnect();
            throw;
        }
#if NET
        catch (TimeoutException)
        {
            // T3超时：等待回复超时
            _logger.Error($"T3 Timeout[id=0x{id:X8}]: {T3 / 1000} sec.");
            throw new SecsException(message, Resources.T3Timeout);
        }
#endif
        finally
        {
            // 清理：无论成功还是失败，都从字典中移除
            _replyExpectedMessages.TryRemove(id, out _);
        }
    }

    public Task<SecsMessage> SendAsync(SecsMessage message, CancellationToken cancellation = default)
        => SendDataMessageAsync(message, MessageIdGenerator.NewId(), cancellation);

    public IAsyncEnumerable<PrimaryMessageWrapper> GetPrimaryMessageAsync(CancellationToken cancellation = default)
        => _primaryMessageChannel.Reader.ReadAllAsync(cancellation);

    /// <summary>
    /// 处理接收到的SECS数据消息
    /// </summary>
    /// <param name="header">消息头（包含S/F、设备ID、消息ID等）</param>
    /// <param name="rootItem">根Item（数据体）</param>
    /// <param name="cancellation">取消令牌</param>
    /// <remarks>
    /// 数据流：
    /// 1. 构造SecsMessage对象
    /// 2. 检查DeviceId是否匹配（S9F1除外）
    /// 3. 根据F的奇偶性判断消息类型：
    ///    - 奇数F（如S1F1）：主消息（Primary Message）
    ///    - 偶数F（如S1F2）：次消息（Secondary Message/回复消息）
    /// 4. 主消息写入_primaryMessageChannel供应用层消费
    /// 5. 次消息查找对应的等待者并完成其Task
    /// </remarks>
    private async Task ProcessDataMessageAsync(MessageHeader header, Item? rootItem, CancellationToken cancellation)
    {
        // 1. 构造SecsMessage对象
        var msg = new SecsMessage(header.S, header.F, header.ReplyExpected)
        {
            SecsItem = rootItem,
        };

        try
        {
            // 2. 检查DeviceId是否匹配（S9F1除外）
            if (header.DeviceId != DeviceId && header.S != 9 && header.F != 1)
            {
                _logger.MessageIn(msg, header.Id);
                _logger.Warning("Received Unrecognized Device Id Message");
                // 发送S9F1错误消息
                var headerBytes = new byte[10];
                header.EncodeTo(new MemoryBufferWriter<byte>(headerBytes));
                var s9f1 = new SecsMessage(9, 1, replyExpected: false)
                {
                    Name = "Unrecognized Device Id",
                    SecsItem = Item.B(headerBytes),
                };
                await SendDataMessageAsync(s9f1, MessageIdGenerator.NewId(), cancellation).ConfigureAwait(false);
                return;
            }

            var id = header.Id;
            // 3. 根据F的奇偶性判断消息类型
            if (header.F % 2 != 0)
            {
                // 奇数F：主消息
                if (header.S != 9)
                {
                    // 非S9Fy错误消息，写入主消息通道供应用层处理
                    _logger.MessageIn(msg, header.Id);
                    await _primaryMessageChannel.Writer.WriteAsync(new PrimaryMessageWrapper(this, msg, id), cancellation).ConfigureAwait(false);
                    return;
                }
                // S9Fy错误消息，尝试从数据中提取原始消息ID
                if (rootItem is { Format: not (SecsFormat.List or SecsFormat.ASCII or SecsFormat.JIS8) } dataItem
                    && dataItem.GetMemory<byte>() is { Length: >= 10 } headerBytes)
                {
                    id = BinaryPrimitives.ReadInt32BigEndian(headerBytes.Span.Slice(6, 4));
                }
                else
                {
                    _logger.Warning("Received S9Fy message without primary message's header bytes.");
                }
            }

            // 4. 偶数F：次消息（回复消息）
            _logger.MessageIn(msg, id);
            // 查找是否有等待此消息ID的发送者
            if (_replyExpectedMessages.TryGetValue(id, out var token))
            {
                msg.Name = token.messageName;
                // 完成等待者的Task，使其返回回复消息
                HandleReplyMessage(token.completeSource, msg);
            }
            else
            {
                _logger.Warning($"Received unexpected secondary message[0x{id:X8}]. Maybe T3 timeout.");
            }
        }
        catch (Exception ex)
        {
            if (cancellation.IsCancellationRequested)
            {
                return;
            }
            _logger.Error("Unhandled exception occurred when processing data message", msg, ex);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStage, DisposalComplete) != DisposalNotStarted)
        {
            throw new ObjectDisposedException(nameof(SecsGem));
        }

        _cancellationSourceForDataMessageProcessing.Cancel();
        _cancellationSourceForDataMessageProcessing.Dispose();
        _replyExpectedMessages.Clear();
    }

    /// <summary>
    /// 将SecsMessage编码为SECS-II二进制格式
    /// </summary>
    /// <param name="msg">要编码的SECS消息</param>
    /// <param name="id">消息ID（System Bytes）</param>
    /// <param name="deviceId">设备ID</param>
    /// <param name="buffer">输出缓冲区</param>
    /// <remarks>
    /// SECS-II二进制格式结构：
    /// ┌────────────────────────────────────────────────────────┐
    /// │ Length (4字节, Big Endian) │ Header (10字节) │ Data  │
    /// └────────────────────────────────────────────────────────┘
    /// 
    /// 编码步骤：
    /// 1. 预留4字节空间用于后续写入总长度
    /// 2. 调用MessageHeader.EncodeTo()编码10字节消息头
    /// 3. 调用Item.EncodeTo()递归编码数据体（如果存在）
    /// 4. 回填总长度（实际数据长度 = WrittenCount - 4）
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NET 
    public static void EncodeMessage(SecsMessage msg, int id, ushort deviceId, ArrayPoolBufferWriter<byte> buffer)
#else
    public static unsafe void EncodeMessage(SecsMessage msg, int id, ushort deviceId, ArrayPoolBufferWriter<byte> buffer)
#endif
    {
        // 预留至少14字节空间（4字节长度 + 10字节头）
        buffer.GetSpan(14);
        // 1. 先跳过4字节，稍后回填总长度
        buffer.Advance(sizeof(int));
        
        // 2. 编码10字节消息头
        new MessageHeader
        {
            DeviceId = deviceId,
            ReplyExpected = msg.ReplyExpected,
            S = msg.S,
            F = msg.F,
            MessageType = MessageType.DataMessage,
            Id = id
        }.EncodeTo(buffer);
        
        // 3. 递归编码Item数据体（S1F1无数据体则不编码）
        msg.SecsItem?.EncodeTo(buffer);

        // 4. 回填总长度（Body长度，不包含自身4字节）
#if NET
        var lengthBytes = MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(buffer.WrittenSpan), 4);
#else
        var lengthBytes = new Span<byte>(Unsafe.AsPointer(ref MemoryMarshal.GetReference(buffer.WrittenSpan)), 4);
#endif
        BinaryPrimitives.WriteInt32BigEndian(lengthBytes, buffer.WrittenCount - sizeof(int));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleReplyMessage(ValueTaskCompletionSource<SecsMessage> source, SecsMessage secondaryMessage)
    {
        if (secondaryMessage.F == 0)
        {
            source.TrySetException(new SecsException(secondaryMessage, Resources.SxF0));
            return;
        }

        if (secondaryMessage.S == 9)
        {
            switch (secondaryMessage.F)
            {
                case 1:
                    source.TrySetException(new SecsException(secondaryMessage, Resources.S9F1));
                    break;
                case 3:
                    source.TrySetException(new SecsException(secondaryMessage, Resources.S9F3));
                    break;
                case 5:
                    source.TrySetException(new SecsException(secondaryMessage, Resources.S9F5));
                    break;
                case 7:
                    source.TrySetException(new SecsException(secondaryMessage, Resources.S9F7));
                    break;
                case 9:
                    source.TrySetException(new SecsException(secondaryMessage, Resources.S9F9));
                    break;
                case 11:
                    source.TrySetException(new SecsException(secondaryMessage, Resources.S9F11));
                    break;
                case 13:
                    source.TrySetException(new SecsException(secondaryMessage, Resources.S9F13));
                    break;
                default:
                    source.TrySetException(new SecsException(secondaryMessage, Resources.S9Fy));
                    break;
            }
            return;
        }

        source.TrySetResult(secondaryMessage);
    }
}
