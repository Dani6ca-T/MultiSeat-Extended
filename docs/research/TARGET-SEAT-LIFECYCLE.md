# Target Seat Lifecycle

**Date**: 2026-08-30
**Purpose**: Define the complete seat lifecycle from creation to cleanup

---

## Current Lifecycle (9-Step Pipeline)

```
ProvisionSeatAsync
    │
    ├── 1. Validate capacity + account
    ├── 1.5. Allocate port block
    ├── 2. Launch background session (RDP loopback)
    ├── 2.5. Suppress RustDesk audio
    ├── 2.7. Pre-write HidHide rules
    ├── 3. Create virtual display (SudoVDA)
    ├── 4. Open firewall ports
    ├── 5. Audio routing (PerSession — no-op)
    ├── 5.7. Seed emulator configs
    ├── 6. Start Vibepollo
    ├── 6.5. Discover SudoVDA UUID
    ├── 6.6/6.7. Apply display isolation
    ├── 7. Controller + Input routing
    ├── 8. HidHide + Keyboard/Mouse hooks
    └── 9. Ready
```

---

## Target Lifecycle (Enhanced)

### Phase 1: Provisioning

```
CreateSeat
    │
    ├── Validate
    │   ├── Check capacity (ActiveSeatCount < MaxSeats)
    │   ├── Check account exists
    │   └── Apply group membership (Users + RDP Users)
    │
    ├── Allocate
    │   ├── Allocate port block (30 ports)
    │   └── Assign emulator netplay port
    │
    ├── Create User
    │   └── Windows account (if not linked)
    │
    └── Create Session
        └── RDP loopback (mstsc → 127.0.0.2)
```

### Phase 2: Configuration

```
ConfigureSeat
    │
    ├── Suppress RustDesk audio
    ├── Pre-write HidHide rules
    ├── Create virtual display (SudoVDA)
    ├── Open firewall ports
    ├── Seed emulator configs
    └── Apply audio defaults (PerSession — no-op)
```

### Phase 3: Streaming

```
StartStreaming
    │
    ├── Start provider (Vibepollo)
    ├── Discover display UUID (from log)
    ├── Apply display isolation (primary + shrunk)
    ├── Set refresh rate (Hz)
    ├── Create controllers (optional)
    ├── Apply HidHide jail
    └── Ready
```

### Phase 4: Active

```
ActiveState
    │
    ├── Monitor health (5s interval)
    ├── Detect crashes
    ├── Re-detect display (if needed)
    ├── Handle client connect/disconnect
    └── Launch apps (on-connect)
```

### Phase 5: Recovery

```
RecoverSeat
    │
    ├── Detect failure (health check)
    ├── Classify failure
    │   ├── Provider crash → Restart provider
    │   ├── Display lost → Re-detect display
    │   ├── Session lost → Re-create session
    │   └── Full failure → Re-provision
    │
    ├── Apply backoff (progressive)
    ├── Restart components
    └── Resume monitoring
```

### Phase 6: Teardown

```
TeardownSeat
    │
    ├── Stop apps (on-connect)
    ├── Uninstall input hooks
    ├── Uncloak HidHide
    ├── Unassign controllers
    ├── Destroy controllers
    ├── Stop provider
    ├── Close Job Object (kill all processes)
    ├── Close firewall ports
    ├── Destroy display
    ├── Disconnect session
    ├── Logoff session
    ├── Release ports
    ├── Cleanup config
    └── Delete user (if created)
```

---

## State Transitions

```
Idle ──→ Provisioning ──→ Configuring ──→ Ready ──→ Streaming
                                                      │
                                                      ├──→ TearingDown ──→ Idle
                                                      │
                                                      └──→ Error ──→ TearingDown ──→ Idle
```

### Transitions

| From | To | Trigger |
|------|-----|---------|
| Idle | Provisioning | ProvisionSeatAsync called |
| Provisioning | Configuring | Session created |
| Configuring | Ready | All components started |
| Ready | Streaming | Client connects, app launched |
| Streaming | Ready | Client disconnects |
| Ready | TearingDown | TeardownSeatAsync called |
| Streaming | TearingDown | TeardownSeatAsync called |
| Any | Error | Provisioning fails |
| Error | TearingDown | Best-effort cleanup |
| TearingDown | Idle | Cleanup complete |

---

## Recovery Scenarios

### Provider Crash

```
1. Detect: Health check finds Vibepollo unreachable
2. Classify: Provider crash (process not alive)
3. Recover: Restart Vibepollo
4. Verify: Health check passes
5. Resume: Continue monitoring
```

### Display Lost

```
1. Detect: Health check finds no display UUID
2. Classify: Display lost (session disconnect/sleep)
3. Recover: Re-detect display (TryLateDisplayDetectionAsync)
4. Verify: Display UUID found
5. Apply: Display isolation (primary + shrunk)
6. Resume: Continue monitoring
```

### Session Lost

```
1. Detect: Health check finds session disconnected
2. Classify: Session lost (sleep/wake, network)
3. Recover: Reconnect mstsc (same session ID)
4. Verify: Session active
5. Resume: Continue monitoring
```

### Full Failure

```
1. Detect: Multiple components failed
2. Classify: Full failure (beyond recovery)
3. Recover: Re-provision seat (new session, new display)
4. Verify: All components healthy
5. Resume: Continue monitoring
```

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Current lifecycle is 9 steps | SeatManager.cs comments | VERIFIED |
| Teardown is best-effort | TeardownSeatInternalAsync (try/catch each step) | VERIFIED |
| No full re-provision exists | Codebase search | VERIFIED (absent) |
| TryLateDisplayDetectionAsync handles display re-detection | SeatManager.cs | VERIFIED |
| SessionHealthCheck monitors health | SessionHealthCheck.cs | VERIFIED |
| MaxRestartAttempts = 3 | Constants.cs | VERIFIED |
