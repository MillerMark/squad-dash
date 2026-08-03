---
title: Plans
nav_order: 12
parent: Features
---

# Plans

## What Plans Are

Plans are SquadDash's system of record for multi-step AI-executed work. A Plan is a durable, versioned DAG (directed acyclic graph) of tasks with approval gates, lifecycle state, and commit evidence. Unlike a transcript (conversation history) or inbox message (notification), a Plan persists across restarts and links all related work to a stable Plan ID stored at `.squad/plans/{planId}.json`.

Plans survive process restarts, give you a clear audit trail of what was done and why, and let SquadDash resume interrupted work from exactly where it left off.

---

## The Surface Roles

SquadDash surfaces plan information across several panels. Each has a distinct responsibility:

| Surface | Role |
|---|---|
| **Transcript** | Where plans are created and discussed. Plan-creation intent (via `/plan` or natural language) is detected here and triggers the plan-proposal flow. |
| **Inbox** | Attention surface. Staged plan proposals, gate approval requests, and recovery alerts appear as inbox messages with action buttons. |
| **Tasks Panel** | Actionable-work projection showing the `.squad/tasks.md` backlog of open items — not the plan graph itself. |
| **Plans Panel** | System of record. Shows live plan status, progress bar, approval gate state, and provides access to the Plan Viewer. |
| **Code Health** | Separate quality cycle. Not part of plan execution. |

---

## Creating a Plan

Use the `/plan` slash command or describe a multi-step feature in plain language in the Transcript. SquadDash detects plan-creation intent and calls the AI coordinator to propose a structured TASKS_JSON plan. The proposal appears in the Inbox as a staged message where you can approve or modify it before execution begins.

**Natural-language examples that trigger plan detection:**
- "Build the user authentication system end to end"
- "Refactor the data layer, add tests, and update the documentation"
- "Implement the Plans panel: model, store, UI, and docs"

---

## Collecting a Plan (Add to Plans)

Instead of executing a plan immediately, you can **collect** it — saving the plan to the Plans panel without starting work. This lets you review, reorder, or delay execution at your discretion.

**How to collect:**
1. A plan proposal arrives in the Inbox.
2. Click the **Add to Plans** action button.
3. The plan transitions from a transient Inbox proposal to a durable `Approved` record in `.squad/plans/{planId}.json`.
4. The plan row appears in the Plans panel with an ✅ Approved badge, ready to start whenever you choose.

**What collection does _not_ do:**
- Does not switch branches, start the loop, or write `.squad/tasks.md`.
- Does not modify any files in the worktree.

**Idempotency:** Clicking "Add to Plans" on an already-collected plan is a safe no-op. Stale Inbox actions against a newer plan revision are silently rejected.

**Starting or resuming later:**
- Right-click an Approved plan in the Plans panel → **Start Plan**, or open the Plan Viewer and click **Start Plan**.
- Right-click an Interrupted plan → **Resume Plan**, or use the **Resume Plan** button in the Plan Viewer.

---

## Plan Lifecycle

Each plan moves through a defined set of lifecycle statuses:

| Status | Icon | Meaning |
|---|---|---|
| Staged | 📋 | Proposed; awaiting user approval in the Inbox |
| Approved | ✅ | Accepted; ready to execute |
| Executing | ▶ | Loop actively running tasks |
| Awaiting Approval | ⏸ | Paused at a human gate; no tasks running |
| Interrupted | ⚠ | Stopped unexpectedly; recovery options available |
| Stopped | ⏹ | Ended by user; task history preserved, no further recovery prompts |
| Completed | ✓ | All tasks finished successfully |
| Archived | 📁 | Hidden from active lists; history preserved |
| Blocked | ✖ | One or more tasks failed; plan paused pending user action |

**Terminal statuses** (cannot resume): `Stopped`, `Completed`, `Archived`.

**Recoverable statuses**: `Interrupted`, `Blocked`, `AwaitingApproval` — execution can continue from these states.

---

## Approval Gates

Gates are human checkpoints between task groups. When all tasks before a gate complete, execution pauses automatically. SquadDash:

- Shows an ⏸ badge in the Plans panel
- Sends a push notification (if configured under **Options → Notifications**)
- Plays the "Approval Needed" sound
- Adds an **Approve & Continue Plan** button to the transcript
- Shows the gate status in the Plan Viewer with a colored badge (🔒 pending, ⏸ awaiting approval, ✓ approved)

