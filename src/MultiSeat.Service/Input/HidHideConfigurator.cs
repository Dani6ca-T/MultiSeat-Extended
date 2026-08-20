using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Configuration;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Input;

/// <summary>
/// Configures HidHide device cloaking per seat.
///
/// HidHide is a kernel-level filter driver that hides HID devices from
/// processes that aren't on its whitelist. We use it to:
///
///   1. Hide physical gamepads from the host desktop (prevents double input)
///   2. Only expose each physical gamepad to the Apollo process running
///      in its assigned seat's session
///   3. Hide ViGEm virtual controllers from the host (they should only
///      appear within their target session)
///
/// Architecture:
///   - HidHide maintains a global device blacklist and per-app whitelist
///   - The whitelist is path-based: specific .exe files can see hidden devices
///   - We whitelist Apollo's exe for each seat, plus any game launchers
///   - The blacklist contains the device instance paths of physical gamepads
///
/// CLI Reference (HidHideCLI.exe):
///   --cloak-on / --cloak-off          Enable/disable cloaking globally
///   --dev-gaming                       List gaming (gamepad) devices
///   --dev-all                          List all HID devices
///   --dev-hide     "HID\VID_..."       Add device to blacklist
///   --dev-unhide   "HID\VID_..."       Remove device from blacklist
///   --app-reg      "C:\...\app.exe"    Whitelist an application
///   --app-unreg    "C:\...\app.exe"    Remove app from whitelist
///   --app-list                         List whitelisted applications
///   --cancel                           Exit WITHOUT saving configuration changes
///
/// ⚠️ The value goes DIRECTLY after the switch. There is no --id and no --path form.
/// This code used to pass "--dev-hide --id &lt;path&gt;", so HidHide received the literal string
/// "--id" as the device instance path, matched no device, and exited 0. Every hide, unhide
/// and whitelist call was a silent no-op while HidHide sat installed and healthy — the same
/// shape of bug as the VoiceMeeter path. Verified against HidHideCLI --help on a real
/// install after @jmlopezdona reported it in issue #19.
///
/// ⚠️ Read-only queries append --cancel. HidHideCLI SAVES ITS CONFIGURATION ON EXIT even
/// when the invocation only listed something, so a bare --dev-gaming rewrites the very
/// config it was asked to report on. That is what the CLI's own --cancel switch documents.
///
/// Requires HidHide v1.4+:
///   https://github.com/nefarius/HidHide/releases
/// </summary>
public sealed class HidHideConfigurator
{
    private readonly ILogger<HidHideConfigurator> _logger;
    private readonly string _cliPath;
    private readonly string _apolloExePath;
    private bool _driverAvailable;

    // Track what we've configured so we can clean up on teardown
    private readonly ConcurrentDictionary<Guid, SeatCloakingState> _seatStates = new();

    public HidHideConfigurator(ILogger<HidHideConfigurator> logger, IOptions<MultiSeatOptions> options)
    {
        _logger = logger;
        _cliPath = options.Value.HidHideCliPath;
        _apolloExePath = options.Value.ApolloExePath;
        _driverAvailable = File.Exists(_cliPath);

        if (!_driverAvailable)
        {
            _logger.LogWarning(
                "HidHide CLI not found at {Path}. Controller cloaking disabled. " +
                "Install HidHide from https://github.com/nefarius/HidHide/releases",
                _cliPath);
        }
    }

    public bool IsDriverAvailable => _driverAvailable;

