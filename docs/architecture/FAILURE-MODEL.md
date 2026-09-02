# Failure Model

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define failure scenarios, detection, recovery, and terminal states.

---

## Failure Categories

### 1. Provider Failures

| Failure | Detection | Recovery | Terminal |
|---------|-----------|----------|----------|
| Provider crash | Health check (5s) | Auto-restart | MaxRestartAttempts |
| Provider hang | Health check (5s) | Force terminate + restart | MaxRestartAttempts |
| Provider config error | Provisioning | Throw, Error state | Manual fix |

### 2. Session Failures

| Failure | Detection | Recovery | Terminal |
|---------|-----------|----------|----------|
| Session disconnect | Health check (5s) | Reconnect | Session logoff |
| mstsc crash | PID check | Re-launch mstsc | Session logoff |
| Session timeout | Health check (5s) | Reconnect | Session logoff |

### 3. Display Failures

| Failure | Detection | Recovery | Terminal |
|---------|-----------|----------|----------|
| Display not found | Health check (5s) | Late detection retry | Provider captures primary |
| Display lost | Health check (5s) | Re-detect display | Display not available |

### 4. Process Failures

| Failure | Detection | Recovery | Terminal |
|---------|-----------|----------|----------|
| Game crash | Process exit | Optional restart | Game stopped |
| Orphan process | WMI scan | Adopt or terminate | Process killed |
| Helper crash | Process exit | Re-run helper | Best-effort |

### 5. Infrastructure Failures

| Failure | Detection | Recovery | Terminal |
|---------|-----------|----------|----------|
| Service crash | Windows SCM | Auto-restart | Seats lost |
| Windows reboot | Service stop | Service restart | Seats lost |
| Driver failure | Driver crash | Reboot needed | Driver unavailable |

---

## Recovery Priority

| Priority | Failure | Recovery |
|----------|---------|----------|
| P0 | Provider crash | Auto-restart |
| P0 | Orphan process | Adopt or terminate |
| P1 | Game crash | Detection + optional restart |
| P1 | Session disconnect | Reconnect |
| P2 | Display lost | Late detection |
| P2 | Full failure | Re-provision |

---

## Backoff Policy

| Crash Count | Backoff | Reset |
|-------------|---------|-------|
| 1-2 | Immediate | Stable 30s |
| 3 | 30 seconds | Stable 30s |
| 4 | 60 seconds | Stable 30s |
| 5+ | 120 seconds | Stable 30s |

---

## Terminal States

| State | Meaning | Recovery |
|-------|---------|----------|
| Failed | Max attempts exhausted | Manual intervention |
| Error | Provisioning failed | Manual retry |
| Idle | Torn down cleanly | Re-provision |

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Provider crash detected by health check | SessionHealthCheck | FACT |
| Progressive backoff pattern | Helios ProcessManager | FACT |
| MaxRestartAttempts = 3 | Constants.cs | FACT |
| Service crash loses seats | In-memory state | FACT |
