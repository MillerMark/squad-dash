---
title: Saturation and Contrast
nav_order: 2
parent: Appearance
---

# Saturation and Contrast

The **Contrast** and **Saturation** sliders let you fine-tune the intensity and vividness of the current theme palette without changing the tint or base theme. Both settings are saved per workspace.

![Screenshot: View menu open showing Contrast and Saturation submenus](images/contrast-saturation-menu.png)
📸
> Screenshot needed: The View menu open with the Contrast submenu visible and the slider at some non-zero value, showing the ±0.00 label.

---

## Contrast

**View → Contrast**

The contrast slider boosts or reduces the difference between light and dark colors in the palette.

- Range: **0.0 to 1.0**
- **0.0** = no change (default)
- **1.0** = maximum contrast — colors above midpoint approach white; colors below midpoint approach black

### How it works

Each color in the palette is pushed toward its nearest extreme:
- Colors brighter than midgray move toward white as you increase contrast.
- Colors darker than midgray move toward black as you increase contrast.

> **Note:** Text colors in SquadDash are tuned so that in the dark theme all text is above midgray, and in the light theme all text is below midgray. This ensures contrast always makes text *more* readable, never less.

![Screenshot: contrast slider at 0.0 vs 0.8](images/contrast-comparison.png)
📸
> Screenshot needed: Side-by-side or before/after showing the main window at contrast 0.0 and contrast ~0.8, to show the effect on panel surfaces and text.

---

## Saturation

**View → Saturation**

The saturation slider controls how vivid or muted the tinted palette appears.

- Range: **−1.0 to +1.0**
- **0.0** = no change (default)
- **Positive values** boost saturation — colors become more vivid and intense
- **Negative values** reduce saturation — colors become more muted and grayscale

### How it works

- At **+1.0**, colors are fully saturated — the tint hue is as vivid as possible.
- At **−1.0**, all colors are fully desaturated — the UI renders in grayscale.
- Values between those two extremes blend proportionally.

![Screenshot: saturation slider at −0.5, 0.0, and +0.5](images/saturation-comparison.png)
📸
> Screenshot needed: Three views showing the same tint at low, neutral, and high saturation to illustrate the range.

---

## Performance

Both sliders use a **settle-and-apply** behavior: the palette is not recomputed on every pixel of drag. Instead, SquadDash waits until you stop moving the slider for ~250 ms before applying the change. This keeps the UI responsive while dragging.

---

## Per-Workspace Persistence

Contrast and saturation are saved independently per workspace. When you switch workspaces, SquadDash restores the contrast and saturation values that were last used in that workspace.

The `"__default__"` workspace value is used as the starting point for any workspace that has not had its own contrast or saturation set yet.

---

## Quick Reference

| Action | How |
|---|---|
| Adjust contrast | View → Contrast → drag slider |
| Adjust saturation | View → Saturation → drag slider |
| Reset to default | Drag slider back to 0.0 |

---

## See Also

- **[Tinting and Themes](Tinting and Themes.md)** — Base theme, color tints, accent offset, font scale
- **[Keyboard Shortcuts](../reference/keyboard-shortcuts.md)** — Tint shortcut keys
