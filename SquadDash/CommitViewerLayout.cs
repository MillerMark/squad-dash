namespace SquadDash;

internal static class CommitViewerLayout
{
    internal static double CalculateWindowWidth(double workingWidth, double minimumWidth = 720) =>
        Math.Max(minimumWidth, Math.Min(1600, workingWidth * 0.75));
}
