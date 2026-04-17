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

    // ── Timeouts ─────────────────────────────────────────────────────
    public int SessionConnectTimeoutMs { get; set; } = 15_000;
    public int ProcessLaunchTimeoutMs { get; set; } = 10_000;
    public int HealthCheckIntervalMs { get; set; } = 5_000;
}
