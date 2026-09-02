# Architecture Risks

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Document architectural risks with likelihood, impact, and mitigation.

---

## Risk Categories

### Windows Compatibility

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Windows update breaks TermWrap | Medium | High | TermWrap auto offset discovery |
| Windows update breaks SudoVDA | Low | High | Monitor SudoVDA releases |
| Windows API changes | Low | Medium | Abstract Windows APIs |

### Driver Compatibility

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| SudoVDA license changes | Low | High | Investigate license now |
| HidHide session jail removed | Low | Medium | Monitor HidHide releases |
| Driver signing requirements | Low | Medium | Use signed drivers |

### RDP

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| RDP loopback broken | Low | High | Test on new Windows versions |
| NLA requirements change | Low | Medium | Monitor RDP changes |
| mstsc behavior changes | Low | Medium | Test mstsc updates |

### Provider Instability

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Vibepollo abandoned | Low | Medium | Apollo as fallback |
| Vibepollo breaking changes | Medium | Medium | Provider abstraction |
| GPLv3 license conflict | Low | High | Keep as external process |

### Game Compatibility

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Games refuse RDP | High | Medium | Document limitations |
| Anti-cheat conflicts | High | High | Do not patch anti-cheat |
| Steam restrictions | High | Medium | Accept limitation |

### Security

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Credential exposure | Low | High | DPAPI + no credentials in wire |
| Privilege escalation | Low | High | GrantSeatAdministrator = false |
| API exposure | Medium | Medium | ApiBindLoopbackOnly option |

### License

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| GPLv3 code in core | Low | High | Provider is external process |
| SudoVDA license unknown | Medium | Medium | Investigate license |
| HidHide license change | Low | Low | MIT, permissive |

### Maintenance

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| TermWrap unmaintained | Low | High | No viable alternative |
| SudoVDA unmaintained | Low | Medium | Virtual-Display-Driver as fallback |
| Helios pattern drift | Low | Low | Reference only, not code |

---

## Risk Summary

| Category | Risks | High | Medium | Low |
|----------|-------|------|--------|-----|
| Windows compatibility | 3 | 1 | 1 | 1 |
| Driver compatibility | 3 | 1 | 1 | 1 |
| RDP | 3 | 1 | 2 | 0 |
| Provider instability | 4 | 1 | 2 | 1 |
| Game compatibility | 3 | 1 | 2 | 0 |
| Security | 3 | 2 | 1 | 0 |
| License | 3 | 1 | 1 | 1 |
| Maintenance | 3 | 1 | 1 | 1 |
| **Total** | **25** | **9** | **11** | **5** |

---

## Top Risks

1. **Games refuse RDP** (High likelihood, Medium impact) — Accept limitation
2. **Anti-cheat conflicts** (High likelihood, High impact) — Do not patch
3. **Steam restrictions** (High likelihood, Medium impact) — Accept limitation
4. **Credential exposure** (Low likelihood, High impact) — DPAPI + security architecture
5. **SudoVDA license** (Medium likelihood, Medium impact) — Investigate now

---

## Evidence

| Risk | Source | Status |
|------|--------|--------|
| TermWrap auto offset discovery | TermWrap README | FACT |
| Vibepollo is GPLv3 | LICENSE file | FACT |
| SudoVDA license unknown | LICENSE search | FACT (absent) |
| Games refuse RDP | Research | FACT |
| Anti-cheat conflicts | Duo release notes | FACT (public) |
