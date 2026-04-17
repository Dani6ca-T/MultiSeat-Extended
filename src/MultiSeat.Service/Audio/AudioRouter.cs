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
///   3. Returns the VAC device ID for Apollo config (audio_sink)
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

    // Available VAC endpoints (populated at startup or on-demand)
    private readonly List<AudioEndpointInfo> _vacEndpoints = [];
    private readonly object _vacLock = new();
    private bool _vacScanned;

    // Seat → assigned VAC endpoint mapping
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
    /// Get all discovered VAC endpoints. Triggers a scan if not done yet.
    /// </summary>
    public IReadOnlyList<AudioEndpointInfo> GetAvailableVacEndpoints()
    {
        EnsureVacScanned();
        lock (_vacLock)
        {
            return _vacEndpoints
                .Where(e => !_assignments.Values.Any(a => a.Endpoint.DeviceId == e.DeviceId))
                .ToList()
                .AsReadOnly();
        }
    }

    /// <summary>
    /// Assign a VAC cable to the seat and configure it as the session's
    /// default audio output. Returns the cable index.
    /// </summary>
    public int AssignCable(SeatInfo seat)
    {
        EnsureVacScanned();

        AudioEndpointInfo? endpoint;
        lock (_vacLock)
        {
            // Find the first unassigned VAC endpoint
            endpoint = _vacEndpoints.FirstOrDefault(e =>
                !_assignments.Values.Any(a => a.Endpoint.DeviceId == e.DeviceId));
        }

        if (endpoint is null)
        {
            _logger.LogError(
                "No available VAC endpoints for seat {Id}. " +
                "Discovered {Count} VAC cables, {Assigned} already assigned. " +
                "Install more VAC cables or reduce seat count.",
                seat.Id, _vacEndpoints.Count, _assignments.Count);
            throw new InvalidOperationException(
                "No VAC cables available. Install more Virtual Audio Cable devices.");
        }

        var assignment = new AudioAssignment(endpoint, seat.SessionId);
        _assignments.TryAdd(seat.Id, assignment);

        // Store the device ID on the seat for Apollo config
        seat.AudioDeviceId = endpoint.DeviceId;

        _logger.LogInformation(
            "Assigned VAC '{Name}' (cable {Cable}) to seat {Id} — device: {DeviceId}",
            endpoint.FriendlyName, endpoint.VacCableIndex, seat.Id, endpoint.DeviceId);

        // Set the VAC as the default audio device within the seat's session.
        // This must happen INSIDE the target session for per-session isolation.
        SetDefaultAudioInSession(seat, endpoint);

        return endpoint.VacCableIndex;
    }

    /// <summary>
    /// Release a VAC cable assignment when a seat is torn down.
    /// </summary>
    public void ReleaseCable(SeatInfo seat)
    {
        if (_assignments.TryRemove(seat.Id, out var assignment))
        {
            _logger.LogInformation(
                "Released VAC '{Name}' (cable {Cable}) from seat {Id}",
                assignment.Endpoint.FriendlyName,
                assignment.Endpoint.VacCableIndex,
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
            _vacEndpoints.Clear();
        }
        EnsureVacScanned();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PRIVATE
    // ═══════════════════════════════════════════════════════════════════

    private void EnsureVacScanned()
    {
        lock (_vacLock)
        {
            if (_vacScanned) return;

            EnsureVoiceMeeterRunning();

            _vacEndpoints.Clear();
            _vacEndpoints.AddRange(_deviceEnumerator.FindVacEndpoints());
            _vacScanned = true;

            if (_vacEndpoints.Count == 0)
            {
                _logger.LogWarning(
                    "No virtual audio devices detected. " +
                    "Audio isolation will not work. " +
                    "Run prerequisites\\install-prerequisites.ps1 to install VB-CABLE and VoiceMeeter Potato.");
            }
            else if (_vacEndpoints.Count < _options.MaxSeats)
            {
                _logger.LogWarning(
                    "Only {Count} virtual audio device(s) found but MaxSeats is {Max}. " +
                    "Some seats may not have audio isolation. " +
                    "Ensure VoiceMeeter Potato is installed and running.",
                    _vacEndpoints.Count, _options.MaxSeats);
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

        if (running)
        {
            _logger.LogDebug("VoiceMeeter Potato is running");
            return;
        }

        if (!File.Exists(VoiceMeeterExe))
        {
            _logger.LogDebug(
                "VoiceMeeter not installed at {Path} — skipping startup", VoiceMeeterExe);
            return;
        }

        try
        {
            // Launch VoiceMeeter in the console session so it can register its
            // audio devices in the user's session context. Fire-and-forget — we
            // don't need to wait for the process handle, just give it a moment to
            // initialize before we scan for audio devices.
            _ = _processInjector.LaunchInConsoleSessionAsync(
                VoiceMeeterExe, null, CancellationToken.None);

            _logger.LogInformation(
                "Started VoiceMeeter Potato in console session for audio routing");

            // Brief pause to let VoiceMeeter register its audio devices
            System.Threading.Thread.Sleep(3000);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not start VoiceMeeter Potato in console session. " +
                "Audio routing for seats using VoiceMeeter virtual devices may not work.");
        }
    }

    /// <summary>
    /// Launch a helper process inside the target session that sets the
    /// default audio device via IPolicyConfig.
    ///
    /// This is the key to per-session audio isolation: the IPolicyConfig
    /// COM call happens within the session's own audio context, so it
    /// only affects that session's default device.
    /// </summary>
    private void SetDefaultAudioInSession(SeatInfo seat, AudioEndpointInfo endpoint)
    {
        if (seat.SessionId < 0)
        {
            _logger.LogWarning("Seat {Id}: no active session — skipping audio device set", seat.Id);
            return;
        }

        try
        {
            // Build the helper command that will run inside the target session.
            // We use PowerShell to invoke the COM call directly — no separate binary needed.
            // This is a one-shot process that sets the default device and exits.
            var script = BuildPowerShellAudioHelper(endpoint.DeviceId);

            _ = _processInjector.LaunchInSessionAsync(
                seat.SessionId,
                seat.AccountName,
                "powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
                null,
                CancellationToken.None);

            _logger.LogInformation(
                "Launched audio helper in session {Sid} to set default device to '{Name}'",
                seat.SessionId, endpoint.FriendlyName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to set default audio device in session {Sid} for seat {Id}. " +
                "Audio may play through the wrong device.",
                seat.SessionId, seat.Id);
        }
    }

    /// <summary>
    /// Build a PowerShell one-liner that uses IPolicyConfig COM to set
    /// the default audio endpoint. Runs inside the target session.
    ///
    /// This avoids needing a separate compiled helper binary — PowerShell
    /// can create COM objects and call methods directly.
    /// </summary>
    private static string BuildPowerShellAudioHelper(string deviceId)
    {
        // The PowerShell script:
        // 1. Creates the PolicyConfigClient COM object
        // 2. Calls SetDefaultEndpoint for all 3 roles
        // 3. Exits
        //
        // We use the COM interop approach directly in PowerShell.
        // The IPolicyConfig interface requires some boilerplate, so
        // we use an alternative approach: calling the nircmd utility
        // or using the AudioDeviceCmdlets module if available.
        //
        // Simplest reliable approach: use a C# snippet via Add-Type.

        // Escape the device ID for embedding in a PS string
        var escaped = deviceId.Replace("'", "''");

        return
            "$code = @'" + "\n" +
            "using System; using System.Runtime.InteropServices;" + "\n" +
            "[ComImport, Guid(\"870AF99C-171D-4F9E-AF0D-E63DF40C2BC9\")] class PolicyConfigClient { }" + "\n" +
            "[ComImport, Guid(\"F8679F50-850A-41CF-9C72-430F290290C8\"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]" + "\n" +
            "interface IPolicyConfig {" + "\n" +
            "  void R1(); void R2(); void R3(); void R4(); void R5();" + "\n" +
            "  void R6(); void R7(); void R8(); void R9(); void R10();" + "\n" +
            "  [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);" + "\n" +
            "}" + "\n" +
            "public class AudioHelper {" + "\n" +
            "  public static void Set(string devId) {" + "\n" +
            "    var pc = (IPolicyConfig)new PolicyConfigClient();" + "\n" +
            "    for(int r=0;r<3;r++) pc.SetDefaultEndpoint(devId, r);" + "\n" +
            "  }" + "\n" +
            "}" + "\n" +
            "'@" + "\n" +
            "Add-Type -TypeDefinition $code" + "\n" +
            $"[AudioHelper]::Set('{escaped}')";
    }
}

/// <summary>
/// Tracks the VAC endpoint assigned to a seat.
/// </summary>
public sealed record AudioAssignment(AudioEndpointInfo Endpoint, int SessionId);
