namespace MultiSeat.Shared.Models;

public sealed class AccountInfo
{
    public required string Username { get; init; }
    public string? Sid { get; set; }
    public string? ProfilePath { get; set; }
    public bool IsManaged { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
