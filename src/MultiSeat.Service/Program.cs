using MultiSeat.Service;
using MultiSeat.Service.Accounts;
using MultiSeat.Service.Api;
using MultiSeat.Service.Audio;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Display;
using MultiSeat.Service.Emulators;
using MultiSeat.Service.Input;
using MultiSeat.Service.Monitoring;
using MultiSeat.Service.Sessions;
using MultiSeat.Service.Storage;
using MultiSeat.Service.Streaming;

// ── Click-dialog-by-PID helper mode ──────────────────────────────────
// Finds any visible top-level window owned by <pid> that contains a child
// button matching <buttonText>, and clicks it. Does not depend on window title.
// Usage: MultiSeat.Service.exe --click-dialog-pid <pid> "Button Text" [timeoutMs]
if (args.Length >= 3 && args[0] == "--click-dialog-pid" && int.TryParse(args[1], out var clickPid))
{
    var timeoutMs = args.Length >= 4 && int.TryParse(args[3], out var t) ? t : 8000;
    return MultiSeat.Service.Sessions.DialogClickHelper.RunByPid(clickPid, args[2], timeoutMs);
}

// ── Click-dialog helper mode ──────────────────────────────────────────
// Invoked by the service via CreateProcessAsUser in the console session.
// Polls for a window with the given title, finds the named button, and clicks it.
// Usage: MultiSeat.Service.exe --click-dialog "Window Title" "Button Text" [timeoutMs]
if (args.Length >= 3 && args[0] == "--click-dialog")
{
    var timeoutMs = args.Length == 4 && int.TryParse(args[3], out var t) ? t : 8000;
    return MultiSeat.Service.Sessions.DialogClickHelper.Run(args[1], args[2], timeoutMs);
}

// ── Mute-audio helper mode ────────────────────────────────────────────
// Invoked by the service via CreateProcessAsUser in the console session so
// that the Core Audio API sees Session 1's audio sessions (session-scoped).
// Usage: MultiSeat.Service.exe --mute-audio <pid> [timeoutMs]
// A process has no audio session until it first renders audio, which under AudioMode.PerSession
// happens well after mstsc launches — so 0 means one attempt, >0 polls until that deadline, and
// -1 watches for the target process's whole lifetime (what PerSession uses).
if (args.Length >= 2 && args[0] == "--mute-audio" && int.TryParse(args[1], out var pidToMute))
{
    var muteTimeoutMs = args.Length >= 3 && int.TryParse(args[2], out var t) ? t : 0;
    return AudioMuteHelper.MuteByPid(pidToMute, muteTimeoutMs) ? 0 : 1;
}

// ── Audio-peak reporting mode ─────────────────────────────────────────
// Reports which render endpoints are actually carrying audio, and from which process,
// by polling peak meters. This host is headless and reached over RustDesk, so "play a
// sound and listen" is not an available measurement — and RustDesk re-routes host audio,
// which would confound any listening test of audio routing. This is the objective
// substitute.
//
// Session-scoped like --mute-audio: run it INSIDE the session you want to measure
// (console session for host audio, a seat/RDP session for that session's audio).
// Usage: MultiSeat.Service.exe --audio-peaks [seconds]
if (args.Length >= 1 && args[0] == "--audio-peaks")
{
    var window = args.Length >= 2 && double.TryParse(args[1], out var secs) ? secs : 5.0;
    return AudioPeakHelper.ReportPeaks(window) ? 0 : 1;
}

// ── Loopback-capture helper mode ──────────────────────────────────────
// Records what is being played TO an output endpoint and reports the peak amplitude
// captured. --audio-peaks proves audio is FLOWING to an endpoint; this proves it can be
// CAPTURED FROM one, which is a different claim and the go/no-go gate for per-session
// audio (#10/#12): each seat would render to the private "Remote Audio" endpoint RDP
// creates in its session, with Apollo loopback-capturing that. On some Windows builds
// capture from it silently yields nothing.
//
// Session-scoped like --audio-peaks — "Remote Audio" exists only inside its own session.
// <device> is a friendly-name substring, a full endpoint id, or "default".
// Usage: MultiSeat.Service.exe --capture-loopback <device> [seconds] [out.wav]
if (args.Length >= 2 && args[0] == "--capture-loopback")
{
    var captureSeconds = args.Length >= 3 && double.TryParse(args[2], out var cs) ? cs : 10.0;
    var outPath = args.Length >= 4
        ? args[3]
        : Path.Combine(Path.GetTempPath(), "loopback-capture.wav");

    return AudioLoopbackCaptureHelper.CaptureLoopback(args[1], captureSeconds, outPath) ? 0 : 1;
}

