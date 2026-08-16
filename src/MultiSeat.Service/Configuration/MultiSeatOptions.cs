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

    // ── Audio ────────────────────────────────────────────────────────
    // How seat game audio reaches Moonlight. See AudioMode for the trade-off.
    // Default SharedHost — the historical behaviour; PerSession drops mic support.
    public AudioMode AudioMode { get; set; } = AudioMode.SharedHost;

    // ── Virtual Audio Cable ──────────────────────────────────────────
    // Only used when AudioMode = SharedHost. PerSession needs no virtual cables at all.
    public int VacCableCount { get; set; } = 4;  // number of VAC cables installed

    // ── HidHide ──────────────────────────────────────────────────────
    public string HidHideCliPath { get; set; } = @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe";

    // ── Input Isolation ──────────────────────────────────────────────
    public string InputHookDllPath { get; set; } = @"MultiSeatInputHook.dll";

    // Keyboard/mouse session isolation via the InputHook DLL.
    // Default OFF — it is a no-op as architected: the low-level WH_KEYBOARD_LL/WH_MOUSE_LL
    // hooks are installed from the service process (Session 0), where GetForegroundWindow()
    // returns NULL, so ShouldPassThrough() always passes the event. There is also no
    // cross-session K/M bleed to prevent (physical input goes to the console session; Moonlight
    // input is SendInput'd inside the seat session). Re-enabling is only meaningful if the hook
    // is re-architected to run inside the seat session. See CLAUDE.md "Known Constraints".
    public bool EnableKeyboardMouseIsolation { get; set; } = false;
    public bool AutoAssignControllers { get; set; } = true;

    // ── Display ──────────────────────────────────────────────────────
    // Resize a seat to whatever resolution its Moonlight client asks for.
    //
    // Apollo's own dd_resolution_option = auto cannot do this: a seat streams its RDP session
    // surface, nothing inside the session can resize it (issue #15), and Apollo logs
    // "[1610] failed to set display mode!". Only mstsc sets that size, so following the client
    // means reconnecting the session — which preserves the Windows session and everything
    // running in it, but does briefly interrupt an active stream.
    //
    // Off by default for that reason, and because the trigger (a client connecting) could not
    // be exercised on the reference host, which is headless and has never had a Moonlight
    // client attached. The resize path itself IS verified: 1280x720 -> 1920x1080 on a live
    // seat, session id preserved.
    public bool FollowClientResolution { get; set; } = false;

    // Enable Windows Advanced Color (HDR) on virtual displays at seat creation.
    // Requires SudoVDA driver v0.5+ with HDR EDID support.
    // When enabled, Apollo will stream in HDR if the Moonlight client also supports it.
    //
    // ⚠️ Currently a NO-OP for a seat — nothing reads this to enable advanced colour, and no
    // seat has ever streamed HDR.
    //
    // An earlier version of this comment said HDR was impossible for a seat because the RDP
    // surface has no EDID. That was wrong, and measuring it is what showed so: inside a live seat
    // the active RDP target reports advancedColorSupported = TRUE with advancedColorEnabled =
    // false at 8 bits per channel. The capability is advertised; what does not follow is the
    // VidPN SOURCE mode, which stays SDR.
    //
    // Nonary (Vibepollo/Vibeshine) demonstrated HDR working inside a terminal session by forcing
    // Windows to rebuild that source mode — an FP16 shared-displayable primary, then
    // D3DKMTSetVidPnSourceOwner and D3DKMTSetDisplayMode with PreserveVidPn=FALSE — all user
    // mode. See issue #15. MultiSeat does not implement that yet, which is why this flag does
    // nothing rather than why HDR is impossible.
    //
    // To check a host: GET /api/seats/{id}/diagnostics/advanced-color, or run
    // MultiSeat.Service.exe --advanced-color <file> inside the session.
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

    // ── Shared game library ──────────────────────────────────────────
    // Create a shared games/ROMs location all seat accounts can read/write, so a Steam game
    // installed by one seat's account is not re-downloaded by another owning account, and ROMs
    // live in one place. Creates {SharedGameLibraryDir}\SteamLibrary and \ROMs at startup and
    // grants BUILTIN\Users Modify. Point each seat's Steam at the SteamLibrary folder manually.
    public bool EnableSharedGameLibrary { get; set; } = true;
    public string SharedGameLibraryDir { get; set; } = @"C:\MultiSeatGames";

    // ── Emulator netplay ─────────────────────────────────────────────
    // Assign each seat a deterministic, collision-free netplay port from its own port block
    // (seat.PortBase + Constants.OffsetRetroArchNetplay) and open it in the firewall. Seats
    // netplay each other over loopback (127.0.0.1:<host-seat-port>).
    public bool EnableEmulatorNetplay { get; set; } = true;

    // Opt-in: seed each seat user's retroarch.cfg with its netplay port + the shared ROM dir.
    // Off by default because it writes into a user-profile / emulator config file.
    public bool SeedRetroArchNetplayConfig { get; set; } = false;

    // Override for the seat's RetroArch config path. Empty → auto-detect
    // C:\Users\{AccountName}\AppData\Roaming\RetroArch\retroarch.cfg.
    public string RetroArchConfigPath { get; set; } = string.Empty;

    // ── Rebuild ───────────────────────────────────────────────────────
    // Absolute path to the repo root. Required for the dashboard Rebuild button.
    // Example: C:\MultiSeat-Development
    public string SourceDir { get; set; } = string.Empty;
}

