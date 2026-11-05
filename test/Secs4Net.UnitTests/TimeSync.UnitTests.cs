using System;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SECSbuilder;
using SECShandler.Functions;
using SECShandler.Interfaces;
using SECSparser;
using Xunit;

namespace Secs4Net.UnitTests;

public class TimeSyncUnitTests
{
    /// <summary>
    /// 验证 SECS 两种 TIME 格式、年份窗口和百分之一秒精度。
    /// </summary>
    [Theory]
    [InlineData("900101000000", "1990-01-01T00:00:00.0000000Z")]
    [InlineData("891231235959", "2089-12-31T23:59:59.0000000Z")]
    [InlineData("000229123456", "2000-02-29T12:34:56.0000000Z")]
    [InlineData("240229123456", "2024-02-29T12:34:56.0000000Z")]
    [InlineData("2024022912345699", "2024-02-29T12:34:56.9900000Z")]
    [InlineData("2024022912345600", "2024-02-29T12:34:56.0000000Z")]
    public void Parse_StandardTime_PreservesRawTextAndUtc(string text, string expected)
    {
        using var message = new SecsMessage(2, 31) { SecsItem = A(text) };

        var data = S2F31_parser.Parse(message);

        data.TimeText.Should().Be(text);
        data.HostTimeUtc.Kind.Should().Be(DateTimeKind.Utc);
        data.HostTimeUtc.ToString("O").Should().Be(expected);
    }