// ── Hide-windows helper mode ──────────────────────────────────────────
// Invoked by the service via CreateProcessAsUser in the console session so that
// EnumWindows sees the console user's windows (window enumeration is per-desktop;
// Session 0 cannot see the console session's mstsc window). Hides the mstsc
// window that holds a seat's RDP session Active (GitHub issue #8).
// Usage: MultiSeat.Service.exe --hide-windows <pid>
if (args.Length == 2 && args[0] == "--hide-windows" && int.TryParse(args[1], out var pidToHide))
{
    return WindowHideHelper.HideByPid(pidToHide) ? 0 : 1;
}

// With a duration, stays resident and keeps the RDP client window hidden instead of hiding
// once: mstsc re-shows that window on connect, on reconnect, and when the session resolution
// changes, and a single hide leaves it covering the console user's screen (where closing it —
// the obvious response — disconnects the seat).
// Usage: MultiSeat.Service.exe --hide-windows <pid> <seconds | -1 for the process's lifetime>
if (args.Length == 3 && args[0] == "--hide-windows"
    && int.TryParse(args[1], out var pidToWatch) && int.TryParse(args[2], out var hideSeconds))
{
    return WindowHideHelper.WatchAndHide(pidToWatch, hideSeconds) ? 0 : 1;
}

// ── Enum-displays helper mode ─────────────────────────────────────────
// Launched inside the console session via CreateProcessAsUser so that
// QueryDisplayConfig sees the real display topology (Session 0 has no displays).
// Usage: MultiSeat.Service.exe --enum-displays <output-json-file>
if (args.Length == 2 && args[0] == "--enum-displays")
{
    return MultiSeat.Service.Display.DisplayEnumeratorHelper.RunAndWriteToFile(args[1]);
}

// ── Set-default-capture helper mode ──────────────────────────────────
// Sets the Windows default audio capture (microphone) device for the current session.
// Usage: MultiSeat.Service.exe --set-default-capture <deviceId>
if (args.Length == 2 && args[0] == "--set-default-capture")
{
    return MultiSeat.Service.Audio.AudioCaptureHelper.SetDefaultAudioDevice(args[1]) ? 0 : 1;
}

// ── Set-default-render helper mode ───────────────────────────────────
// Sets the Windows default audio render (output) device for the current session so
// games automatically output game audio to the assigned VAC device. Apollo
// loopback-captures that device for audio_sink streaming to Moonlight.
// Usage: MultiSeat.Service.exe --set-default-render <deviceId>
if (args.Length == 2 && args[0] == "--set-default-render")
{
    return MultiSeat.Service.Audio.AudioCaptureHelper.SetDefaultAudioDevice(args[1]) ? 0 : 1;
}

// ── Setup-display-isolation helper mode ──────────────────────────────
// Invoked inside a seat's RDP session after Apollo has created the SudoVDA display.
// Makes SudoVDA the primary display and shrinks the RDP display to 640×480 so
// TermService only encodes a tiny secondary display instead of full game content.
// The SudoVDA IddCx device path (SeatInfo.DisplayDevicePath / Apollo's output_name)
// MUST be passed — without it the helper has no safe way to disambiguate between
// multiple active SudoMaker monitor instances and refuses to run.
// Usage: MultiSeat.Service.exe --setup-display-isolation <sudovda-iddcx-path>
if (args.Length == 2 && args[0] == "--setup-display-isolation")
{
    return DisplayModeHelper.SetupDisplayIsolation(args[1]);
}

