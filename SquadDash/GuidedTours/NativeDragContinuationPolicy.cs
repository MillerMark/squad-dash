using System.Windows;

namespace SquadDash.GuidedTours;

internal static class NativeDragContinuationPolicy
{
    public static bool ShouldCancel(bool escapePressed, DragDropKeyStates keyStates)
        => escapePressed || (keyStates & DragDropKeyStates.LeftMouseButton) == 0;
}
