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
        builder.AppendLine("This verification must be performed by the roster `fact-checker` utility agent.");
        builder.AppendLine("You are the coordinator, not the verifier. Call the host-provided `delegate_roster_agent` tool exactly once with " +
                           "`agent_handle` set to `fact-checker`, and pass the complete verification assignment and required result schema in `prompt`. " +
                           "That host-owned route applies the configured Fact Checker identity, charter, history, and model profile (for example, `Verity Cross`). " +
                           "Do not use the generic `task` tool or create a temporary agent named Fact Checker. " +
                           "Do not perform the verification yourself or substitute the implementation agent, a generic reviewer, or prose-only findings.");
        builder.AppendLine("Copy the complete host-provided result schema, including identity fields and array element types, verbatim into the fact-checker assignment. " +
                           "After the fact-checker returns, relay its PLAN_TASK_VERIFICATION_JSON result exactly as the only top-level result. " +
                           "If it cannot establish a claim without human observation, it must use `human-review-required`; it must never simulate or imply a human observation.");
        builder.AppendLine(PlanDelegationRequestContext.Build(
            plan.PlanId,
            plan.Revision,
            task.TaskId,
            "verification",
            candidate.ExecutionAttemptId,
            plan.Branch));
        builder.AppendLine();
        builder.AppendLine("Independently verify the candidate task result below. Do not modify the repository.");
        var taskIndex = plan.Tasks.ToList().FindIndex(candidate =>
            string.Equals(candidate.TaskId, task.TaskId, StringComparison.Ordinal));
        var stepLabel = PlanRecoveryPresentationBuilder.FormatStepLabel(
            task.DisplayStepLabel ?? (taskIndex >= 0 ? (taskIndex + 1).ToString() : null),
            task.Title ?? task.TaskId);
        builder.AppendLine($"User-facing identity: `{stepLabel}` — {task.Title ?? task.TaskId}. In narrative commentary and the verification summary, call this `{stepLabel}` or use its title. Use the exact task ID `{task.TaskId}` only in structured fields and technical diagnostics; never abbreviate it as `task006` or present its numeric suffix as the Step number.");
        AppendVerificationLevelInstructions(builder, task);
        builder.AppendLine("Your job is to find missing or overstated work, unsupported claims, disconnected production wiring, " +
                           "and tests that pass without proving the behavior requested by the task.");
        builder.AppendLine();
        builder.AppendLine(PlanExecutionContextBuilder.Build(plan, task));
        builder.AppendLine();
        builder.AppendLine("## Candidate handoff");
        builder.AppendLine(JsonSerializer.Serialize(candidate));
        builder.AppendLine();
        builder.AppendLine($"Host-recorded baseline: `{baselineCommit}`");
        if (candidate.IsVerifiedNoOp)
        {
            builder.AppendLine($"Evaluated current snapshot: `{candidate.EvaluatedCommit}`");
            builder.AppendLine("Candidate completion kind: `verified-no-op` (no task commit is claimed or expected).");
        }
        else
        {
            builder.AppendLine($"Candidate commit: `{candidate.Commit}`");
        }
        builder.AppendLine("Host-recorded changed files:");
        foreach (var file in changedFiles) builder.AppendLine($"- `{file}`");
        builder.AppendLine("Host-recorded diff summary:");
        builder.AppendLine(diffSummary);
        builder.AppendLine();
        if (candidate.IsVerifiedNoOp)
        {
            builder.AppendLine("The empty task diff is expected and is not proof of completion. Inspect the repository state at the evaluated " +
                               "snapshot, every location named in `noOpEvidence`, relevant production call sites, and tests. Independently prove " +
                               "that every current-task requirement was already satisfied at the evaluated snapshot. Treat the worker's summary and " +
                               "no-op evidence as claims, not proof. Reject the no-op claim when any task-owned implementation or verification " +
                               "is still missing; provide the bounded work needed so the normal one-rework policy can correct it.");
        }
        else
        {
            builder.AppendLine("Inspect the actual commit, diff, production call sites, and tests. A helper or test is not production integration " +
                               "unless the running path consumes it. Treat the worker summary as a claim, not evidence. Compare the task contract " +
                               "with the host-recorded baseline sufficiently to identify when the same behavior and proof already existed before " +
                               "the candidate commit. This is an efficiency review, not a new acceptance gate: record a clearly redundant but " +
                               "safe, compatible addition in the verification summary or test assessment, and accept it when the resulting code " +
                               "still satisfies every task requirement and every material candidate claim is supported. Do not request rework, " +
                               "return human-review-required, or consume the task's one automatic rework solely because verified-no-op would have " +
                               "been a cleaner result. Require intervention only for a concrete defect, unsupported material claim, or missing " +
                               "task-owned requirement under the existing verdict rules.");
        }
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

    private static void AppendVerificationLevelInstructions(StringBuilder builder, PlanTask task)
    {
        var level = PlanTaskVerificationLevel.Normalize(task.VerificationLevel);
        builder.AppendLine($"Host-selected verification level: `{level}`. This level is authoritative; the coordinator must not reinterpret it.");
        if (level == PlanTaskVerificationLevel.Enhanced)
        {
            builder.AppendLine(PlanTaskVerificationInstructions.EnhancedIndependentReview);
        }
        else
        {
            builder.AppendLine(
                "Perform the standard independent review: validate the task-owned claims against the actual diff, production wiring, and tests.");
        }
        builder.AppendLine();
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
        builder.AppendLine("`missingOrOverstatedWork` and `reworkInstructions` must be JSON arrays of strings. Put all priority, evidence, location, and correction details inside each string; do not return objects in either array.");
        builder.AppendLine(PlanTaskVerificationResultParser.Marker);
        builder.AppendLine("{");
        builder.AppendLine($"  \"planId\": \"{plan.PlanId}\",");
        builder.AppendLine($"  \"taskId\": \"{task.TaskId}\",");
        builder.AppendLine($"  \"revision\": \"{plan.Revision}\",");
        builder.AppendLine($"  \"evaluatedCommit\": \"{candidate.VerificationTargetCommit}\",");
        builder.AppendLine("  \"verdict\": \"accepted|rework-required|human-review-required\",");
        builder.AppendLine("  \"summary\": \"...\",");
        builder.AppendLine("  \"claimFindings\": [{\"claim\":\"...\",\"disposition\":\"supported|missing|overstated|unclear\",\"evidence\":\"...\"}],");
        builder.AppendLine("  \"missingOrOverstatedWork\": [\"<discrepancy string; use [] when none>\"],");
        builder.AppendLine("  \"testAssessment\": \"...\",");
        builder.AppendLine("  \"reworkInstructions\": [\"<correction string; use [] unless verdict is rework-required>\"]");
        builder.AppendLine("}");
    }
}
