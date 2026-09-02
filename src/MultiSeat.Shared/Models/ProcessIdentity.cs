namespace MultiSeat.Shared.Models;

/// <summary>
/// Value object that uniquely identifies a process instance, protecting against PID reuse.
///
/// A process ID (PID) alone is insufficient as an identity: Windows recycles PIDs, so
/// a PID captured during seat provisioning may refer to a completely different process
/// after the original exits. By pairing the PID with the process's start time, we can
/// distinguish between the original process and any recycled PID.
///
/// INVARIANT: Two ProcessIdentity values with the same PID but different StartedAt values
/// represent different process instances.
/// </summary>
public readonly record struct ProcessIdentity : IComparable<ProcessIdentity>
{
    /// <summary>
    /// The operating system process ID.
    /// </summary>
    public int ProcessId { get; init; }

    /// <summary>
    /// The UTC time when the process was started.
    /// Used to detect PID reuse — if a PID exists but its start time differs,
    /// the PID was reused by a different process.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    public ProcessIdentity(int processId, DateTimeOffset startedAt)
    {
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId), processId,
                "Process ID must be positive.");
        ProcessId = processId;
        StartedAt = startedAt;
    }

    /// <summary>
    /// Returns true if the given PID and start time match this identity.
    /// Used to detect PID reuse: if the PID exists but the start time differs,
    /// the process is a different instance.
    /// </summary>
    public bool Matches(int pid, DateTimeOffset startTime) =>
        ProcessId == pid && StartedAt == startTime;

    public int CompareTo(ProcessIdentity other) =>
        ProcessId.CompareTo(other.ProcessId);

    public override string ToString() => $"PID {ProcessId} @ {StartedAt:O}";
}
