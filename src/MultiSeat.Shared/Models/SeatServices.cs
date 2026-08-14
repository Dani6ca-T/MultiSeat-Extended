namespace MultiSeat.Shared.Models;

/// <summary>
/// Live status of each subsystem running for a seat.
/// </summary>
public sealed class SeatServices
{
    public bool Apollo { get; set; }
    public int ApolloRestarts { get; set; }
    public bool Display { get; set; }
    public bool Audio { get; set; }
    public bool Controller { get; set; }

    /// <summary>
    /// True when MultiSeat manages a ViGEm virtual controller for this seat
    /// (EnableViGEmController). When false (the default), Apollo forwards the
    /// Moonlight client's controller natively and <see cref="Controller"/> is
    /// not a meaningful health signal — the dashboard shows "Native" instead of
    /// a down/grey light.
    /// </summary>
    public bool ControllerManaged { get; set; }

    /// <summary>
    /// True when MultiSeat assigns this seat a host-side virtual audio cable
    /// (AudioMode = SharedHost). When false, audio is per-session: the seat's RDP
    /// session owns a private "Remote Audio" endpoint that Apollo captures directly,
    /// MultiSeat assigns no device, and <see cref="Audio"/> is not a device-health
    /// signal — the dashboard shows "Session" rather than a down light.
    /// </summary>
    public bool AudioManaged { get; set; }
    public bool InputHooks { get; set; }
    public bool Firewall { get; set; }
    public bool Session { get; set; }
}
