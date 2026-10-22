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

/// <summary>
/// Live, non-durable state for the coordinator's read-only Assess &amp; Continue review.
/// This is intentionally separate from task execution and independent verification.
/// </summary>
internal sealed record PlanRecoveryAssessmentActivityEvent(
    string PlanId,
    string TaskId,
    bool IsActive);

/// <summary>
/// Live, non-durable activity for feedback submitted from a human approval checkpoint.
/// The durable gate transition takes over once the feedback becomes rework or an amendment.
/// </summary>
internal sealed record PlanApprovalChangeActivityEvent(
    string PlanId,
    string GateId,
    bool IsActive);

/// <summary>
/// Live, non-durable state for a bounded conversational correction to one interrupted task.
/// Drafting claims an editable recovery choice across surfaces, Submitted claims a direct
/// review correction already sent to the queue, and Running additionally drives the task
/// spinner without pretending that the paused plan runner itself has resumed.
/// </summary>
internal enum PlanTaskCorrectionActivityState
{
    None,
    Drafting,
    Submitted,
    Running,
}

internal sealed record PlanTaskCorrectionActivityEvent(
    string PlanId,
    string TaskId,
    PlanTaskCorrectionActivityState State);

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
