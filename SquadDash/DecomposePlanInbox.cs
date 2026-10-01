using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

internal sealed record DecomposePlanActionDefinition(
    string Label,
    string Action,
    string? Branch,
    string Hint);

/// <summary>Builds and reads host-owned Inbox messages for pending decomposition plans.</summary>
internal static class DecomposePlanInbox
{
    internal const string AttachmentType = "decompose-plan";
    internal const string ActionRouteMode = "decompose";
    internal const string RecoveryRouteMode = "decompose-recovery";
    internal const string RecoveryEvidenceRouteMode = "decompose-recovery-evidence";

    private static readonly JsonSerializerOptions SnapshotOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions DecisionOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static bool RequestsInboxDelivery(DecomposedTaskGroup group) =>
        string.Equals(group.Delivery, "inbox", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A transcript-only proposal that survives a workspace transition no longer has a reliable
    /// nearby action surface. Promote it to the workspace Inbox so it remains discoverable without
    /// requiring another Coordinator turn in that workspace.
    /// </summary>
    internal static bool ShouldCreateAutomaticReminderOnWorkspaceHydration(
        PendingDecomposePlan plan,
        bool reminderExistsIncludingArchive) =>
        !RequestsInboxDelivery(plan.Group) && !reminderExistsIncludingArchive;

    internal static bool IsUnreadAutomaticReminder(
        PendingDecomposePlan plan,
        InboxMessage? message)
    {
        if (message is null || message.Read || RequestsInboxDelivery(plan.Group) ||
            !string.Equals(message.Id, BuildMessageId(plan), StringComparison.Ordinal))
        {
            return false;
        }

        return TryGetPlanAttachment(message, plan, out _);
    }

    internal static bool TryGetPlanAttachment(
        InboxMessage message,
        PendingDecomposePlan plan,
        out InboxAttachment? attachment)
    {
        attachment = message.Attachments.FirstOrDefault(candidate =>
            string.Equals(candidate.Type, AttachmentType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.PlanGroupId, plan.Group.GroupId, StringComparison.Ordinal) &&
            string.Equals(candidate.PlanRevision, plan.Revision, StringComparison.Ordinal));
        return attachment is not null;
    }

    internal static string BuildMessageId(PendingDecomposePlan plan) =>
        $"decompose-plan-{plan.Group.GroupId}-{plan.Revision}";

    internal static string BuildRecoveryMessageId(PendingDecomposePlan plan, string taskId) =>
        $"decompose-recovery-{plan.Group.GroupId}-{taskId}-{plan.Revision}";

    internal static bool ResponseAddressesPlan(PendingDecomposePlan plan, string? rawResponse)
    {
        if (DecomposeDecisionParser.TryParse(rawResponse, out var decision) && decision is not null &&
            string.Equals(decision.GroupId, plan.Group.GroupId, StringComparison.Ordinal) &&
            string.Equals(decision.Revision, plan.Revision, StringComparison.Ordinal))
            return true;
        if (StagedPlanRevisionProposalParser.TryParse(rawResponse, out var stagedRevision, out _) &&
            stagedRevision is not null &&
            string.Equals(stagedRevision.GroupId, plan.Group.GroupId, StringComparison.Ordinal) &&
            string.Equals(stagedRevision.BaseRevision, plan.Revision, StringComparison.Ordinal))
            return true;
        return TasksJsonParser.TryParse(rawResponse ?? string.Empty, out var replacement) &&
               replacement is not null &&
               string.Equals(replacement.GroupId, plan.Group.GroupId, StringComparison.Ordinal);
    }

    internal static InboxMessage BuildMessage(
        PendingDecomposePlan plan,
        DateTimeOffset timestamp,
        bool explicitlyRequested,
        string? activeBranch = null)
    {
        var group = plan.Group;
        var reason = explicitlyRequested
            ? "This decomposition plan was sent to your Inbox as requested."
            : "This decomposition plan was staged in the transcript but was not acted on before the conversation moved on.";
        var actions = BuildActionDefinitions(plan, activeBranch)
            .Select(action => BuildInboxAction(plan, action))
            .ToList();

        return new InboxMessage
        {
            Id = BuildMessageId(plan),
            Subject = $"Pending plan: {group.GroupTitle}",
            From = "SquadDash",
            Timestamp = timestamp,
            Read = false,
            Priority = "high",
            Body = $"{reason}\n\n" +
                   $"**{group.GroupTitle}**  \n" +
                   $"{group.Summary}\n\n" +
                   $"Proposed branch: `{group.Branch}`  \n" +
                   $"Tasks: {group.Tasks.Count}  \n" +
                   $"Plan revision: `{plan.Revision}`\n\n" +
                   "Open the attached dependency graph to inspect the plan, then choose an action above.",
            Attachments =
            [
                new InboxAttachment
                {
                    Type = AttachmentType,
                    Label = "View plan and dependencies",
                    PlanGroupId = group.GroupId,
                    PlanRevision = plan.Revision,
                    // The IDs point at the live pending plan. The snapshot keeps the attachment
                    // viewable for audit if the pending file is later accepted and removed.
                    Content = JsonSerializer.Serialize(plan, SnapshotOptions),
                }
            ],
            Actions = actions,
        };
    }

    internal static InboxMessage BuildRecoveryMessage(
        PendingDecomposePlan plan,
        string taskId,
        string reason,
        DateTimeOffset timestamp,
        PlanTaskCommitEvidence? taskCommitEvidence = null,
        Plan? durablePlan = null,
        string? currentHead = null,
        Func<string, string, bool>? isAncestor = null)
    {
        InboxAction BuildAction(string label, string action, string hint) => new()
        {
            Label = label,
            RouteMode = RecoveryRouteMode,
            Prompt = DecomposeRecoveryDecisionParser.Marker + "\n" + JsonSerializer.Serialize(
                new DecomposeRecoveryDecision(plan.Group.GroupId, plan.Revision, action),
                DecisionOptions),
            Hint = hint,
        };

        var hasCommitEvidence = taskCommitEvidence is not null &&
            string.Equals(taskCommitEvidence.TaskId, taskId, StringComparison.Ordinal);
        var completedWorkReview = durablePlan is null
            ? null
            : CompletedWorkReviewPresentationBuilder.Build(durablePlan, taskId);
        var isAmendment = plan.Group.Tasks.Any(task =>
            string.Equals(task.Id, taskId, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(task.AmendmentGateId));
        var taskIndex = plan.Group.Tasks.ToList().FindIndex(task =>
            string.Equals(task.Id, taskId, StringComparison.Ordinal));
        var taskTitle = plan.Group.Tasks.FirstOrDefault(task =>
            string.Equals(task.Id, taskId, StringComparison.Ordinal))?.Title ?? taskId;
        var stepLabel = PlanRecoveryPresentationBuilder.FormatStepLabel(
            taskIndex >= 0 ? (taskIndex + 1).ToString() : null,
            taskTitle);
        var reviewCard = durablePlan is null
            ? null
            : PlanRecoveryPresentationBuilder.BuildHumanReviewCard(durablePlan, taskId);
        var reviewAttention = durablePlan is null
            ? null
            : PlanReviewAttentionPresentationResolver.Resolve(durablePlan, taskId);
        var attentionCard = durablePlan is null
            ? new PlanAttentionCardPresentation(
                PlanAttentionCategory.UnexpectedState,
                "Unexpected State Encountered",
                "SquadDash encountered an unexpected state and stopped the plan to preserve its current work.",
                reason,
                ShowDiagnosticsActions: true)
            : PlanAttentionCardPresentationBuilder.Build(
                durablePlan,
                taskId,
                reviewCard,
                (reviewAttention?.Checklist.Count ?? 0) > 0);
        InboxAction BuildEvidenceLink() => new()
        {
            Label = completedWorkReview?.ComparisonLabel ??
                    (hasCommitEvidence
                        ? PlanRecoveryPresentationBuilder.BuildCommittedReviewLabel(
                            stepLabel,
                            taskCommitEvidence!.BaselineCommit,
                            taskCommitEvidence.Commit)
                        : PlanRecoveryPresentationBuilder.BuildUnattributedReviewLabel(
                            stepLabel,
                            durablePlan?.InterruptionData?.LastCommit)),
            RouteMode = RecoveryEvidenceRouteMode,
            Prompt = DecomposeRecoveryDecisionParser.Marker + "\n" + JsonSerializer.Serialize(
                new DecomposeRecoveryDecision(plan.Group.GroupId, plan.Revision, "review-completed-work"),
                DecisionOptions),
            Hint = completedWorkReview?.ComparisonHint ??
                   (hasCommitEvidence
                       ? PlanRecoveryPresentationBuilder.CommittedReviewHint(
                           stepLabel,
                           taskCommitEvidence!.BaselineCommit,
                           taskCommitEvidence.Commit)
                       : PlanRecoveryPresentationBuilder.UnattributedReviewHint(
                           stepLabel,
                           durablePlan?.InterruptionData?.LastCommit)),
        };
        var primary = durablePlan is not null
            ? PlanRecoveryActionPresentationBuilder.Build(
                durablePlan,
                taskId,
                currentHead,
                isAncestor)
            : hasCommitEvidence
                ? new PlanRecoveryPrimaryActionPresentation(
                    isAmendment
                        ? PlanRecoveryPrimaryActionKind.ApproveAmendment
                        : PlanRecoveryPrimaryActionKind.AcceptStep,
                    isAmendment ? "Approve Amendment & Continue" : $"{stepLabel} Is Complete",
                    isAmendment
                        ? "Accept the host-recorded amendment commit shown in this message, approve its checkpoint, and continue. No additional AI assessment runs."
                        : $"Accept the host-recorded commit shown in this message, mark only {stepLabel} complete, and continue. No additional AI assessment runs.",
                    "accept-step-complete")
                : new PlanRecoveryPrimaryActionPresentation(
                    PlanRecoveryPrimaryActionKind.AcceptUnattributedStep,
                    $"{stepLabel} Is Complete & Continue",
                    $"After reviewing repository changes, explicitly mark {stepLabel} complete and continue. Commit attribution remains marked as uncertain.",
                    "accept-step-complete");

        var actions = new List<InboxAction>();
        if (hasCommitEvidence || completedWorkReview is not null ||
            primary.Kind == PlanRecoveryPrimaryActionKind.AcceptUnattributedStep)
            actions.Add(BuildEvidenceLink());
        if (attentionCard.Category == PlanAttentionCategory.UnexpectedState)
        {
            actions.Add(BuildAction(
                $"Continue Working on {stepLabel}",
                "continue-working",
                "Continue from the preserved repository state without discarding completed work."));
        }
        else
        {
            if (attentionCard.Category == PlanAttentionCategory.HumanVerificationRequired ||
                primary.Kind is PlanRecoveryPrimaryActionKind.AcceptStep or
                    PlanRecoveryPrimaryActionKind.AcceptRejectedNoOp or
                    PlanRecoveryPrimaryActionKind.ApproveAmendment)
            {
                actions.Add(BuildAction(primary.Label, primary.ProtocolAction, primary.Hint));
            }
            actions.Add(BuildAction(
                attentionCard.Category == PlanAttentionCategory.AutomaticWorkExhausted
                    ? "Try Again"
                    : $"Continue Working on {stepLabel}",
                "continue-working",
                $"Start one new bounded attempt for {stepLabel} from the preserved repository state."));
        }
        actions.Add(BuildAction(
            "✎ Revise Remaining Plan…",
            "replan-failed-task",
            "Replace the blocked task with smaller approved steps."));

        var reviewAnalysis = reviewCard is { AnalysisBullets.Count: > 0 }
            ? "\n\n**Automated review**\n" +
              string.Join("\n", reviewCard.AnalysisBullets.Select(bullet => "- " + bullet.Text))
            : string.Empty;
        var body = $"**{attentionCard.Title}**\n\n" +
                   $"Plan: **{plan.Group.GroupTitle}**  \n" +
                   $"{stepLabel}: **{taskTitle}**\n\n" +
                   attentionCard.Explanation + "\n\n" +
                   (attentionCard.Category is PlanAttentionCategory.HumanVerificationRequired or
                       PlanAttentionCategory.HumanInputRequired
                       ? "Human check: "
                       : "What happened: ") +
                   attentionCard.RecordedReason + reviewAnalysis + "\n";

        if (hasCommitEvidence)
        {
            var shortCommit = taskCommitEvidence!.Commit.Length > 7
                ? taskCommitEvidence.Commit[..7]
                : taskCommitEvidence.Commit;
            body += $"Commits: `{shortCommit}`\n";
            var tests = completedWorkReview?.TestSummary ??
                        CompletedWorkReviewPresentationBuilder.BuildVerificationSummary(
                            taskCommitEvidence.Verification);
            if (tests is not null) body += tests + "\n";
            body += "\nUse the commit link above when you need to inspect the preserved changes.\n";
        }
        else if (primary.Kind == PlanRecoveryPrimaryActionKind.AcceptRejectedNoOp)
            body += $"\nSquadDash rejected a reported no-op. You can explicitly accept {stepLabel} after a current-snapshot safety check, retry verification, or revise the remaining plan.\n";
        else
            body += $"\nSquadDash does not have exact task-owned commit attribution. Review the repository changes, then explicitly mark {stepLabel} complete or continue working.\n";

        body += "\nChoose an action below.";

        return new InboxMessage
        {
            Id = BuildRecoveryMessageId(plan, taskId),
            Subject = $"{attentionCard.Title}: {plan.Group.GroupTitle}",
            From = "SquadDash",
            Timestamp = timestamp,
            Read = false,
            Priority = "critical",
            Body = body,
            Attachments =
            [
                new InboxAttachment
                {
                    Type = AttachmentType,
                    Label = "View plan and dependencies",
                    PlanGroupId = plan.Group.GroupId,
                    PlanRevision = plan.Revision,
                    Content = JsonSerializer.Serialize(plan, SnapshotOptions),
                }
            ],
            Actions = actions,
        };
    }

    /// <summary>
    /// Returns the one canonical, branch-aware action set used by the transcript, Inbox,
    /// and plan viewer. When the proposed branch is already active, the redundant proposed-
    /// branch action is omitted.
    /// </summary>
    internal static IReadOnlyList<DecomposePlanActionDefinition> BuildActionDefinitions(
        PendingDecomposePlan plan,
        string? activeBranch)
    {
        var group = plan.Group;
        var actions = new List<DecomposePlanActionDefinition>
        {
            new(
                "Add to Plans",
                "collect",
                null,
                "Save this plan to the Plans panel without starting work. You can launch it later."),
        };
        if (!string.Equals(activeBranch, group.Branch, StringComparison.Ordinal))
        {
            actions.Add(new DecomposePlanActionDefinition(
                $"Execute in {group.Branch} Branch",
                "execute-new-branch",
                group.Branch,
                $"Let SquadDash prepare {group.Branch}, then start the dependency-aware loop. Workers cannot switch branches."));
        }
        actions.Add(new DecomposePlanActionDefinition(
            "Execute in Active Branch",
            "execute-active-branch",
            null,
            "Make the current branch the host-owned plan branch, then start the dependency-aware loop."));
        return actions;
    }

    internal static bool TryReadSnapshot(InboxAttachment attachment, out PendingDecomposePlan? plan)
    {
        plan = null;
        if (!string.Equals(attachment.Type, AttachmentType, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(attachment.Content))
            return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<PendingDecomposePlan>(attachment.Content, SnapshotOptions);
            if (parsed is null ||
                !string.Equals(parsed.Group.GroupId, attachment.PlanGroupId, StringComparison.Ordinal) ||
                !string.Equals(parsed.Revision, attachment.PlanRevision, StringComparison.Ordinal))
                return false;
            plan = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            SquadDashTrace.Write(TraceCategory.Inbox,
                $"Decompose plan attachment snapshot could not be parsed: {ex.Message}");
            return false;
        }
    }

    private static InboxAction BuildInboxAction(
        PendingDecomposePlan plan,
        DecomposePlanActionDefinition action)
    {
        var decision = new DecomposeDecision(
            plan.Group.GroupId,
            plan.Revision,
            action.Action,
            action.Branch);
        return new InboxAction
        {
            Label = action.Label,
            RouteMode = ActionRouteMode,
            Prompt = "DECOMPOSE_DECISION_JSON:\n" + JsonSerializer.Serialize(decision, DecisionOptions),
            Hint = action.Hint,
        };
    }
}