    /// <summary>
    /// Called on service startup to clear any HidHide state left over from a previous run.
    ///
    /// HidHide is a kernel driver — its blacklist and cloak state persist across reboots.
    /// The in-memory _seatStates dict is lost on restart, so UncloakForSession would never
    /// clean up. This method turns off cloaking and unhides all gaming devices so the system
    /// starts clean regardless of what the previous run left behind.
    /// </summary>
    public void ResetOnStartup()
    {
        if (!_driverAvailable) return;

        try
        {
            // Unhide any gaming devices that may have been left in the blacklist
            var gamingDevices = ListGamingDevices();
            foreach (var deviceId in gamingDevices)
            {
                RunCli(UnhideDeviceArgs(deviceId));
                _logger.LogDebug("Startup cleanup: unhid device {DeviceId}", deviceId);
            }

            // Disable cloaking globally
            RunCli("--cloak-off");

            _logger.LogInformation(
                "HidHide startup reset: cloaking disabled, {Count} device(s) unhidden",
                gamingDevices.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HidHide startup reset failed (non-critical)");
        }
    }

    /// <summary>
    /// Discover all gaming HID devices (gamepads, joysticks) on the system.
    /// Returns their device instance paths.
    /// </summary>
    public List<string> ListGamingDevices()
    {
        if (!_driverAvailable) return [];

        var output = RunCli(ListGamingDevicesArgs);
        return ParseDeviceList(output);
    }

    /// <summary>
    /// Get the list of currently whitelisted application paths.
    /// </summary>
    public List<string> ListWhitelistedApps()
    {
        if (!_driverAvailable) return [];

        var output = RunCli(ListAppsArgs);
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("--"))
            .ToList();
    }

