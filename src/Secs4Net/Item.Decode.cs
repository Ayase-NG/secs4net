using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace Secs4Net;

public partial class Item
{
    /// <summary>
    /// 从第一个字节解码Format和Length字节数
    /// </summary>
    /// <param name="sourceBytes">输入数据</param>
    /// <param name="format">输出的SECS格式</param>
    /// <param name="lengthByteCount">长度字段的字节数（1-3）</param>
    /// <remarks>
    /// 解码规则：
    /// - format = 字节 >> 2（高6位）
    /// - lengthByteCount = 字节 &amp; 0b11（低2位）
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void DecodeFormatAndLengthByteCount(in ReadOnlySequence<byte> sourceBytes, out SecsFormat format, out byte lengthByteCount)
    {
#if NET
        var formatSeqFirstSpan = sourceBytes.FirstSpan;
#else
        var formatSeqFirstSpan = sourceBytes.First.Span;
#endif

#if DEBUG
        byte formatAndLengthByte = formatSeqFirstSpan[0];
#else
        byte formatAndLengthByte = formatSeqFirstSpan.DangerousGetReference();
#endif
        // 高6位为Format
        format = (SecsFormat)(formatAndLengthByte >> 2);
        // 低2位为Length字节数
        lengthByteCount = (byte)(formatAndLengthByte & 0b00000011);
    }

    /// <summary>
    /// 解码数据长度（Big Endian转小端）
    /// </summary>
    /// <param name="sourceBytes">长度字段（1-3字节）</param>
    /// <returns>解析后的长度值</returns>
    /// <remarks>
    /// SECS-II使用Big Endian编码，需要转换为小端序
    /// 例如：3字节 {0x01, 0x02, 0x03} → int 0x010203 = 66051
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    internal static int DecodeDataLength(in ReadOnlySequence<byte> sourceBytes)
    {
        var dataLength = 0;
        var lengthBytes = dataLength.AsBytes();
        sourceBytes.CopyTo(lengthBytes);
        // 反转字节序（Big Endian → 小端）
        lengthBytes[..(int)sourceBytes.Length].Reverse();
        return dataLength;
    }

    /// <summary>
    /// 从完整缓冲区解码Item（递归）
    /// </summary>
    /// <param name="bytes">输入缓冲区（会被修改）</param>
    /// <returns>解码后的Item</returns>
    /// <remarks>
    /// 解码流程：
    /// 1. 读取Format字节
    /// 2. 根据Format后的Length字节数读取长度
    /// 3. 如果是List：递归解码所有子Item
    /// 4. 否则：根据Format调用DecodeDataItem解码数据
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    [SkipLocalsInit]
    public static Item DecodeFromFullBuffer(ref ReadOnlySequence<byte> bytes)
    {
        // 读取Format字节
        var formatSeq = bytes.Slice(0, 1);
        DecodeFormatAndLengthByteCount(formatSeq, out var format, out var lengthByteCount);

        // 读取Length
        var dataLengthSeq = bytes.Slice(formatSeq.End, lengthByteCount);
        var dataLength = DecodeDataLength(dataLengthSeq);
        bytes = bytes.Slice(dataLengthSeq.End);

        if (format == SecsFormat.List)
        {
            // List类型：递归解码所有子Item
            if (dataLength == 0)
            {
                return L();
            }

            var items = new Item[dataLength];
            foreach (ref var subItem in items.AsSpan())
            {
                subItem = DecodeFromFullBuffer(ref bytes);
            }

            return L(items);
        }

        // 数据类型：读取并解码数据
        var dataItemBytes = bytes.Slice(0, dataLength);
        var item = DecodeDataItem(format, dataItemBytes);
        bytes = bytes.Slice(dataItemBytes.End);
        return item;
    }

