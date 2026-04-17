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
    public bool InputHooks { get; set; }
    public bool Firewall { get; set; }
    public bool Session { get; set; }
}
