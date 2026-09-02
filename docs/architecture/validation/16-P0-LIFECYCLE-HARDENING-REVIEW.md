# P0 Process Lifecycle — Hardening Review

**Date**: 2026-08-31
**Reviewer**: Buffy (adversarial)
**Status**: REVIEW ONLY — no code changes

---

## Executive Summary

P0-1 (Process Ownership), P0-2 (Job Objects), and P0-3 (Process Lifecycle)建立了正确的三层 process ownership/cleanup/monitoring 架构。核心设计决策（ProcessIdentity 保护 PID reuse, Job Object 作为 safety net, event-driven monitoring）是合理的。

但发现 **1 个 HIGH** 和 **3 个 MEDIUM** 级别的问题，以及若干 LOW/INFORMATIONAL 发现。

最关键的发现：

1. **MEDIUM: `Process.Exited` event 未被任何 consumer 订阅** — infrastructure 已建好但没有连接到 recovery logic
2. **MEDIUM: `SessionHealthCheck` 与 `IProcessMonitor` 并存但不通信** — 两套独立的 process liveness 检测机制
3. **MEDIUM: `WindowsProcessGroup._disposed` 非 volatile** — 存在 theoretical visibility issue
4. **LOW: `StartupOrphanDetector._processTracker` 注入但从未使用** — dead code

**没有发现 BLOCKING 问题。** 所有核心 guarantee（PID reuse protection, Job Object KILL_ON_JOB_CLOSE, expected vs unexpected exit）在 source level 是正确的。

---

## Scope

审查范围：

- `ProcessIdentity` (PID + StartedAt)
- `WindowsProcessTracker` (ConcurrentDictionary)
- `WindowsProcessGroup` / `WindowsProcessGroupManager` (Job Object)
- `SafeJobHandle` / `Kernel32` P/Invoke
- `WindowsProcessMonitor` (Process.Exited)
- `StartupOrphanDetector` (WMI scan)
- `VibepolloManager` integration (Start/Stop/KillForReconnect/Restart)
- `SeatManager` teardown ordering
- `SessionHealthCheck` recovery flow
- Test suite (299 tests)

---

## P0-1 Review: Process Identity & Tracker

### ProcessIdentity — PID Reuse Protection

**Source**: `src/MultiSeat.Shared/Models/ProcessIdentity.cs`

**Verdict: CORRECT with caveat**

`ProcessIdentity = PID + StartedAt` 是正确的 PID reuse 防护机制。`record struct` 的 equality 基于两个字段的值，所以 `ConcurrentDictionary<ProcessIdentity, ...>` 自然支持 PID reuse：旧进程的 key `(PID=X, T=old)` 与新进程的 key `(PID=X, T=new)` 不会碰撞。

**CAVEAT: StartedAt 精度**

`ProcessIdentity.StartedAt` 来自 `Process.GetProcessById(pid).StartTime.ToUniversalTime()`。`DateTimeOffset` 精度为 ~100ns (tick), 但 `Process.StartTime` 底层调用 `NtQueryInformationProcess` 返回的 `CreateTime` 精度为 ~15ms (Windows system timer resolution)。

两个独立进程如果在 15ms 内启动，它们的 `StartTime` 可能相同。这不会导致 false positive（不同 PID 不会碰撞），但理论上两个不同进程实例可能有相同的 `(PID, StartedAt)` — 这只有在 PID reuse 发生得极快（<15ms）时才可能。

**实际风险: 极低。** PID reuse 通常间隔远大于 15ms。

### WindowsProcessTracker — Thread Safety

**Source**: `src/MultiSeat.Service/ProcessTracking/WindowsProcessTracker.cs`

**FINDING [LOW]: ConcurrentBag 与 Unregister 的 inconsistency**

`_bySeat` 使用 `ConcurrentBag<ProcessIdentity>`。`Register` 向 bag 添加 identity, 但 `Unregister` 只从 `_processes` 移除，不从 `_bySeat` 移除：

