using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Sessions;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Audio;

/// <summary>
/// Manages audio isolation for each seat.
///
/// Lifecycle per seat:
///   1. On seat provisioning, assigns an available VAC cable endpoint
///   2. Launches an audio helper process inside the seat's session
///      that calls IPolicyConfig.SetDefaultEndpoint() — this makes the
///      VAC cable the default audio output within that session ONLY
///   3. Returns the VAC cable index; stores device ID on seat for Apollo's virtual_sink (mic routing)
///   4. On teardown, releases the cable and restores defaults
///
/// This approach works because Windows maintains separate audio policy
/// state per session (backed by HKCU\Software\Microsoft\Multimedia\Audio
/// for each user profile). Setting the default device from within a session
/// does NOT affect other sessions or the host console.
///
/// Requires:
///   - Virtual Audio Cable (VAC) or VB-Audio Virtual Cable installed
///   - At least one VAC cable per concurrent seat
///   - MultiSeat service must be able to launch processes in target sessions
/// </summary>
public sealed class AudioRouter
{
    private readonly ILogger<AudioRouter> _logger;
    private readonly MultiSeatOptions _options;
    private readonly AudioDeviceEnumerator _deviceEnumerator;
    private readonly ProcessInjector _processInjector;

    private const string VoiceMeeterExe = @"C:\Program Files\VB\Voicemeeter\voicemeeterpro.exe";

    // Available audio seat pairs (populated at startup or on-demand)
    private readonly List<AudioSeatPair> _vacPairs = [];
    private readonly object _vacLock = new();
    private bool _vacScanned;

    // Seat → assigned audio pair mapping
    private readonly ConcurrentDictionary<Guid, AudioAssignment> _assignments = new();

    public AudioRouter(
        ILogger<AudioRouter> logger,
        IOptions<MultiSeatOptions> options,
        AudioDeviceEnumerator deviceEnumerator,
        ProcessInjector processInjector)
    {
        _logger = logger;
        _options = options.Value;
        _deviceEnumerator = deviceEnumerator;
        _processInjector = processInjector;
    }

    /// <summary>
    /// Get all audio seat pairs not yet assigned. Triggers a scan if not done yet.
    /// </summary>
    public IReadOnlyList<AudioSeatPair> GetAvailableSeatPairs()
    {
        EnsureVacScanned();
        lock (_vacLock)
        {
            return _vacPairs
                .Where(p => !_assignments.Values.Any(a => a.Pair.GameRender.DeviceId == p.GameRender.DeviceId))
                .ToList()
                .AsReadOnly();
        }
    }

    /// <summary>
    /// Assign an audio seat pair and populate all audio fields on the seat.
    /// Returns the game-render device's VacCableIndex.
    ///
    /// Requires audiomode:i:1 in the RDP session (set by SessionLauncher in Default.rdp)
    /// so host audio devices are visible inside the session via WASAPI.
    ///
    /// Flow:
    ///   Game audio: session default render → game outputs here → Apollo loopback-captures
    ///   Mic:        Apollo renders Moonlight mic → MicRender → MicCapture ← game captures
    /// SeatManager calls --set-default-render and --set-default-capture helpers inside
    /// the seat session after this returns to scope the IPolicyConfig calls to that session.
    /// </summary>
    public int AssignCable(SeatInfo seat)
    {
        EnsureVacScanned();

        AudioSeatPair? pair;
        lock (_vacLock)
        {
            pair = _vacPairs.FirstOrDefault(p =>
                !_assignments.Values.Any(a => a.Pair.GameRender.DeviceId == p.GameRender.DeviceId));
        }

        if (pair is null)
        {
            _logger.LogError(
                "No available audio devices for seat {Id}. " +
                "Discovered {Count} VAC device(s), {Assigned} already assigned. " +
                "Install more VB-CABLE or VoiceMeeter devices (one per seat).",
                seat.Id, _vacPairs.Count, _assignments.Count);
            throw new InvalidOperationException(
                "No audio devices available. Install more VB-CABLE or VoiceMeeter devices.");
        }

        _assignments.TryAdd(seat.Id, new AudioAssignment(pair, seat.SessionId));

        // Game audio: Apollo audio_sink loopback-captures from the session's default render device.
        seat.AudioGameRenderDeviceId     = pair.GameRender.DeviceId;
        seat.AudioGameRenderFriendlyName = pair.GameRender.FriendlyName;

        // Mic routing: Apollo stream_mic → Steam Streaming Microphone render → games capture from
        // the paired "Microphone (Steam Streaming Microphone)" capture endpoint (session default).
        seat.AudioCaptureDeviceId = pair.MicCapture?.DeviceId ?? string.Empty;

        seat.VacCableIndex = pair.GameRender.VacCableIndex;

        _logger.LogInformation(
            "Assigned audio to seat {Id}: game={Game}, mic capture={Cap}",
            seat.Id,
            pair.GameRender.FriendlyName,
            pair.MicCapture?.FriendlyName ?? "(none — Steam not installed)");

        return seat.VacCableIndex;
    }

