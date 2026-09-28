using System.Text.Json;

namespace SquadDash;

/// <summary>
/// Durable, host-owned claim that a structured plan recovery action must be replayed until
/// plan execution publishes a new start record. It stores parsed identity, not transcript prose.
/// </summary>
internal sealed record PendingPlanContinuationState(
    string PlanId,
    string Revision,
    string Action,
    DateTimeOffset RecordedAt)
{
    internal static PendingPlanContinuationState? Normalize(PendingPlanContinuationState? state)
    {
        if (state is null ||
            string.IsNullOrWhiteSpace(state.PlanId) ||
            string.IsNullOrWhiteSpace(state.Revision) ||
            state.Action is not ("continue-working" or "resume-verification"))
            return null;

        return state with
        {
            PlanId = state.PlanId.Trim(),
            Revision = state.Revision.Trim(),
            Action = state.Action.Trim().ToLowerInvariant(),
            RecordedAt = state.RecordedAt.ToUniversalTime(),
        };
    }

    internal string BuildProtocolResponse() =>
        DecomposeRecoveryDecisionParser.Marker + Environment.NewLine +
        JsonSerializer.Serialize(new DecomposeRecoveryDecision(PlanId, Revision, Action));
}
