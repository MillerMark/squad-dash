# UI Spec: Panel Docking — Hamburger Affordance & Popup Layout

**Status:** Accepted  
**Date:** 2026-06-01  
**Author:** Mira Quill (Documentation & Institutional Memory)  
**Relates to:** [panel-docking-system.md](panel-docking-system.md) — resolves open question D4

---

## Section 1 — Hamburger Affordance (≡ icon)

### 1.1 Decision: Always Visible

**The hamburger icon (≡) is shown at all times, regardless of which zone the panel occupies.**

#### Rationale

| Criterion | Always show | Left/Right only |
|---|---|---|
| **Discoverability** | ✅ Users can find Ctrl+click from any zone | ❌ Hidden until user already knows about docking |
| **Consistency** | ✅ Identical header layout in every zone | ⚠️ Header changes appearance on zone move |
| **Implementation complexity** | ✅ None — static XAML | ❌ Requires Visibility binding or code-behind update after `MovePanel()` |
| **Current implementation** | ✅ Matches (already always shown) | Would require a code change |

The docking feature is not yet widely known. A permanently visible affordance is more likely to be discovered than one that appears only after a panel has already been moved. The simplicity benefit (no dynamic visibility logic) is a secondary but real advantage. **No code change is required.**

### 1.2 Exact Specification

| Property | Value |
|---|---|
| **Glyph** | `≡` (U+2261 IDENTICAL TO) — three horizontal bars, widely recognised as a menu/move icon |
| **WPF element** | `TextBlock` |
| **FontSize** | `14` (hardcoded, not a dynamic resource — matches the implemented value) |
| **Foreground** | `{DynamicResource PanelTitle}` — muted warm tone: `#C0A080` (dark theme) / `#5A4129` (light theme). Matches the panel title text, giving a low-contrast-but-legible muted appearance. |
| **Cursor** | `Hand` |
| **Tooltip** | *"Ctrl+click anywhere on this panel to move it to another zone."* |
| **DockPanel placement** | `DockPanel.Dock="Right"` — placed **left of the close button** by appearing after the close button in XAML order. Both use `Dock="Right"`; XAML declaration order determines final left-to-right stacking (close button outermost right, hamburger immediately to its left). |
| **Margin** | `0,0,4,0` — 4 px gap between hamburger and close button |
| **VerticalAlignment** | `Center` |
| **Named** | `{PanelName}PanelHamburger` (e.g. `TasksPanelHamburger`) for code-behind access |

### 1.3 Panels Carrying the Icon

Tasks, Approvals, Notes, Maintenance, Inbox. Health and Trace are not yet wired — see Section 6.

---

## Section 2 — Ctrl+Click Interaction

### 2.1 Trigger

**Ctrl+left-click** anywhere on the panel surface (the `Border` element registered via `WirePanelDockingCtrlClick()`). The event is captured on `PreviewMouseLeftButtonDown` with `e.Handled = true` to prevent any nested controls from also receiving the click.

### 2.2 Popup

A `ContextMenu` styled with `ThemedContextMenuStyle` appears at the mouse cursor position (`PlacementMode.MousePoint`).

### 2.3 Menu Item Order and Labels

Items are listed top-to-bottom in this order:

| Order | Label | Zone |
|---|---|---|
| 1 | `⬆ Top` | `DockZone.Top` |
| 2 | `◀ Left` | `DockZone.Left` |
| 3 | `▶ Right` | `DockZone.Right` |

The directional glyph prefix (⬆ ◀ ▶) makes the destination spatially intuitive without needing longer label text such as "Move to Top strip". Short labels reduce visual noise; the directional glyph provides sufficient spatial context.

### 2.4 Current Zone Behaviour

The menu item corresponding to the panel's current zone is **disabled** (`IsEnabled = false`). When disabled, `ThemedMenuItemStyle` renders the item in `{DynamicResource SubtleText}` (dim foreground), which provides adequate visual distinction without needing a separate checkmark or "(current)" label. No additional separator between the current-zone item and the other items is required.

### 2.5 Item Style

Each `MenuItem` uses `ThemedMenuItemStyle` applied via `SetResourceReference`. Background is `ChromeSurface`; highlighted state uses `HoverSurface`.

---

## Section 3 — Visual Affordances per Zone

### 3.1 Top Zone (default)

- Panels arranged **horizontally** in a row (`StatusAgentPanelsGrid`).
- Panels sit side by side; the row height matches `ActiveAgentsPanelBorder` via `MultiBinding`.
- The hamburger icon is visible but low-contrast — it blends with the header title foreground and does not dominate the compact horizontal strip.

