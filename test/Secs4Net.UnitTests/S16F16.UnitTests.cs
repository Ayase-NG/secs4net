using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SECSbuilder;
using SECSdata;
using SECShandler.Functions;
using SECShandler.Interfaces;
using SECSGrpcService.Services;
using Xunit;

namespace Secs4Net.UnitTests;

public class S16F16UnitTests
{
    /// <summary>
    /// 成功应答编码作业标识列表及 Boolean true，错误列表必须为空。
    /// </summary>
    [Fact]
    public void Build_Accepted_EncodesJobIdsAndBoolean()
    {
        var data = new S16F16_data { ACKA = true };
        data.PRJOBIDs.AddRange(new[] { "PJ1", "PJ2" });
        using var reply = S16F16_builder.Build(data);

        reply.S.Should().Be(16);
        reply.F.Should().Be(16);
        reply.ReplyExpected.Should().BeFalse();
        var root = reply.SecsItem!;
        root.Format.Should().Be(SecsFormat.List);
        root.Count.Should().Be(2);
        root[0].Items.Select(item => item.GetString()).Should().Equal("PJ1", "PJ2");
        root[1].Count.Should().Be(2);
        root[1][0].Format.Should().Be(SecsFormat.Boolean);
        root[1][0].Count.Should().Be(1);
        root[1][0].FirstValue<bool>().Should().BeTrue();
        root[1][1].Format.Should().Be(SecsFormat.List);
        root[1][1].Count.Should().Be(0);
    }

    /// <summary>
    /// 拒绝应答使用 Boolean false，每条错误编码 U2 ERRCODE 和 ASCII ERRTEXT。
    /// </summary>
    [Fact]
    public void Build_Rejected_EncodesErrorsAndConfirmedIds()
    {
        var data = new S16F16_data();
        data.PRJOBIDs.Add("PJ1");
        data.Errors.Add(new S16F16_error_data { ERRCODE = 1, ERRTEXT = "PJ2 failed." });
        data.Errors.Add(new S16F16_error_data { ERRCODE = 65535, ERRTEXT = "Detailed error." });
        using var reply = S16F16_builder.Build(data);

        var root = reply.SecsItem!;
        root[0][0].GetString().Should().Be("PJ1");
        root[1][0].FirstValue<bool>().Should().BeFalse();
        root[1][1].Count.Should().Be(2);
        root[1][1][0].Count.Should().Be(2);
        root[1][1][0][0].Format.Should().Be(SecsFormat.U2);
        root[1][1][0][0].Count.Should().Be(1);
        root[1][1][0][0].FirstValue<ushort>().Should().Be(1);
        root[1][1][0][1].Format.Should().Be(SecsFormat.ASCII);
        root[1][1][0][1].GetString().Should().Be("PJ2 failed.");
        root[1][1][1][0].FirstValue<ushort>().Should().Be(65535);
    }

    /// <summary>
    /// 旧内部字节确认值只作为兼容入口，在线上必须转换为 Boolean。
    /// </summary>
    [Theory]
    [InlineData((byte)0, true)]
    [InlineData((byte)1, false)]
    public void Build_LegacyAck_ConvertsToStandardBody(byte ack, bool accepted)
    {
        var data = new S16F16_data { ACK = ack };
        data.ACKA.Should().Be(accepted);
        data.IsAccepted.Should().Be(accepted);
        using var reply = S16F16_builder.Build(ack);
        reply.SecsItem![1][0].FirstValue<bool>().Should().Be(accepted);
        reply.SecsItem[1][1].Count.Should().Be(accepted ? 0 : 1);
    }

