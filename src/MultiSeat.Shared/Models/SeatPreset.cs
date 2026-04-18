namespace MultiSeat.Shared.Models;

/// <summary>
/// Persisted seat definition. Survives service restarts.
/// Seats with AutoStart=true are provisioned automatically at service startup.
/// </summary>
public sealed class SeatPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AccountName { get; set; } = string.Empty;
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int Fps { get; set; } = 60;
    public bool AutoStart { get; set; } = false;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
