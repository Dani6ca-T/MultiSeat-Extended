using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Emulators;

/// <summary>
/// Seeds a single emulator's per-seat config (e.g. netplay port + shared content dir) into the
/// seat user's profile during provisioning. Implementations are best-effort and must never throw
/// into the provisioning pipeline. Register each implementation as <see cref="IEmulatorConfigSeeder"/>
/// so new emulators (Dolphin, PCSX2, …) drop in without touching SeatManager.
/// </summary>
public interface IEmulatorConfigSeeder
{
    /// <summary>Human-readable emulator name (for logging).</summary>
    string EmulatorName { get; }

    /// <summary>True when this seeder is enabled via configuration.</summary>
    bool IsEnabled { get; }

    /// <summary>Seed this seat's emulator config. Best-effort; should swallow its own errors.</summary>
    Task SeedAsync(SeatInfo seat, CancellationToken ct);
}
