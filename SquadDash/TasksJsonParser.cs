using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SquadDash;

internal sealed record TasksJsonParseDiagnostic(string Code, string Message);

/// <summary>
/// Parses a <c>TASKS_JSON:</c> block from AI response text into a <see cref="DecomposedTaskGroup"/>.
/// Uses the same brace-balanced JSON extraction technique as <see cref="InboxMessageParser"/>.
/// </summary>
internal static class TasksJsonParser
{
    private const string Marker = "TASKS_JSON:";

    private static readonly Regex GroupIdPattern =
        new(@"^[A-Z]+-\d{8}$", RegexOptions.Compiled);

    private static readonly Regex TaskIdPattern =
        new(@"^([A-Z]+-\d{8})-\d{3}$", RegexOptions.Compiled);

    private static readonly Regex ValidationIdPattern =
        new(@"^([A-Z]+-\d{8})-VAL-\d{3}$", RegexOptions.Compiled);

    private static readonly Regex AgentHandlePattern =
        new(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);

    private static readonly Regex OutputIdPattern =
        new(@"^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Attempts to extract and validate a TASKS_JSON block from <paramref name="text"/>.
    /// Returns <c>true</c> when a valid, well-formed group is found; <c>false</c> otherwise.
    /// Validation errors are written to trace.
    /// </summary>
    internal static bool TryParse(string text, out DecomposedTaskGroup? group) =>
        TryParseCore(text, out group, reportDiagnostic: null);

    internal static bool TryParse(
        string text,
        out DecomposedTaskGroup? group,
        out TasksJsonParseDiagnostic? diagnostic)
    {
        TasksJsonParseDiagnostic? captured = null;
        var parsed = TryParseCore(text, out group, value => captured ??= value);
        diagnostic = captured;
        return parsed;
    }

    private static bool TryParseCore(
        string text,
        out DecomposedTaskGroup? group,
        Action<TasksJsonParseDiagnostic>? reportDiagnostic)
    {
        group = null;

        bool Fail(string code, string message)
        {
            var diagnostic = new TasksJsonParseDiagnostic(code, message);
            reportDiagnostic?.Invoke(diagnostic);
            SquadDashTrace.Write(TraceCategory.General, $"TasksJsonParser: {message}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
            return Fail("empty-response", "response is empty");

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // Use the last occurrence so that multiple blocks resolve to the final one.
        int markerIdx = normalized.LastIndexOf(Marker, StringComparison.Ordinal);
        if (markerIdx < 0)
            return Fail("missing-marker", "TASKS_JSON marker is missing");

        int braceStart = normalized.IndexOf('{', markerIdx + Marker.Length);
        if (braceStart < 0)
            return Fail("missing-json-object", "TASKS_JSON marker is not followed by a JSON object");

        // Walk brace depth to find the closing brace, ignoring braces inside strings.
        int  depth    = 0;
        int  braceEnd = -1;
        bool inString = false;
        bool escaped  = false;
        for (int i = braceStart; i < normalized.Length; i++)
        {
            char c = normalized[i];
            if (escaped)           { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if      (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) { braceEnd = i; break; }
            }
        }

        if (braceEnd < 0)
            return Fail("unbalanced-json", "TASKS_JSON object has unbalanced braces");

        var jsonText = normalized[braceStart..(braceEnd + 1)];

        DecomposedTaskGroup? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<DecomposedTaskGroup>(jsonText, ParseOptions);
        }
        catch (JsonException ex)
        {
            return Fail("json-parse-error", $"JSON parse error — {ex.Message}");
        }

        if (parsed is null)
            return Fail("null-plan", "TASKS_JSON deserialized to null");

        if (string.IsNullOrWhiteSpace(parsed.GroupTitle) ||
            string.IsNullOrWhiteSpace(parsed.Branch) ||
            string.IsNullOrWhiteSpace(parsed.Summary))
        {
            return Fail("missing-plan-metadata", "groupTitle, branch, and summary must be non-empty");
        }

        // Validate groupId format.
        if (!GroupIdPattern.IsMatch(parsed.GroupId ?? string.Empty))
        {
            return Fail("invalid-group-id",
                $"invalid groupId '{parsed.GroupId}' — must match [A-Z]+-\\d{{8}}");
        }

        // Validate task count.
        if (parsed.Tasks is null || parsed.Tasks.Count == 0)
        {
            return Fail("missing-tasks", "tasks array is null or empty");
        }

        if (parsed.Tasks.Count > 25)
        {
            return Fail("too-many-tasks", $"{parsed.Tasks.Count} tasks exceeds maximum of 25");
        }

        // Build a set of valid task IDs and validate each ID format.
        var validIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in parsed.Tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Id))
            {
                return Fail("missing-task-id", "a task has a null or empty id");
            }

            var m = TaskIdPattern.Match(task.Id);
            if (!m.Success || m.Groups[1].Value != parsed.GroupId)
            {
                return Fail("invalid-task-id",
                    $"task id '{task.Id}' does not match {{groupId}}-NNN pattern");
            }

            if (!validIds.Add(task.Id))
            {
                return Fail("duplicate-task-id", $"duplicate task id '{task.Id}'");
            }

            if (string.IsNullOrWhiteSpace(task.Title))
            {
                return Fail("missing-task-title", $"task '{task.Id}' has an empty title");
            }

            if (string.IsNullOrWhiteSpace(task.Description))
            {
                return Fail("missing-task-description", $"task '{task.Id}' has an empty description");
            }

            if (task.AgentAssignments is { Count: > 4 })
            {
                return Fail("too-many-agent-assignments",
                    $"task '{task.Id}' has more than four primary agent assignments");
            }

            var assignedHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var assignment in task.AgentAssignments ?? [])
            {
                var handle = assignment.AgentHandle ?? string.Empty;
                if (!AgentHandlePattern.IsMatch(handle) ||
                    string.IsNullOrWhiteSpace(assignment.Role) ||
                    !assignedHandles.Add(handle))
                {
                    return Fail("invalid-agent-assignment",
                        $"task '{task.Id}' has an invalid or duplicate agent assignment");
                }
            }

