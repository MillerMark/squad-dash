# Fullscreen Transcript Mode

A focused view mode that hides everything except the transcript — no agent cards, no status panel, no prompt bar. Ideal for deep reading of long agent responses, pair-programming reviews, or presenting work on screen.

---

## Activating Fullscreen Mode

Open the **View** menu and select **Full Screen Transcript**.

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

## Peeking the Prompt Bar

You don't have to exit fullscreen to send a message. Press **any printable key** and the prompt bar slides in at the bottom — just the text input and Send button, nothing else.

![Screenshot: Peeked prompt bar in fullscreen mode](images/fullscreen-prompt-peek.png)
> 📸 *Screenshot needed: Fullscreen transcript with the peeked prompt bar visible at the bottom — show the slim input + send button appearing over the transcript edge. The transcript content should still be clearly visible behind it.*

The key you pressed becomes the first character in the text box. Continue typing normally.

### Dismissing the Peek

| Action | Result |
|---|---|
| **Escape** | Dismiss the peeked prompt, return to pure fullscreen |
| **Escape** (when prompt not peeked) | Exit fullscreen entirely |

So two Escapes from fullscreen — one to close the prompt peek, one to exit fullscreen — is the keyboard-only exit path.

---

## Push-to-Talk in Fullscreen

**Double-tap Ctrl** (push-to-talk) works in fullscreen. When you activate PTT, the prompt bar automatically appears so you can see the recording indicator and confirm your speech is captured — without leaving fullscreen mode.

When recording ends, the bar stays visible (peeked state) so you can review the transcribed text before sending.

---

## Exiting Fullscreen

Fullscreen exits when you:

- Press **Escape** (with the prompt not peeked)
- Click certain controls that require the full UI (e.g., agent cards)
- Select **View → Normal** from the menu

---

## Persistence

Fullscreen state is saved **per workspace folder**. If you close SquadUI while in fullscreen with workspace `~/Projects/MyApp` open, the next time you open that workspace it will restore to fullscreen automatically.

Other workspaces are unaffected — each folder tracks its own view mode independently.

---

## Keyboard Summary

| Key / Action | Effect |
|---|---|
| **Any printable key** | Peek prompt bar (key becomes first character) |
| **Double-Ctrl** (PTT) | Peek prompt bar + start voice recording |
| **Escape** (prompt peeked) | Dismiss prompt peek |
| **Escape** (prompt not peeked) | Exit fullscreen |

---

## Related

- **[View Modes](view-modes.md)** — Overview of Normal, Fullscreen, and Documentation modes
- **[Keyboard Shortcuts](../reference/keyboard-shortcuts.md)** — All global and context-specific shortcuts
- **[Push-to-Talk](../reference/keyboard-shortcuts.md#push-to-talk-ptt)** — Voice input details
