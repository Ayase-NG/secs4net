using System.Diagnostics.CodeAnalysis;
using Secs4Net;
using SECShandler.Interfaces;

namespace SECShandler.Storage
{
    /// <summary>
    /// 线程安全的 VID→Item 内存缓存；内部只持有自有托管数组，不保留消息池化内存。
    /// </summary>
    public sealed class TypedVidValueStorage : ITypedVidValueStorage
    {
        private readonly object _sync = new();
        private readonly Dictionary<ushort, Item> _values = new();

        /// <summary>
        /// 深拷贝输入后替换同 VID 旧值，避免外部消息释放或修改影响缓存。
        /// </summary>
        public void UpsertByVid(ushort vid, Item value)
        {
            ArgumentNullException.ThrowIfNull(value);
            var copy = CloneItem(value);

            lock (_sync)
            {
                // 替换时释放旧 Item；读取方持有自己的副本，不会受到影响。
                if (_values.TryGetValue(vid, out var previous))
                    previous.Dispose();

                _values[vid] = copy;
            }
        }

        /// <summary>
        /// 在锁内复制最新值，调用方释放或修改返回值不会改变缓存。
        /// </summary>
        public bool TryGetByVid(ushort vid, [NotNullWhen(true)] out Item? value)
        {
            lock (_sync)
            {
                // 只返回独立副本，禁止暴露内部 Item 及其可写内存。
                if (_values.TryGetValue(vid, out var stored))
                {
                    value = CloneItem(stored);
                    return true;
                }

                value = null;
                return false;
            }
        }

        /// <summary>
        /// 按原 SECS 格式递归复制，保留数组、嵌套列表及空项，不经字符串转换。
        /// </summary>
        private static Item CloneItem(Item value)
        {
            // 每种数值类型使用独立托管数组，避免共享输入 Item 的 Memory/MemoryOwner。
            return value.Format switch
            {
                SecsFormat.List => Item.L(value.Items.Select(CloneItem)),
                SecsFormat.ASCII => Item.A(value.GetString()),
                SecsFormat.JIS8 => Item.J(value.GetString()),
                SecsFormat.Binary => Item.B(value.GetMemory<byte>().ToArray()),
                SecsFormat.Boolean => Item.Boolean(value.GetMemory<bool>().ToArray()),
                SecsFormat.I1 => Item.I1(value.GetMemory<sbyte>().ToArray()),
                SecsFormat.I2 => Item.I2(value.GetMemory<short>().ToArray()),
                SecsFormat.I4 => Item.I4(value.GetMemory<int>().ToArray()),
                SecsFormat.I8 => Item.I8(value.GetMemory<long>().ToArray()),
                SecsFormat.U1 => Item.U1(value.GetMemory<byte>().ToArray()),
                SecsFormat.U2 => Item.U2(value.GetMemory<ushort>().ToArray()),
                SecsFormat.U4 => Item.U4(value.GetMemory<uint>().ToArray()),
                SecsFormat.U8 => Item.U8(value.GetMemory<ulong>().ToArray()),
                SecsFormat.F4 => Item.F4(value.GetMemory<float>().ToArray()),
                SecsFormat.F8 => Item.F8(value.GetMemory<double>().ToArray()),
                _ => throw new NotSupportedException($"Unsupported SECS format: {value.Format}.")
            };
        }
    }
}
