using System;
using System.Globalization;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SECSbuilder;
using SECShandler.Functions;
using SECShandler.Handlers;
using SECShandler.Interfaces;
using SECSparser;
using Xunit;

namespace Secs4Net.UnitTests;

public class DateTimeQueryUnitTests
{
    /// <summary>
    /// S2F17 仅有消息头，不接受 L[0] 或其他任何数据项。
    /// </summary>
    [Fact]
    public void Parse_HeaderOnly_AcceptsOnlyS2F17WithoutBody()
    {
        using var valid = new SecsMessage(2, 17);
        using var emptyList = new SecsMessage(2, 17) { SecsItem = L() };
        using var ascii = new SecsMessage(2, 17) { SecsItem = A("2024022912345699") };
        using var wrongType = new SecsMessage(2, 31);

        Action parse = () => S2F17_parser.Parse(valid);
        Action parseList = () => S2F17_parser.Parse(emptyList);
        Action parseAscii = () => S2F17_parser.Parse(ascii);
        Action parseWrongType = () => S2F17_parser.Parse(wrongType);

        parse.Should().NotThrow();
        parseList.Should().Throw<InvalidOperationException>();
        parseAscii.Should().Throw<InvalidOperationException>();
        parseWrongType.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// 确认 TIME 为固定 ASCII 16 位，截取百分之一秒，不因区域日历或时区发生变化。
    /// </summary>
    [Theory]
    [InlineData("en-US")]
    [InlineData("ar-SA")]
    [InlineData("th-TH")]
    public void Build_TimeData_IsInvariantAscii16(string culture)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            using var reply = S2F18_builder.Build(new DateTime(2024, 2, 29, 12, 34, 56, 999, DateTimeKind.Utc));

            reply.S.Should().Be(2);
            reply.F.Should().Be(18);
            reply.ReplyExpected.Should().BeFalse();
            reply.SecsItem!.Format.Should().Be(SecsFormat.ASCII);
            reply.SecsItem.Count.Should().Be(16);
            reply.SecsItem.GetString().Should().Be("2024022912345699");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>
    /// 未约定时区的 DateTime 不允许被静默作为 UTC 时间发送。
    /// </summary>
    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Build_NonUtcTime_IsRejected(DateTimeKind kind)
    {
        Action build = () => S2F18_builder.Build(new DateTime(2024, 2, 29, 12, 34, 56, kind));

        build.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// 查询与设置消息均由时间处理器注册，不将 SecondaryMessage 当作请求路由。
    /// </summary>
    [Fact]
    public void Handler_RegistersQueryAndSetRequests()
    {
        var handler = new TimeSyncPrimaryMessageHandler(Substitute.For<ITimeSyncStorage>());

        handler.SupportedMessages.Should().Contain((2, 17)).And.Contain((2, 31));
        handler.CanHandle(2, 17).Should().BeTrue();
        handler.CanHandle(2, 31).Should().BeTrue();
        handler.CanHandle(2, 18).Should().BeFalse();
        handler.CanHandle(1, 17).Should().BeFalse();
    }

    /// <summary>
    /// 通过实际 SECS 收发和处理器路由验证设备时钟查询及非法数据体错误。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Handle_Query_ReturnsTimeOrIllegalData(int bodyKind)
    {
        var pipe1 = new Pipe(new PipeOptions(useSynchronizationContext: false));
        var pipe2 = new Pipe(new PipeOptions(useSynchronizationContext: false));
        var hostConnection = new PipeConnection(pipe1.Reader, pipe2.Writer);
        var equipmentConnection = new PipeConnection(pipe2.Reader, pipe1.Writer);
        var options = Options.Create(new SecsGemOptions { DeviceId = 0, T3 = 5000 });
        using var host = new SecsGem(options, hostConnection, Substitute.For<ISecsGemLogger>());
        using var equipment = new SecsGem(options, equipmentConnection, Substitute.For<ISecsGemLogger>());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var storage = Substitute.For<ITimeSyncStorage>();
        storage.GetEquipmentTimeUtc().Returns(new DateTime(2024, 2, 29, 12, 34, 56, 990, DateTimeKind.Utc));
        // 返回时间必须取自当前设备时钟，而非历史 Host 时间。
        storage.GetLastHostTimeUtc().Returns(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var handler = new TimeSyncPrimaryMessageHandler(storage);
        using var request = new SecsMessage(2, 17)
        {
            SecsItem = bodyKind switch { 1 => L(), 2 => A("unexpected"), _ => null }
        };
        hostConnection.Start(cts.Token);
        equipmentConnection.Start(cts.Token);

        // 异步接收设备端 S2F17 请求。
        var incoming = equipment.GetPrimaryMessageAsync(cts.Token).FirstAsync(cts.Token).AsTask();
        // 异步发送查询并等待与请求关联的应答。
        var pendingReply = host.SendAsync(request, cts.Token);
        // 异步取得收到的主消息。
        var primary = await incoming;
        // 异步通过已注册处理器分发查询并返回 S2F18 或 S9F7。
        await handler.HandleAsync(equipment, primary, cts.Token);

        // Header Only 返回当前设备时间，不应调用任何时间设置操作。
        if (bodyKind == 0)
        {
            // 异步取得与 S2F17 关联的 S2F18。
            using var reply = await pendingReply;
            reply.S.Should().Be(2);
            reply.F.Should().Be(18);
            reply.ReplyExpected.Should().BeFalse();
            reply.SecsItem!.GetString().Should().Be("2024022912345699");
            storage.Received(1).GetEquipmentTimeUtc();
        }
        else
        {
            // 异步确认 S9F7 触发主消息事务错误，而不是返回伪造的正常时间。
            Func<Task> receive = async () =>
            {
                // 异步等待查询事务的错误结果。
                using var reply = await pendingReply;
            };
            // 异步断言 Illegal Data 错误传播到 Host。
            await receive.Should().ThrowAsync<SecsException>();
            primary.SecondaryMessage!.S.Should().Be(9);
            primary.SecondaryMessage.F.Should().Be(7);
            primary.SecondaryMessage.SecsItem!.Count.Should().Be(10);
            storage.DidNotReceive().GetEquipmentTimeUtc();
        }

        storage.DidNotReceive().GetLastHostTimeUtc();
        storage.DidNotReceive().UpdateHostTime(Arg.Any<DateTime>(), Arg.Any<string>());
    }

    /// <summary>
    /// 无 W-bit 时不发送时间应答；已取消的查询不读取设备时钟。
    /// </summary>
    [Fact]
    public async Task Handle_NoReplyExpectedOrCancelled_DoesNotReadClock()
    {
        var pipe1 = new Pipe(new PipeOptions(useSynchronizationContext: false));
        var pipe2 = new Pipe(new PipeOptions(useSynchronizationContext: false));
        var connection = new PipeConnection(pipe1.Reader, pipe2.Writer);
        using var equipment = new SecsGem(Options.Create(new SecsGemOptions()), connection, Substitute.For<ISecsGemLogger>());
        using var request = new SecsMessage(2, 17, replyExpected: false);
        var primary = new PrimaryMessageWrapper(equipment, request, 1);
        var storage = Substitute.For<ITimeSyncStorage>();

        // 异步验证无 W-bit 不进入应答发送路径。
        await TimeSyncSxFyFunctions.HandleS2F17ReplyAsync(primary, storage);
        primary.SecondaryMessage.Should().BeNull();

        // 异步验证已取消的查询直接结束，不读取或修改设备时间。
        Func<Task> cancelled = () => TimeSyncSxFyFunctions.HandleS2F17ReplyAsync(primary, storage, new CancellationToken(true));
        // 异步断言取消语义。
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        storage.DidNotReceive().GetEquipmentTimeUtc();
        storage.DidNotReceive().UpdateHostTime(Arg.Any<DateTime>(), Arg.Any<string>());
    }
}
