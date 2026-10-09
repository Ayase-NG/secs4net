using Grpc.Core;
using Grpc.Net.Client;
using GY.EFEM;

namespace SECSGrpcService.Services
{
    /// <summary>
    /// EFEM gRPC 客户端调用封装，对应 <c>secs.proto</c> 中定义的主动外发接口。
    /// 负责：
    /// 1) 通过 Nacos 解析目标服务地址；
    /// 2) 向目标服务发起 Start/Stop/Pause/Resume/PPSELECT/StatusReport 调用；
    /// 3) 统一记录调用成功与异常日志。
    /// </summary>
    public sealed class SecsEfemGrpc
    {
        private readonly ILogger<SecsEfemGrpc> _logger;
        private readonly NacosGrpcResolver _nacosGrpcResolver;
        private readonly IConfiguration _configuration;

        /// <summary>
        /// 构造 gRPC 客户端封装对象。
        /// </summary>
        public SecsEfemGrpc(ILogger<SecsEfemGrpc> logger, NacosGrpcResolver nacosGrpcResolver, IConfiguration configuration)
        {
            _logger = logger;
            _nacosGrpcResolver = nacosGrpcResolver;
            _configuration = configuration;
        }

        /// <summary>
        /// 从 Nacos 动态发现服务实例后发送 StartMeasurement（无参）。
        /// </summary>
        public async Task SendStartMeasurementToServiceAsync(
            string serviceName,
            string groupName,
            IEnumerable<string>? clusters,
            bool useHttps,
            IEnumerable<string>? fallbackAddresses = null,
            CancellationToken cancellationToken = default)
        {
            // 魔镜部分需要单独获取address
            var addresses = await _nacosGrpcResolver.ResolveAddressesAsync(serviceName, groupName, clusters, useHttps, fallbackAddresses, cancellationToken);
            await SendStartMeasurementToClientsAsync(addresses, cancellationToken);
        }

        /// <summary>
        /// 从 Nacos 动态发现服务实例后发送 StopMeasurement。
        /// </summary>
        public async Task SendStopMeasurementToServiceAsync(
            string serviceName,
            string groupName,
            IEnumerable<string>? clusters,
            bool useHttps,
            WaferMessage waferMessage,
            IEnumerable<string>? fallbackAddresses = null,
            CancellationToken cancellationToken = default)
        {
            var addresses = await _nacosGrpcResolver.ResolveAddressesAsync(serviceName, groupName, clusters, useHttps, fallbackAddresses, cancellationToken);
            await SendStopMeasurementToClientsAsync(addresses, waferMessage, cancellationToken);
        }

        /// <summary>
        /// 从 Nacos 动态发现服务实例后发送 PauseMeasurement。
        /// </summary>
        public async Task SendPauseMeasurementToServiceAsync(
            string serviceName,
            string groupName,
            IEnumerable<string>? clusters,
            bool useHttps,
            WaferMessage waferMessage,
            IEnumerable<string>? fallbackAddresses = null,
            CancellationToken cancellationToken = default)
        {
            var addresses = await _nacosGrpcResolver.ResolveAddressesAsync(serviceName, groupName, clusters, useHttps, fallbackAddresses, cancellationToken);
            await SendPauseMeasurementToClientsAsync(addresses, waferMessage, cancellationToken);
        }

        /// <summary>
        /// 从 Nacos 动态发现服务实例后发送 ResumeMeasurement。
        /// </summary>
        public async Task SendResumeMeasurementToServiceAsync(
            string serviceName,
            string groupName,
            IEnumerable<string>? clusters,
            bool useHttps,
            WaferMessage waferMessage,
            IEnumerable<string>? fallbackAddresses = null,
            CancellationToken cancellationToken = default)
        {
            var addresses = await _nacosGrpcResolver.ResolveAddressesAsync(serviceName, groupName, clusters, useHttps, fallbackAddresses, cancellationToken);
            await SendResumeMeasurementToClientsAsync(addresses, waferMessage, cancellationToken);
        }

