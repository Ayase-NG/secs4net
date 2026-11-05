using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using SECShandler.Storage;
using Xunit;

namespace Secs4Net.UnitTests;

public class TypedVidValueStorageUnitTests
{
    /// <summary>
    /// 提供所有 SECS 类型及空值，验证缓存不丢失类型、数组或列表结构。
    /// </summary>
    public static IEnumerable<object[]> TypedValues()
    {
        yield return new object[] { A("text") };
        yield return new object[] { J("设备") };
        yield return new object[] { B(0, 255) };
        yield return new object[] { Boolean(true, false) };
        yield return new object[] { I1(-1, 2) };
        yield return new object[] { I2(-300, 300) };
        yield return new object[] { I4(-70000, 70000) };
        yield return new object[] { I8(long.MinValue, long.MaxValue) };
        yield return new object[] { U1(0, 255) };
        yield return new object[] { U2(0, ushort.MaxValue) };
        yield return new object[] { U4(0, uint.MaxValue) };
        yield return new object[] { U8(0, ulong.MaxValue) };
        yield return new object[] { F4(-1.25f, 3.5f) };
        yield return new object[] { F8(-1.25, 3.5) };
        yield return new object[] { L(A("nested"), L(U4(1, 2), Boolean(true))) };
        yield return new object[] { L() };
        yield return new object[] { A() };
        yield return new object[] { U4() };
    }

    /// <summary>
    /// 任意支持类型写入后均保持格式和完整内容，不转换为 string。
    /// </summary>
    [Theory]
    [MemberData(nameof(TypedValues))]
    public void UpsertAndRead_PreservesFormatAndValue(Item source)
    {
        using (source)
        {
            var storage = new TypedVidValueStorage();
            storage.UpsertByVid(123, source);

            storage.TryGetByVid(123, out var value).Should().BeTrue();
            using (value)
            {
                value!.Format.Should().Be(source.Format);
                value.Equals(source).Should().BeTrue();
            }
        }
    }

    /// <summary>
    /// 相同 VID 替换为最新类型与值，其他 VID 不受影响，缺失项返回 null。
    /// </summary>
    [Fact]
    public void Upsert_ReplacesOnlyMatchingVid()
    {
        var storage = new TypedVidValueStorage();
        using var oldValue = U4(123);
        using var newValue = L(A("latest"), F8(1.25));
        using var otherValue = Boolean(true);
        storage.UpsertByVid(1, oldValue);
        storage.UpsertByVid(2, otherValue);
        storage.TryGetByVid(1, out var previousRead).Should().BeTrue();
        using (previousRead)
        {
            storage.UpsertByVid(1, newValue);
            previousRead!.FirstValue<uint>().Should().Be(123);
        }

        storage.TryGetByVid(1, out var updated).Should().BeTrue();
        using (updated)
            updated!.Equals(newValue).Should().BeTrue();
        storage.TryGetByVid(2, out var other).Should().BeTrue();
        using (other)
            other!.Equals(otherValue).Should().BeTrue();
        storage.TryGetByVid(3, out var missing).Should().BeFalse();
        missing.Should().BeNull();
    }

    /// <summary>
    /// 缓存必须拥有自己的数值数组，不能保留输入的池化内存所有权。
    /// </summary>
    [Fact]
    public void Upsert_SourceMutatedAndDisposed_CacheRemainsValid()
    {
        var storage = new TypedVidValueStorage();
        using (var source = U1(MemoryPool<byte>.Shared.Rent(16)))
        {
            source.GetMemory<byte>().Span.Fill(42);
            storage.UpsertByVid(1, source);
            source.GetMemory<byte>().Span.Fill(99);
        }

        storage.TryGetByVid(1, out var value).Should().BeTrue();
        using (value)
            value!.FirstValue<byte>().Should().Be(42);
    }

    /// <summary>
    /// 修改或释放源列表及读取副本，不得污染后续读取的嵌套列表和值。
    /// </summary>
    [Fact]
    public void TryGet_DeepCopiesNestedListsOnWriteAndRead()
    {
        var storage = new TypedVidValueStorage();
        using (var source = L(U4(1, 2), L(A("original"))))
        {
            storage.UpsertByVid(1, source);
            source[0].FirstValue<uint>() = 100;
            source[1][0].Dispose();
            source[1][0] = A("changed source");
        }

        storage.TryGetByVid(1, out var first).Should().BeTrue();
        using (first)
        {
            first![0].FirstValue<uint>().Should().Be(1);
            first[0].FirstValue<uint>() = 200;
            first[1][0].Dispose();
            first[1][0] = A("changed read");
        }

        storage.TryGetByVid(1, out var second).Should().BeTrue();
        using (second)
        {
            second![0].FirstValue<uint>().Should().Be(1);
            second[1][0].GetString().Should().Be("original");
        }
    }

    /// <summary>
    /// 并发替换和读取同一 VID 时不共享可释放的内部 Item。
    /// </summary>
    [Fact]
    public void ConcurrentUpsertAndRead_ReturnsIndependentValidValues()
    {
        var storage = new TypedVidValueStorage();
        Parallel.For(0, 500, index =>
        {
            using var source = U4((uint)index);
            storage.UpsertByVid(1, source);
            storage.TryGetByVid(1, out var read).Should().BeTrue();
            using (read)
            {
                read!.FirstValue<uint>().Should().BeLessThan(500);
                read.FirstValue<uint>() = uint.MaxValue;
            }
        });

        storage.TryGetByVid(1, out var latest).Should().BeTrue();
        using (latest)
            latest!.FirstValue<uint>().Should().BeLessThan(500);
    }

    /// <summary>
    /// 拒绝 null 写入，且不能覆盖先前缓存值。
    /// </summary>
    [Fact]
    public void Upsert_Null_IsRejectedWithoutChangingStoredValue()
    {
        var storage = new TypedVidValueStorage();
        using var source = A("original");
        storage.UpsertByVid(1, source);
        Action write = () => storage.UpsertByVid(1, null!);

        write.Should().Throw<ArgumentNullException>();
        storage.TryGetByVid(1, out var value).Should().BeTrue();
        using (value)
            value!.GetString().Should().Be("original");
    }
}
