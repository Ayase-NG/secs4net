using CommunityToolkit.HighPerformance.Buffers;

namespace Secs4Net;

/// <summary>
/// 主消息包装器，用于处理来自设备/Host的主动发送消息（Primary Message）
/// </summary>
/// <remarks>
/// 在SECS/GEM协议中：
/// - Primary Message: 设备主动发送的消息，通常期望接收方回复
/// - Secondary Message: 对Primary Message的回复
/// 
/// 例如：设备发送S6F11（事件报告），期望Host回复S6F12
/// </remarks>
public sealed class PrimaryMessageWrapper
{
    private readonly SemaphoreSlim _semaphoreSlim = new(initialCount: 1);
    private readonly WeakReference<SecsGem> _secsGem;
    
    /// <summary>原始的主消息（来自设备/Host）</summary>
    public SecsMessage PrimaryMessage { get; }
    
    /// <summary>消息ID，用于匹配请求与回复</summary>
    public int Id { get; }
    
    /// <summary>已发送的回复消息（如果有）</summary>
    public SecsMessage? SecondaryMessage { get; private set; }

    /// <summary>
    /// 内部构造函数，由SecsGem创建
    /// </summary>
    /// <param name="secsGem">对SecsGem的弱引用，避免循环引用导致内存泄漏</param>
    /// <param name="primaryMessage">接收到的原始主消息</param>
    /// <param name="id">消息ID，用于匹配回复</param>
    internal PrimaryMessageWrapper(SecsGem secsGem, SecsMessage primaryMessage, int id)
    {
        _secsGem = new WeakReference<SecsGem>(secsGem);
        PrimaryMessage = primaryMessage;
        Id = id;
    }

    /// <summary>
    /// 尝试回复主消息
    /// </summary>
    /// <param name="replyMessage">回复消息，如果为null则自动回复S9F7（未知消息错误）</param>
    /// <param name="cancellation">取消令牌</param>
    /// <returns>是否回复成功；如果已经回复过则返回false</returns>
    public async Task<bool> TryReplyAsync(SecsMessage? replyMessage = null, CancellationToken cancellation = default)
    {
        // 检查：主消息是否需要回复（ReplyExpected标志）
        if (!PrimaryMessage.ReplyExpected)
        {
            throw new SecsException("The message does not need to reply");
        }

        // 检查：SecsGem实例是否仍然存在（通过弱引用）
        if (!_secsGem.TryGetTarget(out var secsGem))
        {
            throw new SecsException("Hsms connector loss, the message has no chance to reply via the ReplyAsync method");
        }

        // 如果没有提供回复消息，构造S9F7错误响应
        // S9F7: Unknown message - 用于告知对方不认识该消息类型
        if (replyMessage is null)
        {
            var headerBytes = new byte[10];
            var buffer = new MemoryBufferWriter<byte>(headerBytes);
            new MessageHeader
            {
                DeviceId = secsGem.DeviceId,
                ReplyExpected = PrimaryMessage.ReplyExpected,
                S = PrimaryMessage.S,
                F = PrimaryMessage.F,
                MessageType = MessageType.DataMessage,
                Id = Id
            }.EncodeTo(buffer);
            replyMessage = new SecsMessage(9, 7, replyExpected: false)
            {
                Name = "Unknown Message",
                SecsItem = Item.B(headerBytes),
            };
        }
        else
        {
            // 用户提供了回复消息，关闭ReplyExpected标志
            // 因为这是回复消息，不需要再期待对方的回复
            replyMessage.ReplyExpected = false;
        }

        // 使用信号量确保同一消息只被回复一次（防止并发重复回复）
        await _semaphoreSlim.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            // 再次检查：是否已经回复过
            if (SecondaryMessage is not null)
            {
                return false;
            }

            // S9Fy消息使用新ID（因为S9Fy是错误消息，不是真正的回复）
            // 其他消息使用原始ID来匹配请求
            int id = replyMessage.S == 9 ? MessageIdGenerator.NewId() : Id;
            await secsGem.SendDataMessageAsync(replyMessage, id, cancellation).ConfigureAwait(false);
            SecondaryMessage = replyMessage;
            return true;
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    public override string ToString() => PrimaryMessage.ToString();
}
