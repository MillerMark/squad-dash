---
title: Loop Panel
nav_order: 2
parent: Panels
---

# Loop Panel

The Loop Panel lets you run SquadDash agents in a repeating loop — executing a prompt on a schedule — without staying at the keyboard. You configure the iteration interval, per-iteration timeout, and the instructions body in a `loop.md` file at the workspace root; the panel shows live status and lets you start, stop, or abort the loop at any time.

> **Note:** The Loop Panel drives SquadDash's own *native loop* mechanism (agents run inside SquadDash) or the `squad` CLI's loop mode, depending on which radio button is selected. These are two distinct execution paths.

---

## Opening the Panel

**View** menu → **Loop Panel** (toggles visibility).

The panel is shown by default. Close it with its **×** button; reopen via the View menu. Visibility is persisted per workspace.

---

## Controls

| Control | Description |
|---|---|
| **Native Agents** / **Squad CLI** radio | Switch between SquadDash's built-in loop engine and the Squad CLI loop runner. Cannot be changed while the loop is running. |
| **Continuous Context** checkbox | When unchecked (default), each iteration runs in a fresh session so agent state does not accumulate across rounds. Check it to reuse the same conversation session across all iterations. |
| **Start Loop** / **Queue Loop** | Starts the loop. In Native Agents mode, if the coordinator is currently busy, the button becomes **Queue Loop** and auto-starts once the current prompt finishes. |
| **Stop** | Graceful stop — the current iteration finishes normally, then the loop halts. |
| **Abort** | Immediate stop — cancels the running prompt right now. Only visible while the loop is running. |
| **Status label** | Live status: `● Running · Round N`, `⏳ Waiting · next in Xm`, `◌ Stopping after this iteration…`, `⏸ Loop queued — waiting for coordinator`. |

---

## Configuring the Loop with `loop.md`

The loop reads its timing and instructions from `loop.md` in the workspace root. You can open or create this file from the **✏️ Edit** button in the panel (if present), or create it manually.

### Required frontmatter

The file **must** include `configured: true` in the frontmatter or the parser treats it as unconfigured and the loop will not start.

```yaml
---
configured: true
interval: 30
timeout: 10
description: "Nightly review loop"
commands: [stop_loop]
---
```

### Frontmatter fields

| Field | Type | Default | Description |
|---|---|---|---|
| `configured` | `true` / `false` | — | **Required.** Must be `true` to enable the loop. |
| `interval` | number (minutes) | `10` | Wait time between the end of one iteration and the start of the next. |
| `timeout` | number (minutes) | `5` | Maximum wall-clock time per iteration before it is forcibly aborted. |
| `description` | string | `""` | Human-readable label shown in the UI. |
| `commands` | list | `[]` | SquadDash commands the agent is allowed to invoke this iteration (see below). |

### Body = loop instructions

Everything after the closing `---` is the prompt sent to the agent at the start of each iteration.

```markdown
---
configured: true
interval: 15
timeout: 8
commands: [stop_loop]
---

Check `.squad/tasks.md` for the highest-priority open task. Work on it, commit
progress, and mark the task done. If no open tasks remain, stop the loop.
```

---

## Agent-Initiated Commands

If `commands` lists `stop_loop` (or `start_loop`), SquadDash appends a reference block to the prompt explaining how to use them. The agent invokes a command by appending a `HOST_COMMAND_JSON` block at the very end of its response:

```
HOST_COMMAND_JSON:
[
  { "command": "stop_loop" }
]
```

This block must be the **last content** in the response — nothing may follow it. SquadDash only executes the command if the block is correctly terminated with no trailing text.

Only emit a command when the condition is actually met — the instructions SquadDash injects say exactly this.

---

## Loop Output Pane

A scrollable log below the controls records lifecycle events for the current session: iteration start/end, timeout errors, wait countdowns, and abort notices. This log is in-memory only and resets when SquadDash restarts.

Agent output for each iteration appears in the **coordinator transcript** in the main panel, not in the loop output pane.

---

## Tips

- Set `timeout` shorter than `interval` so iterations cannot overlap.
- Leave **Continuous Context** unchecked unless the agent explicitly needs memory of previous iterations — accumulated context slows responses and can confuse the agent.
- Sending a prompt from the main prompt box while the native loop is running pauses the loop, drains the queue, then resumes — the status label shows `🔁 Queue items pending — loop will resume after queue drains.`
- The `stop_loop` command is the cleanest way to have the agent self-terminate when its work is done.

---

## Related

- **[Tasks Panel](Tasks.md)** — Surface the `.squad/tasks.md` backlog alongside the loop
- **[Prompt Queue](../features/prompt-queue.md)** — How prompts queue and interact with loop iterations
- **[Keyboard Shortcuts](../reference/keyboard-shortcuts.md)** — Global shortcuts reference
