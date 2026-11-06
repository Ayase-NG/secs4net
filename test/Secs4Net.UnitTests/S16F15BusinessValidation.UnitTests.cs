using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SECShandler.Functions;
using SECShandler.Interfaces;
using SECSGrpcService.Services;
using Xunit;

namespace Secs4Net.UnitTests;

public class S16F15BusinessValidationUnitTests
{
    /// <summary>
    /// 每种业务错误均应拒绝整批，不能写入部分有效计划或占用去重键。
    /// </summary>
    [Theory]
    [InlineData("empty batch")]
    [InlineData("empty PJID")]
    [InlineData("duplicate PJID")]
    [InlineData("empty recipe")]
    [InlineData("unsupported MF")]
    [InlineData("empty carriers")]
    [InlineData("empty CarrierID")]
    [InlineData("duplicate CarrierID")]
    [InlineData("empty slots")]
    [InlineData("zero slot")]
    [InlineData("duplicate slot")]
    [InlineData("pause requested")]
    [InlineData("later invalid PJ")]
    public async Task Handle_InvalidBusinessParameters_RejectsWithoutWriting(string scenario)
    {
        var jobs = Substitute.For<IJobPlanStorage>();
        var guard = Substitute.For<IIdempotencyGuard>();
        var batch = scenario switch
        {
            "empty batch" => L(),
            "empty PJID" => L(BuildJob(id: " ")),
            "duplicate PJID" => L(BuildJob(id: "PJ1"), BuildJob(id: " pj1 ")),
            "empty recipe" => L(BuildJob(recipe: " ")),
            "unsupported MF" => L(BuildJob(materialFormat: B(1))),
            "empty carriers" => L(BuildJob(carriers: L())),
            "empty CarrierID" => L(BuildJob(carriers: L(L(A(" "), L(U1(1)))))),
            "duplicate CarrierID" => L(BuildJob(carriers: L(L(A("CARRIER1"), L(U1(1))), L(A(" carrier1 "), L(U1(2)))))),
            "empty slots" => L(BuildJob(carriers: L(L(A("CARRIER1"), L())))),
            "zero slot" => L(BuildJob(carriers: L(L(A("CARRIER1"), L(U1(0)))))),
            "duplicate slot" => L(BuildJob(carriers: L(L(A("CARRIER1"), L(U1(1), U4(1)))))),
            "pause requested" => L(BuildJob(pause: L(U4(100)))),
            _ => L(BuildJob(id: "VALID"), BuildJob(id: "INVALID", recipe: ""))
        };

        // 异步通过真实 SECS 事务验证业务拒绝应答。
        var ack = await HandleAsync(batch, jobs, guard);

        ack.Should().Be(1);
        jobs.DidNotReceive().UpsertProcessJob(Arg.Any<ProcessJobPlan>());
        guard.ReceivedCalls().Should().BeEmpty();
    }

    /// <summary>
    /// 创建只要求远程在线，不能在本地在线或离线状态接受计划。
    /// </summary>
    [Theory]
    [InlineData(DeviceOnlineState.OffLine)]
    [InlineData(DeviceOnlineState.OnLineLocal)]
    public async Task Handle_NotRemote_RejectsBeforeGuard(DeviceOnlineState state)
    {
        var jobs = Substitute.For<IJobPlanStorage>();
        var guard = Substitute.For<IIdempotencyGuard>();
        // 异步验证控制状态校验。
        var ack = await HandleAsync(L(BuildJob()), jobs, guard, state);

        ack.Should().Be(1);
        jobs.DidNotReceive().UpsertProcessJob(Arg.Any<ProcessJobPlan>());
        guard.ReceivedCalls().Should().BeEmpty();
    }

