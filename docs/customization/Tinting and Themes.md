---
title: Tinting and Themes
nav_order: 1
parent: Customization
---

# Tinting and Themes

SquadDash ships with light and dark base themes, plus an eight-stop tint system, an accent colour control, and a UI font scale — all saved per workspace.

---

## Colour Tints

A **tint** rotates the hue of the entire SquadDash palette by a fixed amount, giving the UI a distinct colour feel without changing the contrast or readability of the base theme.

Eight tint stops are available:

| # | Name | Hue character |
|---|---|---|
| 0 | **Natural** | No hue shift — default palette |
| 1 | **Amber** | Warm golden-yellow |
| 2 | **Mint** | Fresh green |
| 3 | **Sage** | Muted earthy green |
| 4 | **Aqua** | Cool cyan-teal |
| 5 | **Sky** | Bright blue |
| 6 | **Plum** | Deep purple |
| 7 | **Blush** | Soft rose-pink |

The tint is applied on top of whichever base theme (light or dark) is currently active, so switching between light and dark is independent of the tint you choose.

![Screenshot: side-by-side tint swatches](images/tint-swatches.png)
> 📸 *Screenshot needed: The main window shown in each of the 8 tint stops (or a montage), so readers can preview the palettes before choosing.*

### Selecting a tint

**Menu:**  View → Tint → *choose a name*

**Keyboard:** `Ctrl+Alt++` (or `Ctrl+Alt+Numpad+`) cycles forward through the 8 stops.  `Ctrl+Alt+−` (or `Ctrl+Alt+Numpad−`) cycles backwards.

![Screenshot: View → Tint menu open](images/tint-menu.png)
> 📸 *Screenshot needed: The View → Tint submenu open, showing all 8 colour swatches with the current tint checked.*

---

## Accent Colour Offset

The **accent colour** is the hue used for the active agent panel border and other highlight elements. Each tint stop ships with a smart default accent offset pre-applied, but you can dial it in for any tint.

### Available offsets

Nine offsets in 30° steps: **−105°, −90°, −60°, −30°, 0°, +30°, +60°, +90°, +105°**

| Tint | Default accent offset |
|---|---|
| Natural | +30° |
| Amber | −60° |
| Mint | −90° |
| Sage | −60° |
| Aqua | −105° |
| Sky | +105° |
| Plum | +105° |
| Blush | +90° |

### Changing the accent offset

**Right-click** the active agent panel border (the coloured strip on the left edge of the agent panel). A context menu appears listing all nine offset options; the current offset is checked.

![Screenshot: accent offset context menu](images/accent-offset-menu.png)
> 📸 *Screenshot needed: The accent colour offset context menu open on the active panel border, showing the nine radio-style offset choices.*

> **Tip:** Switching to a different tint stop automatically resets the accent offset to that tint's smart default. You can then fine-tune from there.

---

## UI Font Scale

The **system font scale** adjusts the size of all text in the SquadDash UI — labels, menus, panel headers, and so on.

Seven scale levels are available:

| Level | Scale | Character |
|---|---|---|
| 0 | 0.75× | Very compact |
| 1 | 0.875× | Compact |
| 2 | **1.0×** | Normal (default) |
| 3 | 1.125× | Slightly large |
| 4 | 1.25× | Large |
| 5 | 1.4× | Very large |
| 6 | 1.6× | Maximum |

### Changing the font scale

Hold **Ctrl** and scroll the mouse wheel **while the cursor is over the title bar**.  Scroll up to increase, scroll down to decrease.

The font scale is saved **globally** (not per workspace) so it persists across all workspaces.

> **Note:** The font scale affects the *system UI text* size. Transcript and prompt font sizes have their own separate Ctrl+Scroll controls (see **[Keyboard Shortcuts](../reference/keyboard-shortcuts.md)**).

---

## Per-Workspace Persistence

The following appearance settings are stored independently for each workspace:

| Setting | Persisted per workspace? |
|---|---|
| Tint stop | ✅ Yes |
| Accent colour offset | ✅ Yes |
| UI font scale | ❌ No — saved globally |
| Transcript font size | ❌ No — saved globally |
| Prompt font size | ❌ No — saved globally |

When you switch workspaces, SquadDash automatically restores the tint and accent offset that were last used in that workspace.

---

## Quick Reference

| Action | How |
|---|---|
| Next tint | `Ctrl+Alt++` / View → Tint |
| Previous tint | `Ctrl+Alt+−` |
| Change accent offset | Right-click active agent panel border |
| Increase font scale | `Ctrl+Wheel Up` over title bar |
| Decrease font scale | `Ctrl+Wheel Down` over title bar |

---

## See Also

- **[Keyboard Shortcuts](../reference/keyboard-shortcuts.md)** — Full shortcut reference including tint and font scale shortcuts
- **[Configuration](../reference/configuration.md)** — Application-level settings