**To add or remove gates:** right-click any task node in the Plan Viewer and choose **Require approval before this task** or **Require approval after this task**.

**To approve a gate (three ways):**
1. Click the **Approve & Continue** button in the transcript message.
2. Open the Plan Viewer and click **Approve & Continue**.
3. Right-click the plan row in the Plans panel and choose **Approve & Continue**.

A plan with multiple simultaneous gates awaiting approval remains in `Awaiting Approval` until all active gates are resolved.

---

## Interruption and Recovery

When a plan stops unexpectedly (error, process restart, or manual stop):

1. Status transitions to `Interrupted`.
2. The interruption reason, loop iteration, affected files, and last commit are recorded in the plan's `interruptionData`.
3. Recovery options appear in the transcript:
   - **Replan Failed Task** — replace the failed task with a revised plan
   - **Continue / Retry Task** — retry as-written from the last known state
   - **Analyze with AI** — gather evidence and propose the best recovery action

**AI-assisted recovery** inspects the worktree, commit history, and partial work evidence, then proposes one of:
- Adopt orphan commit (work was done but not recorded)
- Partial adopt (merge verified work from an orphaned commit)
- Revert and retry (undo problematic changes and start clean)
- Clean retry (worktree is already clean; retry immediately)
- Replan (rewrite the task to take a different approach)

**Resume or end** from the Plans panel or Plan Viewer using the **Resume Plan** or **End Plan** buttons.

---

## The Plan Viewer

Click any plan row in the Plans panel to open the Plan Viewer:

- **Dependency graph** with task nodes showing live status:
  - 🟢 Green = Complete (shows 7-char commit SHA)
  - 🟠 Orange = Partial
  - 🔴 Red = Failed
  - 🔵 Blue = Executing
  - ⬜ Grey = Pending
- **Approval gate badges** on graph edges (🔒 pending, ⏸ awaiting, ✓ approved)
- **Plan metadata header**: Plan ID, source branch, creation timestamp, source type
- **Interruption detail panel** when status is `Interrupted` — shows reason, affected files, and last commit
- **Lifecycle-appropriate action buttons** (e.g., Approve & Continue, Resume, End Plan)

![Screenshot: Plans panel showing active plan with progress bar](images/plans-panel-active.png)
> 📸 *Screenshot needed: Plans panel with one active plan showing Executing status and progress bar.*

![Screenshot: Plan Viewer showing dependency graph with status-colored nodes](images/plan-viewer-graph.png)
> 📸 *Screenshot needed: Plan Viewer open, showing task nodes in various status colors, an approval gate badge, and the metadata header.*

---

## Where Plans Are Stored

| Path | Contents |
|---|---|
| `.squad/plans/{planId}.json` | **Authoritative** durable plan record (lifecycle status, tasks, gates, timestamps) |
| `.squad/plans/pending/{planId}.json` | Staged proposal before user approval |
| `.squad/tasks.md` | Projection of the active plan during execution — never commit this manually |

> **Source of truth:** The durable Plan record (`.squad/plans/{planId}.json`) is always authoritative. `.squad/tasks.md` is a host-managed projection written during execution and should not be treated as the canonical plan state. Agents never edit `.squad/tasks.md` — it is host-owned.

> **Important:** Do not commit `.squad/tasks.md` during plan execution. SquadDash manages all task status markers. Committing it externally will cause false dirty-worktree warnings that SquadDash must reconcile.

---

## Locked Historical Controls

Once a task completes or a gate is traversed, its approval controls in the Plan Viewer become read-only. This prevents retroactive edits to work that has already been verified and committed.

| Control | Locked when |
|---|---|
| Task-entry gate (before a task) | Task has started or completed |
| Task-exit gate (after a task) | Task has completed |
| Stage milestone | Gate traversed, or all upstream tasks complete and downstream work has begun |
| ALL-join gate | All inbound tasks have completed |

Locked controls display a tooltip explaining the lock — for example, _"Task entry — completed work cannot be modified."_ Plans that have not yet started execution have no execution locks.

---

## Preflight and Branch Checks

Before switching to a plan's target branch, SquadDash verifies the worktree is clean:

- If uncommitted changes are detected on files that are **not** host-owned (`.squad/tasks.md`, plan logs, metadata), a **"Changes Blocking Branch Switch"** dialog appears listing the affected files. Commit or stash the changes, then retry.
- Host-owned files and metadata-only changes never trigger this block.
- If the check passes, SquadDash proceeds automatically with the branch switch.

