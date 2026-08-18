# Targeted Code & Architectural Review

This document defines the review severity rubric, review discipline, and reporting standards for FrameHub.

---

## Review Severity Rubric

All review findings must be classified into one of four distinct severity tiers. Reviewers must base findings strictly on verified code evidence, never speculative preferences.

| Severity | Definition | Action Required | Examples |
|---|---|---|---|
| **P0** | **Critical Security / Destructive Defect** | Immediate blocker. Must fix before proceeding. | Remote arbitrary process kill; unauthenticated write bypass; kernel panic / native crash; silent data destruction. |
| **P1** | **Major Correctness / Invariant Violation** | Must fix before checkpoint. | Duplicate domain ownership introduced; wrong process targeted due to stale identity; WAL intent not persisted before OS mutation; scope escalation bug. |
| **P2** | **Narrow Correctness / UX Defect** | Fix in single narrow follow-up. | Incorrect fallback calculation; disabled state not updating properly; localized string missing; chip selection miscalculation in edge-case topology. |
| **P3** | **Minor Polish / Documentation Gap** | Optional / non-blocking. | Documentation formatting; non-critical test fixture cleanup; comment clarification. |

---

## One-Review / One-Fix Rule

To maintain development velocity and prevent unproductive review thrashing:

1. **Structured Sequence**:
   - `Implementation` $\rightarrow$ `Validation` $\rightarrow$ **`ONE Targeted Review`** $\rightarrow$ `(If P1/P2) ONE Narrow Fix Round` $\rightarrow$ `Validation` $\rightarrow$ `Checkpoint`.
2. **No Endless Audits**: Do not perform repeated repository-wide or multi-round audits on completed milestones.
3. **Closed Checkpoint Principle**: A pushed checkpoint is considered accepted and stable unless:
   - Manual smoke testing reveals a reproducible defect.
   - New hard evidence of an invariant violation is discovered.
   - A subsequent scheduled milestone explicitly modifies the subsystem.

---

## Review Report Format

Targeted reviews should conclude with a structured report following this template:

```markdown
# Targeted Review: [Milestone / Feature Name]

### Baseline & Diff Inspection
- **Branch**: `main`
- **HEAD**: `[commit-hash]`
- **Dirty Files**: `[count]` (all expected)
- **Diff Hygiene (`git diff --check`)**: Clean

---

### Detailed Findings by Area
- **Feature Separation & Architecture**: [Analysis]
- **Identity & Safety Validation**: [Analysis]
- **Authentication & Trust Boundaries**: [Analysis]
- **Localization & UX Quality**: [Analysis]
- **Automated Test Coverage**: [Analysis]

---

### Findings Summary
- **P0**: 0
- **P1**: 0
- **P2**: 0
- **P3**: 0

---

### Final Verdict
[MILESTONE NAME] REVIEW PASS
(or [MILESTONE NAME] REVIEW NEEDS FIXES)
```
