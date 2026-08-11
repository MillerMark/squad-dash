namespace SquadDash;

internal static class CommitViewerLayout
{
    internal static double CalculateWindowWidth(double workingWidth, double minimumWidth = 720) =>
        Math.Max(minimumWidth, Math.Min(1600, workingWidth * 0.75));

    internal static string BuildUncertainCommitToolTip(string? explanation)
    {
        const string summary =
            "Commit attribution is uncertain. SquadDash included this commit as evidence, " +
            "but could not confirm that it belongs to this step.";
        return string.IsNullOrWhiteSpace(explanation)
            ? summary
            : $"{summary}\n\nWhy it is uncertain: {explanation.Trim()}";
    }

    internal static bool IsCommitAttributionUncertain(string relation, string? explanation)
    {
        if (string.Equals(relation, PlanRecoveryCommitRelation.Unknown, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(explanation)) return false;

        return explanation.Contains("predates baseline", StringComparison.OrdinalIgnoreCase) ||
               explanation.Contains("predates the baseline", StringComparison.OrdinalIgnoreCase) ||
               explanation.Contains("outside the captured baseline", StringComparison.OrdinalIgnoreCase) ||
               explanation.Contains("outside the assessed baseline", StringComparison.OrdinalIgnoreCase);
    }
}
