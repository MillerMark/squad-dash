# Docking Map UI Specification

**Version:** 1.1 — draft  
**Author:** Mira Quill (Documentation & Specification Specialist)  
**Date:** 2026-06-02  
**Relates to:** [panel-docking-system.md](panel-docking-system.md), [panel-docking-ui-spec.md](panel-docking-ui-spec.md)

---

## Overview

The **Docking Map** replaces the hamburger (≡) button and its three-item context menu that currently appear on dockable panel headers. Instead of a one-level menu offering only three coarse zone destinations, the Docking Map presents a spatially accurate miniature of the current panel layout, letting users place a panel precisely — into a specific position within a column or the top row — with two clicks: one to open the map, one to choose a destination.

### What changes

| Before | After |
|---|---|
| Hamburger (≡) button left of the close button | **Grip strip** drawn across the top of the panel border |
| Ctrl+click on panel surface or click ≡ | Click anywhere in the grip strip |
| Context menu: ⬆ Top / ◀ Left / ▶ Right | Docking Map popup showing the full layout |
| Three coarse destinations only | Precise positional placement within any group |

### Scope

Dockable panels: **Tasks, Approvals, Notes, Inbox, Maintenance** (Health and Trace to be added later — see [panel-docking-ui-spec.md §6](panel-docking-ui-spec.md)).

`PanelDockingService.MovePanel(panelId, DockZone)` continues to handle the actual moves; the Docking Map is purely an interaction layer on top of the existing service.

---

## Section 1 — Grip Strip Affordance

### 1.1 Location and Dimensions

The grip strip occupies the topmost portion of the panel's outer `Border`:

- **Width:** Full width of the panel border (edge to edge, including corner radius areas)
- **Height:** Equal to the border's corner radius, nominally **8 px** (device-independent units). Implementations must read the border's actual `CornerRadius.TopLeft` value rather than hardcoding 8.
- **Position:** Flush with the top edge of the border

### 1.2 Visual — Hatched Lines

The strip is drawn as a series of **horizontal 1-px lines** following the rules below:

- Lines are drawn at every **other** pixel row within the strip height. For an 8 px strip this produces approximately 4 lines with 4 transparent rows interleaved.
- Each line starts at the leftmost visible pixel at that Y offset and ends at the rightmost visible pixel, respecting the border's corner radius. Near the top-left and top-right corners the visible width shrinks according to the rounded corner geometry — lines get progressively shorter as they approach the corner arc.
- **Color:** A semi-transparent version of the panel's accent or border color, theme-matched. Recommended opacity: 35–50 %. For dark theme this is approximately `#60` prefix applied to the `PanelBorder` or accent color. For light theme the same opacity applied to the corresponding light-theme border color.
- Lines must be pixel-snapped using `GuidelineSet` so they render as crisp 1-device-pixel strokes on standard DPI. On high-DPI displays, device pixels are used (1 physical pixel per line).

### 1.3 Hit Area

The hit area for the grip strip is the full **width × corner-radius-height** rectangle at the top of the panel, regardless of where the visible lines are drawn (including the transparent rows between lines). This rectangle is the **clickable affordance** that replaces the hamburger button.

Cursor over the hit area: `Hand` (`Cursors.Hand`), communicating the grip strip is a clickable control.

A **tooltip** appears on hover over the grip strip with the text **"Docking map…"** (using the Unicode ellipsis character U+2026, not three separate dots). The tooltip uses theme colors: background `ChromeSurface`, foreground `LabelText`, matching the active light or dark theme.

### 1.4 Removal of Hamburger Button

The `≡` (`TextBlock` with U+2261) is **removed** from all dockable panel headers. The `WirePanelDockingCtrlClick()` mechanism (Ctrl+click on the full panel surface) is also removed. The grip strip click is the sole trigger for the Docking Map.

Named elements to remove: `TasksPanelHamburger`, `ApprovalsPanelHamburger`, `NotesPanelHamburger`, `InboxPanelHamburger`, `MaintenancePanelHamburger`.

