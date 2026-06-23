---
title: Code Health Panel
nav_order: 5
parent: Panels
---

# Code Health Panel

The Code Health Panel is your command centre for SquadDash's autonomous housekeeping system.It lists every task defined in `.squad/maintenance.md` (and any user-owned task files), lets you enable or disable individual tasks, adjust their run frequency and options, trigger an immediate run, and browse past maintenance reports — all without leaving the main window.

For full details on how Maintenance Mode works (idle detection, safety levels, reports, and the `INBOX_MESSAGE_JSON` format) see [Maintenance Mode](../features/maintenance-mode.md).

---

## Opening the Panel

**View** menu → **Maintenance** (toggles visibility). Visibility is persisted per workspace.

Close the panel with its **×** button.

---

## What the Panel Shows
the panel lists available code health, maintenance tasks. You can add your own tasks here or use the existing ones. And you can also edit the existing tasks by right clicking on one and selecting "Edit Task..."


![Screenshot: Code Health panel overview](images/code-health-panel-overview.png)
Tasks are listed in alphabetical order by title.

If you check the check box next to a code health task. You'll see frequency and safety information appear below the task. Frequency determines how often the task is run. Tasks are run either manually or during idle time when those frequency conditions have been satisfied. And safety. Is an indication of what kinds of changes would happen to the repository. If it says report, then no changes would occur. It's just going to create a report, and likely that'll come in the form of a message sent to your inbox. If it is branch, it'll create a brand new branch before making any changes, and if it is direct, that means it will make changes directly to the currently active branch.

---

## Panel Header

| Control | What it does |
|---------|-------------|
| **Maintenance Tasks:** picker | Toggles whether the idle cycle actually runs tasks. **Run on idle** lets tasks trigger automatically after the `idle_timeout` expires. **Manual runs only** suppresses the idle cycle. Right-click the picker to access **Simulate Idle**, which triggers a maintenance window immediately — frequency rules still apply. Persisted to `maintenance.md` as `enabled_on_idle:`. |
| **Filter box** | Hides tasks whose title and instructions do not match the entered text. Non-matching rows are collapsed instantly; the filter is cleared by pressing **×**. |
| **Status label** | Shows the current state: a countdown to the next scheduled window ("Next maintenance in: 14m 32s"), an active-run indicator ("● Running — Run Tests…"), or is hidden when idle. |

---

## Task Rows

Each task appears as a row with a checkbox on the left and details on the right.

### Checkbox (enable / disable)

The checkbox reflects the task's `enabled:` field in `maintenance.md`.

- **Checked** — task participates in idle cycles and can be run manually.
- **Unchecked** — task is skipped in all idle cycles and cannot be run via **Run Now**.

Clicking the checkbox flips `enabled: false ↔ true` in `.squad/maintenance.md` immediately and reloads the panel. Both methods (checkbox and direct file edit) are equivalent — the file is the single source of truth.

### Title

The task's `title:` value (or the task ID slug if no title is set). Disabled tasks are rendered at reduced opacity.

### Frequency picker (enabled tasks only)

A compact picker button next to the title lets you change how often the task runs without editing the file manually:

| Option | Behaviour |
|--------|-----------|
| **Always** | Runs every idle window, no cooldown |
| **Daily** | At most once per UTC calendar day |
| **Weekly** | At most once per Monday–Sunday UTC week |
| **Monthly** | At most once per calendar month |
| **After Commits** | Once per new HEAD commit SHA |

Changing the picker writes the new `frequency:` value back to `.squad/maintenance.md` immediately.

### Safety chip (enabled tasks only)

Displayed next to the frequency picker when the task's effective safety is not the default (`branch`):

| Safety level | Display |
|-------------|---------|
| `report-only` | Pill chip with subtle border — "report-only" |
| `branch` | No chip (this is the default; no indicator needed) |
| `direct` | Amber warning chip — "⚠ direct commits" |

Hover over any chip to see a tooltip explaining what the safety level permits.

> The *effective* safety is the stricter of the global floor (set in `.squad/maintenance.md` frontmatter) and the task's own setting. See [Safety model](../features/maintenance-mode.md#safety-model) for the full promotion rules.

### Last-run timestamp

If the task has run at least once, a small "Last run: *relative time*" line appears beneath the frequency picker.

### Options (enabled tasks only)

Tasks that define an `options:` block in `.squad/maintenance.md` surface those options as radio buttons or pickers directly in the row. Selecting a value writes the new setting back to the file immediately so the next run uses the updated value.

### Hover preview

