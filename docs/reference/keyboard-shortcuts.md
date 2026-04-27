# Keyboard Shortcuts

Known keyboard shortcuts and hotkeys in SquadUI.

---

## Global Shortcuts

| Shortcut | Action |
|---|---|
| **Double-Ctrl** | Activate push-to-talk (voice input) |
| **Shift-Click** (on agent card) | Open agent transcript panel |
| **Ctrl+Scroll** (on transcript) | Adjust transcript font size (persisted globally) |
| **Ctrl+Scroll** (on prompt) | Adjust prompt font size (persisted globally) |

---

## Push-to-Talk (PTT)

**Double-tap Ctrl** to activate voice input:
1. Tap **Ctrl** twice quickly
2. Speak your prompt
3. Release **Ctrl** (or tap **Esc**) to end recording

Requires Azure Cognitive Services Speech key (see **[Configuration](configuration.md)**).

---

## Transcript Panel Shortcuts

| Shortcut | Action |
|---|---|
| **Scroll** | Navigate history |
| **Right-click** | Copy text (if implemented) |
| **Ctrl+Scroll** | Adjust transcript font size (persisted globally) |

---

## Fullscreen Transcript Shortcuts

These shortcuts apply when **Full Screen Transcript** mode is active (View → Full Screen Transcript).

| Key / Action | Effect |
|---|---|
| **Any printable key** | Peek prompt bar (key becomes first character) |
| **Double-Ctrl** (PTT) | Peek prompt bar + start voice recording |
| **Escape** (prompt peeked) | Dismiss prompt peek, stay in fullscreen |
| **Escape** (prompt not peeked) | Exit fullscreen |

See **[Fullscreen Transcript Mode](../features/fullscreen-transcript.md)** for full details.

---

## Agent Card Shortcuts

| Action | Shortcut |
|---|---|
| Open transcript | **Shift-Click** |
| Hover highlight | Hover mouse over card |

---

## Future Shortcuts

Planned for future releases:
- **Ctrl-F** — Search within transcript
- **Ctrl-K** — Quick agent switcher
- **Ctrl-Shift-T** — Reopen closed transcript

---

## Customization

Currently, keyboard shortcuts are not customizable. This may be added in a future release.

---

## Next

- **[Configuration](configuration.md)** — Application settings
- **[Getting Started](../getting-started/README.md)** — Installation and first run