/// <summary>
/// Where a seat's game audio is rendered, and therefore what Apollo captures.
/// </summary>
public enum AudioMode
{
    /// <summary>
    /// Seats render onto the HOST's audio subsystem (RDP audiomode:i:1) and each seat gets a
    /// dedicated host-side virtual cable (VB-CABLE / VoiceMeeter) that Apollo names as its
    /// virtual_sink. Requires those cables installed — one per seat, which caps seats at 4.
    ///
    /// Known limitation, and the reason PerSession exists: every seat shares the host's single
    /// audio subsystem, so an active seat suspends the console session's own playback and its
    /// audio leaks onto the console's physical output (issues #10, #12). No amount of
    /// default-device juggling fixes that — there is one global default and one shared endpoint.
    ///
    /// Supports the Moonlight → game microphone path (stream_mic).
    /// </summary>
    SharedHost,

    /// <summary>
    /// Each seat keeps its audio INSIDE its own RDP session (audiomode:i:0). Windows gives every
    /// session a private "Remote Audio" render endpoint and makes it that session's default, so
    /// games play to it automatically and Apollo loopback-captures it from within the session.
    /// The host's physical devices are never a render target for any seat, which is what makes
    /// this isolation real rather than negotiated.
    ///
    /// Needs NO virtual audio cables — no VB-CABLE, no VoiceMeeter — and therefore has no
    /// 4-seat audio ceiling. The redirected stream still reaches the console-side mstsc, so
    /// SessionLauncher.MuteMstscAudio becomes load-bearing here: it is what stops seat audio
    /// playing out of the host's speakers.
    ///
    /// Two hard-won rules, both verified in the field before we shipped this:
    ///   - Do NOT name the endpoint. Writing it to audio_sink makes Apollo re-role it; writing
    ///     it to virtual_sink makes Apollo rewrite its wave format, which breaks the endpoint
    ///     for every loopback client including Apollo itself. Leave both keys unset and Apollo
    ///     simply takes the session default, which is already the endpoint we want.
    ///   - Client-side "Play audio on host PC" must be ON (the opposite of SharedHost). That is
    ///     safe here because the "host" of a redirected session IS the seat's own session.
    ///
    /// COST: no microphone. A session that keeps its own audio cannot see the host's Steam
    /// Streaming Microphone, so stream_mic is written disabled. Game audio out works; the
    /// Moonlight → game mic path does not. Installs that need the mic should stay on SharedHost.
    /// </summary>
    PerSession,
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