Hovering over a task row that has an `instructions:` field opens a markdown popup showing the full instruction text, with the task title as the header. The popup appears to the right of the panel and disappears when the cursor leaves the row.

![Screenshot: Code Health hover popup](images/code-health-hover-popup.png)
> 📸 *Screenshot needed: Hover popup open over a task row, showing the task title as header and the first few lines of its instructions in rendered markdown.*

### Context menu (right-click a task row)

Right-clicking any task row opens a task-level context menu:

| Item | What it does |
|------|-------------|
| **Run Now** | Runs this specific task immediately, bypassing frequency history. Use this to force a task to execute right now regardless of when it last ran. |
| **Edit Task…** | Opens the task in the [Task Editor](#task-editor) where you can change the title, instructions, frequency, safety level, and enable/disable state. |

---

## Recent Reports

A collapsible **Recent Reports** section appears at the bottom of the task list, separated by a horizontal rule. It is collapsed by default.

Expanding it lists the 30 most recent maintenance reports, each shown as a one-line button:

```
2026-05-20 03:14  •  3 tasks
2026-05-19 02:51  •  2 tasks
```

Clicking a report row opens the corresponding `.squad/maintenance-reports/YYYYMMDD-HHmmss.md` file in your default markdown viewer.

---

## Panel Context Menu

Right-clicking on the panel background (not on a task row) opens a panel-level context menu:

| Item | What it does |
|------|-------------|
| **New Task** | Creates a new maintenance task in a user-owned file and opens it immediately in the [Task Editor](#task-editor). New tasks are saved separately from the default shipped tasks so your custom tasks are never overwritten by SquadDash updates. |
| **Edit Maintenance File** | Opens `.squad/maintenance.md` in the markdown editor. Use this to add new tasks, change the global safety floor, or adjust the `idle_timeout`. |

---

## Task Editor

The Task Editor is a dialog that lets you create and configure individual maintenance tasks without editing `.squad/maintenance.md` by hand.

Open it by:
- Right-clicking a task row → **Edit Task…**
- Right-clicking the panel background → **New Task** (creates a new task and opens the editor immediately)

### Fields

| Field | Description |
|-------|-------------|
| **Title** | Human-readable name shown in the panel and in maintenance reports. |
| **Enabled** | Whether this task participates in idle cycles. Equivalent to the row checkbox. |
| **Frequency** | How often the task runs. See [Frequency values](../features/maintenance-mode.md#frequency-values) for the full list. |
| **Safety** | Per-task safety level (`report-only`, `branch`, or `direct`). Cannot be less safe than the global floor. |
| **Instructions** | The prompt sent to the agent when this task runs. Supports Ctrl+scroll to zoom, voice dictation (double-tap Ctrl), and AI revision shortcuts (Ctrl+Shift+A to revise, Ctrl+Shift+C to clean up). |

Changes are saved back to the task's source file immediately when you click **Save**.

> **Multiple task files:** Tasks you create via **New Task** are written to a user-owned file separate from the default `.squad/maintenance.md`. This keeps your custom tasks safe from being overwritten when SquadDash ships updated default tasks. Both files are loaded and displayed together in the panel.

---

## "While You Were Away" Banner

After a maintenance cycle completes, a notification banner appears at the top of the main SquadDash window summarising what ran. The Code Health Panel simultaneously reflects the updated task state (last-run timestamps, any transient status messages).

---

## Tips

- Keep `safety: branch` as your global floor while getting started. Promote individual tasks to `direct` only when you are confident they will not make unintended edits.
- Right-click any task row and choose **Run Now** to force that task to execute immediately, bypassing its frequency history — useful for testing newly-created tasks.
- Right-click the **Maintenance Tasks:** picker and choose **Simulate Idle** (or type `trigger_idle_cycle` in the prompt box) to simulate an idle window without waiting for the real timeout.
- If you normally use **Manual runs only** but want a single maintenance cycle when you finish your next prompt, type `/maintenance` in the prompt box. The cycle fires at the next true idle point without changing the panel's mode setting. See [/maintenance — Run once on idle](../features/maintenance-mode.md#maintenance--run-once-on-idle) for details.
- If a task's `after-commits` frequency never seems to trigger, check `maintenance-state.json` in the workspace root — delete the file to reset all run history.

---

## Related

- **[Maintenance Mode](../features/maintenance-mode.md)** — Full reference: `.squad/maintenance.md` format, safety model, frequency values, reports, inbox messages, and template preprocessing
- **[Inbox Panel](Inbox.md)** — Read messages sent by maintenance tasks using `INBOX_MESSAGE_JSON`
- **[Loop Panel](Loop.md)** — Run agents on demand or on a schedule
