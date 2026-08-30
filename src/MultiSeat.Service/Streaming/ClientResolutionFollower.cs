using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Sessions;
using MultiSeat.Service.Storage;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Streaming;

/// <summary>
/// Resizes a seat to the resolution its Moonlight client asked for.
///
/// A seat streams its RDP session surface and nothing inside the session can resize it
/// (issue #15), so Apollo's own <c>dd_resolution_option = auto</c> cannot take effect: it logs
/// <c>[1610] failed to set display mode!</c> and the client gets whatever size the session was
/// created with. The only thing that sets that size is mstsc, so following the client means
/// reconnecting the session at the requested size — which preserves the Windows session, so
/// anything running in the seat survives.
///
/// Rather than detect connect/disconnect edges, this reads the LAST mode in the log each tick
/// and acts when it differs from the seat's current size. That is self-correcting: a tick that
/// is missed, or a service restart, changes nothing about the answer, and it covers resume as
/// well as first connect without tracking state that could drift.
///
/// Off by default (<see cref="MultiSeatOptions.FollowClientResolution"/>): applying it
/// reconnects the session, which briefly interrupts an active stream.
/// </summary>
public sealed class ClientResolutionFollower
{
    private readonly SeatManager _seats;
    private readonly ApolloManager _apollo;
    private readonly SeatPresetStore _presets;
    private readonly MultiSeatOptions _options;
    private readonly ILogger<ClientResolutionFollower> _logger;

    /// <summary>
    /// Last mode applied per seat, so a mode we already honoured is not re-applied on every
    /// tick. Without it, a client requesting something mstsc will not give exactly — an odd
    /// size, or one clamped by Windows — would cause an endless reconnect loop.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, RequestedMode> _applied = new();

    public ClientResolutionFollower(
        SeatManager seats,
        ApolloManager apollo,
        SeatPresetStore presets,
        IOptions<MultiSeatOptions> options,
        ILogger<ClientResolutionFollower> logger)
    {
        _seats = seats;
        _apollo = apollo;
        _presets = presets;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>What to do about the mode a client asked for.</summary>
    internal enum FollowAction
    {
        /// <summary>The seat is already that size.</summary>
        AlreadyCorrectSize,

        /// <summary>This exact mode was tried and the seat still is not that size.</summary>
        AlreadyAttempted,

        /// <summary>mstsc would silently ignore this geometry, so reconnecting would achieve nothing.</summary>
        GeometryRejected,

        /// <summary>Reconnect the session at the requested size.</summary>
        Resize,
    }

    /// <summary>
    /// The whole decision, with no I/O in it.
    ///
    /// The ordering matters and each branch guards a real failure: without AlreadyCorrectSize a
    /// steady state would reconnect on every tick; without AlreadyAttempted a size Windows refuses
    /// to give reconnects the seat forever, interrupting the stream each time; and a geometry mstsc
    /// ignores must not be attempted at all, since the reconnect would cost an interruption and
    /// change nothing.
    ///
    /// A DIFFERENT request after a refused one must still go through - otherwise one bad size from
    /// one client would freeze the seat's resolution for good.
    /// </summary>
    internal static FollowAction Decide(
        RequestedMode requested, int seatWidth, int seatHeight, RequestedMode? lastApplied)
    {
        if (requested.Width == seatWidth && requested.Height == seatHeight)
            return FollowAction.AlreadyCorrectSize;

        if (lastApplied is not null
            && lastApplied.Width == requested.Width && lastApplied.Height == requested.Height)
            return FollowAction.AlreadyAttempted;

        if (!RdpGeometry.ForClient(requested.Width, requested.Height).IsValid)
            return FollowAction.GeometryRejected;

        return FollowAction.Resize;
    }

    /// <summary>
    /// Returns true when the seat was resized, so the caller can broadcast the change.
    /// Never throws: a failure here must not take down the health check.
    /// </summary>
    public async Task<bool> ProcessSeatAsync(SeatInfo seat, CancellationToken ct)
    {
        if (!_options.FollowClientResolution) return false;
        if (seat.Status is not (SeatStatus.Ready or SeatStatus.Streaming)) return false;

        try
        {
            var logPath = _apollo.GetLogPath(seat.AccountName, _options.ApolloConfigDir);
            if (!File.Exists(logPath)) return false;

            string text;
            // Apollo holds the log open, so share read AND write.
            using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
                text = await sr.ReadToEndAsync(ct);

            var requested = ApolloLogParser.ParseLastRequestedMode(text);
            if (requested is null) return false;

            _applied.TryGetValue(seat.Id, out var last);
            var action = Decide(requested, seat.Width, seat.Height, last);

            // Everything except AlreadyAttempted records the mode: the point of remembering is
            // that we have now dealt with this request, however it turned out.
            if (action is not FollowAction.AlreadyAttempted)
                _applied[seat.Id] = requested;

            if (action is FollowAction.GeometryRejected)
            {
                _logger.LogWarning(
                    "Seat {Id}: client asked for {W}x{H}, which mstsc would ignore — leaving the " +
                    "seat at {CurW}x{CurH}",
                    seat.Id, requested.Width, requested.Height, seat.Width, seat.Height);
            }

            if (action is not FollowAction.Resize) return false;

            _logger.LogInformation(
                "Seat {Id}: client requested {W}x{H}{Hz} but the session is {CurW}x{CurH} — " +
                "reconnecting it at the requested size",
                seat.Id, requested.Width, requested.Height,
                requested.RefreshHz is null ? "" : $"@{requested.RefreshHz}Hz",
                seat.Width, seat.Height);

            await _seats.SetResolutionAsync(seat.Id, requested.Width, requested.Height, _presets, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Seat {Id}: could not follow the client's requested resolution (non-critical)", seat.Id);
            return false;
        }
    }
}
