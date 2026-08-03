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
        sb.AppendLine("You are performing a **non-mutating validation check**. You must NOT create, amend, or push any commits. " +
                       "Your job is to evaluate the assertions below against the current repository state and return a structured result.");
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

        // Assertions
        sb.AppendLine("## Assertions to Evaluate");
        sb.AppendLine();
        foreach (var assertion in validation.Assertions)
            sb.AppendLine($"- {assertion}");
        sb.AppendLine();

        // Task outputs (compact plan-wide context)
        AppendTaskOutputs(sb, plan, validation);

        // Verification commands
        if (validation.Commands.Count > 0)
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
            if (!string.IsNullOrWhiteSpace(task.CompletionSummary))
                sb.Append($": {Truncate(task.CompletionSummary, 200)}");
            sb.AppendLine();
            if (task.Outputs is { Count: > 0 })
            {
                foreach (var output in task.Outputs)
                    sb.AppendLine($"  - Output `{output.OutputId}`: {output.Description}");
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
        if (validation.OutputIds.Count > 0)
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
            if (!string.IsNullOrWhiteSpace(task.CompletionSummary))
                sb.Append($"\n  Summary: {Truncate(task.CompletionSummary, 300)}");
            if (!string.IsNullOrWhiteSpace(task.Commit))
                sb.Append($"\n  Commit: `{task.Commit}`");
            sb.AppendLine();
            if (task.Outputs is { Count: > 0 })
            {
                foreach (var output in task.Outputs)
                    sb.AppendLine($"  - Output `{output.OutputId}`: {output.Description}");
            }
        }
        sb.AppendLine();
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";

    private static string EscapeJson(string text) =>
        text.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