### 3.2 Left Zone

- Panels stacked **vertically** in the left column of `ContentZoneGrid`.
- Each panel fills the available height (no fixed height constraint — height is shared by panels in the stack).
- Column is **user-resizable** via `GridSplitter`; initial width when the first panel arrives equals that panel's natural/current width (no jarring resize on arrival).

### 3.3 Right Zone

- Identical vertical stacking behaviour to Left zone, in the right column of `ContentZoneGrid`.
- Same `GridSplitter` resize behaviour and initial width rule.

### 3.4 Zone Width Rule (Left/Right)

Initial width = the arriving panel's `ActualWidth` at the moment of the move. The `GridSplitter` allows user adjustment from that initial point. If the zone is already non-empty when a second panel arrives, the existing column width is preserved.

---

## Section 4 — Popup Appearance

### 4.1 Style References

| Element | Style resource |
|---|---|
| `ContextMenu` | `ThemedContextMenuStyle` |
| Each `MenuItem` | `ThemedMenuItemStyle` |

`ThemedContextMenuStyle` supplies: background `ChromeSurface`, border `PanelBorder` (1 px), padding 2 px, corner radius 4 px.

`ThemedMenuItemStyle` supplies: background `ChromeSurface`, foreground `LabelText`, font size `FontSizeNormal`, hover background `HoverSurface`, disabled foreground `SubtleText`.

### 4.2 Separator

No separator between the disabled (current) item and the enabled items. The disabled visual state (`SubtleText` foreground) provides sufficient differentiation. Adding a separator would require knowing which item is current at build time, adding complexity for minimal gain.

### 4.3 Checkmark / "(current)" Label

Not used. The disabled state is the sole indicator that a zone is the panel's current location. This matches the existing implementation and avoids a redundant visual element.

---

## Section 5 — Keyboard Shortcut

**Status: Deferred.** No keyboard shortcut is specified in this release.

A keyboard shortcut for invoking the dock popup (e.g. a modifier + key combination focused on the active panel) was noted in the ADR as out of scope for the initial implementation. This section is a placeholder for when that work is prioritised.

**Future spec items to fill in here:**
- Shortcut combination (e.g. `Alt+D`, `Shift+F10` scoped to panel)
- Focus requirement (panel must be focused or hovered)
- Accessibility requirements (menu must be keyboard-navigable — already true via `ContextMenu` default behaviour)

---

## Section 6 — Health / Trace Panel Registration

**Health** (`HealthPanelBorder`) and **Trace** (`TracePanelBorder`) panels are created at runtime (not in XAML) and are **not yet registered** with `PanelDockingService` or wired to `WirePanelDockingCtrlClick()`.

This is a **known implementation gap**, not a spec gap:

- `PanelDockingService.RegisterPanel()` is ready and waiting.
- The hamburger `TextBlock` and its tooltip must be added to each panel's header at the runtime-creation callsite.
- `WirePanelDockingCtrlClick()` must either be extended (called again after panel creation) or the wiring inlined at the creation site.
- Panel IDs: `"health"` and `"trace"` (matching the ADR panel ID table).

Until this wiring is done, Health and Trace panels are not dockable and display no hamburger icon. This is acceptable for the initial release.

---

## Section 7 — Implementation Gaps and Follow-Up Tasks

Items in this section represent differences between this spec and the current implementation, or known gaps requiring future code changes.

### 7.1 Health and Trace — Docking Not Wired *(gap, not a spec divergence)*

**What:** Health and Trace panels are not registered with `PanelDockingService` and are not in `WirePanelDockingCtrlClick()`.  
**File:** `SquadDash\MainWindow.xaml.cs`  
**How:** At the runtime callsite where `HealthPanelBorder` and `TracePanelBorder` are created, call `_dockingService.RegisterPanel("health", healthBorder)` and `_dockingService.RegisterPanel("trace", traceBorder)`, add hamburger `TextBlock` to each panel header, then add both to the `dockable` array in `WirePanelDockingCtrlClick()` (or wire inline).

### 7.2 No Divergences from Spec

The hamburger affordance (always visible, `PanelTitle` foreground, FontSize 14, Hand cursor, correct tooltip and placement) matches this spec exactly as shipped in commits d3acb2d / e597d29 / 2cff1b2. The popup (3 items, directional labels, current-zone disabled, `ThemedContextMenuStyle` / `ThemedMenuItemStyle`) also matches. **No code changes are required by this spec.**
