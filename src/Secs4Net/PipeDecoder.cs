using CommunityToolkit.HighPerformance;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Secs4Net;

public sealed class PipeDecoder
{
    private readonly PipeReader _reader;
    public PipeWriter Input { get; }

    private readonly Channel<MessageHeader> _controlMessageChannel = Channel
        .CreateUnbounded<MessageHeader>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });

    private readonly Channel<(MessageHeader header, Item? rootItem)> _dataMessageChannel = Channel
        .CreateUnbounded<(MessageHeader header, Item? rootItem)>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });

    public PipeDecoder(PipeReader reader, PipeWriter input)
    {
        _reader = reader;
        Input = input;
    }

    internal IAsyncEnumerable<MessageHeader> GetControlMessages(CancellationToken cancellation)
        => _controlMessageChannel.Reader.ReadAllAsync(cancellation);

    public IAsyncEnumerable<(MessageHeader header, Item? rootItem)> GetDataMessages(CancellationToken cancellation)
        => _dataMessageChannel.Reader.ReadAllAsync(cancellation);

    public Task StartAsync(CancellationToken cancellation)
        => DecodeLoopAsync(_controlMessageChannel.Writer, _dataMessageChannel.Writer, _reader, cancellation);

    /// <summary>
    /// 消息解码主循环
    /// </summary>
    /// <remarks>
    /// 数据流（接收方向）：
    /// 
    /// Socket数据 → Pipe → DecodeLoopAsync → 数据/控制消息Channel
    /// 
    /// 解码步骤：
    /// 1. 读取4字节 → 总消息长度
    /// 2. 读取10字节 → 消息头（Header）
    /// 3. 根据消息类型分发到不同Channel：
    ///    - 控制消息（Control Message）→ _controlMessageChannel
    ///    - 数据消息（Data Message）→ _dataMessageChannel
    /// 4. 数据消息需要进一步解码Item树
    /// 
    /// Item解码采用流式处理：
    /// - 使用栈（Stack）跟踪嵌套的List
    /// - 遇到List时压栈，继续读取子项
    /// - List满时弹栈，继续处理外层
    /// </remarks>
    private static async Task DecodeLoopAsync(
        ChannelWriter<MessageHeader> controlMessageWriter,
        ChannelWriter<(MessageHeader header, Item? rootItem)> dataMessageWriter,
        PipeReader reader,
        CancellationToken cancellation)
    {
        // 栈用于跟踪嵌套List的解码状态
        var stack = new Stack<ItemList>(capacity: 8);
        Item item;
        var totalLengthBytes = new byte[4];
        var messageHeaderBytes = new byte[10];
        
        // 预先读取4字节（总长度字段）
        var buffer = await PipeReadAsync(reader, required: 4, cancellation).ConfigureAwait(false);
        
        while (!cancellation.IsCancellationRequested)
        {
        Start:
            // === 步骤1: 读取4字节总消息长度 ===
            if (IsBufferInsufficient(reader, ref buffer, required: 4))
            {
                buffer = await PipeReadAsync(reader, required: 4, cancellation).ConfigureAwait(false);
            }
            var totalLengthSeq = buffer.Slice(buffer.Start, 4);
            totalLengthSeq.CopyTo(totalLengthBytes);
            // Big Endian解析消息长度（不包含自身4字节）
            uint messageLength = BinaryPrimitives.ReadUInt32BigEndian(totalLengthBytes);
            buffer = buffer.Slice(totalLengthSeq.End);

            Debug.WriteLine($"Get new message with length: {messageLength}");

            // === 步骤2: 读取10字节消息头 ===
            if (IsBufferInsufficient(reader, ref buffer, required: 10))
            {
                buffer = await PipeReadAsync(reader, required: 10, cancellation).ConfigureAwait(false);
            }
            var messageHaderSeq = buffer.Slice(buffer.Start, 10);
            messageHaderSeq.CopyTo(messageHeaderBytes);
            // 解码消息头
            MessageHeader.Decode(messageHeaderBytes, out var header);
            buffer = buffer.Slice(messageHaderSeq.End);

            Debug.WriteLine($"Get message(id:{header.Id:X8}) header");

            // === 步骤3: 判断消息类型并分发 ===
            // 如果消息只有头没有数据体
            if (messageLength == 10) // only message header
            {
                if (header.MessageType == MessageType.DataMessage)
                {
                    // 无数据体的数据消息
                    await dataMessageWriter.WriteAsync((header, rootItem: null), cancellation).ConfigureAwait(false);
                }
                else
                {
                    // 控制消息（如LinkTest、Select等）
                    await controlMessageWriter.WriteAsync(header, cancellation).ConfigureAwait(false);
                }
                continue;
            }

            // === 步骤4: 解码Item数据体 ===
            // 情况A：缓冲区已有完整数据，可一次性解码
            if (buffer.Length >= messageLength - 10)
            {
                var rootItem = Item.DecodeFromFullBuffer(ref buffer);
                Debug.WriteLine($"Get data message(id:{header.Id:X8}) with total bytes: {messageLength} and decoded directly");
                await dataMessageWriter.WriteAsync((header, rootItem), cancellation).ConfigureAwait(false);
                continue;
            }

            // 情况B：数据分块到达，需要流式解码
        GetNewItem:
            // 读取1字节：Format + Length字节数
            if (IsBufferInsufficient(reader, ref buffer, required: 1))
            {
                buffer = await PipeReadAsync(reader, required: 1, cancellation).ConfigureAwait(false);
            }

            var formatSeq = buffer.Slice(0, 1);
            // 解析Format（高6位）和Length字节数（低2位）
            Item.DecodeFormatAndLengthByteCount(formatSeq, out var itemFormat, out var itemContentLengthByteCount);
            buffer = buffer.Slice(formatSeq.End);

            // 读取Length（1-3字节）
            if (IsBufferInsufficient(reader, ref buffer, required: itemContentLengthByteCount))
            {
                buffer = await PipeReadAsync(reader, required: itemContentLengthByteCount, cancellation).ConfigureAwait(false);
            }
            var itemContentLengthBytes = buffer.Slice(0, itemContentLengthByteCount);
            var itemContentLength = Item.DecodeDataLength(itemContentLengthBytes);
            buffer = buffer.Slice(itemContentLengthBytes.End);

            // 解码Item内容
            if (itemFormat is SecsFormat.List)
            {
                // List类型
                if (itemContentLength == 0)
                {
                    // 空List
                    item = Item.L();
                    Debug.WriteLine($"Decoded List[0]");
                }
                else
                {
                    // 非空List，压栈并继续读取子项
                    Debug.WriteLine($"Decoded List[{itemContentLength}]");
                    stack.Push(new ItemList(size: itemContentLength));
                    goto GetNewItem;
                }
            }
            else
            {
                // 数据类型（ASCII、Binary、数值等）
                if (IsBufferInsufficient(reader, ref buffer, required: itemContentLength))
                {
                    buffer = await PipeReadAsync(reader, required: itemContentLength, cancellation).ConfigureAwait(false);
                }
                var itemDataBytes = buffer.Slice(0, itemContentLength);
                item = Item.DecodeDataItem(itemFormat, itemDataBytes);
                buffer = buffer.Slice(itemDataBytes.End);
                Debug.WriteLine($"Decoded Item[{itemFormat}], length: {itemContentLength}");
            }

            // 处理栈：添加Item到当前List，必要时弹栈
            if (stack.Count > 0)
            {
                var list = stack.Peek();
                list.Add(item);
                // 当List的子项全部解码完成时，弹栈并继续处理外层
                while (list.IsFull) 
                {
                    item = Item.L(stack.Pop().Items);
                    if (stack.Count > 0)
                    {
                        list = stack.Peek();
                        list.Add(item);
                    }
                    else
                    {
                        Debug.WriteLine($"Get data message(id:{header.Id:X8}) decoded by data chunked");
                        await dataMessageWriter.WriteAsync((header, item), cancellation).ConfigureAwait(false);
                        goto Start;
                    }
                }
                goto GetNewItem;
            }
            else
            {
                // 栈为空，说明是根Item，直接输出
                Debug.WriteLine($"Get data message(id:{header.Id:X8}) decoded by data chunked");
                await dataMessageWriter.WriteAsync((header, item), cancellation).ConfigureAwait(false);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsBufferInsufficient(PipeReader reader, ref ReadOnlySequence<byte> remainedBuffer, int required)
    {
        if (remainedBuffer.Length >= required)
        {
            return false;
        }

        reader.AdvanceTo(remainedBuffer.Start);
        return !PipeTryRead(reader, required, ref remainedBuffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool PipeTryRead(PipeReader reader, int required, ref ReadOnlySequence<byte> buffer)
    {
        if (reader.TryRead(out var result))
        {
            buffer = result.Buffer;
            if (buffer.Length >= required)
            {
                return true;
            }
            else
            {
                reader.AdvanceTo(consumed: buffer.Start, examined: buffer.End);
            }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    private static ValueTask<ReadOnlySequence<byte>> PipeReadAsync(PipeReader reader, int required, CancellationToken cancellation)
    {
        ReadOnlySequence<byte> buffer = ReadOnlySequence<byte>.Empty;
        if (PipeTryRead(reader, required, ref buffer))
        {
            return new(buffer);
        }

        return SlowPipeReadAsync(reader, required, cancellation);

        [MethodImpl(MethodImplOptions.NoInlining)]
#if NET
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
        static async ValueTask<ReadOnlySequence<byte>> SlowPipeReadAsync(PipeReader reader, int required, CancellationToken cancellation)
        {
            while (true)
            {
                //StartT8Timer();
                var result = await reader.ReadAsync(cancellation).ConfigureAwait(false);
                //StopT8Timer();
                var buffer = result.Buffer;

                if (buffer.Length >= required)
                {
                    return buffer;
                }
                reader.AdvanceTo(consumed: buffer.Start, examined: buffer.End);
            }
        }
    }

    private sealed class ItemList(int size)
    {
        private readonly Item[] _items = new Item[size];
        private int _current;

        public bool IsFull => _current == _items.Length;
        public void Add(Item item) => _items[_current++] = item;
        public Item[] Items => _items;
    }
}
