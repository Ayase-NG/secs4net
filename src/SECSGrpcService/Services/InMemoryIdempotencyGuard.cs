using SECShandler.Interfaces;
using System.Collections.Concurrent;

namespace SECSGrpcService.Services;

/// <summary>
/// 进程内幂等保护实现（最小版）。
/// 使用键过期策略，在短 TTL 内拦截重复请求。
/// </summary>
public sealed class InMemoryIdempotencyGuard : IIdempotencyGuard
{
    private readonly ConcurrentDictionary<string, DateTime> _entries = new();

    public bool TryBegin(string scope, string key, TimeSpan ttl)
    {
        if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(key))
            return true;

        var now = DateTime.UtcNow;
        var composite = $"{scope}:{key}";

        // 先清理已过期键，避免字典无限增长。
        foreach (var item in _entries)
        {
            if (item.Value <= now)
            {
                _entries.TryRemove(item.Key, out _);
            }
        }

        var expiresAt = now.Add(ttl);

        // if 关键分支：不存在则写入并放行；已存在且未过期则拦截；已过期则覆盖并放行。
        while (true)
        {
            if (_entries.TryAdd(composite, expiresAt))
                return true;

            if (!_entries.TryGetValue(composite, out var existedExpiresAt))
                continue;

            if (existedExpiresAt <= now)
            {
                if (_entries.TryUpdate(composite, expiresAt, existedExpiresAt))
                    return true;

                continue;
            }

            return false;
        }
    }
}