    /// <summary>
    /// Configure cloaking for a seat:
    ///   1. Hide the physical gamepad(s) assigned to this seat from all processes
    ///   2. Whitelist the seat's Apollo process so it CAN see the gamepads
    ///   3. Enable cloaking if not already on
    /// </summary>
    public void CloakForSession(SeatInfo seat)
    {
        if (!_driverAvailable)
        {
            _logger.LogDebug("HidHide not available — skipping cloaking for seat {Id}", seat.Id);
            return;
        }

        var state = new SeatCloakingState();

        try
        {
            // Step 1: Discover gaming devices to hide
            var gamingDevices = ListGamingDevices();
            if (gamingDevices.Count == 0)
            {
                _logger.LogDebug("No gaming devices found to cloak for seat {Id}", seat.Id);
                return;
            }

            // Step 2: Hide all physical gaming devices
            // (This is a system-wide operation — all physical gamepads get hidden)
            foreach (var deviceId in gamingDevices)
            {
                var result = RunCli(HideDeviceArgs(deviceId));
                state.HiddenDevices.Add(deviceId);
                _logger.LogDebug("Hidden device: {DeviceId}", deviceId);
            }

            // Step 3: Whitelist Apollo for this seat's session
            // Apollo needs to see the virtual controller (ViGEm) to capture input
            var apolloPath = GetApolloExePath();
            if (apolloPath is not null)
            {
                RunCli(RegisterAppArgs(apolloPath));
                state.WhitelistedApps.Add(apolloPath);
                _logger.LogDebug("Whitelisted Apollo: {Path}", apolloPath);
            }

            // Step 4: Enable cloaking globally
            RunCli("--cloak-on");
            state.CloakingEnabled = true;

            _seatStates.TryAdd(seat.Id, state);

            _logger.LogInformation(
                "HidHide cloaking applied for seat {Id}: {Devices} devices hidden, " +
                "{Apps} apps whitelisted",
                seat.Id, state.HiddenDevices.Count, state.WhitelistedApps.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure HidHide cloaking for seat {Id}", seat.Id);
        }
    }

    /// <summary>
    /// Remove cloaking configuration for a seat.
    /// Unhides devices and removes whitelisted apps that were added for this seat.
    /// </summary>
    public void UncloakForSession(SeatInfo seat)
    {
        if (!_driverAvailable) return;

        if (!_seatStates.TryRemove(seat.Id, out var state))
            return;

        try
        {
            // Remove app whitelist entries we added
            foreach (var appPath in state.WhitelistedApps)
            {
                RunCli(UnregisterAppArgs(appPath));
                _logger.LogDebug("Unwhitelisted: {Path}", appPath);
            }

            // If no other seats are active, unhide all devices and disable cloaking
            if (_seatStates.IsEmpty)
            {
                foreach (var deviceId in state.HiddenDevices)
                {
                    RunCli(UnhideDeviceArgs(deviceId));
                    _logger.LogDebug("Unhidden device: {DeviceId}", deviceId);
                }

                RunCli("--cloak-off");
                _logger.LogInformation("HidHide cloaking disabled — no active seats");
            }
            else
            {
                _logger.LogInformation(
                    "HidHide state cleaned for seat {Id} — {Remaining} seats still active, " +
                    "cloaking remains on",
                    seat.Id, _seatStates.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up HidHide state for seat {Id}", seat.Id);
        }
    }

    /// <summary>
    /// Whitelist an additional application (e.g., a game launcher)
    /// so it can see hidden devices.
    /// </summary>
    public void WhitelistApplication(string exePath)
    {
        if (!_driverAvailable) return;

        RunCli(RegisterAppArgs(exePath));
        _logger.LogInformation("Whitelisted application: {Path}", exePath);
    }

    /// <summary>
    /// Remove an application from the whitelist.
    /// </summary>
    public void UnwhitelistApplication(string exePath)
    {
        if (!_driverAvailable) return;

        RunCli(UnregisterAppArgs(exePath));
        _logger.LogInformation("Unwhitelisted application: {Path}", exePath);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PRIVATE
    // ═══════════════════════════════════════════════════════════════════

    // ---- Argument construction -------------------------------------------------------
    // Centralised because these were wrong at ELEVEN call sites and nothing noticed. Every
    // switch takes its value directly, quoted; there is no --id and no --path form. Covered
    // by HidHideArgumentTests, which asserts those forms can never come back.

    internal static string HideDeviceArgs(string deviceInstancePath) =>
        $"--dev-hide \"{deviceInstancePath}\"";

    internal static string UnhideDeviceArgs(string deviceInstancePath) =>
        $"--dev-unhide \"{deviceInstancePath}\"";

    internal static string RegisterAppArgs(string exePath) =>
        $"--app-reg \"{exePath}\"";

    internal static string UnregisterAppArgs(string exePath) =>
        $"--app-unreg \"{exePath}\"";

    // --cancel on reads: HidHideCLI saves its config on exit even for a pure listing.
    internal const string ListGamingDevicesArgs = "--dev-gaming --cancel";
    internal const string ListAppsArgs          = "--app-list --cancel";

    private string RunCli(string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _cliPath,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(10_000))
        {
            _logger.LogWarning("HidHide CLI timed out for: {Args}", arguments);
            try { process.Kill(); } catch { /* best effort */ }
            return string.Empty;
        }

        if (process.ExitCode != 0)
        {
            _logger.LogWarning(
                "HidHide CLI exited with code {Code} for: {Args}\nstderr: {Err}",
                process.ExitCode, arguments, stderr.ToString().Trim());
        }

        return stdout.ToString();
    }

    private static List<string> ParseDeviceList(string cliOutput)
    {
        // HidHide CLI outputs device instance paths, one per line.
        // Filter out header/comment lines.
        return cliOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line =>
                !string.IsNullOrWhiteSpace(line) &&
                !line.StartsWith("--") &&
                (line.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase) ||
                 line.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private string? GetApolloExePath()
    {
        // Use the configured Apollo path (MultiSeat's own ApolloVibe install) so
        // cloaking whitelists the same binary MultiSeat launches for seats.
        return File.Exists(_apolloExePath) ? _apolloExePath : null;
    }

    /// <summary>
    /// Tracks the HidHide configuration state for a single seat,
    /// so we can undo it on teardown.
    /// </summary>
    private sealed class SeatCloakingState
    {
        public List<string> HiddenDevices { get; } = [];
        public List<string> WhitelistedApps { get; } = [];
        public bool CloakingEnabled { get; set; }
    }
}
