namespace SquadDash;

/// <summary>Live, non-durable activity signal for a currently executing validation node.</summary>
internal sealed record PlanValidationActivityPulseEvent(
    string PlanId,
    string ValidationId,
    SpinnerActivityKind Kind);
