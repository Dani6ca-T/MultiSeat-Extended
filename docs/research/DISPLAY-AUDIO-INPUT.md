# MultiSeat-Extended: Display, Audio, Input

## Display Subsystem

### Архитектура

```
VirtualDisplayManager
    ├── ResolutionNegotiator (проверка/валидация)
    ├── DisplayEnumeratorHelper (диагностика через console session)
    ├── AdvancedColorHelper (HDR probe)
    └── DisplayModeHelper (display isolation, refresh rate)
```

### Virtual Display (SudoVDA)

- **Драйвер**: SudoVDA (IddCx virtual display adapter)
- **Регистрация**: ROOT\DISPLAY\0000 (PnP)
- **Управление**: Vibepollo создаёт/уничтожает virtual monitors
- **MultiSeat**: только отслеживает через `output_name` (UUID)

### Display Creation Flow

```
SeatManager.ProvisionSeatAsync()
    → VirtualDisplayManager.CreateDisplayAsync(seat)
        → ResolutionNegotiator.Negotiate(width, height, fps)
        → seat.DisplayDevicePath = null  // Vibepollo manages internally
        → _displays[seat.Id] = new VirtualDisplay(...)
    
    → VibepolloManager.StartAsync(seat)
        → Vibepollo creates SudoVDA monitor when client connects
    
    → ParseSudoVdaDisplayId(logPath)
        → Find UUID in Vibepollo log
        → seat.DisplayDevicePath = UUID
        → VibepolloConfigBuilder.UpdateDisplayOutput(configPath, UUID)
    
    → ApplyDisplayIsolationAsync(seat)
        → --setup-display-isolation <UUID>
        → SudoVDA becomes session primary
        → RDP display shrunk to 640×480
        → --set-display-hz <fps>
```

### Display Isolation

```csharp
// SeatManager.ApplyDisplayIsolationAsync(seat)
// 1. Setup display isolation
_sessionLauncher.RunHelperInSeatSession(
    seat.SessionId, seat.AccountName,
    $"\"{helperExe}\" --setup-display-isolation \"{seat.DisplayDevicePath}\"");
// Result: SudoVDA is primary, RDP display shrunk to 640×480

// 2. Set refresh rate
_sessionLauncher.RunHelperInSeatSession(
    seat.SessionId, seat.AccountName,
    $"\"{helperExe}\" --set-display-hz {seat.Fps}");
// Result: SudoVDA refresh rate set to seat.Fps
```

### Resolution Management

```csharp
// SeatManager.SetResolutionAsync(seatId, width, height, ...)
// Resolution change requires new session:
_vibepolloManager.KillForReconnect(seat);
_sessionLauncher.DisconnectSession(seat.SessionId);
seat.SessionId = await _sessionLauncher.LaunchSessionAsync(seat.AccountName, ct, geometry);
_configBuilder.BuildConfig(seat, _options.VibepolloConfigDir);
seat.VibepolloProcessId = await _vibepolloManager.StartAsync(seat, ct);
```

### Display Diagnostics

```csharp
// VirtualDisplayManager.EnumerateAllConnectedPaths()
// Runs --enum-displays helper in console session
// Returns: GdiName, FriendlyName, DevicePath, Active, TargetAvailable

// AdvancedColorHelper — HDR probe
// Reports HDR-capable vs active per display target
// Can enable/disable Advanced Color
```

### Каждый seat может иметь независимый display?

**Да.** Каждый seat получает собственный SudoVDA virtual monitor через Vibepollo. MultiSeat трекает UUID каждого через `seat.DisplayDevicePath`. Vibepollo создаёт virtual monitor при подключении клиента и управляет его жизненным циклом.

---

## Audio Subsystem

### Режим: PerSession (единственный поддерживаемый)

```
┌─────────────────────────────────────────────┐
│ Console Session                             │
│   ├── Host audio output                     │
│   └── mstsc (hidden) ← RDP loopback        │
│       └── Seat Session                      │
│           ├── "Remote Audio" endpoint       │
│           │   (Windows created, per-session)│
│           ├── Game renders audio here       │
│           └── Vibepollo loopback-captures   │
│               (from inside session)         │
└─────────────────────────────────────────────┘
```

### Аудио Route

1. Windows создаёт "Remote Audio" endpoint в каждой RDP session
2. Game рендерит аудио в этот endpoint (session default)
3. Vibepollo loopback-captures изнутри session
4. mstsc (console side) muted → аудио не попадает на host speakers

### Audio Configuration

```ini
# В sunshine.conf — НЕТ audio_sink или virtual_sink
# Vibepollo captures session's own Remote Audio endpoint
keep_sink_default = disabled
auto_capture_sink = disabled
stream_mic = disabled
```

