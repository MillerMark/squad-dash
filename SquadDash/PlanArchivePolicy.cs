namespace SquadDash;

internal static class PlanArchivePolicy
{
    internal static bool IsAvailable(Plan plan) =>
        plan.LifecycleStatus is not (PlanLifecycleStatus.Executing or
            PlanLifecycleStatus.AwaitingApproval or PlanLifecycleStatus.Archived);

    internal static bool RequiresConfirmation(Plan plan) =>
        plan.LifecycleStatus != PlanLifecycleStatus.Completed;

    internal static bool ConfirmArchive(Plan plan, Func<bool> confirmIncompletePlan) =>
        IsAvailable(plan) && (!RequiresConfirmation(plan) || confirmIncompletePlan());
}
