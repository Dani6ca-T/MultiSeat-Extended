namespace MultiSeat.Shared.Models;

public sealed class SeatRequest
{
    public required string AccountName { get; init; }
    public int Width { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public int Fps { get; init; } = 60;
    public string? LaunchApp { get; init; }
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