```csharp
// Unregister (line ~68):
public void Unregister(ProcessIdentity identity)
{
    _processes.TryRemove(identity, out _);
    // NOTE: _bySeat NOT updated
}
```

这意味着 `GetByOwner` 可能返回已 unregistered 的 process（通过 `_bySeat` 找到 identity, 但 `_processes.GetValueOrDefault` 返回 null, 被 `.Where(p => p is not null)` 过滤掉）。

**功能影响: 无。** 返回结果是正确的（不会返回 null entry）。但 `_bySeat` 会缓慢增长（stale identities 积累）。

**实际风险: LOW。** In-memory leak, 数量有限（每 seat 最多几十个 processes）。

### CONTRACT VIOLATION

**Source**: `src/MultiSeat.Shared/IProcessTracker.cs` line ~27

Documentation 声明：

> INVARIANT-2 enforcement: If a process with the same PID+StartedAt is already registered for a different seat, this call throws.

但 `WindowsProcessTracker.Register` 实现：

```csharp
public void Register(ProcessIdentity identity, Guid ownerSeatId, ManagedProcessType processType)
{
    var process = new ManagedProcess { ... };
    _processes[identity] = process;  // silently overwrites
    var seatBag = _bySeat.GetOrAdd(ownerSeatId, _ => new ConcurrentBag<ProcessIdentity>());
    seatBag.Add(identity);
}
```

**不检查是否已为不同 seat 注册。** 如果同一 `(PID, StartedAt)` 被两个不同 seat 注册，后者静默覆盖前者。

**Severity: LOW。** 在当前使用模式下（每个 seat 独立 PID），cross-seat 同一 identity 注册不会发生。但 invariant 被违反。

---

## P0-2 Review: Job Object

### Native API Correctness

**Source**: `src/MultiSeat.Service/Interop/Kernel32.cs` + `WindowsProcessGroup.cs`

**Verdict: CORRECT**

`CreateJobObjectW` → `SetInformationJobObject(JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE)` → `SafeJobHandle` → `CloseHandle` — 链路正确。

