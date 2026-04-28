using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace SECSGrpcService.Services;

/// <summary>
/// 将日志追加写入按天分文件的简单 Provider。
/// 文件名格式：yyyy-MM-dd_secs.log
/// </summary>
public sealed class DateFileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly object _fileLock = new();
    private readonly ConcurrentDictionary<string, DateFileLogger> _loggers = new();

    public DateFileLoggerProvider(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, name => new DateFileLogger(name, _directory, _fileLock));

    public void Dispose()
    {
    }

    private sealed class DateFileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly string _directory;
        private readonly object _fileLock;

        public DateFileLogger(string categoryName, string directory, object fileLock)
        {
            _categoryName = categoryName;
            _directory = directory;
            _fileLock = fileLock;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null) return;

            var now = DateTime.Now;
            var filePath = Path.Combine(_directory, $"{now:yyyy-MM-dd}_secs.log");
            var line = $"{now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {_categoryName}: {message}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            lock (_fileLock)
            {
                File.AppendAllText(filePath, line + Environment.NewLine);
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
