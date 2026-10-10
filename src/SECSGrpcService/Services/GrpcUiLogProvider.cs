using Microsoft.Extensions.Logging;

namespace SECSGrpcService.Services;

/// <summary>
/// 将现有 gRPC 框架和本项目 gRPC 服务日志发布给测试窗体，不改变原有日志输出。
/// </summary>
public sealed class GrpcUiLogProvider : ILoggerProvider
{
    /// <summary>有 gRPC 日志写入时通知订阅者；可能从非 UI 线程触发。</summary>
    public event Action<string>? Logged;

    /// <summary>按日志类别创建仅负责 gRPC 相关日志的转发器。</summary>
    public ILogger CreateLogger(string categoryName) => new GrpcUiLogger(this, categoryName);

    /// <summary>此 Provider 不持有需要额外释放的资源。</summary>
    public void Dispose() { }

    /// <summary>将日志转给窗体；UI 订阅者的异常不影响 gRPC 请求处理。</summary>
    private void Publish(string line)
    {
        try
        {
            Logged?.Invoke(line);
        }
        catch (Exception)
        {
            // if 关键分支：窗口关闭等 UI 异常不得使业务日志写入失败。
        }
    }

    private sealed class GrpcUiLogger : ILogger
    {
        private readonly GrpcUiLogProvider _provider;
        private readonly string _category;

        /// <summary>保存日志类别，以便筛选 gRPC 调用相关条目。</summary>
        public GrpcUiLogger(GrpcUiLogProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        /// <summary>本窗体不显示日志 Scope，其余 Provider 仍可独立处理 Scope。</summary>
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        /// <summary>只接收信息级以上的 gRPC 框架和应用收发日志。</summary>
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None &&
            (_category.StartsWith("Grpc.", StringComparison.Ordinal) ||
             _category is "SECSGrpcService.Services.ReportGrpc" or
                 "SECSGrpcService.Services.SecsEfemGrpc" or
                 "SECSGrpcService.Services.GrpcCommandDispatcher" or
                 "SECSGrpcService.Services.NacosGrpcResolver");

        /// <summary>格式化 gRPC 日志并发布给窗体，不捕获请求对象。</summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // if 关键分支：过滤非 gRPC 类别和低级别日志，避免淹没右侧窗口。
            if (!IsEnabled(logLevel) || _provider.Logged is null)
            {
                return;
            }

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            var line = $"{DateTime.Now:HH:mm:ss.fff} [{logLevel}] {_category}: {message}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            _provider.Publish(line);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            /// <summary>空作用域没有待释放的资源。</summary>
            public void Dispose() { }
        }
    }
}
