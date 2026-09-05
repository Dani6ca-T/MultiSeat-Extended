namespace MultiSeat.Shared.Models;

public enum SeatStatus
{
    Idle,
    Provisioning,
    Configuring,
    Ready,
    Streaming,
    Connecting,
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
    public int StreamingProcessId { get; set; }

    // Emulator netplay — RetroArch host port for this seat (PortBase + offset; 0 = disabled).
    // Seats connect to each other over loopback at 127.0.0.1:<this port>.
    public int RetroArchNetplayPort { get; set; }

    // Audio — game output (audiomode:i:1 makes host devices visible in RDP session)
    public string? AudioGameRenderDeviceId { get; set; }     // session default render → Apollo loopback-captures for audio_sink
    public string? AudioGameRenderFriendlyName { get; set; } // friendly name → Apollo audio_sink

    // Audio — mic routing (Moonlight mic → Steam Streaming Microphone → games)
    public string? AudioCaptureDeviceId { get; set; } // "Microphone (Steam Streaming Microphone)" device ID → session default capture
    public int VacCableIndex { get; set; } = -1;

    // Input
    public int ViGEmControllerIndex { get; set; } = -1;

    // Lifecycle
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadyAt { get; set; }

    // UTC timestamp of the latest actual SeatStatus transition, stamped by
    // SeatState.TransitionTo (the single Status mutation point). Same-state re-asserts
    // are documented no-ops and do not restamp. Serialized with the seat, so HTTP and
    // WebSocket consumers — which both receive whole SeatInfo objects — observe state
    // age without reconstructing it from logs. Defaults to construction time, which is
    // also when the seat enters its initial status.
    public DateTimeOffset LastTransitionAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ErrorMessage { get; set; }
    public string? LaunchApp { get; set; }

    // Root PID of the process launched via LaunchAppInSeatAsync (dashboard "launch").
    // Tracked so the health check can return the seat to Ready when the app exits
    // (SessionHealthCheck Check 3). 0 = no app tracked. Only the root process is
    // tracked; children are not part of the app lifetime.
    public int LaunchedProcessId { get; set; }

    // OS start time of that same launched process (captured immediately after launch).
    // Pairs with LaunchedProcessId to form a ProcessIdentity so seat teardown can
    // terminate the app without ever killing an unrelated process whose PID Windows
    // recycled after the original exited. Null when no app is tracked or the start
    // time could not be obtained (the process had already exited).
    public DateTimeOffset? LaunchedProcessStartedAt { get; set; }

    // Preset
    public bool AutoStart { get; set; } = false;
    public NvencQualityPreset NvencPreset { get; set; } = NvencQualityPreset.Balanced;

    // Granular provisioning progress — set at each major step, cleared on Ready/Error
    public string? ProvisioningStep { get; set; }
}
