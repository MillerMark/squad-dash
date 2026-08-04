using System;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// Encapsulates the logic for subscribing a PlanViewerWindow to live <see cref="PlanProgressEvent"/>
/// updates via <see cref="WeakEventBroker"/>. Handles event filtering by PlanId, stale-event
/// rejection, rapid-event coalescence, and subscription lifecycle (detach on close).
/// Designed to be testable independently of WPF visual tree rendering.
/// </summary>
internal sealed class PlanViewerLiveSyncHandler
{
    private const int CoalesceDelayMs = 80;

    private readonly string _planId;
    private readonly WeakEventBroker _broker;
    private readonly Action<Plan> _applyUpdate;
    private readonly Dispatcher? _dispatcher;
    private readonly DispatcherTimer? _coalesceTimer;

    private Plan? _currentPlan;
    private Plan? _pendingUpdate;
    private bool _disposed;

    // Strong reference to the delegate prevents the WeakEventBroker from GC-ing the subscription.
    private readonly Action<PlanProgressEvent> _handler;

    internal PlanViewerLiveSyncHandler(
        string planId,
        Plan initialPlan,
        WeakEventBroker broker,
        Action<Plan> applyUpdate,
        Dispatcher? dispatcher = null)
    {
        _planId = planId;
        _currentPlan = initialPlan;
        _broker = broker;
        _applyUpdate = applyUpdate;
        _dispatcher = dispatcher;
        _handler = OnPlanProgressEvent;

        if (dispatcher is not null)
        {
            _coalesceTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(CoalesceDelayMs),
            };
            _coalesceTimer.Tick += OnCoalesceTimerTick;
        }

        _broker.Subscribe(_handler);
    }

    /// <summary>The most recently applied plan state (for test assertions).</summary>
    internal Plan? CurrentPlan => _currentPlan;

    /// <summary>Count of updates successfully applied (for test assertions).</summary>
    internal int AppliedCount { get; private set; }

    /// <summary>Count of events rejected as stale (for test assertions).</summary>
    internal int RejectedCount { get; private set; }

    internal void Detach()
    {
        if (_disposed) return;
        _disposed = true;
        _coalesceTimer?.Stop();
        _broker.Unsubscribe(_handler);
    }

    private void OnPlanProgressEvent(PlanProgressEvent evt)
    {
        if (_disposed) return;
        if (!string.Equals(evt.PlanId, _planId, StringComparison.Ordinal)) return;

        if (IsStale(evt.UpdatedPlan))
        {
            RejectedCount++;
            SquadDashTrace.Write(TraceCategory.General,
                $"PlanViewerLiveSync: rejected stale event for {_planId} " +
                $"(incoming={evt.UpdatedPlan.Progress.CompletedCount}, current={_currentPlan?.Progress.CompletedCount})");
            return;
        }

        // Validation/task/lifecycle transitions are user-visible state, not noisy activity
        // pulses. Apply them immediately so an application restart requested directly after a
        // transition cannot strand an open viewer on the preceding blue/executing state.
        if (HasVisibleStateTransition(_currentPlan, evt.UpdatedPlan))
        {
            _coalesceTimer?.Stop();
            _pendingUpdate = null;
            ApplyOnDispatcher(evt.UpdatedPlan);
        }
        else if (_coalesceTimer is not null)
        {
            _pendingUpdate = evt.UpdatedPlan;
            _coalesceTimer.Stop();
            _coalesceTimer.Start();
        }
        else
        {
            ApplyNow(evt.UpdatedPlan);
        }
    }

    private void ApplyOnDispatcher(Plan plan)
    {
        if (_dispatcher is not null && !_dispatcher.CheckAccess())
            _dispatcher.Invoke(() => ApplyNow(plan));
        else
            ApplyNow(plan);
    }

    private static bool HasVisibleStateTransition(Plan? current, Plan incoming)
    {
        if (current is null ||
            !string.Equals(current.LifecycleStatus, incoming.LifecycleStatus, StringComparison.Ordinal) ||
            !string.Equals(current.Progress.ExecutingTaskId, incoming.Progress.ExecutingTaskId, StringComparison.Ordinal) ||
            current.Progress.CompletedCount != incoming.Progress.CompletedCount)
            return true;

        var currentTasks = current.Tasks.ToDictionary(task => task.TaskId, task => task.Status, StringComparer.Ordinal);
        if (incoming.Tasks.Any(task => !currentTasks.TryGetValue(task.TaskId, out var status) || status != task.Status))
            return true;

        var currentValidations = (current.Validations ?? []).ToDictionary(
            validation => validation.ValidationId,
            validation => validation.Status,
            StringComparer.Ordinal);
        return (incoming.Validations ?? []).Any(validation =>
            !currentValidations.TryGetValue(validation.ValidationId, out var status) ||
            status != validation.Status);
    }

    private void OnCoalesceTimerTick(object? sender, EventArgs e)
    {
        _coalesceTimer!.Stop();
        if (_pendingUpdate is not null)
        {
            ApplyNow(_pendingUpdate);
            _pendingUpdate = null;
        }
    }

    private void ApplyNow(Plan plan)
    {
        if (_disposed) return;
        _currentPlan = plan;
        AppliedCount++;
        _applyUpdate(plan);
    }

    private bool IsStale(Plan incoming)
    {
        if (_currentPlan is null) return false;
        return incoming.Progress.CompletedCount < _currentPlan.Progress.CompletedCount;
    }

    /// <summary>
    /// Directly handle an event without coalescence (for testing without a dispatcher).
    /// </summary>
    internal void HandleEventDirect(PlanProgressEvent evt)
    {
        OnPlanProgressEvent(evt);
    }
}
