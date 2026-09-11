namespace SquadDash;

internal sealed record ValidatedPlanTranscriptSummary(
    string Heading,
    string Summary,
    string Counts,
    string Branch,
    string Identity);

internal static class PlanProposalTranscriptPolicy
{
    internal static bool ShouldSuppressInternalTurn(string? sourceTag) =>
        string.Equals(sourceTag, "plan-visualizer-review", StringComparison.Ordinal) ||
        string.Equals(sourceTag, "plan-human-proof-coverage", StringComparison.Ordinal) ||
        string.Equals(sourceTag, "decompose-repair", StringComparison.Ordinal);

    internal static ValidatedPlanTranscriptSummary BuildValidatedSummary(PendingDecomposePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var group = plan.Group;
        var taskCount = group.Tasks.Count;
        var gateCount = group.ApprovalGates?.Count ?? 0;
        var validationCount = group.Validations?.Count ?? 0;

        return new ValidatedPlanTranscriptSummary(
            $"✓ Plan “{group.GroupTitle}” is ready for approval.",
            group.Summary,
            $"{taskCount} task{PluralSuffix(taskCount)} · " +
            $"{gateCount} approval checkpoint{PluralSuffix(gateCount)} · " +
            $"{validationCount} validation{PluralSuffix(validationCount)}",
            group.Branch,
            $"Plan ID: {group.GroupId} · Revision: {plan.Revision}");
    }

    private static string PluralSuffix(int count) => count == 1 ? string.Empty : "s";
}
