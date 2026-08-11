namespace SquadDash;

internal static class CommitViewerLayout
{
    internal static double CalculateWindowWidth(double workingWidth, double minimumWidth = 720) =>
        Math.Max(minimumWidth, Math.Min(1600, workingWidth * 0.75));

    internal static string BuildUncertainCommitToolTip(string? explanation)
    {
        return "SquadDash included this commit as evidence, but could not confirm that it belongs to this step.";
    }

    internal static bool IsCommitAttributionUncertain(string relation, string? explanation)
    {
        return string.Equals(relation, PlanRecoveryCommitRelation.Unknown, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(relation, PlanRecoveryCommitRelation.Unrelated, StringComparison.OrdinalIgnoreCase);
    }
}
