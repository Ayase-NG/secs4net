using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SECShandler.Functions;
using SECShandler.Interfaces;
using Xunit;

namespace Secs4Net.UnitTests;

public class ProcessJobExecutionUnitTests
{
    /// <summary>
    /// 正常执行固定 PJ、Carrier、端口、配方及选槽，成功后才标记 Processing。
    /// </summary>
    [Fact]
    public async Task Execute_ReadyMaterial_StartsAndMarksProcessing()
    {
        var fixture = new Fixture();
        fixture.Client.SelectRecipeAndStartAsync(Arg.Any<ProcessJobExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                fixture.Carrier.State.Should().Be(ProcessJobCarrierState.Starting);
                var request = call.Arg<ProcessJobExecutionRequest>();
                request.PJID.Should().Be("PJ1");
                request.CarrierId.Should().Be("C1");
                request.PortId.Should().Be("2");
                request.RecipeId.Should().Be("RECIPE1");
                request.Mode.Should().Be("00");
                request.LotId.Should().Be("LOT1");
                request.Slots.Should().Equal(1u, 25u);
                return Task.CompletedTask;
            });

        // 异步执行统一流程，确认命令完成之前尚未标记为加工中。
        var started = await fixture.Coordinator.TryExecuteAsync(CancellationToken.None);

