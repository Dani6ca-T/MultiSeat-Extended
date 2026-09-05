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

/// <summary>
/// A request targets a resource that no longer exists (seat removed by a concurrent
/// teardown while the request entered the lifecycle boundary, account deleted or never
/// managed). The API maps this to 404 Not Found: the endpoint's own pre-check already
/// passed, so only the race remains — retrying the identical request is meaningless
/// until the resource exists again, unlike a validation error in the request itself.
///
/// Derives from <see cref="InvalidOperationException"/> so every existing non-HTTP
/// catcher keeps catching it exactly as before. Only the specific "disappeared"
/// condition is typed this way; validation, illegal-state and backend failures stay
/// plain <see cref="InvalidOperationException"/> and keep mapping to 400.
/// </summary>
internal sealed class ResourceNotFoundException : InvalidOperationException
{
    public ResourceNotFoundException(string message) : base(message) { }
}
