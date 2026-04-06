using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Secs4Net;

public readonly record struct MessageHeader
{
    public int Id { get; init; }
    public ushort DeviceId { get; init; }
    public MessageType MessageType { get; init; }
    public byte S { get; init; }
    public byte F { get; init; }
    public bool ReplyExpected { get; init; }

    /// <summary>
    /// 将消息头编码为10字节二进制格式
    /// </summary>
    /// <param name="buffer">输出缓冲区</param>
    /// <remarks>
    /// HSMS消息头（10字节）结构：
    /// ┌─────────────────────────────────────────────────────────┐
    /// │ 0-1    │ 2         │ 3   │ 4   │ 5        │ 6-9       │
    /// │ Device │ S | W-Bit │ F   │ E   │ MsgType  │ System    │
    /// │ ID(2B) │ (1B)      │(1B) │(1B) │ (1B)     │ Bytes(4B) │
    /// └─────────────────────────────────────────────────────────┘
    /// 
    /// - Device ID (2B): 设备ID，Big Endian
    /// - S (7B) + W-Bit (1B): S编号，W-Bit=1表示需要回复
    /// - F (1B): F编号
    /// - E (1B): 保留位
    /// - MessageType (1B): 消息类型（Data/Control）
    /// - System Bytes (4B): 消息ID，Big Endian，用于匹配请求和回复
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    internal void EncodeTo(IBufferWriter<byte> buffer)
    {
        var span = buffer.GetSpan(sizeHint: 10);
        // 字节0-1: Device ID (Big Endian)
        BinaryPrimitives.WriteUInt16BigEndian(span, DeviceId);
        ref var r0 = ref MemoryMarshal.GetReference(span);
        // 字节2: S编号 + W-Bit（最高位）
        Unsafe.Add(ref r0, 2u) = (byte)(S | (ReplyExpected ? 0b1000_0000 : 0));
        // 字节3: F编号
        Unsafe.Add(ref r0, 3u) = F;
        // 字节4: E（保留位，固定为0）
        Unsafe.Add(ref r0, 4u) = 0;
        // 字节5: MessageType
        Unsafe.Add(ref r0, 5u) = (byte)MessageType;
        // 字节6-9: System Bytes (Big Endian)
        BinaryPrimitives.WriteInt32BigEndian(span[6..], Id);
        buffer.Advance(10);
    }

    /// <summary>
    /// 从10字节二进制数据解码消息头
    /// </summary>
    /// <param name="span">输入的10字节数据</param>
    /// <param name="header">输出的消息头结构</param>
    /// <remarks>
    /// 解码过程与EncodeTo相反：
    /// 1. 读取字节0-1为Device ID（与0x7FFF按位与去除W位）
    /// 2. 读取字节2，分离S编号和W-Bit
    /// 3. 读取字节3为F编号
    /// 4. 读取字节5为MessageType
    /// 5. 读取字节6-9为System Bytes
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    internal static void Decode(ReadOnlySpan<byte> span, out MessageHeader header)
    {
        ref var r0 = ref MemoryMarshal.GetReference(span);
        var s = Unsafe.Add(ref r0, 2u);
        header = new MessageHeader
        {
            // Device ID：读取2字节并清除最高位（W-Bit）
            DeviceId = (ushort)(BinaryPrimitives.ReadUInt16BigEndian(span) & 0b01111111_11111111),
            // W-Bit：从字节2的最高位提取
            ReplyExpected = (s & 0b1000_0000) != 0,
            // S编号：字节2的低7位
            S = (byte)(s & 0b0111_1111),
            // F编号：字节3
            F = Unsafe.Add(ref r0, 3u),
            // MessageType：字节5
            MessageType = (MessageType)Unsafe.Add(ref r0, 5u),
            // System Bytes：字节6-9 (Big Endian)
            Id = BinaryPrimitives.ReadInt32BigEndian(span[6..]),
        };
    }
}
