# Using the Trace Log

The **Trace Log** is a floating window that streams internal SquadDash events in real time. It is the primary tool for diagnosing runtime behaviour: scroll decisions, routing, agent thread lifecycle, docking layout, callout geometry, and more.

---

## Opening the Trace Window

**Via the menu:** Developer → Show Trace Window

**Via slash command:** type `/trace` in the prompt box.

The window is non-modal and stays on top of other SquadDash content. It does not appear in the taskbar. Close it with the `×` button — tracing has zero overhead when the window is closed.

---

## Trace Entry Format

Each entry in the log follows this format:

```
2024-06-15 09:42:17.381 +00:00 [Category] message text here
```

| Field | Description |
|---|---|
| Timestamp | Date, time (millisecond precision), and UTC offset |
| `[Category]` | The subsystem that generated the entry (see categories below) |
| Message | Human-readable description of the event |

---

## Trace Categories

The window shows a checkbox for each category. Uncheck a category to hide its entries without losing them from the file log.

| Category | What it covers |
|---|---|
| `Scroll` | Transcript scroll controller decisions (auto-scroll, lock, threshold) |
| `PromptHealth` | Prompt validation and health checks |
| `UI` | General UI events — window lifecycle, menu actions, layout |
| `Bridge` | Communication between SquadDash and the Squad process |
| `Load` | Workspace and file loading |
| `Performance` | Timing and latency measurements |
| `AgentCards` | Agent card rendering and state changes |
| `Routing` | Prompt and response routing decisions |
| `Shutdown` | Application and workspace teardown |
| `Startup` | Application and workspace startup |
| `Threads` | Agent transcript thread creation and management |
| `TranscriptPanels` | Transcript panel layout and switching |
| `Unhandled` | Unhandled exceptions and unexpected states |
| `Workspace` | Workspace switching and ownership |
| `Sound` | Notification sound playback |
| `Inbox` | Inbox panel events |
| `Docking` | Panel docking — slot hover preview, zone rect calculations |
| `Callouts` | Callout shape geometry — triangle points, dangle side, placement angle |
| `ImageEditor` | Clipboard image annotation editor — zoom, scroll, layout |
| `General` | Catch-all for events that don't fit another category |

---

## Log File

Trace entries are also written to a file:

- **Per-workspace:** `<workspace>/.squad/state/trace.log`  
- **Global (before a workspace loads):** a global log path in the application data folder

The file rotates automatically when it exceeds 32 MB.

---

## Practical Examples

### Diagnosing a docking layout issue

Enable the `Docking` category and right-click a panel to trigger a docking map rebuild. Look for entries tagged `[build-side-seq]`:

```
[Docking] === Starting BuildSideSequence for Right side ===
[Docking] Available zones on Right: [Right(Tier=0,Occ=T,Supp=F), Right2(Tier=1,Occ=T,Supp=F)]
[Docking] Adjacency between Right@Tier0 and Right2@Tier1:
[Docking]   Final decision: INCLUDE thin (both occupied + adjacent)
```

### Diagnosing guided tour step transitions

Enable `UI` and `Callouts`. Start a tour and step through it. You'll see entries when the callout opens, repositions, and closes:

```
[Callouts] Callout placed: target=PromptTextBox placement=North
[UI] Tour step advanced: index=3 title="The Prompt Box"
```

If a step never advances, look for advance trigger entries — a missing `MenuOpened` or `QuickReplySelected` entry means the trigger never fired.

### Diagnosing scroll lock behaviour

Enable `Scroll`. Scroll the transcript manually and watch for lock/unlock entries:

```
[Scroll] Auto-scroll locked (user scrolled up) thread=coordinator
[Scroll] Auto-scroll unlocked (scrolled to bottom) thread=coordinator
```

---

## Tips

- Use the **Clear** button in the trace window to discard old entries before reproducing a specific issue.
- The trace window does not block the UI — you can interact with SquadDash normally while it is open.
- Entries are capped at 16,000 characters each; very long messages are truncated with a note.
- The window holds up to 250,000 characters of recent log text before it starts trimming from the top.