    /// <summary>
    /// 默认模型为拒绝，禁止无错误描述的失败、成功与错误并存及空作业标识。
    /// </summary>
    [Theory]
    [InlineData("missing error")]
    [InlineData("accepted with error")]
    [InlineData("empty ID")]
    [InlineData("empty description")]
    public void Build_InconsistentModel_IsRejected(string scenario)
    {
        var data = new S16F16_data();
        data.IsAccepted.Should().BeFalse();
        // 根据场景构造互相矛盾或缺少信息的应答。
        if (scenario == "accepted with error")
        {
            data.ACKA = true;
            data.Errors.Add(new S16F16_error_data { ERRTEXT = "error" });
        }
        else if (scenario == "empty ID")
        {
            data.ACKA = true;
            data.PRJOBIDs.Add(" ");
        }
        else if (scenario == "empty description")
        {
            data.Errors.Add(new S16F16_error_data());
        }

        Action build = () => S16F16_builder.Build(data);
        build.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// 自定义字节值不能作为 ACKA 隐式发送。
    /// </summary>
    [Fact]
    public void Build_InvalidLegacyAck_IsRejected()
    {
        Action build = () => S16F16_builder.Build((byte)3);
        build.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// 真正创建成功后，返回本批次全部 PJID。
    /// </summary>
    [Fact]
    public async Task Handle_CreatedJobs_ReturnsTheirIds()
    {
        var jobs = Substitute.For<IJobPlanStorage>();
        // 异步完成两项 PJ 创建并取得 Host 端应答。
        using var reply = await HandleAsync(L(BuildJob("PJ1"), BuildJob("PJ2")), jobs);

        reply.SecsItem![0].Items.Select(item => item.GetString()).Should().Equal("PJ1", "PJ2");
        reply.SecsItem[1][0].FirstValue<bool>().Should().BeTrue();
        reply.SecsItem[1][1].Count.Should().Be(0);
        jobs.Received(2).UpsertProcessJob(Arg.Any<ProcessJobPlan>());
    }

    /// <summary>
    /// 保存中途失败时只列出已确认的 PJID，并返回失败和真实错误原因。
    /// </summary>
    [Fact]
    public async Task Handle_PartialSaveFailure_ReturnsOnlyConfirmedIds()
    {
        var jobs = Substitute.For<IJobPlanStorage>();
        jobs.When(storage => storage.UpsertProcessJob(Arg.Is<ProcessJobPlan>(plan => plan.PJID == "PJ2")))
            .Do(_ => throw new InvalidOperationException("PJ2 storage unavailable."));
        var guard = new InMemoryIdempotencyGuard();
        // 异步模拟第一个保存成功、第二个保存失败。
        using var reply = await HandleAsync(L(BuildJob("PJ1"), BuildJob("PJ2")), jobs, guard);

        reply.SecsItem![0].Items.Select(item => item.GetString()).Should().Equal("PJ1");
        reply.SecsItem[1][0].FirstValue<bool>().Should().BeFalse();
        reply.SecsItem[1][1][0][1].GetString().Should().Contain("PJ2 storage unavailable");
        guard.TryBegin("S16F15", "1:PJ1,PJ2", TimeSpan.FromSeconds(30)).Should().BeTrue();
    }

    /// <summary>
    /// 去重命中且没有保存结果时不能返回创建成功，也不能删除其他请求的占用。
    /// </summary>
    [Fact]
    public async Task Handle_DuplicateWhileUnconfirmed_ReturnsError()
    {
        var jobs = Substitute.For<IJobPlanStorage>();
        var guard = new InMemoryIdempotencyGuard();
        guard.TryBegin("S16F15", "1:PJ1", TimeSpan.FromSeconds(30), out var token).Should().BeTrue();
        // 异步模拟首次请求尚未保存时的重复创建。
        using var reply = await HandleAsync(L(BuildJob("PJ1")), jobs, guard);

        reply.SecsItem![0].Count.Should().Be(0);
        reply.SecsItem[1][0].FirstValue<bool>().Should().BeFalse();
        reply.SecsItem[1][1][0][1].GetString().Should().Contain("already being processed");
        jobs.DidNotReceive().UpsertProcessJob(Arg.Any<ProcessJobPlan>());
        guard.TryRemove("S16F15", "1:PJ1", token).Should().BeTrue();
    }

    /// <summary>
    /// 已确认保存的相同 PJ 重传返回成功标识，但不重置其执行状态。
    /// </summary>
    [Fact]
    public async Task Handle_DuplicateWithConfirmedPlan_ReturnsSuccess()
    {
        var jobs = Substitute.For<IJobPlanStorage>();
        jobs.TryGetProcessJob("PJ1", out Arg.Any<ProcessJobPlan>()).Returns(call =>
        {
            call[1] = new ProcessJobPlan
            {
                PJID = "PJ1", RecipeId = "RECIPE1", AutoStart = true, AutoStarted = true,
                Carriers = new List<ProcessJobCarrierPlan> { new() { CarrierId = "CARRIER1", Slots = new List<uint> { 1 } } }
            };
            return true;
        });
        var guard = new InMemoryIdempotencyGuard();
        guard.TryBegin("S16F15", "1:PJ1", TimeSpan.FromSeconds(30)).Should().BeTrue();
        // 异步模拟已有成功计划的重复请求。
        using var reply = await HandleAsync(L(BuildJob("PJ1")), jobs, guard);

        reply.SecsItem![0][0].GetString().Should().Be("PJ1");
        reply.SecsItem[1][0].FirstValue<bool>().Should().BeTrue();
        reply.SecsItem[1][1].Count.Should().Be(0);
        jobs.DidNotReceive().UpsertProcessJob(Arg.Any<ProcessJobPlan>());
    }

    /// <summary>
    /// 实际发送后保存结构化设备确认，不把 Boolean ACKA 写入 HCACK 字段。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_SentReply_PersistsStructuredAcknowledgement(bool accepted)
    {
        var history = Substitute.For<ISecsInteractionHistoryStore>();
        // 异步完成合法或空批次请求，并记录真实的应答结果。
        using var reply = await HandleAsync(accepted ? L(BuildJob("PJ1")) : L(),
            Substitute.For<IJobPlanStorage>(), history: history);

        var call = history.ReceivedCalls().Should().ContainSingle().Subject;
        var args = call.GetArguments();
        args[0].Should().Be("S16F16");
        args[2].Should().BeNull();
        using var json = JsonDocument.Parse((string)args[1]!);
        json.RootElement.GetProperty("ACKA").GetBoolean().Should().Be(accepted);
        json.RootElement.GetProperty("PRJOBIDs").GetArrayLength().Should().Be(accepted ? 1 : 0);
        json.RootElement.GetProperty("Errors").GetArrayLength().Should().Be(accepted ? 0 : 1);
    }

    /// <summary>
    /// 追溯存储失败不能造成第二次保存 PJ 或改变已经发送的成功结果。
    /// </summary>
    [Fact]
    public async Task Handle_HistoryFailure_DoesNotRepeatCreation()
    {
        var history = Substitute.For<ISecsInteractionHistoryStore>();
        history.SaveInteractionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte?>(),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("History unavailable.")));
        var jobs = Substitute.For<IJobPlanStorage>();
        // 异步验证应答发送后的追溯异常不会被当成业务创建失败。
        using var reply = await HandleAsync(L(BuildJob("PJ1")), jobs, history: history);

        reply.SecsItem![1][0].FirstValue<bool>().Should().BeTrue();
        jobs.Received(1).UpsertProcessJob(Arg.Any<ProcessJobPlan>());
        history.ReceivedCalls().Should().ContainSingle();
    }

