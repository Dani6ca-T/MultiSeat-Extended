namespace MultiSeat.Service.Sessions;

/// <summary>
/// Extension methods for seat state transition validation.
/// </summary>
public static class SeatStateExtensions
{
    private static readonly Dictionary<Shared.Models.SeatStatus, Shared.Models.SeatStatus[]> ValidTransitions = new()
    {
        [Shared.Models.SeatStatus.Idle] = [Shared.Models.SeatStatus.Provisioning],
        [Shared.Models.SeatStatus.Provisioning] = [Shared.Models.SeatStatus.Configuring, Shared.Models.SeatStatus.Error, Shared.Models.SeatStatus.TearingDown],
        [Shared.Models.SeatStatus.Configuring] = [Shared.Models.SeatStatus.Ready, Shared.Models.SeatStatus.Error, Shared.Models.SeatStatus.TearingDown],
        [Shared.Models.SeatStatus.Ready] = [Shared.Models.SeatStatus.Streaming, Shared.Models.SeatStatus.Connecting, Shared.Models.SeatStatus.TearingDown, Shared.Models.SeatStatus.Error],
        [Shared.Models.SeatStatus.Streaming] = [Shared.Models.SeatStatus.Ready, Shared.Models.SeatStatus.Connecting, Shared.Models.SeatStatus.TearingDown, Shared.Models.SeatStatus.Error],
        [Shared.Models.SeatStatus.Connecting] = [Shared.Models.SeatStatus.Ready, Shared.Models.SeatStatus.Streaming, Shared.Models.SeatStatus.TearingDown, Shared.Models.SeatStatus.Error],
        [Shared.Models.SeatStatus.TearingDown] = [Shared.Models.SeatStatus.Idle],
        [Shared.Models.SeatStatus.Error] = [Shared.Models.SeatStatus.TearingDown, Shared.Models.SeatStatus.Provisioning],
    };

    public static bool CanTransitionTo(this Shared.Models.SeatStatus current, Shared.Models.SeatStatus target)
    {
        return ValidTransitions.TryGetValue(current, out var allowed) && allowed.Contains(target);
    }
}
