using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Grpc.Core;
using GY.EFEM;
using SECSGrpcService.Services;
using Xunit;

namespace Secs4Net.UnitTests;

public class ProcessJobGrpcSequenceUnitTests
{
    /// <summary>
    /// 仅配方和启动均成功时完成流程，且严格先 RecipeSelect 后 StartMeasurement。
    /// </summary>
    [Fact]
    public async Task Execute_Success_UsesCorrectOrderAndPayload()
    {
        var invoker = new ControlInvoker();
        var recipe = new RecipeMessage { PPID = "R1", PortId = "2", LotId = "LOT1", Mode = "00" };
        recipe.SlotsList.Add(new uint[] { 1, 25 });

        // 异步执行真实生成客户端的两步调用，使用进程内调用器替代网络。
        await ProcessJobGrpcSequence.ExecuteAsync(new EFEM.EFEMClient(invoker), recipe, 5000, CancellationToken.None);

        invoker.Methods.Should().Equal("RecipeSelect", "StartMeasurement");
        invoker.Recipe.Should().BeSameAs(recipe);
    }

    /// <summary>
    /// 非零返回或传输异常必须向调用方传播，配方失败时不能启动。
    /// </summary>
    [Theory]
    [InlineData("RecipeSelect", false)]
    [InlineData("RecipeSelect", true)]
    [InlineData("StartMeasurement", false)]
    [InlineData("StartMeasurement", true)]
    public async Task Execute_Failure_DoesNotRetryOrHideError(string stage, bool transportFailure)
    {
        var invoker = new ControlInvoker { FailureStage = stage, TransportFailure = transportFailure };
        // 异步验证非零业务返回码和 RPC 传输失败均不能被吞掉。
        Func<Task> execute = () => ProcessJobGrpcSequence.ExecuteAsync(new EFEM.EFEMClient(invoker), new RecipeMessage(), 5000, CancellationToken.None);
        await execute.Should().ThrowAsync<Exception>();

        invoker.Methods.Should().Equal(stage == "RecipeSelect" ? new[] { "RecipeSelect" } : new[] { "RecipeSelect", "StartMeasurement" });
    }

    /// <summary>
    /// 记录生成客户端调用并注入业务拒绝或传输异常，不需要真实 EFEM 服务。
    /// </summary>
    private sealed class ControlInvoker : CallInvoker
    {
        public List<string> Methods { get; } = new();
        public RecipeMessage? Recipe { get; private set; }
        public string? FailureStage { get; init; }
        public bool TransportFailure { get; init; }

        /// <summary>
        /// 返回可释放的 Unary 应答，并完整记录调用顺序与配方请求。
        /// </summary>
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method,
            string? host, CallOptions options, TRequest request)
        {
            Methods.Add(method.Name);
            if (request is RecipeMessage recipe) Recipe = recipe;
            var failed = method.Name == FailureStage;
            var response = failed && TransportFailure
                ? Task.FromException<TResponse>(new RpcException(new Status(StatusCode.Unavailable, "unknown outcome")))
                : Task.FromResult((TResponse)(object)new ControlReply { MessageCode = failed ? 1 : 0, Message = failed ? "Rejected" : "OK" });
            return new AsyncUnaryCall<TResponse>(response, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
        }

        /// <summary>测试不支持同步调用。</summary>
        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        /// <summary>测试不支持客户端流。</summary>
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
        /// <summary>测试不支持服务端流。</summary>
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        /// <summary>测试不支持双向流。</summary>
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }
}
