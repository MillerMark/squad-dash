using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SquadDash;

internal sealed record TasksJsonParseDiagnostic(
    string Code,
    string Message,
    IReadOnlyList<string>? IndependentProblems = null)
{
    internal IReadOnlyList<string> GetRepairDiagnostics() =>
        IndependentProblems is { Count: > 0 }
            ? IndependentProblems
            : [$"{Code}: {Message}"];
}

/// <summary>
/// Parses a <c>TASKS_JSON:</c> block from AI response text into a <see cref="DecomposedTaskGroup"/>.
/// Uses the same brace-balanced JSON extraction technique as <see cref="InboxMessageParser"/>.
/// </summary>
internal static class TasksJsonParser
{
    private const string Marker = "TASKS_JSON:";
    private const int MaxHumanProofRequirementsPerCheckpoint = 7;

    private static readonly Regex GroupIdPattern =
        new(@"^[A-Z]+-\d{8}$", RegexOptions.Compiled);

    private static readonly Regex TaskIdPattern =
        new(@"^([A-Z]+-\d{8})-(?:\d{3}|AMD-\d{3}|VAL-\d{3}-FIX-\d{3})$", RegexOptions.Compiled);

    private const string TaskIdFormatDescription =
        "{groupId}-NNN, {groupId}-AMD-NNN, or {groupId}-VAL-NNN-FIX-NNN pattern";

    private static readonly Regex ValidationIdPattern =
        new(@"^([A-Z]+-\d{8})-VAL-\d{3}$", RegexOptions.Compiled);

    private static readonly Regex AgentHandlePattern =
        new(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);

