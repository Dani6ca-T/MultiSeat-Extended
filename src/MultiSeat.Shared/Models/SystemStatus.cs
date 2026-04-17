namespace MultiSeat.Shared.Models;

public sealed class SystemStatus
{
    public int ActiveSeats { get; set; }
    public int MaxSeats { get; set; } = Constants.MaxSeats;
    public GpuInfo? Gpu { get; set; }
    public long SystemMemoryMb { get; set; }
    public long AvailableMemoryMb { get; set; }
    public string WindowsBuild { get; set; } = string.Empty;
    public bool RdpWrapperActive { get; set; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class GpuInfo
{
    public string Name { get; set; } = string.Empty;
    public int UtilizationPercent { get; set; }
    public long VramTotalMb { get; set; }
    public long VramUsedMb { get; set; }
    public int EncoderUtilizationPercent { get; set; }
    public int ActiveEncoderSessions { get; set; }
}
