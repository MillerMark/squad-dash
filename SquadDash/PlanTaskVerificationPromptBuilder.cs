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
        builder.AppendLine("This verification must be performed by the Squad CLI built-in `fact-checker` utility agent.");
        builder.AppendLine("You are the coordinator, not the verifier. Use the `task` tool exactly once in synchronous mode to launch the " +
                           "Squad `fact-checker` utility slot, using the CLI's `general-purpose` agent type. If " +
                           "`.squad/agents/fact-checker/charter.md` exists, use its identity (which may be named `Fact Checker` or `Verity Cross`). " +
                           "If it is absent, launch the agent as `Fact Checker` with this built-in role: independently verify claims, inspect counter-hypotheses, " +
                           "separate evidence from assertion, and recommend accept, rework, or human review. Pass the complete verification assignment and required result schema to the agent. " +
                           "Do not perform the verification yourself and do not substitute the implementation agent, a generic reviewer, or prose-only findings.");
        builder.AppendLine("After the fact-checker returns, relay its PLAN_TASK_VERIFICATION_JSON result exactly as the only top-level result. " +
                           "If it cannot establish a claim without human observation, it must use `human-review-required`; it must never simulate or imply a human observation.");
        builder.AppendLine();
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
        builder.AppendLine("Treat every explicit current-task requirement as binding. Do not waive missing work merely because a later task " +
                           "might address it. A worker-created deferral of work otherwise owned by the current task is legitimate only when the " +
                           "candidate handoff declares it in `deferredWork`, names one or more downstream owner task IDs, and the approved " +
                           "contracts of those tasks clearly own the deferred work. Require rework only when repository and plan evidence " +
                           "definitively establish a bounded omission owned by the current task. If ownership or intent remains uncertain, " +
                           "report `human-review-required` as a warning instead of rejecting the task.");
        builder.AppendLine("Distinguish worker-created deferral from plan-authored downstream integration. If the approved current task describes " +
                           "an interface or service and explicitly names a later approved task as its production consumer, that later wiring is " +
                           "already owned by the plan and does not belong in the current handoff's `deferredWork`. Verify the current contract and " +
                           "interface now; verify the named consumer when its owning task runs. Mark the current handoff overstated only if it claims " +
                           "that future consumer is already wired when repository evidence shows it is not.");
        AppendDownstreamContracts(builder, plan, task);
        builder.AppendLine();
        builder.AppendLine("Return exactly one result. `missingOrOverstatedWork` is mandatory even when empty. " +
                           "Use `accepted` only when every material claim is supported. Use `rework-required` for a clear, bounded correction. " +
                           "Use `human-review-required` when evidence or product intent is ambiguous.");
        AppendVerdictRules(builder);
        AppendSchema(builder, plan, task, candidate);
        return builder.ToString();
    }

    private static void AppendDownstreamContracts(StringBuilder builder, Plan plan, PlanTask task)
    {
        var descendants = GetDescendants(plan, task.TaskId);
        builder.AppendLine();
        builder.AppendLine("## Approved downstream task contracts eligible to own declared deferrals");
        if (descendants.Count == 0)
        {
            builder.AppendLine("None. This task cannot defer any requirement.");
            return;
        }

        foreach (var descendant in descendants)
        {
            builder.AppendLine($"- `{descendant.TaskId}` — {descendant.Title ?? descendant.TaskId}");
            builder.AppendLine($"  {descendant.Description}");
        }
    }

    private static IReadOnlyList<PlanTask> GetDescendants(Plan plan, string taskId)
    {
        var descendants = new HashSet<string>(StringComparer.Ordinal);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var candidate in plan.Tasks)
            {
                if (descendants.Contains(candidate.TaskId)) continue;
                if (!candidate.DependsOn.Contains(taskId, StringComparer.Ordinal) &&
                    !candidate.DependsOn.Any(descendants.Contains))
                    continue;
                descendants.Add(candidate.TaskId);
                changed = true;
            }
        }

        return plan.Tasks.Where(candidate => descendants.Contains(candidate.TaskId)).ToArray();
    }

    internal static string BuildEnvelopeRepair(
        Plan plan,
        PlanTask task,
        DecomposeStepResult candidate,
        string? priorFindings = null,
        string? validationError = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Your verification completed, but SquadDash could not accept the structured result.");
        if (!string.IsNullOrWhiteSpace(validationError))
            builder.AppendLine($"Exact validation error: {validationError.Trim()}");
        builder.AppendLine("The completed verification must be the spawned `fact-checker` utility agent's result. Do not replace it with the coordinator's own assessment.");
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
        AppendVerdictRules(builder);
        AppendSchema(builder, plan, task, candidate);
        return builder.ToString();
    }

    private static void AppendVerdictRules(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine("Verdict consistency rules (these are mandatory):");
        builder.AppendLine("- `accepted`: every claim finding is `supported`, `missingOrOverstatedWork` is `[]`, and `reworkInstructions` is `[]`.");
        builder.AppendLine("- `rework-required`: a definite, task-owned, bounded defect is supported by repository evidence and `reworkInstructions` contains the concrete correction.");
        builder.AppendLine("- `human-review-required`: a discrepancy is suspected, evidence is incomplete, ownership is uncertain, or product intent is ambiguous. Preserve it as a warning for human judgment; do not reject the result or invent rework instructions.");
        builder.AppendLine("Never return `accepted` with a `missing`, `overstated`, or `unclear` finding. Never erase a discrepancy merely to keep `accepted`.");
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