    /// <summary>
    /// 合法计划应规范化文本但保留槽位定义；手动启动与非空配方参数均不额外限制。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_ValidPlan_SavesAndLeavesRecipeEditsReserved(bool autoStart)
    {
        var jobs = Substitute.For<IJobPlanStorage>();
        ProcessJobPlan? saved = null;
        jobs.When(storage => storage.UpsertProcessJob(Arg.Any<ProcessJobPlan>()))
            .Do(call => saved = call.Arg<ProcessJobPlan>());
        var guard = new InMemoryIdempotencyGuard();
        var batch = L(BuildJob(id: " PJ1 ", recipe: " RECIPE1 ", materialFormat: A("0x0d"),
            carriers: L(L(A(" CARRIER1 "), L(U4(25), U1(1)))), autoStart: autoStart,
            parameters: L(L(A("future parameter"), L(U4(123), A("value"))))));

        // 异步创建合法 PJ，不以物料尚未到达为拒绝条件。
        var ack = await HandleAsync(batch, jobs, guard);

        ack.Should().Be(0);
        saved.Should().NotBeNull();
        saved!.PJID.Should().Be("PJ1");
        saved.RecipeId.Should().Be("RECIPE1");
        saved.AutoStart.Should().Be(autoStart);
        saved.Carriers[0].CarrierId.Should().Be("CARRIER1");
        saved.Carriers[0].Slots.Should().Equal(25u, 1u);
        saved.PauseEvents.Should().BeEmpty();
        guard.TryBegin("S16F15", "1:PJ1", TimeSpan.FromSeconds(30)).Should().BeFalse();
    }

    /// <summary>
    /// 固定槽位范围为 1～25，边界值有效，越界时整批拒绝且不保存前面的合法 PJ。
    /// </summary>
    [Theory]
    [InlineData(0u, false)]
    [InlineData(1u, true)]
    [InlineData(25u, true)]
    [InlineData(26u, false)]
    [InlineData(uint.MaxValue, false)]
    public async Task Handle_PhysicalSlotRange_AcceptsOnlyOneThroughTwentyFive(uint slot, bool accepted)
    {
        var jobs = Substitute.For<IJobPlanStorage>();
        var batch = L(BuildJob(id: "FIRST"),
            BuildJob(id: "SECOND", carriers: L(L(A("CARRIER2"), L(U4(slot))))));

        // 异步验证 Mapping 未完成也可以创建合法计划，但不能接受物理范围之外的槽位。
        var ack = await HandleAsync(batch, jobs);

        ack.Should().Be(accepted ? (byte)0 : (byte)1);
        jobs.Received(accepted ? 2 : 0).UpsertProcessJob(Arg.Any<ProcessJobPlan>());
    }

    /// <summary>
    /// 已有相同定义是重传，不覆盖已启动信息；槽位顺序不影响定义一致性。
    /// </summary>
    [Fact]
    public async Task Handle_IdenticalExistingPlan_PreservesExecutionState()
    {
        var jobs = Substitute.For<IJobPlanStorage>();
        var existing = ExistingPlan();
        jobs.TryGetProcessJob("PJ1", out Arg.Any<ProcessJobPlan>()).Returns(call =>
        {
            call[1] = existing;
            return true;
        });

        // 异步验证重复创建不会重置已有执行记录。
        var ack = await HandleAsync(L(BuildJob(carriers: L(L(A("CARRIER1"), L(U1(2), U1(1)))))), jobs);

        ack.Should().Be(0);
        jobs.DidNotReceive().UpsertProcessJob(Arg.Any<ProcessJobPlan>());
        existing.AutoStarted.Should().BeTrue();
        existing.AutoStartedPortId.Should().Be("PORT1");
        existing.AutoStartedLotId.Should().Be("LOT1");
    }

    /// <summary>
    /// 相同 PJID 的不同计划不能经创建请求覆盖，且整批的其他新 PJ 也不能先写入。
    /// </summary>
    [Theory]
    [InlineData("recipe")]
    [InlineData("autoStart")]
    [InlineData("slots")]
    [InlineData("carrier")]
    public async Task Handle_ConflictingExistingPlan_RejectsWholeBatch(string difference)
    {
        var jobs = Substitute.For<IJobPlanStorage>();
        var guard = Substitute.For<IIdempotencyGuard>();
        var existing = ExistingPlan();
        // 模拟冲突发生在批次第二个 PJ，验证第一个新 PJ 不会提前落入存储。
        jobs.TryGetProcessJob("PJ1", out Arg.Any<ProcessJobPlan>()).Returns(call =>
        {
            call[1] = existing;
            return true;
        });
        var changed = difference switch
        {
            "recipe" => BuildJob(recipe: "ANOTHER_RECIPE"),
            "autoStart" => BuildJob(autoStart: false),
            "slots" => BuildJob(carriers: L(L(A("CARRIER1"), L(U1(1))))),
            _ => BuildJob(carriers: L(L(A("ANOTHER_CARRIER"), L(U1(1), U1(2)))))
        };

        // 异步验证整批冲突检查先于任何保存。
        var ack = await HandleAsync(L(BuildJob(id: "NEW"), changed), jobs, guard);

        ack.Should().Be(1);
        jobs.DidNotReceive().UpsertProcessJob(Arg.Any<ProcessJobPlan>());
        guard.ReceivedCalls().Should().BeEmpty();
        existing.AutoStarted.Should().BeTrue();
    }

