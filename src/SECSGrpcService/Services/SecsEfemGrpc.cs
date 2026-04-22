using Grpc.Core;
using Grpc.Net.Client;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SECSGrpcService.Services
{
    /// <summary>
    /// EFEM gRPC 客户端调用封装，对应 <c>secs.proto</c> 中定义的控制类接口。
    /// </summary>
    public sealed class SecsEfemGrpc
    {
        private readonly ILogger<SecsEfemGrpc> _logger;

        public SecsEfemGrpc(ILogger<SecsEfemGrpc> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 对指定远端地址列表主动发送 StartMeasurement。
        /// </summary>
        public async Task SendStartMeasurementToClientsAsync(IEnumerable<string> targetAddresses, StartMessage startMessage, CancellationToken cancellationToken = default)
        {
            if (targetAddresses == null) return;

            foreach (var address in targetAddresses)
            {
                if (string.IsNullOrWhiteSpace(address)) continue;

                try
                {
                    using var channel = GrpcChannel.ForAddress(address);
                    var client = new global::SECSGrpcService.EFEM.EFEMClient(channel.CreateCallInvoker());

                    var startCall = client.StartMeasurementAsync(startMessage, cancellationToken: cancellationToken);
                    var startReply = await startCall.ResponseAsync.ConfigureAwait(false);
                    _logger.LogInformation("StartMeasurement -> {Address} returned {Code}: {Msg}", address, startReply.MessageCode, startReply.Message);
                }
                catch (RpcException rex)
                {
                    _logger.LogError(rex, "StartMeasurement RPC to {Address} failed", address);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send StartMeasurement to {Address}", address);
                }

                if (cancellationToken.IsCancellationRequested) break;
            }
        }

        /// <summary>
        /// 对指定的远端地址列表依次发起 StartMeasurement / ProcessProgramSelect / PauseMeasurement / ResumeMeasurement / StopMeasurement 请求。
        /// 每个地址都会创建一个短生命周期的 gRPC 通道并顺序调用这些 RPC（按需可拆分成单独的方法）。
        /// </summary>
        public async Task SendStartMeasurementAndRelatedRequestsToClientsAsync(IEnumerable<string> targetAddresses, StartMessage startMessage, RecipeMessage recipeMessage, WaferMessage waferMessage, CancellationToken cancellationToken = default)
        {
            if (targetAddresses == null) return;

            foreach (var address in targetAddresses)
            {
                if (string.IsNullOrWhiteSpace(address)) continue;

                try
                {
                    using var channel = GrpcChannel.ForAddress(address);
                    var client = new global::SECSGrpcService.EFEM.EFEMClient(channel.CreateCallInvoker());

                    try
                    {
                        var startCall = client.StartMeasurementAsync(startMessage, cancellationToken: cancellationToken);
                        var startReply = await startCall.ResponseAsync.ConfigureAwait(false);
                        _logger.LogInformation("StartMeasurement -> {Address} returned {Code}: {Msg}", address, startReply.MessageCode, startReply.Message);
                    }
                    catch (RpcException rex)
                    {
                        _logger.LogError(rex, "StartMeasurement RPC to {Address} failed", address);
                    }

                    try
                    {
                        var procCall = client.ProcessProgramSelectAsync(recipeMessage, cancellationToken: cancellationToken);
                        var procReply = await procCall.ResponseAsync.ConfigureAwait(false);
                        _logger.LogInformation("ProcessProgramSelect -> {Address} returned {Code}: {Msg}", address, procReply.MessageCode, procReply.Message);
                    }
                    catch (RpcException rex)
                    {
                        _logger.LogError(rex, "ProcessProgramSelect RPC to {Address} failed", address);
                    }

                    try
                    {
                        var pauseCall = client.PauseMeasurementAsync(waferMessage, cancellationToken: cancellationToken);
                        var pauseReply = await pauseCall.ResponseAsync.ConfigureAwait(false);
                        _logger.LogInformation("PauseMeasurement -> {Address} returned {Code}: {Msg}", address, pauseReply.MessageCode, pauseReply.Message);
                    }
                    catch (RpcException rex)
                    {
                        _logger.LogError(rex, "PauseMeasurement RPC to {Address} failed", address);
                    }

                    try
                    {
                        var resumeCall = client.ResumeMeasurementAsync(waferMessage, cancellationToken: cancellationToken);
                        var resumeReply = await resumeCall.ResponseAsync.ConfigureAwait(false);
                        _logger.LogInformation("ResumeMeasurement -> {Address} returned {Code}: {Msg}", address, resumeReply.MessageCode, resumeReply.Message);
                    }
                    catch (RpcException rex)
                    {
                        _logger.LogError(rex, "ResumeMeasurement RPC to {Address} failed", address);
                    }

                    try
                    {
                        var stopCall = client.StopMeasurementAsync(waferMessage, cancellationToken: cancellationToken);
                        var stopReply = await stopCall.ResponseAsync.ConfigureAwait(false);
                        _logger.LogInformation("StopMeasurement -> {Address} returned {Code}: {Msg}", address, stopReply.MessageCode, stopReply.Message);
                    }
                    catch (RpcException rex)
                    {
                        _logger.LogError(rex, "StopMeasurement RPC to {Address} failed", address);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send requests to {Address}", address);
                }

                if (cancellationToken.IsCancellationRequested) break;
            }
        }
    }
}
