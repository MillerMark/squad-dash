using System.Text;
using System.Text.Json;

namespace SquadDash;

internal static class PlanTaskVerificationPromptBuilder
{
    internal static string Build(
        Plan plan,
        PlanTask task,
        DecomposeStepResult candidate,
        string baselineCommit,
        IReadOnlyList<string> changedFiles,
        string diffSummary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Independently verify the candidate task result below. Do not modify the repository.");
        builder.AppendLine("Your job is to find missing or overstated work, unsupported claims, disconnected production wiring, " +
                           "and tests that pass without proving the behavior requested by the task.");
        builder.AppendLine();
        builder.AppendLine(PlanExecutionContextBuilder.Build(plan, task));
        builder.AppendLine();
        builder.AppendLine("## Candidate handoff");
        builder.AppendLine(JsonSerializer.Serialize(candidate));
        builder.AppendLine();
        builder.AppendLine($"Host-recorded baseline: `{baselineCommit}`");
        builder.AppendLine($"Candidate commit: `{candidate.Commit}`");
        builder.AppendLine("Host-recorded changed files:");
        foreach (var file in changedFiles) builder.AppendLine($"- `{file}`");
        builder.AppendLine("Host-recorded diff summary:");
        builder.AppendLine(diffSummary);
        builder.AppendLine();
        builder.AppendLine("Inspect the actual commit, diff, production call sites, and tests. A helper or test is not production integration " +
                           "unless the running path consumes it. Treat the worker summary as a claim, not evidence.");
        builder.AppendLine();
        builder.AppendLine("Return exactly one result. `missingOrOverstatedWork` is mandatory even when empty. " +
                           "Use `accepted` only when every material claim is supported. Use `rework-required` for a clear, bounded correction. " +
                           "Use `human-review-required` when evidence or product intent is ambiguous.");
        AppendSchema(builder, plan, task, candidate);
        return builder.ToString();
    }

    internal static string BuildEnvelopeRepair(
        Plan plan,
        PlanTask task,
        DecomposeStepResult candidate,
        string? priorFindings = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Your verification completed, but the response omitted the required structured result.");
        builder.AppendLine("Do not inspect files again, run tools, modify the repository, or launch workers.");
        builder.AppendLine("Convert the findings already reached into the required result. Do not omit, soften, or reinterpret a discrepancy.");
        if (!string.IsNullOrWhiteSpace(priorFindings))
        {
            builder.AppendLine();
            builder.AppendLine("## Findings from the completed verification pass");
            builder.AppendLine(priorFindings.Length <= 6000
                ? priorFindings.Trim()
                : priorFindings[^6000..].Trim());
        }
        builder.AppendLine();
        builder.AppendLine("Return only the corrected PLAN_TASK_VERIFICATION_JSON object below. Do not add prose before or after it.");
        AppendSchema(builder, plan, task, candidate);
        return builder.ToString();
    }

    private static void AppendSchema(
        StringBuilder builder,
        Plan plan,
        PlanTask task,
        DecomposeStepResult candidate)
    {
        builder.AppendLine(PlanTaskVerificationResultParser.Marker);
        builder.AppendLine("{");
        builder.AppendLine($"  \"planId\": \"{plan.PlanId}\",");
        builder.AppendLine($"  \"taskId\": \"{task.TaskId}\",");
        builder.AppendLine($"  \"revision\": \"{plan.Revision}\",");
        builder.AppendLine($"  \"evaluatedCommit\": \"{candidate.Commit}\",");
        builder.AppendLine("  \"verdict\": \"accepted|rework-required|human-review-required\",");
        builder.AppendLine("  \"summary\": \"...\",");
        builder.AppendLine("  \"claimFindings\": [{\"claim\":\"...\",\"disposition\":\"supported|missing|overstated|unclear\",\"evidence\":\"...\"}],");
        builder.AppendLine("  \"missingOrOverstatedWork\": [],");
        builder.AppendLine("  \"testAssessment\": \"...\",");
        builder.AppendLine("  \"reworkInstructions\": []");
        builder.AppendLine("}");
    }
}
