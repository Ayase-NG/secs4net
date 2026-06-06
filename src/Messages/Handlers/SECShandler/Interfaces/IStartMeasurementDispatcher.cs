using SECSdata;

namespace SECShandler.Interfaces
{
    public sealed class StartMeasurementDispatchRequest
    {
        public string Name { get; set; } = string.Empty;
        public string LotId { get; set; } = string.Empty;
        public string PPID { get; set; } = string.Empty;
        public List<uint> Slots { get; set; } = new();
    }

    public sealed class WaferDispatchRequest
    {
        public string SlotId { get; set; } = string.Empty;
        public string WaferId { get; set; } = string.Empty;
        public string LotId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string PPID { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
    }

    public sealed class RecipeDispatchRequest
    {
        public string PPName { get; set; } = string.Empty;
        public string PPID { get; set; } = string.Empty;
        public string WaferId { get; set; } = string.Empty;
        public string LotId { get; set; } = string.Empty;
    }

    public interface IMeasurementDispatcher
    {
        Task DispatchStartMeasurementAsync(S2F41_data data, CancellationToken cancellationToken);
        Task DispatchStopMeasurementAsync(S2F41_data data, CancellationToken cancellationToken);
        Task DispatchPauseMeasurementAsync(S2F41_data data, CancellationToken cancellationToken);
        Task DispatchResumeMeasurementAsync(S2F41_data data, CancellationToken cancellationToken);
        Task DispatchProcessProgramSelectAsync(S2F41_data data, CancellationToken cancellationToken);
        Task DispatchSlotMapSelectAsync(S3F17_data data, CancellationToken cancellationToken);
    }
}
