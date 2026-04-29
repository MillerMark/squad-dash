# Loop Panel

The **Loop Panel** lets you run SquadDash's native loop mode without touching the command line. Set an interval and timeout in `loop.md`, then start and stop the loop from the toolbar.

> **Note:** The Loop Panel drives SquadDash's own loop mechanism — not the `squad` CLI's loop mode. Loop iterations are executed directly by SquadDash agents.

---

## Opening the Loop Panel

Click the **loop icon** in the toolbar to show or hide the Loop Panel. It can also be toggled from the **View** menu.

![Screenshot: Loop panel in the toolbar area](images/loop-panel-toolbar.png)
> 📸 *Screenshot needed: The SquadDash main window with the Loop Panel visible — show the panel with status indicator, iteration counter, and Start/Stop/Edit buttons. Toolbar toggle button should be visible.*

---

## Loop Panel Controls

| Control | Description |
|---|---|
| **Status indicator** | Shows `Running` or `Stopped` |
| **Iteration counter** | Current iteration number (e.g., `Iteration 3`) |
| **Start** button | Begins the loop |
| **Stop** button | Halts the loop after the current iteration finishes |
| **✏️ Edit** button | Opens `loop.md` in the doc editor (creates the file if it doesn't exist) |

![Screenshot: Loop panel controls close-up](images/loop-panel-controls.png)
> 📸 *Screenshot needed: Close-up of the Loop Panel — show the status indicator (Running or Stopped), the iteration number, and all three buttons (Start, Stop, Edit pencil icon). Ideally capture it mid-run with a non-zero iteration count.*

---

## Configuring the Loop with `loop.md`

The loop's timing is controlled by a frontmatter block in `loop.md` at the workspace root. Click **✏️** in the panel to open or create this file.

### Frontmatter Fields

```yaml
---
interval: 30
timeout: 10
---
```

| Field | Type | Description |
|---|---|---|
| `interval` | integer (minutes) | How long to wait between the end of one iteration and the start of the next |
| `timeout` | integer (minutes) | Maximum time allowed for a single iteration before it is forcibly stopped |

### Example `loop.md`

```markdown
---
interval: 15
timeout: 5
---

# Loop Instructions

Each iteration, check `.squad/tasks.md` for the highest-priority unclaimed task and begin working on it.
Complete the task or make measurable progress, then commit your changes.
```

The body of `loop.md` is the prompt (or instructions) sent to the agents at the start of each iteration. Keep it focused and actionable.

---

## Starting a Loop

1. Open `loop.md` via the **✏️** button and configure `interval` and `timeout`.
2. Write the loop instructions in the body of `loop.md`.
3. Click **Start** in the Loop Panel.

The status indicator changes to `Running` and the iteration counter increments each cycle.

---

## Stopping a Loop

Click **Stop**. The current iteration completes normally before the loop halts. The status indicator returns to `Stopped`.

---

## Related

- **[Tasks Panel](tasks-panel.md)** — Surface the `.squad/tasks.md` backlog alongside the loop
- **[Keyboard Shortcuts](../reference/keyboard-shortcuts.md)** — Toolbar toggle shortcuts
- **[Transcripts](../concepts/transcripts.md)** — Loop iteration output appears in the agent transcript
