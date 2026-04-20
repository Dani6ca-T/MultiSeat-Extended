namespace MultiSeat.Shared.Models;

public enum SeatStatus
{
    Idle,
    Provisioning,
    Configuring,
    Ready,
    Streaming,
    TearingDown,
    Error
}

public sealed class SeatInfo
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string AccountName { get; init; }
    public int SessionId { get; set; } = -1;
    public SeatStatus Status { get; set; } = SeatStatus.Idle;

    // Display
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int Fps { get; set; } = 60;
    public string? DisplayDevicePath { get; set; }

    // Networking
    public int PortBase { get; set; }
    public int ApolloProcessId { get; set; }

    // Audio
    public string? AudioDeviceId { get; set; }        // render device (VAC Input) Windows device ID → IPolicyConfig
    public string? AudioFriendlyName { get; set; }    // render device friendly name → Apollo virtual_sink
    public string? AudioCaptureDeviceId { get; set; } // capture device (VAC Output) → set as default mic in session
    public int VacCableIndex { get; set; } = -1;

    // Input
    public int ViGEmControllerIndex { get; set; } = -1;

    // Lifecycle
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadyAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? LaunchApp { get; set; }

    // Preset
    public bool AutoStart { get; set; } = false;
}
