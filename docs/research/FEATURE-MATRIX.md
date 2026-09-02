# MultiSeat-Extended: Матрица возможностей (Feature Matrix)

## Легенда

| Symbol | Значение |
|--------|----------|
| YES | Полностью реализовано |
| PARTIAL | Частично реализовано |
| NO | Не реализовано |
| UNKNOWN | Информации недостаточно |

---

## Windows Users & Sessions

| Capability | MultiSeat-Extended | MultiSeat upstream | Vibepollo | Duo | Helios | TermWrap | neo_multiseat | MultiseatProject |
|------------|-------------------|-------------------|-----------|-----|--------|----------|---------------|-----------------|
| Windows user creation | YES | YES | NO | YES | NO | NO | YES | UNKNOWN |
| User account management | YES | YES | NO | YES | NO | NO | YES | UNKNOWN |
| Session creation via RDP | YES | YES | NO | YES | NO | YES* | YES | UNKNOWN |
| Concurrent sessions | YES | YES | NO | YES | NO | YES | YES | UNKNOWN |
| Session monitoring | YES | YES | NO | YES | NO | NO | YES | UNKNOWN |
| Session reconnect | YES | YES | NO | YES | NO | NO | NO | UNKNOWN |
| Session disconnect handling | YES | YES | NO | YES | NO | NO | NO | UNKNOWN |
| N seats (N>2) | YES | YES | NO | PAID | NO | N/A | YES | UNKNOWN |
| N seats (unlimited) | YES | YES | N/A | PAID | N/A | N/A | YES | UNKNOWN |

*TermWrap provides the RDP patching that enables concurrent sessions.

---

## RDP / Terminal Services

| Capability | MultiSeat-Extended | MultiSeat upstream | Vibepollo | Duo | Helios | TermWrap | neo_multiseat |
|------------|-------------------|-------------------|-----------|-----|--------|----------|---------------|
| RDP loopback session creation | YES | YES | NO | YES | NO | NO | NO |
| RDP Wrapper integration | YES | YES | NO | YES | NO | YES | YES |
| termsrv.dll patching | NO (uses RDPWrap) | NO | NO | NO | NO | YES | YES |
| Dynamic offset discovery | NO | NO | NO | NO | NO | YES | NO |
| NLA management | YES | YES | NO | YES | NO | NO | YES |
| mstsc window management | YES | YES | NO | YES | NO | NO | NO |
| RDP file generation | YES | YES | NO | YES | NO | NO | YES |
| Credential management | YES | YES | NO | YES | NO | NO | NO |
| DWM frame interval | YES | YES | NO | UNKNOWN | NO | NO | NO |

---

## Virtual Display

| Capability | MultiSeat-Extended | MultiSeat upstream | Vibepollo | Duo | Helios | TermWrap |
|------------|-------------------|-------------------|-----------|-----|--------|----------|
| Virtual display driver | YES (SudoVDA) | YES (SudoVDA) | YES (own + SudoVDA) | YES (custom WDDM) | NO | NO |
| Display creation per seat | YES | YES | YES | YES | NO | NO |
| Display isolation | YES | YES | NO | YES | NO | NO |
| Resolution matching | YES | YES | YES | YES | NO | NO |
| Refresh rate control | YES | YES | YES | YES (up to 500Hz) | NO | NO |
| HDR support | PARTIAL (probe) | PARTIAL | YES | YES (paid) | NO | NO |
| Headless mode | YES | YES | YES | YES | NO | NO |
| Display layout restoration | YES | YES | YES | YES | NO | NO |
| Late display detection | YES | YES | NO | UNKNOWN | NO | NO |

---

## Encoder / Streaming

| Capability | MultiSeat-Extended | MultiSeat upstream | Vibepollo | Duo | Helios | TermWrap |
|------------|-------------------|-------------------|-----------|-----|--------|----------|
| NVENC support | YES | YES | YES | YES | NO | NO |
| AMF support | NO | NO | YES | YES | NO | NO |
| AV1 encoding | NO | NO | YES | YES | NO | NO |
| HEVC encoding | NO | NO | YES | YES | NO | NO |
| H.264 encoding | YES | YES | YES | YES | NO | NO |
| Software encoding fallback | NO | NO | YES | YES | NO | NO |
| Encoder probing | NO | NO | YES | YES | NO | NO |
| NVENC quality presets | YES | YES | YES | YES | NO | NO |
| Frame generation | NO | NO | YES (NVIDIA Smooth Motion) | UNKNOWN | NO | NO |
| Lossless Scaling integration | YES | YES | YES | UNKNOWN | NO | NO |

---

## Audio