            var routingMode = task.AgentRoutingMode?.Trim();
            if (routingMode is not null && routingMode is not ("assigned" or "generic"))
            {
                return Fail("invalid-agent-routing-mode",
                    $"task '{task.Id}' has invalid agentRoutingMode '{routingMode}'");
            }
            if (routingMode == "assigned" && task.AgentAssignments is not { Count: > 0 })
            {
                return Fail("missing-assigned-agent",
                    $"task '{task.Id}' selects assigned routing without an assignment");
            }
            if (routingMode == "generic" &&
                (task.AgentAssignments is { Count: > 0 } || string.IsNullOrWhiteSpace(task.GenericAgentReason)))
            {
                return Fail("invalid-generic-routing",
                    $"task '{task.Id}' must explain its explicit generic routing and omit assignments");
            }

            var proofIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var requirement in task.ProofRequirements ?? [])
            {
                var requirementId = requirement.RequirementId ?? string.Empty;
                if (!OutputIdPattern.IsMatch(requirementId) ||
                    !OutputIdPattern.IsMatch(requirement.ProofType ?? string.Empty) ||
                    string.IsNullOrWhiteSpace(requirement.Description) ||
                    !proofIds.Add(requirementId))
                {
                    return Fail("invalid-proof-requirement",
                        $"task '{task.Id}' has an invalid or duplicate proof requirement");
                }
            }
        }

        var outputOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        var tasksById = parsed.Tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        foreach (var task in parsed.Tasks)
        {
            foreach (var output in task.Outputs ?? [])
            {
                var outputId = output.OutputId ?? string.Empty;
                if (!OutputIdPattern.IsMatch(outputId) ||
                    string.IsNullOrWhiteSpace(output.Description) ||
                    !outputOwners.TryAdd(outputId, task.Id))
                {
                    return Fail("invalid-task-output",
                        $"task '{task.Id}' has an invalid or duplicate output id '{output.OutputId}'");
                }
            }
        }

        // Validate all dependsOn IDs reference valid siblings.
        foreach (var task in parsed.Tasks)
        {
            if (task.DependsOn is not null)
            {
                foreach (var dep in task.DependsOn)
                {
                    if (!validIds.Contains(dep))
                    {
                        return Fail("unknown-task-dependency",
                            $"task '{task.Id}' depends on unknown id '{dep}'");
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(task.ParentTaskId) &&
                (!validIds.Contains(task.ParentTaskId) ||
                 string.Equals(task.ParentTaskId, task.Id, StringComparison.Ordinal)))
            {
                return Fail("invalid-parent-task",
                    $"task '{task.Id}' has invalid parentTaskId '{task.ParentTaskId}'");
            }

            foreach (var input in task.Inputs ?? [])
            {
                if (!outputOwners.TryGetValue(input, out var ownerId) ||
                    string.Equals(ownerId, task.Id, StringComparison.Ordinal) ||
                    !DependsTransitivelyOn(tasksById, task.Id, ownerId))
                {
                    return Fail("invalid-task-input",
                        $"task '{task.Id}' references input '{input}' without depending on its producer");
                }
            }
        }

        // Validate approval gates if present.
        if (parsed.ApprovalGates is { Count: > 0 })
        {
            // Build leaf-task set: tasks that no other task depends on.
            var dependedUpon = new HashSet<string>(StringComparer.Ordinal);
            foreach (var task in parsed.Tasks)
                foreach (var dep in task.DependsOn ?? [])
                    dependedUpon.Add(dep);
            var leafIds = validIds.Where(id => !dependedUpon.Contains(id)).ToHashSet(StringComparer.Ordinal);

            // Build root-task set: tasks with no DependsOn.
            var rootIds = parsed.Tasks
                .Where(t => t.DependsOn is null || t.DependsOn.Count == 0)
                .Select(t => t.Id)
                .ToHashSet(StringComparer.Ordinal);

            var gateIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var gate in parsed.ApprovalGates)
            {
                if (string.IsNullOrWhiteSpace(gate.GateId))
                {
                    return Fail("missing-gate-id", "a gate has a null or empty gateId");
                }

                if (string.IsNullOrWhiteSpace(gate.Message))
                {
                    return Fail("missing-gate-message", $"gate '{gate.GateId}' has an empty message");
                }

                if (!gateIds.Add(gate.GateId))
                {
                    return Fail("duplicate-gate-id", $"duplicate gate id '{gate.GateId}'");
                }

                foreach (var id in gate.AfterTaskIds ?? [])
                {
                    if (!validIds.Contains(id))
                    {
                        return Fail("unknown-gate-after-task",
                            $"gate '{gate.GateId}' afterTaskIds references unknown task '{id}'");
                    }
                }

                foreach (var id in gate.BeforeTaskIds ?? [])
                {
                    if (!validIds.Contains(id))
                    {
                        return Fail("unknown-gate-before-task",
                            $"gate '{gate.GateId}' beforeTaskIds references unknown task '{id}'");
                    }
                }

                // Reject before-first-step: AfterTaskIds empty/null AND BeforeTaskIds contain only root tasks.
                var hasAfter  = gate.AfterTaskIds  is { Count: > 0 };
                var hasBefore = gate.BeforeTaskIds is { Count: > 0 };
                if (!hasAfter && hasBefore && gate.BeforeTaskIds!.All(id => rootIds.Contains(id)))
                {
                    return Fail("gate-before-first-task",
                        $"gate '{gate.GateId}' is a before-first-step gate; use plan-level execution approval instead");
                }

                // Reject after-final-step: BeforeTaskIds empty/null AND AfterTaskIds contain only leaf tasks.
                if (!hasBefore && hasAfter && gate.AfterTaskIds!.All(id => leafIds.Contains(id)))
                {
                    return Fail("gate-after-final-task",
                        $"gate '{gate.GateId}' is an after-final-step gate; it would never block any task");
                }
            }
        }

        if (parsed.Validations is { Count: > 0 })
        {
            if (parsed.Validations.Count > 16)
            {
                return Fail("too-many-validations", "validations exceeds maximum of 16");
            }

            var validationIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var validation in parsed.Validations)
            {
                var validationId = validation.ValidationId ?? string.Empty;
                var match = ValidationIdPattern.Match(validationId);
                if (!match.Success || match.Groups[1].Value != parsed.GroupId ||
                    !validationIds.Add(validationId))
                {
                    return Fail("invalid-validation-id",
                        $"invalid or duplicate validation id '{validation.ValidationId}'");
                }
                if (string.IsNullOrWhiteSpace(validation.Title) ||
                    string.IsNullOrWhiteSpace(validation.Description) ||
                    validation.Assertions is not { Count: > 0 } ||
                    validation.Assertions.Any(string.IsNullOrWhiteSpace) ||
                    validation.Assertions.Distinct(StringComparer.Ordinal).Count() != validation.Assertions.Count ||
                    validation.AfterTaskIds is not { Count: > 0 })
                {
                    return Fail("incomplete-validation",
                        $"validation '{validation.ValidationId}' is missing its title, description, prerequisites, or assertions");
                }
                if (validation.AfterTaskIds.Concat(validation.BeforeTaskIds).Any(id => !validIds.Contains(id)) ||
                    validation.AfterTaskIds.Intersect(validation.BeforeTaskIds, StringComparer.Ordinal).Any())
                {
                    return Fail("invalid-validation-boundary",
                        $"validation '{validation.ValidationId}' has an unknown or overlapping task boundary");
                }
                if ((validation.OutputIds ?? []).Any(outputId =>
                        !outputOwners.TryGetValue(outputId, out var ownerId) ||
                        !validation.AfterTaskIds.Any(afterId =>
                            string.Equals(afterId, ownerId, StringComparison.Ordinal) ||
                            DependsTransitivelyOn(tasksById, afterId, ownerId))))
                {
                    return Fail("unknown-validation-output",
                        $"validation '{validation.ValidationId}' references an unknown output");
                }
                if (validation.Mode is not ("command" or "evidence" or "hybrid" or "audit") ||
                    (validation.Mode is "command" or "hybrid") && validation.Commands is not { Count: > 0 })
                {
                    return Fail("invalid-validation-mode",
                        $"validation '{validation.ValidationId}' has an invalid mode or no command");
                }
                if (validation.BeforeTaskIds.Any(beforeId => validation.AfterTaskIds.Any(afterId =>
                        DependsTransitivelyOn(tasksById, afterId, beforeId))))
                {
                    return Fail("validation-dependency-cycle",
                        $"validation '{validation.ValidationId}' creates a dependency cycle");
                }
            }
        }


        // Structured proof contracts opt a plan into a mandatory completion audit. This is a
        // declarative contract, not a filename/description heuristic: legacy plans remain valid,
        // while new proof-bearing plans cannot silently substitute tests for a live observation.
        if (parsed.Tasks.Any(task => task.ProofRequirements is { Count: > 0 }))
        {
            var dependedUpon = parsed.Tasks
                .SelectMany(task => task.DependsOn ?? [])
                .ToHashSet(StringComparer.Ordinal);
            var leafIds = validIds.Where(id => !dependedUpon.Contains(id)).ToHashSet(StringComparer.Ordinal);
            var completionAudits = (parsed.Validations ?? [])
                .Where(validation => string.Equals(validation.Mode, "audit", StringComparison.Ordinal))
                .ToArray();
            if (completionAudits.Length != 1 ||
                (completionAudits[0].BeforeTaskIds?.Count ?? 0) != 0 ||
                !leafIds.SetEquals(completionAudits[0].AfterTaskIds ?? []))
            {
                var auditIds = completionAudits.Select(audit => audit.ValidationId).ToArray();
                var actualAfterIds = completionAudits.Length == 1
                    ? completionAudits[0].AfterTaskIds ?? []
                    : [];
                var actualBeforeIds = completionAudits.Length == 1
                    ? completionAudits[0].BeforeTaskIds ?? []
                    : [];
                return Fail(
                    "invalid-proof-completion-audit",
                    "proof-bearing plans require exactly one final audit validation with beforeTaskIds=[] and " +
                    $"afterTaskIds equal to every leaf task. Expected leaf task IDs: [{string.Join(", ", leafIds.Order())}]. " +
                    $"Found audit IDs: [{string.Join(", ", auditIds)}]; actual afterTaskIds: " +
                    $"[{string.Join(", ", actualAfterIds)}]; actual beforeTaskIds: [{string.Join(", ", actualBeforeIds)}]");
            }
        }

        // Validate plan cohesion — observable outcomes, production consumers, tailored final proof.
        var cohesionIssues = PlanCohesionValidator.Validate(parsed);
        if (cohesionIssues.Count > 0)
        {
            foreach (var issue in cohesionIssues)
                SquadDashTrace.Write(TraceCategory.General, $"TasksJsonParser: cohesion — {issue}");
            // Cohesion issues are advisory warnings, not hard failures, to preserve backward compatibility.
            // Plans with cohesion issues are accepted but logged for host review.
        }

        group = parsed;
        return true;
    }

    private static bool DependsTransitivelyOn(
        IReadOnlyDictionary<string, DecomposedSubTask> tasksById,
        string taskId,
        string possibleAncestorId)
    {
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(taskId);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current) || !tasksById.TryGetValue(current, out var task))
                continue;
            foreach (var dependency in task.DependsOn ?? [])
            {
                if (string.Equals(dependency, possibleAncestorId, StringComparison.Ordinal))
                    return true;
                pending.Push(dependency);
            }
        }
        return false;
    }
}