    /// <summary>
    /// Release the audio pair assignment when a seat is torn down.
    /// </summary>
    public void ReleaseCable(SeatInfo seat)
    {
        if (_assignments.TryRemove(seat.Id, out var assignment))
        {
            _logger.LogInformation(
                "Released audio (game={Game}) from seat {Id}",
                assignment.Pair.GameRender.FriendlyName,
                seat.Id);
        }
    }

    /// <summary>
    /// Get the assignment info for a specific seat.
    /// </summary>
    public AudioAssignment? GetAssignment(Guid seatId) =>
        _assignments.GetValueOrDefault(seatId);

    /// <summary>
    /// Force a re-scan of audio endpoints. Call this if VAC devices
    /// were installed/removed while the service is running.
    /// </summary>
    public void RescanDevices()
    {
        lock (_vacLock)
        {
            _vacScanned = false;
            _vacPairs.Clear();
        }
        EnsureVacScanned();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PRIVATE
    // ═══════════════════════════════════════════════════════════════════

    private void EnsureVacScanned()
    {
        // Under per-session audio there are no seat cables by design — VB-CABLE and VoiceMeeter
        // are not merely unused, they are expected to be absent. Return an empty set without
        // scanning so we never launch VoiceMeeter on a host that deliberately uninstalled it,
        // and never warn about "missing" devices the mode does not want.
        if (_options.AudioMode == AudioMode.PerSession) return;

        lock (_vacLock)
        {
            if (_vacScanned) return;

            EnsureVoiceMeeterRunning();

            _vacPairs.Clear();
            _vacPairs.AddRange(_deviceEnumerator.FindSeatAudioPairs());
            _vacScanned = true;

            if (_vacPairs.Count == 0)
            {
                _logger.LogWarning(
                    "No audio seat pairs detected. " +
                    "Audio routing will not work. " +
                    "Run prerequisites\\install-prerequisites.ps1 to install VB-CABLE and VoiceMeeter Potato.");
            }
            else if (_vacPairs.Count < _options.MaxSeats)
            {
                _logger.LogWarning(
                    "Only {Count} VAC device(s) found but MaxSeats is {Max}. " +
                    "Each seat requires 1 virtual audio device for game audio. " +
                    "Install more VB-CABLE or VoiceMeeter devices for additional seats.",
                    _vacPairs.Count, _options.MaxSeats);
            }
        }
    }

    /// <summary>
    /// Ensure VoiceMeeter is running. It must be active for its virtual audio devices
    /// to route audio. Starts it in the console session if installed but not running.
    ///
    /// IMPORTANT: Process.Start from SYSTEM launches in Session 0 (non-interactive).
    /// VoiceMeeter is a GUI app — it must run in the interactive console session so
    /// that its WDM audio devices are registered in the right session context.
    /// Use ProcessInjector to target the console session.
    /// </summary>
    private void EnsureVoiceMeeterRunning()
    {
        var running = System.Diagnostics.Process
            .GetProcessesByName("voicemeeterpro")
            .Length > 0;

        if (!running)
        {
            if (!File.Exists(VoiceMeeterExe))
            {
                _logger.LogDebug(
                    "VoiceMeeter not installed at {Path} — skipping startup", VoiceMeeterExe);
                return;
            }

            try
            {
                _ = _processInjector.LaunchInConsoleSessionAsync(
                    VoiceMeeterExe, null, CancellationToken.None);

                _logger.LogInformation(
                    "Started VoiceMeeter Potato in console session for audio routing");

                // Wait for VoiceMeeter to register its audio devices before scanning
                System.Threading.Thread.Sleep(3000);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not start VoiceMeeter Potato in console session. " +
                    "Audio routing for seats using VoiceMeeter virtual devices may not work.");
                return;
            }
        }
        else
        {
            _logger.LogDebug("VoiceMeeter Potato is running");
        }

        // Ensure B1 is enabled on all virtual input strips so mic audio routed
        // through virtual_sink → VoiceMeeter strip reaches the Output capture device.
        VoiceMeeterConfigurator.EnsureMicRouting(_logger);
    }

}

/// <summary>
/// Tracks the audio seat pair assigned to a seat.
/// </summary>
public sealed record AudioAssignment(AudioSeatPair Pair, int SessionId);