---

## Section 2 — Docking Map Popup

Clicking anywhere in the grip strip opens the **Docking Map** — a floating, borderless, theme-matched popup window showing a miniature representation of the full panel layout.

### 2.1 Positioning

The popup is positioned so that the **source panel's slot button is centered over the mouse cursor** at the moment the grip strip is clicked. This provides a spatial anchor: the user sees "I clicked my panel, and my panel is here under my cursor — now I see the whole layout around me."

`BuildDockingMap` must return the offset from the popup's top-left corner to the center of the source panel's slot button. The popup's `Left`/`Top` are then set so that `slot_center_screen = click_screen_position`.

Screen-edge clamping: if this positioning would push any edge of the popup off-screen, the popup is shifted (not scaled) so it remains **fully visible within the working area of the active monitor**. When clamped, the spatial anchor is no longer exact — this is acceptable degradation.

### 2.2 Zone Regions

The popup rectangle is divided into three **zone regions**:

```
┌─────────────────────────────────────────────┐
│  LEFT  │         TOP ZONE          │  RIGHT  │
│  ZONE  │  (upper half of popup)    │  ZONE   │
│        ├───────────────────────────┤         │
│        │    (lower half — empty)   │         │
└─────────────────────────────────────────────┘
```

More precisely:

- **Top zone:** Centered horizontally between the Left and Right zone regions, occupying the **upper half** of the popup's total height (from the top edge down to the vertical midpoint).
- **Left zone:** The left edge of the popup, full height, wide enough to contain up to 2 column stacks side by side.
- **Right zone:** The right edge of the popup, full height, wide enough to contain up to 2 column stacks side by side.
- The lower half of the popup between Left and Right zones contains no interactive content; it provides visual breathing room and is filled with the popup's background color.

**Empty zone handling:** If a zone has no panels and the source panel is not being offered a destination there, the zone region either collapses (zero width for Left/Right, zero height contribution for Top) or displays a single full-size "dock here" drop-target button. See Rule D in Section 3.

### 2.3 Popup Sizing

The popup dimensions are computed dynamically at popup-open time from the content being rendered.

#### Slot button sizes

| Slot type | Minimum size |
|---|---|
| Column slot (Left/Right zone) | 48 × 48 px |
| Top-row slot (Top zone) | 64 × 32 px |
| Full-height empty-zone slot (Left/Right) | 48 × (zone height) |
| Full-width empty Top-zone slot | (zone width) × 32 px |

These are **minimum** sizes; slots may grow to fit abbreviated panel names or icons.

#### Layout metrics

| Property | Value |
|---|---|
| Gap between slot buttons within a group | 4 px |
| Gutter between zone regions | 8 px |
| Popup internal padding (all sides) | 8 px |
| Popup corner radius | 4 px |

#### Width and height calculation

1. Compute each zone region's required width (Left, Top content, Right) plus gutters and padding.
2. Compute each zone region's required height; overall popup height = max(Left zone height, Top zone minimum height contribution, Right zone height) + padding.
3. The total popup size = sum of zone widths + gutters + padding × overall height.

### 2.4 Appearance

- **Theme-matched:** All colors derive from the active SquadDash theme (dark or light). No hardcoded color values.
- **Background:** `ChromeSurface` (same as the existing `ThemedContextMenuStyle` popup background).
- **Border/shadow:** No visible outer border. A subtle drop shadow (WPF `DropShadowEffect`, blur radius 8 px, opacity 0.35, offset 0,2) provides depth separation from the content beneath.
- **Zone gutters:** A 1 px line in `PanelBorder` color (at 40 % opacity) separates zone regions visually.
- **Slot buttons:**
  - **Active (enabled):** Background `HoverSurface` on hover, `ChromeSurface` at rest. Foreground `LabelText`. Border 1 px `PanelBorder`. Corner radius 3 px.
  - **Source panel slot (disabled):** Background `ChromeSurface` darkened toward `AppBackground` (approximately 60 % blend). Foreground `SubtleText`. `IsEnabled=False`. No hover effect. This slot is the spatial anchor — it represents "where you are."
  - **Expansion slots (new column):** Same as active but with a dashed 1 px `PanelBorder` border and a "+" prefix in the label, indicating the button creates a new column rather than moving into an existing one.
