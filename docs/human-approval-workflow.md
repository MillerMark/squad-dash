# Human Approval Workflow

Comprehensive guide to the pre-execution gate editing, approval states, versioned actions, and Inbox lifecycle in SquadDash.

---

## Overview

SquadDash uses **approval gates** to pause plan execution at defined checkpoints and require explicit human sign-off before downstream tasks proceed. The system guarantees:

- A **single, stable Inbox message per plan** that tracks every gate across the plan's lifecycle.
- **Versioned click tokens** that prevent stale or duplicated approvals across surfaces.
- **Atomic live updates** so the Inbox and transcript card always reflect the latest evidence.
- **Restart-safe** durable state persisted in the Inbox store.

---

## 1. Pre-Execution Gate Editing

Before a plan begins executing, you can add, remove, or reposition approval gates from the plan viewer. Edits are handled by `PendingPlanGateEditor`.

| Step | What happens |
|------|-------------|
| User edits gates on a pending plan | Gate configuration is applied to the `PendingDecomposePlanStore`. |
| Content revision recomputed | A new draft revision hash is derived from the updated gate definition. |
| Inbox message atomically replaced | The old message (whose ID embeds the previous revision) is deleted and a replacement is saved, preserving the original timestamp and read state. |

**Key invariant:** The Inbox message ID for a pending plan always embeds the current revision. After a gate edit the old message vanishes and a new one takes its place — no stale actions can survive.

---

## 2. Approval States

### 2.1 Early Window (Gate Just Activated)

When a gate's prerequisites (`AfterTaskIds`) are all complete but ungated work still remains, the system enters the **early window**:

- The Inbox message is created or updated with the new checkpoint.
- A **one-time notification** fires: sound (`SoundEvent.ApprovalNeeded`), push notification, and Ultimate Callout.
- The notification coordinator (`ApprovalCardNotificationCoordinator`) marks the version as notified via `TryMarkNotifiedAsync` so subsequent refreshes do not re-fire.
- Execution **continues** on any tasks that are not behind an unapproved gate.

### 2.2 Fully Blocked

When no ungated eligible task remains — determined by `ApprovalGateReadinessEvaluator.ShouldStopForApproval` — the plan execution loop stops:

- All remaining ready tasks are behind unapproved gates.
- A themed approval card appears in the coordinator transcript (`TranscriptApprovalCardBuilder`).
- The card shows completed-task evidence, commit SHAs, changed files, downstream impact, and an approve button.

---

## 3. The Single Stable Per-Plan Inbox Message

`DurableApprovalRequestManager` maintains **exactly one Inbox message per plan** for the plan's entire lifecycle.

| Property | Value |
|----------|-------|
| **Message ID** | `approval-gate-{planId}` — stable, never changes. |
| **Subject** | `Approval needed: {plan title}` |
| **Priority** | `high` while any gate is active. |
| **Attachments** | `approval-gate` (durable state JSON) + `approval-snapshot` (review evidence). |

### Lifecycle

1. **First gate ready →** Message created with one active gate ID.
2. **Additional gates ready →** Same message updated; gate IDs appended. Archived flag cleared if message was previously archived.
3. **Gate approved →** Gate moved from `ActiveGateIds` to `ResolvedCheckpoints` with timestamp and optional note.
4. **All gates resolved →** Message marked `Read = true`, actions removed, body updated with resolution history, `Archived = true`.

The message exposes one aggregate **Approve Checkpoint(s) & Continue** action for the exact active-gate set. It does not require a separate click for every gate that became ready during the same review window.

---

## 4. Atomic Live Updates & Spinner Behavior

When an approval action is in progress:

1. `TranscriptApprovalCardBuilder.ShowUpdatingState` displays a semi-transparent overlay with a `⟳ Updating approval request…` spinner over the card.
2. The approve button and note text box are disabled.
3. After the async operation completes, a successful card becomes a disabled **Approved** record. A rejected/stale action is refreshed and re-enabled.
4. Inbox message body and attachments are atomically replaced via `InboxStore` (which uses atomic file replacement underneath).

---

## 5. Versioned Cross-Surface Actions

Approval can be triggered from multiple surfaces: the Inbox message actions, the transcript card, the plans panel, or the plan viewer. The `ApprovalActionCoordinator` prevents conflicts with **versioned click tokens**.

### ApprovalClickToken

```
PlanId          — which plan
PlanRevision    — content revision at render time
RequestVersion  — monotonic counter incremented on every state change
GateIds         — exact list of active gates when the button was rendered
```

### Click Validation Flow

