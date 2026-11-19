using System;
using System.Threading;
using System.Threading.Tasks;
using GY.EFEM;

namespace SECSGrpcService.Services;

/// <summary>
/// PJ 的单实例 gRPC 顺序调用，不广播、不重试、不吞异常。
/// </summary>
internal static class ProcessJobGrpcSequence
{
    /// <summary>
    /// 同一客户端先选配方再启动，每步独立超时且必须明确返回 MessageCode=0。
    /// </summary>
    internal static async Task ExecuteAsync(EFEM.EFEMClient client, RecipeMessage recipe, int timeoutMs, CancellationToken cancellationToken)
    {
        using (var recipeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            recipeTimeout.CancelAfter(timeoutMs);
            // 异步等待配方选择结果；非零应答直接拒绝后续 START。
            using var call = client.RecipeSelectAsync(recipe, cancellationToken: recipeTimeout.Token);
            var reply = await call.ResponseAsync.ConfigureAwait(false);
            if (reply.MessageCode != 0)
                throw new InvalidOperationException($"RecipeSelect rejected ({reply.MessageCode}): {reply.Message}");
        }
        cancellationToken.ThrowIfCancellationRequested();
        using var startTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startTimeout.CancelAfter(timeoutMs);
        // 异步等待启动确认；异常、超时或非零结果向协调器传播，禁止自动重发。
        using var start = client.StartMeasurementAsync(new NoParams(), cancellationToken: startTimeout.Token);
        var started = await start.ResponseAsync.ConfigureAwait(false);
        if (started.MessageCode != 0)
            throw new InvalidOperationException($"StartMeasurement rejected ({started.MessageCode}): {started.Message}");
    }
}
