namespace SquadDash;

/// <summary>
/// Live, non-durable activity signal for a task currently being executed by a plan.
/// Multiple coordinator, named-agent, and generic-child pulses intentionally accumulate
/// in the task spinner's bounded physics model.
/// </summary>
internal sealed record PlanTaskActivityPulseEvent(
    string PlanId,
    string TaskId,
    SpinnerActivityKind Kind);

internal static class PlanTaskActivityPulsePolicy
{
    internal static bool MatchesLiveTarget(
        string? persistedPlanId,
        string? livePlanId,
        string? liveTargetId,
        string? targetId) =>
        !string.IsNullOrWhiteSpace(persistedPlanId) &&
        string.Equals(persistedPlanId, livePlanId, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(targetId) &&
        string.Equals(targetId, liveTargetId, StringComparison.Ordinal);
}