1. Surface captures a click token at render time via `RegisterAsync` or `GetCurrentToken`.
2. User clicks approve → `TryApproveAsync(clickToken, gateIdsToResolve, note)` is called.
3. Coordinator compares the token field-by-field against the current state.
4. Result is one of:

| Result | Meaning |
|--------|---------|
| `Approved` | Token matched; gates resolved; `RequestVersion` incremented; `ApprovalResolved` event raised. |
| `StaleRejected` | Plan revision, version, or gate list has changed since the button was rendered. The surface should refresh. |
| `AlreadyResolved` | All requested gates were already resolved (e.g., by another surface). |
| `PersistenceFailed` | The authoritative plan transition could not be saved; no approval was recorded. |

**Critical safety property:** Approval applies only to the gate IDs present in the clicked request version. A later checkpoint that arrives between render and click is **never silently included** — the token mismatch causes `StaleRejected`, forcing a fresh render.

### Cross-Surface Invalidation

Inbox, transcript, Plans panel, plan viewer, and structured coordinator approvals all route through the same `PlanApprovalRuntime` callback. A successful durable plan save disables the transcript action, atomically refreshes the Inbox action, publishes `PlanProgressEvent` to the Plans panel and any open plan viewer, and resumes the loop only if it had been fully paused.

---

## 6. Resolved / Read / Archive & Later Unarchive

| Action | Effect |
|--------|--------|
| **Resolve last active gate** | Message marked `Read = true`, `Archived = true`, actions cleared. Body shows resolution history with timestamps and notes. |
| **New gate arrives on archived plan** | `Archived` flag cleared, `Read` set to `false`, new gate appended to `ActiveGateIds`, actions rebuilt. `ApprovalRefreshNeeded` fires. |

The message is never deleted — it accumulates the complete approval history for the plan in `ResolvedCheckpoints`:

```
✅ gate-1 resolved 2026-07-15 14:30 — "Looks good, tests pass"
✅ gate-2 resolved 2026-07-15 15:12
```

---

## 7. Notification Rules

Notifications are deduplicated per inbox message version by `ApprovalCardNotificationCoordinator`:

| Channel | When | Dedup mechanism |
|---------|------|-----------------|
| **Sound** | Early window, first notification for this version | `TryMarkNotifiedAsync` sets `LastNotifiedAt`; returns `false` on subsequent calls. |
| **Push notification** | Same as sound | Same gate; fires asynchronously via `PushNotificationService`. |
| **Inbox update** | Every state change | Always applied (idempotent message save). |
| **Transcript card** | Fully blocked state | Rendered by `TranscriptApprovalCardBuilder`; identified by `TranscriptApprovalCardTag(PlanId, GateId, Version)`. |

When a later checkpoint is appended, the durable request version advances and `LastNotifiedAt` resets, so that newly available review opportunity triggers exactly one fresh notification.

---

## 8. Task & Commit Summaries

The `ApprovalReviewSnapshot` presents structured evidence for every completed task within the gate boundary:

- **Per-task:** `TaskId`, `Title`, `CompletionSummary`, and a list of `ReviewCommitEntry` records.
- **Per-commit:** `ShortSha` (7-char), `FullSha`, `Subject` (first line of commit message), optional `VerificationPassed` flag (✓ / ✗).
- **Downstream impact:** Tasks in `BeforeTaskIds` that will be unblocked, with their current status.
- **Independent work:** Tasks completed outside the gate boundary during the early window are labeled separately as `IndependentWorkEntry` records.

---

## 9. Commit-Aware Historical File Review

Each `ChangedFileEntry` includes:

| Field | Description |
|-------|-------------|
| `FilePath` | Path relative to the repo root. |
| `Status` | `Added`, `Modified`, `Deleted`, `Renamed`, `Copied`, or `Unknown`. |
| `Insertions` / `Deletions` | Line-level diff statistics from `git show --numstat`. |
| `CommitSha` | The commit that introduced this change. |

File evidence is gathered in a single batched `git show --numstat` call across all relevant commit SHAs for efficiency.

The transcript card shows an expandable changed-files section (up to 50 files) with color-coded status indicators and `+N / −M` diff stats.

---

## 10. Current-File Viewer Actions

Each changed file provides two `app://` scheme links routed by the UI layer:

| Action | URI format | Behavior |
|--------|-----------|----------|
| **View at reviewed commit** | `app://file-at-commit:{sha}:{path}` | Opens the file content as it existed at the commit where it was reviewed. |
| **View current workspace file** | `app://open-workspace-file:{path}` | Opens the live workspace version in its registered viewer. |
| **View commit diff** | `app://commit-diff:{sha}` | Opens the full commit in the built-in diff viewer. |

