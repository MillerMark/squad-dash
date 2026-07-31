using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash;

internal sealed record DeveloperApprovalSimulationStartResult(
    Plan Plan,
    PlanApprovalGate Gate,
    ApprovalReviewSnapshot Snapshot,
    ApprovalClickToken ClickToken,
    string MessageId);

/// <summary>
/// Creates a disposable approval checkpoint for UI development. The synthetic plan remains
/// in memory while the Inbox request and approval action use the production durable/runtime
/// components. No PlanStore entry is created and no execution loop can be started here.
/// </summary>
internal sealed class DeveloperApprovalSimulator
{
    internal const string PlanId = "SQUADDASH-DEVELOPER-APPROVAL-SIMULATION";
    internal const string GateId = "SQUADDASH-DEVELOPER-APPROVAL-SIMULATION-GATE";

    private readonly InboxStore _inbox;
    private readonly PlanApprovalRuntime _runtime;

    internal DeveloperApprovalSimulator(InboxStore inbox)
    {
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _runtime = new PlanApprovalRuntime(
            new DurableApprovalRequestManager(inbox),
            new ApprovalActionCoordinator(),
            (plan, gate, _) => Task.FromResult(BuildSnapshot(plan, gate)));
    }

    internal Plan? CurrentPlan { get; private set; }

    internal bool IsActive => CurrentPlan is not null;

    internal static string MessageId => DurableApprovalRequestManager.BuildMessageId(PlanId);

    internal async Task<DeveloperApprovalSimulationStartResult> StartAsync(
        CancellationToken cancellationToken = default)
    {
        Clear();

        var plan = BuildPlan();
        var advance = await _runtime.AdvanceAsync(plan, cancellationToken).ConfigureAwait(false);
        var gate = advance.NewlyReadyGates.SingleOrDefault()
                   ?? throw new InvalidOperationException("The simulated approval gate did not become ready.");
        var snapshot = advance.ReviewSnapshot
                       ?? throw new InvalidOperationException("The simulated approval review was not created.");
        var token = advance.ClickToken
                    ?? throw new InvalidOperationException("The simulated approval action was not created.");
        var messageId = advance.MessageId
                        ?? throw new InvalidOperationException("The simulated Inbox request was not created.");

        CurrentPlan = advance.UpdatedPlan;
        return new DeveloperApprovalSimulationStartResult(
            advance.UpdatedPlan,
            gate,
            snapshot,
            token,
            messageId);
    }

    internal async Task<ApprovalRuntimeResolutionResult> ApproveAsync(
        ApprovalClickToken clickToken,
        string? note,
        CancellationToken cancellationToken = default)
    {
        if (CurrentPlan is null)
            return new ApprovalRuntimeResolutionResult(ApprovalClickResult.StaleRejected, null, false);

        var resolution = await _runtime.ApproveAsync(
            clickToken,
            CurrentPlan,
            note,
            persistPlan: updated =>
            {
                CurrentPlan = updated;
                return true;
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (resolution.UpdatedPlan is not null)
            CurrentPlan = resolution.UpdatedPlan;
        return resolution;
    }

    internal void Clear()
    {
        _runtime.Actions.Unregister(PlanId);
        _inbox.Delete(MessageId);
        CurrentPlan = null;
    }

    private static Plan BuildPlan()
    {
        var now = DateTimeOffset.UtcNow;
        var completedTask = new PlanTask(
            TaskId: "SIM-APPROVAL-001",
            Title: "Prepare simulated approval evidence",
            Description: "Create representative completed work without changing the repository.",
            DependsOn: [],
            Priority: "medium",
            Status: PlanTaskStatus.Complete,
            CompletedAt: now.AddMinutes(-1),
            CompletionSummary: "Synthetic work is ready for a human review of the approval experience.");
        var downstreamTask = new PlanTask(
            TaskId: "SIM-APPROVAL-002",
            Title: "Continue after simulated approval",
            Description: "Represent work that would become eligible after approval.",
            DependsOn: [completedTask.TaskId],
            Priority: "medium",
            Status: PlanTaskStatus.Pending);
        var gate = new PlanApprovalGate(
            GateId,
            "Review the simulated completed work before allowing the next task to continue.",
            [completedTask.TaskId],
            [downstreamTask.TaskId],
            PlanGateStatus.Pending,
            PlanRevision: "developer-simulation");

        return new Plan(
            PlanId,
            Revision: $"developer-simulation-{Guid.NewGuid():N}",
            Source: PlanSource.Manual,
            LifecycleStatus: PlanLifecycleStatus.Executing,
            Title: "Developer Simulation — Plan Approval Request",
            Branch: "(no branch — simulation only)",
            Summary: "Exercises approval UI without launching AI or modifying plan execution state.",
            Tasks: [completedTask, downstreamTask],
            ApprovalGates: [gate],
            Progress: new PlanProgress(1, 2),
            Timestamps: new PlanTimestamps(now, now, now));
    }

    private static ApprovalReviewSnapshot BuildSnapshot(Plan plan, PlanApprovalGate gate)
    {
        var completedTasks = plan.Tasks
            .Where(task => gate.AfterTaskIds.Contains(task.TaskId, StringComparer.Ordinal))
            .Select(task => new ReviewTaskEntry(
                task.TaskId,
                task.Title ?? task.TaskId,
                task.CompletionSummary,
                []))
            .ToArray();
        var downstreamTasks = plan.Tasks
            .Where(task => gate.BeforeTaskIds.Contains(task.TaskId, StringComparer.Ordinal))
            .Select(task => new DownstreamTaskEntry(
                task.TaskId,
                task.Title ?? task.TaskId,
                task.Status))
            .ToArray();

        return new ApprovalReviewSnapshot(
            plan.PlanId,
            plan.Title,
            plan.Progress.CompletedCount,
            plan.Progress.TotalCount,
            plan.LifecycleStatus,
            gate.GateId,
            gate.Message,
            gate.AfterTaskIds,
            gate.BeforeTaskIds,
            completedTasks,
            downstreamTasks,
            [],
            [],
            DateTimeOffset.UtcNow);
    }
}
