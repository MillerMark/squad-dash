using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SquadDash;

internal sealed record PlanDefinitionEditResult(
    PendingDecomposePlan DisplayedPlan,
    Plan DurablePlan);

internal sealed class PlanEditPauseRequiredException(string message) : InvalidOperationException(message);

internal static class StagedPlanEditor
{
    internal static bool TryValidateUnlockedChanges(
        Plan current,
        DecomposedTaskGroup candidate,
        out string? error)
    {
        error = null;
        var currentGroup = PendingDecomposePlanAdapter.FromPlan(current).Group;
        var currentTasks = currentGroup.Tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        var candidateTasks = candidate.Tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        var lockedTaskIds = current.Tasks
            .Where(task => task.Status != PlanTaskStatus.Pending ||
                           string.Equals(task.TaskId, current.Progress.ExecutingTaskId, StringComparison.Ordinal))
            .Select(task => task.TaskId)
            .ToArray();
        var changedLockedTask = lockedTaskIds.FirstOrDefault(taskId =>
            !currentTasks.TryGetValue(taskId, out var before) ||
            !candidateTasks.TryGetValue(taskId, out var after) ||
            !Same(before, after));
        if (changedLockedTask is not null)
        {
            error = $"Task '{changedLockedTask}' has started or already produced execution history. " +
                    "Its definition is frozen; only future pending tasks can be changed.";
            return false;
        }

        var currentGates = (currentGroup.ApprovalGates ?? [])
            .ToDictionary(gate => gate.GateId, StringComparer.Ordinal);
        var candidateGates = (candidate.ApprovalGates ?? [])
            .ToDictionary(gate => gate.GateId, StringComparer.Ordinal);
        var changedLockedGate = current.ApprovalGates
            .Where(gate => gate.Status != PlanGateStatus.Pending)
            .Select(gate => gate.GateId)
            .FirstOrDefault(gateId =>
                !currentGates.TryGetValue(gateId, out var before) ||
                !candidateGates.TryGetValue(gateId, out var after) ||
                !Same(before, after));
        if (changedLockedGate is not null)
        {
            error = $"Approval gate '{changedLockedGate}' has already been reached and is frozen.";
            return false;
        }

        var currentValidations = (currentGroup.Validations ?? [])
            .ToDictionary(validation => validation.ValidationId, StringComparer.Ordinal);
        var candidateValidations = (candidate.Validations ?? [])
            .ToDictionary(validation => validation.ValidationId, StringComparer.Ordinal);
        var changedLockedValidation = (current.Validations ?? [])
            .Where(validation => validation.Status != PlanValidationStatus.Pending)
            .Select(validation => validation.ValidationId)
            .FirstOrDefault(validationId =>
                !currentValidations.TryGetValue(validationId, out var before) ||
                !candidateValidations.TryGetValue(validationId, out var after) ||
                !Same(before, after));
        if (changedLockedValidation is not null)
        {
            error = $"Validation '{changedLockedValidation}' has already started or completed and is frozen.";
            return false;
        }
        return true;
    }

    internal static DecomposedTaskGroup UpdatePlan(
        DecomposedTaskGroup group,
        string groupTitle,
        string branch,
        string summary,
        string? delivery) => group with
        {
            GroupTitle = groupTitle.Trim(),
            Branch = branch.Trim(),
            Summary = summary.Trim(),
            Delivery = string.IsNullOrWhiteSpace(delivery) ? null : delivery.Trim(),
        };

    internal static bool TryValidate(
        DecomposedTaskGroup candidate,
        out DecomposedTaskGroup? normalized,
        out string? error)
    {
        normalized = null;
        error = null;
        var input = "TASKS_JSON:\n" + JsonSerializer.Serialize(candidate);
        if (!TasksJsonParser.TryParse(input, out var parsed, out var diagnostic) || parsed is null)
        {
            error = diagnostic?.Message ?? "The edited plan failed schema validation.";
            return false;
        }
        if (!DecomposePlanRevision.TryNormalize(parsed, out var result, out error))
            return false;
        if (!CodeHealthGroupRunner.HasNoDependencyCycle(result, out var cycleIds))
        {
            error = "The edited plan contains a dependency cycle involving: " +
                    string.Join(", ", cycleIds ?? []);
            return false;
        }
        normalized = result;
        return true;
    }