    /// <summary>
    /// 根据Format解码数据Item
    /// </summary>
    /// <param name="format">SECS数据格式</param>
    /// <param name="bytes">数据字节</param>
    /// <returns>解码后的Item</returns>
    /// <remarks>
    /// 数据格式处理策略：
    /// - 字符串类型（ASCII、JIS8）：使用StringPool优化内存
    /// - 数值类型：根据长度决定解码方式：
    ///   - 0字节：返回空Item
    ///   - &lt;1024字节：使用byte[]数组
    ///   - &gt;=1024字节：使用MemoryOwner减少GC压力
    /// - 大端序数值：需要ReverseEndianness
    /// </remarks>
    internal static Item DecodeDataItem(SecsFormat format, in ReadOnlySequence<byte> bytes)
    {
        var length = (int)bytes.Length;
        return (format, length) switch
        {
            // ASCII字符串
            (SecsFormat.ASCII, 0) => A(),
            (SecsFormat.ASCII, >= 256) => A(ASCIIEncoding.GetString(bytes)),
            (SecsFormat.ASCII, _) => A(DecodePooledString(length, bytes, ASCIIEncoding)),

            // JIS8字符串
            (SecsFormat.JIS8, 0) => J(),
            (SecsFormat.JIS8, >= 256) => J(JIS8Encoding.GetString(bytes)),
            (SecsFormat.JIS8, _) => J(DecodePooledString(length, bytes, JIS8Encoding)),

            // Binary
            (SecsFormat.Binary, 0) => B(),
            (SecsFormat.Binary, >= 1024) => B(DecodeMemoryOwner<byte>(length, bytes)),
            (SecsFormat.Binary, _) => B(DecodeMemory<byte>(length, bytes)),

            // Boolean（每字节表示一个bool值）
            (SecsFormat.Boolean, 0) => Boolean(),
            (SecsFormat.Boolean, >= 1024) => Boolean(DecodeMemoryOwner<bool>(length, bytes)),
            (SecsFormat.Boolean, _) => Boolean(DecodeMemory<bool>(length, bytes)),

            // 整数类型（Big Endian编码，需要反转字节序）
            (SecsFormat.I8, 0) => I8(),
            (SecsFormat.I8, >= 1024) => I8(DecodeMemoryOwner<long>(length, bytes)),
            (SecsFormat.I8, _) => I8(DecodeMemory<long>(length, bytes)),

            (SecsFormat.I1, 0) => I1(),
            (SecsFormat.I1, >= 1024) => I1(DecodeMemoryOwner<sbyte>(length, bytes)),
            (SecsFormat.I1, _) => I1(DecodeMemory<sbyte>(length, bytes)),

            (SecsFormat.I2, 0) => I2(),
            (SecsFormat.I2, >= 1024) => I2(DecodeMemoryOwner<short>(length, bytes)),
            (SecsFormat.I2, _) => I2(DecodeMemory<short>(length, bytes)),

            (SecsFormat.I4, 0) => I4(),
            (SecsFormat.I4, >= 1024) => I4(DecodeMemoryOwner<int>(length, bytes)),
            (SecsFormat.I4, _) => I4(DecodeMemory<int>(length, bytes)),

            // 浮点类型（Big Endian编码）
            (SecsFormat.F8, 0) => F8(),
            (SecsFormat.F8, >= 1024) => F8(DecodeMemoryOwner<double>(length, bytes)),
            (SecsFormat.F8, _) => F8(DecodeMemory<double>(length, bytes)),

            (SecsFormat.F4, 0) => F4(),
            (SecsFormat.F4, >= 1024) => F4(DecodeMemoryOwner<float>(length, bytes)),
            (SecsFormat.F4, _) => F4(DecodeMemory<float>(length, bytes)),

            // 无符号整数类型（Big Endian编码）
            (SecsFormat.U8, 0) => U8(),
            (SecsFormat.U8, >= 1024) => U8(DecodeMemoryOwner<ulong>(length, bytes)),
            (SecsFormat.U8, _) => U8(DecodeMemory<ulong>(length, bytes)),

            (SecsFormat.U1, 0) => U1(),
            (SecsFormat.U1, >= 1024) => U1(DecodeMemoryOwner<byte>(length, bytes)),
            (SecsFormat.U1, _) => U1(DecodeMemory<byte>(length, bytes)),

            (SecsFormat.U2, 0) => U2(),
            (SecsFormat.U2, >= 1024) => U2(DecodeMemoryOwner<ushort>(length, bytes)),
            (SecsFormat.U2, _) => U2(DecodeMemory<ushort>(length, bytes)),

            (SecsFormat.U4, 0) => U4(),
            (SecsFormat.U4, >= 1024) => U4(DecodeMemoryOwner<uint>(length, bytes)),
            (SecsFormat.U4, _) => U4(DecodeMemory<uint>(length, bytes)),

            _ => ThrowHelper(),
        };

        [SkipLocalsInit]
        static string DecodePooledString(int length, in ReadOnlySequence<byte> bytes, Encoding encoding)
        {
            using var spanOwner = SpanOwner<byte>.Allocate(length);
            var span = spanOwner.Span;
            bytes.CopyTo(span);
            return StringPool.Shared.GetOrAdd(span, encoding);
        }

        [SkipLocalsInit]
        static unsafe Memory<T> DecodeMemory<T>(int length, in ReadOnlySequence<byte> bytes) where T : unmanaged, IEquatable<T>
        {
            var memory = new T[length / sizeof(T)];
            var span = memory.AsSpan();
            bytes.CopyTo(span.AsBytes());
            ReverseEndiannessHelper<T>.Reverse(span);
            return memory;
        }

        [SkipLocalsInit]
        static unsafe IMemoryOwner<T> DecodeMemoryOwner<T>(int length, in ReadOnlySequence<byte> bytes) where T : unmanaged, IEquatable<T>
        {
            var owner = MemoryOwner<T>.Allocate(length / sizeof(T));
            var span = owner.Span;
            bytes.CopyTo(span.AsBytes());
            ReverseEndiannessHelper<T>.Reverse(span);
            return owner;
        }

        [DoesNotReturn]
        static Item ThrowHelper() => throw new ArgumentOutOfRangeException();
    }
}