**Struct layout verification:**

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct JobObjectExtendedLimitInformation
{
    public JobObjectBasicLimitInformation BasicLimitInformation;  // offset 0
    public IoCounters IoInfo;                                      // offset 56 (x64)
    public IntPtr ProcessMemoryLimit;                             // offset 104
    public IntPtr JobMemoryLimit;                                 // offset 112
    public IntPtr PeakProcessMemoryUsed;                          // offset 120
    public IntPtr PeakJobMemoryUsed;                              // offset 128
}
```

`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000` — 正确。

### KILL_ON_JOB_CLOSE Guarantee

**Source**: Windows SDK documentation

> When the last handle to a job object is closed, all processes in the job are terminated.

**验证:** `SafeJobHandle` 在 `WindowsProcessGroup.Dispose()` 时调用 `ReleaseHandle()` → `CloseHandle()`。当最后一个 `WindowsProcessGroup` 被 dispose, Windows 终止所有 assigned processes。

**但这有前提条件:**

1. 进程必须成功 assign 到 Job Object
2. 进程不能使用 `CREATE_BREAKAWAY_FROM_JOB`
3. 进程的父 Job Object 不能阻止 breakaway

对于 Vibepollo (Sunshine fork), 这些条件在默认配置下满足。但 **不是 guaranteed** — 取决于 Vibepollo 的行为。

### FINDING [MEDIUM]: `_disposed` 非 volatile

**Source**: `src/MultiSeat.Service/ProcessTracking/WindowsProcessGroup.cs` line ~22

```csharp
private bool _disposed;  // NOT volatile
```

`Dispose()` 用 `_disposed` 做 idempotency check:

```csharp
public void Dispose()
{
    if (_disposed) return;
    _disposed = true;
    _jobHandle.Dispose();
}
```

在 x86/x64 memory model 下，一个线程写入 `_disposed = true` 不保证立即对另一个线程可见。理论上两个线程可以同时通过 `if (_disposed) return` 检查。

**实际影响:** `SafeJobHandle.ReleaseHandle()` 是 reference-counted — double-close 是安全的。所以这不是 crash bug, 但是一个 code smell。

**修复建议:** 使用 `Interlocked.Exchange(ref _disposed, 1) == 1` 或标记为 `volatile`。

### FINDING [LOW]: `AssignProcessToJobObject` 后进程 handle 泄漏？

**Source**: `WindowsProcessGroup.AssignProcess()`

```csharp
var processHandle = Kernel32.OpenProcess(access, false, (uint)processId);
// ...
try
{
    if (!Kernel32.AssignProcessToJobObject(_jobHandle, processHandle))
    {
        // ...
    }
}
finally
{
    Kernel32.CloseHandle(processHandle);  // ✓ handle 正确关闭
}
```

**VERDICT: 正确。** `finally` block 确保 handle 关闭。

---

## P0-3 Review: Process Monitor

### Event-Driven Monitoring

**Source**: `src/MultiSeat.Service/ProcessTracking/WindowsProcessMonitor.cs`

**FINDING [MEDIUM]: `ProcessExited` event 无 consumer**

```csharp
public event EventHandler<ProcessExitInfo>? ProcessExited;
```

这个 event 在 `OnProcessExited` 中被 invoke，但 **没有代码订阅它**。`SessionHealthCheck` 不使用它。`VibepolloManager` 不使用它。

当前 recovery 仍然完全依赖 `SessionHealthCheck` 的 5 秒 polling。

**这意味着 P0-3 的 event-driven monitoring 实际上是 dead infrastructure** — 监控运行着，event 触发着，但没人听。

### FINDING [MEDIUM]: 双重 liveness 检测

`SessionHealthCheck.CheckSeatAsync()` (line ~118):

```csharp
var vibepolloAlive = IsProcessAlive(seat.VibepolloProcessId);
```

其中 `IsProcessAlive`:

```csharp
private static bool IsProcessAlive(int pid)
{
    if (pid <= 0) return false;
    try
    {
        using var proc = Process.GetProcessById(pid);
        return !proc.HasExited;
    }
    catch (ArgumentException) { return false; }
}
```

同时 `WindowsProcessMonitor` 也通过 `Process.Exited` 检测 process liveness。

**两套独立的检测机制同时运行，互不知情。**

当前不会导致 double recovery（因为 `ProcessExited` 无人订阅），但一旦连接 consumer，必须处理 double recovery。

### FINDING [LOW]: Process.Exited 与 HasExited 竞争

**Source**: `WindowsProcessMonitor.StartMonitoring()` line ~75-85

```csharp
process = Process.GetProcessById(identity.ProcessId);
if (process.HasExited)  // ← check
{
    process.Dispose();
    return;
}
// ... 几行代码 ...
process.EnableRaisingEvents = true;  // ← enable
process.Exited += OnProcessExited;   // ← subscribe
```

如果进程在 `HasExited` 检查之后、`Exited +=` 之前退出，`Process.Exited` 可能不会触发（取决于 Windows 内部时序）。

**但这是一个非常窄的窗口**（纳秒级），且在该窗口内进程退出的概率极低。如果发生，monitoring entry 会残留在 `_entries` 中直到 `Dispose` 或 `StopMonitoring` 清理。

### FINDING [MEDIUM-LOW]: OnProcessExited 中 entry disposal 顺序

**Source**: `WindowsProcessMonitor.OnProcessExited()` line ~150-170

```csharp
// Clean up the entry
_entries.TryRemove(matchedIdentity, out _);
matchedEntry.Dispose();  // Process disposed here

var exitInfo = new ProcessExitInfo
{
    // ... matchedEntry still referenced here
    OwnerSeatId = matchedEntry.OwnerSeatId,  // ← OK (class, not struct)
    ProcessType = matchedEntry.ProcessType,   // ← OK
    // ...
};

