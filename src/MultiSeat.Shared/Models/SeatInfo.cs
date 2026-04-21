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

    // Audio — game output (audiomode:i:1 makes host devices visible in RDP session)
    public string? AudioGameRenderDeviceId { get; set; }     // session default render → Apollo loopback-captures for audio_sink
    public string? AudioGameRenderFriendlyName { get; set; } // friendly name → Apollo audio_sink

    // Audio — mic routing (Moonlight mic → games)
    public string? AudioDeviceId { get; set; }        // mic render device ID (legacy/IPolicyConfig)
    public string? AudioFriendlyName { get; set; }    // mic render friendly name → Apollo virtual_sink
    public string? AudioCaptureDeviceId { get; set; } // mic capture device ID → set as session default capture
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
