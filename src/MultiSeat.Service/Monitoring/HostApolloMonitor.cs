using System.Diagnostics;
using System.Management;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Interop;
using MultiSeat.Service.Streaming;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Monitoring;

/// <summary>
/// Reports on the host's own standalone Apollo — the instance MultiSeat coexists with and
/// deliberately never touches (see MultiSeatWorker.KillOrphanedApolloProcesses).
///
/// Detection is the exact inverse of the cleanup rule: an Apollo is OURS if it runs from the
/// configured MultiSeat Apollo install dir or was launched with a per-seat MultiSeat config, so
/// anything else is the host's. Keeping the two definitions mirrored matters — if they ever
/// disagree, MultiSeat would either report the host's Apollo as a seat's or, far worse, treat
/// the host's as an orphan to reap.
/// </summary>
public sealed partial class HostApolloMonitor
{
    private readonly MultiSeatOptions _options;
    private readonly ApolloServerQuery _serverQuery;
    private readonly ILogger<HostApolloMonitor> _logger;

    /// <summary>Apollo's default <c>port</c> when its config does not say otherwise.</summary>
    private const int DefaultApolloPort = 47989;

    public HostApolloMonitor(
        IOptions<MultiSeatOptions> options,
        ApolloServerQuery serverQuery,
        ILogger<HostApolloMonitor> logger)
    {
        _options = options.Value;
        _serverQuery = serverQuery;
        _logger = logger;
    }

    public async Task<HostApolloInfo> CollectAsync(CancellationToken ct = default)
    {
        var info = new HostApolloInfo
        {
            ConsoleSessionId = (int)Kernel32.WTSGetActiveConsoleSessionId(),
            ServiceStatus = QueryApolloServiceStatus(),
        };

        var host = FindStandaloneApollo();
        if (host is null)
        {
            info.Note = info.ServiceStatus is null
                ? "No standalone Apollo running. MultiSeat does not require one — this is only " +
                  "the Apollo you would run for the console account itself."
                : $"No standalone Apollo process running (ApolloService is {info.ServiceStatus}).";
            return info;
        }

        info.Detected = true;
        info.ProcessId = host.Value.Pid;
        info.ExecutablePath = host.Value.ExePath;
        info.StartedAt = host.Value.StartedAt;

        var port = ResolvePort(host.Value.ExePath);
        info.Port = port;
        info.WebUiPort = port + 1;
        info.PairedClientCount = CountPairedClients(host.Value.ExePath);

        await QueryServerInfoAsync(info, port, ct);
        return info;
    }

    /// <summary>
    /// The first Apollo process that is not MultiSeat's. Mirrors the ownership test used by
    /// startup cleanup. Returns null when only MultiSeat's own instances are running.
    /// </summary>
    private (int Pid, string? ExePath, DateTimeOffset? StartedAt)? FindStandaloneApollo()
    {
        var exeName = Path.GetFileNameWithoutExtension(_options.ApolloExePath); // "sunshine"
        var managedExeDir = Path.GetDirectoryName(_options.ApolloExePath);
        var managedConfigDir = _options.ApolloConfigDir;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process WHERE Name = '{exeName}.exe'");

            foreach (var o in searcher.Get().Cast<ManagementObject>())
            {
                var pid = Convert.ToInt32(o["ProcessId"]);
                var exePath = o["ExecutablePath"] as string;
                var cmdLine = o["CommandLine"] as string;

                if (ApolloOwnership.IsMultiSeatManaged(exePath, cmdLine, managedExeDir, managedConfigDir))
                    continue;

                DateTimeOffset? started = null;
                try { started = Process.GetProcessById(pid).StartTime; } catch { /* raced or denied */ }

                return (pid, exePath, started);
            }
        }
        catch (Exception ex)
        {
            // Same fail-safe posture as cleanup: report nothing rather than guess.
            _logger.LogDebug(ex, "WMI query for standalone Apollo failed");
        }

        return null;
    }


    /// <summary>
    /// Apollo's <c>port</c> from the config next to its executable, or the documented default.
    /// Everything else Moonlight uses is derived from it (web UI is port+1, HTTPS is port-5).
    /// </summary>
    private int ResolvePort(string? exePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(dir)) return DefaultApolloPort;

            var conf = Path.Combine(dir, "config", "sunshine.conf");
            if (!File.Exists(conf)) return DefaultApolloPort;

            foreach (var line in File.ReadLines(conf))
            {
                var m = PortLineRegex().Match(line);
                if (m.Success && int.TryParse(m.Groups["port"].Value, out var p))
                    return p;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the standalone Apollo config; assuming default port");
        }

        return DefaultApolloPort;
    }

    /// <summary>
    /// How many clients are paired, from Apollo's own state file.
    ///
    /// serverinfo cannot answer this: its PairStatus is relative to the uniqueid in the request,
    /// so a dashboard probe using its own id is always told "not paired" — which had this card
    /// reporting no paired clients on a host with two.
    ///
    /// The state file also holds the web UI username, password hash and salt. Only
    /// root.named_devices is read, and only its length is kept; nothing else from that file
    /// enters MultiSeat, let alone the API.
    /// </summary>
    private int CountPairedClients(string? exePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(dir)) return -1;

            var statePath = Path.Combine(dir, "config", "sunshine_state.json");
            if (!File.Exists(statePath)) return -1;

            using var doc = JsonDocument.Parse(File.ReadAllText(statePath));
            if (doc.RootElement.TryGetProperty("root", out var root)
                && root.TryGetProperty("named_devices", out var devices)
                && devices.ValueKind == JsonValueKind.Array)
                return devices.GetArrayLength();

            return 0; // state file present and readable, no devices in it
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the standalone Apollo state file for pairing count");
            return -1;
        }
    }

    /// <summary>
    /// Ask Apollo the same question Moonlight asks. This is the difference between "a process
    /// exists" and "a client could actually use it", which is the part worth showing.
    /// </summary>
    private async Task QueryServerInfoAsync(HostApolloInfo info, int port, CancellationToken ct)
    {
        var server = await _serverQuery.QueryAsync(port, ct);
        if (server is null)
        {
            info.Note =
                $"Apollo is running (PID {info.ProcessId}) but did not answer on port {port}. " +
                "It may still be starting, or be configured on a different port.";
            return;
        }

        info.Reachable = true;
        info.HostName = server.HostName;
        info.AppVersion = server.AppVersion;
        info.Streaming = server.Streaming;
    }

    /// <summary>ApolloService state, or null when the service is not installed.</summary>
    private string? QueryApolloServiceStatus()
    {
        try
        {
            using var sc = new ServiceController("ApolloService");
            return sc.Status.ToString();
        }
        catch
        {
            return null; // not installed — normal on a host that runs Apollo manually
        }
    }

    [GeneratedRegex(@"^\s*port\s*=\s*(?<port>\d{2,5})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PortLineRegex();
}
