# Plan Reliability — Disposable Live-Probe Report

> **Status:** Template / Expected Outcomes  
> **Date:** 2026-08-02  
> **Branch:** `feature/plan-reliability-observability`  
> **Revision:** `1177597d6627bc45`

---

## Purpose

This document defines the live-probe protocol for verifying plan reliability in a disposable self-hosted environment. It specifies what to exercise, what to measure, and what to record.

---

## Probe Scenario

### Setup

| Parameter | Value |
|-----------|-------|
| Plan type | Self-hosted, disposable |
| Named agents | 2–3 parallel (e.g., `alpha-task`, `beta-task`, `gamma-docs`) |
| Approval gates | 1 editable milestone approval |
| Repair scenario | 1 controlled protocol-repair response |
| Restart scenario | 1 build restart mid-execution |
| Completion review | Induced if interruption produces committed work |

### Steps

1. **Start plan** — Collect a decomposed plan into the Plans panel, then start execution
2. **Parallel execution** — Observe 2+ agents executing simultaneously
3. **Milestone approval** — Wait for approval gate; edit the milestone text; approve
4. **Protocol repair** — Induce a repair scenario (e.g., agent returns malformed JSON); observe capture in `PendingRepairResult` and replay after acknowledgment
5. **Build restart** — Kill and restart the application mid-task; verify `ActiveLoopExecutionState` restores correctly
6. **Completed-work review** — If the restart interrupts committed work, verify the recovery presentation appears in Inbox/transcript
7. **Plan completion** — Allow plan to finish; verify all tasks reach `Completed` state

---

## Expected Outcomes

### Agent & Worktree Evidence

| Checkpoint | Expected Evidence |
|------------|-------------------|
| Parallel execution | Multiple agent worktrees active simultaneously; Plan Viewer shows multiple `Executing` spinners |
| Milestone approval | Gate status transitions: `Pending` → `AwaitingApproval` → `Resolved`; Inbox message updates with resolution note |
| Protocol repair | `PendingRepairResult` field populated in conversation state JSON; after replay, field is cleared |
| Build restart | `ActiveLoopExecutionState` persists in workspace state; on restart, plan resumes from correct iteration |
| Completed-work review | `CompletedWorkReviewPresentation` shown with commit SHA, changed files, downstream impact |
| Plan completion | All tasks `Complete`; plan lifecycle status `Completed`; continuation queue returns `null` |

### Timing Expectations

| Operation | Expected Duration |
|-----------|-------------------|
| UI refresh coalescence | ≤80ms between rapid events (no visual thrashing) |
| Approval message creation | <500ms (atomic file write + Inbox refresh) |
| Restart recovery | <2s from launch to plan resumption |
| Stale event rejection | Immediate (no UI update triggered) |

### UI Observations

- [ ] Plans panel shows correct aggregate `PlanTaskActivityState` at all times
- [ ] Plan Viewer updates without flicker during rapid completions
- [ ] Approval controls become read-only after gate resolution
- [ ] Continuation queue item appears/disappears correctly
- [ ] Trace panel shows diagnostic entries for rejection/recovery events

---

## Residual Defects (To Be Filled During Live Run)

| # | Severity | Description | Component | Workaround |
|---|----------|-------------|-----------|------------|
| 1 | — | _(none observed yet)_ | — | — |

---

## Diagnostics Checklist

During the live probe, verify these trace entries appear:

- [ ] `PlanViewerLiveSync: rejected stale event` — at least once (induce by sending lower completion count)
- [ ] Approval checkpoint append logged
- [ ] Plan collection transition logged
- [ ] Plan start/resume transition logged
- [ ] Repair result capture logged
- [ ] Repair result replay logged

---

## Conclusion Template

> **Result:** PASS / FAIL  
> **Tests passing:** _/170  
> **Residual defects:** _  
> **Recommendation:** Ready for merge / Needs follow-up on [specific issue]

---

## Notes

This probe is designed to be **disposable** — the plan, agents, and worktrees are cleaned up after the run. No production state is modified. The probe validates the same invariants covered by the 170 unit tests but exercises real I/O paths (file system, process lifecycle, WPF dispatcher).