ProcessExited?.Invoke(this, exitInfo);
```

`matchedEntry` 是 class (reference type)，所以 `Dispose()` 后仍可访问其属性。**不是 bug**, 但依赖于 `MonitoringEntry.Dispose()` 只 dispose `Process` 而不 nullify properties。

### Race: StopMonitoring 与 OnProcessExited

**Scenario:**

```
Thread A: StopMonitoring(identity)
  → TryRemove(identity) → succeeds, gets entry → Dispose

Thread B: OnProcessExited
  → finds matching entry → TryRemove(identity) → gets null (already removed by A)
  → matchedEntry is null → returns early
```

**VERDICT: SAFE.** `ConcurrentDictionary.TryRemove` 是 atomic — 只有一个线程能得到 entry。

但如果时序相反：

```
Thread B: OnProcessExited
  → finds matching entry → TryRemove → succeeds → Dispose
  → fires ProcessExited event

Thread A: StopMonitoring(identity)
  → TryRemove(identity) → gets null (already removed by B)
  → returns (no-op)
```

**VERDICT: SAFE.** StopMonitoring 是 no-op。ProcessExited event 已经触发。

---

## Race Conditions

### RACE-1: MarkExpectedExit 与进程 crash

```
MarkExpectedExit(identity)
    ↓ (flag set)
process crashes (unexpected)
    ↓
OnProcessExited fires
    → wasExpected = true (WRONG — crash was unexpected)
```

**分析:** 这是一个 known race，documentation 已说明：

> If the process already exited (race), the flag is set but the exit event may already have fired with WasExpected=false

但实际时序是相反的：flag 在 crash 之前设置，所以 event 会看到 `WasExpected=true`。

**实际影响:** 如果 crash 恰好在 `MarkExpectedExit` 之后、`Kill()` 之前发生，exit event 会标记为 expected，不会触发 recovery。但此时 `Kill()` 会 throw (process already exited), 会被 catch 处理。**不会导致 stuck state** — health check 会在下一个 tick 发现 process dead 并触发 recovery。

**Severity: LOW** (window is very narrow, health check is backup)

### RACE-2: RestartAsync 与 HealthCheck 并发

```
HealthCheck.CheckSeatAsync(seat)
    → IsProcessAlive(pid) returns false
    → calls RestartAsync(seat, ct)

同时:
Process.Exited 无人订阅，不参与
```

**当前无 double recovery risk** — 只有 `HealthCheck` 一条路径。

但 `CheckAllSeatsAsync` 是顺序遍历 seats（不是 parallel），所以同一 seat 不会被并发 restart。

**VERDICT: SAFE** (当前架构下)

### RACE-3: Teardown 与 Start 并发

```
Thread A: TeardownSeatAsync(seatA)
  → TryRemove from _seats
  → Stop → Job Dispose

Thread B: ProvisionSeatAsync (new seat)
  → TryAdd to _seats (different seatId)
```

**VERDICT: SAFE** — 不同 seat 独立操作。

但如果 Thread B 尝试 restart 同一 seat：

```
Thread A: TeardownSeatAsync(seatA)
  → _seats.TryRemove(seatA.id)
  → ... subsystem teardown ...

Thread B: RestartVibepolloAsync(seatA.id)
  → GetSeat(seatA.id) → null (already removed)
  → throws "Seat not found"
```

**VERDICT: SAFE** — TryRemove 之后的 seat 不可被其他线程访问。

---

## Failure Scenarios

### Service Crash

```
MultiSeat.Service crashes
  → Windows terminates service process
  → All handles closed (SafeJobHandle → CloseHandle)
  → KILL_ON_JOB_CLOSE triggers
  → All assigned provider processes terminated
  → Tracker state lost (in-memory)
  → Monitor state lost (in-memory)
  → Service restarts (Windows SCM)
  → KillOrphanedVibepolloProcesses cleans up any survivors
  → StartupOrphanDetector logs remaining orphans
  → Fresh state