    /// <summary>
    /// 即使相同 DATAID/PJID 已命中去重键，新的非法参数仍须拒绝，且不删除旧占用。
    /// </summary>
    [Fact]
    public async Task Handle_InvalidParametersWithOccupiedKey_DoesNotReturnSuccess()
    {
        var guard = new InMemoryIdempotencyGuard();
        guard.TryBegin("S16F15", "1:PJ1", TimeSpan.FromSeconds(30), out var token).Should().BeTrue();
        var jobs = Substitute.For<IJobPlanStorage>();

        // 异步验证业务校验发生在短期去重分支之前。
        var ack = await HandleAsync(L(BuildJob(recipe: "")), jobs, guard);

        ack.Should().Be(1);
        jobs.DidNotReceive().UpsertProcessJob(Arg.Any<ProcessJobPlan>());
        guard.TryRemove("S16F15", "1:PJ1", token).Should().BeTrue();
    }

    /// <summary>
    /// 一级配方文件夹缺失时拒绝整批，文件、嵌套目录和路径文本不能作为 PPID 命中。
    /// </summary>
    [Theory]
    [InlineData("MISSING")]
    [InlineData("FILE_ONLY")]
    [InlineData("NESTED_ONLY")]
    [InlineData("PARENT/NESTED_ONLY")]
    [InlineData(@"..\OUTSIDE")]
    [InlineData(@"D:\OUTSIDE")]
    public async Task Handle_RecipeNotInTopLevelDirectories_RejectsWholeBatch(string missingRecipe)
    {
        using var recipes = new RecipeDirectoryFixture("RECIPE1", Path.Combine("PARENT", "NESTED_ONLY"));
        File.WriteAllText(Path.Combine(recipes.DirectoryPath, "FILE_ONLY"), "not a recipe directory");
        var jobs = Substitute.For<IJobPlanStorage>();
        var guard = Substitute.For<IIdempotencyGuard>();

        // 异步验证第一个 PJ 配方存在、第二个缺失时也不能保存部分批次。
        var ack = await HandleAsync(L(BuildJob(id: "VALID"), BuildJob(id: "INVALID", recipe: missingRecipe)),
            jobs, guard, recipeRootPath: recipes.DirectoryPath);

        ack.Should().Be(1);
        jobs.DidNotReceive().UpsertProcessJob(Arg.Any<ProcessJobPlan>());
        guard.ReceivedCalls().Should().BeEmpty();
    }

    /// <summary>
    /// 根目录不存在或实际为文件时不能跳过配方校验。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_InvalidRecipeRoot_RejectsWithoutWriting(bool rootIsFile)
    {
        using var recipes = new RecipeDirectoryFixture();
        var root = Path.Combine(recipes.DirectoryPath, "unavailable");
        // 构造无法枚举为目录的根路径。
        if (rootIsFile)
            File.WriteAllText(root, "file");
        var jobs = Substitute.For<IJobPlanStorage>();
        var guard = Substitute.For<IIdempotencyGuard>();

        // 异步确认根目录故障返回拒绝，而非默认接受未知配方。
        var ack = await HandleAsync(L(BuildJob()), jobs, guard, recipeRootPath: root);

        ack.Should().Be(1);
        jobs.DidNotReceive().UpsertProcessJob(Arg.Any<ProcessJobPlan>());
        guard.ReceivedCalls().Should().BeEmpty();
    }

    /// <summary>
    /// 沿用 Windows 文件夹名的大小写不敏感语义，PPID 原文仍保存在计划中。
    /// </summary>
    [Fact]
    public async Task Handle_ExistingRecipeDirectory_AcceptsCaseInsensitiveName()
    {
        using var recipes = new RecipeDirectoryFixture("recipe1");
        var jobs = Substitute.For<IJobPlanStorage>();

        // 异步验证 PPID 匹配一级文件夹后可以正常创建。
        var ack = await HandleAsync(L(BuildJob()), jobs, recipeRootPath: recipes.DirectoryPath);

        ack.Should().Be(0);
        jobs.Received(1).UpsertProcessJob(Arg.Is<ProcessJobPlan>(plan => plan.RecipeId == "RECIPE1"));
    }

