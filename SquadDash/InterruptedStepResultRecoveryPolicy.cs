namespace SquadDash;

/// <summary>
/// Recognizes a complete step-result envelope returned after its plan loop has already stopped.
/// The result only opens host-controlled commit-range review; it does not bypass Git validation
/// or human acceptance.
/// </summary>
internal static class InterruptedStepResultRecoveryPolicy
{
    internal static bool CanRouteToCommitReview(Plan? plan, DecomposeStepResult? result)
    {
        if (plan is null || result is null ||
            plan.LifecycleStatus != PlanLifecycleStatus.Interrupted ||
            !string.Equals(result.Status, "complete", StringComparison.Ordinal) ||
            !string.Equals(result.GroupId, plan.PlanId, StringComparison.Ordinal) ||
            !string.Equals(result.Revision, plan.Revision, StringComparison.Ordinal) ||
            !string.Equals(result.TaskId, plan.InterruptionData?.InterruptedTaskId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(result.Commit) ||
            !string.Equals(result.Verification?.Status, "passed", StringComparison.OrdinalIgnoreCase))
            return false;

        return plan.Tasks.Any(task =>
            string.Equals(task.TaskId, result.TaskId, StringComparison.Ordinal) &&
            task.Status is not (PlanTaskStatus.Complete or PlanTaskStatus.Superseded));
    }
}
