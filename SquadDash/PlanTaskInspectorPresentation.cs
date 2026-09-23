using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

internal sealed record PlanTaskInspectorActivitySummary(
    PlanTaskActivityState State,
    string Label,
    string Description,
    string ColorKey,
    bool ShouldShowCallout);

internal static class PlanTaskInspectorPresentation
{
    internal static IReadOnlyList<PlanApprovalGate> SelectApprovalGuidanceGates(PlanTask task, Plan? plan)
    {
        if (plan is null)
            return [];

        return plan.ApprovalGates
            .Where(gate => ShouldShowApprovalGuidance(task, gate))
            .DistinctBy(gate => gate.GateId, StringComparer.Ordinal)
            .ToArray();
    }

    internal static bool ShouldShowApprovalGuidance(PlanTask task, PlanApprovalGate gate)
    {
        var startsAfterThisTask = gate.AfterTaskIds.Contains(task.TaskId, StringComparer.Ordinal);
        if (startsAfterThisTask)
            return true;

        var startsBeforeThisTask = gate.BeforeTaskIds.Contains(task.TaskId, StringComparer.Ordinal);
        if (!startsBeforeThisTask)
            return false;

        return !IsResolved(gate);
    }

    internal static PlanTaskActivityState ResolveActivityState(
        PlanTask task,
        Plan? plan,
        bool hasMatchingLiveRound = true,
        bool isRecoveryAssessmentActive = false,
        bool isCorrectionRunning = false)
    {
        var state = plan is null
            ? ResolveStatusFallback(task.Status)
            : PlanTaskActivityResolver.Resolve(plan).TryGetValue(task.TaskId, out var resolved)
                ? PlanTaskActivityPresentation.ResolveLiveState(resolved, hasMatchingLiveRound)
                : ResolveStatusFallback(task.Status);

        if (isRecoveryAssessmentActive)
            state = PlanTaskActivityState.Assessing;
        if (isCorrectionRunning)
            state = PlanTaskActivityState.Reworking;

        return state;
    }

    internal static PlanTaskInspectorActivitySummary? BuildActivitySummary(
        PlanTask task,
        Plan? plan,
        bool hasMatchingLiveRound = true,
        bool isRecoveryAssessmentActive = false,
        bool isCorrectionRunning = false)
    {
        var state = ResolveActivityState(
            task,
            plan,
            hasMatchingLiveRound,
            isRecoveryAssessmentActive,
            isCorrectionRunning);

        return state switch
        {
            PlanTaskActivityState.Executing => new(
                state,
                "Working now",
                "This step is actively receiving implementation work.",
                "ActivePanelTitle",
                ShouldShowCallout: true),
            PlanTaskActivityState.Assessing => new(
                state,
                "Assessing existing work",
                "SquadDash is checking repository state before continuing this step.",
                "ActivePanelTitle",
                ShouldShowCallout: true),
            PlanTaskActivityState.Verifying => new(
                state,
                "Verifying now",
                "SquadDash is independently checking the candidate work for this step.",
                "ActivePanelTitle",
                ShouldShowCallout: true),
            PlanTaskActivityState.VerificationPending => new(
                state,
                "Verification pending",
                "Candidate work is saved and waiting for its verification pass.",
                "PriorityMid",
                ShouldShowCallout: true),
            PlanTaskActivityState.Reworking => new(
                state,
                "Correcting now",
                "This step is receiving requested correction work.",
                "PriorityMid",
                ShouldShowCallout: true),
            _ => null,
        };
    }

    private static bool IsResolved(PlanApprovalGate gate) =>
        string.Equals(gate.Status, PlanGateStatus.Approved, StringComparison.Ordinal) ||
        string.Equals(gate.Status, PlanGateStatus.Skipped, StringComparison.Ordinal);

    private static PlanTaskActivityState ResolveStatusFallback(string? status) => status switch
    {
        PlanTaskStatus.Complete or PlanTaskStatus.Superseded => PlanTaskActivityState.Completed,
        PlanTaskStatus.Failed => PlanTaskActivityState.Blocked,
        PlanTaskStatus.HumanReviewRequired => PlanTaskActivityState.AwaitingApproval,
        PlanTaskStatus.VerificationPending => PlanTaskActivityState.VerificationPending,
        PlanTaskStatus.Reworking => PlanTaskActivityState.Reworking,
        PlanTaskStatus.Partial => PlanTaskActivityState.Interrupted,
        PlanTaskStatus.Executing => PlanTaskActivityState.Executing,
        _ when PlanTaskStatus.IsVerifying(status) => PlanTaskActivityState.Verifying,
        _ => PlanTaskActivityState.Queued,
    };
}