```

**VERDICT: Service crash cleanup is reliable** (assuming Job Object assignment succeeded).

### Provider Crash (Normal)

```
Vibepollo crashes
  → Process.Exited fires (无人订阅)
  → 5s later, HealthCheck detects IsProcessAlive false
  → RestartAsync called
  → MarkAndStopMonitoring (no-op, already dead)
  → UnregisterFromTracker
  → Launch new instance
  → Register + StartMonitoring + AssignProcess
```

**VERDICT: Recovery works.** 5-second delay is acceptable.

### Provider Crash During Teardown

```
TeardownSeatAsync
  → VibepolloManager.Stop(seat)
    → MarkAndStopMonitoring
    → Process.Kill(entireProcessTree)
    → Process already crashed
    → catch ArgumentException (OK)
  → Job Dispose (safety net, no processes to kill)
```

**VERDICT: SAFE.**

---

## Test Quality

### Real Guarantees vs Unit Test Assumptions

| Test | What It Actually Verifies | Confidence Level |
|------|--------------------------|-----------------|
| `KillOnClose_TerminatesAssignedProcess` | Fallback path (manual kill if assignment fails) | LOW — doesn't verify KILL_ON_JOB_CLOSE |
| `IsAlive_DetectsPidReuse_DifferentStartTime` | Current process with wrong start time | HIGH — real OS call |
| `IsAlive_ReturnsTrue_ForCurrentProcess` | Current process is alive | HIGH — real OS call |
| `StartMonitoring_StoppedProcess_DoesNotMonitor` | Already-exited process not monitored | HIGH — real OS call |
| All ConcurrentDictionary tests | In-memory dictionary behavior | HIGH — but doesn't prove thread safety under load |
| `Concurrent_StartStop_DoesNotThrow` | No exception during concurrent access | HIGH — real concurrent operations |

### Untested Scenarios

| Scenario | Risk | Status |
|----------|------|--------|
| KILL_ON_JOB_CLOSE actually kills process | HIGH | **UNTESTED** (requires non-job test runner) |
| Process exits between HasExited and Exited subscription | LOW | **UNTESTED** (timing-dependent) |
| Late Exited event after StopMonitoring | MEDIUM | **UNTESTED** |
| Double recovery (HealthCheck + ProcessExited) | MEDIUM | **UNTESTED** (ProcessExited not wired) |
| Service crash → Job cleanup | HIGH | **UNTESTED** (requires service restart) |
| PID reuse with real OS processes | MEDIUM | **UNTESTED** (requires controlled PID reuse) |
| Concurrent RestartAsync + Stop | HIGH | **UNTESTED** |
| MarkExpectedExit race with crash | LOW | **UNTESTED** |

### Test Environment Limitation

所有 Job Object 测试都在 test runner 内运行，而 test runner 本身在一个 Windows Job Object 中。这导致 `AssignProcessToJobObject` 对 test-spawned processes 返回 `ERROR_ACCESS_DENIED`。测试 graceful 处理了这个情况，但没有验证 production-level KILL_ON_JOB_CLOSE 行为。

---

## Documentation Consistency

| Document | Claim | Source Reality | Status |
|----------|-------|---------------|--------|
| `P0-3-PROCESS-LIFECYCLE.md` | "Event-driven process exit detection" | ProcessExited fires but nobody listens | **OVERSTATED** |
| `P0-3-PROCESS-LIFECYCLE.md` | "Recovery: Yes (SessionHealthCheck)" | HealthCheck does polling, not event-driven | **MISLEADING** |
| `P0-3-PROCESS-LIFECYCLE.md` | "16 new tests" | Correct | **ACCURATE** |
| `P0-2-JOB-OBJECTS.md` | "KILL_ON_JOB_CLOSE safety net" | Correct but conditional | **ACCURATE** |
| `IProcessTracker.cs` | "INVARIANT-2 enforcement: throws" | Implementation silently overwrites | **INCORRECT** |
| `StartupOrphanDetector.cs` | "Only identifies orphans, does NOT kill" | Correct | **ACCURATE** |
| `PROCESS-RECOVERY.md` | "Process exit monitoring: On demand" | Process.Exited exists but unconnected | **OUTDATED** |

---

## Architecture Compliance

### Clean Architecture

| Layer | Contains | Windows API? | Status |
|-------|----------|-------------|--------|
| MultiSeat.Shared | IProcessTracker, IProcessMonitor, IProcessGroup, ProcessIdentity, ManagedProcess, ProcessExitInfo | ❌ No | ✅ CORRECT |
| MultiSeat.Service | WindowsProcessTracker, WindowsProcessGroup, WindowsProcessMonitor, Kernel32, SafeJobHandle | ✅ Yes | ✅ CORRECT |

**No Windows API leakage into domain layer.**

### Dependency Direction

```
VibepolloManager → IProcessTracker (Shared)
VibepolloManager → IProcessGroupManager (Shared)
VibepolloManager → IProcessMonitor (Shared)
SeatManager → IProcessGroupManager (Shared)
SessionHealthCheck → VibepolloManager (Service)
```

**No circular dependencies.**

### Seat Aggregate

Seat 作为 aggregate root 的模型是正确的。ProcessTracker, ProcessGroup, ProcessMonitor 都通过 SeatId 关联到 Seat，但不直接引用 Seat 对象。

---

## Findings by Severity

### HIGH

无。

### MEDIUM

| # | Finding | File | Impact |
|---|---------|------|--------|
| M1 | `ProcessExited` event 无 consumer — event-driven monitoring 是 dead infrastructure | `WindowsProcessMonitor.cs` | Recovery 仍依赖 5s polling |
| M2 | `SessionHealthCheck` 与 `IProcessMonitor` 双重 liveness 检测，互不知情 | `SessionHealthCheck.cs` + `WindowsProcessMonitor.cs` | 未来接 consumer 时需处理 double recovery |
| M3 | `WindowsProcessGroup._disposed` 非 volatile | `WindowsProcessGroup.cs:22` | Theoretical double-dispose (SafeHandle 安全) |

### LOW

| # | Finding | File | Impact |
|---|---------|------|--------|
| L1 | `IProcessTracker.Register` 文档声明 throw, 实际静默覆盖 | `IProcessTracker.cs` + `WindowsProcessTracker.cs` | Contract violation |
| L2 | `_bySeat` ConcurrentBag 不随 Unregister 清理 | `WindowsProcessTracker.cs` | Memory growth (bounded) |
| L3 | `StartupOrphanDetector._processTracker` 注入但从未使用 | `StartupOrphanDetector.cs` | Dead code |
| L4 | `StartupOrphanDetector` correlation 使用 AccountName 子串匹配 | `StartupOrphanDetector.cs` | False positive risk |
| L5 | `MarkExpectedExit` 与 process crash 的 race | `IProcessMonitor.cs` | Exit 可能标记为 expected (健康检查作为 backup) |
| L6 | `Process.Exited` 可能在 `HasExited` 检查后丢失 | `WindowsProcessMonitor.cs` | 极小窗口，monitoring entry 残留 |

### INFORMATIONAL

| # | Finding | Impact |
|---|---------|--------|
| I1 | KILL_ON_JOB_CLOSE 无 integration test | 测试覆盖盲区 |
| I2 | Concurrent restart + teardown 无 test | 测试覆盖盲区 |
| I3 | Service crash → cleanup 无 test | 测试覆盖盲区 |
| I4 | Vibepollo child process 不能保证继承 Job Object | 取决于 Vibepollo 行为 |

---

## False Guarantees

以下文档声明听起来比实际保证更强：

| Document | Claim | Reality |
|----------|-------|---------|
| `IProcessTracker.cs` | "INVARIANT-2 enforcement: throws" | Implementation silently overwrites |
| `P0-3-PROCESS-LIFECYCLE.md` | "Event-driven crash detection replaces polling" | ProcessExited exists but nobody subscribes |
| `P0-3-PROCESS-LIFECYCLE.md` | "Immediate, no polling overhead" | Recovery 仍通过 5s HealthCheck polling |
| `PROCESS-RECOVERY.md` | "Process exit monitoring: On demand" | 建议比实际更完善 |
| `IProcessGroup.cs` | "INVARIANT-2: A process can be assigned to at most one group" | 无 enforcement — 同一 PID 可被 assign 到多个 Job |

---

## Required Changes Before P1

### Must Fix

1. **M1 + M2: 连接 `ProcessExited` 到 recovery logic，或移除 event-driven monitoring**
   - 方案 A: 在 `VibepolloManager` 中订阅 `ProcessExited`, 调用 `RestartAsync`（需处理 double recovery）
   - 方案 B: 保持 polling-based recovery, 移除 `IProcessMonitor`（减少复杂度）
   - 推荐: 方案 A — event-driven 是正确方向, 但必须处理与 `SessionHealthCheck` 的互斥

2. **L1: 修复 `IProcessTracker` 文档或实现**
   - 方案 A: 在 `Register` 中检查 cross-seat 并 throw
   - 方案 B: 修改文档为 "last writer wins"
   - 推荐: 方案 A — invariant 应该被 enforced

### Should Fix

3. **M3: `WindowsProcessGroup._disposed` 改为 volatile 或使用 `Interlocked`**
4. **L2: `Unregister` 同时从 `_bySeat` 移除 identity**
5. **L3: 移除 `StartupOrphanDetector._processTracker` 无用依赖**

### Can Defer

6. **I1-I4: 增加 integration tests** — 但需要非 job-runner 环境

---

## P1 Roadmap Recommendation

当前顺序（P1-A Game Tracking, P1-B Provider Abstraction, P1-C Backoff）需要调整：

### Recommended Order

```
P1-0: Fix M1+M2 (connect ProcessExited or remove)
       ↓
