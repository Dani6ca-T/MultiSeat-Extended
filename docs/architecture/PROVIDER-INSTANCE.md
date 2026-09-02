# Provider Instance

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define how a provider instance is managed within MultiSeat-Extended.

---

## Provider Instance Model

### Identity

| Property | Type | Description |
|----------|------|-------------|
| ProviderInstanceId | Guid | Unique identifier |
| SeatId | Guid | Owning seat |
| ProviderName | string | Provider type (e.g., "Vibepollo") |
| ProcessId | int | OS process ID |

**FACT**: VibepolloProcessId is stored in SeatInfo.

---

## Multi-Instance Architecture

### Key Insight

**Providers do NOT natively support multi-instance.** Vibepollo is a single-user, single-session daemon. MultiSeat-Extended creates the illusion of multi-instance by:

1. Running each provider in a separate Windows session
2. Assigning unique port blocks per instance
3. Providing separate configuration directories
4. Isolating display targets per instance

### Instance Isolation

| Resource | Isolation Method |
|----------|-----------------|
| Process | Separate Windows session |
| Ports | Unique 30-port block per seat |
| Config | Separate sunshine.conf per seat |
| Display | Separate SudoVDA UUID per seat |
| Audio | Per-session Remote Audio endpoint |
| Input | HidHide session jail |
| Credentials | Separate sunshine_state.json per seat |
| Logs | Separate log file per seat |

**FACT**: MultiSeat-Extended isolates providers via session + port + config.

---

## Instance Lifecycle

### Provisioning

```
1. GenerateConfig(seat)
   └── Creates sunshine.conf in seat's config directory
2. Start(seat)
   └── Launches provider process in seat's session
3. ParseDisplayId(logPath)
   └── Discovers SudoVDA UUID from provider log
4. UpdateDisplayOutput(configPath, displayId)
   └── Points provider to correct display
5. Restart(seat)
   └── Restarts with correct display target
```

### Monitoring

```
1. QueryHealth(port)
   └── HTTP ping every 5 seconds
2. IsAlive(pid)
   └── Process existence check
3. GetRestartCount(seatId)
   └── Track restart attempts
```

### Teardown

```
1. Stop(seat)
   └── Graceful shutdown (close message)
2. ForceTerminate(pid)
   └── Kill if graceful fails
3. CleanupConfig(accountName, configDir)
   └── Remove configuration files
```

---

## Instance Configuration

### sunshine.conf (Per Instance)

```ini
# Provider identity
sunshine_name = "MultiSeat-Seat-{SeatId}"

# Network
port = {PortBase}  # Unique per seat

# Display
output_name = {DisplayUuid}  # SudoVDA UUID

# Encoding
encoder = {NvencPreset}  # 1-7

# Audio
audio_sink = default  # PerSession mode

# Client
apps = {AppsJsonPath}  # Game definitions
```

### sunshine_state.json (Per Instance)

```json
{
  "uuid": "unique-per-instance",
  "cert": "...",
  "key": "...",
  "paired_clients": []
}
```

**FACT**: VibepolloConfigBuilder generates these files.

---

## Instance Health

### Health Check

| Check | Method | Interval | Failure |
|-------|--------|----------|---------|
| Process alive | PID check | 5s | Mark degraded |
| HTTP reachable | GET / | 5s | Mark degraded |
| Streaming status | GET /api/config | 5s | Informational |

### Restart Policy

| Restart | Backoff | Max |
|---------|---------|-----|
| 1st | Immediate | - |
| 2nd | Immediate | - |
| 3rd | 30 seconds | - |
| 4th | 60 seconds | - |
| 5th+ | 120 seconds | MaxRestartAttempts |

**INFERENCE**: Based on Helios ProcessManager pattern.

---

## Instance Identity

### UUID

Each provider instance gets a unique UUID stored in sunshine_state.json. This is critical for:
- Moonlight client pairing
- Display configuration persistence
- Certificate management

### Clone Support

When cloning a seat:
1. Generate new UUID
2. Generate new certificates
3. Clear paired clients
4. Start in Disabled state

**INFERENCE**: Based on Helios v0.8.1 clone feature.

---

## Instance Ports

### Port Block Allocation

```
Seat 0: 48100-48129
Seat 1: 48130-48159
Seat 2: 48160-48189
Seat 3: 48190-48219
```

### Port Usage

| Offset | Port | Purpose |
|--------|------|---------|
| -5 | PortBase-5 | GFE HTTPS (pairing) |
| 0 | PortBase | GFE HTTP (config) |
| 1 | PortBase+1 | Web UI HTTPS |
| 9 | PortBase+9 | Video RTP |
| 10 | PortBase+10 | Control ENet |
| 11 | PortBase+11 | Audio RTP |
| 12 | PortBase+12 | Mic RTP |
| 26 | PortBase+26 | RTSP |

**FACT**: Constants defines port offsets.

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| VibepolloProcessId stored in SeatInfo | SeatInfo model | FACT |
| Config generated per seat | VibepolloConfigBuilder | FACT |
| Ports allocated per seat | PortAllocator | FACT |
| Display UUID per seat | ParseSudoVdaDisplayId | FACT |
| Health check is HTTP | VibepolloServerQuery | FACT |
| MaxRestartAttempts = 3 | Constants.cs | FACT |
