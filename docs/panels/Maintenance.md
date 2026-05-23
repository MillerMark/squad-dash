---
title: Maintenance Panel
nav_order: 5
parent: Panels
---

# Maintenance Panel

The Maintenance Panel is your command centre for SquadDash's autonomous housekeeping system. It lists every task defined in `.squad/maintenance.md`, lets you enable or disable individual tasks, adjust their run frequency and options, trigger an immediate run, and browse past maintenance reports — all without leaving the main window.

For full details on how Maintenance Mode works (idle detection, safety levels, reports, and the `INBOX_MESSAGE_JSON` format) see [Maintenance Mode](../features/maintenance-mode.md).

---

## Opening the Panel

**View** menu → **Maintenance** (toggles visibility). Visibility is persisted per workspace.

Close the panel with its **×** button.

---

## What the Panel Shows

```
┌─ Maintenance ──────────────────────────── [Run now] [×] ─┐
│  Maintenance Tasks: ▾ (run manually)                      │
│  ● Running — Run Tests…                                   │
│  ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄   │
│  ☐  Code Smell Cleanup                                    │
│  ☑  Run Tests                         Daily ▾             │
│     Last run: 3h ago                                      │
│  ☑  TODO / FIXME Scanner              Daily ▾  ⚠ direct   │
│     Last run: yesterday                                   │
│  ▸ Recent Reports                                         │
└───────────────────────────────────────────────────────────┘
```

> 📸 *Screenshot needed: Maintenance panel with two or three tasks, one enabled (showing frequency picker and last-run line), one disabled (grayed). Recent Reports expander visible at the bottom.*

Tasks are loaded from `.squad/maintenance.md` and listed in alphabetical order by title.

---

## Panel Header

| Control | What it does |
|---------|-------------|
| **Maintenance Tasks:** picker | Toggles whether the idle cycle actually runs tasks. **Run on idle** lets tasks trigger automatically after the `idle_timeout` expires. **Manual runs only** suppresses the idle cycle — tasks only run when you click **Run now**. Persisted to `maintenance.md` as `enabled_on_idle:`. |
| **Run now** button | Triggers an immediate maintenance run for all enabled tasks, regardless of frequency history. Disabled while a run is in progress. |
| **Filter box** | Hides tasks whose title and instructions do not match the entered text. Non-matching rows are collapsed instantly; the filter is cleared by pressing **×**. |
| **Status label** | Shows the current state: a countdown to the next scheduled window ("Next maintenance in: 14m 32s"), an active-run indicator ("● Running — Run Tests…"), or is hidden when idle. |

---

## Task Rows

Each task appears as a row with a checkbox on the left and details on the right.

### Checkbox (enable / disable)

The checkbox reflects the task's `enabled:` field in `maintenance.md`.

- **Checked** — task participates in idle cycles and can be run manually.
- **Unchecked** — task is skipped in all idle cycles; **Run now** will not execute it.

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

> 📸 *Screenshot needed: Hover popup open over a task row, showing the task title as header and the first few lines of its instructions in rendered markdown.*

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

## Context Menu

Right-clicking anywhere on the panel background (not on a task row) opens a panel-level context menu:

| Item | What it does |
|------|-------------|
| **Edit Maintenance File** | Opens `.squad/maintenance.md` in the markdown editor. Use this to add new tasks, change the global safety floor, or adjust the `idle_timeout`. |

---

## "While You Were Away" Banner

After a maintenance cycle completes, a notification banner appears at the top of the main SquadDash window summarising what ran. The Maintenance Panel simultaneously reflects the updated task state (last-run timestamps, any transient status messages).

---

## Tips

- Keep `safety: branch` as your global floor while getting started. Promote individual tasks to `direct` only when you are confident they will not make unintended edits.
- Use the **Run now** button to test a newly-enabled task immediately rather than waiting for the next idle window.
- Type `trigger_idle_cycle` in the prompt box to simulate an idle window without waiting for the real timeout.
- If a task's `after-commits` frequency never seems to trigger, check `maintenance-state.json` in the workspace root — delete the file to reset all run history.

---

## Related

- **[Maintenance Mode](../features/maintenance-mode.md)** — Full reference: `.squad/maintenance.md` format, safety model, frequency values, reports, inbox messages, and template preprocessing
- **[Inbox Panel](Inbox.md)** — Read messages sent by maintenance tasks using `INBOX_MESSAGE_JSON`
- **[Loop Panel](Loop.md)** — Run agents on demand or on a schedule
