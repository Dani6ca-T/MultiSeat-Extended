using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Accounts;
using MultiSeat.Service.Api;
using MultiSeat.Service.Audio;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Display;
using MultiSeat.Service.Emulators;
using MultiSeat.Service.Input;
using MultiSeat.Service.ProcessTracking;
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
    private readonly IAccountManager _accounts;
    private readonly ISessionLauncher _sessionLauncher;
    private readonly ProcessInjector _processInjector;
    private readonly IVirtualDisplayManager _displayManager;
    private readonly IStreamingProvider _streaming;
    private readonly ApolloManager _apolloManager;
    private readonly PortAllocator _portAllocator;
    private readonly FirewallManager _firewall;
    private readonly AudioRouter _audioRouter;
    private readonly ControllerManager _controllerManager;
    private readonly InputRouter _inputRouter;
    private readonly InputHookManager _inputHookManager;
    private readonly HidHideConfigurator _hidHide;
    private readonly OnConnectAppLauncher _onConnectApps;
    private readonly IEnumerable<IEmulatorConfigSeeder> _emulatorSeeders;
    private readonly SeatLifecycleGate _lifecycleGate;

    /// <summary>The per-seat cancellation / serialization gate. Exposed for tests.</summary>
    internal SeatLifecycleGate LifecycleGate => _lifecycleGate;

    /// <summary>The active controller manager. Exposed for tests.</summary>
    internal ControllerManager ControllerManager => _controllerManager;

    /// <summary>The active XInput→seat router. Exposed for tests.</summary>
    internal InputRouter InputRouter => _inputRouter;

    public SeatManager(
        ILogger<SeatManager> logger,
        IOptions<MultiSeatOptions> options,
        IAccountManager accounts,
        ISessionLauncher sessionLauncher,
        ProcessInjector processInjector,
        IVirtualDisplayManager displayManager,
        IStreamingProvider streaming,
        ApolloManager apolloManager,
        PortAllocator portAllocator,
        FirewallManager firewall,
        AudioRouter audioRouter,
        ControllerManager controllerManager,
        InputRouter inputRouter,
        InputHookManager inputHookManager,
        HidHideConfigurator hidHide,
        OnConnectAppLauncher onConnectApps,
        IEnumerable<IEmulatorConfigSeeder> emulatorSeeders,
        SeatLifecycleGate lifecycleGate)
    {
        _logger = logger;
        _options = options.Value;
        _accounts = accounts;
        _sessionLauncher = sessionLauncher;
        _processInjector = processInjector;
        _displayManager = displayManager;
        _streaming = streaming;
        _apolloManager = apolloManager;
        _portAllocator = portAllocator;
        _firewall = firewall;
        _audioRouter = audioRouter;
        _controllerManager = controllerManager;
        _inputRouter = inputRouter;
        _inputHookManager = inputHookManager;
        _hidHide = hidHide;
        _onConnectApps = onConnectApps;
        _emulatorSeeders = emulatorSeeders;
        _lifecycleGate = lifecycleGate;
    }

    // Guards the account-ownership critical section in ProvisionSeatAsync (dedup check +
    // seat registration). It is held only for that short check-and-insert — never across the
    // long-running provisioning work, which serializes per seat via SeatLifecycleGate. The
    // seat registry itself is concurrent, so this lock exists only to make
    // "is AccountName free?" + "register seat" atomic: two concurrent provisions for the same
    // account must not both pass the check and register.
    private readonly object _accountOwnershipLock = new();

    /// <summary>
    /// True when any seat in <paramref name="seats"/> occupies <paramref name="accountName"/> —
    /// i.e. is live or currently provisioning that account. Mirrors <see cref="ActiveSeatCount"/>'s
    /// notion of "live": Idle entries were never provisioned and Error entries hold no resources
    /// (their ports/sessions were released on failure), so neither blocks a fresh provision of the
    /// same account. Comparison is case-insensitive because Windows account names are, and the
    /// per-account config directory (ApolloConfigBuilder) is case-insensitive on NTFS.
    /// </summary>
    internal static bool AccountNameHasLiveSeat(IEnumerable<SeatInfo> seats, string accountName) =>
        seats.Any(s => s.Status is not (SeatStatus.Idle or SeatStatus.Error)
            && string.Equals(s.AccountName, accountName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Atomically register <paramref name="seat"/> in <paramref name="seats"/> unless another
    /// live/provisioning seat already occupies its AccountName. The check and the insert run
    /// under <paramref name="ownershipLock"/> — a lock covering only this short ownership
    /// decision, never the long provisioning work — so two concurrent provisions of the same
    /// account cannot both register. Returns true when the seat was registered.
    /// </summary>
    internal static bool TryRegisterSeat(
        ConcurrentDictionary<Guid, SeatInfo> seats, object ownershipLock, SeatInfo seat)
    {
        lock (ownershipLock)
        {
            if (AccountNameHasLiveSeat(seats.Values, seat.AccountName))
                return false;

            return seats.TryAdd(seat.Id, seat);
        }
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

        // Correct the account's groups before the session is created, so a seat provisioned by an
        // older build stops being a local administrator and gains the Remote Desktop Users
        // membership the RDP loopback logon needs. Idempotent, and a no-op for linked accounts.
        _accounts.ApplySeatGroupMembership(request.AccountName);

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

        // Register the seat under the account-ownership lock so the "already provisioned?"
        // check and the dictionary insert are one atomic step. At most one live/provisioning
        // seat may exist per AccountName: the per-account Apollo config directory, log, and
        // sunshine_state.json are keyed by AccountName (not seat id), so a second live seat
        // for the same account would share them — last-writer-wins config, and the first
        // seat's later restart would re-read the second seat's ports.
        //
        // The per-seat SeatLifecycleGate cannot protect this: each provision creates a fresh
        // seat Guid, so two provisions of the same account hold different gates. This lock
        // covers only the ownership decision; it is released before any provisioning work.
        if (!TryRegisterSeat(_seats, _accountOwnershipLock, seat))
            throw new InvalidOperationException(
                $"Account '{request.AccountName}' already has a seat — tear it down first.");

        await BroadcastState(seat);

        // Per-seat lifecycle gate. Acquired AFTER TryAdd so the gate's per-id semaphore
        // exists and any parallel recovery/reconnect/StopApollo for this id waits instead
        // of racing. Released when the try-block leaves scope (success or failure).
        using var lease = await _lifecycleGate.AcquireAsync(seat.Id, ct);

        try
        {
            // ── 1. Allocate ports ─────────────────────────────────────
            seat.PortBase = _portAllocator.Allocate();
            _logger.LogInformation("Seat {Id}: ports {Base}-{End}",
                seat.Id, seat.PortBase, seat.PortBase + Shared.Constants.PortsPerSeat - 1);

            // ── 1.5. Assign emulator netplay port from this seat's block ──
            // A free offset in the 30-port block gives each seat a unique, collision-free netplay
            // host port. Seats netplay each other over loopback (127.0.0.1:<this port>).
            if (_options.EnableEmulatorNetplay)
            {
                seat.RetroArchNetplayPort = seat.PortBase + Shared.Constants.OffsetRetroArchNetplay;
                _logger.LogInformation(
                    "Seat {Id}: RetroArch netplay host port {Port}", seat.Id, seat.RetroArchNetplayPort);
            }

            // ── 2. Launch background session ──────────────────────────
            // Pass the seat's resolution as the RDP geometry. The seat streams its RDP session
            // surface (there is no in-seat virtual display — issue #15), and that surface's size
            // is set by mstsc at connect time and cannot be changed from inside the session. So
            // this is what makes the dashboard resolution actually take effect; without it the
            // session inherits whatever size mstsc picks, which tracks the console desktop.
            seat.SessionId = await _sessionLauncher.LaunchSessionAsync(
                seat.AccountName, ct, RdpGeometry.ForClient(seat.Width, seat.Height));
            _logger.LogInformation("Seat {Id}: Windows session {Sid}", seat.Id, seat.SessionId);

            seat.TransitionTo(SeatStatus.Configuring, _logger);
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

            // ── 2.7. Pre-write gamepad jail rules ───────────────
            // Before Apollo exists, on purpose. HidHide filters at OPEN time, so a rule
            // written after the pad is created is late by definition - and dwm, explorer and
            // GameInputSvc of every session open each new pad inside that window and keep
            // handles that never expire. A rule for an absent device matches nothing, so this
            // is inert when the seat has no known pad path. No-op unless
            // EnableHidHideCloaking + EnablePadRulePreWrite are both on.
            try { _hidHide.PreWriteRules(seat); }
            catch (Exception ex) { _logger.LogWarning(ex, "Seat {Id}: pre-writing gamepad jail rules failed (non-critical)", seat.Id); }

            // ── 3. Virtual display ────────────────────────────────────
            await _displayManager.CreateDisplayAsync(seat, ct);
            _logger.LogDebug("Seat {Id}: VDA ready ({W}x{H}@{F})",
                seat.Id, seat.Width, seat.Height, seat.Fps);

            // ── 4. Firewall ───────────────────────────────────────────
            await _firewall.OpenPortsAsync(seat, ct);

            // ── 5. Audio routing ──────────────────────────────────────
            seat.ProvisioningStep = "Audio";
            await BroadcastState(seat);

            // PerSession needs no host-side audio device: the seat's RDP session has its own
            // "Remote Audio" endpoint and Apollo captures that. Skipping AssignCable is not just
            // an optimisation — it throws when no virtual cables are installed, and uninstalling
            // VB-CABLE/VoiceMeeter is a supported (indeed expected) state in this mode.
            if (_options.AudioMode == AudioMode.PerSession)
            {
                _logger.LogInformation(
                    "Seat {Id}: per-session audio — no virtual cable assigned; Apollo captures " +
                    "the session's own Remote Audio endpoint", seat.Id);
            }
            else
            {
                // Assign VAC before Apollo so the config has the audio device
                seat.VacCableIndex = _audioRouter.AssignCable(seat);
                _logger.LogDebug("Seat {Id}: VAC cable {C}", seat.Id, seat.VacCableIndex);
            }

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

            // NOTE: MultiSeat intentionally does NOT set the seat's game-audio device as the
            // session default render. The Windows default output device is machine-wide (shared
            // by the console and every seat), so doing so hijacked the host's audio (issue #10).
            // Apollo points the game at the seat's device itself via virtual_sink in sunshine.conf
            // (for the duration of the stream, restored afterwards) — see ApolloConfigBuilder.

            // ── 5.7. Seed emulator configs (opt-in, best-effort) ──────────
            // Write each enabled emulator's per-seat netplay config into the seat user's profile
            // (e.g. RetroArch netplay port + shared ROM dir). Mirrors the RustDesk seed above:
            // best-effort, never fails provisioning.
            foreach (var seeder in _emulatorSeeders)
            {
                if (!seeder.IsEnabled) continue;
                try
                {
                    await seeder.SeedAsync(seat, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Seat {Id}: {Emulator} config seed failed (non-critical)",
                        seat.Id, seeder.EmulatorName);
                }
            }

            // ── 6. Apollo streaming ───────────────────────────────────────
            // Apollo is launched AFTER display + audio so it can capture both.
            // The session is still ACTIVE (mstsc connected) so Apollo's SudoVDA IPC
            // can initialize the virtual display. Without an active session,
            // QueryDisplayConfig returns ERROR_ACCESS_DENIED and the encoder probe fails.
            seat.ProvisioningStep = "Apollo";
            await BroadcastState(seat);

            seat.StreamingProcessId = await _streaming.StartAsync(seat, ct);
            _logger.LogInformation("Seat {Id}: Apollo PID {Pid}", seat.Id, seat.StreamingProcessId);

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

                // Wait for Apollo to initialize SudoVDA IPC and write its display log.
                // The session MUST stay ACTIVE (mstsc connected) — Apollo calls QueryDisplayConfig
                // both at startup AND when each Moonlight client connects. Disconnected sessions
                // return ERROR_ACCESS_DENIED, causing "Failed to initialize video capture/encoding".
                await Task.Delay(5000, ct);

                // NOTE: We intentionally do NOT disconnect mstsc here.
                // The session stays Active for the lifetime of the seat so Apollo can
                // always query and set display modes when clients connect.

                var displayId = _apolloManager.ParseSudoVdaDisplayId(logPath);
                if (displayId != null)
                {
                    seat.DisplayDevicePath = displayId;
                    _apolloManager.UpdateDisplayOutput(seat, displayId);

                    _logger.LogInformation(
                        "Seat {Id}: SudoVDA UUID discovered ({Dev}) — restarting Apollo with display target",
                        seat.Id, displayId);

                    // Restart Apollo with the correct output_name (UUID).
                    // Brief delay to let Apollo finish writing logs before we kill it.
                    _streaming.Stop(seat);
                    await Task.Delay(2000, ct);
                    seat.StreamingProcessId = await _streaming.StartAsync(seat, ct);

                    // ── 6.6/6.7: Display isolation + refresh-rate clamp ─────
                    await ApplyDisplayIsolationAsync(seat, ct);
                }
                else
                {
                    // Not a fault, and deliberately not a warning. Apollo does not create the
                    // seat's virtual display at startup — it creates it when a client connects
                    // and launches an app — so there is nothing to find at provisioning time on
                    // ANY host. TryLateDisplayDetectionAsync retries from the health-check tick
                    // and applies isolation if it ever appears. The old text here claimed the
                    // seat would "capture the primary monitor instead", which read as a broken
                    // install and cost issue #15's reporter two days of driver debugging.
                    _logger.LogDebug(
                        "Seat {Id}: no virtual display in the Apollo log yet — expected at this " +
                        "point; Apollo creates one on client connect and the health check retries",
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
            seat.TransitionTo(SeatStatus.Ready, _logger);
            seat.ReadyAt = DateTimeOffset.UtcNow;
            seat.ProvisioningStep = null;
            await BroadcastState(seat);
            _logger.LogInformation(
                "Seat {Id}: READY for Moonlight connection on port {P}",
                seat.Id, seat.PortBase + Shared.Constants.OffsetGfeHttp);

            return seat;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Seat {Id}: provisioning failed at {Status}", seat.Id, seat.Status);
            seat.TransitionTo(SeatStatus.Error, _logger);
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
        // Cheap pre-gate validation: only a Ready or Streaming seat can host a launch. The
        // session id is captured here so the post-gate revalidation can detect a session
        // replacement (SetResolutionAsync / /session-reconnect) that ran while we waited.
        var seat = GetSeat(seatId)
            ?? throw new InvalidOperationException("Seat not found.");

        if (seat.Status is not SeatStatus.Ready and not SeatStatus.Streaming)
            throw new InvalidOperationException($"Seat is in {seat.Status} state — cannot launch apps.");

        var sessionIdAtEntry = seat.SessionId;

        // Per-seat lifecycle gate. LaunchInSessionAsync creates a real process in the seat's
        // session and then flips the seat to Streaming; the gate makes the whole
        // revalidate → launch → state-mutate transaction atomic with respect to teardown and
        // the other lifecycle callers (the same boundary ResetController and SetResolutionAsync
        // use). Without the gate, a concurrent teardown can remove the seat — and disconnect +
        // log off its session — between the status check above and the process creation, so the
        // app lands in a session that is being destroyed: an orphan process with no seat in
        // _seats to ever return to Ready or kill it.
        using var lease = await _lifecycleGate.AcquireAsync(seatId, ct);

        // The seat was captured BEFORE waiting for the gate; while we waited, a concurrent
        // teardown could have removed it (H2 ordering: removal → TearingDown → teardown → gate
        // release) or a session replacement (SetResolutionAsync / /session-reconnect) could have
        // moved it to a different session. Re-read membership and lifecycle state now that the
        // gate is held, and re-confirm the session id is still the one validated above — the
        // launch targets the exact seat + session the user asked for, never a stale capture.
        // TearingDown is the "removed" signal; any other non-launchable status (Error,
        // Connecting, …) means the session can no longer host the app either. Throw to surface
        // the lost race to the caller (the API endpoint maps this to 400 BadRequest).
        seat = GetSeat(seatId);
        if (seat is null || !AppLaunchStillValid(seat.Status) || seat.SessionId != sessionIdAtEntry)
        {
            _logger.LogWarning(
                "Seat {Id}: removed or session changed while launching app — aborting", seatId);
            throw new InvalidOperationException("Seat was removed while launching app.");
        }

        // Track the ROOT PID of the launched process so the health check can return the
        // seat to Ready when the app exits (SessionHealthCheck Check 3). Only the root
        // process is tracked; children are not part of the app lifetime. Set together with
        // the Streaming transition before the state is broadcast, so an observed Streaming
        // seat always carries its tracking state. SessionId is read under the held gate, so it
        // cannot change between this revalidation and CreateProcessAsUser.
        var pid = await _processInjector.LaunchInSessionAsync(
            seat.SessionId, seat.AccountName,
            request.ExecutablePath, request.Arguments, request.WorkingDirectory, ct);

        // Capture the OS start time next to the PID so teardown can terminate the app
        // safely against PID reuse (pairing them forms the process identity). Null when the
        // process already exited before the start time could be read — then teardown has
        // nothing of the seat's left to kill.
        seat.LaunchedProcessStartedAt = pid > 0
            ? ApolloManager.GetProcessStartTime(pid)
            : null;

        seat.TransitionTo(SeatStatus.Streaming, _logger);
        seat.LaunchApp = request.ExecutablePath;
        seat.LaunchedProcessId = pid;
        await BroadcastState(seat);
    }

    /// <summary>
    /// Teardown a single seat — reverse order of provisioning.
    ///
    /// The per-seat lifecycle gate is acquired BEFORE the seat is removed from <c>_seats</c>,
    /// so a failed gate acquisition can never make a live seat disappear: if another lifecycle
    /// operation holds the gate past its acquisition timeout, the TimeoutException propagates
    /// with the seat still registered and its status untouched — the caller can retry, and no
    /// invisible orphan (session/Apollo/ports without a registry entry) is left behind.
    /// </summary>
    public async Task TeardownSeatAsync(Guid seatId, CancellationToken ct)
    {
        // Gate acquisition is deliberately not cancellable (CancellationToken.None): teardown
        // must not be abandonable mid-flight — service shutdown and the DELETE endpoint both
        // rely on it completing. The caller's ct is still honoured by the individual teardown
        // steps inside TeardownSeatInternalAsync.
        var (seat, lease) = await TryBeginTeardownAsync(
            _seats, _lifecycleGate, seatId,
            SeatLifecycleGate.DefaultAcquisitionTimeout, CancellationToken.None);

        if (seat is null)
            return; // not registered, or a concurrent teardown already handled it — no-op

        // The seat is removed from the registry and the gate is held for the whole teardown,
        // so the Apollo Stop + Disconnect + DestroyDisplay sequence is atomic with respect to
        // any in-flight lifecycle operation for the same seat, and a parallel recovery tick
        // that captured the seat before removal cannot re-enter after teardown starts.
        using (lease!)
        {
            seat.TransitionTo(SeatStatus.TearingDown, _logger);
            await BroadcastState(seat);

            await TeardownSeatInternalAsync(seat, ct);
            _logger.LogInformation("Seat {Id}: torn down", seat.Id);
        }
    }

    /// <summary>
    /// Gate-then-remove step of teardown, split out so the ordering invariant is unit-testable
    /// without the full SeatManager dependency graph (same seam pattern as
    /// <see cref="TryRegisterSeat"/>).
    ///
    /// Acquires the per-seat lifecycle gate for <paramref name="seatId"/>, then removes the
    /// seat from <paramref name="seats"/> only once the gate is held. On gate-acquisition
    /// failure the <see cref="TimeoutException"/> propagates and the seat REMAINS registered:
    /// a teardown that cannot get the gate must never make a live seat disappear. Returns the
    /// removed seat together with the held lease (caller disposes it after tearing down), or
    /// (null, null) when the seat is absent or a concurrent teardown already removed it —
    /// double teardown is a safe no-op.
    /// </summary>
    internal static async Task<(SeatInfo? Seat, SeatLifecycleGate.ILease? Lease)> TryBeginTeardownAsync(
        ConcurrentDictionary<Guid, SeatInfo> seats,
        SeatLifecycleGate gate,
        Guid seatId,
        TimeSpan gateTimeout,
        CancellationToken ct)
    {
        var lease = await gate.AcquireAsync(seatId, gateTimeout, ct);

        if (!seats.TryRemove(seatId, out var seat))
        {
            lease.Dispose();
            return (null, null);
        }

        return (seat, lease);
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
        //
        // Capture the launched-app identities (dashboard launch + on-connect apps) BEFORE
        // Forget drops the launcher state — teardown terminates them explicitly below, so
        // cleanup never depends solely on the session logoff.
        IReadOnlyList<ProcessIdentity> onConnectLaunched = [];
        try { onConnectLaunched = _onConnectApps.GetLaunchedProcesses(seat.Id); } catch { /* best effort */ }
        try { _onConnectApps.Forget(seat.Id); } catch { /* best effort */ }
        try { _inputHookManager.Uninstall(); } catch { /* best effort */ }
        try { _hidHide.UncloakForSession(seat); } catch { /* best effort */ }
        try { UnassignControllersForSeat(seat.Id); } catch { /* best effort */ }
        try { _controllerManager.DestroyController(seat); } catch { /* best effort */ }
        try { _streaming.Stop(seat); } catch { /* best effort */ }
        try { _audioRouter.ReleaseCable(seat); } catch { /* best effort */ }
        try { await _firewall.ClosePortsAsync(seat, ct); } catch { /* best effort */ }
        try { await _displayManager.DestroyDisplayAsync(seat, ct); } catch { /* best effort */ }

        // Explicitly terminate the seat's launched apps (dashboard "launch" + on-connect)
        // before the session is logged off. Identity-aware: each PID is killed only while it
        // still denotes the process this seat actually launched (PID + start time match), so
        // a recycled PID can never kill an unrelated process. Best-effort — an app that
        // cannot be terminated is logged and teardown continues; the logoff below remains
        // the backstop for anything that survived.
        try { TerminateLaunchedApps(seat, onConnectLaunched); } catch (Exception ex) { _logger.LogWarning(ex, "Seat {Id}: error cleaning up launched apps during teardown", seat.Id); }

        try { _sessionLauncher.DisconnectSession(seat.SessionId); } catch { /* best effort */ }
        try { _sessionLauncher.LogoffSession(seat.SessionId); } catch { /* best effort */ }
        try { _portAllocator.Release(seat.PortBase); } catch { /* best effort */ }

        // Clean up per-seat Apollo config directory
        try { _apolloManager.CleanupSeatConfig(seat); } catch { /* best effort */ }
    }

    /// <summary>
    /// Identity-aware, best-effort termination of the seat's launched applications: the
    /// dashboard-launched root process (<see cref="SeatInfo.LaunchedProcessId"/> + its
    /// recorded start time) and the on-connect apps the launcher started. Kills each process
    /// only while its PID still denotes the exact process that was launched, so a stale or
    /// recycled PID never terminates an unrelated process. Already-exited processes are
    /// treated as clean. Any failure is logged inside <see cref="LaunchedProcessCleanup"/>
    /// and teardown continues.
    /// </summary>
    private void TerminateLaunchedApps(SeatInfo seat, IReadOnlyList<ProcessIdentity> onConnectLaunched)
    {
        var identities = new List<ProcessIdentity>(onConnectLaunched.Count + 1);

        if (seat.LaunchedProcessId > 0 && seat.LaunchedProcessStartedAt is { } startedAt)
            identities.Add(new ProcessIdentity(seat.LaunchedProcessId, startedAt));

        identities.AddRange(onConnectLaunched);

        if (identities.Count > 0)
            LaunchedProcessCleanup.TerminateAll(identities, _logger);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PER-SEAT SERVICE MANAGEMENT
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Get the live status of each subsystem for a seat.
    ///
    /// Async because it asks the seat's Apollo whether it actually answers, rather than only
    /// whether its process exists — the two differ exactly when a seat is broken in the way a
    /// user notices. Only queried when the process is alive and the seat has a port, so a torn
    /// down or provisioning seat costs nothing.
    /// </summary>
    public async Task<SeatServices> GetSeatServicesAsync(Guid seatId, CancellationToken ct = default)
    {
        var seat = GetSeat(seatId);
        if (seat is null) return new SeatServices();

        var apolloAlive = seat.StreamingProcessId > 0 && _streaming.IsAlive(seatId);

        Monitoring.ApolloServerInfo? server = null;
        if (apolloAlive)
            server = await _streaming.QueryHealthAsync(seat, ct);

        return new SeatServices
        {
            Apollo = apolloAlive,
            ApolloReachable = server is not null,
            ApolloStreaming = server?.Streaming ?? false,
            ApolloRestarts = _streaming.GetRestartCount(seatId),
            Display = !string.IsNullOrEmpty(seat.DisplayDevicePath),
            // PerSession: the endpoint is created by Windows with the session itself, so there
            // is no device assignment that could be missing — report healthy, and let
            // AudioManaged tell the dashboard not to read this as a device light.
            Audio = _options.AudioMode == AudioMode.PerSession || seat.VacCableIndex >= 0,
            AudioManaged = _options.AudioMode == AudioMode.SharedHost,
            Controller = seat.ViGEmControllerIndex >= 0,
            ControllerManaged = _options.EnableViGEmController,
            InputHooks = _inputHookManager.IsInstalled,
            Firewall = seat.PortBase > 0,
            Session = seat.SessionId >= 0
        };
    }

    /// <summary>Stop Apollo for a seat without tearing down everything else.</summary>
    public async Task StopApollo(Guid seatId)
    {
        var seat = GetSeat(seatId)
            ?? throw new InvalidOperationException("Seat not found.");

        // Per-seat lifecycle gate. Stops the Apollo instance record and mutates
        // StreamingProcessId; must serialize with recovery/reconnect/range-changers.
        using var lease = await _lifecycleGate.AcquireAsync(seatId, CancellationToken.None);

        _streaming.Stop(seat);
        seat.StreamingProcessId = 0;
        _ = BroadcastState(seat);
        _logger.LogInformation("Seat {Id}: Apollo stopped by user", seatId);
    }

    /// <summary>Start Apollo for a seat (must already have session + display).</summary>
    public async Task StartApolloAsync(Guid seatId, CancellationToken ct)
    {
        var seat = GetSeat(seatId)
            ?? throw new InvalidOperationException("Seat not found.");

        // Per-seat lifecycle gate. Starts Apollo, mutates StreamingProcessId, updates
        // ApolloManager's instance record. Must serialize with recovery/reconnect.
        using var lease = await _lifecycleGate.AcquireAsync(seatId, ct);

        if (seat.SessionId < 0)
            throw new InvalidOperationException("No active session — provision the seat first.");

        seat.StreamingProcessId = await _streaming.StartAsync(seat, ct);

        // Re-apply display config
        if (!string.IsNullOrEmpty(seat.DisplayDevicePath))
            _apolloManager.UpdateDisplayOutput(seat, seat.DisplayDevicePath);

        _ = BroadcastState(seat);
        _logger.LogInformation("Seat {Id}: Apollo started by user (PID {Pid})", seatId, seat.StreamingProcessId);
    }

    /// <summary>Restart Apollo for a seat (stop + start).</summary>
    public async Task RestartApolloAsync(Guid seatId, CancellationToken ct)
    {
        var seat = GetSeat(seatId)
            ?? throw new InvalidOperationException("Seat not found.");

        // Per-seat lifecycle gate. Stop + Start are a compound lifecycle mutation; the gate
        // makes them atomic with respect to other lifecycle callers (recovery, reconnect,
        // resolution change, nvenc change).
        using var lease = await _lifecycleGate.AcquireAsync(seatId, ct);

        _streaming.Stop(seat);
        seat.StreamingProcessId = 0;

        seat.StreamingProcessId = await _streaming.StartAsync(seat, ct);

        if (!string.IsNullOrEmpty(seat.DisplayDevicePath))
            _apolloManager.UpdateDisplayOutput(seat, seat.DisplayDevicePath);

        if (seat.StreamingProcessId > 0)
            await ApplyDisplayIsolationAsync(seat, ct);

        _ = BroadcastState(seat);
        _logger.LogInformation("Seat {Id}: Apollo restarted by user (PID {Pid})", seatId, seat.StreamingProcessId);
    }

    /// <summary>
    /// Second chance at finding the seat's SudoVDA display, run from the health-check tick.
    ///
    /// Provisioning looks for the display ~5s after Apollo starts, but Apollo does not create
    /// one then. Creation happens in its <c>proc_t::execute()</c> — i.e. when a client connects
    /// and launches an app — gated on <c>headless_mode</c> (which ApolloConfigBuilder now sets).
    /// At provisioning time there is therefore nothing to find, DisplayDevicePath stays null,
    /// and display isolation is skipped for the seat's whole life, leaving TermService CPU high.
    ///
    /// So retry while the seat runs. Once Apollo creates the display it logs a fresh
    /// "Currently available display devices:" block, this picks it up, and isolation is applied.
    ///
    /// Deliberately does NOT restart Apollo the way the provisioning path does: a client is
    /// streaming by the time this succeeds, and Apollo has already pointed itself at the new
    /// display (it assigns config::video.output_name after creating it). Writing output_name to
    /// the config here only makes the next start target it directly.
    ///
    /// Returns true when the display was found and isolation was attempted.
    /// </summary>
    public async Task<bool> TryLateDisplayDetectionAsync(SeatInfo seat, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(seat.DisplayDevicePath)) return false; // already known
        if (seat.StreamingProcessId <= 0) return false;                     // Apollo not running

        string text;
        try
        {
            var logPath = _apolloManager.GetLogPath(seat.AccountName, _options.ApolloConfigDir);
            if (!File.Exists(logPath)) return false;

            // Apollo holds the log open, so share read AND write.
            using var fs = new FileStream(
                logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            text = await sr.ReadToEndAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Seat {Id}: late display detection could not read Apollo log", seat.Id);
            return false;
        }

        // ParseLatestSudoVdaDisplayIdFromLogText slices from the LAST display-enumeration
        // block so we parse Apollo's most recent view — the first block is always the startup
        // enumeration, which never contains the virtual display.
        var result = ApolloManager.ParseLatestSudoVdaDisplayIdFromLogText(text);
        if (result.DeviceId is null) return false; // still nothing — stay quiet, we run every tick

        seat.DisplayDevicePath = result.DeviceId;

        _apolloManager.UpdateDisplayOutput(seat, result.DeviceId);

        _logger.LogInformation(
            "Seat {Id}: SudoVDA display found after client connect ({Dev}) — applying display isolation",
            seat.Id, result.DeviceId);

        await ApplyDisplayIsolationAsync(seat, ct);
        return true;
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
    public async Task ResetAudio(Guid seatId)
    {
        var seat = GetSeat(seatId)
            ?? throw new InvalidOperationException("Seat not found.");

        // Nothing to reset under per-session audio: MultiSeat assigns no device, and the
        // session's Remote Audio endpoint lives and dies with the session itself. Re-assigning
        // here would throw on a host that has (legitimately) no virtual cables installed.
        if (_options.AudioMode == AudioMode.PerSession)
        {
            _logger.LogInformation(
                "Seat {Id}: audio reset is a no-op under per-session audio — the session owns " +
                "its own Remote Audio endpoint. Restart the seat's Apollo if capture is wrong.",
                seatId);
            return;
        }

        // Per-seat lifecycle gate. ReleaseCable + AssignCable + ApplyAudioDefaults mutate the
        // AudioRouter assignment state and the seat's audio fields; teardown releases the cable
        // from its own side, so the gate makes the whole reset transaction atomic with respect
        // to teardown (the same boundary ResetController, SetResolutionAsync, LaunchAppInSeatAsync
        // and ResetDisplayAsync use). Without it, a concurrent teardown can release the cable
        // between our ReleaseCable and AssignCable, so the re-assign lands on a seat that is
        // being torn down — the AudioRouter keeps a cable assignment whose seat no longer exists
        // in _seats, and the helper runs in a session that is being logged off.
        using var lease = await _lifecycleGate.AcquireAsync(seatId, CancellationToken.None);

        // The seat was captured BEFORE waiting for the gate; a concurrent teardown could have
        // removed it (H2 ordering: removal → TearingDown → teardown → gate release) while we
        // waited. Re-read membership and lifecycle state now that the gate is held, and abort
        // before any side effect if the seat is gone or tearing down.
        seat = GetSeat(seatId);
        if (seat is null || !AudioResetStillValid(seat.Status))
        {
            _logger.LogWarning(
                "Seat {Id}: removed while resetting audio — aborting", seatId);
            throw new InvalidOperationException("Seat was removed while resetting audio.");
        }

        _audioRouter.ReleaseCable(seat);
        seat.VacCableIndex = _audioRouter.AssignCable(seat);

        ApplyAudioDefaults(seat);

        _ = BroadcastState(seat);
        _logger.LogInformation("Seat {Id}: audio reset, cable #{C}", seatId, seat.VacCableIndex);
    }

    /// <summary>
    /// Re-run the --set-default-capture helper in the seat's session without reassigning devices.
    /// Call this to fix mic routing when the initial helper invocation during provisioning failed
    /// or ran in the wrong session. Does NOT touch the default render device — that is machine-wide
    /// and would hijack the host's audio (issue #10); Apollo manages the game-audio sink itself.
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

        // Only the capture (mic) default is set here. The render default is intentionally left
        // alone — see the note in ProvisionSeatAsync and ApolloConfigBuilder (issue #10).
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

        // Per-seat lifecycle gate. KillForReconnect + Start mutate StreamingProcessId and the
        // ApolloManager instance record; must serialize with recovery and reconnect.
        using var lease = await _lifecycleGate.AcquireAsync(seatId, ct);

        seat.NvencPreset = preset;

        _streaming.KillForReconnect(seat);
        await Task.Delay(500, ct);
        seat.StreamingProcessId = await _streaming.StartAsync(seat, ct);

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
            seatId, preset, seat.StreamingProcessId);
    }

    /// <summary>
    /// Change a live seat's resolution.
    ///
    /// The seat streams its RDP session surface, and that surface's size is fixed by mstsc when
    /// the session is created (issue #15 — there is no in-seat virtual display to resize, and
    /// ChangeDisplaySettingsEx from inside the session returns success while doing nothing).
    /// So changing resolution means giving the seat a new session at the new size: disconnect,
    /// reconnect with the new geometry, and restart Apollo so it re-reads the desktop.
    ///
    /// The Windows session id is preserved — mstsc reconnects to the same session rather than
    /// logging it off — so anything running in the seat survives.
    /// </summary>
    public async Task SetResolutionAsync(Guid seatId, int width, int height,
        SeatPresetStore presetStore, CancellationToken ct)
    {
        var seat = GetSeat(seatId)
            ?? throw new InvalidOperationException("Seat not found.");

        var geometry = RdpGeometry.ForClient(width, height);
        if (!geometry.IsValid)
            throw new ArgumentException(
                $"{width}x{height} is not a usable desktop size — mstsc would ignore it.");

        if (seat.Width == width && seat.Height == height)
        {
            _logger.LogDebug("Seat {Id}: already {W}x{H}, nothing to do", seatId, width, height);
            return;
        }

        _logger.LogInformation(
            "Seat {Id}: changing resolution {OldW}x{OldH} -> {W}x{H}",
            seatId, seat.Width, seat.Height, width, height);

        // Per-seat lifecycle gate. KillForReconnect + DisconnectSession + LaunchSessionAsync
        // + Start mutate SessionId, StreamingProcessId, and the ApolloManager instance record.
        // The gate makes the whole compound change atomic with respect to recovery, reconnect,
        // and other lifecycle callers.
        using var lease = await _lifecycleGate.AcquireAsync(seatId, ct);

        // The seat was captured BEFORE waiting for the gate. While we waited, a concurrent
        // DELETE may have torn it down (H2 ordering: removal → TearingDown → teardown → gate
        // release, so the captured object now reads TearingDown). Every side effect below —
        // KillForReconnect, DisconnectSession, LaunchSessionAsync (creates a NEW Windows
        // session), config rebuild, Apollo start — would otherwise run against a removed seat
        // and orphan the session/Apollo it creates. Re-check BEFORE any of them.
        if (!ResolutionChangeStillValid(seat.Status))
        {
            _logger.LogWarning(
                "Seat {Id}: removed while changing resolution — aborting", seatId);
            throw new InvalidOperationException(
                "Seat was removed while changing resolution.");
        }

        seat.Width = width;
        seat.Height = height;

        // Take the session down and bring it back at the new size. Apollo is stopped first so it
        // is not capturing a desktop that is about to change under it.
        _streaming.KillForReconnect(seat);
        _sessionLauncher.DisconnectSession(seat.SessionId);

        seat.SessionId = await _sessionLauncher.LaunchSessionAsync(seat.AccountName, ct, geometry);

        // Apollo advertises the seat's resolution in its config, so regenerate before starting.
        _apolloManager.RebuildConfig(seat);
        seat.StreamingProcessId = await _streaming.StartAsync(seat, ct);

        if (seat.AutoStart)
        {
            presetStore.Upsert(new SeatPreset
            {
                AccountName = seat.AccountName,
                Width = width,
                Height = height,
                Fps = seat.Fps,
                AutoStart = true,
                NvencPreset = seat.NvencPreset,
            });
        }

        _ = BroadcastState(seat);
        _logger.LogInformation(
            "Seat {Id}: resolution now {W}x{H} on session {Sid} (Apollo PID {Pid})",
            seatId, width, height, seat.SessionId, seat.StreamingProcessId);
    }

    /// <summary>
    /// Whether SetResolutionAsync may still run its side effects after the per-seat lifecycle
    /// gate was acquired: only while the seat is still a registered member. A status of
    /// TearingDown means a concurrent teardown removed the seat from _seats while the request
    /// waited for the gate (H2 ordering: removal → TearingDown → teardown → gate release), so
    /// the captured object now reads TearingDown. LaunchSessionAsync below creates a NEW
    /// Windows session — running it for a removed seat would orphan that session and the
    /// Apollo started into it (nothing in _seats would ever tear them down). Every other
    /// status keeps the pre-existing semantics: resolution changes were never gated on a
    /// status precondition, and no other reachable state invalidates the change.
    /// </summary>
    internal static bool ResolutionChangeStillValid(SeatStatus status) =>
        status != SeatStatus.TearingDown;

    /// <summary>
    /// Whether <see cref="ResetController"/> may still run its destroy/create/assign
    /// transaction after the per-seat lifecycle gate was acquired: only while the seat
    /// is still a registered member. A status of <c>TearingDown</c> means a concurrent
    /// teardown removed the seat from <c>_seats</c> while the request waited for the
    /// gate (H2 ordering: removal → TearingDown → teardown → gate release), so the
    /// captured object now reads TearingDown. <c>CreateController</c> for such a seat
    /// would register a real ViGEm virtual controller with no seat in <c>_seats</c> to
    /// ever tear it down. Every other status keeps the pre-existing semantics: controller
    /// reset is allowed for Ready, Streaming, Error, etc. (the user can fix a misbehaving
    /// pad in any non-tearing-down state).
    /// </summary>
    internal static bool ControllerResetStillValid(SeatStatus status) =>
        status != SeatStatus.TearingDown;

    /// <summary>
    /// Whether <see cref="LaunchAppInSeatAsync"/> may still launch a process after the per-seat
    /// lifecycle gate was acquired: only while the seat is a registered member in a state that
    /// can host a launched app. A status of <c>TearingDown</c> means a concurrent teardown
    /// removed the seat from <c>_seats</c> while the request waited for the gate (H2 ordering:
    /// removal → TearingDown → teardown → gate release), so the captured object now reads
    /// TearingDown. <c>LaunchInSessionAsync</c> for such a seat would create a real process in a
    /// session that is being disconnected + logged off — an orphan process with no seat in
    /// <c>_seats</c> to ever return to Ready or kill it. Any other non-launchable status (Error,
    /// Connecting, …) means the seat's session can no longer host the app either. This mirrors
    /// the pre-gate precondition (launch is only offered on Ready or Streaming seats) and keeps
    /// it true across the gate wait.
    /// </summary>
    internal static bool AppLaunchStillValid(SeatStatus status) =>
        status is SeatStatus.Ready or SeatStatus.Streaming;

    /// <summary>
    /// Whether <see cref="ResetDisplayAsync"/> may still run its destroy/recreate transaction
    /// after the per-seat lifecycle gate was acquired: only while the seat is still a registered
    /// member. A status of <c>TearingDown</c> means a concurrent teardown removed the seat from
    /// <c>_seats</c> while the request waited for the gate (H2 ordering: removal → TearingDown →
    /// teardown → gate release), so the captured object now reads TearingDown. Re-running
    /// destroy + create for such a seat would re-register the display assignment after teardown
    /// released it — an orphan record with no seat in <c>_seats</c> to ever release it again, and
    /// a config write (UpdateDisplayOutput) aimed at a config teardown already cleaned. Every
    /// other status keeps the pre-existing semantics: display reset is a repair action offered on
    /// any non-tearing-down seat (Ready, Streaming, Error, …). Mirrors
    /// <see cref="ResolutionChangeStillValid"/> and <see cref="ControllerResetStillValid"/>.
    /// </summary>
    internal static bool DisplayResetStillValid(SeatStatus status) =>
        status != SeatStatus.TearingDown;

    /// <summary>
    /// Whether <see cref="ResetAudio"/> may still run its release/re-assign transaction after the
    /// per-seat lifecycle gate was acquired: only while the seat is still a registered member. A
    /// status of <c>TearingDown</c> means a concurrent teardown removed the seat from <c>_seats</c>
    /// while the request waited for the gate (H2 ordering: removal → TearingDown → teardown → gate
    /// release), so the captured object now reads TearingDown. Re-running AssignCable for such a
    /// seat would leave the AudioRouter holding a cable assignment whose seat no longer exists in
    /// <c>_seats</c> — that cable pair is never released and stays unavailable to future seats.
    /// Every other status keeps the pre-existing semantics: audio reset is a repair action offered
    /// on any non-tearing-down seat (Ready, Streaming, Error, …). Mirrors
    /// <see cref="DisplayResetStillValid"/> and <see cref="ControllerResetStillValid"/>.
    /// </summary>
    internal static bool AudioResetStillValid(SeatStatus status) =>
        status != SeatStatus.TearingDown;

    /// <summary>Recreate the virtual display for a seat.</summary>
    public async Task ResetDisplayAsync(Guid seatId, CancellationToken ct)
    {
        var seat = GetSeat(seatId)
            ?? throw new InvalidOperationException("Seat not found.");

        // Per-seat lifecycle gate. DestroyDisplay + CreateDisplay + UpdateDisplayOutput mutate
        // the display assignment record and the seat's Apollo config; the gate makes the whole
        // transaction atomic with respect to teardown and other lifecycle callers (the same
        // boundary ResetController, SetResolutionAsync and LaunchAppInSeatAsync use). Without it,
        // a concurrent teardown can release the display assignment and clean the seat's config
        // between Destroy and Create, leaving a re-registered display record / rewritten config
        // for a seat that no longer exists in _seats.
        using var lease = await _lifecycleGate.AcquireAsync(seatId, ct);

        // The seat was captured BEFORE waiting for the gate; a concurrent teardown could have
        // removed it (H2 ordering: removal → TearingDown → teardown → gate release) while we
        // waited. Re-read membership and lifecycle state now that the gate is held, and abort
        // before any side effect if the seat is gone or tearing down.
        seat = GetSeat(seatId);
        if (seat is null || !DisplayResetStillValid(seat.Status))
        {
            _logger.LogWarning(
                "Seat {Id}: removed while resetting display — aborting", seatId);
            throw new InvalidOperationException("Seat was removed while resetting display.");
        }

        await _displayManager.DestroyDisplayAsync(seat, ct);
        await _displayManager.CreateDisplayAsync(seat, ct);

        // Update Apollo config
        if (!string.IsNullOrEmpty(seat.DisplayDevicePath))
            _apolloManager.UpdateDisplayOutput(seat, seat.DisplayDevicePath);

        _ = BroadcastState(seat);
        _logger.LogInformation("Seat {Id}: display reset", seatId);
    }

    /// <summary>Recreate the virtual controller for a seat.</summary>
    public async Task ResetController(Guid seatId)
    {
        // Per-seat lifecycle gate. DestroyController + CreateController + AssignController
        // mutate the ViGEm driver state and the InputRouter routing; the gate makes the
        // entire destroy/create/assign transaction atomic with respect to teardown and
        // other lifecycle callers. Without the gate, a concurrent teardown can interleave
        // between DestroyController and CreateController, leaving a real ViGEm virtual
        // controller registered for a seat that no longer exists in _seats (orphan
        // controller + stuck XInput→controller routing in InputRouter).
        using var lease = await _lifecycleGate.AcquireAsync(seatId, CancellationToken.None);

        // The seat was captured BEFORE the gate; a concurrent teardown could have
        // removed the seat (and disposed the ViGEm client we are about to talk to)
        // while we waited. Re-check membership and lifecycle state now that the gate
        // is held. TearingDown is the H2 "removed" signal — by then the seat is gone
        // from _seats and CreateController would orphan the virtual controller. Throw
        // to surface the lost race to the caller (the API endpoint maps this to 400
        // BadRequest, matching the original "Seat not found." semantics for the
        // not-yet-torn-down case).
        var seat = GetSeat(seatId);
        if (seat is null || !ControllerResetStillValid(seat.Status))
        {
            _logger.LogWarning(
                "Seat {Id}: removed while resetting controller — aborting", seatId);
            throw new InvalidOperationException("Seat was removed while resetting controller.");
        }

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

    /// <summary>
    /// True when MultiSeat manages ViGEm virtual controllers + physical-XInput routing
    /// (EnableViGEmController). When false (default), Apollo forwards the Moonlight client's
    /// controller natively and the Input-tab assignment UI has no effect. Read from the
    /// bound options here (the API's inner DI container doesn't bind MultiSeatOptions).
    /// </summary>
    public bool ControllerRoutingEnabled => _options.EnableViGEmController;



    public IReadOnlyList<string> GetPairedClients(Guid seatId)
    {
        var seat = GetSeat(seatId);
        if (seat is null) return Array.Empty<string>();
        return _apolloManager.GetSeatPairedClients(seat);
    }

    public bool UnpairClient(Guid seatId, string clientName)
    {
        var seat = GetSeat(seatId);
        if (seat is null) return false;
        return _apolloManager.UnpairSeatClient(seat, clientName);
    }

    public void UnpairAllClients(Guid seatId)
    {
        var seat = GetSeat(seatId);
        if (seat is null) return;
        _apolloManager.UnpairAllSeatClients(seat);
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
