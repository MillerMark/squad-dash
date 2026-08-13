namespace SquadDash;

/// <summary>
/// Pure viewport policy for keeping active plan work visible without disturbing a reader whose
/// current viewport already contains the active stage and its following stage.
/// </summary>
internal static class PlanViewerAutoScrollPolicy
{
    internal static readonly TimeSpan InteractionQuietPeriod = TimeSpan.FromSeconds(30);

    internal static bool IsInteractionQuiet(DateTime lastInteractionUtc, DateTime nowUtc) =>
        nowUtc - lastInteractionUtc >= InteractionQuietPeriod;

    internal static double CalculateHorizontalOffset(
        double currentOffset,
        double viewportWidth,
        double visibilityStart,
        double visibilityEnd,
        double extentWidth)
    {
        if (viewportWidth <= 0 || visibilityEnd <= visibilityStart || extentWidth <= viewportWidth)
            return currentOffset;

        const double tolerance = 0.5;
        var viewportEnd = currentOffset + viewportWidth;
        if (visibilityStart >= currentOffset - tolerance &&
            visibilityEnd <= viewportEnd + tolerance)
            return currentOffset;

        // Prefer the requested following stage at the right edge. When both stages fit, this also
        // brings the active stage back into view if the reader had previously scrolled past it.
        var requestedOffset = visibilityEnd - viewportWidth;
        var maximumOffset = Math.Max(0, extentWidth - viewportWidth);
        return Math.Clamp(requestedOffset, 0, maximumOffset);
    }

    internal static double CalculateRightAlignedOffset(
        double currentOffset,
        double viewportWidth,
        double targetRight,
        double extentWidth)
    {
        if (viewportWidth <= 0 || targetRight <= 0 || extentWidth <= viewportWidth)
            return currentOffset;

        var maximumOffset = Math.Max(0, extentWidth - viewportWidth);
        return Math.Clamp(targetRight - viewportWidth, 0, maximumOffset);
    }
}