---

## Loop Execution Details

### Execution cadence

The plan loop runs every 6 seconds by default (`interval: 0.1` minutes in `.squad/loop-executing-plan.md`). Each round the Loop panel updates to show:

- **Plan title** — the name of the currently executing plan
- **Current task** — the task ID being worked on this round
- **Round elapsed time** — how long the current round has been running
- **Total active time** — cumulative time since the plan started executing

### Polling coalescing

When the AI coordinator polls a background agent multiple times in a row for the same agent ID, SquadDash coalesces those polling events into a single 📡 satellite item in the tool transcript. This reduces visual noise without losing information — the item shows the total poll count.

### Active worker visibility

Spawned agent cards remain visible through their terminal states (Complete, Lost, Aborted, etc.). You can always see what each background worker finished doing, even after it exits.

### Step result repair

If a worker completes normally but omits the required `DECOMPOSE_STEP_RESULT_JSON` envelope, SquadDash automatically issues one bounded hidden repair prompt. The transcript shows:

> ⚙ SquadDash is requesting the missing result envelope…

If the repair turn also fails to produce a valid envelope, the plan transitions to `Blocked` for manual recovery.

### Execution history

Every completed plan round is appended to `.squad/logs/plan-execution.ndjson` (NDJSON format, append-only). The file is automatically trimmed to the 500 most recent entries. Open it via the Loop Output window context menu → **Open Execution Log**.

---

## Related

- **[Inbox Panel](inbox.md)** — Where plan proposals and gate alerts arrive
- **[Tasks Panel](../panels/Tasks.md)** — The actionable task backlog surface
- **[Slash Commands](../reference/slash-commands.md)** — `/plan` command reference
- **[Human Approval Workflow](../human-approval-workflow.md)** — Gate editing, versioned approval tokens, and cross-surface coordination

---

## Known Limitations

- **Single active plan per workspace.** Only one plan can be Executing at a time. Collecting multiple plans is supported; starting a second plan while one is executing is blocked.
- **No drag-and-drop reorder.** Collected plans in the Plans panel cannot be reordered visually; they sort by creation timestamp.
- **No partial collection.** "Add to Plans" collects the entire plan. You cannot collect a subset of tasks from a single proposal.
- **Stale Inbox actions.** If the plan is revised between proposal and collection, the original Inbox "Add to Plans" button silently rejects. The user must re-open the updated proposal.
- **No undo after Start Plan.** Once a plan transitions from Approved → Executing, it cannot be returned to Approved without interruption.

---

## Manual Verification Checklist

Use this checklist to verify the collected-plan workflow end-to-end:

1. **Create a plan proposal** — Send a multi-step prompt (e.g., `/plan refactor auth, add tests, update docs`). Verify the Inbox shows a staged plan with **Add to Backlog** and **Add to Plans** action buttons.
2. **Collect the plan** — Click **Add to Plans**. Verify:
   - The plan row appears in the Plans panel with ✅ Approved status.
   - No branch switch occurred. No loop started. `.squad/tasks.md` was not written.
   - The durable plan file exists at `.squad/plans/{planId}.json`.
3. **Idempotent re-collect** — Click **Add to Plans** again on the same Inbox message. Verify no error and no duplicate plan row.
4. **Open the Plan Viewer** — Click the plan row in the Plans panel. Verify the dependency graph renders with all tasks in Pending status. Verify approval-gate controls are editable (no lock icons).
5. **Start the plan** — Right-click the plan row → **Start Plan** (or click **Start Plan** in the Plan Viewer). Verify:
   - Status transitions to ▶ Executing.
   - The loop begins running tasks.
   - `.squad/tasks.md` is now written.
   - Progress updates appear live in the Plans panel.
6. **Verify live sync** — Open the Plan Viewer while a plan is executing. Verify task nodes update in real time as steps complete (green nodes, commit SHAs).
7. **Verify locked controls** — After at least one task completes, verify that its entry/exit gate controls in the Plan Viewer show lock icons and the tooltip reads _"… — completed work cannot be modified."_
8. **Interrupt and resume** — Stop the app mid-execution. Restart. Verify the plan shows ⚠ Interrupted. Click **Resume Plan**. Verify execution resumes from where it left off.
9. **End a plan** — On an interrupted plan, click **End Plan**. Verify status transitions to ⏹ Stopped and the plan cannot be resumed.
