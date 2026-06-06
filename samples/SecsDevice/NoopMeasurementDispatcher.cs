using SECSdata;
using SECShandler.Interfaces;
using System.Threading;
using System.Threading.Tasks;

namespace SecsDevice;

/// <summary>
/// Sample 工程占位分发器：不做真实 gRPC 调用，仅保证接口编译通过。
/// </summary>
internal sealed class NoopMeasurementDispatcher : IMeasurementDispatcher
{
    public Task DispatchStartMeasurementAsync(S2F41_data data, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DispatchStopMeasurementAsync(S2F41_data data, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DispatchPauseMeasurementAsync(S2F41_data data, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DispatchResumeMeasurementAsync(S2F41_data data, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DispatchProcessProgramSelectAsync(S2F41_data data, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DispatchSlotMapSelectAsync(S3F17_data data, CancellationToken cancellationToken) => Task.CompletedTask;
}
