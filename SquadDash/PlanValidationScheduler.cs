namespace SquadDash;

/// <summary>
/// Determines whether a validation turn should run next, ahead of the blocked frontier.
/// Pure scheduling logic with no side effects.
/// </summary>
internal static class PlanValidationScheduler
{
    /// <summary>
    /// Returns the next ready validation that should be scheduled, or null if no validations are ready.
    /// Validations in <see cref="PlanValidationStatus.Ready"/> state are scheduled before their
    /// blocked frontier tasks.
    /// </summary>
    internal static PlanValidationNode? SelectNextSchedulable(Plan plan)
    {
        return PlanValidationReadinessEvaluator.SelectNextReady(plan);
    }

    /// <summary>
    /// Returns true if any validation is currently in <see cref="PlanValidationStatus.Validating"/>
    /// state, indicating a validation turn is in progress.
    /// </summary>
    internal static bool IsValidationInProgress(Plan plan)
    {
        return (plan.Validations ?? []).Any(v =>
            v.Status == PlanValidationStatus.Validating);
    }

    /// <summary>
    /// Returns the validation currently being validated (Validating status), or null.
    /// Used on restart to resume a validation that was interrupted.
    /// </summary>
    internal static PlanValidationNode? GetInProgressValidation(Plan plan)
    {
        return (plan.Validations ?? []).FirstOrDefault(v =>
            v.Status == PlanValidationStatus.Validating);
    }

    /// <summary>
    /// Computes all task IDs blocked by validations that have not passed.
    /// Union with approval gate blocked IDs for complete blocking picture.
    /// </summary>
    internal static IReadOnlySet<string> ComputeBlockedTaskIds(Plan plan)
    {
        return PlanValidationReadinessEvaluator.ComputeAllBlockedTaskIds(plan);
    }
}