### Microphone

**Недоступен.** Seat session не видит host's Steam Streaming Microphone. Standard Moonlight не отправляет mic packets. Альтернативы (logabell/moonlight-qt-mic) не видны изнутри session.

### Может ли Seat A использовать один audio endpoint, а Seat B другой?

**Да.** Каждая RDP session имеет свой собственный "Remote Audio" endpoint. Windows автоматически делает его session default. Vibepollo запущенный внутри session loopback-captures именно этот endpoint. Полная изоляция между seats.

### Cleanup

- Нет device assignment → нет cleanup
- Per-session audio owns its own endpoint
- ResetAudio() — no-op в PerSession mode

---

## Input Subsystem

### Архитектура

```
Physical Device
    ↓
Input Processing
    ↓
Seat
    ↓
Application
```

### Компоненты

```
Input
├── InputRouter          # XInput → ViGEm bridge (physical → virtual)
├── ControllerManager    # ViGEm virtual Xbox 360 controllers
├── InputHookManager     # KB/M session isolation (currently no-op)
├── HidHideConfigurator  # Per-seat gamepad isolation
├── HidHideCli           # CLI wrapper for HidHide
├── HidHideSessionJail   # Session jail rules
└── HidHideDevice        # Device model
```

### Controller Flow (Default: Native)

```
Moonlight Client
    ↓ Moonlight protocol
Vibepollo (in seat session)
    ↓ Creates virtual Xbox 360 controller (ViGEm)
Game in seat session
    ↓ Reads controller
```

- `EnableViGEmController = false` (default)
- Vibepollo forwards Moonlight client's controller natively
- No MultiSeat-managed ViGEm pad
- Dashboard shows "Native" controller

### Controller Flow (Optional: ViGEm)

```
Physical XInput Controller (host)
    ↓ XInput.GetState() polling (~1ms)
InputRouter
    ↓ ForwardToSeat()
ControllerManager
    ↓ SubmitState() → ViGEm virtual controller
Seat Session
    ↓ Game reads virtual controller
```

- `EnableViGEmController = true` (opt-in)
- MultiSeat creates ViGEm virtual Xbox 360 per seat
- InputRouter polls physical XInput at ~1ms
- Physical state → Xbox360Report → ViGEm
- Vibration feedback flows backwards

### Input Isolation

#### Keyboard/Mouse (InputHookManager)

```csharp
// Currently a NO-OP
// WH_KEYBOARD_LL/WH_MOUSE_LL hooks run in SYSTEM service (Session 0)
// GetForegroundWindow() returns NULL → ShouldPassThrough() always passes
// No cross-session K/M bleed anyway (physical → console, Moonlight → seat)
```

#### Gamepad Isolation (HidHide Session Jail)

```csharp
// EnableHidHideCloaking = false (default)
// Hides gamepad to only one session via undocumented !<sessionId> suffix
// HidHide >= 1.4.181.0, Logic.c:817

// EnablePadRulePreWrite = false (default)
// Write rules BEFORE Vibepollo creates pad (HidHide filters at OPEN time)

// SeatPadDevicePaths = {} (identity leg of attribution)
// Known pad device instance paths per seat account
```

### Поток физического ввода

```
Physical Keyboard/Mouse
    ↓ WH_KEYBOARD_LL/WH_MOUSE_LL (Session 0)
    ↓ GetForegroundWindow() = NULL → pass through
    ↓
Console Session (host desktop)

Moonlight Client
    ↓ Moonlight protocol
Vibepollo
    ↓ SendInput() inside seat session
Game in seat session

Physical Gamepad
    ↓ XInput.GetState()
InputRouter (if EnableViGEmController)
    ↓ ForwardToSeat()
ControllerManager → ViGEm
    ↓
Game in seat session
```

### HidHide CLI Traps

```csharp
// HidHideCli handles 5 traps:
// 1. Never gives tool stdout/stderr (hangs — redirect through cmd.exe to file)
// 2. Enforces ~800ms gap with fresh transcript per call + retry
// 3. Treats empty read as failure (not empty config)
// 4. Retries 0x0005 Access denied (single-caller control device)
// 5. Puts value directly after each switch
// Reads carry --cancel (without it, listing saves over config)
```

### ViGEm Controller Ownership

```csharp
// HidHide device ownership is DERIVED, never named
// XUSB node parent: ROOT\... = emulated, USB\... = physical
// HID node parent looks physical even for emulated pad
// Attribution: identity (SeatPadDevicePaths) → elimination
// Elimination refused when >1 unconfined emulated pad exists
```
