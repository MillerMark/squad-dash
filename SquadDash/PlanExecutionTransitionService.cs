namespace SquadDash;

/// <summary>Outcome of a plan start or resume attempt.</summary>
internal enum ExecutionTransitionOutcome
{
    /// <summary>Plan was successfully transitioned to Executing.</summary>
    Started,

    /// <summary>Plan was already in the Executing state — no change applied.</summary>
    AlreadyExecuting,

    /// <summary>Plan is in a terminal state and cannot be started or resumed.</summary>
    TerminalPlan,

    /// <summary>Plan is not in the expected lifecycle status for the requested transition.</summary>
    InvalidStatus,
}

/// <summary>
/// Owns the host-side start and resume transitions for plans.
/// Pure logic + persistence; no UI dependencies.
/// </summary>
internal sealed class PlanExecutionTransitionService
{
    private readonly PlanStore _store;

    internal PlanExecutionTransitionService(PlanStore store)
    {
        _store = store;
    }

    /// <summary>Result of a start or resume attempt.</summary>
    internal sealed record TransitionResult(
        Plan? Plan,
        ExecutionTransitionOutcome Outcome,
        string? Message = null);

    /// <summary>
    /// Attempts to start an <see cref="PlanLifecycleStatus.Approved"/> plan.
    /// Sets lifecycle to Executing, stamps <see cref="PlanTimestamps.StartedAt"/>,
    /// and persists via <see cref="PlanStore"/>.
    /// </summary>
    internal TransitionResult Start(Plan plan, DateTimeOffset timestamp)
    {
        if (PlanLifecycleStatus.IsTerminal(plan.LifecycleStatus))
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"PlanExecutionTransitionService.Start: rejected terminal plan '{plan.PlanId}' (status={plan.LifecycleStatus}).");
            return new TransitionResult(plan, ExecutionTransitionOutcome.TerminalPlan,
                $"Plan '{plan.PlanId}' is in terminal status '{plan.LifecycleStatus}' and cannot be started.");
        }

        if (plan.LifecycleStatus == PlanLifecycleStatus.Executing)
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"PlanExecutionTransitionService.Start: plan '{plan.PlanId}' is already executing — idempotent guard.");
            return new TransitionResult(plan, ExecutionTransitionOutcome.AlreadyExecuting);
        }

        if (plan.LifecycleStatus != PlanLifecycleStatus.Approved)
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"PlanExecutionTransitionService.Start: plan '{plan.PlanId}' is in status '{plan.LifecycleStatus}' — expected Approved.");
            return new TransitionResult(plan, ExecutionTransitionOutcome.InvalidStatus,
                $"Plan '{plan.PlanId}' must be in Approved status to start (current: {plan.LifecycleStatus}).");
        }

        var updated = plan with
        {
            LifecycleStatus = PlanLifecycleStatus.Executing,
            Timestamps = plan.Timestamps with
            {
                StartedAt = timestamp,
            },
        };

        _store.Save(updated);
        SquadDashTrace.Write(TraceCategory.General,
            $"PlanExecutionTransitionService.Start: plan '{plan.PlanId}' transitioned Approved → Executing.");
        return new TransitionResult(updated, ExecutionTransitionOutcome.Started);
    }

    /// <summary>
    /// Attempts to resume an <see cref="PlanLifecycleStatus.Interrupted"/> plan.
    /// Sets lifecycle to Executing, clears interruption recovery state, and persists.
    /// </summary>
    internal TransitionResult Resume(Plan plan, DateTimeOffset timestamp)
    {
        if (PlanLifecycleStatus.IsTerminal(plan.LifecycleStatus))
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"PlanExecutionTransitionService.Resume: rejected terminal plan '{plan.PlanId}' (status={plan.LifecycleStatus}).");
            return new TransitionResult(plan, ExecutionTransitionOutcome.TerminalPlan,
                $"Plan '{plan.PlanId}' is in terminal status '{plan.LifecycleStatus}' and cannot be resumed.");
        }

        if (plan.LifecycleStatus == PlanLifecycleStatus.Executing)
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"PlanExecutionTransitionService.Resume: plan '{plan.PlanId}' is already executing — idempotent guard.");
            return new TransitionResult(plan, ExecutionTransitionOutcome.AlreadyExecuting);
        }

        if (plan.LifecycleStatus != PlanLifecycleStatus.Interrupted)
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"PlanExecutionTransitionService.Resume: plan '{plan.PlanId}' is in status '{plan.LifecycleStatus}' — expected Interrupted.");
            return new TransitionResult(plan, ExecutionTransitionOutcome.InvalidStatus,
                $"Plan '{plan.PlanId}' must be in Interrupted status to resume (current: {plan.LifecycleStatus}).");
        }

        var recoveredInterruption = plan.InterruptionData is not null
            ? plan.InterruptionData with { RecoveryState = PlanRecoveryState.Recovered }
            : null;

        var updated = plan with
        {
            LifecycleStatus = PlanLifecycleStatus.Executing,
            InterruptionData = recoveredInterruption,
        };

        _store.Save(updated);
        SquadDashTrace.Write(TraceCategory.General,
            $"PlanExecutionTransitionService.Resume: plan '{plan.PlanId}' transitioned Interrupted → Executing.");
        return new TransitionResult(updated, ExecutionTransitionOutcome.Started);
    }
}
