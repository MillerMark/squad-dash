---
title: Plan Execution Internals
nav_order: 5
parent: Developing
---

# Plan Execution Internals

Developer reference for the pure-logic helpers and test coverage introduced by the PLANUX-20260728 plan (tasks 001–011).

---

## New Pure-Logic Helpers

| Class | Purpose |
|---|---|
| `ILoopClock` / `SystemLoopClock` | Abstraction over `DateTimeOffset.UtcNow` and `Task.Delay` so loop cadence tests use a `FakeClock` without real delays |
| `LoopBoundaryDiagnostics` | Immutable record capturing timing data for one inter-round boundary: configured delay, actual delay, delay source, queue-drain flag |
| `ReadAgentSatelliteCoalescer` | Coalesces consecutive `read_agent` poll calls for the same agent into a single transcript entry; exposes `TryExtractAgentId` for pure-logic testing |
| `DecomposeEnvelopeRepairPrompt` | Builds the bounded hidden repair prompt sent when a worker omits its `DECOMPOSE_STEP_RESULT_JSON` envelope |
| `PlanExecutionLog` | Append-only NDJSON log at `.squad/logs/plan-execution.ndjson`; trims to 500 entries on load |
| `PlanPreflightBlockedException` | Thrown when uncommitted non-host-owned changes block a branch switch; carries `ChangedPaths` and `TargetBranch` |
| `ConfirmPreservedWorkDialog` | View-model for the concise interruption-recovery choice (Continue / Retry vs. Replan) |
| `PlanPreflightBlockedDialog` | View-model for the "Changes Blocking Branch Switch" dialog shown during preflight |

---

## Key Test Files

| Test file | Coverage |
|---|---|
| `LoopCadenceTests` | Loop interval math, `ILoopClock` fake-clock integration, `LoopBoundaryDiagnostics` collection |
| `AgentPollCoalescingTests` | `ReadAgentSatelliteCoalescer.TryExtractAgentId` edge cases (null, malformed, numeric, array JSON) |
| `DecomposeEnvelopeRepairTests` | `DecomposeEnvelopeRepairPrompt.Build` field presence; `DecomposeStepResultParser.TryParse` success/failure matrix |
| `PlanExecutionLogTests` | Append, round-trip field fidelity, malformed-line skipping, MaxEntries trim, file creation |
| `PlanPreflightTests` | `PlanPreflightBlockedException` shape: properties, message format, inheritance |
| `RecoveryUiTests` | `DecomposePlanInbox.BuildRecoveryMessage` body content, action labels, priority, ID |
| `PlanExecutionScenarioTests` | End-to-end scenarios spanning all 10 features: cadence, coalescing, envelope repair, log, preflight, PlanStore lifecycle, recovery inbox |

---

## Architecture Notes

- **Loop interval** is stored as `interval: 0.1` minutes (= 6 s) in `.squad/loop-executing-plan.md`. `LoopMdParser.ParseFromContent` requires a fenced frontmatter block with `configured: true`.
- **Coalescing** is purely in-memory: `ReadAgentSatelliteCoalescer.FindActiveEntry` walks the transcript list in reverse and reuses the last open `read_agent` entry for the same `agent_id`. It has a WPF dependency (`ToolTranscriptEntry`) so only `TryExtractAgentId` is covered by headless unit tests.
- **Envelope repair** is a single bounded retry: if the repair turn also produces no valid envelope, `_repairAttemptActive` prevents a second attempt and the plan transitions to `Blocked`.
- **PlanStore state machine** is enforced by `PlanStoreUpdater` pure static methods (`ApplyExecutionStarted`, `ApplyStepAccepted`, `ApplyCompleted`, `ApplyInterrupted`, `ApplyStopped`, `ApplyBlocked`). Load-time repair of impossible states is handled by `PlanStoreUpdater.RepairInconsistentState`.

---

## Collected-Plan Services (steps 001–008)

These services implement the "collect now, execute later" workflow.

| Class | Responsibility |
|---|---|
| `PlanCollectionService` | Owns the pending → durable transition. Converts a `PendingDecomposePlan` to an `Approved` Plan in the `PlanStore`. Idempotent, stale-revision rejection, active-plan protection, best-effort pending cleanup. |
| `PlanExecutionTransitionService` | Owns `Start` (Approved → Executing) and `Resume` (Interrupted → Executing). Pure logic + persistence; no UI. Idempotent guards for already-executing and terminal plans. |
| `PlanProgressPublisher` | Enforces persist-then-notify ordering: durable save must succeed before any observer is notified; an observer failure does not invalidate a saved transition. |
| `PlanViewerLiveSyncHandler` | Subscribes to `PlanProgressEvent` via `WeakEventBroker`. Filters by PlanId, rejects stale events (lower completion count), coalesces rapid updates with an 80 ms `DispatcherTimer`, and detaches on window close. |
| `PlanApprovalControlLockPolicy` | Pure-logic policy determining whether Plan Viewer approval controls are read-only based on execution progress. Task-entry, task-exit, stage-milestone, and ALL-join lock rules. |
| `PlanProgressEvent` | Published via `WeakEventBroker` on every lifecycle or progress change. Carries the fully updated `Plan` so subscribers avoid a separate store read. |

### Key invariants

- **Collection never launches execution.** `PlanCollectionService.Collect` only persists an `Approved` plan; starting the loop is a separate explicit `PlanExecutionTransitionService.Start` call.
- **Durable Plan is authoritative.** `.squad/plans/{planId}.json` is the system of record. `.squad/tasks.md` is a host-managed projection written during execution.
- **Persist-then-notify ordering.** `PlanProgressPublisher.TryPublish` calls `persist(plan)` first; if that throws, no notification fires. If `notify(plan)` throws, the persisted state is still valid.
- **80 ms coalescence window.** `PlanViewerLiveSyncHandler` buffers rapid `PlanProgressEvent` bursts into a single UI refresh every 80 ms, preventing visual thrashing during fast task completions.