        /// <summary>
        /// 从 Nacos 动态发现服务实例后发送 ProcessProgramSelect。
        /// </summary>
        public async Task SendProcessProgramSelectToServiceAsync(
            string serviceName,
            string groupName,
            IEnumerable<string>? clusters,
            bool useHttps,
            RecipeMessage recipeMessage,
            IEnumerable<string>? fallbackAddresses = null,
            CancellationToken cancellationToken = default)
        {
            var addresses = await _nacosGrpcResolver.ResolveAddressesAsync(serviceName, groupName, clusters, useHttps, fallbackAddresses, cancellationToken);
            await SendProcessProgramSelectToClientsAsync(addresses, recipeMessage, cancellationToken);
        }

        /// <summary>
        /// 从 Nacos 动态发现服务实例后发送 ProcessProgramSelect。
        /// </summary>
        public async Task SendTakeLoadPortToServiceAsync(
            string serviceName,
            string groupName,
            IEnumerable<string>? clusters,
            bool useHttps,
            PortInfo port,
            IEnumerable<string>? fallbackAddresses = null,
            CancellationToken cancellationToken = default)
        {
            var addresses = await _nacosGrpcResolver.ResolveAddressesAsync(serviceName, groupName, clusters, useHttps, fallbackAddresses, cancellationToken);
            await SendTakeLoadPortToClientsAsync(addresses, port, cancellationToken);
        }

