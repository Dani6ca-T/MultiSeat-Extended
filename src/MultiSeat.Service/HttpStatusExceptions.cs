namespace MultiSeat.Service;

/// <summary>
/// A request cannot currently be satisfied because server capacity is exhausted (seat
/// limit reached, no port blocks left). The API maps this to 503 Service Unavailable:
/// the condition is temporary and the caller may retry later, unlike a conflict with a
/// specific existing resource.
///
/// Derives from <see cref="InvalidOperationException"/> so every existing non-HTTP
/// catcher (worker autostart, tooling) keeps catching it exactly as before.
/// </summary>
internal sealed class CapacityExhaustedException : InvalidOperationException
{
    public CapacityExhaustedException(string message) : base(message) { }
}

/// <summary>
/// A request conflicts with current server state (account already has a seat, account
/// already exists/is already linked). The API maps this to 409 Conflict: retrying the
/// identical request cannot succeed until the state changes.
///
/// Derives from <see cref="InvalidOperationException"/> so every existing non-HTTP
/// catcher keeps catching it exactly as before.
/// </summary>
internal sealed class ResourceConflictException : InvalidOperationException
{
    public ResourceConflictException(string message) : base(message) { }
}
