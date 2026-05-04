---
title: Keyboard Shortcuts
nav_order: 3
parent: Reference
---

# Keyboard Shortcuts

Known keyboard shortcuts and hotkeys in SquadDash.

---

## Global Shortcuts

| Shortcut | Action |
|---|---|
| **F11** | Toggle fullscreen transcript mode |
| **Double-Ctrl** | Activate push-to-talk (voice input) |
| **Shift-Click** (on agent card) | Open agent transcript panel |
| **Ctrl+F** | Focus the transcript/markdown search box |
| **F3** | Jump to next search match |
| **Shift+F3** | Jump to previous search match |
| **Ctrl+Shift+C** | Open the screenshot capture overlay |
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
| **Page Up** | Scroll transcript up one page |
| **Page Down** | Scroll transcript down one page |
| **Ctrl+End** | Scroll transcript to the bottom |
| **Right-click** | Copy text (if implemented) |
| **Ctrl+Scroll** | Adjust transcript font size (persisted globally) |

---

## Fullscreen Transcript Shortcuts

These shortcuts apply when **Full Screen Transcript** mode is active (press **F11** or use View → Full Screen Transcript).

| Key / Action | Effect |
|---|---|
| **F11** | Toggle fullscreen on/off |
| **Mouse to bottom edge** | Reveal prompt bar |
| **Double-Ctrl** (PTT) | Reveal prompt bar + start voice recording |
| **Ctrl+V** or **Shift+Insert** | Paste clipboard text into the prompt bar |
| **Escape** (prompt bar showing) | Dismiss prompt bar, stay in fullscreen |
| **Escape** (prompt bar not showing) | Exit fullscreen |

See **[Fullscreen Transcript Mode](../features/fullscreen-transcript.md)** for full details.

---

## Prompt Navigation Shortcuts

| Shortcut | Action |
|---|---|
| **Ctrl+Page Up** | Navigate to the previous prompt in history |
| **Ctrl+Page Down** | Navigate to the next prompt in history |

---

## Prompt Queue Tab Shortcuts

| Shortcut | Action |
|---|---|
| **Ctrl+Tab** | Cycle to the next queue tab (requires 2+ queued items) |
| **Ctrl+Shift+Tab** | Cycle to the previous queue tab |

---

## Prompt Editor Shortcuts

| Shortcut | Action |
|---|---|
| **Ctrl+B** | Wrap selected text in markdown bold (`**…**`) |

---

## Documentation Editor Shortcuts

These shortcuts apply when the **Documentation Panel** source editor has focus.

| Shortcut | Action |
|---|---|
| **Ctrl+F** | Open find-in-source bar |
| **Ctrl+B** | Wrap selection in markdown bold (`**…**`) |
| **Ctrl+I** | Wrap selection in markdown italic (`*…*`) |
| **Double-Ctrl** | Activate push-to-talk (dictate into doc source) |

---

## Agent Card Shortcuts

| Action | Shortcut |
|---|---|
| Open transcript | **Shift-Click** |
| Hover highlight | Hover mouse over card |

---

## Customization

Currently, keyboard shortcuts are not customizable. This may be added in a future release.

---

## Next

- **[Host Commands](host-commands.md)** — AI-directed commands the AI can invoke to control SquadDash
- **[Slash Commands](slash-commands.md)** — `/` commands for the prompt box
- **[Configuration](configuration.md)** — Application settings
- **[Getting Started](../getting-started/README.md)** — Installation and first run
