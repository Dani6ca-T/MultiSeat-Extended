namespace MultiSeat.Shared.Models;

/// <summary>
/// The host's own Apollo — the standalone instance a user runs for the console account,
/// separate from the per-seat instances MultiSeat launches.
///
/// MultiSeat deliberately coexists with it: its own Apollo lives in a different install dir and
/// a different port range, and startup cleanup never kills it. The consequence was that the one
/// Apollo the operator uses personally was the only one invisible in the dashboard. This is that
/// instance described the same way a seat is, so the console can be read at a glance alongside
/// the seats.
/// </summary>
public sealed class HostApolloInfo
{
    /// <summary>True when a non-MultiSeat Apollo process is running on this host.</summary>
    public bool Detected { get; set; }

    public int ProcessId { get; set; }
    public string? ExecutablePath { get; set; }
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>Port Moonlight connects to (Apollo's <c>port</c>). Null when it cannot be determined.</summary>
    public int? Port { get; set; }

    /// <summary>Apollo's web UI port — <c>port + 1</c>, served over HTTPS.</summary>
    public int? WebUiPort { get; set; }

    /// <summary>True when Apollo answered a serverinfo query, i.e. Moonlight could reach it too.</summary>
    public bool Reachable { get; set; }

    /// <summary>Server name Apollo reports to clients.</summary>
    public string? HostName { get; set; }

    public string? AppVersion { get; set; }

    /// <summary>True when Apollo reports a client actively streaming.</summary>
    public bool Streaming { get; set; }

    /// <summary>
    /// How many Moonlight clients are paired with this instance, read from Apollo's state file.
    ///
    /// NOT from serverinfo: its PairStatus answers "is the client asking this question paired?",
    /// so a probe with its own uniqueid always gets 0 and the dashboard reported "no paired
    /// clients" on a host with several. -1 when the state file could not be read.
    /// </summary>
    public int PairedClientCount { get; set; } = -1;

    /// <summary>State of the ApolloService Windows service, or null when it is not installed.</summary>
    public string? ServiceStatus { get; set; }

    /// <summary>The console session the host user is on — the host's equivalent of a seat's session.</summary>
    public int ConsoleSessionId { get; set; } = -1;

    /// <summary>
    /// Why the picture is incomplete, when it is — e.g. no standalone Apollo installed, or a
    /// process is running but not answering. Null when everything was determined.
    /// </summary>
    public string? Note { get; set; }
}