    /// <summary>
    /// 拒绝非标准长度、空白、ISO 时间、非法日期及秒值。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("20240229123456")]
    [InlineData("20240229123456999")]
    [InlineData("2024-02-29T12:34:56Z")]
    [InlineData(" 240229123456")]
    [InlineData("240229123456 ")]
    [InlineData("24022912345A")]
    [InlineData("２４０２２９１２３４５６")]
    [InlineData("230229123456")]
    [InlineData("241301123456")]
    [InlineData("240230123456")]
    [InlineData("240229243456")]
    [InlineData("240229126056")]
    [InlineData("240229123460")]
    [InlineData("1900022912345600")]
    public void Parse_InvalidTime_IsRejected(string text)
    {
        using var message = new SecsMessage(2, 31) { SecsItem = A(text) };

        Action parse = () => S2F31_parser.Parse(message);

        parse.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// 验证 TIME 不携带时区时，可按明确的设备/Host 时区约定转换到 UTC。
    /// </summary>
    [Fact]
    public void Parse_ConfiguredTimeZone_ConvertsToUtc()
    {
        using var message = new SecsMessage(2, 31) { SecsItem = A("2024022912345699") };
        var zone = TimeZoneInfo.CreateCustomTimeZone("HostUtcPlus8", TimeSpan.FromHours(8), "Host", "Host");

        var data = S2F31_parser.Parse(message, zone);

        data.HostTimeUtc.Should().Be(new DateTime(2024, 2, 29, 4, 34, 56, 990, DateTimeKind.Utc));
    }

    /// <summary>
    /// 验证 S2F31 必须包含单个 ASCII 数据项。
    /// </summary>
    [Fact]
    public void Parse_WrongMessageOrItem_IsRejected()
    {
        using var wrongMessage = new SecsMessage(2, 17) { SecsItem = A("240229123456") };
        using var missingItem = new SecsMessage(2, 31);
        using var listItem = new SecsMessage(2, 31) { SecsItem = L(A("240229123456")) };
        using var binaryItem = new SecsMessage(2, 31) { SecsItem = B(0) };

        Action wrongType = () => S2F31_parser.Parse(wrongMessage);
        Action missing = () => S2F31_parser.Parse(missingItem);
        Action list = () => S2F31_parser.Parse(listItem);
        Action binary = () => S2F31_parser.Parse(binaryItem);

        wrongType.Should().Throw<ArgumentException>();
        missing.Should().Throw<InvalidOperationException>();
        list.Should().Throw<InvalidOperationException>();
        binary.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// 验证 S2F32 使用 B[1]，且不要求应答。
    /// </summary>
    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    public void Build_DefinedTiack_ProducesStandardSecondary(byte tiack)
    {
        using var message = S2F32_builder.Build(tiack);

        message.S.Should().Be(2);
        message.F.Should().Be(32);
        message.ReplyExpected.Should().BeFalse();
        message.SecsItem!.Format.Should().Be(SecsFormat.Binary);
        message.SecsItem.Count.Should().Be(1);
        message.SecsItem.FirstValue<byte>().Should().Be(tiack);
    }

    /// <summary>
    /// 禁止使用保留 TIACK 值编码业务错误。
    /// </summary>
    [Theory]
    [InlineData((byte)2)]
    [InlineData((byte)255)]
    public void Build_ReservedTiack_IsRejected(byte tiack)
    {
        Action build = () => S2F32_builder.Build(tiack);

        build.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// 通过实际 SECS 收发验证成功、解析失败和设备设置失败的应答及存储调用。
    /// </summary>
    [Theory]
    [InlineData("2024022912345699", false, (byte)0, true)]
    [InlineData("2024022912345699", true, (byte)1, true)]
    [InlineData("20240229123456", false, (byte)1, false)]
    [InlineData("230229123456", false, (byte)1, false)]
    public async Task Handle_Request_RepliesWithDefinedTiack(string text, bool storageFails, byte expectedTiack, bool updateExpected)
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

        // 模拟设备设置失败，确认不能返回完成或使用保留错误码。
        if (storageFails)
            storage.When(s => s.UpdateHostTime(Arg.Any<DateTime>(), Arg.Any<string>()))
                .Do(_ => throw new InvalidOperationException("Device clock unavailable."));

        hostConnection.Start(cts.Token);
        equipmentConnection.Start(cts.Token);
        using var request = new SecsMessage(2, 31) { SecsItem = A(text) };

        // 异步接收设备端主消息，供共享处理函数应答。
        var incoming = equipment.GetPrimaryMessageAsync(cts.Token).FirstAsync(cts.Token).AsTask();
        // 异步发送 Host 时间设置请求，并等待匹配的 SecondaryMessage。
        var pendingReply = host.SendAsync(request, cts.Token);
        // 异步等待设备接收到请求。
        var primary = await incoming;
        // 异步完成时间设置及应答。
        await TimeSyncSxFyFunctions.HandleS2F31ReplyAsync(primary, storage, cts.Token);
        // 异步取得 Host 收到的应答。
        using var reply = await pendingReply;

        reply.S.Should().Be(2);
        reply.F.Should().Be(32);
        reply.SecsItem!.FirstValue<byte>().Should().Be(expectedTiack);
        storage.Received(updateExpected ? 1 : 0).UpdateHostTime(Arg.Any<DateTime>(), text);
    }

    /// <summary>
    /// 无 W-bit 时仍处理时间设置但不应答；取消的请求不能修改设备时间。
    /// </summary>
    [Theory]
    [InlineData("240229123456", true)]
    [InlineData("230229123456", false)]
    public async Task Handle_NoReplyExpected_ProcessesTimeWithoutReply(string text, bool updateExpected)
    {
        var pipe1 = new Pipe(new PipeOptions(useSynchronizationContext: false));
        var pipe2 = new Pipe(new PipeOptions(useSynchronizationContext: false));
        var connection = new PipeConnection(pipe1.Reader, pipe2.Writer);
        using var equipment = new SecsGem(Options.Create(new SecsGemOptions()), connection, Substitute.For<ISecsGemLogger>());
        using var request = new SecsMessage(2, 31, replyExpected: false) { SecsItem = A(text) };
        var primary = new PrimaryMessageWrapper(equipment, request, 1);
        var storage = Substitute.For<ITimeSyncStorage>();

        // 异步验证无 W-bit 仍处理请求，但不会调用应答发送路径。
        await TimeSyncSxFyFunctions.HandleS2F31ReplyAsync(primary, storage);
        primary.SecondaryMessage.Should().BeNull();
        storage.Received(updateExpected ? 1 : 0).UpdateHostTime(Arg.Any<DateTime>(), text);
        storage.ClearReceivedCalls();

        // 异步验证取消会传播到调用方，并且不设置时钟。
        Func<Task> cancelled = () => TimeSyncSxFyFunctions.HandleS2F31ReplyAsync(primary, storage, new CancellationToken(true));
        // 异步断言取消语义。
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        storage.DidNotReceive().UpdateHostTime(Arg.Any<DateTime>(), Arg.Any<string>());
    }
}
