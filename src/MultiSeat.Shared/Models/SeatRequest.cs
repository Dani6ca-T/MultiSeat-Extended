namespace MultiSeat.Shared.Models;

public enum NvencQualityPreset
{
    Latency  = 0,  // P1, no two-pass, no spatial AQ — lowest encode latency
    Balanced = 1,  // P4, quarter-res two-pass, spatial AQ — default
    Quality  = 2,  // P7, full-res two-pass, spatial AQ, higher VBV — best quality
}

public sealed class SeatRequest
{
    public required string AccountName { get; init; }
    public int Width { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public int Fps { get; init; } = 60;
    public string? LaunchApp { get; init; }
    public NvencQualityPreset NvencPreset { get; init; } = NvencQualityPreset.Balanced;
}

public sealed class NvencPresetRequest
{
    public NvencQualityPreset Preset { get; init; } = NvencQualityPreset.Balanced;
}

public sealed class AutoStartRequest
{
    public bool Enabled { get; init; }
}

public sealed class LaunchAppRequest
{
    public required string ExecutablePath { get; init; }
    public string? Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
}