P1-A: Fix L1 (enforce cross-seat invariant)
       ↓
P1-B: Game Process Tracking (extends IProcessMonitor usage)
       ↓
P1-C: Provider Abstraction (IStreamingProvider)
       ↓
P1-D: Progressive Crash Backoff
```

**理由:**

1. P1-0 必须先做 — 当前 event-driven monitoring 是 dead code, 要么连接要么移除
2. P1-A (invariant fix) 很小但重要 — 在增加更多使用前修复 contract
3. P1-B (Game Tracking) 自然扩展 IProcessMonitor 到 game processes
4. P1-C (Provider Abstraction) 解耦 Vibepollo — 但影响面大, 建议在 game tracking 稳定后做
5. P1-D (Backoff) 依赖于可靠的 crash detection — 应在 P1-0 之后

---

## Final Verdict

### P0-1

**PASS WITH CHANGES**

ProcessIdentity 正确保护 PID reuse。WindowsProcessTracker 功能正确但有 contract violation（L1）和 memory leak（L2）。

### P0-2

**PASS WITH CHANGES**

Job Object 实现正确，KILL_ON_JOB_CLOSE guarantee 有效（有条件）。`_disposed` volatile issue（M3）应修复。

### P0-3

**PASS WITH CHANGES**

ProcessMonitor 基础设施正确但未连接 consumer（M1）。双重 liveness 检测（M2）需要架构决策。

### Blocking Issues

**无。** 所有 P0 组件可以继续使用，但应优先修复 M1+M2。

### Can We Start P1?

**YES, 但有前提。**

P1-0（连接或移除 `ProcessExited`）应该作为 P1 的第一步。之后可以并行推进 Game Tracking 和 Provider Abstraction。

---

*Review completed. No production code changed.*
