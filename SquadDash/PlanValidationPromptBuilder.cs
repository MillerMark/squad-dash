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
        sb.AppendLine("SquadDash requires this validation to be performed by the roster `fact-checker` utility agent; " +
                      "this is a host workflow requirement, not a user request to delegate ordinary plan work.");
        sb.AppendLine("You are the coordinator, not the validator. Call the host-provided `delegate_roster_agent` tool exactly once with " +
                      "`agent_handle` set to `fact-checker`, and pass this complete assignment and required response schema in `prompt`. " +
                      "That host-owned route applies the configured Fact Checker identity, charter, history, and model profile (for example, `Verity Cross`). " +
                      "Do not use the generic `task` tool or create a temporary agent named Fact Checker. " +
                      "Do not perform the validation yourself or substitute another agent.");
        sb.AppendLine("After the fact-checker returns, relay its PLAN_VALIDATION_RESULT_JSON result as the only top-level result.");
        sb.AppendLine(PlanDelegationRequestContext.Build(
            plan.PlanId,
            plan.Revision,
            validation.ValidationId,
            "validation",
            attemptId: null,
            branch: plan.Branch));
        sb.AppendLine("The result must contain exactly one `assertionEvidence` item for each approved assertion below, in the same order. " +
                      "Copy each assertion text verbatim. Do not split, merge, summarize, or rewrite assertions.");
        sb.AppendLine();
        sb.AppendLine("You are performing a **non-mutating validation check**. You must NOT create, amend, or push any commits. " +
                       "Your job is to evaluate the assertions below against the current repository state and return a structured result.");
        sb.AppendLine("AI and automated evidence must never be described as direct human observation. If all automated and repository " +
                      "evidence is satisfactory but an assertion still requires a person to observe the running product, return the " +
                      "needs-human-proof outcome so SquadDash can route it through a human approval checkpoint with a small atomic review " +
                      "checklist. This is not a failed validation. Return failed only " +
                      "for an actual implementation, automated-proof, safety, or contract defect.");
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
                          "Never pass an assertion whose observable outcome was not genuinely exercised. When human observation is the " +
                          "only missing evidence, use needs-human-proof rather than failed.");
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
              "outcome": "passed | needs-human-proof | failed",
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
              "humanProofRequirements": [],
              "validatedCommit": "{{observedHead ?? "<HEAD commit SHA>"}}"
            }
            """);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("For needs-human-proof, set passed to false and include one to seven humanProofRequirements. Each item must use " +
                      "proofType live-ui-observation, restart-observation, or human-observation; provide a unique requirementId, a short " +
                      "description of what the observation proves, an atomic yes/no question, and accessMode directly-reachable or recipe. " +
                      "Use this item shape: { \"requirementId\": \"...\", \"proofType\": \"human-observation\", " +
                      "\"description\": \"what this observation proves\", \"question\": \"Did the expected behavior occur?\", " +
                      "\"accessMode\": \"directly-reachable\" }. Recipe access must include verificationRecipe with " +
                      "nonempty setup, open, observe, and cleanup fields. Include only observations that are still genuinely missing. " +
                      "For passed or failed, return an empty humanProofRequirements array.");

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

    internal static string EscapeJson(string text) =>
        text.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
