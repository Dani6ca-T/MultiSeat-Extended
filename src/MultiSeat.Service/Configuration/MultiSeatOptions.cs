namespace MultiSeat.Service.Configuration;

public sealed class MultiSeatOptions
{
    public const string SectionName = "MultiSeat";

    // ── Seats ────────────────────────────────────────────────────────
    public int MaxSeats { get; set; } = 4;
    public int PortBase { get; set; } = Shared.Constants.PortBase;

    // ── Apollo / Sunshine ────────────────────────────────────────────
    public string ApolloExePath { get; set; } = Shared.Constants.DefaultApolloPath;
    public string ApolloConfigDir { get; set; } = Shared.Constants.DefaultApolloConfigDir;

    // NVENC quality preset: 1 (P1, lowest latency) → 7 (P7, highest quality).
    // P4 is balanced — good quality without perceptible encode latency.
    // Apollo default is 1; we raise it because the NVENC hardware handles P4 at full framerate.
    public int NvencPreset { get; set; } = 4;

    // ── API ──────────────────────────────────────────────────────────
    public int ApiPort { get; set; } = Shared.Constants.DefaultApiPort;
    public string ApiKey { get; set; } = string.Empty;  // set in appsettings or env
    public bool RequireHttps { get; set; } = true;
    public string[] CorsOrigins { get; set; } = [];

    // ── Virtual Audio Cable ──────────────────────────────────────────
    public int VacCableCount { get; set; } = 4;  // number of VAC cables installed

    // ── HidHide ──────────────────────────────────────────────────────
    public string HidHideCliPath { get; set; } = @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe";

    // ── Input Isolation ──────────────────────────────────────────────
    public string InputHookDllPath { get; set; } = @"MultiSeatInputHook.dll";
    public bool EnableKeyboardMouseIsolation { get; set; } = true;
    public bool AutoAssignControllers { get; set; } = true;

    // ── Display ──────────────────────────────────────────────────────
    // Enable Windows Advanced Color (HDR) on virtual displays at seat creation.
    // Requires SudoVDA driver v0.5+ with HDR EDID support.
    // When enabled, Apollo will stream in HDR if the Moonlight client also supports it.
    public bool EnableHdr { get; set; } = false;

    // ── Controller emulation ─────────────────────────────────────────
    // When true, MultiSeat creates a ViGEm virtual Xbox 360 controller per seat
    // and routes a host-side physical XInput controller into the session.
    // When false (default), Apollo handles controller forwarding natively
    // from the Moonlight client (e.g. ROG Ally). Enabling this alongside
    // Apollo's built-in controller forwarding causes duplicate controllers.
    public bool EnableViGEmController { get; set; } = false;

    // ── Launch-on-connect apps ───────────────────────────────────────
    // Apps launched into a seat's session when a Moonlight client connects.
    // Empty by default (feature off). Use this INSTEAD of Windows autostart for
    // game launchers (Steam Big Picture, EmulationStation, RetroBat, …): launching
    // them after the client connects guarantees Apollo's virtual controller already
    // exists, so the launcher's startup controller scan detects it. Apps autostarted
    // at login run before any stream and never see the per-stream virtual pad.
    public LaunchOnConnectApp[] LaunchOnConnect { get; set; } = [];

    // Delay after the client-connect event before launching the apps, giving Apollo
    // a moment to create the virtual controller so the apps enumerate it at startup.
    public int LaunchOnConnectDelayMs { get; set; } = 4_000;

    // Kill the launched apps when the Moonlight client disconnects. When false,
    // the apps stay running and are reused on the next connect (no relaunch while
    // still alive). Single-instance launchers like Steam tolerate either setting.
    public bool KillLaunchOnConnectAppsOnDisconnect { get; set; } = false;

    // ── Timeouts ─────────────────────────────────────────────────────
    public int SessionConnectTimeoutMs { get; set; } = 15_000;
    public int ProcessLaunchTimeoutMs { get; set; } = 10_000;
    public int HealthCheckIntervalMs { get; set; } = 5_000;

    // ── Rebuild ───────────────────────────────────────────────────────
    // Absolute path to the repo root. Required for the dashboard Rebuild button.
    // Example: C:\MultiSeat-Development
    public string SourceDir { get; set; } = string.Empty;
}

/// <summary>
/// One app to launch into a seat session when a Moonlight client connects.
/// Configured under MultiSeat:LaunchOnConnect in appsettings.json.
/// </summary>
public sealed class LaunchOnConnectApp
{
    /// <summary>Absolute path to the executable (e.g. Steam.exe).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Optional command-line arguments (e.g. "-bigpicture").</summary>
    public string? Arguments { get; set; }

    /// <summary>Optional working directory; null inherits the launcher default.</summary>
    public string? WorkingDirectory { get; set; }
}
