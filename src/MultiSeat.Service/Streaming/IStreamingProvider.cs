using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Streaming;

/// <summary>
/// Provider-neutral contract for streaming server lifecycle and status.
///
/// The Core (SeatManager, SessionHealthCheck) depends on this interface
/// rather than on a concrete streaming server implementation. The current
/// implementation is ApolloManager (Apollo/Sunshine fork). Future providers
/// (Foundation-Sunshine, vanilla Sunshine) will implement this same contract.
///
/// This interface covers only streaming lifecycle and health-query operations.
/// Apollo-specific concerns (config generation, log parsing, client pairing,
/// display discovery) remain on the concrete implementation for now and will
/// be addressed in future isolated commits.
/// </summary>
public interface IStreamingProvider
{
    /// <summary>
    /// Start the streaming server for a seat.
    /// Generates per-seat config, launches the process inside the seat's
    /// Windows session, and returns the process ID.
    /// </summary>
    Task<int> StartAsync(SeatInfo seat, CancellationToken ct);

    /// <summary>
    /// Stop the streaming server for a seat.
    /// Kills the entire process tree and removes the instance record.
    /// </summary>
    void Stop(SeatInfo seat);

    /// <summary>
    /// Kill the streaming server but preserve instance state for immediate
    /// restart (used during sleep/reconnect, resolution change, NVENC change).
    /// Resets the crash-restart counter since a reconnect is not a crash.
    /// </summary>
    void KillForReconnect(SeatInfo seat);

    /// <summary>
    /// Restart a crashed streaming server.
    /// Reuses the existing config. Increments the restart counter.
    /// Returns -1 if max restart attempts have been exceeded.
    /// </summary>
    Task<int> RestartAsync(SeatInfo seat, CancellationToken ct);

    /// <summary>
    /// Whether the streaming server process is alive for a seat.
    /// </summary>
    bool IsAlive(Guid seatId);

    /// <summary>
    /// Query whether the streaming server is reachable and actually serving.
    /// Sends the same request a Moonlight client would. Returns null when
    /// the instance did not answer.
    /// </summary>
    Task<Monitoring.ApolloServerInfo?> QueryHealthAsync(SeatInfo seat, CancellationToken ct);

    /// <summary>
    /// How many automatic crash-restart attempts have been made for this seat.
    /// </summary>
    int GetRestartCount(Guid seatId);
}
