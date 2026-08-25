using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

internal static class PlanReviewActivityKind
{
    internal const string ManualCorrection = "manual-correction";
    internal const string ReviewDiscussion = "review-discussion";
}

/// <summary>
/// Describes conversational activity related to paused plan work without changing the plan's
/// execution boundary. Manual correction is durable; discussion is a routing decision only.
/// </summary>
internal sealed record PlanReviewActivityResponse(
    [property: JsonPropertyName("planId")] string PlanId,
    [property: JsonPropertyName("revision")] string Revision,
    [property: JsonPropertyName("activity")] string Activity,
    [property: JsonPropertyName("taskIds")] IReadOnlyList<string> TaskIds,
    [property: JsonPropertyName("summary")] string Summary);

/// <summary>
/// Tracks the editable correction prompt created from an approval card so its action surface
/// and matching Plan Viewer checkpoint can show one non-duplicable working state.
/// </summary>
internal sealed record PendingPlanReviewCorrectionContext(
    string QueueItemId,
    string PlanId,
    string GateId,
    string TaskId);

/// <summary>
/// Tracks the editable bounded-correction prompt for an interrupted plan task. This is separate
/// from approval-gate corrections so concurrent plans cannot suppress one another's controls.
/// </summary>
internal sealed record PendingPlanTaskCorrectionContext(
    string QueueItemId,
    string PlanId,
    string TaskId);

internal static class PlanTaskCorrectionActivityPolicy
{
    internal static PlanTaskCorrectionActivityState Resolve(
        PendingPlanTaskCorrectionContext? context,
        string planId,
        string taskId,
        bool isPromptRunning,
        string? dispatchedItemId,
        string? dispatchedSourceTag)
    {
        if (context is null ||
            !string.Equals(context.PlanId, planId, StringComparison.Ordinal) ||
            !string.Equals(context.TaskId, taskId, StringComparison.Ordinal))
            return PlanTaskCorrectionActivityState.None;

        var isMatchingDispatch = isPromptRunning &&
            (string.Equals(dispatchedItemId, context.QueueItemId, StringComparison.Ordinal) ||
             string.Equals(
                 dispatchedSourceTag,
                 "plan-recovery-correction-draft",
                 StringComparison.Ordinal));
        return isMatchingDispatch
            ? PlanTaskCorrectionActivityState.Running
            : PlanTaskCorrectionActivityState.Drafting;
    }
}

internal static class PlanReviewCorrectionActivityPolicy
{
    internal static (string PlanId, string GateId)? Resolve(
        (string PlanId, string GateId)? gateResponse,
        PendingPlanReviewCorrectionContext? reviewCorrection) =>
        gateResponse ?? (reviewCorrection is null
            ? null
            : (reviewCorrection.PlanId, reviewCorrection.GateId));

    internal static (string PlanId, string GateId)? ResolveActive(
        (string PlanId, string GateId)? gateResponse,
        PendingPlanReviewCorrectionContext? reviewCorrection,
        bool isPromptRunning)
    {
        if (isPromptRunning && gateResponse is { } activeGateResponse)
            return activeGateResponse;
        return reviewCorrection is null
            ? null
            : (reviewCorrection.PlanId, reviewCorrection.GateId);
    }
}

internal static class PlanReviewCorrectionTaskActivityPolicy
{
    internal static PlanTaskCorrectionActivityState Resolve(
        PendingPlanReviewCorrectionContext? context,
        string planId,
        string taskId,
        bool isPromptRunning,
        string? dispatchedItemId,
        string? dispatchedSourceTag)
    {
        if (context is null ||
            !string.Equals(context.PlanId, planId, StringComparison.Ordinal) ||
            !string.Equals(context.TaskId, taskId, StringComparison.Ordinal))
            return PlanTaskCorrectionActivityState.None;

        var isMatchingDispatch = isPromptRunning &&
            (string.Equals(dispatchedItemId, context.QueueItemId, StringComparison.Ordinal) ||
             string.Equals(
                 dispatchedSourceTag,
                 "plan-review-correction-draft",
                 StringComparison.Ordinal));
        return isMatchingDispatch
            ? PlanTaskCorrectionActivityState.Running
            : PlanTaskCorrectionActivityState.Drafting;
    }
}

internal static class PlanReviewActivityResponseParser
{
    internal const string Marker = "PLAN_REVIEW_ACTIVITY_JSON:";

