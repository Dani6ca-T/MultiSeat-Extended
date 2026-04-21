using MultiSeat.Service;
using MultiSeat.Service.Accounts;
using MultiSeat.Service.Api;
using MultiSeat.Service.Audio;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Display;
using MultiSeat.Service.Input;
using MultiSeat.Service.Monitoring;
using MultiSeat.Service.Sessions;
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
// Usage: MultiSeat.Service.exe --mute-audio <pid>
if (args.Length == 2 && args[0] == "--mute-audio" && int.TryParse(args[1], out var pidToMute))
{
    return AudioMuteHelper.MuteByPid(pidToMute) ? 0 : 1;
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
builder.Services.AddSingleton<PortAllocator>();
builder.Services.AddSingleton<AudioDeviceEnumerator>();
builder.Services.AddSingleton<AudioRouter>();
builder.Services.AddSingleton<ControllerManager>();
builder.Services.AddSingleton<InputRouter>();
builder.Services.AddSingleton<InputHookManager>();
builder.Services.AddSingleton<DeviceWatcher>();
builder.Services.AddSingleton<HidHideConfigurator>();
builder.Services.AddSingleton<FirewallManager>();
builder.Services.AddSingleton<SeatPresetStore>();
builder.Services.AddSingleton<GpuMonitor>();
builder.Services.AddSingleton<MetricsCollector>();
builder.Services.AddSingleton<SessionHealthCheck>();
builder.Services.AddSingleton<SeatManager>();

// ── Background workers ──────────────────────────────────────────────
builder.Services.AddHostedService<MultiSeatWorker>();

// ── Embedded API server ──────────────────────────────────────────────
ApiServer.ConfigureServices(builder.Services, builder.Configuration);

var host = builder.Build();

// ── Map API endpoints ────────────────────────────────────────────────
if (host is IApplicationBuilder appHost)
{
    // This path is for the WebApplication case; for Worker Service we
    // start the API server inside MultiSeatWorker instead.
}

await host.RunAsync();
return 0;
