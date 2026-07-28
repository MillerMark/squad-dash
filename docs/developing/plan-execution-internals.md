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
