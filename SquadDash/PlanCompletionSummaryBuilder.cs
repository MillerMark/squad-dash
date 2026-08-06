using System.Text;

namespace SquadDash;

internal static class PlanCompletionSummaryBuilder
{
    internal static InboxMessage Build(Plan plan)
    {
        var completedAt = plan.Timestamps.CompletedAt ?? DateTimeOffset.UtcNow;
        return new InboxMessage
        {
            Id = $"plan-completion-{plan.PlanId}-{plan.Revision}",
            Subject = $"Plan completed: {plan.Title}",
            From = "SquadDash Plans",
            Timestamp = completedAt,
            Priority = "low",
            Body = BuildBody(plan),
            Attachments =
            [
                new InboxAttachment
                {
                    Type = "decompose-plan",
                    Label = "View completed plan",
                    PlanGroupId = plan.PlanId,
                    PlanRevision = plan.Revision,
                },
            ],
        };
    }

    internal static string BuildBody(Plan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {plan.Title}");
        builder.AppendLine();
        builder.AppendLine("The plan completed successfully. This summary is assembled from durable task handoffs, " +
                           "independent verification reports, validation evidence, rework history, and human approvals.");
        builder.AppendLine();
        builder.AppendLine("## Timeline");
        AddTime(builder, "Created", plan.Timestamps.CreatedAt);
        AddTime(builder, "Started", plan.Timestamps.StartedAt);
        AddTime(builder, "Completed", plan.Timestamps.CompletedAt);

        builder.AppendLine();
        builder.AppendLine("## Tasks");
        foreach (var task in plan.Tasks)
        {
            builder.AppendLine();
            builder.AppendLine($"### {task.Title ?? task.TaskId}");
            builder.AppendLine($"- Status: **{task.Status}**");
            if (!string.IsNullOrWhiteSpace(task.Commit)) builder.AppendLine($"- Commit: `{task.Commit}`");
            if (task.CompletedAt is { } completedAt) AddTime(builder, "Completed", completedAt);
            if (task.Handoff is { } handoff)
            {
                builder.AppendLine($"- Handoff: {handoff.Summary}");
                if (handoff.ChangedFiles.Count > 0)
                {
                    builder.AppendLine("- Files changed:");
                    foreach (var file in handoff.ChangedFiles) builder.AppendLine($"  - `{file}`");
                }
                if (!string.IsNullOrWhiteSpace(handoff.Verification?.Summary))
                    builder.AppendLine($"- Verification: {handoff.Verification.Summary}");
            }
            else if (!string.IsNullOrWhiteSpace(task.CompletionSummary))
                builder.AppendLine($"- Handoff: {task.CompletionSummary}");

            if (task.VerificationHistory is { Count: > 0 })
            {
                builder.AppendLine("- Independent verification:");
                foreach (var report in task.VerificationHistory)
                {
                    builder.AppendLine($"  - **{report.Verdict}** — {report.Summary}");
                    foreach (var discrepancy in report.MissingOrOverstatedWork)
                        builder.AppendLine($"    - Missing or overstated: {discrepancy}");
                }
            }
            var requestedReworks = task.AttemptHistory?.Count(attempt =>
                string.Equals(attempt.Disposition, "changes-requested", StringComparison.OrdinalIgnoreCase)) ?? 0;
            if (requestedReworks > 0)
                builder.AppendLine($"- Human-requested rework attempts: {requestedReworks}");
        }

        if (plan.Validations is { Count: > 0 })
        {
            builder.AppendLine();
            builder.AppendLine("## Contract validations");
            foreach (var validation in plan.Validations)
            {
                builder.AppendLine($"- **{validation.Title}** — {validation.Status}: {validation.Summary ?? validation.Description}");
                if (validation.CompletedAt is { } completedAt) AddTime(builder, "Validated", completedAt);
                if (!string.IsNullOrWhiteSpace(validation.ValidatedCommit))
                    builder.AppendLine($"  - Evaluated commit: `{validation.ValidatedCommit}`");
            }
        }

        if (plan.ApprovalGates.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Human approvals");
            foreach (var gate in plan.ApprovalGates)
            {
                builder.AppendLine($"- **{gate.Status}** — {gate.Message}");
                if (!string.IsNullOrWhiteSpace(gate.ResolvedBy)) builder.AppendLine($"  - Approved by: {gate.ResolvedBy}");
                if (gate.ResolvedAt is { } resolvedAt) AddTime(builder, "Resolved", resolvedAt);
                if (!string.IsNullOrWhiteSpace(gate.ResolutionNote)) builder.AppendLine($"  - Note: {gate.ResolutionNote}");
                foreach (var evidence in gate.ProofEvidence ?? [])
                {
                    builder.AppendLine($"  - Human proof `{evidence.RequirementId}` ({evidence.ProofType}): {evidence.Summary}");
                    foreach (var artifact in evidence.Artifacts ?? [])
                        builder.AppendLine($"    - Artifact: `{artifact}`");
                }
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static void AddTime(StringBuilder builder, string label, DateTimeOffset? timestamp)
    {
        if (timestamp is null) return;
        builder.AppendLine($"- {label}: {InboxRelativeTimePresenter.Encode(timestamp.Value)}");
    }
}
