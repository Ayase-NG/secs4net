using System;
using System.Collections.Generic;
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

public class JobIdempotencyReleaseUnitTests
{
    /// <summary>
    /// 两种创建请求在离线或存储异常时释放占用，在成功保存后保留占用。
    /// </summary>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, true, false)]
    public async Task Handle_ReleaseDependsOnBusinessAck(bool processJob, bool online, bool storageFails)
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
        var device = Substitute.For<IDevice>();
        device.IsOnline.Returns(online ? DeviceOnlineState.OnLineRemote : DeviceOnlineState.OffLine);
        var jobs = Substitute.For<IJobPlanStorage>();
        // CJ 测试需要已有 PJ；PJ 创建测试则必须模拟尚未存在的新作业。
        if (!processJob)
        {
            jobs.TryGetProcessJob("PJ1", out Arg.Any<ProcessJobPlan>()).Returns(call =>
            {
                call[1] = new ProcessJobPlan
                {
                    PJID = "PJ1",
                    Carriers = new List<ProcessJobCarrierPlan> { new() { CarrierId = "CARRIER1" } }
                };
                return true;
            });
        }
        var ports = Substitute.For<IPortContextStorage>();
        ports.GetAll().Returns(Array.Empty<PortRuntimeContext>());
        var guard = new InMemoryIdempotencyGuard();
        var scope = processJob ? "S16F15" : "S14F9";
        var key = processJob ? "1:PJ1" : "Equipment:ControlJob:CJ1";

        // 模拟业务写入异常，而不是应答传输异常。
        if (storageFails)
        {
            jobs.When(storage => storage.UpsertProcessJob(Arg.Any<ProcessJobPlan>()))
                .Do(_ => throw new InvalidOperationException("Save failed."));
            jobs.When(storage => storage.UpsertControlJob(Arg.Any<ControlJobPlan>()))
                .Do(_ => throw new InvalidOperationException("Save failed."));
        }

        using var request = BuildRequest(processJob);
        hostConnection.Start(cts.Token);
        equipmentConnection.Start(cts.Token);
        // 异步接收设备端创建请求。
        var incoming = equipment.GetPrimaryMessageAsync(cts.Token).FirstAsync(cts.Token).AsTask();
        // 异步发送 Host 请求并等待匹配应答。
        var pendingReply = host.SendAsync(request, cts.Token);
        // 异步取得当前事务包装器。
        var primary = await incoming;

        // 分别验证两个创建处理函数的释放路径。
        if (processJob)
        {
            // 异步创建 PJ 或返回拒绝，并在回包前决定是否释放去重记录。
            await RemoteCommandSxFyFunctions.HandleS16F15Async(equipment, primary, device,
                Substitute.For<ICommandDispatcher>(), guard, ports, jobs, cts.Token, recipes.DirectoryPath);
        }
        else
        {
            // 异步创建 CJ 或返回拒绝，并在回包前决定是否释放去重记录。
            await RemoteCommandSxFyFunctions.HandleS14F9Async(equipment, primary, device,
                guard, ports, jobs, cts.Token);
        }

        // 异步取得 Host 收到的业务 ACK。
        using var reply = await pendingReply;
        var succeeded = online && !storageFails;
        reply.SecsItem!.FirstValue<byte>().Should().Be(succeeded ? (byte)0 : (byte)1);
        guard.TryBegin(scope, key, TimeSpan.FromSeconds(30)).Should().Be(!succeeded);
    }

    /// <summary>
    /// 解析失败未占用键，不得删除已有其他请求的记录。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_ParseFailure_DoesNotRemoveExistingEntry(bool processJob)
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
        var guard = new InMemoryIdempotencyGuard();
        var scope = processJob ? "S16F15" : "S14F9";
        var key = processJob ? "1:PJ1" : "Equipment:ControlJob:CJ1";
        guard.TryBegin(scope, key, TimeSpan.FromSeconds(30), out var token).Should().BeTrue();
        using var request = new SecsMessage(processJob ? (byte)16 : (byte)14, processJob ? (byte)15 : (byte)9)
        {
            SecsItem = A("invalid")
        };
        hostConnection.Start(cts.Token);
        equipmentConnection.Start(cts.Token);
        // 异步接收无效的创建请求。
        var incoming = equipment.GetPrimaryMessageAsync(cts.Token).FirstAsync(cts.Token).AsTask();
        // 异步发送请求并等待拒绝应答。
        var pendingReply = host.SendAsync(request, cts.Token);
        // 异步取得设备端主消息。
        var primary = await incoming;

        // 两个处理函数解析失败时都不拥有删除令牌。
        if (processJob)
        {
            // 异步处理无效 PJ 创建请求。
            await RemoteCommandSxFyFunctions.HandleS16F15Async(equipment, primary, Substitute.For<IDevice>(),
                Substitute.For<ICommandDispatcher>(), guard, Substitute.For<IPortContextStorage>(),
                Substitute.For<IJobPlanStorage>(), cts.Token, recipes.DirectoryPath);
        }
        else
        {
            // 异步处理无效 CJ 创建请求。
            await RemoteCommandSxFyFunctions.HandleS14F9Async(equipment, primary, Substitute.For<IDevice>(),
                guard, Substitute.For<IPortContextStorage>(), Substitute.For<IJobPlanStorage>(), cts.Token);
        }

        // 异步确认解析失败应答。
        using var reply = await pendingReply;
        reply.SecsItem!.FirstValue<byte>().Should().Be(1);
        guard.TryBegin(scope, key, TimeSpan.FromSeconds(30)).Should().BeFalse();
        guard.TryRemove(scope, key, token).Should().BeTrue();
    }

    /// <summary>
    /// 构建当前业务解析器支持的 PJ/CJ 请求，保持测试聚焦于去重记录释放。
    /// </summary>
    private static SecsMessage BuildRequest(bool processJob)
    {
        // 两种消息使用不同业务标识及结构，但具有相同的短期去重释放语义。
        return processJob
            ? new SecsMessage(16, 15)
            {
                SecsItem = L(U4(1), L(L(A("PJ1"), B(13),
                    L(L(A("CARRIER1"), L(U1(1)))), L(U1(1), A("RECIPE1"), L()), Boolean(true), L())))
            }
            : new SecsMessage(14, 9)
            {
                SecsItem = L(A("Equipment"), A("ControlJob"), L(
                    L(A("ObjID"), A("CJ1")),
                    L(A("ProcessingCtrlSpec"), L(A("PJ1"))),
                    L(A("CarrierInputSpec"), L(A("CARRIER1")))))
            };
    }
}
