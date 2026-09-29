namespace SquadDash;

/// <summary>
/// Applies one durable pause-state transition. Persistence must succeed before any
/// presentation surface is refreshed or a caller advances runtime state.
/// </summary>
internal static class PlanPauseTransitionCoordinator
{
    internal static bool TryApply(
        Plan current,
        bool pause,
        Func<Plan, (bool Success, string? Error)> persist,
        Action<Plan> refreshPresentation,
        out Plan persistedPlan,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(persist);
        ArgumentNullException.ThrowIfNull(refreshPresentation);

        var candidate = current.IsPaused == pause
            ? current
            : pause
                ? PlanStoreUpdater.ApplyPause(current)
                : PlanStoreUpdater.ApplyResume(current);

        if (!ReferenceEquals(candidate, current))
        {
            var result = persist(candidate);
            if (!result.Success)
            {
                persistedPlan = current;
                error = result.Error;
                return false;
            }
        }

        persistedPlan = candidate;
        error = null;
        refreshPresentation(candidate);
        return true;
    }
}