    private static readonly Regex OutputIdPattern =
        new(@"^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
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

        bool Fail(
            string code,
            string message,
            IReadOnlyList<string>? independentProblems = null)
        {
            var diagnostic = new TasksJsonParseDiagnostic(code, message, independentProblems);
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

        if (!TryNormalizeRecoverableSchemaVariations(jsonText, out jsonText, out var normalizationError))
            return Fail(normalizationError!.Code, normalizationError.Message);

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

        var independentDiagnostics = CollectIndependentConformanceDiagnostics(parsed);
        if (independentDiagnostics.Count > 0)
        {
            var first = independentDiagnostics[0];
            var detail = independentDiagnostics.Count == 1
                ? first.Message
                : $"{independentDiagnostics.Count} independent schema problems were found:\n- " +
                  string.Join("\n- ", independentDiagnostics.Select(value => $"{value.Code}: {value.Message}"));
            return Fail(
                first.Code,
                detail,
                independentDiagnostics
                    .Select(value => $"{value.Code}: {value.Message}")
                    .ToArray());
        }

        if (string.IsNullOrWhiteSpace(parsed.GroupTitle) ||
            string.IsNullOrWhiteSpace(parsed.Branch) ||
            string.IsNullOrWhiteSpace(parsed.Summary))
        {
            return Fail("missing-plan-metadata", "groupTitle, branch, and summary must be non-empty");
        }

        if (parsed.Delivery is not null && parsed.Delivery is not ("transcript" or "inbox"))
            return Fail("invalid-delivery", "delivery must be transcript or inbox when supplied");

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
                    $"task id '{task.Id}' does not match {TaskIdFormatDescription}");
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

            // "mid" is canonical; retain the older "medium" spelling when reading durable plans.
            if (task.Priority is not ("critical" or "high" or "mid" or "medium" or "low"))
            {
                return Fail("invalid-task-priority",
                    $"task '{task.Id}' priority must be critical, high, mid, or low");
            }

            if (task.AgentAssignments is { Count: > 1 })
            {
                return Fail("too-many-agent-assignments",
                    $"task '{task.Id}' has more than one primary agent assignment; split required work into dependent tasks and use subordinate children only for read-only assistance");
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
            if (routingMode == "assigned" && task.AgentAssignments is not { Count: 1 })
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

            var executionMode = task.ExecutionMode?.Trim();
            if (executionMode is not null &&
                executionMode is not (PlanTaskExecutionMode.Implementation or PlanTaskExecutionMode.Verification))
            {
                return Fail("invalid-task-execution-mode",
                    $"task '{task.Id}' executionMode must be implementation or verification");
            }
            if (executionMode == PlanTaskExecutionMode.Verification &&
                !HasVerificationProofContract(parsed, task))
            {
                return Fail("verification-task-without-proof",
                    $"verification task '{task.Id}' must declare at least one proof requirement " +
                    "or have its human-only proof routed to an approval gate");
            }

            if (!PlanTaskVerificationLevel.IsValid(task.VerificationLevel))
            {
                return Fail("invalid-task-verification-level",
                    $"task '{task.Id}' verificationLevel must be lightweight, standard, or enhanced");
            }

            var proofIds = new HashSet<string>(StringComparer.Ordinal);
            var humanProofCount = (task.ProofRequirements ?? [])
                .Count(requirement => PlanProofCapabilityPolicy.IsHumanOnly(requirement.ProofType));
            if (humanProofCount > MaxHumanProofRequirementsPerCheckpoint)
            {
                return Fail("too-many-human-proof-requirements",
                    $"task '{task.Id}' declares {humanProofCount} human checks; " +
                    $"a checkpoint may contain at most {MaxHumanProofRequirementsPerCheckpoint}");
            }
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
                if (PlanProofCapabilityPolicy.Classify(requirement.ProofType) == PlanProofExecutorKind.Unsupported)
                {
                    return Fail("unsupported-proof-type",
                        $"task '{task.Id}' uses unsupported proofType '{requirement.ProofType}'");
                }
                if (PlanProofCapabilityPolicy.IsHumanOnly(requirement.ProofType) &&
                    !string.IsNullOrWhiteSpace(requirement.Question) &&
                    PlanHumanReviewChecklist.SplitLegacyQuestions(requirement.Question).Count != 1)
                {
                    return Fail("non-atomic-human-proof",
                        $"task '{task.Id}' must give each human proof requirement one atomic true/false question");
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

                if (string.IsNullOrWhiteSpace(gate.Question))
                {
                    return Fail("missing-gate-question",
                        $"gate '{gate.GateId}' must tell the reviewer exactly what to verify or decide");
                }

                if (!gateIds.Add(gate.GateId))
                {
                    return Fail("duplicate-gate-id", $"duplicate gate id '{gate.GateId}'");
                }

                var gateProofIds = new HashSet<string>(StringComparer.Ordinal);
                var humanProofCount = gate.ProofRequirements?.Count ?? 0;
                if (humanProofCount > MaxHumanProofRequirementsPerCheckpoint)
                {
                    return Fail("too-many-human-proof-requirements",
                        $"gate '{gate.GateId}' declares {humanProofCount} human checks; " +
                        $"a checkpoint may contain at most {MaxHumanProofRequirementsPerCheckpoint}");
                }
                foreach (var requirement in gate.ProofRequirements ?? [])
                {
                    var requirementId = requirement.RequirementId ?? string.Empty;
                    if (!OutputIdPattern.IsMatch(requirementId) ||
                        string.IsNullOrWhiteSpace(requirement.Description) ||
                        !gateProofIds.Add(requirementId) ||
                        !PlanProofCapabilityPolicy.IsHumanOnly(requirement.ProofType))
                    {
                        return Fail("invalid-human-proof-checkpoint",
                            $"gate '{gate.GateId}' must contain unique human-only proof requirements");
                    }
                    if (!string.IsNullOrWhiteSpace(requirement.Question) &&
                        PlanHumanReviewChecklist.SplitLegacyQuestions(requirement.Question).Count != 1)
                    {
                        return Fail("non-atomic-human-proof",
                            $"gate '{gate.GateId}' must give each human proof requirement one atomic true/false question");
                    }
                }

                if ((gate.ProofRequirements is null || gate.ProofRequirements.Count == 0) &&
                    PlanHumanReviewChecklist.SplitLegacyQuestions(gate.Question).Count != 1)
                {
                    return Fail("non-atomic-gate-question",
                        $"gate '{gate.GateId}' must contain one atomic true/false question or separate proof requirements");
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
                if (!hasBefore && hasAfter && gate.AfterTaskIds!.All(id => leafIds.Contains(id)) &&
                    gate.ProofRequirements is not { Count: > 0 })
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
                if ((validation.AfterTaskIds ?? []).Concat(validation.BeforeTaskIds ?? []).Any(id => !validIds.Contains(id)) ||
                    (validation.AfterTaskIds ?? []).Intersect(validation.BeforeTaskIds ?? [], StringComparer.Ordinal).Any())
                {
                    return Fail("invalid-validation-boundary",
                        $"validation '{validation.ValidationId}' has an unknown or overlapping task boundary");
                }
                if ((validation.OutputIds ?? []).Any(outputId =>
                        !outputOwners.TryGetValue(outputId, out var ownerId) ||
                        !(validation.AfterTaskIds ?? []).Any(afterId =>
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
                if ((validation.BeforeTaskIds ?? []).Any(beforeId => (validation.AfterTaskIds ?? []).Any(afterId =>
                        DependsTransitivelyOn(tasksById, afterId, beforeId))))
                {
                    return Fail("validation-dependency-cycle",
                        $"validation '{validation.ValidationId}' creates a dependency cycle");
                }
            }
        }
        // A completion audit is optional: task-level proof and deterministic checks may already
        // establish the plan. When an audit is authored, keep its frontier exact so it cannot
        // silently omit a completion leaf or block downstream work.
        if (parsed.Tasks.Any(task => task.ProofRequirements is { Count: > 0 }) ||
            (parsed.ApprovalGates ?? []).Any(gate => gate.ProofRequirements is { Count: > 0 }))
        {
            // A revision keeps a superseded parent task in the proposal and identifies its
            // one or more replacements with parentTaskId. The parent is therefore not a live
            // completion leaf.  Normalize the replacement graph before calculating leaves
            // so downstream dependencies on the parent are redirected to replacement
            // terminals exactly as they will be when the revision is staged.
            var completionGraph = DecomposePlanRevision.TryNormalize(parsed, out var normalizedCompletionGraph, out _)
                ? normalizedCompletionGraph
                : parsed;
            var supersededParentIds = parsed.Tasks
                .Where(task => !string.IsNullOrWhiteSpace(task.ParentTaskId))
                .Select(task => task.ParentTaskId!)
                .ToHashSet(StringComparer.Ordinal);
            var effectiveTaskIds = validIds
                .Where(id => !supersededParentIds.Contains(id))
                .ToHashSet(StringComparer.Ordinal);
            var dependedUpon = completionGraph.Tasks
                .SelectMany(task => task.DependsOn ?? [])
                .Where(effectiveTaskIds.Contains)
                .ToHashSet(StringComparer.Ordinal);
            var leafIds = effectiveTaskIds
                .Where(id => !dependedUpon.Contains(id))
                .ToHashSet(StringComparer.Ordinal);
            var completionAudits = (parsed.Validations ?? [])
                .Where(validation => string.Equals(validation.Mode, "audit", StringComparison.Ordinal))
                .ToArray();
            if (completionAudits.Length > 1 ||
                (completionAudits.Length == 1 &&
                 ((completionAudits[0].BeforeTaskIds?.Count ?? 0) != 0 ||
                  !leafIds.SetEquals(completionAudits[0].AfterTaskIds ?? []))))
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
                    "a proof-bearing plan may omit a final audit, but when supplied it must be the only audit, " +
                    "use beforeTaskIds=[], and set afterTaskIds equal to every leaf task. " +
                    $"Expected leaf task IDs: [{string.Join(", ", leafIds.Order())}]. " +
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

        var routed = PlanProofCapabilityPolicy.RouteHumanProofsToApprovalGates(parsed);
        var humanOnlyShells = PlanProofCapabilityPolicy.FindHumanOnlyVerificationShellTaskIds(
            parsed,
            routed);
        if (humanOnlyShells.Count > 0)
        {
            // Structural detection is intentionally advisory. A verification task can contain useful
            // command or inspection work in its execution contract even when the author omitted a
            // machine-proof/output declaration. The focused semantic preflight examines that prose
            // before staging and requests repair only when the task is genuinely human-only. Keeping
            // parsing permissive also prevents this new-plan quality check from blocking legacy plan
            // reparse, collection, and revision paths.
            SquadDashTrace.Write(
                "PlanPreflight",
                "Plan contains verification tasks that have no declared AI proof or output after " +
                "human proof routing. Accepted for semantic preflight rather than rejected. Candidate " +
                $"task IDs: [{string.Join(", ", humanOnlyShells)}]");
        }

        group = routed;
        return true;
    }

    /// <summary>
    /// Applies only lossless, unambiguous compatibility repairs. The live specification continues
    /// to require canonical property names; these aliases prevent a costly second model turn when
    /// intent is already explicit. Conflicting canonical and compatibility shapes remain failures.
    /// </summary>
    private static bool TryNormalizeRecoverableSchemaVariations(
        string jsonText,
        out string normalizedJson,
        out TasksJsonParseDiagnostic? diagnostic)
    {
        normalizedJson = jsonText;
        diagnostic = null;

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(
                jsonText,
                new JsonNodeOptions { PropertyNameCaseInsensitive = true },
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                }) as JsonObject;
        }
        catch (JsonException)
        {
            // Preserve the original parser's detailed JSON exception.
            return true;
        }

        if (root is null)
            return true;

        var changed = false;
        var conflicts = new List<TasksJsonParseDiagnostic>();
        if (root["tasks"] is JsonArray tasks)
        {
            foreach (var node in tasks)
            {
                if (node is not JsonObject task)
                    continue;

                var taskId = ReadString(task, "id") ?? "(unknown task)";
                var assignedTo = ReadString(task, "assignedTo");
                var hasCanonicalAssignments = task["agentAssignments"] is JsonArray;
                if (!string.IsNullOrWhiteSpace(assignedTo) && hasCanonicalAssignments)
                {
                    conflicts.Add(new TasksJsonParseDiagnostic(
                        "conflicting-agent-assignment-shapes",
                        $"task '{taskId}' contains both assignedTo and agentAssignments; remove assignedTo and keep one canonical assignment"));
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(assignedTo))
                {
                    task["agentAssignments"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["agentHandle"] = assignedTo,
                            ["role"] = ReadString(task, "title") ?? "Primary task owner",
                            ["allowGenericChildren"] = true,
                        },
                    };
                    task["agentRoutingMode"] ??= "assigned";
                    task.Remove("assignedTo");
                    changed = true;
                    SquadDashTrace.Write(TraceCategory.General,
                        $"TasksJsonParser: normalized assignedTo compatibility alias for '{taskId}'");
                }
            }
        }

        if (root["approvalGates"] is JsonArray gates)
        {
            foreach (var node in gates)
            {
                if (node is not JsonObject gate)
                    continue;

                var gateId = ReadString(gate, "gateId") ?? "(unknown gate)";
                if (string.IsNullOrWhiteSpace(ReadString(gate, "message")))
                {
                    var fallback = ReadString(gate, "title") ?? ReadString(gate, "question");
                    if (!string.IsNullOrWhiteSpace(fallback))
                    {
                        gate["message"] = fallback;
                        changed = true;
                        SquadDashTrace.Write(TraceCategory.General,
                            $"TasksJsonParser: normalized missing message for '{gateId}' from its title or question");
                    }
                }
            }
        }

        if (conflicts.Count > 0)
        {
            diagnostic = new TasksJsonParseDiagnostic(
                conflicts[0].Code,
                conflicts.Count == 1
                    ? conflicts[0].Message
                    : $"{conflicts.Count} independent schema problems were found:\n- " +
                      string.Join("\n- ", conflicts.Select(value => $"{value.Code}: {value.Message}")),
                conflicts.Select(value => $"{value.Code}: {value.Message}").ToArray());
            return false;
        }

        if (changed)
            normalizedJson = root.ToJsonString();
        return true;

        static string? ReadString(JsonObject value, string propertyName) =>
            value[propertyName] is JsonValue property && property.TryGetValue<string>(out var text)
                ? text
                : null;
    }

    /// <summary>
    /// Collects independent, locally decidable schema defects in one pass. Relational checks such as
    /// dependency cycles and proof-boundary semantics remain in the ordered validator below because
    /// later diagnostics may depend on earlier identities being valid. Returning all shape defects at
    /// once lets the single automatic repair turn correct the whole object instead of playing
    /// one-error-at-a-time whack-a-mole.
    /// </summary>
    private static IReadOnlyList<TasksJsonParseDiagnostic> CollectIndependentConformanceDiagnostics(
        DecomposedTaskGroup parsed)
    {
        var diagnostics = new List<TasksJsonParseDiagnostic>();

        void Add(string code, string message) => diagnostics.Add(new TasksJsonParseDiagnostic(code, message));

        if (string.IsNullOrWhiteSpace(parsed.GroupTitle) ||
            string.IsNullOrWhiteSpace(parsed.Branch) ||
            string.IsNullOrWhiteSpace(parsed.Summary))
        {
            Add("missing-plan-metadata", "groupTitle, branch, and summary must be non-empty");
        }

        if (parsed.Delivery is not null && parsed.Delivery is not ("transcript" or "inbox"))
            Add("invalid-delivery", "delivery must be transcript or inbox when supplied");

        if (!GroupIdPattern.IsMatch(parsed.GroupId ?? string.Empty))
            Add("invalid-group-id", $"invalid groupId '{parsed.GroupId}' — must match [A-Z]+-\\d{{8}}");

        if (parsed.Tasks is null || parsed.Tasks.Count == 0)
        {
            Add("missing-tasks", "tasks array is null or empty");
            return diagnostics;
        }

        if (parsed.Tasks.Count > 25)
            Add("too-many-tasks", $"{parsed.Tasks.Count} tasks exceeds maximum of 25");

        var taskIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in parsed.Tasks)
        {
            var taskId = string.IsNullOrWhiteSpace(task.Id) ? "(unknown task)" : task.Id;
            if (string.IsNullOrWhiteSpace(task.Id))
                Add("missing-task-id", "a task has a null or empty id");
            else
            {
                var match = TaskIdPattern.Match(task.Id);
                if (!match.Success || match.Groups[1].Value != parsed.GroupId)
                    Add("invalid-task-id", $"task id '{task.Id}' does not match {TaskIdFormatDescription}");
                if (!taskIds.Add(task.Id))
                    Add("duplicate-task-id", $"duplicate task id '{task.Id}'");
            }

            if (string.IsNullOrWhiteSpace(task.Title))
                Add("missing-task-title", $"task '{taskId}' has an empty title");
            if (string.IsNullOrWhiteSpace(task.Description))
                Add("missing-task-description", $"task '{taskId}' has an empty description");
            var contractExampleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var example in task.ContractExamples ?? [])
            {
                var exampleId = example?.ExampleId ?? string.Empty;
                if (example is null)
                {
                    Add("null-contract-example", $"task '{taskId}' has a null contract example");
                    continue;
                }
                if (!OutputIdPattern.IsMatch(exampleId))
                    Add("invalid-contract-example-id",
                        $"task '{taskId}' contract example '{exampleId}' requires a lowercase kebab-case exampleId");
                else if (!contractExampleIds.Add(exampleId))
                    Add("duplicate-contract-example-id",
                        $"task '{taskId}' repeats contract example id '{exampleId}'");
                if (string.IsNullOrWhiteSpace(example.Title))
                    Add("missing-contract-example-title",
                        $"task '{taskId}' contract example '{exampleId}' has an empty title");
                if (string.IsNullOrWhiteSpace(example.Input))
                    Add("missing-contract-example-input",
                        $"task '{taskId}' contract example '{exampleId}' has an empty input");
                if (string.IsNullOrWhiteSpace(example.ExpectedBehavior))
                    Add("missing-contract-example-expected-behavior",
                        $"task '{taskId}' contract example '{exampleId}' has an empty expectedBehavior");
            }
            if (task.Priority is not ("critical" or "high" or "mid" or "medium" or "low"))
                Add("invalid-task-priority", $"task '{taskId}' priority must be critical, high, mid, or low");
            if (task.AgentAssignments is { Count: > 1 })
                Add("too-many-agent-assignments", $"task '{taskId}' has more than one primary agent assignment; split required work into dependent tasks and use subordinate children only for read-only assistance");

            var routingMode = task.AgentRoutingMode?.Trim();
            if (routingMode is not null && routingMode is not ("assigned" or "generic"))
                Add("invalid-agent-routing-mode", $"task '{taskId}' has invalid agentRoutingMode '{routingMode}'");
            else if (routingMode == "assigned" && task.AgentAssignments is not { Count: 1 })
                Add("missing-assigned-agent", $"task '{taskId}' selects assigned routing without an assignment");
            else if (routingMode == "generic" &&
                     (task.AgentAssignments is { Count: > 0 } || string.IsNullOrWhiteSpace(task.GenericAgentReason)))
                Add("invalid-generic-routing", $"task '{taskId}' must explain its explicit generic routing and omit assignments");

            var executionMode = task.ExecutionMode?.Trim();
            if (executionMode is not null &&
                executionMode is not (PlanTaskExecutionMode.Implementation or PlanTaskExecutionMode.Verification))
                Add("invalid-task-execution-mode", $"task '{taskId}' executionMode must be implementation or verification");
            else if (executionMode == PlanTaskExecutionMode.Verification &&
                     !HasVerificationProofContract(parsed, task))
                Add("verification-task-without-proof",
                    $"verification task '{taskId}' must declare at least one proof requirement " +
                    "or have its human-only proof routed to an approval gate");
            if (!PlanTaskVerificationLevel.IsValid(task.VerificationLevel))
                Add("invalid-task-verification-level",
                    $"task '{taskId}' verificationLevel must be lightweight, standard, or enhanced");

            var assignedHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var assignment in task.AgentAssignments ?? [])
            {
                var handle = assignment.AgentHandle ?? string.Empty;
                if (!AgentHandlePattern.IsMatch(handle) ||
                    string.IsNullOrWhiteSpace(assignment.Role) ||
                    !assignedHandles.Add(handle))
                    Add("invalid-agent-assignment",
                        $"task '{taskId}' has an invalid or duplicate agent assignment '{handle}'");
            }

            var proofIds = new HashSet<string>(StringComparer.Ordinal);
            var humanProofCount = (task.ProofRequirements ?? [])
                .Count(requirement => PlanProofCapabilityPolicy.IsHumanOnly(requirement.ProofType));
            if (humanProofCount > MaxHumanProofRequirementsPerCheckpoint)
                Add("too-many-human-proof-requirements",
                    $"task '{taskId}' declares {humanProofCount} human checks; " +
                    $"a checkpoint may contain at most {MaxHumanProofRequirementsPerCheckpoint}");
            foreach (var requirement in task.ProofRequirements ?? [])
            {
                var requirementId = requirement.RequirementId ?? string.Empty;
                if (!OutputIdPattern.IsMatch(requirementId) ||
                    !OutputIdPattern.IsMatch(requirement.ProofType ?? string.Empty) ||
                    string.IsNullOrWhiteSpace(requirement.Description) ||
                    !proofIds.Add(requirementId))
                    Add("invalid-proof-requirement",
                        $"task '{taskId}' has invalid or duplicate proof requirement '{requirementId}'");
                else if (PlanProofCapabilityPolicy.Classify(requirement.ProofType) ==
                         PlanProofExecutorKind.Unsupported)
                    Add("unsupported-proof-type",
                        $"task '{taskId}' uses unsupported proofType '{requirement.ProofType}'");
                if (PlanProofCapabilityPolicy.IsHumanOnly(requirement.ProofType) &&
                    !string.IsNullOrWhiteSpace(requirement.Question) &&
                    PlanHumanReviewChecklist.SplitLegacyQuestions(requirement.Question).Count != 1)
                    Add("non-atomic-human-proof",
                        $"task '{taskId}' must give proof requirement '{requirementId}' one atomic true/false question");
            }
        }