| Capability | MultiSeat-Extended | MultiSeat upstream | Vibepollo | Duo | Helios | TermWrap |
|------------|-------------------|-------------------|-----------|-----|--------|----------|
| Audio isolation per seat | YES (PerSession) | YES (PerSession) | PARTIAL | YES | YES | NO |
| Virtual audio device | NO (not needed) | NO | YES | YES | YES | NO |
| Microphone passthrough | NO | NO | YES | UNKNOWN | NO | NO |
| Per-instance audio routing | YES | YES | YES | YES | YES | NO |
| Host audio protection | YES | YES | NO | YES | NO | NO |
| Audio crash recovery | YES | YES | YES | UNKNOWN | NO | NO |

---

## Input / Controller

| Capability | MultiSeat-Extended | MultiSeat upstream | Vibepollo | Duo | Helios | TermWrap |
|------------|-------------------|-------------------|-----------|-----|--------|----------|
| Keyboard/mouse session isolation | NO (no-op) | NO | NO | YES | NO | NO |
| Gamepad isolation (HidHide) | YES (optional) | YES (optional) | NO | YES | NO | NO |
| ViGEm virtual controller | YES (optional) | YES (optional) | NO | YES (custom) | NO | NO |
| XInput routing | YES | YES | NO | YES | NO | NO |
| Controller auto-assignment | YES | YES | NO | YES | NO | NO |
| Vibration feedback | YES | YES | NO | UNKNOWN | NO | NO |
| Native Moonlight controller | YES | YES | YES | YES | NO | NO |

---

## Game / Process Management

| Capability | MultiSeat-Extended | MultiSeat upstream | Vibepollo | Duo | Helios | TermWrap |
|------------|-------------------|-------------------|-----------|-----|--------|----------|
| Game launching per seat | YES | YES | NO | YES | NO | NO |
| Launch-on-connect | YES | YES | NO | YES | NO | NO |
| Process monitoring | PARTIAL | PARTIAL | NO | YES | NO | NO |
| Game mutex isolation | NO | NO | NO | YES | NO | NO |
| Steam multi-instance | NO | NO | NO | YES | NO | NO |
| Process patching | NO | NO | NO | YES | NO | NO |
| Shared game library | YES | YES | NO | YES | NO | NO |
| Emulator netplay | YES | YES | NO | NO | NO | NO |
| Playnite integration | YES | YES | YES | UNKNOWN | NO | NO |
| RTSS integration | YES | YES | YES | UNKNOWN | NO | NO |

---

## Service Management

| Capability | MultiSeat-Extended | MultiSeat upstream | Vibepollo | Duo | Helios | TermWrap |
|------------|-------------------|-------------------|-----------|-----|--------|----------|
| Windows Service mode | YES | YES | YES | YES | YES | NO |
| SYSTEM execution | YES | YES | YES | YES | YES | NO |
| Auto-start seats | YES | YES | NO | YES | NO | NO |
| Crash recovery | YES | YES | YES | YES | YES | NO |
| Health checks | YES | YES | NO | YES | NO | NO |
| Auto-restart | YES | YES | YES | YES | YES | NO |
| Service install/uninstall | YES | YES | YES | YES | YES | NO |

---

## API / IPC

| Capability | MultiSeat-Extended | MultiSeat upstream | Vibepollo | Duo | Helios | TermWrap |
|------------|-------------------|-------------------|-----------|-----|--------|----------|
| REST API | YES | YES | YES | YES | NO | NO |
| WebSocket | YES | YES | NO | YES | NO | NO |
| Named Pipes | NO | NO | NO | NO | YES | NO |
| Web dashboard | YES (React) | YES (React) | YES | YES (Web UI) | YES (WPF) | NO |
| API authentication | YES | YES | YES | YES | NO | NO |

---

## Security

| Capability | MultiSeat-Extended | MultiSeat upstream | Vibepollo | Duo | Helios | TermWrap |
|------------|-------------------|-------------------|-----------|-----|--------|----------|
| Credential encryption | YES (DPAPI) | YES (DPAPI) | NO | YES | NO | NO |
| Seat accounts as standard users | YES | YES | N/A | YES | N/A | N/A |
| ACL hardening | YES | YES | NO | YES | NO | NO |
| API key auth | YES | YES | YES | YES | NO | NO |
| HTTPS support | NO | NO | YES | YES | NO | NO |
| Network isolation | PARTIAL | PARTIAL | NO | YES | NO | NO |

---

## Diagnostics / Monitoring

| Capability | MultiSeat-Extended | MultiSeat upstream | Vibepollo | Duo | Helios | TermWrap |
|------------|-------------------|-------------------|-----------|-----|--------|----------|
| GPU monitoring | YES | YES | NO | YES | NO | NO |
| Metrics collection | YES | YES | NO | YES | NO | NO |
| Log management | YES | YES | YES | YES | YES | NO |
| Display enumeration | YES | YES | YES | YES | NO | NO |
| Audio diagnostics | YES | YES | NO | YES | NO | NO |
| HidHide diagnostics | YES | YES | NO | NO | NO | NO |
| Metrics export (Prometheus) | NO | NO | NO | NO | NO | NO |
