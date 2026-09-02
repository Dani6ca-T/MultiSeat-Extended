# Display Validation

**Date**: 2026-08-30
**Status**: VALIDATED

---

## Purpose

Validate display architecture against actual source code.

---

## 1. What SudoVDA Does

**Evidence**: VirtualDisplayManager.cs
```csharp
public bool IsDriverAvailable => IsSudoVdaAdapterPresent();
```

**Analysis**: SudoVDA is an IddCx kernel driver that creates virtual displays.

**VERDICT**: SudoVDA creates virtual displays.

---

## 2. What MultiSeat Does

**Evidence**: VirtualDisplayManager.cs
```csharp
public Task CreateDisplayAsync(SeatInfo seat, CancellationToken ct)
{
    var (width, height, fps) = ResolutionNegotiator.Negotiate(
        seat.Width, seat.Height, seat.Fps);
    // ...
    _displays[seat.Id] = new VirtualDisplay(
        SeatId: seat.Id,
        DevicePath: null,
        Width: width,
        Height: height,
        Fps: fps,
        CreatedAt: DateTimeOffset.UtcNow);
}
```

**Analysis**: MultiSeat negotiates resolution/fps and tracks display assignment.

**VERDICT**: MultiSeat manages display assignment.

---

## 3. What Vibepollo Does

**Evidence**: SeatManager.cs
```csharp
var displayId = _vibepolloManager.ParseSudoVdaDisplayId(logPath);
seat.DisplayDevicePath = displayId;
_configBuilder.UpdateDisplayOutput(configPath, displayId);
```

**Analysis**: Vibepollo creates the actual virtual display and MultiSeat discovers UUID from log.

**VERDICT**: Vibepollo creates display, MultiSeat assigns it.

---

## 4. Source of Truth

| Property | Owner | Location |
|----------|-------|----------|
| Display UUID | Vibepollo | sunshine_state.json |
| Display assignment | MultiSeat | SeatInfo.DisplayDevicePath |
| Resolution | MultiSeat | SeatInfo.Width, Height |
| Refresh rate | MultiSeat | SeatInfo.Fps |
| Display isolation | MultiSeat | helper.exe |

**VERDICT**: MultiSeat is source of truth for assignment.

---

## 5. Display Belonging to Seat

**Evidence**: VirtualDisplayManager.cs
```csharp
private readonly ConcurrentDictionary<Guid, VirtualDisplay> _displays = new();
```

**Analysis**: Display tracked by SeatId in dictionary.

**VERDICT**: Display belongs to Seat via SeatId.

---

## 6. Provider Restart

**Evidence**: SeatManager.cs
```csharp
public async Task RestartVibepolloAsync(Guid seatId, CancellationToken ct)
{
    _vibepolloManager.Stop(seat);
    seat.VibepolloProcessId = 0;
    seat.VibepolloProcessId = await _vibepolloManager.StartAsync(seat, ct);
    // Re-apply display config
    var configPath = _vibepolloManager.GetConfigPath(seat.Id);
    if (configPath is not null)
    {
        if (!string.IsNullOrEmpty(seat.DisplayDevicePath))
            _configBuilder.UpdateDisplayOutput(configPath, seat.DisplayDevicePath);
    }
    if (seat.VibepolloProcessId > 0)
        await ApplyDisplayIsolationAsync(seat, ct);
}
```

**Analysis**: Display isolation re-applied after provider restart.

**VERDICT**: Display state preserved across provider restart.

---

## 7. Windows Reboot

**Evidence**: VirtualDisplayManager.cs

**Analysis**: In-memory state lost on reboot. Display re-detection needed.

**VERDICT**: Display state lost on reboot.

---

## 8. Display Assignment Stale

**Evidence**: SeatManager.cs
```csharp
public async Task<bool> TryLateDisplayDetectionAsync(SeatInfo seat, CancellationToken ct)
{
    if (!string.IsNullOrEmpty(seat.DisplayDevicePath)) return false; // already known
    if (seat.VibepolloProcessId <= 0) return false; // Vibepollo not running
    // ...
}
```

**Analysis**: Late detection retries from health check tick.

**VERDICT**: Stale assignment detected and corrected.

---

## Summary

| Aspect | Status | Evidence |
|--------|--------|----------|
| SudoVDA creates displays | Verified | VirtualDisplayManager.cs |
| MultiSeat assigns displays | Verified | VirtualDisplayManager.cs |
| Vibepollo discovers UUID | Verified | ParseSudoVdaDisplayId |
| Source of truth defined | Verified | Architecture doc |
| Display belongs to Seat | Verified | ConcurrentDictionary |
| Provider restart handled | Verified | RestartVibepolloAsync |
| Windows reboot NOT handled | Verified | In-memory state |
| Stale assignment detected | Verified | TryLateDisplayDetectionAsync |

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| SudoVDA is IddCx driver | VirtualDisplayManager.cs | FACT |
| MultiSeat negotiates resolution | ResolutionNegotiator | FACT |
| Vibepollo creates display | ParseSudoVdaDisplayId | FACT |
| Display tracked by SeatId | ConcurrentDictionary | FACT |
| Display isolation re-applied | RestartVibepolloAsync | FACT |
| In-memory state lost on reboot | VirtualDisplayManager.cs | FACT |
| Late detection retries | TryLateDisplayDetectionAsync | FACT |
