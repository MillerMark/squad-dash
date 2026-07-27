namespace SquadDash;

/// <summary>
/// Published via <see cref="WeakEventBroker"/> whenever a plan advances through a lifecycle
/// transition or its step progress changes. Carries the fully updated <see cref="Plan"/> object
/// so subscribers do not need a separate store read.
/// </summary>
internal sealed record PlanProgressEvent(
    string PlanId,
    Plan   UpdatedPlan);
