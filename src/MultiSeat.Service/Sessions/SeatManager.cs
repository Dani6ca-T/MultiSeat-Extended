using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Accounts;
using MultiSeat.Service.Api;
using MultiSeat.Service.Audio;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Display;
using MultiSeat.Service.Input;
using MultiSeat.Service.Streaming;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Sessions;

/// <summary>
/// Top-level orchestrator for seat lifecycle.
/// Coordinates all subsystems to provision, configure, and tear down seats.
///
/// Provisioning pipeline (order matters — each step depends on previous):
///   1. Validate capacity + account
///   2. Allocate port block
///   3. Launch background Windows session
///   4. Create virtual display (SudoVDA)
///   5. Open firewall ports
///   6. Start Apollo streaming server (needs display + ports)
///   7. Assign VAC audio cable + update Apollo config
///   8. Create ViGEm controller + HidHide cloaking
///   9. Broadcast Ready state to WebSocket clients
///
/// Teardown is reverse order with best-effort exception handling.
/// </summary>
public sealed class SeatManager
{
    private readonly ConcurrentDictionary<Guid, SeatInfo> _seats = new();
    private readonly ILogger<SeatManager> _logger;
    private readonly MultiSeatOptions _options;
    private readonly AccountManager _accounts;
    private readonly SessionLauncher _sessionLauncher;
    private readonly ProcessInjector _processInjector;
    private readonly VirtualDisplayManager _displayManager;
    private readonly ApolloManager _apolloManager;
    private readonly ApolloConfigBuilder _configBuilder;
    private readonly PortAllocator _portAllocator;
    private readonly FirewallManager _firewall;
    private readonly AudioRouter _audioRouter;
    private readonly ControllerManager _controllerManager;
    private readonly InputRouter _inputRouter;
    private readonly InputHookManager _inputHookManager;
    private readonly HidHideConfigurator _hidHide;
    private readonly OnConnectAppLauncher _onConnectApps;

    public SeatManager(
        ILogger<SeatManager> logger,
        IOptions<MultiSeatOptions> options,
        AccountManager accounts,
        SessionLauncher sessionLauncher,
        ProcessInjector processInjector,
        VirtualDisplayManager displayManager,
        ApolloManager apolloManager,
        ApolloConfigBuilder configBuilder,
        PortAllocator portAllocator,
        FirewallManager firewall,
        AudioRouter audioRouter,
        ControllerManager controllerManager,
        InputRouter inputRouter,
        InputHookManager inputHookManager,
        HidHideConfigurator hidHide,
        OnConnectAppLauncher onConnectApps)
    {
        _logger = logger;
        _options = options.Value;
        _accounts = accounts;
        _sessionLauncher = sessionLauncher;
        _processInjector = processInjector;
        _displayManager = displayManager;
        _apolloManager = apolloManager;
        _configBuilder = configBuilder;
        _portAllocator = portAllocator;
        _firewall = firewall;
        _audioRouter = audioRouter;
        _controllerManager = controllerManager;
        _inputRouter = inputRouter;
        _inputHookManager = inputHookManager;
        _hidHide = hidHide;
        _onConnectApps = onConnectApps;
    }

    public int ActiveSeatCount => _seats.Count(s => s.Value.Status is not SeatStatus.Idle and not SeatStatus.Error);
    public IReadOnlyCollection<SeatInfo> GetAllSeats() => _seats.Values.ToList().AsReadOnly();
    public SeatInfo? GetSeat(Guid id) => _seats.GetValueOrDefault(id);

