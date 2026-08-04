using System;
using System.Collections.Generic;
using System.Threading;

namespace SquadDash;

/// <summary>
/// Timer-driven simulation orchestrator that drives a disposable plan validation node through
/// Ready → Validating → (Passed | Failed) → Stale → Ready in a continuous loop.
/// Publishes production <see cref="PlanProgressEvent"/> and <see cref="PlanValidationActivityPulseEvent"/>
/// via <see cref="WeakEventBroker"/> so that open Plan Viewers exercise the same live-sync
/// and activity-spinner paths used during real plan execution.
/// </summary>
internal sealed class ValidationStateSimulator : IDisposable
{
    internal const string PlanId = "SQUADDASH-DEVELOPER-VALIDATION-SIMULATION";
    internal const string ValidationId = "SIM-VALIDATION-001";
    private const string TaskId = "SIM-VALIDATION-TASK-001";

    private readonly WeakEventBroker _broker;
    private readonly PlanStore? _planStore;
    private readonly int _stepIntervalMs;
    private Timer? _stateTimer;
    private Timer? _pulseTimer;
    private bool _disposed;
    private int _cycleCount;

    /// <summary>Current simulation state in the state machine.</summary>
    internal SimulationPhase Phase { get; private set; } = SimulationPhase.Idle;

    /// <summary>The current disposable plan reflecting the latest validation state.</summary>
    internal Plan? CurrentPlan { get; private set; }

    /// <summary>Number of full Ready→…→Stale cycles completed (for test assertions).</summary>
    internal int CycleCount => _cycleCount;

    /// <summary>Log of events published during the simulation (for test assertions).</summary>
    internal List<object> PublishedEvents { get; } = new();

    /// <summary>Whether the next result transition should simulate a failure (alternates).</summary>
    internal bool NextResultIsFailed { get; set; }

    internal ValidationStateSimulator(
        WeakEventBroker broker,
        PlanStore? planStore = null,
        int stepIntervalMs = 2000)
    {
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _planStore = planStore;
        _stepIntervalMs = stepIntervalMs;
    }