        started.Should().BeTrue();
        fixture.Carrier.State.Should().Be(ProcessJobCarrierState.Processing);
        fixture.History.ReceivedCalls().Should().HaveCount(2);
    }

    /// <summary>
    /// 缺少 Mapping、空槽、故障或未知数据均不能领取任务或下发命令。
    /// </summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("short")]
    [InlineData("empty slot")]
    [InlineData("fault")]
    [InlineData("unknown")]
    [InlineData("duplicate carrier")]
    [InlineData("wrong carrier")]
    public async Task Execute_InvalidMaterial_DoesNotStart(string scenario)
    {
        var fixture = new Fixture();
        var map = Enumerable.Repeat((byte)1, 25).ToArray();
        if (scenario == "empty slot") map[0] = 0;
        if (scenario == "fault") map[12] = 2;
        if (scenario == "unknown") map[12] = 3;
        if (scenario == "missing") map = Array.Empty<byte>();
        if (scenario == "short") map = new byte[] { 1 };
        var port = new ProcessJobPortSnapshot("1", scenario == "wrong carrier" ? "OTHER" : "C1", map, "");
        fixture.Materials.GetPorts().Returns(scenario == "duplicate carrier" ? new[] { port, port with { PortId = "2" } } : new[] { port });

        // 异步确认未就绪物料只等待，不执行任何副作用。
        var started = await fixture.Coordinator.TryExecuteAsync(CancellationToken.None);

        started.Should().BeFalse();
        fixture.Carrier.State.Should().Be(ProcessJobCarrierState.Waiting);
        fixture.Client.ReceivedCalls().Should().BeEmpty();
        fixture.Jobs.DidNotReceive().TryBeginCarrierExecution(Arg.Any<string>(), Arg.Any<string>());
    }

    /// <summary>
    /// 手动计划、本地在线以及非空闲设备不能自动执行。
    /// </summary>
    [Theory]
    [InlineData("manual")]
    [InlineData("local")]
    [InlineData("busy")]
    public async Task Execute_NotAuthorizedOrIdle_DoesNotStart(string scenario)
    {
        var fixture = new Fixture();
        if (scenario == "manual") fixture.Plan.AutoStart = false;
        if (scenario == "local") fixture.Device.IsOnline.Returns(DeviceOnlineState.OnLineLocal);
        if (scenario == "busy") fixture.Device.RunStatus.Returns(DeviceRunStatus.Running);

        // 异步检查自动启动及设备门禁。
        (await fixture.Coordinator.TryExecuteAsync(CancellationToken.None)).Should().BeFalse();

        fixture.Client.ReceivedCalls().Should().BeEmpty();
    }

    /// <summary>
    /// RPC 异常后保留待核实状态，后续触发不能自动重发。
    /// </summary>
    [Fact]
    public async Task Execute_ClientFailure_BlocksAutomaticRetry()
    {
        var fixture = new Fixture();
        fixture.Client.SelectRecipeAndStartAsync(Arg.Any<ProcessJobExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new TimeoutException("START outcome unknown")));

        // 异步模拟启动结果不确定。
        (await fixture.Coordinator.TryExecuteAsync(CancellationToken.None)).Should().BeFalse();
        fixture.Carrier.State.Should().Be(ProcessJobCarrierState.StartUncertain);
        // 异步模拟另一个触发点再次进入。
        (await fixture.Coordinator.TryExecuteAsync(CancellationToken.None)).Should().BeFalse();

        fixture.Client.ReceivedCalls().Should().ContainSingle();
    }

    /// <summary>
    /// 设备状态在领取后改变时安全退回原状态，尚未下发命令无需进入待核实。
    /// </summary>
    [Fact]
    public async Task Execute_MaterialDisappearsAfterClaim_ReleasesBeforeCommands()
    {
        var fixture = new Fixture();
        var first = fixture.Materials.GetPorts();
        fixture.Materials.GetPorts().Returns(first, Array.Empty<ProcessJobPortSnapshot>());

        // 异步模拟校验与领取之间发生卸载。
        (await fixture.Coordinator.TryExecuteAsync(CancellationToken.None)).Should().BeFalse();

        fixture.Carrier.State.Should().Be(ProcessJobCarrierState.Waiting);
        fixture.Client.ReceivedCalls().Should().BeEmpty();
    }

    /// <summary>
    /// 异步流程期间重复触发只启动一次，避免同一任务被多个入口领取。
    /// </summary>
    [Fact]
    public async Task Execute_OverlappingTriggers_OnlyOneStarts()
    {
        var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Client.SelectRecipeAndStartAsync(Arg.Any<ProcessJobExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => { entered.SetResult(); return finish.Task; });

        // 异步启动首次执行并等待其进入 RPC 阶段。
        var first = fixture.Coordinator.TryExecuteAsync(CancellationToken.None);
        await entered.Task;
        // 异步模拟 RPC 尚未完成时的另一个触发。
        (await fixture.Coordinator.TryExecuteAsync(CancellationToken.None)).Should().BeFalse();
        finish.SetResult();
        // 异步等待首次执行结束。
        (await first).Should().BeTrue();
        fixture.Client.ReceivedCalls().Should().ContainSingle();
    }

    /// <summary>
    /// 调用取消后保持不确定占用并传播取消，而不是伪装为正常失败后重试。
    /// </summary>
    [Fact]
    public async Task Execute_CancelledDuringCall_PreservesClaim()
    {
        var fixture = new Fixture();
        using var cts = new CancellationTokenSource();
        fixture.Client.SelectRecipeAndStartAsync(Arg.Any<ProcessJobExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => { cts.Cancel(); return Task.FromCanceled(cts.Token); });

        // 异步验证服务停止不会使不确定启动重新变成可领取。
        Func<Task> execute = async () => await fixture.Coordinator.TryExecuteAsync(cts.Token);
        await execute.Should().ThrowAsync<OperationCanceledException>();
        fixture.Carrier.State.Should().Be(ProcessJobCarrierState.StartUncertain);
    }

    /// <summary>
    /// 追溯失败不影响已确认启动，也不能重复下发命令。
    /// </summary>
    [Fact]
    public async Task Execute_HistoryFailure_DoesNotRepeatStart()
    {
        var fixture = new Fixture();
        fixture.History.SaveInteractionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("DB unavailable")));

        // 异步验证日志存储故障不改变设备执行结果。
        (await fixture.Coordinator.TryExecuteAsync(CancellationToken.None)).Should().BeTrue();
        fixture.Carrier.State.Should().Be(ProcessJobCarrierState.Processing);
        fixture.Client.ReceivedCalls().Should().ContainSingle();
    }

    /// <summary>
    /// 构建共享计划与带条件状态更新的替身，隔离 gRPC 传输和实际文件快照。
    /// </summary>
    private sealed class Fixture
    {
        public IJobPlanStorage Jobs { get; } = Substitute.For<IJobPlanStorage>();
        public IProcessJobMaterialSource Materials { get; } = Substitute.For<IProcessJobMaterialSource>();
        public IProcessJobExecutionClient Client { get; } = Substitute.For<IProcessJobExecutionClient>();
        public IDevice Device { get; } = Substitute.For<IDevice>();
        public ISecsInteractionHistoryStore History { get; } = Substitute.For<ISecsInteractionHistoryStore>();
        public ProcessJobPlan Plan { get; } = new() { PJID = "PJ1", RecipeId = "RECIPE1", AutoStart = true };
        public ProcessJobCarrierPlan Carrier { get; } = new() { CarrierId = "C1", Slots = new() { 1, 25 } };
        public ProcessJobExecutionCoordinator Coordinator { get; }

        /// <summary>
        /// 设置合法 Mapping，并按预期状态模拟原子领取和更新。
        /// </summary>
        public Fixture()
        {
            Plan.Carriers.Add(Carrier);
            Jobs.GetAllProcessJobs().Returns(new[] { Plan });
            Jobs.TryBeginCarrierExecution("PJ1", "C1").Returns(_ =>
            {
                if (Carrier.State is not (ProcessJobCarrierState.Waiting or ProcessJobCarrierState.Ready)) return false;
                Carrier.State = ProcessJobCarrierState.Starting;
                return true;
            });
            Jobs.TryUpdateCarrierState("PJ1", "C1", Arg.Any<ProcessJobCarrierState>(), Arg.Any<ProcessJobCarrierState>())
                .Returns(call =>
                {
                    if (Carrier.State != call.ArgAt<ProcessJobCarrierState>(2)) return false;
                    Carrier.State = call.ArgAt<ProcessJobCarrierState>(3);
                    return true;
                });
            Device.IsOnline.Returns(DeviceOnlineState.OnLineRemote);
            Device.RunStatus.Returns(DeviceRunStatus.Idel);
            Device.Mode.Returns("00");
            Materials.GetPorts().Returns(new[] { new ProcessJobPortSnapshot("2", " c1 ", Enumerable.Repeat((byte)1, 25).ToArray(), "LOT1") });
            Coordinator = new ProcessJobExecutionCoordinator(Jobs, Materials, Client, Device, History,
                NullLogger<ProcessJobExecutionCoordinator>.Instance);
        }
    }
}
