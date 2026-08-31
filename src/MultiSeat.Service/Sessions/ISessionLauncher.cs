namespace MultiSeat.Service.Sessions;

/// <summary>
/// Abstraction for RDP loopback session management.
///
/// Covers the session lifecycle operations used by SeatManager, SessionHealthCheck,
/// and the API layer. Does NOT include Windows-specific token acquisition
/// (GetSessionToken) — that belongs to ProcessInjector, which retains a direct
/// dependency on the concrete SessionLauncher.
///
/// INVARIANT-1: A session created by LaunchSessionAsync can be disconnected and
///              reconnected via the same interface.
/// INVARIANT-2: DisconnectSession and LogoffSession are best-effort — they do
///              not throw on failure.
/// INVARIANT-3: RunHelperInSeatSession executes a command inside the seat's
///              RDP session as the seat user.
///
/// This interface follows the same pattern as IProcessGroup, IProcessMonitor,
/// and IProcessTracker: the contract lives alongside the implementation, enabling
/// mocking in tests and loose coupling in the service layer.
/// </summary>
public interface ISessionLauncher
{
    /// <summary>
    /// Launch an RDP loopback session for a seat account.
    /// Connects mstsc to 127.0.0.2 to create a background Windows session.
    /// </summary>
    /// <param name="accountName">Windows account to log in as.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="geometry">Desktop resolution for the session.</param>
    /// <returns>The Windows session ID.</returns>
    Task<int> LaunchSessionAsync(string accountName, CancellationToken ct, RdpGeometry geometry);

    /// <summary>
    /// Disconnect the RDP session (mstsc disconnect, not logoff).
    /// Best-effort: does not throw on failure.
    /// </summary>
    void DisconnectSession(int sessionId);

    /// <summary>
    /// Log off the user from the session.
    /// Best-effort: does not throw on failure.
    /// </summary>
    void LogoffSession(int sessionId);

    /// <summary>
    /// Check whether the Windows session still exists (any state).
    /// Returns false if the session was terminated.
    /// </summary>
    bool IsSessionAlive(int sessionId);

    /// <summary>
    /// Check whether the session is in Active state (not Disconnected).
    /// A Disconnected session breaks QueryDisplayConfig / DXGI.
    /// </summary>
    bool IsSessionActive(int sessionId);

    /// <summary>
    /// Run a command-line tool inside the seat's RDP session as the seat user.
    /// Used for display isolation, refresh-rate clamping, and HDR diagnostics.
    /// </summary>
    /// <param name="sessionId">Windows session ID of the seat.</param>
    /// <param name="accountName">Seat's Windows account (for token acquisition).</param>
    /// <param name="commandLine">Full command line to execute.</param>
    void RunHelperInSeatSession(int sessionId, string accountName, string commandLine);
}