    /// <summary>Creates the disposable plan and begins the timer-driven state loop.</summary>
    internal Plan Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ValidationStateSimulator));
        if (Phase != SimulationPhase.Idle)
            throw new InvalidOperationException("Simulation is already running.");

        CurrentPlan = BuildInitialPlan();
        PersistAndPublish(CurrentPlan);
        Phase = SimulationPhase.Ready;

        _stateTimer = new Timer(OnStateTimerTick, null, _stepIntervalMs, Timeout.Infinite);
        return CurrentPlan;
    }

    /// <summary>Advances the simulation to the next state (for testing without timers).</summary>
    internal void AdvanceState()
    {
        if (_disposed || CurrentPlan is null) return;

        switch (Phase)
        {
            case SimulationPhase.Ready:
                TransitionToValidating();
                break;
            case SimulationPhase.Validating:
                TransitionToResult();
                break;
            case SimulationPhase.Passed:
            case SimulationPhase.Failed:
                TransitionToStale();
                break;
            case SimulationPhase.Stale:
                TransitionToReady();
                _cycleCount++;
                break;
        }
    }

    /// <summary>Stops timers and removes simulation plan from the store.</summary>
    internal void CleanUp()
    {
        StopTimers();
        if (_planStore is not null)
        {
            try { _planStore.Delete(PlanId); }
            catch (Exception ex)
            {
                SquadDashTrace.Write("Simulation", $"CleanUp delete failed: {ex.Message}");
            }
        }
        CurrentPlan = null;
        Phase = SimulationPhase.Idle;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopTimers();
    }

    // ── State transitions ─────────────────────────────────────────────────────

    private void TransitionToValidating()
    {
        CurrentPlan = PlanStoreUpdater.ApplyValidationStarted(CurrentPlan!, ValidationId);
        Phase = SimulationPhase.Validating;
        PersistAndPublish(CurrentPlan);
        StartPulseTimer();
    }

    private void TransitionToResult()
    {
        StopPulseTimer();
        var passed = !NextResultIsFailed;
        NextResultIsFailed = !NextResultIsFailed; // alternate for next cycle
        CurrentPlan = PlanStoreUpdater.ApplyValidationResult(
            CurrentPlan!,
            ValidationId,
            passed,
            passed ? "All assertions verified successfully (simulation)." : "Assertion check failed (simulation).",
            passed ? ["✓ Build output verified", "✓ Test suite passed"] : ["✗ Contract assertion failed"],
            validatedCommit: null);
        Phase = passed ? SimulationPhase.Passed : SimulationPhase.Failed;
        PersistAndPublish(CurrentPlan);
    }

    private void TransitionToStale()
    {
        CurrentPlan = PlanStoreUpdater.ApplyValidationStale(
            CurrentPlan!, ValidationId, "Upstream task output changed (simulation).");
        // ApplyValidationStale only transitions from Passed. For Failed, manually set Stale.
        if (Phase == SimulationPhase.Failed)
        {
            CurrentPlan = ForceValidationStatus(CurrentPlan, ValidationId, PlanValidationStatus.Stale);
        }
        Phase = SimulationPhase.Stale;
        PersistAndPublish(CurrentPlan);
    }

    private void TransitionToReady()
    {
        CurrentPlan = PlanStoreUpdater.ApplyValidationReady(CurrentPlan!, ValidationId);
        // ApplyValidationReady only transitions from Pending/Stale. Ensure it applied.
        if (GetValidationStatus(CurrentPlan) != PlanValidationStatus.Ready)
        {
            CurrentPlan = ForceValidationStatus(CurrentPlan, ValidationId, PlanValidationStatus.Ready);
        }
        Phase = SimulationPhase.Ready;
        PersistAndPublish(CurrentPlan);
    }

    // ── Timer callbacks ───────────────────────────────────────────────────────

    private void OnStateTimerTick(object? state)
    {
        if (_disposed) return;
        AdvanceState();
        _stateTimer?.Change(_stepIntervalMs, Timeout.Infinite);
    }

    private void StartPulseTimer()
    {
        _pulseTimer?.Dispose();
        _pulseTimer = new Timer(OnPulseTimerTick, null, 0, 400);
    }

    private void StopPulseTimer()
    {
        _pulseTimer?.Dispose();
        _pulseTimer = null;
    }

    private void OnPulseTimerTick(object? state)
    {
        if (_disposed || Phase != SimulationPhase.Validating) return;
        var pulse = new PlanValidationActivityPulseEvent(PlanId, ValidationId, SpinnerActivityKind.Thinking);
        PublishedEvents.Add(pulse);
        _broker.Publish(pulse);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void PersistAndPublish(Plan plan)
    {
        _planStore?.Save(plan);
        var evt = new PlanProgressEvent(plan.PlanId, plan);
        PublishedEvents.Add(evt);
        _broker.Publish(evt);
    }

    private void StopTimers()
    {
        _stateTimer?.Dispose();
        _stateTimer = null;
        StopPulseTimer();
    }

    private static string? GetValidationStatus(Plan plan)
    {
        if (plan.Validations is null) return null;
        foreach (var v in plan.Validations)
            if (string.Equals(v.ValidationId, ValidationId, StringComparison.Ordinal))
                return v.Status;
        return null;
    }

    private static Plan ForceValidationStatus(Plan plan, string validationId, string status)
    {
        if (plan.Validations is null) return plan;
        var updated = new List<PlanValidationNode>(plan.Validations.Count);
        foreach (var v in plan.Validations)
            updated.Add(string.Equals(v.ValidationId, validationId, StringComparison.Ordinal)
                ? v with { Status = status }
                : v);
        return plan with { Validations = updated };
    }

    private static Plan BuildInitialPlan()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new PlanTask(
            TaskId: TaskId,
            Title: "Simulated prerequisite task",
            Description: "Represents completed work preceding the validation node.",
            DependsOn: [],
            Priority: "medium",
            Status: PlanTaskStatus.Complete,
            CompletedAt: now.AddMinutes(-2),
            CompletionSummary: "Synthetic task completed for validation simulation.");

        var validation = new PlanValidationNode(
            ValidationId: ValidationId,
            Title: "Simulated cross-task validation",
            Description: "Exercises the shield state machine without repository mutations.",
            AfterTaskIds: [TaskId],
            BeforeTaskIds: [],
            Assertions: ["Build output is clean", "All tests pass"],
            OutputIds: null,
            Mode: "automated",
            Commands: null,
            RevalidateAtCompletion: true,
            Status: PlanValidationStatus.Ready);

        return new Plan(
            PlanId: PlanId,
            Revision: $"dev-validation-sim-{Guid.NewGuid():N}",
            Source: PlanSource.Manual,
            LifecycleStatus: PlanLifecycleStatus.Executing,
            Title: "Developer Simulation — Validation States",
            Branch: "(no branch — simulation only)",
            Summary: "Drives a validation node through Ready → Validating → Passed/Failed → Stale in a loop.",
            Tasks: [task],
            ApprovalGates: [],
            Progress: new PlanProgress(CompletedCount: 1, TotalCount: 1),
            Timestamps: new PlanTimestamps(CreatedAt: now, StartedAt: now, LastRunAt: now),
            Validations: [validation]);
    }

    internal enum SimulationPhase
    {
        Idle,
        Ready,
        Validating,
        Passed,
        Failed,
        Stale,
    }
}