    /// <summary>
    /// 无 W-bit 时创建仍生效，但不得发送 S16F16。
    /// </summary>
    [Fact]
    public async Task Handle_NoReplyExpected_DoesNotSendSecondary()
    {
        using var recipes = new RecipeDirectoryFixture("RECIPE1");
        var pipe1 = new Pipe(new PipeOptions(useSynchronizationContext: false));
        var pipe2 = new Pipe(new PipeOptions(useSynchronizationContext: false));
        var connection = new PipeConnection(pipe1.Reader, pipe2.Writer);
        using var equipment = new SecsGem(Options.Create(new SecsGemOptions()), connection, Substitute.For<ISecsGemLogger>());
        using var request = new SecsMessage(16, 15, replyExpected: false) { SecsItem = L(U4(1), L(BuildJob("PJ1"))) };
        var device = Substitute.For<IDevice>();
        device.IsOnline.Returns(DeviceOnlineState.OnLineRemote);
        var primary = new PrimaryMessageWrapper(equipment, request, 1);
        var jobs = Substitute.For<IJobPlanStorage>();
        // 异步验证无应答请求不进入发送路径。
        await RemoteCommandSxFyFunctions.HandleS16F15Async(equipment, primary, device,
            Substitute.For<ICommandDispatcher>(), new InMemoryIdempotencyGuard(),
            Substitute.For<IPortContextStorage>(), jobs, CancellationToken.None, recipes.DirectoryPath);

        primary.SecondaryMessage.Should().BeNull();
        jobs.Received(1).UpsertProcessJob(Arg.Any<ProcessJobPlan>());
    }

    /// <summary>
    /// 构建一个满足当前创建业务约束的 PJ。
    /// </summary>
    private static Item BuildJob(string id) => L(A(id), B(13), L(L(A("CARRIER1"), L(U1(1)))),
        L(U1(1), A("RECIPE1"), L()), Boolean(true), L());

    /// <summary>
    /// 使用隔离配方目录和实际内存 SECS 收发取得结构化创建应答。
    /// </summary>
    private static async Task<SecsMessage> HandleAsync(Item batch, IJobPlanStorage jobs, IIdempotencyGuard? guard = null,
        ISecsInteractionHistoryStore? history = null)
    {
        using var recipes = new RecipeDirectoryFixture("RECIPE1");
        var pipe1 = new Pipe(new PipeOptions(useSynchronizationContext: false));
        var pipe2 = new Pipe(new PipeOptions(useSynchronizationContext: false));
        var hostConnection = new PipeConnection(pipe1.Reader, pipe2.Writer);
        var equipmentConnection = new PipeConnection(pipe2.Reader, pipe1.Writer);
        var options = Options.Create(new SecsGemOptions { DeviceId = 0, T3 = 5000 });
        using var host = new SecsGem(options, hostConnection, Substitute.For<ISecsGemLogger>());
        using var equipment = new SecsGem(options, equipmentConnection, Substitute.For<ISecsGemLogger>());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var request = new SecsMessage(16, 15) { SecsItem = L(U4(1), batch) };
        var device = Substitute.For<IDevice>();
        device.IsOnline.Returns(DeviceOnlineState.OnLineRemote);
        hostConnection.Start(cts.Token);
        equipmentConnection.Start(cts.Token);
        // 异步等待 Equipment 接收到创建请求。
        var incoming = equipment.GetPrimaryMessageAsync(cts.Token).FirstAsync(cts.Token).AsTask();
        // 异步发起 Host 创建事务。
        var pendingReply = host.SendAsync(request, cts.Token);
        // 异步取得原请求包装器。
        var primary = await incoming;
        // 异步执行业务处理和应答。
        await RemoteCommandSxFyFunctions.HandleS16F15Async(equipment, primary, device,
            Substitute.For<ICommandDispatcher>(), guard ?? new InMemoryIdempotencyGuard(),
            Substitute.For<IPortContextStorage>(), jobs, cts.Token, recipes.DirectoryPath, history);
        // 异步返回 Host 解码后的应答，由测试调用方负责释放。
        return await pendingReply;
    }
}
