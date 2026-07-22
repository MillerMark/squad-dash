using System.Windows;

namespace SquadDash.GuidedTours;

internal static class NativeDragContinuationPolicy
{
    // Only Escape should cancel a drag.  The left button being released is the
    // normal "drop" signal — WPF sets Action = Drop at that point by default,
    // so we must NOT cancel based on LeftMouseButton being absent.
    public static bool ShouldCancel(bool escapePressed, DragDropKeyStates keyStates)
        => escapePressed;
}
