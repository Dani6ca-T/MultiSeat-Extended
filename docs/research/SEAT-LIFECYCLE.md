# MultiSeat-Extended: Жизненный цикл Seat

## Обзор

Seat — это изолированная Windows сессия с полным стеком: аккаунт, сессия, дисплей, аудио, streaming server, контроллер. SeatManager оркестрирует весь lifecycle.

## Lifecycle States

```
┌─────────┐
│  Idle   │ ← SeatInfo создана, ресурсы не выделены
└────┬────┘
     │ ProvisionSeatAsync()
     ▼
┌──────────────┐
│ Provisioning │ ← Аккаунт проверен, порты выделены
└────┬─────────┘
     │
     ▼
┌──────────────┐
│ Configuring  │ ← Session создана, дисплей настраивается
└────┬─────────┘
     │
     ▼
┌────────┐
│ Ready  │ ← Все компоненты готовы, ждёт Moonlight клиент
└────┬───┘
     │ Moonlight клиент подключается
     ▼
┌────────────┐
│ Streaming  │ ← Клиент активно стримит
└────┬───────┘
     │ Клиент отключается
     ▼
┌────────┐
│ Ready  │ ← Возврат в готовность
└────────┘
     │ TeardownSeatAsync()
     ▼
┌──────────────┐
│ TearingDown  │ ← Ресурсы освобождаются (best-effort)
└────┬─────────┘
     │
     ▼
┌────────┐
│  Idle  │ ← Все ресурсы освобождены
└────────┘

Аварийные переходы:
  Provisioning → Error → TearingDown → Idle
  Streaming → Error → TearingDown → Idle
```

## Provisioning Pipeline (порядок важен!)

`SeatManager.ProvisionSeatAsync(SeatRequest)` выполняет 9 шагов:

### Step 1: Validate + Allocate Ports

```csharp
// Проверка лимита
if (ActiveSeatCount >= _options.MaxSeats)
    throw new InvalidOperationException(...);

// Проверка аккаунта
if (!_accounts.AccountExists(request.AccountName))
    throw new InvalidOperationException(...);

// Корректировка групп аккаунта
_accounts.ApplySeatGroupMembership(request.AccountName);

// Выделение портов (30 портов на seat)
seat.PortBase = _portAllocator.Allocate();
// Seat 0 → 48100-48129, Seat 1 → 48130-48159, etc.
```

### Step 1.5: Assign Emulator Netplay Port

```csharp
if (_options.EnableEmulatorNetplay)
{
    seat.RetroArchNetplayPort = seat.PortBase + Constants.OffsetRetroArchNetplay;
    // Seat 0 → 48113, Seat 1 → 48143, etc.
}
```

### Step 2: Launch Windows Session

```csharp
seat.SessionId = await _sessionLauncher.LaunchSessionAsync(
    seat.AccountName, ct, 
    RdpGeometry.ForClient(seat.Width, seat.Height));
```

**Что происходит внутри:**
1. Получение credentials из AccountManager
2. Проверка существующей сессии (Active/Disconnected)
3. Если Disconnected → ReconnectSessionAsync
4. Если нет сессии → CreateSessionViaRdpLoopbackAsync:
   - Store RDP credential (CredWrite)
   - Start window hider (mstsc window)
   - Write Default.rdp (RdpFileBuilder)
   - Launch mstsc.exe в console session → 127.0.0.2
   - Poll WTS пока сессия не появится
   - Launch keepalive process
   - Save mstsc process in _pendingMstsc
   - Cleanup credentials

### Step 2.5: Suppress RustDesk Audio

```csharp
// Запись конфигурации RustDesk с отключённым аудио
var rustDeskConfig = Path.Combine(
    @"C:\Users", seat.AccountName,
    @"AppData\Roaming\RustDesk\config");
File.WriteAllTextAsync(rustDeskConfig, "[options]\nenable-audio = \"N\"\n");

// Убийство RustDesk процессов в сессии
foreach (var p in Process.GetProcessesByName("RustDesk"))
    if (p.SessionId == seat.SessionId) p.Kill();
```

### Step 2.7: Pre-write Gamepad Jail Rules

```csharp
// HidHide фильтрует при OPEN time, поэтому правила нужно написать ДО Vibepollo
_hidHide.PreWriteRules(seat);
```

### Step 3: Virtual Display

```csharp
await _displayManager.CreateDisplayAsync(seat, ct);
// ResolutionNegotiator.Negotiate() проверяет/warning
// Vibepollo будет создавать виртуальный дисплей при подключении клиента
```

### Step 4: Firewall Ports

```csharp
await _firewall.OpenPortsAsync(seat, ct);
// Открывает порты для Vibepollo (HTTPS, HTTP, Video, Audio, Control, RTSP)
```

### Step 5: Audio Routing (PerSession)

```csharp
// PerSession: нет VAC, нет IPolicyConfig
// Каждая сессия имеет свой "Remote Audio" endpoint
// Vibepollo loopback-captures его изнутри сессии
seat.ProvisioningStep = "Audio";
// No AudioCaptureDeviceId, no --set-default-render
```

### Step 5.7: Seed Emulator Configs

```csharp
foreach (var seeder in _emulatorSeeders)
{
    if (!seeder.IsEnabled) continue;
    await seeder.SeedAsync(seat, ct);
    // RetroArch: netplay port, shared ROM dir
}
```

### Step 6: Start Vibepollo

