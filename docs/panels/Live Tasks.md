---
title: Live Tasks Window
nav_order: 2
parent: Panels
---

# Live Tasks Window

The Live Tasks window is a compact floating overlay that summarises the current background-task queue and open squad tasks. Open it any time with the `/tasks` slash command; it stays open on top of the SquadDash window until you close it or type `/dropTasks`.

---

## Opening and Closing

| Action | What it does |
|---|---|
| `/tasks` in the prompt box | Opens (or brings to front) the Live Tasks window and refreshes its content. |
| `/dropTasks` | Closes the window. |
| **✕** button | Closes the window directly. |

> **Tip:** The window reopens automatically at its last position the next time you use `/tasks`.

---

## Content

The window shows a formatted snapshot of running background tasks and the open items from `.squad/tasks.md`. Priority groups are colour-coded with the same indicators used in the Tasks panel (◆ Critical · ▲ High · ● Mid · ▾ Low).

A **Copy** button in the header copies the raw text content to the clipboard.

![Screenshot: Live Tasks window open showing task summary](images/live-tasks-window.png)
> 📸 *Screenshot needed: The Live Tasks floating window — show the title bar, Copy button, a couple of priority group headings with task items, and the Watch Health section at the bottom (collapsed or expanded).*

---

## Watch Health

The bottom of the Live Tasks window contains a collapsible **Watch Health** section that surfaces the `squad watch` CLI process. It lets you monitor and control workspace health directly from the `/tasks` overlay.

![Screenshot: Watch Health section expanded in the Live Tasks window](images/live-tasks-watch-health.png)
> 📸 *Screenshot needed: The Watch Health section expanded inside the Live Tasks window — show the header row with chevron (▼), a blue status dot, the "Watch Health" label, and a recent timestamp; below it the Refresh / Copy / Start / Stop buttons, the Interval / Execute / Notify controls, and several lines of scrollable health output.*

---

### Header row

The header row is always visible, even when the section is collapsed.

| Element | Description |
|---|---|
| **▶ / ▼ chevron** | Indicates collapsed (▶) or expanded (▼) state. Click anywhere on the header to toggle. |
| **Status dot** | Colored circle: active blue = watch is running, muted/subtle = stopped, red = error. |
| **"Watch Health" label** | Static section title. |
| **Timestamp** | Time of the most recent health check, e.g. `14:22:08`. Updates after each refresh. |

---

### Controls

The following buttons appear when the section is expanded:

| Button | What it does |
|---|---|
| **Refresh** | Re-runs `squad watch --health` and updates the output area with the latest results. |
| **Copy** | Copies the current health output lines to the clipboard. |
| **Start** | Starts `squad watch` using the configured Interval, Execute, and Notify options. |
| **Stop** | Kills the running watch process. |

---

### Options

The following options are visible when the section is expanded and are **disabled while a watch is running**:

| Option | Type | Default | Description |
|---|---|---|---|
| **Interval** | Number field | `5` | Minutes between watch cycles. |
| **Execute** | Checkbox | unchecked | When checked, passes the `--execute` flag to `squad watch`. |
| **Notify** | Combo box | `important` | Controls notification verbosity. Values: `all`, `important`, `none`. |

---

### Auto-refresh

While a watch process is running, the output area refreshes automatically every **15 seconds** — no manual Refresh click required.

---

### Collapse state persistence

The expanded or collapsed state of the Watch Health section is saved per workspace and restored the next time the Live Tasks window is opened.

---

## Related

- **[Tasks Panel](Tasks.md)** — Sidebar panel showing the full task backlog
- **[Slash Commands](../reference/slash-commands.md)** — `/tasks`, `/dropTasks`, and other slash commands
- **[Loop Panel](Loop.md)** — Run agents in a loop to work through the task backlog automatically