    internal static DecomposedTaskGroup UpdateTask(
        DecomposedTaskGroup group,
        string taskId,
        string title,
        string description,
        string priority,
        IReadOnlyList<string> dependsOn)
    {
        var tasks = group.Tasks.Select(task =>
            string.Equals(task.Id, taskId, StringComparison.Ordinal)
                ? task with
                {
                    Title = title.Trim(),
                    Description = description.Trim(),
                    Priority = priority.Trim(),
                    DependsOn = dependsOn.Distinct(StringComparer.Ordinal).ToArray(),
                }
                : task).ToArray();
        return group with { Tasks = tasks };
    }

    internal static DecomposedTaskGroup UpdateTaskAdvanced(
        DecomposedTaskGroup group,
        string taskId,
        IReadOnlyList<DecomposedAgentAssignment>? assignments,
        string? routingMode,
        bool? parallelEligible,
        string? genericAgentReason,
        string? executionMode)
    {
        var tasks = group.Tasks.Select(task => task.Id == taskId
            ? task with
            {
                AgentAssignments = assignments is { Count: > 0 } ? assignments : null,
                AgentRoutingMode = string.IsNullOrWhiteSpace(routingMode) ? null : routingMode.Trim(),
                ParallelEligible = parallelEligible,
                GenericAgentReason = string.IsNullOrWhiteSpace(genericAgentReason)
                    ? null
                    : genericAgentReason.Trim(),
                ExecutionMode = string.IsNullOrWhiteSpace(executionMode) ? null : executionMode.Trim(),
            }
            : task).ToArray();
        return group with { Tasks = tasks };
    }

    internal static DecomposedTaskGroup UpdateTaskVerificationLevel(
        DecomposedTaskGroup group,
        string taskId,
        string? verificationLevel)
    {
        var normalized = PlanTaskVerificationLevel.Normalize(verificationLevel);
        var tasks = group.Tasks.Select(task => task.Id == taskId
            ? task with
            {
                // Standard is the compatibility default, so omit it from serialized plans.
                VerificationLevel = normalized == PlanTaskVerificationLevel.Standard
                    ? null
                    : normalized,
            }
            : task).ToArray();
        return group with { Tasks = tasks };
    }

    internal static DecomposedTaskGroup UpdateValidation(
        DecomposedTaskGroup group,
        string validationId,
        string title,
        string description,
        string mode,
        IReadOnlyList<string> afterTaskIds,
        IReadOnlyList<string> beforeTaskIds,
        IReadOnlyList<string> assertions,
        IReadOnlyList<string> commands,
        bool revalidateAtCompletion,
        IReadOnlyList<string>? outputIds = null)
    {
        var validations = (group.Validations ?? []).Select(validation =>
            string.Equals(validation.ValidationId, validationId, StringComparison.Ordinal)
                ? validation with
                {
                    Title = title.Trim(),
                    Description = description.Trim(),
                    Mode = mode.Trim(),
                    AfterTaskIds = afterTaskIds.Distinct(StringComparer.Ordinal).ToArray(),
                    BeforeTaskIds = beforeTaskIds.Distinct(StringComparer.Ordinal).ToArray(),
                    Assertions = assertions.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
                    Commands = commands.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
                    RevalidateAtCompletion = revalidateAtCompletion,
                    OutputIds = outputIds is { Count: > 0 }
                        ? outputIds.Distinct(StringComparer.Ordinal).ToArray()
                        : null,
                }
                : validation).ToArray();
        return group with { Validations = validations.Length == 0 ? null : validations };
    }

    internal static DecomposedTaskGroup UpdateGate(
        DecomposedTaskGroup group,
        string gateId,
        string message,
        string? question,
        IReadOnlyList<string> afterTaskIds,
        IReadOnlyList<string> beforeTaskIds,
        IReadOnlyList<DecomposedTaskProofRequirement>? proofRequirements = null)
    {
        var gates = (group.ApprovalGates ?? []).Select(gate =>
            string.Equals(gate.GateId, gateId, StringComparison.Ordinal)
                ? gate with
                {
                    Message = message.Trim(),
                    Question = string.IsNullOrWhiteSpace(question) ? null : question.Trim(),
                    AfterTaskIds = afterTaskIds.Distinct(StringComparer.Ordinal).ToArray(),
                    BeforeTaskIds = beforeTaskIds.Distinct(StringComparer.Ordinal).ToArray(),
                    ProofRequirements = proofRequirements is { Count: > 0 } ? proofRequirements : null,
                }
                : gate).ToArray();
        return group with { ApprovalGates = gates.Length == 0 ? null : gates };
    }

