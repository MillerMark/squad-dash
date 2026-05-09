---
title: Prompt Queue
nav_order: 4
parent: Features
---

# Prompt Queue

When you submit a prompt while an agent is still running, it doesn't get lost — it enters the **Prompt Queue**. Queued prompts wait their turn and dispatch automatically when the current agent turn completes.

---

## How the Queue Works

1. You type a prompt and press Send while the agent is mid-run.
2. The prompt enters the queue instead of interrupting the running turn.
3. When the current turn finishes, the first queued item dispatches automatically.
4. The cycle repeats until the queue is empty.

![Screenshot: Prompt queue tabs showing multiple queued items](images/prompt-queue-tabs.png)
> 📸 *Screenshot needed: The prompt area with two or more queue tabs visible (#1, #2, etc.) — show the tab labels and, ideally, one tab selected with its editable text visible.*

---

## Queue Tabs

Each queued item appears as a numbered tab: **#1**, **#2**, and so on.

- Click a tab to view and **edit** the queued prompt before it dispatches.
- Edits are applied when the item eventually runs — you can refine a queued prompt at any time before its turn.
- Tabs are removed as items dispatch.

---

## Navigating Between Queued Prompts

You can move between queue tabs without reaching for the mouse:

| Shortcut | Action |
|---|---|
| **Ctrl+Tab** | Cycle to the next queue tab |
| **Ctrl+Shift+Tab** | Cycle to the previous queue tab |

These shortcuts require at least two queued items to be present. Clicking a tab directly also selects it and loads its text into the prompt box for editing.

![Screenshot: Queue tab navigation with multiple tabs](images/prompt-queue-tab-nav.png)
> 📸 *Screenshot needed: Three or more queue tabs visible with one tab highlighted/selected — ideally while an agent is mid-run so all tabs are visible simultaneously.*

---

## Editing a Queued Prompt

Click any queue tab (or navigate to it with Ctrl+Tab) to open the prompt text for that item. Edit freely — the change takes effect when that item reaches the front of the queue.

- You can edit any queued item at any time, **including while the queue is paused**.
- Attachments (images, follow-up pills) added before queuing are preserved with the item.

![Screenshot: Editing a queued prompt in its tab](images/prompt-queue-edit.png)
> 📸 *Screenshot needed: A queue tab selected with the prompt text visible in an editable state — show the tab active and the text area with some content.*

---

## Prioritising a Queued Prompt: Ctrl+Enter

Press **Ctrl+Enter** to move the currently viewed item to the front of the queue (position #1), so it dispatches next.

- **On the Active Draft tab** — enqueues the current draft at the front of the queue, clears the text box, and queues it as #1. Only works when there are already other items in the queue.
- **On a queued tab (#2, #3, …)** — moves that item to position #1. No-op if it is already first.

After the move, a transient **"« Now at the front of the queue."** label appears on the tab for a few seconds confirming the reorder. The queue is renumbered automatically.

The prompt bar also surfaces a hint — *"Ctrl+Enter moves this to the front of the queue."* — whenever the shortcut would have a meaningful effect.

![Screenshot: Priority feedback label after Ctrl+Enter](images/prompt-queue-priority-feedback.png)
> 📸 *Screenshot needed: A queue tab that has just been prioritised — show the "« Now at the front of the queue." transient label visible in the tab strip.*

---

## Pausing the Queue: `[[AWAITING_INPUT]]`

If the agent outputs `[[AWAITING_INPUT]]`, the queue **pauses**. Queued prompts will not dispatch until you explicitly respond to the agent.

This allows agents to ask clarifying questions mid-conversation without the queue steamrolling past them.

### What the UI shows when paused

When the queue pauses, the transcript displays two lines in subtle text:

```
⏸ Queue paused — AI is waiting for your response before continuing.
You can also select or enter a prompt below and click Send.
```

The Send button also switches from **Queue** to **Send** — this ensures your reply fires immediately (directly to the AI) rather than being appended to the back of the queue.

### Editing while paused

Queued items **remain in their tabs and are fully editable** while the queue is paused. You can click any tab, revise the text, and reorder with Ctrl+Enter before the queue resumes.

### Resuming

Send any response to the agent (type a reply and press Send, or click a queued tab and click Send). The queue resumes automatically and dispatches the next item.

![Screenshot: Queue paused state in transcript](images/prompt-queue-paused.png)
> 📸 *Screenshot needed: The transcript showing the two `⏸ Queue paused` lines in subtle/dimmed text, with queued tabs still visible above the prompt box — ideally with the Send button label visible.*

---

## Pausing Auto-Resume at Startup (Shift key)

If SquadDash closed while items were still in the queue, it normally re-dispatches them automatically on the next launch. To suppress this, **hold either Shift key while SquadDash is starting up** (at the moment the window loads).

- The Shift state is read once — releasing Shift after the window appears has no effect.
- If queue auto-dispatch was suppressed, the transcript shows: `⏸ Startup paused — Shift was held. Queue and loop auto-resume suppressed.`
- The queued items remain in place; you can review or edit them before dispatching manually.

This is useful when you want to inspect or revise queued prompts before they run rather than having them fire immediately on startup.

---

## Voice Dictation and the Queue

If a prompt was dictated by voice, the queue tab shows **clean text only** — the voice annotation (`some or all of this prompt was dictated by voice`) is not visible while the item is waiting in the queue.

The annotation appears in the **transcript** only after the item dispatches and the turn completes.

See **[Voice Input](voice-input.md)** for details on the voice annotation.

---

## Queue Behaviour at a Glance

| Scenario | Result |
|---|---|
| Prompt submitted while agent running | Enters queue as next tab |
| Agent turn completes | First queued item dispatches automatically |
| Agent outputs `[[AWAITING_INPUT]]` | Queue pauses; Send button switches to "Send"; resumes after user responds |
| Edit a queued tab | Changes apply when that item dispatches |
| Ctrl+Enter on Active Draft (queue non-empty) | Draft enqueued at front (#1); prompt box cleared |
| Ctrl+Enter on queued tab | That item moved to front (#1); transient feedback label shown |
| Voice-dictated queued prompt | Tab shows clean text; annotation added after dispatch |
| Shift held at startup | Queue auto-dispatch suppressed; items remain for manual review |

---

## Related

- **[Voice Input](voice-input.md)** — How voice-dictated prompts interact with the queue
- **[Transcripts](../concepts/transcripts.md)** — How dispatched prompts appear in the transcript
- **[Keyboard Shortcuts](../reference/keyboard-shortcuts.md)** — Full list of prompt box and queue shortcuts