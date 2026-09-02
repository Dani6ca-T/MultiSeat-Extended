# Configuration Architecture

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define configuration layers, credential separation, and persistence.

---

## Configuration Layers

### 1. Host Configuration

| File | Purpose | Owner |
|------|---------|-------|
| appsettings.json | Service settings | MultiSeat |
| appsettings.Production.json | Production overrides | MultiSeat |

**Contains**:
- MaxSeats
- PortBase
- VibepolloExePath
- VibepolloConfigDir
- ApiPort
- ApiKey (encrypted)
- HidHideCliPath
- InputHookDllPath

**FACT**: MultiSeatOptions reads from appsettings.json.

---

### 2. Seat Configuration

| File | Purpose | Owner |
|------|---------|-------|
| SeatPreset (in-memory) | Seat defaults | MultiSeat |

**Contains**:
- AccountName
- Width, Height, Fps
- NvencPreset
- EnablePlaynite, EnableRtss, EnableLosslessScaling
- HdrMode

**FACT**: SeatPreset stores seat defaults.

---

### 3. Provider Configuration

| File | Purpose | Owner |
|------|---------|-------|
| sunshine.conf | Provider settings | MultiSeat (generated) |
| apps.json | Game definitions | MultiSeat (generated) |
| sunshine_state.json | Pairing, certificates | Provider (written) |

**Contains**:
- sunshine_name
- port
- output_name
- encoder
- audio_sink
- apps

**FACT**: VibepolloConfigBuilder generates these files.

---

### 4. Runtime State

| File | Purpose | Owner |
|------|---------|-------|
| SeatInfo (in-memory) | Seat state | MultiSeat |

**Contains**:
- Id
- Status
- SessionId
- VibepolloProcessId
- DisplayDevicePath
- PortBase

**FACT**: SeatInfo tracks runtime state.

---

### 5. Secrets

| Secret | Storage | Location |
|--------|---------|----------|
| API key | DPAPI | Encrypted file |
| Provider certificates | sunshine_state.json | Per-seat |
| Windows account password | Windows | Local account |

**DECISION**: Secrets never cross public models.

---

## Configuration Boundaries

### MultiSeat Generates

| File | For |
|------|-----|
| sunshine.conf | Provider |
| apps.json | Provider |
| SeatInfo | Runtime |

### Provider Writes

| File | Purpose |
|------|---------|
| sunshine_state.json | Certificates, pairing |
| Log files | Diagnostics |

### User Configures

| File | Purpose |
|------|---------|
| appsettings.json | Host settings |
| Dashboard UI | Seat settings |

---

## Credential Separation

### Rule

Credentials must NOT appear in:
- SeatSpec (API wire model)
- ProviderConfiguration (config files)
- Command line
- Environment variables
- Logs

### Implementation

```csharp
// BAD: Credentials in config
public class SeatSpec
{
    public string Password { get; set; } // WRONG
}

// GOOD: Credentials in DPAPI
public class SeatSpec
{
    public string AccountName { get; set; } // OK
    // Password stored in DPAPI, not in SeatSpec
}
```

---

## Configuration Persistence

### Current State

| Data | Persistence |
|------|-------------|
| Host config | appsettings.json (file) |
| Seat presets | In-memory (lost on restart) |
| Seat runtime state | In-memory (lost on restart) |
| Provider config | sunshine.conf (file) |
| Provider state | sunshine_state.json (file) |

### Target State

| Data | Persistence |
|------|-------------|
| Host config | appsettings.json (file) |
| Seat presets | Disk (JSON file) |
| Seat runtime state | Disk (JSON file) |
| Provider config | sunshine.conf (file) |
| Provider state | sunshine_state.json (file) |

**DECISION**: Seat state persistence is P1 priority.

---

## Configuration Validation

### At Startup

1. Validate appsettings.json
2. Validate VibepolloExePath exists
3. Validate SudoVDA installed
4. Validate HidHide installed

### At Provisioning

1. Validate SeatRequest
2. Validate account exists
3. Validate port block available
4. Validate display driver ready

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| MultiSeatOptions reads appsettings.json | MultiSeatOptions.cs | FACT |
| VibepolloConfigBuilder generates sunshine.conf | VibepolloConfigBuilder.cs | FACT |
| SeatInfo is in-memory | ConcurrentDictionary | FACT |
| Secrets in DPAPI | Security implementations | FACT |
