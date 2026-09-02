# Apollo vs Vibepollo — Comparison

**Date**: 2026-08-30
**Purpose**: Detailed comparison of Apollo and Vibepollo

---

## Fork Relationship

```
Sunshine (LizardByte)
    ↓
Apollo (ClassicOldSong)
    ↓
Vibepollo (Nonary)
```

- Apollo is a fork of Sunshine
- Vibepollo is a fork of Apollo
- Both inherit core Sunshine architecture

---

## Feature Comparison

| Feature | Apollo | Vibepollo | Winner |
|---------|--------|-----------|--------|
| **Base** | Sunshine fork | Apollo fork | - |
| **Virtual display** | SudoVDA (built-in) | Own bundled driver | Vibepollo (more control) |
| **Per-client identity** | Yes (fixed) | Yes (inherited) | Tie |
| **Permission system** | Yes | Yes | Tie |
| **Clipboard sync** | Yes | Yes | Tie |
| **Client hooks** | Yes | Yes | Tie |
| **Input-only mode** | Yes | Yes | Tie |
| **Dual GPU** | Yes | Yes | Tie |
| **HDR support** | Yes | Yes | Tie |
| **RTSS integration** | No | Yes | Vibepollo |
| **Lossless Scaling** | No | Yes | Vibepollo |
| **NVIDIA Smooth Motion** | No | Yes | Vibepollo |
| **AI-generated code** | No | Yes (99%) | Apollo (quality) |
| **License** | GPLv3 | GPLv3 | Tie |
| **Development pace** | Slower | Very active | Vibepollo |
| **Community** | Larger (10.7k stars) | Smaller | Apollo |
| **Stability** | More stable | Less stable | Apollo |

---

## Architecture Comparison

### Apollo Architecture

```
Sunshine core
    + Built-in SudoVDA
    + Per-client fixed identity
    + Permission system
    + Clipboard sync
    + Client hooks
```

### Vibepollo Architecture

```
Apollo core
    + Own bundled virtual display driver
    + RTSS integration
    + Lossless Scaling
    + NVIDIA Smooth Motion
    + AI-generated enhancements
```

---

## Virtual Display Comparison

| Aspect | Apollo | Vibepollo |
|--------|--------|-----------|
| Driver | SudoVDA (SudoMaker) | Own bundled driver |
| Identity | Fixed per client | Fixed per client |
| HDR | Yes | Yes |
| Resolution matching | Auto | Auto |
| Lifecycle | Created on stream start | Created on stream start |
| Config | sunshine.conf | sunshine.conf |
| Multi-display | Limited (Issue #874) | Limited |

---

## Permission System Comparison

| Aspect | Apollo | Vibepollo |
|--------|--------|-----------|
| Role-based | Yes | Yes |
| First client | FULL permissions | FULL permissions |
| New clients | View + List only | View + List only |
| Permissions | View, List, Launch, Mouse, Keyboard | View, List, Launch, Mouse, Keyboard |
| Config | Web UI | Web UI |

---

## Development Activity

### Apollo

- **Stars**: 10.7k
- **Forks**: 420
- **Last update**: Slower pace
- **Maintainer**: ClassicOldSong (single maintainer)
- **Community**: Larger, more established

### Vibepollo

- **Stars**: Smaller
- **Forks**: Fewer
- **Last update**: Very active (multiple releases per week)
- **Maintainer**: Nonary (Chase Payne)
- **Community**: Smaller, newer

---

## Code Quality

### Apollo

- Based on Sunshine (mature codebase)
- Human-written code
- More stable
- Better tested

### Vibepollo

- "99% AI-generated" (author's statement)
- Rapid development
- More features
- Less stable

---

## Multi-Instance Support

### Apollo

- Manual setup required
- Wiki instructions available
- Virtual display conflicts (Issue #874)
- Not designed for multiseat

### Vibepollo

- Manual setup required
- Same limitations as Apollo
- Not designed for multiseat

---

## What MultiSeat-Extended Should Use

### Use Apollo If:

- Stability is priority
- Larger community support
- Human-written code preferred
- Less frequent updates needed

### Use Vibepollo If:

- Latest features needed
- RTSS/Lossless Scaling integration needed
- Active development preferred
- Can tolerate AI-generated code

### Recommendation

**Use Vibepollo** for MultiSeat-Extended because:
1. More active development
2. RTSS integration (game compatibility)
3. Lossless Scaling (frame generation)
4. NVIDIA Smooth Motion (smoother gameplay)
5. More frequent bug fixes

**But**: Both are GPLv3, so must keep as external process.

---

## Evidence

| Claim | Source | Evidence | Status |
|-------|--------|----------|--------|
| Apollo is Sunshine fork | README | "Sunshine fork" | VERIFIED |
| Vibepollo is Apollo fork | README | Fork chain | VERIFIED |
| Apollo uses SudoVDA | README | "Apollo uses SudoVDA" | VERIFIED |
| Vibepollo has own driver | README | Research summary | VERIFIED |
| Apollo has permission system | README | "Permission management" | VERIFIED |
| Vibepollo has RTSS integration | README | Research summary | VERIFIED |
| Vibepollo is 99% AI-generated | README | Author statement | VERIFIED |
| Apollo has 10.7k stars | GitHub | Star count | VERIFIED |
| Apollo GPLv3 | LICENSE | GPLv3 text | VERIFIED |
| Vibepollo GPLv3 | LICENSE | GPLv3 text | VERIFIED |
