---
title: Entering Prompts
nav_order: 1
parent: Features
---

# Entering Prompts

The **prompt input area** sits at the bottom of the SquadDash window. It's where you type instructions, questions, or commands to your agents. Everything you send goes through here — text you type manually, voice dictation, and slash commands.

---

## The Prompt Text Box

The text box resizes as you type, expanding up to a maximum height before scrolling. You can write a single sentence or a long multi-line message — whichever the task calls for.

To the right of the text box are two buttons stacked vertically:

- **Send** (or **Queue** when an agent is running) — dispatches your prompt.
- **⚠ Abort...** — stops the current agent run.

![Screenshot: Prompt text box and Send button in idle state](images/prompt-input-idle.png)
> 📸 *Screenshot needed: The full prompt area at the bottom of the SquadDash window — text box empty or with a short prompt typed, Send button visible to the right, Abort button below it (greyed out).*

---

## Sending a Prompt

1. Click inside the text box (or press a key — the box is focused by default on startup).
2. Type your prompt.
3. Press **Enter** to send.

You can also click the **Send** button directly.

> **Multi-line prompts:** Press **Shift+Enter** or **Ctrl+Enter** to insert a line break without sending. Once your prompt spans more than one line, the box grows to show the full content. Press **Enter** on a blank line when you're ready to send, or click **Send**.

> **Send vs Queue:** If an agent is already running when you press Send, the button label changes to **Queue** and your prompt is added to the [Prompt Queue](prompt-queue.md) rather than interrupting the current run.

---

## Aborting a Running Prompt

While an agent is running, the **⚠ Abort...** button (below the Send button) becomes active.

Clicking it opens a confirmation dialog listing the currently running agent turns you can stop. Select the targets you want to abort and confirm.

![Screenshot: Abort button active while a prompt is running](images/prompt-input-abort.png)
> 📸 *Screenshot needed: The prompt area with an agent actively running — Abort button visible and enabled, ideally with the confirmation dialog also shown or a separate shot of each state.*

> **Note:** Abort is a hard stop. Any work the agent has already written to disk is kept, but the current turn ends immediately.

---

## Prompt History

Use **Ctrl+↑** and **Ctrl+↓** to scroll through previously sent prompts. This lets you quickly re-use or tweak a recent prompt without retyping it.

---

## Slash Commands

Type `/` at the start of the text box to open the slash-command IntelliSense list. Use **↑** / **↓** to navigate, **Tab** or **Enter** to accept a suggestion, and **Escape** to dismiss.

See the [Keyboard Shortcuts](../reference/keyboard-shortcuts.md) reference for the full list.

---

## @ Agent References

Type `@` anywhere in the prompt box to address a specific agent directly. An IntelliSense dropdown appears below the cursor listing the agents on your squad.

![Screenshot: @ IntelliSense dropdown appearing below the cursor after typing @ in the prompt box](images/prompt-at-intellisense.png)
> 📸 *Screenshot needed: Prompt box with `@` or a few letters typed (e.g. `@ly`), IntelliSense dropdown open beneath the cursor showing matching agent names with one entry highlighted.*

Use **↑** / **↓** to move through the list, **Tab** or **Enter** to accept, and **Escape** to dismiss without inserting.

Accepting a suggestion inserts `@handle ` into your prompt — the agent's kebab-case handle followed by a space. The prompt is **not submitted automatically**; you can continue typing, add more text, or mention additional agents before sending.

![Screenshot: Prompt box after accepting an @ suggestion — showing @handle inserted mid-prompt](images/prompt-at-accepted.png)
> 📸 *Screenshot needed: Prompt text box after accepting a suggestion — show the resulting `@handle` text in the box with the cursor positioned after the trailing space, ready for more input.*

### What happens when you send a prompt with `@handle`

| Scenario | Result |
|---|---|
| One agent mentioned — `@lyra-morn please fix the dialog` | Coordinator routes the request **directly to that specialist**. |
| Multiple agents mentioned — `@lyra-morn update the UI and @arjun-sen wire the backend` | Coordinator **orchestrates both named specialists** for the request. |

> **Tip:** You can place `@handle` mentions anywhere in your prompt — at the start, mid-sentence, or at the end. SquadDash scans the full prompt text for recognized handles before routing.

---

## See Also

- **[Prompt Queue](prompt-queue.md)** — What happens when you send a prompt while an agent is already running
- **[Voice Input](voice-input.md)** — Dictate prompts with your microphone instead of typing
- **[Fullscreen Transcript](fullscreen-transcript.md)** — View the full conversation history in an expanded view
- **[Hiring Agents](hiring-agents.md)** — Set up the agents you'll be prompting
- **[Keyboard Shortcuts](../reference/keyboard-shortcuts.md)** — All prompt-box shortcuts at a glance
