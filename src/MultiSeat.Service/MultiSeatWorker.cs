using System.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using MultiSeat.Service.Api;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Input;
using MultiSeat.Service.Monitoring;
using MultiSeat.Service.Sessions;
using MultiSeat.Service.Streaming;

namespace MultiSeat.Service;

/// <summary>
/// Primary background service. Runs the embedded API server and
/// periodic health checks for all active seats.
/// </summary>
public sealed class MultiSeatWorker : BackgroundService
{
    private readonly ILogger<MultiSeatWorker> _logger;
    private readonly MultiSeatOptions _options;
    private readonly SeatManager _seatManager;
    private readonly RdpWrapper _rdpWrapper;
    private readonly SessionHealthCheck _healthCheck;
    private readonly InputRouter _inputRouter;
    private readonly InputHookManager _inputHookManager;
    private readonly HidHideConfigurator _hidHide;
    private readonly DeviceWatcher _deviceWatcher;
    private readonly FirewallManager _firewall;
    private readonly IServiceProvider _services;

    private WebApplication? _apiApp;

    public MultiSeatWorker(
        ILogger<MultiSeatWorker> logger,
        IOptions<MultiSeatOptions> options,
        SeatManager seatManager,
        RdpWrapper rdpWrapper,
        SessionHealthCheck healthCheck,
        InputRouter inputRouter,
        InputHookManager inputHookManager,
        HidHideConfigurator hidHide,
        DeviceWatcher deviceWatcher,
        FirewallManager firewall,
        IServiceProvider services)
    {
        _logger = logger;
        _options = options.Value;
        _seatManager = seatManager;
        _rdpWrapper = rdpWrapper;
        _healthCheck = healthCheck;
        _inputRouter = inputRouter;
        _inputHookManager = inputHookManager;
        _hidHide = hidHide;
        _deviceWatcher = deviceWatcher;
        _firewall = firewall;
        _services = services;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MultiSeat Service starting — Windows {Build}",
            Environment.OSVersion.VersionString);

        // ── Step 0: Kill orphaned Apollo processes from previous run ──
        // On restart, in-memory seat state is lost. Any Apollo instances still
        // running from the previous run hold their ports, causing port conflicts
        // when new seats are provisioned. Kill them before starting.
        KillOrphanedApolloProcesses();

        // ── Step 0b: Set DWM frame interval for RDP sessions ────────
        // The Microsoft Remote Display Adapter's DWM composition rate defaults
        // to ~33ms (~30fps), causing the 32Hz cap Apollo sees. Lower the interval
        // so RDP sessions compose at the requested frame rate.
        SetDwmFrameInterval();

        // ── Step 1: Verify multi-session is available ────────────────
        if (!_rdpWrapper.EnsureMultiSession())
        {
            _logger.LogError(
                "RDP Wrapper multi-session patch not detected. " +
                "Concurrent sessions will not work. Install RDP Wrapper Library and restart.");
        }

        // ── Step 2: Start input subsystems ─────────────────────────────
        // Clear any HidHide state left over from a previous run before starting.
        // HidHide is a kernel driver — its blacklist and cloak state survive reboots.
        // Without this reset, devices hidden in a previous session stay hidden after reboot.
        _hidHide.ResetOnStartup();
        _inputRouter.Start();
        _inputHookManager.Start();
        _deviceWatcher.Start();
        _logger.LogInformation("Input subsystems started");

        // ── Step 3: Ensure API port is open in Windows Firewall ──────
        // The dashboard must be reachable from LAN devices (e.g. ROG Ally).
        // Windows Firewall blocks inbound connections by default; no install
        // script adds this rule, so we ensure it exists on every startup.
        await _firewall.EnsureApiPortOpenAsync(_options.ApiPort, stoppingToken);

        // ── Step 4: Start embedded API server ────────────────────────
        _apiApp = ApiServer.Build(_services, _options);
        _ = _apiApp.RunAsync(stoppingToken);
        _logger.LogInformation("API server listening on port {Port}", _options.ApiPort);

        // ── Step 5: Health-check loop ────────────────────────────────
        using var timer = new PeriodicTimer(
            TimeSpan.FromMilliseconds(_options.HealthCheckIntervalMs));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _healthCheck.CheckAllSeatsAsync(_seatManager, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Health check cycle failed");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private void KillOrphanedApolloProcesses()
    {
        var exeName = Path.GetFileNameWithoutExtension(_options.ApolloExePath); // "sunshine"
        try
        {
            var procs = Process.GetProcessesByName(exeName);
            if (procs.Length == 0) return;

            _logger.LogInformation(
                "Killing {Count} orphaned Apollo process(es) from previous run",
                procs.Length);

            foreach (var proc in procs)
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(3000);
                    _logger.LogDebug("Killed orphaned Apollo PID {Pid}", proc.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to kill orphaned Apollo PID {Pid}", proc.Id);
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during orphaned Apollo cleanup");
        }
    }

    /// <summary>
    /// Set DWMFRAMEINTERVAL in the Terminal Server WinStations registry key.
    /// This controls the DWM composition interval for RDP sessions.
    /// Default is ~33ms (~30fps). We set it to 1ms to allow DWM to compose
    /// at the maximum rate the display/encoder can handle.
    /// Requires service restart + new RDP sessions to take effect.
    /// </summary>
    private void SetDwmFrameInterval()
    {
        const string keyPath = @"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations";
        const string valueName = "DWMFRAMEINTERVAL";
        // 1ms = let DWM run as fast as possible; the actual rate is capped by
        // the display's refresh rate and encoder throughput.
        const int intervalMs = 1;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath, writable: true);
            if (key is null)
            {
                _logger.LogWarning(
                    "Registry key HKLM\\{Path} not found — cannot set DWM frame interval",
                    keyPath);
                return;
            }

            var current = key.GetValue(valueName);
            if (current is int currentVal && currentVal == intervalMs)
            {
                _logger.LogDebug("DWMFRAMEINTERVAL already set to {Ms}ms", intervalMs);
                return;
            }

            key.SetValue(valueName, intervalMs, RegistryValueKind.DWord);
            _logger.LogInformation(
                "Set DWMFRAMEINTERVAL to {Ms}ms (was {Old}) — " +
                "new RDP sessions will use high-framerate DWM composition",
                intervalMs, current ?? "unset");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to set DWMFRAMEINTERVAL — RDP sessions may be capped at ~30fps");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MultiSeat Service stopping — tearing down all seats");

        await _seatManager.TeardownAllAsync(cancellationToken);

        // Stop input subsystems
        _deviceWatcher.Stop();
        _inputHookManager.Stop();
        _inputRouter.Stop();

        if (_apiApp is not null)
            await _apiApp.StopAsync(cancellationToken);

        await base.StopAsync(cancellationToken);
    }
}
