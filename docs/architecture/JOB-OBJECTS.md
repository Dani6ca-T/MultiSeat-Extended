# Job Objects

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define when and how to use Windows Job Objects for process isolation and cleanup.

---

## What Are Job Objects

Windows Job Objects are kernel objects that allow grouping processes and applying limits to them. Key feature: **JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE** terminates all processes in the job when the job handle is closed.

---

## Job Object Usage

### Per-Seat Job Object

Each Seat gets its own Job Object containing:

| Process | Required | Reason |
|---------|----------|--------|
| sunshine.exe (provider) | Yes | Provider process |
| game.exe (games) | Yes | Game processes |
| helper.exe (display isolation) | Yes | Helper processes |
| mstsc.exe (RDP) | No | Managed by Windows |

**DECISION**: Each Seat has one Job Object for provider + game + helper processes.

---

### Job Object Configuration

```csharp
var jobHandle = CreateJobObject(null, null);

var limits = new JOBOBJECT_BASIC_LIMIT_INFORMATION
{
    LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
};

SetInformationJobObject(jobHandle, JobObjectInfoType.BasicLimitInformation, ref limits);
```

**DECISION**: Use JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE for guaranteed cleanup.

---

## Nested Jobs

### Question

Should we use nested Job Objects (one per seat, one global)?

### Analysis

| Approach | Pros | Cons |
|----------|------|------|
| Flat (one per seat) | Simple, clear ownership | No global limit |
| Nested (per seat + global) | Global resource limits | Complexity |

### Decision

**Flat model** — One Job Object per Seat. No nested jobs.

**Rationale**: Seat is the aggregate root and owns all processes. Global limits add complexity without clear benefit.

---

## Process Assignment

### When to Assign

| Process | Assign When | Remove When |
|---------|-------------|-------------|
| sunshine.exe | Provider started | Provider stopped |
| game.exe | Game launched | Game exited |
| helper.exe | Helper launched | Helper exited |

### Assignment Flow

```
1. Create Job Object (on Seat provisioning)
2. Assign provider process (on provider start)
3. Assign game process (on game launch)
4. Assign helper process (on helper launch)
5. Close Job Object (on Seat teardown)
   └── All processes terminated
```

---

## Breakaway Processes

### Question

Should provider processes be allowed to break away from the Job Object?

### Analysis

| Allow breakaway | Prevent breakaway |
|-----------------|-------------------|
| Provider can spawn independent processes | All provider children are contained |
| Risk of orphan processes | Guaranteed cleanup |
| More flexible | More predictable |

### Decision

**Prevent breakaway** — All processes in the job are contained.

**Rationale**: Orphan processes are a known problem (research finding). Job Objects solve this.

---

## Kill-on-Close Behavior

### When Handle is Closed

```
Seat teardown
    │
    ├── Job Object handle closed
    │
    └── All processes in job terminated
        ├── sunshine.exe
        ├── game.exe
        └── helper.exe
```

### Timing

Job Object handle is closed AFTER best-effort graceful shutdown but BEFORE resource release.

### Flow

```
1. Graceful shutdown provider (8s timeout)
2. Graceful shutdown games (best-effort)
3. Close Job Object handle (force kill remaining)
4. Release ports
5. Destroy display
6. Logoff session
```

**DECISION**: Job Object is the final cleanup guarantee.

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE terminates processes | Windows API | FACT |
| Helios uses CreateJobObject | Helios ProcessManager | FACT (inferred) |
| Orphan processes are a problem | Research finding | FACT |
| Job Objects solve orphan problem | Windows documentation | FACT |