// ── Set-display-hz helper mode ────────────────────────────────────────
// 2-arg form: invoked inside a seat's RDP session — targets the session-primary display
// (Microsoft Remote Display Adapter) via ChangeDisplaySettingsEx(null, ...).
// Usage: MultiSeat.Service.exe --set-display-hz <hz>
if (args.Length == 2 && args[0] == "--set-display-hz" && int.TryParse(args[1], out var hz))
{
    return DisplayModeHelper.SetPrimaryDisplayHz(hz) ? 0 : 1;
}
// 3-arg form: invoked inside the console session — targets a specific GDI display device
// (e.g. \\.\DISPLAY5) so that IddCx Console displays (SudoVDA) can be reached from
// within WinSta0\Default where their display pipeline lives.
// Usage: MultiSeat.Service.exe --set-display-hz <deviceName> <hz>
if (args.Length == 3 && args[0] == "--set-display-hz" && int.TryParse(args[2], out var hz3))
{
    return DisplayModeHelper.SetDisplayHz(args[1], hz3) ? 0 : 1;
}

var builder = Host.CreateApplicationBuilder(args);

// ── Configuration ────────────────────────────────────────────────────
// Ensure we load config from the exe's directory, not the working directory
var exeDir = AppContext.BaseDirectory;
builder.Configuration.AddJsonFile(Path.Combine(exeDir, "appsettings.json"), optional: true, reloadOnChange: true);
builder.Services.Configure<MultiSeatOptions>(builder.Configuration.GetSection(MultiSeatOptions.SectionName));

// ── Windows Service support ──────────────────────────────────────────
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "MultiSeatService";
});

// ── Core services (singletons — one per host lifetime) ───────────────
builder.Services.AddSingleton<AccountManager>();
builder.Services.AddSingleton<SessionLauncher>();
builder.Services.AddSingleton<RdpWrapper>();
builder.Services.AddSingleton<ProcessInjector>();
builder.Services.AddSingleton<VirtualDisplayManager>();
builder.Services.AddSingleton<ApolloManager>();
builder.Services.AddSingleton<ApolloConfigBuilder>();
builder.Services.AddSingleton<OnConnectAppLauncher>();
builder.Services.AddSingleton<PortAllocator>();
builder.Services.AddSingleton<AudioDeviceEnumerator>();
builder.Services.AddSingleton<AudioRouter>();
builder.Services.AddSingleton<ControllerManager>();
builder.Services.AddSingleton<InputRouter>();
builder.Services.AddSingleton<InputHookManager>();
builder.Services.AddSingleton<HidHideConfigurator>();
builder.Services.AddSingleton<FirewallManager>();
builder.Services.AddSingleton<SeatPresetStore>();
builder.Services.AddSingleton<GpuMonitor>();
builder.Services.AddSingleton<MetricsCollector>();
builder.Services.AddSingleton<SessionHealthCheck>();

// Shared game library + emulator config seeders (register each seeder as IEmulatorConfigSeeder
// so SeatManager picks them up; add Dolphin/PCSX2 seeders here later with no other changes).
builder.Services.AddSingleton<SharedLibraryProvisioner>();
builder.Services.AddSingleton<IEmulatorConfigSeeder, RetroArchConfigSeeder>();

builder.Services.AddSingleton<SeatManager>();

// ── Background workers ──────────────────────────────────────────────
builder.Services.AddHostedService<MultiSeatWorker>();

// ── Embedded API server ──────────────────────────────────────────────
ApiServer.ConfigureServices(builder.Services, builder.Configuration);

var host = builder.Build();

// ── Log-filter inspection mode ────────────────────────────────────────
// Reports which levels actually reach each logging provider — above all the Windows Event
// Log, the only destination a service has. Unlike the helper modes at the top of this file
// this one runs AFTER Build(), on purpose: it must inspect the REAL host's configuration.
// Reconstructing an equivalent host here would be free to drift from the one that ships,
// which would make the diagnostic worse than useless. Building the host does not start it —
// no hosted service runs and no port is bound — so this is safe to run on a live machine.
//
// Exit code 0 = the service's Information logs reach the Event Log, 1 = they do not.
// Usage: MultiSeat.Service.exe --log-filters
if (args.Contains("--log-filters"))
    return MultiSeat.Service.Diagnostics.LogFilterInspector.Run(host.Services);

await host.RunAsync();
return 0;
