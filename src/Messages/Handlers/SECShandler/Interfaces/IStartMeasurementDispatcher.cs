namespace SECShandler.Interfaces
{
    public sealed class StartMeasurementDispatchRequest
    {
        public string Name { get; set; } = string.Empty;
        public string LotId { get; set; } = string.Empty;
        public string PPID { get; set; } = string.Empty;
        public List<uint> Slots { get; set; } = new();
    }

    public interface IStartMeasurementDispatcher
    {
        Task DispatchStartMeasurementAsync(StartMeasurementDispatchRequest request, CancellationToken cancellationToken);
    }
}
