using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using FluentAssertions;
using SECSGrpcService.Services;
using Xunit;

namespace Secs4Net.UnitTests;

public class IdempotencyGuardUnitTests
{
    /// <summary>
    /// 保留旧接口的短期拦截语义，成功释放后可重新占用。
    /// </summary>
    [Fact]
    public void TryRemove_OwnToken_AllowsRetry()
    {
        var guard = new InMemoryIdempotencyGuard();
        var ttl = TimeSpan.FromSeconds(30);
        guard.TryBegin("S16F15", "PJ1", ttl, out var token).Should().BeTrue();
        token.Should().NotBe(Guid.Empty);
        guard.TryBegin("S16F15", "PJ1", ttl).Should().BeFalse();

        guard.TryRemove("S16F15", "PJ1", token).Should().BeTrue();
        guard.TryRemove("S16F15", "PJ1", token).Should().BeFalse();
        guard.TryBegin("S16F15", "PJ1", ttl, out var newToken).Should().BeTrue();
        newToken.Should().NotBe(token);
    }

    /// <summary>
    /// 重复请求拿不到删除令牌，错误或空令牌均不能解除别人的防重。
    /// </summary>
    [Fact]
    public void TryRemove_DuplicateOrWrongToken_DoesNotRemove()
    {
        var guard = new InMemoryIdempotencyGuard();
        var ttl = TimeSpan.FromSeconds(30);
        guard.TryBegin("S16F15", "PJ1", ttl, out var owner).Should().BeTrue();
        guard.TryBegin("S16F15", "PJ1", ttl, out var duplicate).Should().BeFalse();
        duplicate.Should().Be(Guid.Empty);

        guard.TryRemove("S16F15", "PJ1", duplicate).Should().BeFalse();
        guard.TryRemove("S16F15", "PJ1", Guid.NewGuid()).Should().BeFalse();
        guard.TryBegin("S16F15", "PJ1", ttl).Should().BeFalse();
        guard.TryRemove("S16F15", "PJ1", owner).Should().BeTrue();
    }

    /// <summary>
    /// 旧请求超过 TTL 后才失败，不能删除新请求已经取得的占用。
    /// </summary>
    [Fact]
    public void TryRemove_ExpiredOwner_DoesNotRemoveNewEntry()
    {
        var clock = new ManualTimeProvider();
        var guard = new InMemoryIdempotencyGuard(clock);
        var ttl = TimeSpan.FromSeconds(30);
        guard.TryBegin("S16F15", "PJ1", ttl, out var oldToken).Should().BeTrue();
        clock.Advance(ttl);
        guard.TryBegin("S16F15", "PJ1", ttl, out var newToken).Should().BeTrue();

        guard.TryRemove("S16F15", "PJ1", oldToken).Should().BeFalse();
        guard.TryBegin("S16F15", "PJ1", ttl).Should().BeFalse();
        guard.TryRemove("S16F15", "PJ1", newToken).Should().BeTrue();
    }

    /// <summary>
    /// 删除操作必须同时匹配 scope、key 和令牌，不能影响其他业务记录。
    /// </summary>
    [Fact]
    public void TryRemove_DifferentScopeOrKey_DoesNotRemove()
    {
        var guard = new InMemoryIdempotencyGuard();
        var ttl = TimeSpan.FromSeconds(30);
        guard.TryBegin("S16F15", "PJ1", ttl, out var token).Should().BeTrue();
        guard.TryBegin("S14F9", "PJ1", ttl, out var other).Should().BeTrue();

        guard.TryRemove("S14F9", "PJ1", token).Should().BeFalse();
        guard.TryRemove("S16F15", "PJ2", token).Should().BeFalse();
        guard.TryRemove("S16F15", "PJ1", token).Should().BeTrue();
        guard.TryBegin("S14F9", "PJ1", ttl).Should().BeFalse();
        guard.TryRemove("S14F9", "PJ1", other).Should().BeTrue();
    }

    /// <summary>
    /// 空键沿用不启用去重的兼容行为，不创建任何可释放记录。
    /// </summary>
    [Theory]
    [InlineData("", "PJ1")]
    [InlineData("S16F15", "")]
    [InlineData(" ", "PJ1")]
    public void TryBegin_EmptyScopeOrKey_HasNoOwnership(string scope, string key)
    {
        var guard = new InMemoryIdempotencyGuard();
        guard.TryBegin(scope, key, TimeSpan.FromSeconds(30), out var token).Should().BeTrue();
        token.Should().Be(Guid.Empty);
        guard.TryRemove(scope, key, Guid.NewGuid()).Should().BeFalse();
    }

    /// <summary>
    /// 并发占用只能产生一个持有者；并发释放只能有一次成功。
    /// </summary>
    [Fact]
    public void ConcurrentBeginAndRemove_HaveSingleWinner()
    {
        var guard = new InMemoryIdempotencyGuard();
        var tokens = new ConcurrentBag<Guid>();
        Parallel.For(0, 100, _ =>
        {
            // 同一个键只允许一个调用取得删除权限。
            if (guard.TryBegin("S16F15", "PJ1", TimeSpan.FromSeconds(30), out var token))
                tokens.Add(token);
        });
        var owner = tokens.Should().ContainSingle().Subject;
        var removals = new ConcurrentBag<bool>();
        Parallel.For(0, 100, _ => removals.Add(guard.TryRemove("S16F15", "PJ1", owner)));
        removals.Should().ContainSingle(result => result);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        /// <summary>
        /// 返回可控 UTC，避免过期测试依赖实际等待。
        /// </summary>
        public override DateTimeOffset GetUtcNow() => _now;

        /// <summary>
        /// 将测试时钟向前推进指定时间。
        /// </summary>
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
