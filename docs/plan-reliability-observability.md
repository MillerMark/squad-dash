# Plan Reliability & Observability

Technical documentation for the plan execution reliability features introduced in the `feature/plan-reliability-observability` branch. These features ensure that plan execution survives process restarts, approval gates persist durably, and the UI presents consistent activity state at all times.

---

## Table of Contents

1. [Lifecycle Authority](#lifecycle-authority)
2. [Repair Replay](#repair-replay)
3. [Recovery Choices](#recovery-choices)
4. [Queued Continuation](#queued-continuation)
5. [Activity States](#activity-states)
6. [Approval Restoration](#approval-restoration)
7. [Anchor Inference](#anchor-inference)
8. [Diagnostics](#diagnostics)
9. [Limitations](#limitations)

---

## Lifecycle Authority

**File:** `SquadDash/WorkspaceConversationStore.cs` — `ActiveLoopExecutionState`

`ActiveLoopExecutionState` is the single source of truth for an in-progress plan execution loop. It is persisted as part of the workspace conversation state and survives process restart.

### Key Properties

| Property | Purpose |
|----------|---------|
| `LoopPath` | The file-system path being monitored for loop events |
| `FilterText` | Active filter text for the loop panel |
| `DecomposeGroupId` | Plan group currently executing (null if no plan) |
| `DecomposeRevision` | Revision identifier for the current execution |
| `PlanExecutionAttempt` | Current attempt state (assignments, attempt ID) |
| `PreviousPlanExecutionAttempts` | Up to 20 historical attempts for diagnostics |
| `LastCompletedIteration` | Iteration watermark for idempotent replay |
| `RecoveryTaskId` / `RecoveryAttemptId` | If recovering from interruption, which task/attempt |
| `RepairRequestCount` / `FreshAttemptCount` | Counters for protocol-repair tracking |
| `TaskBaselineCommit` | Git commit baseline before the current task started |
| `PendingRepairResult` | Captured repair result awaiting replay |

### Normalization

`ActiveLoopExecutionState.Normalize()` enforces invariants on deserialization:
- Trims all string fields; treats whitespace-only as null
- Discards `PlanExecutionAttempt` if it doesn't match the current group/revision
- Discards `PendingRepairResult` if its group/revision doesn't match
- Deduplicates previous attempts by AttemptId, keeping last 20
- Returns `null` if both loop path and group ID are empty (no meaningful state)

---

## Repair Replay

**File:** `SquadDash/WorkspaceConversationStore.cs` — `PendingRepairResult`

When a protocol-repair response arrives but cannot be immediately consumed (e.g., the process is restarting), it is captured in `PendingRepairResult` and persisted alongside `ActiveLoopExecutionState`.

### Record Shape

```csharp
record PendingRepairResult(
    string GroupId,
    string Revision,
    string TaskId,
    string? AttemptId,
    string? ResultJson,
    string? ErrorText);
```

### Matching Logic

`PendingRepairResult.Matches(groupId, revision, attemptId)` returns `true` when:
- GroupId matches exactly (ordinal)
- Revision matches exactly (ordinal)
- AttemptId is null (wildcard) OR matches exactly

This scoping ensures a stale repair result from a previous plan revision is never replayed into a newer execution.

### Lifecycle

1. Agent sends a protocol-repair response
2. SquadDash captures it as `PendingRepairResult` and persists
3. On restart, `Normalize()` validates scope; discards if mismatched
4. When the loop resumes, the pending result is replayed into the execution pipeline
5. After successful consumption, the field is cleared

---

## Recovery Choices

**File:** `SquadDash/CompletedWorkReviewPresentation.cs`

When a plan is interrupted mid-task, the user must decide how to handle partially-completed work. `CompletedWorkReviewPresentationBuilder.Build()` constructs an immutable presentation model from durable plan state.

### Presentation Model

| Field | Content |
|-------|---------|
| `StopReason` | Human-readable interruption reason |
| `TaskTitle` / `TaskId` | Which task was interrupted |
| `Commit` | Commit evidence (SHA, summary, verification status) |
| `ChangedFiles` | Files affected by the interrupted work |
| `TestSummary` | Test results if verification ran (e.g., "Tests passed") |
| `DownstreamTasks` | Tasks that depend on this one (still pending/executing) |
| `AcceptanceEffect` | What happens if the user accepts the partial work |
| `RetryRiskWarning` | Warning about retrying already-committed work |

### Design Principles

- **Never infers ownership from timing** — always uses durable provenance data
- Returns `null` if no commit evidence exists for the requested task
- Downstream task discovery uses the plan's dependency graph, not execution order

---

## Queued Continuation

**File:** `SquadDash/PlanContinuationQueuePresentation.cs`

Displays the next locked continuation step in the Inbox queue. This is a synthetic, non-editable Inbox item that tells the user what happens next.

### Build Logic

1. Compute `nextStepNumber = CompletedCount + 2` (current step is +1, next is +2)
2. If `nextStepNumber > TotalCount`, return `null` (plan is finishing)
3. Resolve the task at that position
4. Build dependency explanation (why it's next)
5. Return `PlanContinuationQueueDisplay(StepNumber, Label, Description)`

### User-facing Description Includes

- Plan title
- Next task name
- Why it's next (dependency-ready or explicit dependency list)
- Release condition (after current step accepted + approval resolved)
- Non-editable notice

---

## Activity States

**File:** `SquadDash/PlanTaskActivityState.cs`, `SquadDash/PlanTaskActivityResolver.cs`

### The 6 States

| State | Visual Indicator | Meaning |
|-------|-----------------|---------|
| `Executing` | Spinner | Task is actively running |
| `Queued` | Static dot | Waiting for dependencies or execution slot |
| `AwaitingApproval` | Static "Waiting" | Blocked by an approval gate |
| `Blocked` | Static blocked | Failed dependency or explicit block |
| `Interrupted` | Interrupted icon | Execution was interrupted |
| `Completed` | Checkmark | Task finished successfully |

### Resolution Algorithm (`PlanTaskActivityResolver.Resolve`)

For each task, evaluated in priority order:

1. **Terminal:** Complete/Superseded → `Completed`; Failed → `Blocked`
2. **Active:** Executing → `Executing`
3. **Partial:** Partial status → `Interrupted`
4. **Gate-blocked:** Task ID in a pending/awaiting gate's `BeforeTaskIds` → `AwaitingApproval`
5. **Plan-level interruption:** Plan is Interrupted → `Interrupted`
6. **Plan-level awaiting:** Plan is AwaitingApproval → `AwaitingApproval`
7. **Failed dependency:** Any dependency has Failed status → `Blocked`
8. **Default:** → `Queued`

### Plan-Level Resolution (`ResolvePlanLevel`)

Maps `PlanLifecycleStatus` to the aggregate indicator shown in the Plans panel. `Stopped` and `Archived` both map to `Completed`.

---

## Approval Restoration

**File:** `SquadDash/DurableApprovalRequestManager.cs`

Maintains exactly **one** Inbox message per plan for its entire approval lifecycle. All approval gates for a plan are aggregated into this single message.

### Key Behaviors

| Operation | Behavior |
|-----------|----------|
| `AppendCheckpointAsync` | Adds a gate ID to the message; creates message if first gate; unarchives if archived; idempotent if gate already tracked |
| `RefreshEvidenceAsync` | Updates snapshot data without changing gate set |
| `ResolveCheckpointAsync` | Moves gate from active to resolved with timestamp and disposition |
| Archive | Sets `Archived = true`; message remains in store for history |

### Persistence Model

```csharp
record DurableApprovalState(
    string PlanId,
    IReadOnlyList<string> ActiveGateIds,
    IReadOnlyList<ResolvedCheckpointEntry> ResolvedCheckpoints,
    DateTimeOffset? LastNotifiedAt,
    bool Archived,
    int Version);
```

### Concurrency

- Per-plan `SemaphoreSlim` ensures serialized mutations
- Uses `InboxStore` atomic file replacement for crash safety
- Message ID is deterministic from PlanId (`BuildMessageId`)

### Restart Survival

On restart, `InboxStore` loads all messages from disk. The `DurableApprovalRequestManager` finds the existing message by ID and continues from the persisted `DurableApprovalState`. No in-memory state needs reconstruction.

---

## Anchor Inference

**File:** `SquadDash/ApprovalAnchorInferenceEngine.cs`, `SquadDash/ApprovalAnchorPresentation.cs`

Determines which approval gate is the **primary visual controller** (rendered at full opacity) when no stored presentation anchor exists.

### Priority Order

1. **Stage milestone** — anchor starts with `stage:` (e.g., `stage:2`)
2. **ALL join** — anchor starts with `all:` (multiple tasks must complete)
3. **Task exit/entry** — anchor starts with `task-after:` or `task-before:`
4. **Declaration order** — first gate in the plan (fallback)

### Equivalence

Gates sharing the same anchor string as the primary are marked as "equivalent" and rendered at half-opacity. This prevents visual clutter when multiple gates represent the same logical boundary.

### Output Model

```csharp
record ApprovalAnchorPresentation(
    string PrimaryGateId,
    string PrimaryAnchor,
    IReadOnlyList<string> EquivalentGateIds,
    string RequirementsSentence,
    IReadOnlyList<ApprovalAnchorSummaryItem> SummaryItems);
```

### Requirements Sentence

A human-readable sentence derived from the anchor type:
- Stage: "Human approval required between stage N and stage N+1 of M."
- ALL join: "Human approval required at ALL join before Task1, Task2."
- Task-after: "Human approval required after TaskName completes."
- Task-before: "Human approval required before TaskName starts."

---

## Diagnostics

**File:** `SquadDash/SquadDashTrace.cs` (used pervasively)

All reliability-critical paths use `SquadDashTrace.Write(category, message)` for structured trace logging visible in the Trace panel.

### Usage Pattern

```csharp
SquadDashTrace.Write("Category", $"Descriptive message: {details}");
```

### Coverage in Reliability Features

| Component | Category | What's Logged |
|-----------|----------|---------------|
| `PlanViewerLiveSyncHandler` | `General` | Stale event rejection with completion counts |
| `DurableApprovalRequestManager` | `Approval` | Checkpoint append, resolve, archive operations |
| `PlanCollectionService` | `Plans` | Collection transitions, idempotency hits |
| `PlanExecutionTransitionService` | `Plans` | Start/resume transitions, guard rejections |
| `PlanProgressPublisher` | — | Persistence errors (via caller) |

### Design Rules

- Every `catch` block that doesn't rethrow must call `SquadDashTrace.Write()` (per decision 2026-06-02)
- Categories are short, descriptive strings for filtering in the Trace panel
- Never log secrets or user content — only structural/operational data

---

## Limitations

### Known Constraints

1. **Single-plan-per-workspace assumption** — `ActiveLoopExecutionState` tracks one active plan group at a time. Concurrent plans in the same workspace are not supported.

2. **PendingRepairResult is single-slot** — Only one pending repair result can be stored at a time. If a second repair arrives before the first is consumed, the first is overwritten.

3. **80ms coalescence is best-effort** — `PlanViewerLiveSyncHandler`'s timer relies on the WPF Dispatcher. If the UI thread is blocked, coalescence may exceed 80ms.

4. **Previous attempts capped at 20** — `Normalize()` keeps only the last 20 previous plan execution attempts. Earlier history is discarded on deserialization.

5. **Gate-blocked inference is static** — `GetGateBlockedTaskIds` is computed once per `Resolve()` call. If gate status changes during resolution (impossible in practice since input is immutable), the result could be stale.

6. **Anchor inference has no stored override** — The engine always infers from plan structure. There is no mechanism to manually pin a primary anchor.

7. **Stale-event rejection uses completion count only** — If a plan regresses its completed count (e.g., a task is un-completed), the event would be rejected as stale. This scenario is not expected in normal operation.

8. **DurableApprovalState version is monotonic** — No conflict resolution; last-writer-wins within the per-plan lock scope.

---

## Test Coverage

| Test Suite | Tests | Coverage Area |
|------------|-------|---------------|
| `PendingRepairResultTests` | 14 | Matching logic, scope validation, normalization |
| `CompletedWorkReviewPresentationTests` | 26 | Builder output, edge cases, null handling |
| `PlanTaskActivityResolverTests` | 24 | All 6 states, gate blocking, plan-level mapping |
| `PlanContinuationQueueTests` | 13 | Build logic, boundary conditions |
| `ApprovalNotificationLifecycleTests` | 39 | Full notification matrix, idempotency |
| `ApprovalAnchorInferenceEngineTests` | 29 | Priority selection, equivalence, font metrics |
| `DeterministicPlanLifecycleHarnessTests` | 25 | End-to-end lifecycle scenarios |

**Total: 170 focused tests** covering the reliability subsystem.