---

## 11. Free-Text Notes

The transcript approval card includes an optional **note text box** where the reviewer can explain why they are approving:

- Watermark placeholder: *"Add a note about why you're approving…"*
- Pressing **Enter** in the note box triggers the approval action (keyboard-friendly flow).
- The note is saved in `ResolvedCheckpointEntry.ResolutionNote` and displayed in the archived message body.
- Notes are optional — leaving the box empty is valid.

---

## 12. Restart Recovery

`DurableApprovalRequestManager` is designed for crash-safe recovery:

1. **On workspace startup**, `PlanApprovalRuntime.RestoreAsync()` compares authoritative PlanStore gates with the persisted Inbox attachment.
2. Missing approval messages are recreated; stale Inbox gate IDs are resolved from authoritative plan state.
3. The `ApprovalActionCoordinator` restores the exact persisted request version and active-gate list, rather than inventing a new version during restart.
4. All per-plan mutations are serialized under async `SemaphoreSlim` locks, so concurrent restart and normal flow cannot corrupt state.

Because state is embedded in the Inbox message attachment and reconciled against PlanStore (not trusted as a second authority), **no approval progress is lost or resurrected across restarts**.

---

## 13. Revision Safety

Revision safety is enforced at multiple levels:

| Layer | Mechanism |
|-------|-----------|
| **Pending plan edits** | Gate edit → new content revision → old Inbox message deleted, replacement saved with new revision-based ID. |
| **Click tokens** | `ApprovalClickToken.Matches()` compares `PlanRevision`, `RequestVersion`, and `GateIds` exactly. Any mismatch → `StaleRejected`. |
| **Per-plan locks** | `SemaphoreSlim` per plan ID in both `DurableApprovalRequestManager` and `ApprovalActionCoordinator`. Prevents TOCTOU races. |
| **Atomic file writes** | `InboxStore` uses atomic file replacement — partial writes are impossible. |
| **Gate-scoped approval** | Approval applies **only** to the gate IDs shown in the clicked request version. A later checkpoint never silently piggybacks on an earlier click. |

---

## 14. Gate Readiness & Scheduling

`ApprovalGateReadinessEvaluator` is a pure-logic evaluator with no side effects:

| Method | Purpose |
|--------|---------|
| `EvaluateGates(plan)` | Returns readiness state for every pending gate. A gate is ready when all `AfterTaskIds` are complete or superseded. |
| `ComputeDownstreamFrontier(plan, gate)` | BFS expansion of `BeforeTaskIds` plus transitive dependents — the full set of tasks blocked by this gate. |
| `SelectNextUngatedTask(plan)` | Picks the next eligible task not behind any unapproved gate, in plan declaration order. |
| `ShouldStopForApproval(plan)` | Returns `true` when no ungated eligible work remains but ready gates exist with pending downstream tasks. |
| `GetReleasedTaskIds(plan, gateId)` | After approval, returns tasks in `BeforeTaskIds` whose non-gated dependencies are all satisfied — the set newly eligible for execution. |

---

## 15. Troubleshooting

### Approve button is disabled

- The card may be in the **updating state** (spinner overlay visible). Wait for the async operation to complete.
- The button is disabled after clicking to prevent double-submission.

### "Stale" rejection after clicking approve

- The plan was modified (new gate added, gate edited, or revision changed) between when the card was rendered and when you clicked.
- **Resolution:** The surface should automatically refresh. Review the updated evidence and click approve again.

### Notification sound plays but no card appears

- You are in the **early window**: the gate is ready but ungated work still remains. Execution has not stopped yet.
- The transcript card only appears when execution is **fully blocked** (no eligible ungated tasks).

### Inbox message shows "archived" but work is still pending

- All previously active gates were resolved, causing auto-archive.
- If a new gate activates later, the message will **automatically unarchive** and reappear as unread.

### Approval note not saved

- Notes are only persisted when approval succeeds (`Approved` result). If the click was `StaleRejected`, the note is discarded with the stale action.
- Re-enter your note after the surface refreshes.

### Missing commit evidence or changed files

- Commit evidence requires the commit SHA to be reachable in the local repository. If the branch was force-pushed or garbage-collected, evidence may be unavailable.
- The snapshot builder silently returns empty results for unreachable SHAs rather than failing.

### State appears inconsistent after crash

- On restart, `RestoreActivePlanIds()` rebuilds state from persisted Inbox messages. If the Inbox file was corrupted, the JSON deserializer logs a trace message (`TraceCategory.Inbox`) and skips that entry.
- Check `SquadDashTrace` output for `"Durable approval state could not be parsed"` messages.
