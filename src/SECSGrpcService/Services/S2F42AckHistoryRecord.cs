namespace SECSGrpcService.Services;

public sealed class SecsInteractionHistoryRecord
{
    public long Id { get; set; }
    public string SxFy { get; set; } = string.Empty;
    public string SecsMessage { get; set; } = string.Empty;
    public byte? Hcack { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
