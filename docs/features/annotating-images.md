---
title: Annotating Images
nav_order: 7
parent: Features
---

# Annotating Images

The Annotation Editor opens automatically when you paste a clipboard image or attach an existing image to a prompt. It gives you a full set of drawing tools to mark up the image before attaching it.

![Screenshot: Annotation Editor overview](images/annotation-editor-overview.png)
> 📸 *Screenshot needed: Full annotation editor window showing the toolbar, a sample image on the canvas, and at least one annotation of each type.*

---

## Toolbar Reference

| Tool | Icon | Description |
|------|------|-------------|
| **Move / Select** | ![](images/icon-move.png) | Select and drag existing annotations. No tool active — this is the default mode. |
| **Crop** | ![](images/icon-crop.png) | Drag on the canvas to draw a crop rectangle. Resize with edge/corner handles. Press Enter or click Attach/Insert to apply. |
| **Arrow** | ![](images/icon-arrow.png) | Drag to draw an annotation arrow. The tail is where you start; the arrowhead lands where you release. |
| **Rectangle** | ![](images/icon-rect.png) | Drag to draw a rectangle annotation. Useful for highlighting regions. |
| **Text** | ![](images/icon-text.png) | Click on the canvas to place an editable text label. |
| **Measure Line** | ![](images/icon-measure.png) | Drag to draw a dimension/measurement line with outward arrowheads at both ends. |
| **X Mark** | ![](images/icon-x.png) | Click to place an X annotation at a point. Useful for marking errors or exclusions. |
| **Cursor Indicator** | ![](images/icon-cursor.png) | Click once to enter placement mode, then click on the canvas to stamp a mouse-cursor overlay. |
| **Eyedropper** | ![](images/icon-eyedropper.png) | Click anywhere on the canvas to sample a pixel color. The hex value appears in the toolbar. |
| **Round Corners** | ![](images/icon-round-corners.png) | When enabled, the output PNG has its four corners masked transparent. Only visible in doc/insert mode (hidden in prompt attachment mode). |

> 📸 *Icon screenshots needed — right-click each placeholder above to paste the icon PNG.*
> - `images/icon-move.png` — 18×18 four-directional arrow
> - `images/icon-crop.png` — 18×18 overlapping L-brackets with diagonal line
> - `images/icon-arrow.png` — 18×18 annotation arrow (angled, pointing lower-left)
> - `images/icon-rect.png` — 18×18 rounded rectangle outline
> - `images/icon-text.png` — 18×18 bold "T" letterform
> - `images/icon-measure.png` — 18×18 horizontal shaft with outward arrowheads
> - `images/icon-x.png` — 18×18 X mark (thick rounded lines)
> - `images/icon-cursor.png` — 18×18 pointer cursor (black with white fill)
> - `images/icon-eyedropper.png` — 18×18 classic pipette/dropper
> - `images/icon-round-corners.png` — 18×18 four corner-bracket arcs

---

## Shift+Drag — Axis Constraint

When **dragging an annotation** (arrow, measure line, rectangle, X mark, or text):

- Hold **Shift** while dragging to **constrain movement to a single axis** — the dominant axis (horizontal or vertical) is determined by which direction you move first.
- Release Shift to return to free movement.

> **Tip:** Axis constraint is useful for keeping annotation arrows perfectly horizontal or vertical.

### Shift+Click — Multi-Drop Mode

Holding **Shift** when clicking the **Arrow**, **Rectangle**, **Text**, **Measure Line**, or **X Mark** button enters **multi-drop mode**:

- Place one annotation, then place another — without re-clicking the button each time.
- The active-tool indicator bar becomes slightly wider and rounded to signal multi-drop is active.
- Press **ESC** or click the tool button again to exit multi-drop mode.

---

## Ctrl+Drag — Copy Annotation

When **dragging an existing annotation** (with Move/Select tool or body-drag):

- Hold **Ctrl** while dragging to **copy** the annotation instead of moving it.
- A standby clone appears on the canvas while Ctrl is held, showing where the copy will land.
- Releasing the mouse with **Ctrl held** creates the copy; releasing **without Ctrl** moves the original.
- You can **press or release Ctrl mid-drag** to switch between copy and move mode — the cursor changes to a cross (copy) or four-directional arrow (move) to indicate the current mode.

---

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| **Ctrl+Z** | Undo last change |
| **Delete** | Remove the selected annotation |
| **Space** (hold) | Switch to pan mode — drag to scroll the canvas |
| **Shift** (hold during drag) | Constrain drag to horizontal or vertical axis |
| **Ctrl** (hold during drag) | Copy annotation instead of moving it |
| **ESC** | Exit the current tool mode; if no mode is active, prompts to discard and close |
| **Enter** | Attach / insert the image (equivalent to the toolbar button) |
| **Ctrl+0** | Reset canvas zoom to 100% |

---

## Crop Region

Drag anywhere on the canvas (with no annotation tool active) to draw a crop rectangle. The dashed overlay shows the region that will be included in the final image. Handles on the edges and corners let you resize the crop after drawing.

Press **Enter** (or click **Attach Image / Insert Image**) to finalise the crop and close the editor.

---

## Related

- **[Paste Image](paste-image.md)** — How to paste clipboard images and attach them to prompts
- **[Keyboard Shortcuts](../reference/keyboard-shortcuts.md)** — Full shortcut reference