    /// <summary>
    /// Full seat provisioning pipeline.
    /// </summary>
    public async Task<SeatInfo> ProvisionSeatAsync(SeatRequest request, CancellationToken ct)
    {
        // Count only live seats — Error/Idle entries hold no resources (their ports and
        // sessions were already released on failure) and must not block new provisioning.
        if (ActiveSeatCount >= _options.MaxSeats)
            throw new InvalidOperationException($"Maximum seat count ({_options.MaxSeats}) reached.");

        if (!_accounts.AccountExists(request.AccountName))
            throw new InvalidOperationException($"Account '{request.AccountName}' does not exist. Create it first via /api/accounts.");

        var seat = new SeatInfo
        {
            AccountName = request.AccountName,
            Width = request.Width,
            Height = request.Height,
            Fps = request.Fps,
            LaunchApp = request.LaunchApp,
            NvencPreset = request.NvencPreset,
            Status = SeatStatus.Provisioning,
            ProvisioningStep = "Session"
        };

        _seats.TryAdd(seat.Id, seat);
        await BroadcastState(seat);

        try
        {
            // ── 1. Allocate ports ─────────────────────────────────────
            seat.PortBase = _portAllocator.Allocate();
            _logger.LogInformation("Seat {Id}: ports {Base}-{End}",
                seat.Id, seat.PortBase, seat.PortBase + Shared.Constants.PortsPerSeat - 1);

            // ── 2. Launch background session ──────────────────────────
            seat.SessionId = await _sessionLauncher.LaunchSessionAsync(
                seat.AccountName, ct);
            _logger.LogInformation("Seat {Id}: Windows session {Sid}", seat.Id, seat.SessionId);

            seat.Status = SeatStatus.Configuring;
            seat.ProvisioningStep = "Display";
            await BroadcastState(seat);

            // ── 2.5. Suppress RustDesk audio capture in seat session ──────────
            // RustDesk.exe runs in every session and opens the default render
            // endpoint in exclusive WASAPI mode at startup, causing
            // AUDCLNT_E_DEVICE_IN_USE (0x8889000A) for Apollo's loopback.
            // Write a per-user RustDesk2.toml with enable-audio=N before the
            // audio default is set, then kill any RustDesk that started before
            // the config landed. RustDesk re-reads config on each launch, so
            // the service's auto-restart will pick up the new setting.
            try
            {
                var rustDeskConfigDir = Path.Combine(
                    @"C:\Users", seat.AccountName,
                    @"AppData\Roaming\RustDesk\config");
                Directory.CreateDirectory(rustDeskConfigDir);
                var rustDeskConfig = Path.Combine(rustDeskConfigDir, "RustDesk2.toml");
                await File.WriteAllTextAsync(rustDeskConfig,
                    "[options]\nenable-audio = \"N\"\n", ct);
                _logger.LogInformation(
                    "Seat {Id}: wrote RustDesk audio-disable config to {Path}",
                    seat.Id, rustDeskConfig);

                var killed = 0;
                foreach (var p in Process.GetProcessesByName("RustDesk"))
                {
                    try
                    {
                        if (p.SessionId == seat.SessionId)
                        {
                            p.Kill();
                            killed++;
                        }
                    }
                    catch { /* already exited */ }
                    finally { p.Dispose(); }
                }
                if (killed > 0)
                    _logger.LogInformation(
                        "Seat {Id}: killed {N} RustDesk process(es) in session {Sid}",
                        seat.Id, killed, seat.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Seat {Id}: could not suppress RustDesk audio (non-critical)", seat.Id);
            }

            // ── 3. Virtual display ────────────────────────────────────
            await _displayManager.CreateDisplayAsync(seat, ct);
            _logger.LogDebug("Seat {Id}: VDA ready ({W}x{H}@{F})",
                seat.Id, seat.Width, seat.Height, seat.Fps);

            // ── 4. Firewall ───────────────────────────────────────────
            await _firewall.OpenPortsAsync(seat, ct);

            // ── 5. Audio routing ──────────────────────────────────────
            seat.ProvisioningStep = "Audio";
            await BroadcastState(seat);

            // Assign VAC before Apollo so the config has the audio device
            seat.VacCableIndex = _audioRouter.AssignCable(seat);
            _logger.LogDebug("Seat {Id}: VAC cable {C}", seat.Id, seat.VacCableIndex);

            // ── 5.5. Set seat session default capture for mic routing ────
            // Apollo renders Moonlight mic audio into CABLE Input (virtual_sink) from
            // inside the seat session. CABLE Output (the capture counterpart) receives
            // that audio at the kernel WDM level — visible in the seat session as a
            // capture endpoint. Setting CABLE Output as the DEFAULT capture for THIS
            // seat session means games automatically use Moonlight mic without any
            // manual device selection.
            //   Moonlight mic → Apollo → CABLE Input → CABLE Output → games (session default)
            // Running inside the seat session scopes the IPolicyConfig call to that
            // session's HKCU, so multiple seats don't conflict with each other.
            if (!string.IsNullOrEmpty(seat.AudioCaptureDeviceId))
            {
                try
                {
                    var helperExe = Path.Combine(AppContext.BaseDirectory, "MultiSeat.Service.exe");
                    _sessionLauncher.RunHelperInSeatSession(
                        seat.SessionId, seat.AccountName,
                        $"\"{helperExe}\" --set-default-capture \"{seat.AudioCaptureDeviceId}\"");
                    _logger.LogInformation(
                        "Seat {Id}: session capture default set to {DeviceId} (mic)",
                        seat.Id, seat.AudioCaptureDeviceId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Seat {Id}: could not set session capture device (non-critical)", seat.Id);
                }
            }

            // Set game audio render device as the session's default output so games
            // automatically route audio there. Apollo loopback-captures this device
            // for audio_sink. Requires audiomode:i:1 in Default.rdp (set by SessionLauncher).
            if (!string.IsNullOrEmpty(seat.AudioGameRenderDeviceId))
            {
                try
                {
                    var helperExe = Path.Combine(AppContext.BaseDirectory, "MultiSeat.Service.exe");
                    _sessionLauncher.RunHelperInSeatSession(
                        seat.SessionId, seat.AccountName,
                        $"\"{helperExe}\" --set-default-render \"{seat.AudioGameRenderDeviceId}\"");
                    _logger.LogInformation(
                        "Seat {Id}: session render default set to {DeviceId} (game audio)",
                        seat.Id, seat.AudioGameRenderDeviceId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Seat {Id}: could not set session render device (non-critical)", seat.Id);
                }
            }

            // ── 6. Apollo streaming ───────────────────────────────────────
            // Apollo is launched AFTER display + audio so it can capture both.
            // The session is still ACTIVE (mstsc connected) so Apollo's SudoVDA IPC
            // can initialize the virtual display. Without an active session,
            // QueryDisplayConfig returns ERROR_ACCESS_DENIED and the encoder probe fails.
            seat.ProvisioningStep = "Apollo";
            await BroadcastState(seat);

            seat.ApolloProcessId = await _apolloManager.StartAsync(seat, ct);
            _logger.LogInformation("Seat {Id}: Apollo PID {Pid}", seat.Id, seat.ApolloProcessId);

            // ── 6.5: Discover SudoVDA UUID from Apollo's startup log ──────
            // Apollo enumerates displays at startup and writes device UUIDs to its log.
            // UUID (device_id) works at stream LAUNCH time; GDI path (\\.\DISPLAYx) causes
            // Apollo to fall back to the primary monitor.
            // After the first-pass probe completes, Apollo has cached encoder results.
            // The second start with UUID skips the full probe (uses cache), so the
            // SudoVDA IddCx watchdog has time to establish its connection properly.
            seat.ProvisioningStep = "DetectDisplay";
            await BroadcastState(seat);

            {
                var logPath = _apolloManager.GetLogPath(seat.AccountName, _options.ApolloConfigDir);
                var configPath = _apolloManager.GetConfigPath(seat.Id);

                // Wait for Apollo to initialize SudoVDA IPC and write its display log.
                // The session MUST stay ACTIVE (mstsc connected) — Apollo calls QueryDisplayConfig
                // both at startup AND when each Moonlight client connects. Disconnected sessions
                // return ERROR_ACCESS_DENIED, causing "Failed to initialize video capture/encoding".
                await Task.Delay(5000, ct);

                // NOTE: We intentionally do NOT disconnect mstsc here.
                // The session stays Active for the lifetime of the seat so Apollo can
                // always query and set display modes when clients connect.

                var displayId = _apolloManager.ParseSudoVdaDisplayId(logPath);
                if (displayId != null && configPath != null)
                {
                    seat.DisplayDevicePath = displayId;
                    _configBuilder.UpdateDisplayOutput(configPath, displayId);

                    _logger.LogInformation(
                        "Seat {Id}: SudoVDA UUID discovered ({Dev}) — restarting Apollo with display target",
                        seat.Id, displayId);

                    // Restart Apollo with the correct output_name (UUID).
                    // Brief delay to let Apollo finish writing logs before we kill it.
                    _apolloManager.Stop(seat);
                    await Task.Delay(2000, ct);
                    seat.ApolloProcessId = await _apolloManager.StartAsync(seat, ct);

                    // ── 6.6/6.7: Display isolation + refresh-rate clamp ─────
                    await ApplyDisplayIsolationAsync(seat, ct);
                }
                else
                {
                    _logger.LogWarning(
                        "Seat {Id}: SudoVDA display not found in Apollo log — " +
                        "streaming will capture primary monitor instead of virtual display",
                        seat.Id);
                }
            }

            // ── 7. Controller + Input Routing ────────────────────────────
            // Only create a MultiSeat-managed ViGEm controller when explicitly enabled.
            // Apollo already handles controller forwarding from Moonlight clients natively
            // (controller = enabled / gamepad = auto in sunshine.conf). Creating a second
            // ViGEm controller here causes duplicate Xbox controllers in the session.
            if (_options.EnableViGEmController)
            {
                seat.ViGEmControllerIndex = _controllerManager.CreateController(seat);
                _logger.LogDebug("Seat {Id}: ViGEm controller {C}", seat.Id, seat.ViGEmControllerIndex);

                if (_options.AutoAssignControllers)
                {
                    var connected = _inputRouter.GetConnectedControllers();
                    var assigned = _inputRouter.GetAssignments();
                    var freeIdx = connected.FirstOrDefault(idx => !assigned.ContainsKey(idx), -1);
                    if (freeIdx >= 0)
                    {
                        _inputRouter.AssignController(freeIdx, seat.Id);
                        _logger.LogInformation("Seat {Id}: auto-assigned XInput {Idx}", seat.Id, freeIdx);
                    }
                }
            }
            else
            {
                _logger.LogDebug("Seat {Id}: ViGEm controller skipped — Apollo handles Moonlight client input natively", seat.Id);
            }

            // ── 8. HidHide + Keyboard/Mouse Hooks ──────────────────────
            _hidHide.CloakForSession(seat);

            // Install keyboard/mouse hooks to filter input for this session
            _inputHookManager.InstallForSession((uint)seat.SessionId);

            // ── 9. Ready ──────────────────────────────────────────────
            seat.Status = SeatStatus.Ready;
            seat.ReadyAt = DateTimeOffset.UtcNow;
            seat.ProvisioningStep = null;
            await BroadcastState(seat);
            _logger.LogInformation(
                "Seat {Id}: READY for Moonlight connection on port {P}",
                seat.Id, seat.PortBase + Shared.Constants.OffsetHttps);

            return seat;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Seat {Id}: provisioning failed at {Status}", seat.Id, seat.Status);
            seat.Status = SeatStatus.Error;
            seat.ErrorMessage = ex.Message;
            await BroadcastState(seat);

            // Best-effort teardown of whatever was already provisioned
            await TeardownSeatInternalAsync(seat, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Launch an application inside an active seat's session.
    /// </summary>
    public async Task LaunchAppInSeatAsync(Guid seatId, LaunchAppRequest request, CancellationToken ct)
    {
        var seat = GetSeat(seatId)
            ?? throw new InvalidOperationException("Seat not found.");

        if (seat.Status is not SeatStatus.Ready and not SeatStatus.Streaming)
            throw new InvalidOperationException($"Seat is in {seat.Status} state — cannot launch apps.");

        await _processInjector.LaunchInSessionAsync(
            seat.SessionId, seat.AccountName,
            request.ExecutablePath, request.Arguments, request.WorkingDirectory, ct);

        seat.Status = SeatStatus.Streaming;
        seat.LaunchApp = request.ExecutablePath;
        await BroadcastState(seat);
    }

    /// <summary>
    /// Teardown a single seat — reverse order of provisioning.
    /// </summary>
    public async Task TeardownSeatAsync(Guid seatId, CancellationToken ct)
    {
        if (!_seats.TryRemove(seatId, out var seat))
            return;

        seat.Status = SeatStatus.TearingDown;
        await BroadcastState(seat);
        await TeardownSeatInternalAsync(seat, ct);
        _logger.LogInformation("Seat {Id}: torn down", seat.Id);
    }

    /// <summary>
    /// Teardown all seats — called on service shutdown.
    /// </summary>
    public async Task TeardownAllAsync(CancellationToken ct)
    {
        var ids = _seats.Keys.ToList();
        await Task.WhenAll(ids.Select(id => TeardownSeatAsync(id, ct)));
    }

    private async Task TeardownSeatInternalAsync(SeatInfo seat, CancellationToken ct)
    {
        // Reverse order of provisioning — each step is best-effort
        try { _onConnectApps.Forget(seat.Id); } catch { /* best effort */ }
        try { _inputHookManager.Uninstall(); } catch { /* best effort */ }
        try { _hidHide.UncloakForSession(seat); } catch { /* best effort */ }
        try { UnassignControllersForSeat(seat.Id); } catch { /* best effort */ }
        try { _controllerManager.DestroyController(seat); } catch { /* best effort */ }
        try { _apolloManager.Stop(seat); } catch { /* best effort */ }
        try { _audioRouter.ReleaseCable(seat); } catch { /* best effort */ }
        try { await _firewall.ClosePortsAsync(seat, ct); } catch { /* best effort */ }
        try { await _displayManager.DestroyDisplayAsync(seat, ct); } catch { /* best effort */ }
        try { _sessionLauncher.DisconnectSession(seat.SessionId); } catch { /* best effort */ }
        try { _sessionLauncher.LogoffSession(seat.SessionId); } catch { /* best effort */ }
        try { _portAllocator.Release(seat.PortBase); } catch { /* best effort */ }

        // Clean up per-seat Apollo config directory
        try { _configBuilder.CleanupConfig(seat.AccountName, _options.ApolloConfigDir); } catch { /* best effort */ }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PER-SEAT SERVICE MANAGEMENT
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Get the live status of each subsystem for a seat.
    /// </summary>
    public SeatServices GetSeatServices(Guid seatId)
    {
        var seat = GetSeat(seatId);
        if (seat is null) return new SeatServices();

        return new SeatServices
        {
            Apollo = seat.ApolloProcessId > 0 && _apolloManager.IsAlive(seatId),
            ApolloRestarts = _apolloManager.GetRestartCount(seatId),
            Display = !string.IsNullOrEmpty(seat.DisplayDevicePath),
            Audio = seat.VacCableIndex >= 0,
            Controller = seat.ViGEmControllerIndex >= 0,
            InputHooks = _inputHookManager.IsInstalled,
            Firewall = seat.PortBase > 0,
            Session = seat.SessionId >= 0
        };
    }

    /// <summary>Stop Apollo for a seat without tearing down everything else.</summary>
    public void StopApollo(Guid seatId)
    {
        var seat = GetSeat(seatId)
            ?? throw new InvalidOperationException("Seat not found.");
        _apolloManager.Stop(seat);
        seat.ApolloProcessId = 0;
        _ = BroadcastState(seat);
        _logger.LogInformation("Seat {Id}: Apollo stopped by user", seatId);
    }

    /// <summary>Start Apollo for a seat (must already have session + display).</summary>
    public async Task StartApolloAsync(Guid seatId, CancellationToken ct)
    {
        var seat = GetSeat(seatId)
            ?? throw new InvalidOperationException("Seat not found.");

        if (seat.SessionId < 0)
            throw new InvalidOperationException("No active session — provision the seat first.");

        seat.ApolloProcessId = await _apolloManager.StartAsync(seat, ct);

        // Re-apply display config
        var configPath = _apolloManager.GetConfigPath(seat.Id);
        if (configPath is not null)
        {
            if (!string.IsNullOrEmpty(seat.DisplayDevicePath))
                _configBuilder.UpdateDisplayOutput(configPath, seat.DisplayDevicePath);
        }

        _ = BroadcastState(seat);
        _logger.LogInformation("Seat {Id}: Apollo started by user (PID {Pid})", seatId, seat.ApolloProcessId);
    }

    /// <summary>Restart Apollo for a seat (stop + start).</summary>
    public async Task RestartApolloAsync(Guid seatId, CancellationToken ct)
    {
        var seat = GetSeat(seatId)
            ?? throw new InvalidOperationException("Seat not found.");

        _apolloManager.Stop(seat);
        seat.ApolloProcessId = 0;

        seat.ApolloProcessId = await _apolloManager.StartAsync(seat, ct);

        var configPath = _apolloManager.GetConfigPath(seat.Id);
        if (configPath is not null)
        {
            if (!string.IsNullOrEmpty(seat.DisplayDevicePath))
                _configBuilder.UpdateDisplayOutput(configPath, seat.DisplayDevicePath);
        }

        if (seat.ApolloProcessId > 0)
            await ApplyDisplayIsolationAsync(seat, ct);

        _ = BroadcastState(seat);
        _logger.LogInformation("Seat {Id}: Apollo restarted by user (PID {Pid})", seatId, seat.ApolloProcessId);
    }

    /// <summary>
    /// Make SudoVDA the session primary, shrink the RDP virtual display to 640×480,
    /// and clamp SudoVDA's refresh rate to seat.Fps. Runs inside the seat's RDP session
    /// via the --setup-display-isolation and --set-display-hz helper modes.
    ///
    /// This state does not survive a session disconnect (sleep/wake) or an Apollo restart,
    /// so this method is called from every code path that (re)starts Apollo:
    ///   - Initial provisioning (after the SudoVDA-output restart).
    ///   - User-triggered RestartApolloAsync.
    ///   - SessionHealthCheck after sleep-reconnect or crash auto-restart.
    ///
    /// Without re-applying after a wake event, SudoVDA stops being primary and the
    /// stream falls back to the Microsoft Remote Display Adapter at its default
    /// 1024×768 — even though Apollo logs request 1920×1080.
    /// Both steps are best-effort; failures are logged and ignored.
    /// </summary>
    public async Task ApplyDisplayIsolationAsync(SeatInfo seat, CancellationToken ct)
    {
        var helperExe = Path.Combine(AppContext.BaseDirectory, "MultiSeat.Service.exe");

        // Skip isolation entirely if we don't know which SudoVDA Apollo created — the helper
        // would otherwise risk grabbing an orphan SudoVDA attached to another session
        // (e.g. the console's RustDesk display) and dragging its resolution along with the seat's.
        if (string.IsNullOrEmpty(seat.DisplayDevicePath))
        {
            _logger.LogWarning(
                "Seat {Id}: skipping display isolation — DisplayDevicePath is unset, " +
                "TermService CPU may be elevated",
                seat.Id);
            return;
        }

        // Let Apollo + SudoVDA IPC settle so the helper sees both displays.
        await Task.Delay(2000, ct);
        try
        {
            _sessionLauncher.RunHelperInSeatSession(
                seat.SessionId, seat.AccountName,
                $"\"{helperExe}\" --setup-display-isolation \"{seat.DisplayDevicePath}\"");
            _logger.LogInformation(
                "Seat {Id}: display isolation applied — SudoVDA is primary, RDP display shrunk to 640×480",
                seat.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Seat {Id}: display isolation failed (non-critical — TermService CPU may be elevated)",
                seat.Id);
        }

        // SudoVDA is now primary, so ChangeDisplaySettingsEx(null,...) in the helper
        // targets it directly. Clamp Hz to seat.Fps so games don't try to render at 1000fps.
        await Task.Delay(500, ct);
        try
        {
            _sessionLauncher.RunHelperInSeatSession(
                seat.SessionId, seat.AccountName,
                $"\"{helperExe}\" --set-display-hz {seat.Fps}");
            _logger.LogInformation(
                "Seat {Id}: SudoVDA refresh rate set to {Hz}Hz",
                seat.Id, seat.Fps);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Seat {Id}: could not set SudoVDA refresh rate (non-critical)", seat.Id);
        }
    }

    /// <summary>Reset the audio routing for a seat (release + re-assign cable + re-apply session defaults).</summary>
    public void ResetAudio(Guid seatId)
    {
        var seat = GetSeat(seatId)
            ?? throw new InvalidOperationException("Seat not found.");

        _audioRouter.ReleaseCable(seat);
        seat.VacCableIndex = _audioRouter.AssignCable(seat);

        ApplyAudioDefaults(seat);

        _ = BroadcastState(seat);
        _logger.LogInformation("Seat {Id}: audio reset, cable #{C}", seatId, seat.VacCableIndex);
    }

    /// <summary>
    /// Re-run the --set-default-render and --set-default-capture helpers in the seat's session
    /// without reassigning devices. Call this to fix audio when the initial helper invocation
    /// during provisioning failed or ran in the wrong session.
    /// </summary>
    public void ApplyAudioDefaults(Guid seatId)
    {
        var seat = GetSeat(seatId)
            ?? throw new InvalidOperationException("Seat not found.");
        ApplyAudioDefaults(seat);
    }

    private void ApplyAudioDefaults(SeatInfo seat)
    {
        var helperExe = Path.Combine(AppContext.BaseDirectory, "MultiSeat.Service.exe");

        if (!string.IsNullOrEmpty(seat.AudioGameRenderDeviceId))
        {
            try
            {
                _sessionLauncher.RunHelperInSeatSession(
                    seat.SessionId, seat.AccountName,
                    $"\"{helperExe}\" --set-default-render \"{seat.AudioGameRenderDeviceId}\"");
                _logger.LogInformation(
                    "Seat {Id}: applied render default {Dev} in session {Sid}",
                    seat.Id, seat.AudioGameRenderDeviceId, seat.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Seat {Id}: could not apply render default (non-critical)", seat.Id);
            }
        }

        if (!string.IsNullOrEmpty(seat.AudioCaptureDeviceId))
        {
            try
            {
                _sessionLauncher.RunHelperInSeatSession(
                    seat.SessionId, seat.AccountName,
                    $"\"{helperExe}\" --set-default-capture \"{seat.AudioCaptureDeviceId}\"");
                _logger.LogInformation(
                    "Seat {Id}: applied capture default {Dev} in session {Sid}",
                    seat.Id, seat.AudioCaptureDeviceId, seat.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Seat {Id}: could not apply capture default (non-critical)", seat.Id);
            }
        }
    }

    /// <summary>
    /// Change the NVENC quality preset for a live seat.
    /// Updates the seat's NvencPreset, regenerates sunshine.conf, and restarts Apollo.
    /// Also persists the change to the autostart preset if AutoStart is enabled.
    /// </summary>
    public async Task SetNvencPresetAsync(Guid seatId, NvencQualityPreset preset,
        SeatPresetStore presetStore, CancellationToken ct)
    {
        var seat = GetSeat(seatId)
            ?? throw new InvalidOperationException("Seat not found.");

        seat.NvencPreset = preset;

        _apolloManager.KillForReconnect(seat);
        await Task.Delay(500, ct);
        seat.ApolloProcessId = await _apolloManager.StartAsync(seat, ct);

        if (seat.AutoStart)
        {
            presetStore.Upsert(new SeatPreset
            {
                AccountName = seat.AccountName,
                Width = seat.Width,
                Height = seat.Height,
                Fps = seat.Fps,
                AutoStart = true,
                NvencPreset = preset,
            });
        }

        _ = BroadcastState(seat);
        _logger.LogInformation(
            "Seat {Id}: NVENC preset changed to {Preset} (Apollo PID {Pid})",
            seatId, preset, seat.ApolloProcessId);
    }

    /// <summary>Recreate the virtual display for a seat.</summary>
    public async Task ResetDisplayAsync(Guid seatId, CancellationToken ct)
    {
        var seat = GetSeat(seatId)
            ?? throw new InvalidOperationException("Seat not found.");

        await _displayManager.DestroyDisplayAsync(seat, ct);
        await _displayManager.CreateDisplayAsync(seat, ct);

        // Update Apollo config
        var configPath = _apolloManager.GetConfigPath(seat.Id);
        if (configPath is not null && !string.IsNullOrEmpty(seat.DisplayDevicePath))
            _configBuilder.UpdateDisplayOutput(configPath, seat.DisplayDevicePath);

        _ = BroadcastState(seat);
        _logger.LogInformation("Seat {Id}: display reset", seatId);
    }

    /// <summary>Recreate the virtual controller for a seat.</summary>
    public void ResetController(Guid seatId)
    {
        var seat = GetSeat(seatId)
            ?? throw new InvalidOperationException("Seat not found.");

        if (!_options.EnableViGEmController)
        {
            _logger.LogDebug("Seat {Id}: controller reset skipped — ViGEm controller disabled", seatId);
            return;
        }

        UnassignControllersForSeat(seatId);
        _controllerManager.DestroyController(seat);
        seat.ViGEmControllerIndex = _controllerManager.CreateController(seat);

        if (_options.AutoAssignControllers)
        {
            var connected = _inputRouter.GetConnectedControllers();
            var assigned = _inputRouter.GetAssignments();
            var freeIdx = connected.FirstOrDefault(idx => !assigned.ContainsKey(idx), -1);
            if (freeIdx >= 0)
                _inputRouter.AssignController(freeIdx, seatId);
        }

        _ = BroadcastState(seat);
        _logger.LogInformation("Seat {Id}: controller reset", seatId);
    }

    /// <summary>Get the InputRouter for API access to controller assignments.</summary>
    public InputRouter InputRouter => _inputRouter;

    /// <summary>Get the InputHookManager for API status queries.</summary>
    public InputHookManager InputHookManager => _inputHookManager;

    /// <summary>Get the ApolloManager for API queries.</summary>
    public ApolloManager ApolloManager => _apolloManager;

    public IReadOnlyList<string> GetPairedClients(Guid seatId)
    {
        var seat = GetSeat(seatId);
        if (seat is null) return Array.Empty<string>();
        return _configBuilder.GetPairedClients(seat.AccountName, _options.ApolloConfigDir);
    }

    public bool UnpairClient(Guid seatId, string clientName)
    {
        var seat = GetSeat(seatId);
        if (seat is null) return false;
        return _configBuilder.UnpairClient(seat.AccountName, _options.ApolloConfigDir, clientName);
    }

    public void UnpairAllClients(Guid seatId)
    {
        var seat = GetSeat(seatId);
        if (seat is null) return;
        _configBuilder.UnpairAllClients(seat.AccountName, _options.ApolloConfigDir);
    }

    private void UnassignControllersForSeat(Guid seatId)
    {
        foreach (var (idx, assignedSeat) in _inputRouter.GetAssignments())
        {
            if (assignedSeat == seatId)
                _inputRouter.UnassignController(idx);
        }
    }

    private static Task BroadcastState(SeatInfo seat) =>
        WebSocketHub.BroadcastSeatUpdateAsync(seat);
}
