using System.Text;

namespace SquadDash;

/// <summary>
/// Builds the validation-turn prompt carrying assertions, task outputs, plan objective,
/// and repository evidence to a non-mutating validation assignment. Validation work must
/// never require or produce a production commit.
/// </summary>
internal static class PlanValidationPromptBuilder
{
    internal static string Build(
        Plan plan,
        PlanValidationNode validation,
        string? observedHead)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Validation Assignment");
        sb.AppendLine();
        sb.AppendLine("This validation must be performed by the Squad CLI built-in `fact-checker` utility agent.");
        sb.AppendLine("You are the coordinator, not the validator. Use the `task` tool exactly once in synchronous mode to launch the " +
                      "Squad `fact-checker` utility slot, using the CLI's `general-purpose` agent type. If " +
                      "`.squad/agents/fact-checker/charter.md` exists, use its identity (which may be named `Fact Checker` or `Verity Cross`). " +
                      "If it is absent, launch the agent as `Fact Checker` with this built-in role: independently verify claims, inspect counter-hypotheses, " +
                      "separate evidence from assertion, and recommend pass, fail, or human review. Pass this complete assignment and its required response schema to the agent. " +
                      "Do not perform the validation yourself or substitute another agent.");
        sb.AppendLine("After the fact-checker returns, relay its PLAN_VALIDATION_RESULT_JSON result as the only top-level result.");
        sb.AppendLine();
        sb.AppendLine("You are performing a **non-mutating validation check**. You must NOT create, amend, or push any commits. " +
                       "Your job is to evaluate the assertions below against the current repository state and return a structured result.");
        sb.AppendLine("AI and automated evidence must never be described as direct human observation. If an assertion requires a person " +
                      "to observe the running product and no approved human-proof checkpoint evidence is supplied, mark that assertion failed " +
                      "and explain that it must be routed to a human approval checkpoint.");
        sb.AppendLine();

        // Plan objective
        sb.AppendLine("## Plan Objective");
        sb.AppendLine();
        sb.AppendLine($"**Plan:** {plan.Title}");
        sb.AppendLine($"**Summary:** {plan.Summary}");
        sb.AppendLine($"**Branch:** {plan.Branch}");
        sb.AppendLine();

        // Validation node
        sb.AppendLine("## Validation Node");
        sb.AppendLine();
        sb.AppendLine($"**ID:** {validation.ValidationId}");
        sb.AppendLine($"**Title:** {validation.Title}");
        sb.AppendLine($"**Description:** {validation.Description}");
        sb.AppendLine($"**Mode:** {validation.Mode}");
        sb.AppendLine();

        if (string.Equals(validation.Mode, "audit", StringComparison.Ordinal))
        {
            sb.AppendLine("## Completion Audit Rules");
            sb.AppendLine();
            sb.AppendLine("This is the plan's independent completion audit. Compare every approved task requirement " +
                          "with the actual commit, diff, transcript evidence, and structured proof evidence. " +
                          "Do not treat a helper, documentation, mock, or headless test as a live UI observation. " +
                          "Do not accept a claimed proof type when its referenced artifacts demonstrate a different kind of proof. " +
                          "Fail any assertion whose observable outcome was not genuinely exercised.");
            sb.AppendLine();
        }

        // Assertions
        sb.AppendLine("## Assertions to Evaluate");
        sb.AppendLine();
        foreach (var assertion in validation.Assertions)
            sb.AppendLine($"- {assertion}");
        sb.AppendLine();

        // Completion audits receive the full accepted plan contract; ordinary validations keep
        // the smaller boundary-scoped context.
        if (string.Equals(validation.Mode, "audit", StringComparison.Ordinal))
            sb.Append(BuildCompactPlanContext(plan));
        else
            AppendTaskOutputs(sb, plan, validation);

        // Verification commands
        if (validation.Commands is { Count: > 0 })
        {
            sb.AppendLine("## Verification Commands");
            sb.AppendLine();
            sb.AppendLine("Run these commands to gather evidence:");
            foreach (var command in validation.Commands)
                sb.AppendLine($"- `{command}`");
            sb.AppendLine();
        }

        // Observed HEAD
        if (!string.IsNullOrWhiteSpace(observedHead))
        {
            sb.AppendLine("## Repository State");
            sb.AppendLine();
            sb.AppendLine($"**HEAD:** `{observedHead}`");
            sb.AppendLine();
        }

        // Result format
        sb.AppendLine("## Required Response Format");
        sb.AppendLine();
        sb.AppendLine("After evaluating all assertions, return ONLY the following structured result. " +
                       "Do not create any commits.");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine($"{PlanValidationResultParser.Marker}");
        sb.AppendLine($$"""
            {
              "validationId": "{{validation.ValidationId}}",
              "planId": "{{plan.PlanId}}",
              "passed": true,
              "summary": "concise validation summary",
              "assertionEvidence": [
            """);
        for (int i = 0; i < validation.Assertions.Count; i++)
        {
            var comma = i < validation.Assertions.Count - 1 ? "," : "";
            sb.AppendLine($$"""
                    { "assertion": "{{EscapeJson(validation.Assertions[i])}}", "passed": true, "evidence": "what was observed" }{{comma}}
                """);
        }
        sb.AppendLine($$"""
              ],
              "validatedCommit": "{{observedHead ?? "<HEAD commit SHA>"}}"
            }
            """);
        sb.AppendLine("```");

