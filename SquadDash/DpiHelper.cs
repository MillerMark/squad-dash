using System.Windows;
using System.Windows.Media;

namespace SquadDash;

internal static class DpiHelper
{
    /// <summary>
    /// Converts a physical screen pixel coordinate (from PointToScreen) to WPF logical DIPs
    /// suitable for assigning to Window.Left / Window.Top.
    /// </summary>
    internal static Point PhysicalToLogical(Visual visual, Point physicalPoint)
    {
        var source = PresentationSource.FromVisual(visual);
        if (source?.CompositionTarget == null)
            return physicalPoint;
        return source.CompositionTarget.TransformFromDevice.Transform(physicalPoint);
    }
}