        var outputOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var task in parsed.Tasks)
        {
            foreach (var output in task.Outputs ?? [])
            {
                var outputId = output.OutputId ?? string.Empty;
                if (!OutputIdPattern.IsMatch(outputId) ||
                    string.IsNullOrWhiteSpace(output.Description) ||
                    !outputOwners.TryAdd(outputId, task.Id))
                    Add("invalid-task-output",
                        $"task '{task.Id}' has invalid or duplicate output id '{output.OutputId}'");
            }
        }

        var tasksById = parsed.Tasks
            .Where(task => !string.IsNullOrWhiteSpace(task.Id))
            .GroupBy(task => task.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var task in parsed.Tasks)
        {
            foreach (var dependencyId in task.DependsOn ?? [])
                if (!taskIds.Contains(dependencyId))
                    Add("unknown-task-dependency",
                        $"task '{task.Id}' depends on unknown id '{dependencyId}'");

            if (!string.IsNullOrWhiteSpace(task.ParentTaskId) &&
                (!taskIds.Contains(task.ParentTaskId) ||
                 string.Equals(task.ParentTaskId, task.Id, StringComparison.Ordinal)))
                Add("invalid-parent-task",
                    $"task '{task.Id}' has invalid parentTaskId '{task.ParentTaskId}'");

            foreach (var input in task.Inputs ?? [])
            {
                if (!outputOwners.TryGetValue(input, out var ownerId) ||
                    string.Equals(ownerId, task.Id, StringComparison.Ordinal) ||
                    !DependsTransitivelyOn(tasksById, task.Id, ownerId))
                    Add("invalid-task-input",
                        $"task '{task.Id}' references input '{input}' without depending on its producer");
            }
        }

        var dependedUponTaskIds = parsed.Tasks
            .SelectMany(task => task.DependsOn ?? [])
            .Where(taskIds.Contains)
            .ToHashSet(StringComparer.Ordinal);
        var rootTaskIds = parsed.Tasks
            .Where(task => task.DependsOn is null || task.DependsOn.Count == 0)
            .Select(task => task.Id)
            .ToHashSet(StringComparer.Ordinal);
        var leafTaskIds = taskIds.Where(id => !dependedUponTaskIds.Contains(id))
            .ToHashSet(StringComparer.Ordinal);
        var gateIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var gate in parsed.ApprovalGates ?? [])
        {
            var gateId = string.IsNullOrWhiteSpace(gate.GateId) ? "(unknown gate)" : gate.GateId;
            if (string.IsNullOrWhiteSpace(gate.GateId))
                Add("missing-gate-id", "a gate has a null or empty gateId");
            else if (!gateIds.Add(gate.GateId))
                Add("duplicate-gate-id", $"duplicate gate id '{gate.GateId}'");
            if (string.IsNullOrWhiteSpace(gate.Message))
                Add("missing-gate-message", $"gate '{gateId}' has an empty message");
            if (string.IsNullOrWhiteSpace(gate.Question))
                Add("missing-gate-question", $"gate '{gateId}' must tell the reviewer exactly what to verify or decide");

            var gateProofIds = new HashSet<string>(StringComparer.Ordinal);
            var humanProofCount = gate.ProofRequirements?.Count ?? 0;
            if (humanProofCount > MaxHumanProofRequirementsPerCheckpoint)
                Add("too-many-human-proof-requirements",
                    $"gate '{gateId}' declares {humanProofCount} human checks; " +
                    $"a checkpoint may contain at most {MaxHumanProofRequirementsPerCheckpoint}");
            foreach (var requirement in gate.ProofRequirements ?? [])
            {
                var requirementId = requirement.RequirementId ?? string.Empty;
                if (!OutputIdPattern.IsMatch(requirementId) ||
                    string.IsNullOrWhiteSpace(requirement.Description) ||
                    !gateProofIds.Add(requirementId) ||
                    !PlanProofCapabilityPolicy.IsHumanOnly(requirement.ProofType))
                    Add("invalid-human-proof-checkpoint",
                        $"gate '{gateId}' has invalid, duplicate, or non-human proof requirement '{requirementId}'");
                if (!string.IsNullOrWhiteSpace(requirement.Question) &&
                    PlanHumanReviewChecklist.SplitLegacyQuestions(requirement.Question).Count != 1)
                    Add("non-atomic-human-proof",
                        $"gate '{gateId}' must give proof requirement '{requirementId}' one atomic true/false question");
            }
            if (!string.IsNullOrWhiteSpace(gate.Question) &&
                (gate.ProofRequirements is null || gate.ProofRequirements.Count == 0) &&
                PlanHumanReviewChecklist.SplitLegacyQuestions(gate.Question).Count != 1)
                Add("non-atomic-gate-question",
                    $"gate '{gateId}' must contain one atomic true/false question or separate proof requirements");

            foreach (var id in gate.AfterTaskIds ?? [])
                if (!taskIds.Contains(id))
                    Add("unknown-gate-after-task",
                        $"gate '{gateId}' afterTaskIds references unknown task '{id}'");
            foreach (var id in gate.BeforeTaskIds ?? [])
                if (!taskIds.Contains(id))
                    Add("unknown-gate-before-task",
                        $"gate '{gateId}' beforeTaskIds references unknown task '{id}'");

            var afterIds = gate.AfterTaskIds ?? [];
            var beforeIds = gate.BeforeTaskIds ?? [];
            if (afterIds.Count == 0 && beforeIds.Count > 0 && beforeIds.All(rootTaskIds.Contains))
                Add("gate-before-first-task",
                    $"gate '{gateId}' is a before-first-step gate; use plan-level execution approval instead");
            if (beforeIds.Count == 0 && afterIds.Count > 0 && afterIds.All(leafTaskIds.Contains) &&
                gate.ProofRequirements is not { Count: > 0 })
                Add("gate-after-final-task",
                    $"gate '{gateId}' is an after-final-step gate; it would never block any task");
        }

        if (parsed.Validations is { Count: > 16 })
            Add("too-many-validations", "validations exceeds maximum of 16");

        var validationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var validation in parsed.Validations ?? [])
        {
            var validationId = validation.ValidationId ?? string.Empty;
            var match = ValidationIdPattern.Match(validationId);
            if (!match.Success || match.Groups[1].Value != parsed.GroupId ||
                !validationIds.Add(validationId))
                Add("invalid-validation-id", $"invalid or duplicate validation id '{validation.ValidationId}'");
            if (string.IsNullOrWhiteSpace(validation.Title) ||
                string.IsNullOrWhiteSpace(validation.Description) ||
                validation.Assertions is not { Count: > 0 } ||
                (validation.Assertions ?? []).Any(string.IsNullOrWhiteSpace) ||
                (validation.Assertions ?? []).Distinct(StringComparer.Ordinal).Count() !=
                    (validation.Assertions?.Count ?? 0) ||
                validation.AfterTaskIds is not { Count: > 0 })
                Add("incomplete-validation", $"validation '{validation.ValidationId}' is missing its title, description, prerequisites, or assertions");
            if (validation.Mode is not ("command" or "evidence" or "hybrid" or "audit") ||
                (validation.Mode is "command" or "hybrid") && validation.Commands is not { Count: > 0 })
                Add("invalid-validation-mode", $"validation '{validation.ValidationId}' has an invalid mode or no command");

            foreach (var id in validation.AfterTaskIds ?? [])
                if (!taskIds.Contains(id))
                    Add("unknown-validation-after-task",
                        $"validation '{validation.ValidationId}' afterTaskIds references unknown task '{id}'");
            foreach (var id in validation.BeforeTaskIds ?? [])
                if (!taskIds.Contains(id))
                    Add("unknown-validation-before-task",
                        $"validation '{validation.ValidationId}' beforeTaskIds references unknown task '{id}'");
            foreach (var id in (validation.AfterTaskIds ?? [])
                         .Intersect(validation.BeforeTaskIds ?? [], StringComparer.Ordinal))
                Add("overlapping-validation-boundary",
                    $"validation '{validation.ValidationId}' contains task '{id}' in both afterTaskIds and beforeTaskIds");
            foreach (var outputId in validation.OutputIds ?? [])
            {
                if (!outputOwners.TryGetValue(outputId, out var ownerId) ||
                    !(validation.AfterTaskIds ?? []).Any(afterId =>
                        string.Equals(afterId, ownerId, StringComparison.Ordinal) ||
                        DependsTransitivelyOn(tasksById, afterId, ownerId)))
                    Add("unknown-validation-output",
                        $"validation '{validation.ValidationId}' references unavailable output '{outputId}'");
            }
            if ((validation.BeforeTaskIds ?? []).Any(beforeId =>
                    (validation.AfterTaskIds ?? []).Any(afterId =>
                        DependsTransitivelyOn(tasksById, afterId, beforeId))))
                Add("validation-dependency-cycle",
                    $"validation '{validation.ValidationId}' creates a dependency cycle");
        }

        foreach (var finding in HumanVerificationReadinessPolicy.Evaluate(parsed).Where(finding =>
                     string.Equals(finding.Severity, "error", StringComparison.Ordinal)))
            Add("invalid-human-verification-access",
                $"{finding.TargetId}/{finding.RequirementId}: {finding.Message}");

        return diagnostics;
    }

    private static bool HasVerificationProofContract(
        DecomposedTaskGroup group,
        DecomposedSubTask task) =>
        task.ProofRequirements is { Count: > 0 } ||
        (group.ApprovalGates ?? []).Any(gate =>
            gate.ProofRequirements is { Count: > 0 } &&
            (gate.AfterTaskIds ?? []).Contains(task.Id, StringComparer.Ordinal));

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