```csharp
seat.VibepolloProcessId = await _vibepolloManager.StartAsync(seat, ct);
// 1. VibepolloConfigBuilder.BuildConfig() → sunshine.conf
// 2. ProcessInjector.LaunchVibepolloInSessionAsync() → CreateProcessAsUser
```

**Что происходит внутри VibepolloManager.StartAsync:**
1. Generate per-seat sunshine.conf
2. Launch Vibepollo inside seat's Windows session
3. Track VibepolloInstance (PID, config, session)

### Step 6.5: Discover SudoVDA UUID

```csharp
// Ожидание 5 секунд для инициализации Vibepollo
await Task.Delay(5000, ct);

// Парсинг лога Vibepollo для поиска SudoVDA display
var displayId = _vibepolloManager.ParseSudoVdaDisplayId(logPath);

if (displayId != null)
{
    seat.DisplayDevicePath = displayId;
    _configBuilder.UpdateDisplayOutput(configPath, displayId);
    
    // Restart Vibepollo с правильным output_name
    _vibepolloManager.Stop(seat);
    await Task.Delay(2000, ct);
    seat.VibepolloProcessId = await _vibepolloManager.StartAsync(seat, ct);
    
    // Apply display isolation
    await ApplyDisplayIsolationAsync(seat, ct);
}
```

### Step 7: Controller + Input Routing

```csharp
if (_options.EnableViGEmController)
{
    seat.ViGEmControllerIndex = _controllerManager.CreateController(seat);
    
    if (_options.AutoAssignControllers)
    {
        var connected = _inputRouter.GetConnectedControllers();
        var assigned = _inputRouter.GetAssignments();
        var freeIdx = connected.FirstOrDefault(idx => !assigned.ContainsKey(idx), -1);
        if (freeIdx >= 0)
            _inputRouter.AssignController(freeIdx, seat.Id);
    }
}
```

### Step 8: HidHide + Input Hooks

```csharp
_hidHide.CloakForSession(seat);
_inputHookManager.InstallForSession((uint)seat.SessionId);
```

### Step 9: Ready

```csharp
seat.Status = SeatStatus.Ready;
seat.ReadyAt = DateTimeOffset.UtcNow;
seat.ProvisioningStep = null;
await BroadcastState(seat);
```

## Display Isolation

`ApplyDisplayIsolationAsync` выполняется после обнаружения SudoVDA:

1. **Setup display isolation** — `--setup-display-isolation <sudovda-iddcx-path>`
   - SudoVDA becomes session primary
   - RDP display shrunk to 640×480
   - Снижает TermService CPU с ~70% до <5%

2. **Set refresh rate** — `--set-display-hz <fps>`
   - Ограничивает SudoVDA refresh rate до seat.Fps

## Teardown Pipeline

`TeardownSeatInternalAsync` выполняет в обратном порядке:

```csharp
// Reverse order — каждый шаг best-effort
try { _onConnectApps.Forget(seat.Id); } catch { }
try { _inputHookManager.Uninstall(); } catch { }
try { _hidHide.UncloakForSession(seat); } catch { }
try { UnassignControllersForSeat(seat.Id); } catch { }
try { _controllerManager.DestroyController(seat); } catch { }
try { _vibepolloManager.Stop(seat); } catch { }
try { await _firewall.ClosePortsAsync(seat, ct); } catch { }
try { await _displayManager.DestroyDisplayAsync(seat, ct); } catch { }
try { _sessionLauncher.DisconnectSession(seat.SessionId); } catch { }
try { _sessionLauncher.LogoffSession(seat.SessionId); } catch { }
try { _portAllocator.Release(seat.PortBase); } catch { }
try { _configBuilder.CleanupConfig(seat.AccountName, ...); } catch { }
```

## Health Check Loop

`SessionHealthCheck.CheckSeatAsync` проверяет каждые 5 секунд:

1. **Session alive?** — `SessionLauncher.IsSessionAlive()`
   - Если нет → SeatStatus.Error
   - Если Disconnected → ReconnectSessionAsync + Restart Vibepollo

2. **Vibepollo alive?** — `Process.GetProcessById()`
   - Если нет → `VibepolloManager.RestartAsync()` (max 3 попытки)
   - Если restart failed → SeatStatus.Error

3. **Launch-on-connect** — `OnConnectAppLauncher.ProcessSeat()`
   - Tail Vibepollo log for CLIENT CONNECTED/DISCONNECTED
   - Launch/kill configured apps

4. **Resolution following** — `ClientResolutionFollower.ProcessSeatAsync()`
   - If FollowClientResolution enabled → reconnect session at new size

5. **Late display detection** — `SeatManager.TryLateDisplayDetectionAsync()`
   - Re-parse Vibepollo log for SudoVDA display
   - Apply display isolation if found

## Key Lifecycle Behaviors

### Session Persistence
- Windows Session ID сохраняется при reconnect (mstsc reconnects to same session)
- Anything running in the seat survives reconnect
- Resolution change requires new session (mstsc sets geometry at connect)

### Vibepollo Restart Behavior
- KillForReconnect resets RestartCount (sleep is not a crash)
- RestartAsync increments RestartCount (max 3 before giving up)
- Each restart re-applies display isolation

### Port Management
- PortAllocator uses bitmap for O(1) allocate/release
- PortsPerSeat = 30 (covers all Vibepollo offsets)
- MaxSeats architectural ceiling = 8 (Constants.MaxSeats)
- Operator limit = MultiSeatOptions.MaxSeats (default 4)
