using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Secs4Net;

public partial class Item
{
    /// <summary>
    /// 编码Item头信息
    /// </summary>
    /// <param name="format">SECS数据格式（如List、ASCII、Binary、I4、F8等）</param>
    /// <param name="count">List的子项数量，或数据的字节长度</param>
    /// <param name="buffer">输出缓冲区</param>
    /// <remarks>
    /// Item头结构（1-4字节）：
    /// ┌────────────┬─────────────────┐
    /// │ Format(6B) │ Length Byte Count(2B) │
    /// │ 高6位为格式码 │ 低2位为长度字节数   │
    /// └────────────┴─────────────────┘
    /// ┌───────────────────────────────────┐
    /// │ Length (1-3B, Big Endian)         │
    /// │ 当Length Byte Count=1时：1字节    │
    /// │ 当Length Byte Count=2时：2字节    │
    /// │ 当Length Byte Count=3时：3字节    │
    /// └───────────────────────────────────┘
    /// 
    /// 编码规则：
    /// - count &lt;= 0xFF：使用1字节长度，总共2字节头
    /// - count &lt;= 0xFFFF：使用2字节长度，总共3字节头
    /// - count &lt;= 0xFFFFFF：使用3字节长度，总共4字节头
    /// - 大于0xFFFFFF：抛出ArgumentOutOfRangeException
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    private static void EncodeItemHeader(SecsFormat format, int count, IBufferWriter<byte> buffer)
    {
        ref var lengthRef0 = ref Unsafe.As<int, byte>(ref Unsafe.AsRef(in count));
        ref var encodedRef0 = ref MemoryMarshal.GetReference(buffer.GetSpan(sizeHint: 4));
        // Format占高6位
        var formatByte = (int)format << 2;
        
        // 根据count大小选择编码方式
        if (count <= 0xff)
        {   // 1字节长度编码
            // formatByte低2位设为01，表示长度占1字节
            encodedRef0 = (byte)(formatByte | 1);
            Unsafe.Add(ref encodedRef0, 1u) = lengthRef0;
            buffer.Advance(2);
            return;
        }
        if (count <= 0xff_ff)
        {   // 2字节长度编码
            // formatByte低2位设为10，表示长度占2字节
            encodedRef0 = (byte)(formatByte | 2);
            Unsafe.Add(ref encodedRef0, 1u) = Unsafe.Add(ref lengthRef0, 1u);
            Unsafe.Add(ref encodedRef0, 2u) = lengthRef0;
            buffer.Advance(3);
            return;
        }
        if (count <= 0xff_ff_ff)
        {   // 3字节长度编码
            // formatByte低2位设为11，表示长度占3字节
            encodedRef0 = (byte)(formatByte | 3);
            Unsafe.Add(ref encodedRef0, 1u) = Unsafe.Add(ref lengthRef0, 2u);
            Unsafe.Add(ref encodedRef0, 2u) = Unsafe.Add(ref lengthRef0, 1u);
            Unsafe.Add(ref encodedRef0, 3u) = lengthRef0;
            buffer.Advance(4);
            return;
        }

        ThrowHelper(count);

        [DoesNotReturn]
        static void ThrowHelper(int count) => throw new ArgumentOutOfRangeException(nameof(count), count, $@"Item length:{count} is overflow");
    }

    /// <summary>
    /// 编码空Item（长度为0的Item）
    /// </summary>
    /// <param name="format">SECS数据格式</param>
    /// <param name="buffer">输出缓冲区</param>
    /// <remarks>
    /// 空Item只有2字节头：Format(1B) + Length=0(1B)
    /// 适用于表示空字符串、空二进制、空列表等
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    private static void EncodeEmptyItem(SecsFormat format, IBufferWriter<byte> buffer)
    {
        var span = buffer.GetSpan(sizeHint: 2);
        ref var r0 = ref MemoryMarshal.GetReference(span);
        // Format占高6位，Length=0用低2位=01表示
        Unsafe.Add(ref r0, 0u) = (byte)(((int)format << 2) | 1);
        // 长度为0
        Unsafe.Add(ref r0, 1u) = 0;
        buffer.Advance(2);
    }
}
