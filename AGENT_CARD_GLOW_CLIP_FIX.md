# Agent Card Glow Clipping Fix - CORRECTED

## Problem Summary

Agent cards in the roster/history section that are partially visible (cropped on the right edge) had a visual bug where the hover glow effect extended beyond the card's visible boundary instead of being clipped at the same location. The glow extended all the way to the start of the next panel.

## Root Cause

The hover glow effect is rendered using an overlay Border element (`AgentCardGlowOverlay` and `InactiveAgentCardGlowOverlay`) that sits at the panel grid level, outside the ScrollViewer that contains the agent cards. When a card is clipped by the ScrollViewer's viewport (e.g., when scrolled to the right edge), the overlay was positioned to match the full card dimensions but wasn't respecting the ScrollViewer's clip boundary.

### Architecture

1. Agent cards are in a horizontal `ScrollViewer`
2. The glow effect is applied to an overlay `Border` positioned at the `ActiveAgentsPanelGrid`/`InactiveAgentsPanelGrid` level
3. The overlay is positioned via `TranslateTransform` to match the card's location
4. When the card extends beyond the ScrollViewer's viewport, the card itself is clipped but the overlay was not

## Previous Fix (INCORRECT - Destroyed the Glow)

The initial fix clipped ALL four sides of the glow to match the viewport, which destroyed the glow effect entirely. The glow is **supposed to** extend beyond the card boundaries on the left, top, and bottom sides - that's intentional and required for the visual effect.

## Corrected Solution

Modified the `UpdateAgentCardGlowOverlayPosition()` method in `MainWindow.xaml.cs` to:

### Key Requirements

**What needs to extend beyond the card:**
- Left side of glow: Must extend ~30 pixels beyond card boundary
- Top of glow: Must extend ~30 pixels beyond card boundary  
- Bottom of glow: Must extend ~30 pixels beyond card boundary

**What needs to be clipped:**
- **RIGHT side ONLY**: Should be clipped at the viewport boundary
- **ONLY when BOTH conditions are true:**
  1. It's the last (rightmost) agent card visible
  2. That card is being cropped (doesn't have enough width to show fully)

### Implementation Details

**File:** `SquadDash\MainWindow.xaml.cs`

**Method:** `UpdateAgentCardGlowOverlayPosition()` (lines ~11457-11535)

**Logic:**
1. Check if the overlay extends beyond the ScrollViewer's right edge
2. If YES: Create a clip geometry that:
   - Extends 100px left of the card (allows glow overflow)
   - Extends 100px above the card (allows glow overflow)
   - Extends 100px below the card (allows glow overflow)
   - Clips at the viewport's right edge (prevents overflow into next panel)
3. If NO: No clipping - glow extends freely on all sides

### Code Structure

```csharp
// Calculate if overlay extends beyond viewport right edge
var viewportRightEdge = scrollViewerTopLeft.X + owningScrollViewer.ViewportWidth;
var overlayRight = topLeft.X + overlay.Width;

if (overlayRight > viewportRightEdge)
{
    // Card is clipped on the right - create clip geometry
    const double glowMargin = 100; // More than enough for the ~30px glow extension
    
    var clipRect = new Rect(
        -glowMargin, // Allow glow to extend left
        -glowMargin, // Allow glow to extend up
        viewportRightEdge - topLeft.X + glowMargin, // Width: from far-left to viewport right edge
        overlay.Height + (2 * glowMargin)); // Height: allow glow to extend down and up

    overlay.Clip = new RectangleGeometry(clipRect);
}
else
{
    // Card is fully visible - no clipping needed
    overlay.Clip = null;
}
```

## Testing

### Build Verification
✅ Project builds successfully with no new warnings or errors

### Visual Testing Required
Please verify the following scenarios:

1. **Normal cards (fully visible):**
   - Hover glow should appear normally around the entire card
   - Glow should extend ~30 pixels beyond card boundaries on ALL sides
   - No clipping should occur

2. **Right-edge clipped cards:**
   - When a card is partially visible on the right edge (scrolled)
   - Hover the card
   - The glow should extend normally on the left, top, and bottom sides
   - The glow should be clipped ONLY on the right side at the viewport boundary
   - The glow should NOT extend beyond the ScrollViewer's right edge

3. **Edge cases:**
   - Cards at the left edge (glow extends fully)
   - Cards that are completely outside the viewport (should not crash)
   - Rapid scrolling while hovering
   - Switching between light/dark themes

## Constraints Honored

✅ **Surgical change:** Only modified the `UpdateAgentCardGlowOverlayPosition()` method  
✅ **Glow preserved:** The glow effect extends properly on three sides  
✅ **Targeted fix:** Only affects cards that extend beyond the right edge of the viewport  
✅ **Fallback safety:** If coordinate transforms fail, clipping is disabled to avoid hiding glow entirely

## Files Modified

- `SquadDash\MainWindow.xaml.cs` - Corrected clipping logic to only clip the right edge when needed

## What Was Wrong with the Previous Fix

The previous implementation used `Rect.Intersect(overlayRect, viewportRect)` which clipped ALL four sides of the overlay to match the viewport. This:
- ❌ Prevented the glow from extending beyond the card on the left side
- ❌ Prevented the glow from extending beyond the card on the top side
- ❌ Prevented the glow from extending beyond the card on the bottom side
- ❌ Destroyed the visual glow effect entirely

The corrected fix:
- ✅ Allows the glow to extend freely on the left, top, and bottom sides
- ✅ Only clips the right side when the card extends beyond the viewport
- ✅ Preserves the glow effect as designed