        /// <summary>
        /// 对指定远端地址列表主动发送 StartMeasurement（无参）。
        /// </summary>
        public async Task SendStartMeasurementToClientsAsync(IEnumerable<string> targetAddresses, CancellationToken cancellationToken = default)
        {
            if (targetAddresses == null) return;

            foreach (var address in targetAddresses)
            {
                if (string.IsNullOrWhiteSpace(address)) continue;

                try
                {
                    using var channel = GrpcChannel.ForAddress(address);
                    var client = new EFEM.EFEMClient(channel.CreateCallInvoker());

                    var startCall = client.StartMeasurementAsync(new NoParams(), cancellationToken: CreateLinkedToken(cancellationToken));
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
        /// 对指定远端地址列表发送 StopMeasurement。
        /// </summary>
        public async Task SendStopMeasurementToClientsAsync(IEnumerable<string> targetAddresses, WaferMessage waferMessage, CancellationToken cancellationToken = default)
        {
            if (targetAddresses == null) return;

            foreach (var address in targetAddresses)
            {
                if (string.IsNullOrWhiteSpace(address)) continue;

                try
                {
                    using var channel = GrpcChannel.ForAddress(address);
                    var client = new EFEM.EFEMClient(channel.CreateCallInvoker());
                    var call = client.StopMeasurementAsync(waferMessage, cancellationToken: CreateLinkedToken(cancellationToken));
                    var reply = await call.ResponseAsync.ConfigureAwait(false);
                    _logger.LogInformation("StopMeasurement -> {Address} returned {Code}: {Msg}", address, reply.MessageCode, reply.Message);
                }
                catch (RpcException rex)
                {
                    _logger.LogError(rex, "StopMeasurement RPC to {Address} failed", address);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send StopMeasurement to {Address}", address);
                }

                if (cancellationToken.IsCancellationRequested) break;
            }
        }

        /// <summary>
        /// 对指定远端地址列表发送 PauseMeasurement。
        /// </summary>
        public async Task SendPauseMeasurementToClientsAsync(IEnumerable<string> targetAddresses, WaferMessage waferMessage, CancellationToken cancellationToken = default)
        {
            if (targetAddresses == null) return;

            foreach (var address in targetAddresses)
            {
                if (string.IsNullOrWhiteSpace(address)) continue;

                try
                {
                    using var channel = GrpcChannel.ForAddress(address);
                    var client = new EFEM.EFEMClient(channel.CreateCallInvoker());
                    var call = client.PauseMeasurementAsync(waferMessage, cancellationToken: CreateLinkedToken(cancellationToken));
                    var reply = await call.ResponseAsync.ConfigureAwait(false);
                    _logger.LogInformation("PauseMeasurement -> {Address} returned {Code}: {Msg}", address, reply.MessageCode, reply.Message);
                }
                catch (RpcException rex)
                {
                    _logger.LogError(rex, "PauseMeasurement RPC to {Address} failed", address);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send PauseMeasurement to {Address}", address);
                }

                if (cancellationToken.IsCancellationRequested) break;
            }
        }

        /// <summary>
        /// 对指定远端地址列表发送 ResumeMeasurement。
        /// </summary>
        public async Task SendResumeMeasurementToClientsAsync(IEnumerable<string> targetAddresses, WaferMessage waferMessage, CancellationToken cancellationToken = default)
        {
            if (targetAddresses == null) return;

            foreach (var address in targetAddresses)
            {
                if (string.IsNullOrWhiteSpace(address)) continue;

                try
                {
                    using var channel = GrpcChannel.ForAddress(address);
                    var client = new EFEM.EFEMClient(channel.CreateCallInvoker());
                    var call = client.ResumeMeasurementAsync(waferMessage, cancellationToken: CreateLinkedToken(cancellationToken));
                    var reply = await call.ResponseAsync.ConfigureAwait(false);
                    _logger.LogInformation("ResumeMeasurement -> {Address} returned {Code}: {Msg}", address, reply.MessageCode, reply.Message);
                }
                catch (RpcException rex)
                {
                    _logger.LogError(rex, "ResumeMeasurement RPC to {Address} failed", address);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send ResumeMeasurement to {Address}", address);
                }

                if (cancellationToken.IsCancellationRequested) break;
            }
        }

        /// <summary>
        /// 对指定远端地址列表发送 ProcessProgramSelect。
        /// </summary>
        public async Task SendProcessProgramSelectToClientsAsync(IEnumerable<string> targetAddresses, RecipeMessage recipeMessage, CancellationToken cancellationToken = default)
        {
            if (targetAddresses == null) return;

            foreach (var address in targetAddresses)
            {
                if (string.IsNullOrWhiteSpace(address)) continue;

                try
                {
                    using var channel = GrpcChannel.ForAddress(address);
                    var client = new EFEM.EFEMClient(channel.CreateCallInvoker());
                    var call = client.RecipeSelectAsync(recipeMessage, cancellationToken: CreateLinkedToken(cancellationToken));
                    var reply = await call.ResponseAsync.ConfigureAwait(false);
                    _logger.LogInformation("RecipeSelect -> {Address} returned {Code}: {Msg}", address, reply.MessageCode, reply.Message);
                }
                catch (RpcException rex)
                {
                    _logger.LogError(rex, "RecipeSelect RPC to {Address} failed", address);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send RecipeSelect to {Address}", address);
                }

                if (cancellationToken.IsCancellationRequested) break;
            }
        }

        /// <summary>
        /// 从 Nacos 动态发现服务实例后发送 StartMeasurement（无参）。
        /// </summary>
        public async Task SendTakeLoadPortToClientsAsync(IEnumerable<string> targetAddresses, PortInfo port, CancellationToken cancellationToken = default)
        {
            if (targetAddresses == null) return;

            foreach (var address in targetAddresses)
            {
                if (string.IsNullOrWhiteSpace(address)) continue;

                try
                {
                    using var channel = GrpcChannel.ForAddress(address);
                    var client = new EFEM.EFEMClient(channel.CreateCallInvoker());
                    var call = client.TakeLoadPortAsync(port, cancellationToken: CreateLinkedToken(cancellationToken));
                    var reply = await call.ResponseAsync.ConfigureAwait(false);
                    _logger.LogInformation("TakeLoadPort -> {Address} returned {Code}: {Msg}", address, reply.MessageCode, reply.Message);
                }
                catch (RpcException rex)
                {
                    _logger.LogError(rex, "TakeLoadPort RPC to {Address} failed", address);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send TakeLoadPort to {Address}", address);
                }

                if (cancellationToken.IsCancellationRequested) break;
            }
        }

        /// <summary>
        /// 创建带超时的调用令牌，超时值来自配置 SecsDispatch:Grpc:CallTimeoutMs。
        /// </summary>
        private CancellationToken CreateLinkedToken(CancellationToken outerToken)
        {
            var timeoutMs = Math.Max(100, _configuration.GetValue<int>("SecsDispatch:Grpc:CallTimeoutMs", 5000));
            var cts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
            cts.CancelAfter(timeoutMs);
            return cts.Token;
        }
    }
}
