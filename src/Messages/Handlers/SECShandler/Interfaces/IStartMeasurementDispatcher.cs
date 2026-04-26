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
        Task DispatchStartMeasurementAsync(StartMeasurementDispatchRequest request, CancellationToken cancellationToken);
        Task DispatchStopMeasurementAsync(WaferDispatchRequest request, CancellationToken cancellationToken);
        Task DispatchPauseMeasurementAsync(WaferDispatchRequest request, CancellationToken cancellationToken);
        Task DispatchResumeMeasurementAsync(WaferDispatchRequest request, CancellationToken cancellationToken);
        Task DispatchProcessProgramSelectAsync(RecipeDispatchRequest request, CancellationToken cancellationToken);
    }
}