    /// <summary>
    /// 配方删除后立即生效，即使短期去重记录仍存在也不能提前返回成功。
    /// </summary>
    [Fact]
    public async Task Handle_RecipeDeletedAfterFirstRequest_RejectsRepeat()
    {
        using var recipes = new RecipeDirectoryFixture("RECIPE1");
        var jobs = Substitute.For<IJobPlanStorage>();
        var guard = new InMemoryIdempotencyGuard();
        // 异步验证配方存在时首次创建成功。
        var firstAck = await HandleAsync(L(BuildJob()), jobs, guard, recipeRootPath: recipes.DirectoryPath);
        firstAck.Should().Be(0);
        Directory.Delete(Path.Combine(recipes.DirectoryPath, "RECIPE1"));

        // 异步验证重复请求仍先校验最新的配方目录，而不是只根据 Guard 确认成功。
        var secondAck = await HandleAsync(L(BuildJob()), jobs, guard, recipeRootPath: recipes.DirectoryPath);

        secondAck.Should().Be(1);
        jobs.Received(1).UpsertProcessJob(Arg.Any<ProcessJobPlan>());
    }

    /// <summary>
    /// 构建合法六字段 PJ，只覆盖指定的业务参数；配方参数始终保持结构合法。
    /// </summary>
    private static Item BuildJob(string id = "PJ1", string recipe = "RECIPE1", Item? materialFormat = null,
        Item? carriers = null, bool autoStart = true, Item? pause = null, Item? parameters = null)
    {
        return L(A(id), materialFormat ?? B(13), carriers ?? L(L(A("CARRIER1"), L(U1(1), U1(2)))),
            L(U1(1), A(recipe), parameters ?? L()), Boolean(autoStart), pause ?? L());
    }

    /// <summary>
    /// 已启动的计划用于验证创建重传不能重置运行信息。
    /// </summary>
    private static ProcessJobPlan ExistingPlan() => new()
    {
        PJID = "PJ1", RecipeId = "RECIPE1", AutoStart = true,
        Carriers = new List<ProcessJobCarrierPlan> { new() { CarrierId = "CARRIER1", Slots = new List<uint> { 1, 2 } } },
        AutoStarted = true, AutoStartedPortId = "PORT1", AutoStartedLotId = "LOT1"
    };

    /// <summary>
    /// 使用真实内存 SECS 收发执行业务处理，并确认创建不会调用设备命令或读取到达上下文。
    /// </summary>
    private static async Task<byte> HandleAsync(Item batch, IJobPlanStorage jobs, IIdempotencyGuard? guard = null,
        DeviceOnlineState state = DeviceOnlineState.OnLineRemote, string? recipeRootPath = null)
    {
        using var defaultRecipes = recipeRootPath is null
            ? new RecipeDirectoryFixture("RECIPE1", "ANOTHER_RECIPE")
            : null;
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
        device.IsOnline.Returns(state);
        var dispatcher = Substitute.For<ICommandDispatcher>();
        var ports = Substitute.For<IPortContextStorage>();
        hostConnection.Start(cts.Token);
        equipmentConnection.Start(cts.Token);
        // 异步接收设备端创建请求。
        var incoming = equipment.GetPrimaryMessageAsync(cts.Token).FirstAsync(cts.Token).AsTask();
        // 异步发送 Host 请求并等待匹配应答。
        var pendingReply = host.SendAsync(request, cts.Token);
        // 异步取得当前 SECS 事务。
        var primary = await incoming;
        // 异步执行校验、计划保存和应答。
        await RemoteCommandSxFyFunctions.HandleS16F15Async(equipment, primary, device, dispatcher,
            guard ?? new InMemoryIdempotencyGuard(), ports, jobs, cts.Token,
            recipeRootPath ?? defaultRecipes!.DirectoryPath);
        // 异步读取 Host 收到的创建结果。
        using var reply = await pendingReply;
        dispatcher.ReceivedCalls().Should().BeEmpty();
        ports.ReceivedCalls().Should().BeEmpty();
        return reply.SecsItem!.FirstValue<byte>();
    }
}
