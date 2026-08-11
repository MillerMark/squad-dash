namespace SquadDash;

internal enum LoopCloseRecoveryAction
{
    None,
    PreserveAutomaticResume,
    OfferManualResume,
    UsePlanRecovery,
}

internal static class LoopCloseRecoveryPolicy
{
    internal static LoopCloseRecoveryAction Resolve(
        ActiveLoopExecutionState? execution,
        bool restartPending)
    {
        if (execution is null)
            return LoopCloseRecoveryAction.None;
        if (restartPending)
            return LoopCloseRecoveryAction.PreserveAutomaticResume;
        return execution.IsExecutingPlan
            ? LoopCloseRecoveryAction.UsePlanRecovery
            : LoopCloseRecoveryAction.OfferManualResume;
    }
}
