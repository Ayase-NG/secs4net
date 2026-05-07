using Secs4Net;
using System.Threading;

namespace SECSGrpcService.Services;

/// <summary>
/// 运行时 SECS 会话上下文。
/// 用于在不同服务间安全共享当前活动的 <see cref="SecsGem"/> 实例
/// （例如：监听服务负责 Attach/Detach，gRPC 服务负责 TryGet 后发送消息）。
/// </summary>
public sealed class SecsGemContext
{
    private readonly object _sync = new();
    private SecsGem? _secsGem;
    private int _dataIdCounter = -1;

    /// <summary>
    /// 绑定当前可用的 <see cref="SecsGem"/> 实例。
    /// </summary>
    public void Attach(SecsGem secsGem)
    {
        lock (_sync)
        {
            _secsGem = secsGem;
        }
    }

    /// <summary>
    /// 解绑当前实例。
    /// 仅当传入对象与当前绑定对象是同一引用时才会清空，避免误删新连接实例。
    /// </summary>
    public void Detach(SecsGem secsGem)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_secsGem, secsGem))
            {
                _secsGem = null;
            }
        }
    }

    /// <summary>
    /// 尝试获取当前活动的 <see cref="SecsGem"/>。
    /// </summary>
    /// <returns>存在可用实例时返回 true，否则返回 false。</returns>
    public bool TryGet(out SecsGem? secsGem)
    {
        lock (_sync)
        {
            secsGem = _secsGem;
            return secsGem is not null;
        }
    }

    /// <summary>
    /// 获取下一个 DATAID（0-255 循环），用于 S6F11 等消息的数据追踪。
    /// </summary>
    public byte GetNextDataId()
    {
        var next = Interlocked.Increment(ref _dataIdCounter);
        return unchecked((byte)next);
    }
}
