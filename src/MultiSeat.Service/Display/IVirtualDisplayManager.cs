using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Display;

/// <summary>
/// Abstraction for virtual display lifecycle management.
///
/// Covers the display operations consumed by SeatManager and the diagnostic API.
/// Does NOT expose internal tracking (GetDisplay, ActiveDisplayCount) or the
/// underlying SudoVDA driver detection details beyond the boolean query.
///
/// INVARIANT-1: CreateDisplayAsync must negotiate resolution/fps before returning.
/// INVARIANT-2: DestroyDisplayAsync clears the seat's DisplayDevicePath.
/// INVARIANT-3: IsDriverAvailable checks the PnP registry, not runtime state.
/// INVARIANT-4: EnumerateAllConnectedPaths is diagnostic — it may fail gracefully
///              and return an empty list.
///
/// This is a concrete-service abstraction for dependency inversion. It is NOT the
/// future IDisplayProvider boundary — that is a separate, broader concept that
/// would abstract the display driver itself.
/// </summary>
public interface IVirtualDisplayManager
{
    /// <summary>
    /// Prepare display settings for a seat. Negotiates resolution/fps and
    /// records the assignment. The streaming provider owns the actual virtual
    /// monitor lifecycle via its per-seat config.
    /// </summary>
    Task CreateDisplayAsync(SeatInfo seat, CancellationToken ct);

    /// <summary>
    /// Release the virtual display assignment for a seat.
    /// Clears DisplayDevicePath on the seat info.
    /// </summary>
    Task DestroyDisplayAsync(SeatInfo seat, CancellationToken ct);

    /// <summary>
    /// True if the SudoVDA driver adapter is present in the system (PnP device exists).
    /// Checks the PnP registry — works from Session 0 without display API calls.
    /// </summary>
    bool IsDriverAvailable { get; }

    /// <summary>
    /// Diagnostic: enumerate every connected display path and return their names.
    /// Used by GET /api/system/displays to help diagnose SudoVDA detection issues.
    /// Falls back to empty list on any error.
    /// </summary>
    IReadOnlyList<object> EnumerateAllConnectedPaths();
}