    internal static DecomposedTaskGroup AddValidationAfterTask(
        DecomposedTaskGroup group,
        string taskId)
    {
        var id = NextValidationId(group);
        var before = group.Tasks
            .Where(task => task.DependsOn.Contains(taskId, StringComparer.Ordinal))
            .Select(task => task.Id)
            .ToArray();
        var validation = new DecomposedValidationNode(
            id,
            "Review " + (group.Tasks.First(task => task.Id == taskId).Title ?? taskId),
            "Verify the observable result before dependent work continues.",
            [taskId],
            before,
            ["The task's stated outcome is present and behaves as described."],
            Mode: "evidence");
        return DependencyEditRecomputer.ReconcileAuditBoundary(
            group with { Validations = (group.Validations ?? []).Append(validation).ToArray() });
    }

    internal static DecomposedTaskGroup InsertTask(
        DecomposedTaskGroup group,
        string anchorTaskId,
        bool before)
    {
        var anchorIndex = group.Tasks.ToList().FindIndex(task =>
            string.Equals(task.Id, anchorTaskId, StringComparison.Ordinal));
        if (anchorIndex < 0) return group;
        var anchor = group.Tasks[anchorIndex];
        var id = NextTaskId(group);
        var inserted = new DecomposedSubTask(
            id,
            "Describe the observable outcome and the production consumer for this step.",
            before ? anchor.DependsOn.ToArray() : [anchorTaskId],
            anchor.Priority,
            before ? "New task before " + (anchor.Title ?? anchor.Id) : "New task after " + (anchor.Title ?? anchor.Id),
            AgentRoutingMode: "generic",
            GenericAgentReason: "Newly inserted task — assign a routing agent or update this reason before executing the plan.");

        var tasks = group.Tasks.ToList();
        tasks.Insert(before ? anchorIndex : anchorIndex + 1, inserted);
        if (before)
        {
            tasks[anchorIndex + 1] = anchor with { DependsOn = [id] };
        }
        else
        {
            for (var index = 0; index < tasks.Count; index++)
            {
                var candidate = tasks[index];
                if (candidate.Id == id || candidate.Id == anchorTaskId ||
                    !candidate.DependsOn.Contains(anchorTaskId, StringComparer.Ordinal))
                    continue;
                tasks[index] = candidate with
                {
                    DependsOn = candidate.DependsOn
                        .Select(dependency => dependency == anchorTaskId ? id : dependency)
                        .ToArray(),
                };
            }
        }
        return DependencyEditRecomputer.ReconcileAuditBoundary(group with { Tasks = tasks.ToArray() });
    }

    internal static DecomposedTaskGroup DeleteTask(DecomposedTaskGroup group, string taskId)
    {
        var removed = group.Tasks.FirstOrDefault(task =>
            string.Equals(task.Id, taskId, StringComparison.Ordinal));
        if (removed is null || group.Tasks.Count <= 1) return group;

        var tasks = group.Tasks
            .Where(task => !string.Equals(task.Id, taskId, StringComparison.Ordinal))
            .Select(task => task.DependsOn.Contains(taskId, StringComparer.Ordinal)
                ? task with
                {
                    DependsOn = task.DependsOn
                        .Where(dependency => !string.Equals(dependency, taskId, StringComparison.Ordinal))
                        .Concat(removed.DependsOn)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray(),
                }
                : task)
            .ToArray();

        var dependentIds = group.Tasks
            .Where(task => task.DependsOn.Contains(taskId, StringComparer.Ordinal))
            .Select(task => task.Id)
            .ToArray();
        static IReadOnlyList<string> RewireBoundary(
            IReadOnlyList<string>? ids,
            string removedId,
            IReadOnlyList<string> replacements) =>
            (ids ?? []).SelectMany(id =>
                    string.Equals(id, removedId, StringComparison.Ordinal) ? replacements : [id])
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        var gates = (group.ApprovalGates ?? []).Select(gate => gate with
        {
            AfterTaskIds = RewireBoundary(gate.AfterTaskIds, taskId, removed.DependsOn),
            BeforeTaskIds = RewireBoundary(gate.BeforeTaskIds, taskId, dependentIds),
        }).ToArray();
        var validations = (group.Validations ?? []).Select(validation => validation with
        {
            AfterTaskIds = RewireBoundary(validation.AfterTaskIds, taskId, removed.DependsOn),
            BeforeTaskIds = RewireBoundary(validation.BeforeTaskIds, taskId, dependentIds),
        }).ToArray();

        // Strip input references and validation outputIds that point to outputs of the deleted task.
        var removedOutputIds = (removed.Outputs ?? [])
            .Select(output => output.OutputId)
            .ToHashSet(StringComparer.Ordinal);
        if (removedOutputIds.Count > 0)
        {
            tasks = tasks.Select(task =>
            {
                if (task.Inputs is null or { Count: 0 }) return task;
                var cleaned = task.Inputs.Where(id => !removedOutputIds.Contains(id)).ToArray();
                return cleaned.Length == task.Inputs.Count
                    ? task
                    : task with { Inputs = cleaned.Length == 0 ? null : cleaned };
            }).ToArray();

            validations = validations.Select(validation =>
            {
                if (validation.OutputIds is null or { Count: 0 }) return validation;
                var cleaned = validation.OutputIds.Where(id => !removedOutputIds.Contains(id)).ToArray();
                return cleaned.Length == validation.OutputIds.Count
                    ? validation
                    : validation with { OutputIds = cleaned.Length == 0 ? null : cleaned };
            }).ToArray();
        }

        return DependencyEditRecomputer.ReconcileAuditBoundary(group with
        {
            Tasks = tasks,
            ApprovalGates = gates.Length == 0 ? null : gates,
            Validations = validations.Length == 0 ? null : validations,
        });
    }