- **Labels:** Each slot displays the panel's abbreviated name (≤ 6 characters, e.g. "Tasks", "Aprvl", "Notes", "Inbox", "Maint"). If the slot is an expansion slot, prefix with "+".

---

## Section 3 — Button Generation Rules

These rules determine which slot buttons appear in each zone region when the Docking Map opens for **source panel P**.

The current layout is read from `PanelDockingService.CurrentLayout` at the moment the grip strip is clicked. Changes to the layout after the popup opens are not reflected until the popup is re-opened.

### Rule A — Source panel slot is always disabled

Wherever P currently lives (its column and position within that column), that slot is drawn in the disabled / near-background style. It is not clickable. All other rules are applied relative to P's current location.

The popup is positioned so the center of this slot is under the mouse cursor when the map opens (see Section 2.1).

### Rule B — Within the same group as P (reorder)

When a **group** (a column in Left/Right zone, or the Top-row group) **contains P**:

- Draw one slot button for **every panel in the group**, in their current order (top-to-bottom for columns; left-to-right for the top row).
- P's slot = disabled (Rule A).
- **No extra insertion slot** is added. Reordering within the group is possible but no new position is created.
- Clicking another panel Q's slot **swaps** P to Q's position, and Q moves to P's former position.
- Slot buttons are evenly spaced within the group's allocated bounding area inside the zone region.

### Rule C — Different group from P (insert)

When a group **does not contain P**:

- Draw one slot button for each existing panel in the group **plus 1 additional insertion slot**.
- Total = N + 1 buttons, evenly spaced within the group's allocated bounding area.
- All N + 1 buttons are enabled.
- The N existing-panel slots represent: "insert P before/at this panel's position, shifting that panel and all subsequent panels one step toward the end."
- The extra insertion slot (drawn at the **end** of the group's button sequence, visually after the last existing panel) represents: "append P at the end of this group."
- Clicking any of the N + 1 buttons moves P into this group at the selected position.

### Rule D — Empty group

When a group has **no panels** and P is not in it:

- Draw exactly **1 full-size slot button**:
  - For a Left/Right column group: the button spans the full available height of the zone region.
  - For the Top-row group: the button spans the full available width of the top-zone region.
- The label is a neutral indicator such as "—" or a dashed outline, communicating "dock here (empty)."
- Clicking it moves P alone into this group.

### Rule E — New-column expansion buttons

For each existing Left-zone column group and each existing Right-zone column group, expansion buttons may be shown to let the user **create a new column** adjacent to an existing one:

- **Left-of-column expansion button:** A full-height single-slot button drawn to the **left** of the column's button stack. Represents "create a new column to the further-left side of this column."
- **Right-of-column expansion button:** A full-height single-slot button drawn to the **right** of the column's button stack. Represents "create a new column to the further-right side (toward center) of this column."

**Suppression conditions — an expansion button is NOT shown when:**

1. P is in this column (only reordering applies per Rule B; no expansion offered for this column).
2. Adding the new column would exceed 2 columns on that side (Rule F).
3. There is already a column occupying the adjacent slot (i.e., both Left col 1 and Left col 2 are populated — no room for a third).
4. The expansion direction is toward the layout boundary and adding would create a 3rd column on that side.

Expansion buttons use the dashed-border active style described in Section 2.4.

### Rule F — Column count limits

- **Maximum 2 columns** in the Left zone; **maximum 2 columns** in the Right zone.
- No expansion button is ever offered that would result in a 3rd column on either side.
- These limits are evaluated against the current layout at popup-open time.

### Sizing Note — Width Preservation on Move

When a panel is moved from one location to another, its **width is preserved** at the destination. Exception: when panels are stacked vertically within the same column, the width of all panels in that column is governed by the column splitter and may be adjusted freely by the user.

---

## Section 4 — Interaction Flow

1. **Discovery:** The user notices horizontal grip lines at the very top of a dockable panel — a subtle but consistent affordance that the panel can be moved.
2. **Open:** The user clicks anywhere within the grip strip. The cursor is `Hand` over this area.
3. **Popup appears:** The Docking Map popup appears with the source panel's slot centered on the mouse cursor.
4. **Orientation:** The source panel's slot is rendered in the disabled / near-background style. The user sees "this is where I am" spatially anchored within the miniature layout.
5. **Select destination:** The user moves the mouse to the desired target slot. Active slots show a hover highlight on pointer entry.
6. **Confirm move:** The user clicks the target slot. `PanelDockingService.MovePanel(panelId, targetZone)` is called with the appropriate zone and position information. The panel moves instantly.
7. **Popup closes:** The popup closes immediately after the move is confirmed.
8. **Cancel:** At any time before step 6, the user can press **Escape** or click anywhere outside the popup to close it with no action taken.

No animation is used in v1. Panel moves are instantaneous. Animation is a planned future enhancement (see Section 7).

---

## Section 5 — Examples

The following examples use ASCII art to show the approximate shape of the Docking Map popup. Each lettered cell represents a slot button. `[P]` = source panel (disabled). `[+]` = expansion slot (new column). `[ ]` = empty-zone full-size slot.

In the ASCII diagrams: Left zone is on the left, Right zone is on the right, Top zone is the upper-middle band.

---

### Example 1 — All panels in Top, nothing docked Left or Right

**Setup:** 5 panels (Tasks, Approvals, Notes, Inbox, Maint) all in Top zone. Source panel P = Notes.

**Rules applied:**
- Top zone contains P → Rule B: 5 slots, P's slot disabled, no extra insertion slot.
- Left zone is empty → Rule D: 1 full-height "dock here" slot.
- Right zone is empty → Rule D: 1 full-height "dock here" slot.
- No existing columns → Rule E: no expansion buttons.

```
┌──────────────────────────────────────────────────────────┐
│        ┌───────────────────────────────────┐             │
│        │ Tasks │ Aprvl │ [Notes]│ Inbox │ Maint │        │
│        └───────────────────────────────────┘             │
│  ┌───┐ ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄  ┌───┐      │
│  │   │                                        │   │      │
│  │ [ ]│                                       │ [ ]│      │
│  │   │                                        │   │      │
│  └───┘                                        └───┘      │
└──────────────────────────────────────────────────────────┘

  LEFT    ←── TOP ZONE (upper half) ──→   RIGHT
  (empty)       [Notes] = disabled        (empty)
```

*Clicking Left `[ ]` or Right `[ ]` docks Notes alone into that zone. Clicking any other Top slot swaps Notes to that position.*

---

### Example 2 — One panel docked Left (col 1), rest in Top; source is a Top panel

**Setup:** Tasks in Left col 1; Approvals, Notes, Inbox, Maint in Top. Source panel P = Inbox (in Top).

**Rules applied:**
- Top zone contains P → Rule B: 4 slots (Aprvl, Notes, **[Inbox]**, Maint), no insertion slot.
- Left col 1 does not contain P → Rule C: 1 existing slot (Tasks) + 1 insertion slot = 2 slots total.
- Rule E for Left col 1: P is NOT in col 1, and col 1 is the only left column, so:
  - Left-of-col1 expansion button shown (would create a new col further left).
  - Right-of-col1 expansion button: col 1 is already the rightmost left column and the right side is the boundary toward center — shown if max not exceeded (1 col currently < 2 max → show it).
- Right zone is empty → Rule D: 1 full-height slot.
- No Right columns → Rule E: no right expansion buttons.

```
┌──────────────────────────────────────────────────────────┐
│              ┌──────────────────────────┐                │
│              │  Aprvl │ Notes │[Inbox]│Maint │           │
│              └──────────────────────────┘                │
│  ┌─┬────┬─┐  ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄   ┌───┐          │
│  │+│    │+│                               │   │          │
│  │ │Tasks│ │                              │ [ ]│          │
│  │+│    │+│                               │   │          │
│  │ │ ── │ │                               └───┘          │
│  │+│Ins.│+│                                              │
│  │ │slot│ │                                              │
│  └─┴────┴─┘                                             │
└──────────────────────────────────────────────────────────┘

  [+] = expansion (new col)   col1: Tasks + insertion slot   RIGHT: empty [ ]
```

*Clicking `Tasks` in Left col 1 inserts Inbox before Tasks. Clicking the insertion slot appends Inbox after Tasks. Clicking a Left `[+]` creates a new left column with Inbox alone.*

---

### Example 3 — Two panels in Left col 1; source is one of them

**Setup:** Tasks and Approvals in Left col 1 (Tasks above Approvals). Notes, Inbox, Maint in Top. Source panel P = Approvals (in Left col 1).

**Rules applied:**
- Left col 1 contains P → Rule B: 2 slots (Tasks, **[Approvals]**), no insertion slot. Clicking Tasks swaps Approvals to Tasks' position (top) and Tasks moves to Approvals' former position (bottom). Rule E: no expansion buttons for this column (P is in it).
- Top zone does not contain P → Rule C: 3 existing slots (Notes, Inbox, Maint) + 1 insertion slot = 4 slots.
- Right zone is empty → Rule D: 1 full-height slot.
- No Right columns → no expansion buttons on the right.

```
┌──────────────────────────────────────────────────────────┐
│              ┌─────────────────────────────┐             │
│              │  Notes │ Inbox │ Maint │ Ins.│            │
│              └─────────────────────────────┘             │
│  ┌──────┐    ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄    ┌───┐     │
│  │ Tasks│                                     │   │     │
│  │      │                                     │ [ ]│     │
│  ├──────┤                                     │   │     │
│  │[Aprvl│                                     └───┘     │
│  │  ]   │                                               │
│  └──────┘                                               │
└──────────────────────────────────────────────────────────┘

  Left col 1: Tasks (swap target) + [Approvals] (disabled)
  Top: Notes, Inbox, Maint + insertion slot
  Right: empty [ ]
```

*Clicking Tasks swaps Approvals and Tasks. Clicking any Top slot moves Approvals into the Top zone at that position. No expansion buttons shown for the Left column because Approvals lives there.*

---

### Example 4 — Two left columns occupied; source is in col 1

**Setup:** Tasks and Approvals in Left col 1; Notes in Left col 2; Inbox and Maint in Top. Source panel P = Tasks (in Left col 1).

**Rules applied:**
- Left col 1 contains P → Rule B: 2 slots (**[Tasks]**, Approvals), no insertion slot. Rule E: no expansion buttons for col 1 (P is in it).
- Left col 2 does not contain P → Rule C: 1 existing slot (Notes) + 1 insertion slot = 2 slots. Rule E for col 2: P is NOT in col 2; however, col 2 is the rightmost left column at the center boundary, and col 1 is already to its left — both left slots filled → no expansion buttons (would exceed 2 left columns, Rule F).
- Top zone does not contain P → Rule C: 2 existing slots (Inbox, Maint) + 1 insertion slot = 3 slots.
- Right zone is empty → Rule D: 1 full-height slot.

```
┌────────────────────────────────────────────────────────────┐
│                   ┌──────────────────────┐                 │
│                   │  Inbox │  Maint │ Ins.│                │
│                   └──────────────────────┘                 │
│  ┌──────┬──────┐  ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄  ┌───┐          │
│  │[Tasks│Notes │                           │   │          │
│  │  ]   │      │                           │ [ ]│          │
│  ├──────┼──────┤                           │   │          │
│  │Aprvl │ Ins. │                           └───┘          │
│  │      │ slot │                                          │
│  └──────┴──────┘                                         │
└────────────────────────────────────────────────────────────┘

  Left col 1: [Tasks] (disabled) + Approvals (swap)
  Left col 2: Notes + insertion slot   (no expansion — both left slots full)
  Top: Inbox, Maint + insertion slot
  Right: empty [ ]
```

*No left expansion buttons because both left columns are occupied (Rule F). Clicking Approvals in col 1 swaps Tasks and Approvals. Clicking Notes or the insertion slot in col 2 moves Tasks into col 2. Clicking any Top slot moves Tasks to the top row.*

---

## Section 6 — Implementation Notes (for Lyra Morn — WPF)

### 6.1 Grip Strip Rendering

**Recommended approach:** Subclass the panel's outer `Border` and override `OnRender(DrawingContext dc)`. In the override:

1. Read `CornerRadius.TopLeft` to obtain the strip height (call it `r`).
2. Read `ActualWidth` for the line width.
3. For each line row `y` in `{1, 3, 5, 7, …}` up to `r` (0-indexed, every other row):
   - Compute the clipped width at that row using the circle equation for the corner arc: `x_offset = r - sqrt(r² - (r-y)²)`. The line starts at `x_offset` and ends at `ActualWidth - x_offset`.
   - Add a `GuidelineSet` snapping the Y coordinate to the nearest device pixel boundary.
   - Call `dc.DrawLine(pen, new Point(x_offset, y + 0.5), new Point(ActualWidth - x_offset, y + 0.5))` (the 0.5 centers the 1-device-px line on the pixel row).
4. Pen: 1 px, color = `(Color)FindResource("PanelBorderColor")` at 40 % opacity (or the equivalent brush resource).

The grip strip hit area is registered by attaching a `MouseLeftButtonDown` handler to the subclassed `Border`, testing whether `e.GetPosition(this).Y <= CornerRadius.TopLeft`.

**Alternative approach:** A `DrawingVisual` child added as an adorner layer overlay. This avoids subclassing but adds adorner layer management complexity. Prefer the `OnRender` override.

### 6.2 Docking Map Popup Window

Declare a WPF `Window` with these properties:

```xml
WindowStyle="None"
AllowsTransparency="True"
Background="Transparent"
Topmost="True"
ShowInTaskbar="False"
ResizeMode="NoResize"
SizeToContent="WidthAndHeight"
```

- **Open:** Call `window.Show()` (not `ShowDialog()`). Position immediately after `Show()` by setting `Left` and `Top` to center the window on the click screen coordinates, then apply screen-edge clamping.
- **Close on focus loss:** Handle the `Deactivated` event — close the window with no action. Also handle `KeyDown` to close on `Key.Escape`.
- **Outside click:** Because `Topmost=True` with `AllowsTransparency=True`, a click outside the popup will naturally trigger `Deactivated`. No separate mouse-capture logic is needed.
- **Drop shadow:** Apply `<Window.Effect><DropShadowEffect BlurRadius="8" Opacity="0.35" ShadowDepth="2"/></Window.Effect>` to the root `Border` inside the window (not the window itself, which has transparent background).

### 6.3 Slot Buttons

Each slot button is a WPF `Button` control:

- Layout: place slot buttons inside a custom layout panel or manually on a `Canvas`. A `UniformGrid` or a `WrapPanel` is not appropriate — use explicit position calculations from the `DockingMapViewModel` (see §6.4) and place buttons with `Canvas.Left` / `Canvas.Top`.
- **Disabled slot style:** Override the default disabled template. Set `IsEnabled=False`. Use a `Style` with a `Trigger` for `IsEnabled=False` that sets background to a near-background blend and foreground to `SubtleText`. Remove the default grayed-out disabled appearance.
- **Active slot style:** `Background=ChromeSurface`, `BorderBrush=PanelBorder`, `BorderThickness=1`, `CornerRadius=3`. `Trigger` for `IsMouseOver=True` sets `Background=HoverSurface`.
- **Expansion slot style:** Same as active but `BorderThickness=1` with a dashed `Pen` (use a `VisualBrush` or a custom `Border` child overlay to simulate dashed border in WPF, since `Border.BorderBrush` does not natively support dashes).

### 6.4 DockingMapViewModel

The map window's data is computed by a method:

```csharp
DockingMapViewModel BuildDockingMap(string sourcePanelId, DockLayout currentLayout)
```

`DockingMapViewModel` structure (sketch):

```csharp
record DockingMapViewModel(
    IReadOnlyList<SlotButtonViewModel> Slots,
    double PopupWidth,
    double PopupHeight
);

record SlotButtonViewModel(
    string Label,
    bool IsSourcePanel,       // disabled / near-background
    bool IsExpansionButton,   // dashed border, "+" label prefix
    double X, double Y,       // Canvas position (top-left of button)
    double Width, double Height,
    Action OnClick            // lambda that calls PanelDockingService.MovePanel(...)
);
```

`BuildDockingMap` applies Rules A–F (Section 3) to the current layout and computes pixel positions for all slots given the sizing constants in Section 2.3. The resulting `DockingMapViewModel` is set as the `DataContext` (or passed directly to the window's code-behind) before `Show()` is called.

### 6.5 Cleanup of Hamburger Button Code

After the grip strip is wired:

1. Remove `TasksPanelHamburger`, `ApprovalsPanelHamburger`, `NotesPanelHamburger`, `InboxPanelHamburger`, `MaintenancePanelHamburger` `TextBlock` elements from XAML and their corresponding `PreviewMouseLeftButtonDown` handlers.
2. Remove or gate off `WirePanelDockingCtrlClick()` in `MainWindow.xaml.cs`.
3. The `ThemedContextMenuStyle` and `ThemedMenuItemStyle` resources may be retained for other uses — do not remove them solely for this change.

---

## Section 7 — Deferred Enhancements and Future Work

### Deferred — v2

#### Animation of Panel Moves

**Not in scope for v1.** Panel moves are instantaneous (see Section 4, step 6).

Two options are documented here for future consideration:

**Option A — "Lightsaber trail" effect:** When the panel moves from position A to B, a brief blur/trail sweeps along the movement path. In dark theme: theme-colored, very bright. In light theme: very dark, approximately 25 % opaque. Communicates movement from source to destination.

**Option B — Arrival flash (simpler, preferred starting point):** Animate the panel's background from medium brightness to its normal brightness when it arrives at the new location — a brief flash indicating "this just moved here." Easier to implement than Option A and still clearly conveys the move.

No API changes are required for either option; animation would be a purely visual layer on top of the existing `MovePanel` call.

#### Keyboard Navigation

**Not in scope for v1.**

Candidate interactions when implemented:

- **Tab / Shift+Tab:** Move focus between enabled slot buttons in reading order (left-to-right, top-to-bottom).
- **Arrow keys:** Move focus spatially to the nearest slot button in the arrow direction.
- **Enter / Space:** Confirm the focused slot (same action as clicking it).
- **Escape:** Close the popup with no action.

The popup window must set `Focusable=True` and call `Focus()` on the first enabled slot button after `Show()`. WPF `Button` keyboard behavior handles Enter/Space by default.

### Future

#### Named Layouts

The interaction model documented here operates on a single current layout. The named-layout recall feature (from [panel-docking-system.md](panel-docking-system.md)) could be integrated by adding a layout-name dropdown or toolbar within the Docking Map popup — deferred to a future spec.

---

**Note — Touch and Stylus:** SquadDash is a desktop Windows application. Touch/stylus hit-area expansion is not applicable and is not planned.

---

*End of Docking Map UI Specification v1.1 — draft*
