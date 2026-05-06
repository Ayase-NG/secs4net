namespace SECSGrpcService.Services;

public sealed class AlarmHistoryRecord
{
    public long Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public uint AlarmId { get; set; }
    public byte[] AlarmCode { get; set; } = Array.Empty<byte>();
    public string AlarmText { get; set; } = string.Empty;
    public int Severity { get; set; }
    public long OccurredAtUnixMs { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
