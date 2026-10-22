namespace SquadDash;

/// <summary>
/// Pure, UI-free decisions for the Plans-panel-row "Pause plan"/"Resume plan" toggle and for
/// deciding whether an approval/review card may render while a plan is paused.
/// <para>
/// These decisions depend ONLY on the supplied <see cref="Plan"/> state — never on runtime loop
/// state, focus, or call history. Keeping them pure makes every branch exhaustively unit-testable
/// without a WPF dispatcher, mirroring <see cref="EmbeddedCaretNavMath"/>.
/// </para>
/// </summary>
internal static class PlanPauseMenuPolicy
{
    internal const string PauseLabel  = "Pause plan";
    internal const string ResumeLabel = "Resume plan";

    /// <summary>
    /// The pause/resume toggle (and its trailing separator) is offered ONLY for a plan that has
    /// STARTED but has not reached a terminal state. "Started" means the plan is executing or has
    /// reached a mid-flight gate/interruption. An approved-but-never-executed plan is "not started"
    /// (hidden); stopped/completed/archived plans are terminal (hidden).
    /// </summary>
    internal static bool IsPauseResumeAvailable(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.LifecycleStatus is
            PlanLifecycleStatus.Executing or
            PlanLifecycleStatus.AwaitingApproval or
            PlanLifecycleStatus.Interrupted or
            PlanLifecycleStatus.Blocked or
            PlanLifecycleStatus.WaitingForCleanWorktree;
    }

    /// <summary>
    /// The toggle label: "Pause plan" while the plan runs normally, "Resume plan" once paused.
    /// A pure function of <see cref="Plan.IsPaused"/>, independent of running/awaiting state.
    /// </summary>
    internal static string ToggleLabel(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.IsPaused ? ResumeLabel : PauseLabel;
    }

    /// <summary>
    /// The single suppression predicate every transcript approval/review-card insertion routes
    /// through: a paused plan must never render an approval card (including on startup rehydration).
    /// A null plan (e.g. the durable record is missing) suppresses nothing.
    /// </summary>
    internal static bool ShouldSuppressApprovalCard(Plan? plan) => plan?.IsPaused == true;
}
