namespace MultiSeat.Service.Streaming;

/// <summary>
/// Decides whether a running Apollo process belongs to MultiSeat.
///
/// This is the promise that lets MultiSeat coexist with a standalone Apollo serving the console
/// player: we never touch a process we did not launch. Both ways of being wrong are expensive and
/// silent — claim a foreign Apollo and startup cleanup kills someone's live stream; disown our own
/// and orphaned instances accumulate holding the seat's ports.
///
/// It lives here because there were TWO copies of it, and they had drifted: the reporting path
/// guarded both of its emptiness checks, while the path that actually KILLS guarded only one.
/// `cmdLine.Contains("")` is true for every process, so an empty ApolloConfigDir made every
/// sunshine.exe on the host — the console player's included — look managed. One implementation,
/// one set of tests.
/// </summary>
internal static class ApolloOwnership
{
    /// <param name="exePath">The process's executable path, as WMI reports it. May be null.</param>
    /// <param name="cmdLine">The process's command line, as WMI reports it. May be null.</param>
    /// <param name="managedExeDir">Directory MultiSeat launches Apollo from.</param>
    /// <param name="managedConfigDir">Root of the per-seat Apollo config directories.</param>
    /// <remarks>
    /// Every emptiness check here is load-bearing. "anything".StartsWith("") and
    /// "anything".Contains("") are both true, so an unset or unresolved directory would claim
    /// every Apollo on the host rather than none. When nothing is known about a process, the
    /// answer is "not ours" — skipping a process we own is recoverable, killing one we do not is
    /// not.
    /// </remarks>
    internal static bool IsMultiSeatManaged(
        string? exePath, string? cmdLine, string? managedExeDir, string? managedConfigDir)
    {
        if (!string.IsNullOrEmpty(exePath) && !string.IsNullOrEmpty(managedExeDir)
            && exePath.StartsWith(managedExeDir, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(cmdLine) && !string.IsNullOrEmpty(managedConfigDir)
            && cmdLine.Contains(managedConfigDir, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
