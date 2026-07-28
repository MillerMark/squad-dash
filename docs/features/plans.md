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
| `.squad/plans/{planId}.json` | Durable plan record (lifecycle status, tasks, gates, timestamps) |
| `.squad/plans/pending/{planId}.json` | Staged proposal before user approval |
| `.squad/tasks.md` | Live task backlog owned by SquadDash during execution — never commit this manually |

> **Important:** Do not commit `.squad/tasks.md` during plan execution. SquadDash manages all task status markers. Committing it externally will cause false dirty-worktree warnings that SquadDash must reconcile.

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
