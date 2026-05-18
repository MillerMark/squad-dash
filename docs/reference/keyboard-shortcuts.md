---
title: Keyboard Shortcuts
nav_order: 3
parent: Reference
---

# Keyboard Shortcuts

Complete reference for all keyboard shortcuts and hotkeys in SquadDash.

---

## Global Shortcuts

| Shortcut | Action |
|---|---|
| **F11** | Toggle fullscreen transcript mode |
| **Shift+F11** | Toggle agents panel focus mode (hides agent cards/panels strip, expands transcript height) |
| **Double-Ctrl** | Activate push-to-talk (voice input) |
| **Shift-Click** (on agent card) | Open agent transcript panel |
| **Ctrl+F** | Focus the transcript/markdown search box (or the doc find bar when the Documentation Panel is open) |
| **F3** | Jump to next search match |
| **Shift+F3** | Jump to previous search match |
| **Ctrl+Shift+C** | Quick AI Cleanup of selected text (uses the configured cleanup prompt); falls back to opening the screenshot capture overlay when no selection is active |
| **Ctrl+Shift+A** | Revise selected text with AI — opens the Revise popup for the focused text box |
| **Ctrl+Scroll** (on transcript) | Adjust transcript font size (persisted globally) |
| **Ctrl+Scroll** (on prompt) | Adjust prompt font size (persisted globally) |
| **Ctrl+Scroll** (on title bar) | Adjust system UI font size — 7 scale levels: 0.75×, 0.875×, 1.0×, 1.125×, 1.25×, 1.4×, 1.6× (saved globally) |
| **Ctrl+Break** | Abort the running prompt (same as clicking the Abort button) |
| **Ctrl+Shift+Break** | Abort the running loop (when the loop panel is active) |
| **Ctrl+Shift+Z** | Redo — works in any focused text control (RichTextBox or TextBox) |

---

## Startup Modifier Keys

These keys are checked **once** as the main window loads. They are not toggles or persistent settings.

| Key held at launch | Effect |
|---|---|
| **Shift** (Left or Right) | Suppresses queue auto-dispatch and loop auto-resume for that session. A `⏸` transcript message confirms when something was suppressed. |

---

## Theme / Appearance Shortcuts

| Shortcut | Action |
|---|---|
| **Ctrl+Alt++** (or **Ctrl+Alt+Numpad+**) | Next tint stop — cycles: Natural → Amber → Mint → Sage → Aqua → Sky → Plum → Blush → Natural |
| **Ctrl+Alt+−** (or **Ctrl+Alt+Numpad−**) | Previous tint stop |
| **Ctrl+Scroll** (on title bar) | Increase or decrease the UI font scale (7 levels from 0.75× compact to 1.6× large; saved globally) |
| **Right-click** active agent panel border | Opens the accent color offset menu — choose an offset from −105° to +105° in 30° steps |

The tint stop, accent offset, and font scale are all saved **per workspace**.
See **[Tinting and Themes](../customization/Tinting and Themes.md)** for full details.

![Screenshot: View → Tint menu showing 8 colour swatches](images/tint-menu.png)
> 📸 *Screenshot needed: The View → Tint submenu open, showing all 8 colour swatches (Natural through Blush) with the current tint checked.*

---

## Window Layout Shortcuts

| Shortcut | Action |
|---|---|
| **Shift+F11** | Toggle agents panel focus mode — hides the agent cards / inline-panels strip and optionally expands the window to fill the monitor height |
| **Ctrl+Alt+Shift+Page Up** | Move the prompt panel **above** the transcript |
| **Ctrl+Alt+Shift+Page Down** | Return the prompt panel to the **bottom** (default position) |

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
| **Ctrl+Shift+Page Up** | Jump to the very first prompt |
| **Ctrl+Shift+Page Down** | Jump to the very latest prompt |
| **Alt+▲ (nav button)** | Jump to the previous prompt containing a `?` (question review mode) |
| **Alt+▼ (nav button)** | Jump to the next prompt containing a `?` (question review mode) |

---

## Prompt Queue Tab Shortcuts

| Shortcut | Action |
|---|---|
| **Ctrl+Q** | Add a new empty queue slot at the **front** (highest dispatch priority) |
| **Ctrl+Shift+Q** | Add a new empty queue slot at the **tail** (lowest dispatch priority) |
| **Ctrl+Delete** | Delete the active queue tab |
| **Ctrl+Tab** | Cycle to the next queue tab (requires 2+ queued items) |
| **Ctrl+Shift+Tab** | Cycle to the previous queue tab |
| **Ctrl+Enter** | Move the active tab (or Active Draft) to the front of the queue (#1). No-op if already first. Shows a transient "« Now at the front of the queue." label. |

---

## Prompt Editor Shortcuts

| Shortcut | Action |
|---|---|
| **Enter** | Submit prompt |
| **Shift+Enter** | Insert newline |
| **Ctrl+V** (clipboard image) | Intercept paste and attach image as **📷 Image** pill |
| **Ctrl+Shift+V** | Attach clipboard text as a follow-up attachment (see **[Attaching Clipboard Text](../features/attach-clipboard-text.md)**) |
| **Ctrl+B** | Wrap selected text in markdown bold (`**…**`) |
| **Ctrl+Shift+A** | Revise selected text with AI — opens the Revise popup |
| **Ctrl+Shift+C** | Quick AI Cleanup of selection (uses configured cleanup prompt) |
| **Shift+F3** (with selection) | Cycle case of selected text — lowercase → UPPERCASE → Title Case → … (repeating) |
| **Backtick `` ` ``** (with selection) | Wrap selection in inline code (`` `…` ``) or a fenced code block if multi-line |
| **Shift+`"` (double-quote, with selection)** | Wrap selection in an inline blockquote (`> …`) |
| **Shift+Space** | Smooth Dictation — remove voice-dictated sentence breaks (see **[Voice Input](../features/voice-input.md#smooth-dictation)**) |

---

## Documentation Editor Shortcuts

These shortcuts apply when the **Documentation Panel** source editor has focus.

| Shortcut | Action |
|---|---|
| **Ctrl+F** | Open find-in-source bar |
| **F3** or **Enter** (find bar focused) | Go to next match in source |
| **Escape** (find bar open) | Close the find bar |
| **Ctrl+B** | Wrap selection in markdown bold (`**…**`) |
| **Ctrl+I** | Wrap selection in markdown italic (`*…*`) |
| **Tab** | Insert a tab character (or indent selected lines) |
| **Double-Ctrl** | Activate push-to-talk (dictate into doc source) |

---

## Agent Card Shortcuts

| Action | Shortcut |
|---|---|
| Open transcript | **Shift-Click** |
| Open accent color menu | **Right-click** the active panel border |

---

## Customization

Keyboard shortcuts are not user-configurable. This may be added in a future release.

---

## Next

- **[Tinting and Themes](../customization/Tinting and Themes.md)** — Colour tints, accent offsets, and font scale
- **[Host Commands](host-commands.md)** — AI-directed commands the AI can invoke to control SquadDash
- **[Slash Commands](slash-commands.md)** — `/` commands for the prompt box
- **[Configuration](configuration.md)** — Application settings
- **[Getting Started](../getting-started/README.md)** — Installation and first run
