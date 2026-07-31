namespace SquadDash;

/// <summary>
/// Immutable identity captured when a loop round starts. Plan selection may advance
/// before the completion callback runs, so audit records must not query mutable runner
/// state when attributing the completed round.
/// </summary>
internal sealed record LoopRoundExecutionIdentity(
    string? PlanId,
    string? Revision,
    string? TaskId,
    string? TaskTitle)
{
    internal static LoopRoundExecutionIdentity ResolveFailure(
        LoopRoundExecutionIdentity? captured,
        string? activePlanId,
        string? activeRevision,
        string? activeTaskId,
        string? activeTaskTitle = null) =>
        new(
            captured?.PlanId ?? activePlanId,
            captured?.Revision ?? activeRevision,
            captured?.TaskId ?? activeTaskId,
            captured?.TaskTitle ?? activeTaskTitle);
}