        return sb.ToString();
    }

    /// <summary>
    /// Builds compact plan-wide context: completed task outputs relevant to this validation.
    /// </summary>
    internal static string BuildCompactPlanContext(Plan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Accepted Task Outputs (Plan Context)");
        sb.AppendLine();
        var completedTasks = plan.Tasks
            .Where(t => t.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded)
            .ToList();
        if (completedTasks.Count == 0)
        {
            sb.AppendLine("No tasks have been completed yet.");
            return sb.ToString();
        }
        foreach (var task in completedTasks)
        {
            sb.Append($"- **{task.TaskId}** ({task.Title ?? task.TaskId})");
            var handoffSummary = task.Handoff?.Summary ?? task.CompletionSummary;
            if (!string.IsNullOrWhiteSpace(handoffSummary))
                sb.Append($": {Truncate(handoffSummary, 200)}");
            sb.AppendLine();
            if (task.Outputs is { Count: > 0 })
            {
                foreach (var output in task.Outputs)
                    sb.AppendLine($"  - Output `{output.OutputId}`: {output.Description}");
            }
            if (task.ProofRequirements is { Count: > 0 })
            {
                foreach (var requirement in task.ProofRequirements)
                {
                    var evidence = task.ProofEvidence?.FirstOrDefault(candidate =>
                        string.Equals(candidate.RequirementId, requirement.RequirementId, StringComparison.Ordinal));
                    sb.AppendLine($"  - Required proof `{requirement.RequirementId}` ({requirement.ProofType}): {requirement.Description}");
                    sb.AppendLine(evidence is null
                        ? "    Returned evidence: MISSING"
                        : $"    Returned evidence ({evidence.ProofType}): {evidence.Summary}");
                    foreach (var artifact in evidence?.Artifacts ?? [])
                        sb.AppendLine($"    Artifact: `{artifact}`");
                }
            }
            if (task.VerificationHistory is { Count: > 0 })
            {
                var latest = task.VerificationHistory[^1];
                sb.AppendLine($"  - Independent verification `{latest.Verdict}`: {Truncate(latest.Summary, 220)}");
            }
        }
        var proofGates = plan.ApprovalGates
            .Where(gate => gate.ProofRequirements is { Count: > 0 })
            .ToArray();
        if (proofGates.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Human Proof Checkpoints");
            sb.AppendLine();
            foreach (var gate in proofGates)
            {
                sb.AppendLine($"- **{gate.GateId}** — status: `{gate.Status}`; reviewer: `{gate.ResolvedBy ?? "not recorded"}`");
                foreach (var requirement in gate.ProofRequirements!)
                {
                    var evidence = gate.ProofEvidence?.FirstOrDefault(candidate =>
                        string.Equals(candidate.RequirementId, requirement.RequirementId, StringComparison.Ordinal));
                    sb.AppendLine($"  - Required proof `{requirement.RequirementId}` ({requirement.ProofType}): {requirement.Description}");
                    sb.AppendLine(evidence is null
                        ? "    Human evidence: MISSING"
                        : $"    Human evidence ({evidence.ProofType}): {evidence.Summary}");
                    foreach (var artifact in evidence?.Artifacts ?? [])
                        sb.AppendLine($"    Artifact: `{artifact}`");
                }
            }
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private static void AppendTaskOutputs(
        StringBuilder sb,
        Plan plan,
        PlanValidationNode validation)
    {
        // Gather completed tasks referenced by afterTaskIds or outputIds
        var relevantTaskIds = new HashSet<string>(validation.AfterTaskIds, StringComparer.Ordinal);
        if (validation.OutputIds is { Count: > 0 })
        {
            var outputTaskIds = plan.Tasks
                .Where(t => t.Outputs is { Count: > 0 } &&
                       t.Outputs.Any(o => validation.OutputIds.Contains(o.OutputId)))
                .Select(t => t.TaskId);
            foreach (var id in outputTaskIds)
                relevantTaskIds.Add(id);
        }

        var relevantTasks = plan.Tasks
            .Where(t => relevantTaskIds.Contains(t.TaskId))
            .ToList();

        if (relevantTasks.Count == 0)
            return;

        sb.AppendLine("## Accepted Task Outputs");
        sb.AppendLine();
        foreach (var task in relevantTasks)
        {
            sb.Append($"- **{task.TaskId}** ({task.Title ?? task.TaskId}) — status: {task.Status}");
            var handoffSummary = task.Handoff?.Summary ?? task.CompletionSummary;
            if (!string.IsNullOrWhiteSpace(handoffSummary))
                sb.Append($"\n  Handoff: {Truncate(handoffSummary, 300)}");
            if (!string.IsNullOrWhiteSpace(task.Commit))
                sb.Append($"\n  Commit: `{task.Commit}`");
            sb.AppendLine();
            if (task.Outputs is { Count: > 0 })
            {
                foreach (var output in task.Outputs)
                    sb.AppendLine($"  - Output `{output.OutputId}`: {output.Description}");
            }
            if (task.VerificationHistory is { Count: > 0 })
            {
                var latest = task.VerificationHistory[^1];
                sb.AppendLine($"  - Verification `{latest.Verdict}`: {Truncate(latest.Summary, 220)}");
            }
        }
        sb.AppendLine();
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";

    private static string EscapeJson(string text) =>
        text.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
