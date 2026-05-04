---
title: Fullscreen Transcript
nav_order: 3
parent: Features
---

# Fullscreen Transcript Mode

A focused view mode that hides everything except the transcript — no agent cards, no status panel, no prompt bar. Ideal for deep reading of long agent responses, pair-programming reviews, or presenting work on screen.

---

## Activating Fullscreen Mode

Press **F11**, or open the **View** menu and select **Full Screen Transcript**.

![Screenshot: View menu with "Full Screen Transcript" highlighted](images/fullscreen-view-menu.png)
> 📸 *Screenshot needed: The View menu open, with "Full Screen Transcript" highlighted. Show the full menu so other options (Normal, View Documentation) are visible for context.*

The agent cards, status panel, and prompt bar all disappear. The transcript expands to fill the entire window.

![Screenshot: Fullscreen transcript mode — transcript filling the full window](images/fullscreen-transcript-active.png)
> 📸 *Screenshot needed: The main window in fullscreen transcript mode — transcript content fills edge-to-edge, no sidebar or prompt bar visible. Ideally show a conversation in progress.*

---

## What Changes in Fullscreen

| UI Element | Normal View | Fullscreen |
|---|---|---|
| Transcript | Center panel | Full window |
| Agent status panel | Visible (left) | Hidden |
| Agent roster / history | Visible (left) | Hidden |
| Prompt bar | Visible (bottom) | Hidden (peek on keypress) |

---

## Revealing the Prompt Bar

You don't have to exit fullscreen to send a message. Move the mouse to the **bottom of the screen** and the prompt bar slides into view — just the text input and Send button, nothing else.

![Screenshot: Prompt bar revealed at bottom of fullscreen](images/fullscreen-prompt-peek.png)
> 📸 *Screenshot needed: Fullscreen transcript with the prompt bar visible at the bottom — show the slim input + send button appearing over the transcript edge. The transcript content should still be clearly visible behind it.*

Move the mouse away from the bottom edge to hide the bar again, or press **Escape** to dismiss it and return to pure fullscreen.

### Dismissing the Prompt Bar

| Action | Result |
|---|---|
| **Move mouse away** | Bar slides back out |
| **Escape** | Dismiss the prompt bar, return to pure fullscreen |
| **Escape** (when prompt bar not showing) | Exit fullscreen entirely |

---

## Push-to-Talk in Fullscreen

**Double-tap Ctrl** (push-to-talk) works in fullscreen. When you activate PTT, the prompt bar automatically appears so you can see the recording indicator and confirm your speech is captured — without leaving fullscreen mode.

When recording ends, the bar stays visible (peeked state) so you can review the transcribed text before sending.

---

## Exiting Fullscreen

Fullscreen exits when you:

- Press **F11** (toggle)
- Press **Escape** (with the prompt bar not showing)
- Click certain controls that require the full UI (e.g., agent cards)
- Select **View → Normal** from the menu

---

## Persistence

Fullscreen state is saved **per workspace folder**. If you close SquadDash while in fullscreen with workspace `~/Projects/MyApp` open, the next time you open that workspace it will restore to fullscreen automatically.

Other workspaces are unaffected — each folder tracks its own view mode independently.

---

## Keyboard & Mouse Summary

| Key / Action | Effect |
|---|---|
| **F11** | Toggle fullscreen on/off |
| **Mouse to bottom edge** | Reveal prompt bar |
| **Double-Ctrl** (PTT) | Reveal prompt bar + start voice recording |
| **Escape** (prompt bar showing) | Dismiss prompt bar, stay in fullscreen |
| **Escape** (prompt bar not showing) | Exit fullscreen |

---

## Related

- **[View Modes](view-modes.md)** — Overview of Normal, Fullscreen, and Documentation modes
- **[Keyboard Shortcuts](../reference/keyboard-shortcuts.md)** — All global and context-specific shortcuts
- **[Push-to-Talk](../reference/keyboard-shortcuts.md#push-to-talk-ptt)** — Voice input details
