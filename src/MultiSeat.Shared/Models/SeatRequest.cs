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

    private int _width = 1920;
    public int Width
    {
        get => _width;
        init => _width = Math.Clamp(value, 640, 7680);
    }

    private int _height = 1080;
    public int Height
    {
        get => _height;
        init => _height = Math.Clamp(value, 480, 4320);
    }

    private int _fps = 60;
    public int Fps
    {
        get => _fps;
        init => _fps = Math.Clamp(value, 1, 240);
    }

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