    internal static DecomposedTaskGroup DeleteValidation(
        DecomposedTaskGroup group,
        string validationId)
    {
        var validations = (group.Validations ?? [])
            .Where(validation => !string.Equals(
                validation.ValidationId, validationId, StringComparison.Ordinal))
            .ToArray();
        return group with { Validations = validations.Length == 0 ? null : validations };
    }

    internal static DecomposedTaskGroup DeleteGate(DecomposedTaskGroup group, string gateId)
    {
        var gates = (group.ApprovalGates ?? [])
            .Where(gate => !string.Equals(gate.GateId, gateId, StringComparison.Ordinal))
            .ToArray();
        return group with { ApprovalGates = gates.Length == 0 ? null : gates };
    }

    internal static string NextTaskId(DecomposedTaskGroup group) =>
        NextId(group.GroupId + "-", group.Tasks.Select(task => task.Id), group.GroupId + "-{0:000}");

    internal static string NextValidationId(DecomposedTaskGroup group) =>
        NextId(group.GroupId + "-VAL-", (group.Validations ?? []).Select(value => value.ValidationId),
            group.GroupId + "-VAL-{0:000}");

    private static string NextId(string prefix, IEnumerable<string> ids, string format)
    {
        var max = ids
            .Where(id => id.StartsWith(prefix, StringComparison.Ordinal))
            .Select(id => int.TryParse(id[prefix.Length..], out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, format, max + 1);
    }

    private static bool Same<T>(T left, T right) =>
        string.Equals(JsonSerializer.Serialize(left), JsonSerializer.Serialize(right), StringComparison.Ordinal);
}

internal sealed class StagedPlanEditHistory(DecomposedTaskGroup initial)
{
    private readonly Stack<DecomposedTaskGroup> _undo = new();
    private readonly Stack<DecomposedTaskGroup> _redo = new();

    internal DecomposedTaskGroup Current { get; private set; } = initial;
    internal bool CanUndo => _undo.Count > 0;
    internal bool CanRedo => _redo.Count > 0;
    internal DecomposedTaskGroup? PeekUndo => _undo.TryPeek(out var value) ? value : null;
    internal DecomposedTaskGroup? PeekRedo => _redo.TryPeek(out var value) ? value : null;

    internal bool Record(DecomposedTaskGroup next)
    {
        if (Same(Current, next)) return false;
        _undo.Push(Current);
        Current = next;
        _redo.Clear();
        return true;
    }

    internal DecomposedTaskGroup? Undo()
    {
        if (_undo.Count == 0) return null;
        _redo.Push(Current);
        Current = _undo.Pop();
        return Current;
    }

    internal DecomposedTaskGroup? Redo()
    {
        if (_redo.Count == 0) return null;
        _undo.Push(Current);
        Current = _redo.Pop();
        return Current;
    }

    internal void ReplaceCurrent(DecomposedTaskGroup persisted) => Current = persisted;

    private static bool Same(DecomposedTaskGroup left, DecomposedTaskGroup right) =>
        string.Equals(JsonSerializer.Serialize(left), JsonSerializer.Serialize(right), StringComparison.Ordinal);
}
