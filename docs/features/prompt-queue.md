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
3. When the current turn finishes, the first queued prompt dispatches automatically.
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

## Editing a Queued Prompt

Click any queue tab to open the prompt text for that item. Edit freely — the change takes effect when that item reaches the front of the queue.

![Screenshot: Editing a queued prompt in its tab](images/prompt-queue-edit.png)
> 📸 *Screenshot needed: A queue tab selected with the prompt text visible in an editable state — show the tab active and the text area with some content.*

---

## Pausing the Queue: `[[AWAITING_INPUT]]`

If the agent outputs `[[AWAITING_INPUT]]`, the queue **pauses**. Queued prompts will not dispatch until the user explicitly responds to the agent.

This allows agents to ask clarifying questions mid-conversation without the queue steamrolling past them.

Once you reply, the queue resumes and dispatches the next item normally.

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
| Agent outputs `[[AWAITING_INPUT]]` | Queue pauses; resumes after user responds |
| Edit a queued tab | Changes apply when that item dispatches |
| Voice-dictated queued prompt | Tab shows clean text; annotation added after dispatch |

---

## Related

- **[Voice Input](voice-input.md)** — How voice-dictated prompts interact with the queue
- **[Transcripts](../concepts/transcripts.md)** — How dispatched prompts appear in the transcript
- **[Keyboard Shortcuts](../reference/keyboard-shortcuts.md)** — Prompt box shortcuts