    internal static bool TryParse(string? text, out PlanReviewActivityResponse? response)
    {
        response = null;
        if (!StructuredJsonBlockParser.TryExtractObject<PlanReviewActivityResponse>(
                text, Marker, out var extraction) || extraction is null)
            return false;

        var payload = extraction.Payload;
        var activity = payload.Activity?.Trim().ToLowerInvariant() ?? string.Empty;
        var taskIds = payload.TaskIds?
            .Where(taskId => !string.IsNullOrWhiteSpace(taskId))
            .Select(taskId => taskId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        if (string.IsNullOrWhiteSpace(payload.PlanId) ||
            string.IsNullOrWhiteSpace(payload.Revision) ||
            activity is not (PlanReviewActivityKind.ManualCorrection or
                PlanReviewActivityKind.ReviewDiscussion) ||
            taskIds.Length == 0 ||
            string.IsNullOrWhiteSpace(payload.Summary))
            return false;

        response = payload with
        {
            Activity = activity,
            TaskIds = taskIds,
            Summary = payload.Summary.Trim(),
        };
        return true;
    }

    internal static string BuildProtocolContext(IReadOnlyList<Plan> plans)
    {
        var boundaries = FindBoundaries(plans);
        if (boundaries.Count == 0) return string.Empty;

        var boundaryLines = boundaries.Select(boundary =>
            $"- planId={boundary.PlanId}; revision={boundary.Revision}; title={boundary.Title}; " +
            $"mode={boundary.Mode}; taskIds=[{string.Join(",", boundary.TaskIds)}]" +
            (boundary.GateId is null ? string.Empty : $"; gateId={boundary.GateId}") +
            $"; manualCorrectionCount={boundary.ManualCorrections.Count}" +
            (boundary.ManualCorrections.Count == 0
                ? string.Empty
                : $"; latestManualCorrection={JsonSerializer.Serialize(boundary.ManualCorrections[^1])}"));
        var example = boundaries[0];
        var manualExample = JsonSerializer.Serialize(new
        {
            planId = example.PlanId,
            revision = example.Revision,
            activity = PlanReviewActivityKind.ManualCorrection,
            taskIds = example.TaskIds,
            summary = "Briefly describe the correction actually attempted or completed in this turn.",
        });
        var discussionExample = JsonSerializer.Serialize(new
        {
            planId = example.PlanId,
            revision = example.Revision,
            activity = PlanReviewActivityKind.ReviewDiscussion,
            taskIds = example.TaskIds,
            summary = "Briefly describe the review topic discussed without repository changes.",
        });

        return $$"""
            ## Conversational work at a human-review boundary
            The plans below remain paused. The user may discuss or directly correct reviewed work
            conversationally without reopening a plan task and without automatically continuing the plan:
            {{string.Join("\n", boundaryLines)}}

            Classify by the action actually taken in this turn:
            - If the user merely reports, investigates, or discusses a problem and no repository change is
              requested or made, respond normally and end with `review-discussion`. This is routing-only and
              does not add durable plan history.
            - If the user asks you to fix or modify the reviewed result directly, do that work normally. The user
              may ask you to delegate it to a different agent. Do not reopen the task, run the plan verifier, or
              continue downstream plan execution. End with `manual-correction`; SquadDash records it so the
              current recovery card can continue from the accumulated work.
            - A bare "fix it" at this boundary means conversational `manual-correction` unless the user explicitly
              asks to reopen, rerun, or send the task back through plan execution.
            - If the user explicitly asks for plan-managed rework, use the existing approval-response or recovery
              protocol instead; do not emit this activity payload.
            - If the user explicitly approves an awaiting gate, use PLAN_GATE_APPROVAL_JSON. If the plan is
              interrupted with completed work awaiting human acceptance, use DECOMPOSE_RECOVERY_JSON with action
              `accept-step-complete`.
            - If the user asks to continue an interrupted plan after conversational corrections, use
              DECOMPOSE_RECOVERY_JSON with action `continue-working`; SquadDash must preserve the accumulated work
              before resuming. For an awaiting approval gate, use `add-amendment` to reconcile and verify the manual
              correction before approval.
            - Unrelated work remains ordinary work and emits no review-activity payload.
            - If more than one listed boundary could match and the user did not identify one, ask which plan or task
              they mean. Do not modify plan state or guess.

            For a direct correction, end with exactly one payload in this form:
            {{Marker}}
            {{manualExample}}

            For related discussion without repository changes, use:
            {{Marker}}
            {{discussionExample}}

            Copy the exact plan, revision, and task IDs from the matching boundary. The payload is host metadata,
            not user-facing prose.
            """;
    }

    internal static string BuildRepairInstruction(IReadOnlyList<Plan> plans) =>
        "The previous response attempted to emit PLAN_REVIEW_ACTIVITY_JSON, but the payload did not match " +
        "the required schema. Do not repeat repository work and do not continue any plan. Reclassify the " +
        "same completed turn and return one corrected payload only.\n\n" + BuildProtocolContext(plans);

    internal static bool IsActiveTarget(Plan plan, string taskId)
    {
        if (!plan.Tasks.Any(task => string.Equals(task.TaskId, taskId, StringComparison.Ordinal)))
            return false;

        if (plan.LifecycleStatus == PlanLifecycleStatus.AwaitingApproval)
            return plan.ApprovalGates.Any(gate =>
                gate.Status == PlanGateStatus.AwaitingApproval &&
                gate.AfterTaskIds.Contains(taskId, StringComparer.Ordinal));

        if (plan.LifecycleStatus is not (PlanLifecycleStatus.Interrupted or PlanLifecycleStatus.Blocked) ||
            !string.Equals(plan.InterruptionData?.InterruptedTaskId, taskId, StringComparison.Ordinal))
            return false;

        var task = plan.Tasks.FirstOrDefault(candidate =>
            string.Equals(candidate.TaskId, taskId, StringComparison.Ordinal));
        if (task?.Status == PlanTaskStatus.HumanReviewRequired)
            return true;

        // Exact host-recorded evidence is also a human decision boundary: the user may accept it
        // or request a bounded conversational correction without rerunning the whole task.
        var evidence = plan.InterruptionData?.TaskCommitEvidence;
        return evidence is not null &&
               string.Equals(evidence.TaskId, taskId, StringComparison.Ordinal) &&
               !string.IsNullOrWhiteSpace(evidence.Commit);
    }

    private static IReadOnlyList<ReviewBoundary> FindBoundaries(IReadOnlyList<Plan> plans)
    {
        var boundaries = new List<ReviewBoundary>();
        foreach (var plan in plans)
        {
            foreach (var gate in plan.ApprovalGates.Where(gate =>
                         gate.Status == PlanGateStatus.AwaitingApproval))
            {
                var taskIds = gate.AfterTaskIds
                    .Where(taskId => plan.Tasks.Any(task =>
                        string.Equals(task.TaskId, taskId, StringComparison.Ordinal)))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (taskIds.Length > 0)
                {
                    var corrections = plan.Tasks
                        .Where(task => taskIds.Contains(task.TaskId, StringComparer.Ordinal))
                        .SelectMany(task => task.ReviewActivity ?? [])
                        .Where(activity => activity.Kind == PlanReviewActivityKind.ManualCorrection)
                        .OrderBy(activity => activity.RecordedAt)
                        .Select(activity => activity.Summary)
                        .ToArray();
                    boundaries.Add(new ReviewBoundary(
                        plan.PlanId, plan.Revision, plan.Title, "approval-gate", taskIds, gate.GateId, corrections));
                }
            }

            if (plan.LifecycleStatus is not (PlanLifecycleStatus.Interrupted or PlanLifecycleStatus.Blocked))
                continue;
            var interruptedTaskId = plan.InterruptionData?.InterruptedTaskId;
            if (string.IsNullOrWhiteSpace(interruptedTaskId) ||
                !IsActiveTarget(plan, interruptedTaskId))
                continue;
            var interruptedCorrections = plan.Tasks
                .First(task => string.Equals(task.TaskId, interruptedTaskId, StringComparison.Ordinal))
                .ReviewActivity?
                .Where(activity => activity.Kind == PlanReviewActivityKind.ManualCorrection)
                .OrderBy(activity => activity.RecordedAt)
                .Select(activity => activity.Summary)
                .ToArray() ?? [];
            boundaries.Add(new ReviewBoundary(
                plan.PlanId, plan.Revision, plan.Title,
                plan.InterruptionData?.TaskCommitEvidence is null
                    ? "interrupted-human-review"
                    : "interrupted-completed-work-review",
                [interruptedTaskId], null, interruptedCorrections));
        }
        return boundaries;
    }

    private sealed record ReviewBoundary(
        string PlanId,
        string Revision,
        string Title,
        string Mode,
        IReadOnlyList<string> TaskIds,
        string? GateId,
        IReadOnlyList<string> ManualCorrections);
}
