using System.Collections.Concurrent;
using System.Diagnostics;
using MultiSeat.Shared;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Streaming;

/// <summary>
/// Manages Windows Firewall rules for per-seat Apollo port ranges.
///
/// Each seat needs inbound TCP+UDP rules for its port block so that
/// Moonlight clients on the LAN can connect. Rules are created on
/// seat provision and removed on teardown.
///
/// Port layout per seat (Apollo map_port offsets from PortBase):
///   -5  GFE HTTPS (TCP) — Moonlight pairing + launch (primary)
///    0  GFE HTTP  (TCP) — Moonlight pairing + launch (plaintext fallback)
///    1  Web UI    (TCP) — Apollo web UI
///    9  Video     (UDP) — RTP video stream
///   10  Control   (UDP) — ENet control channel
///   11  Audio     (UDP) — RTP audio stream
///   12  Mic       (UDP) — RTP mic stream (stream_mic)
///   26  RTSP      (TCP) — Session setup handshake (Sunshine stock 48010)
///
/// Uses netsh advfirewall (available on all Windows 10/11 builds).
/// Requires running as SYSTEM (Windows Service context).
/// </summary>
public sealed class FirewallManager
{
    private readonly ILogger<FirewallManager> _logger;

    // Track which seats have firewall rules so we can clean up
    private readonly ConcurrentDictionary<Guid, string> _ruleNames = new();

    public FirewallManager(ILogger<FirewallManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Ensure the MultiSeat API (dashboard) port is open in the Windows Firewall.
    /// Called once at service startup so the dashboard is reachable from LAN devices.
    /// The rule is idempotent — no-op if it already exists.
    /// </summary>
    public async Task EnsureApiPortOpenAsync(int port, CancellationToken ct)
    {
        const string ruleName = "MultiSeat-API";

        var (existCode, _) = await RunNetshAsync(
            $"advfirewall firewall show rule name=\"{ruleName}\"", ct);

        if (existCode == 0)
        {
            _logger.LogDebug("Firewall rule '{Rule}' already exists — skipping", ruleName);
            return;
        }

        await RunNetshAsync(
            $"advfirewall firewall add rule name=\"{ruleName}\" " +
            $"dir=in action=allow protocol=TCP localport={port} " +
            $"profile=any " +
            $"description=\"MultiSeat dashboard and API\"",
            ct);

        _logger.LogInformation(
            "Created firewall rule '{Rule}' for API port {Port}", ruleName, port);
    }

    /// <summary>
    /// Create inbound firewall rules for a seat's port range.
    /// </summary>
    public async Task OpenPortsAsync(SeatInfo seat, CancellationToken ct)
    {
        var ruleName = $"MultiSeat-Seat-{seat.Id:N}";
        var portBase = seat.PortBase;

        // TCP: GFE HTTPS (pairing/launch), GFE HTTP (fallback), web UI, RTSP setup
        var tcpPorts = string.Join(',',
            portBase + Constants.OffsetGfeHttps,
            portBase + Constants.OffsetGfeHttp,
            portBase + Constants.OffsetWebUi,
            portBase + Constants.OffsetRtsp);
        await RunNetshAsync(
            $"advfirewall firewall add rule name=\"{ruleName}-TCP\" " +
            $"dir=in action=allow protocol=TCP localport={tcpPorts} " +
            $"profile=any " +
            $"description=\"MultiSeat Apollo streaming (TCP)\"",
            ct);

        // UDP: video + control + audio + mic
        var udpPorts = string.Join(',',
            portBase + Constants.OffsetVideo,
            portBase + Constants.OffsetControl,
            portBase + Constants.OffsetAudio,
            portBase + Constants.OffsetMic);
        await RunNetshAsync(
            $"advfirewall firewall add rule name=\"{ruleName}-UDP\" " +
            $"dir=in action=allow protocol=UDP localport={udpPorts} " +
            $"profile=any " +
            $"description=\"MultiSeat Apollo streaming (UDP)\"",
            ct);

        _ruleNames[seat.Id] = ruleName;

        _logger.LogInformation(
            "Seat {Id}: firewall opened — TCP:{TcpPorts} UDP:{UdpPorts}",
            seat.Id, tcpPorts, udpPorts);
    }

    /// <summary>
    /// Remove firewall rules for a seat.
    /// </summary>
    public async Task ClosePortsAsync(SeatInfo seat, CancellationToken ct)
    {
        if (!_ruleNames.TryRemove(seat.Id, out var ruleName))
        {
            // Try the default naming convention
            ruleName = $"MultiSeat-Seat-{seat.Id:N}";
        }

        await RunNetshAsync(
            $"advfirewall firewall delete rule name=\"{ruleName}-TCP\"", ct);
        await RunNetshAsync(
            $"advfirewall firewall delete rule name=\"{ruleName}-UDP\"", ct);

        _logger.LogInformation("Seat {Id}: firewall rules removed", seat.Id);
    }

    /// <summary>
    /// Check if firewall rules exist for a seat.
    /// </summary>
    public async Task<bool> RulesExistAsync(SeatInfo seat, CancellationToken ct)
    {
        var ruleName = $"MultiSeat-Seat-{seat.Id:N}";
        var (exitCode, _) = await RunNetshAsync(
            $"advfirewall firewall show rule name=\"{ruleName}-TCP\"", ct);
        return exitCode == 0;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PRIVATE
    // ═══════════════════════════════════════════════════════════════════

    private async Task<(int ExitCode, string Output)> RunNetshAsync(
        string arguments, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                _logger.LogWarning("Failed to start netsh process");
                return (-1, string.Empty);
            }

            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            var error = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
            {
                _logger.LogDebug("netsh {Args} — exit {Code}: {Error}",
                    arguments, proc.ExitCode, error.Trim());
            }

            return (proc.ExitCode, output);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "netsh command failed: {Args}", arguments);
            return (-1, string.Empty);
        }
    }
}
