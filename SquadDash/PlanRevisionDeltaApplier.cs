using System.Text.Json;
using System.Text.Json.Nodes;

namespace SquadDash;

/// <summary>
/// Materializes a concise, AI-authored plan delta against the durable base definition. The
/// resulting complete group still passes through PlanRevisionApplier, so all existing graph,
/// assignment, runtime-state, and approval checks remain authoritative.
/// </summary>
internal static class PlanRevisionDeltaApplier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly IReadOnlySet<string> PlanPatchFields = Set(
        "groupTitle", "branch", "summary");
    private static readonly IReadOnlySet<string> TaskPatchFields = Set(
        "description", "dependsOn", "priority", "title", "parentTaskId", "agentAssignments",
        "parallelEligible", "agentRoutingMode", "genericAgentReason", "outputs", "inputs",
        "proofRequirements", "amendmentGateId", "executionMode", "verificationLevel",
        "contractExamples");
    private static readonly IReadOnlySet<string> GatePatchFields = Set(
        "message", "afterTaskIds", "beforeTaskIds", "proofRequirements", "question");
    private static readonly IReadOnlySet<string> ValidationPatchFields = Set(
        "title", "description", "afterTaskIds", "beforeTaskIds", "assertions", "outputIds",
        "mode", "commands", "revalidateAtCompletion");

    internal static bool TryMaterialize(
        Plan current,
        PlanRevisionProposalPayload payload,
        out DecomposedTaskGroup? revisedPlan,
        out IReadOnlySet<string> reopenTaskIds,
        out string? error)
    {
        revisedPlan = null;
        reopenTaskIds = new HashSet<string>(StringComparer.Ordinal);
        error = null;

        // Compatibility for approval proposals persisted before the delta protocol shipped.
        if (payload.Operations is not { Count: > 0 })
        {
            if (payload.RevisedPlan is null)
            {
                error = "The proposal contains no delta operations.";
                return false;
            }

            reopenTaskIds = (payload.ReopenTaskIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .ToHashSet(StringComparer.Ordinal);
            revisedPlan = payload.RevisedPlan;
            return true;
        }

        var operations = payload.Operations;
        var reopened = operations
            .Where(operation => string.Equals(operation.Op, "reopenTask", StringComparison.Ordinal))
            .Select(operation => operation.TargetId?.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        reopenTaskIds = reopened;

        var group = PendingDecomposePlanAdapter.FromPlan(current).Group with
        {
            Delivery = null,
            HostRevision = null,
        };
        var tasks = group.Tasks.ToList();
        var gates = (group.ApprovalGates ?? []).ToList();
        var validations = (group.Validations ?? []).ToList();
        var mutationKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var operation in operations)
        {
            var op = operation.Op?.Trim() ?? string.Empty;
            var targetId = operation.TargetId?.Trim();
            if (op.Length == 0)
                return Fail("Every plan revision operation requires op.", out revisedPlan, out error);

            if (string.Equals(op, "reopenTask", StringComparison.Ordinal))
            {
                if (!RequireTarget(targetId, op, out error)) return false;
                var runtimeTask = current.Tasks.FirstOrDefault(task => task.TaskId == targetId);
                if (runtimeTask is null)
                    return Fail($"reopenTask target '{targetId}' does not exist.", out revisedPlan, out error);
                if (runtimeTask.Status is not (PlanTaskStatus.Complete or PlanTaskStatus.Partial or
                    PlanTaskStatus.Failed or PlanTaskStatus.HumanReviewRequired))
                    return Fail($"Task '{targetId}' cannot be reopened from status {runtimeTask.Status}.", out revisedPlan, out error);
                continue;
            }

            var mutationKey = op switch
            {
                "updatePlan" => "plan",
                "editPlanText" => "plan:text:" + operation.Path + ":" + operation.OldText,
                "updateTask" or "removeTask" => "task:" + targetId,
                "editTaskText" => "task:" + targetId + ":text:" + operation.Path + ":" + operation.OldText,
                "addTask" => "task:" + operation.Task?.Id,
                "updateApprovalGate" or "removeApprovalGate" => "gate:" + targetId,
                "editApprovalGateText" => "gate:" + targetId + ":text:" + operation.Path + ":" + operation.OldText,
                "addApprovalGate" => "gate:" + operation.ApprovalGate?.GateId,
                "updateValidation" or "removeValidation" => "validation:" + targetId,
                "editValidationText" => "validation:" + targetId + ":text:" + operation.Path + ":" + operation.OldText,
                "addValidation" => "validation:" + operation.Validation?.ValidationId,
                _ => string.Empty,
            };
            if (mutationKey.Length == 0)
                return Fail($"Unsupported plan revision operation '{op}'.", out revisedPlan, out error);
            if (!mutationKeys.Add(mutationKey))
                return Fail($"The proposal changes '{mutationKey}' more than once. Combine those changes into one operation.", out revisedPlan, out error);

            switch (op)
            {
                case "updatePlan":
                    if (!TryPatch(group, operation.Patch, PlanPatchFields, op, out DecomposedTaskGroup? updatedGroup, out error) ||
                        updatedGroup is null)
                        return false;
                    if (!string.Equals(updatedGroup.GroupId, current.PlanId, StringComparison.Ordinal))
                        return Fail("updatePlan cannot change groupId.", out revisedPlan, out error);
                    group = updatedGroup;
                    break;

                case "editPlanText":
                    if (!TryEditText(group, operation, PlanPatchFields, op, out DecomposedTaskGroup? editedGroup, out error) ||
                        editedGroup is null)
                        return false;
                    if (!string.Equals(editedGroup.GroupId, current.PlanId, StringComparison.Ordinal))
                        return Fail("editPlanText cannot change groupId.", out revisedPlan, out error);
                    group = editedGroup;
                    break;

                case "updateTask":
                {
                    if (!RequireTarget(targetId, op, out error)) return false;
                    var index = tasks.FindIndex(task => task.Id == targetId);
                    if (index < 0)
                        return Fail($"updateTask target '{targetId}' does not exist.", out revisedPlan, out error);
                    if (!CanChangeTask(current, targetId!, reopened, removing: false, out error)) return false;
                    if (!TryPatch(tasks[index], operation.Patch, TaskPatchFields, op, out DecomposedSubTask? updated, out error)) return false;
                    if (!string.Equals(updated!.Id, targetId, StringComparison.Ordinal))
                        return Fail("updateTask cannot change a task ID.", out revisedPlan, out error);
                    tasks[index] = updated;
                    break;
                }

                case "editTaskText":
                {
                    if (!RequireTarget(targetId, op, out error)) return false;
                    var index = tasks.FindIndex(task => task.Id == targetId);
                    if (index < 0)
                        return Fail($"editTaskText target '{targetId}' does not exist.", out revisedPlan, out error);
                    if (!CanChangeTask(current, targetId!, reopened, removing: false, out error)) return false;
                    if (!TryEditText(tasks[index], operation, TaskPatchFields, op, out DecomposedSubTask? edited, out error) ||
                        edited is null)
                        return false;
                    if (!string.Equals(edited.Id, targetId, StringComparison.Ordinal))
                        return Fail("editTaskText cannot change a task ID.", out revisedPlan, out error);
                    tasks[index] = edited;
                    break;
                }

                case "addTask":
                    if (operation.Task is null)
                        return Fail("addTask requires task.", out revisedPlan, out error);
                    if (tasks.Any(task => task.Id == operation.Task.Id))
                        return Fail($"Task '{operation.Task.Id}' already exists.", out revisedPlan, out error);
                    tasks.Add(operation.Task);
                    break;

                case "removeTask":
                {
                    if (!RequireTarget(targetId, op, out error)) return false;
                    var index = tasks.FindIndex(task => task.Id == targetId);
                    if (index < 0)
                        return Fail($"removeTask target '{targetId}' does not exist.", out revisedPlan, out error);
                    if (!CanChangeTask(current, targetId!, reopened, removing: true, out error)) return false;
                    tasks.RemoveAt(index);
                    break;
                }

                case "updateApprovalGate":
                {
                    if (!RequireTarget(targetId, op, out error)) return false;
                    var index = gates.FindIndex(gate => gate.GateId == targetId);
                    if (index < 0)
                        return Fail($"updateApprovalGate target '{targetId}' does not exist.", out revisedPlan, out error);
                    if (!TryPatch(gates[index], operation.Patch, GatePatchFields, op, out DecomposedGate? updated, out error)) return false;
                    if (!string.Equals(updated!.GateId, targetId, StringComparison.Ordinal))
                        return Fail("updateApprovalGate cannot change a gate ID.", out revisedPlan, out error);
                    gates[index] = updated;
                    break;
                }

                case "editApprovalGateText":
                {
                    if (!RequireTarget(targetId, op, out error)) return false;
                    var index = gates.FindIndex(gate => gate.GateId == targetId);
                    if (index < 0)
                        return Fail($"editApprovalGateText target '{targetId}' does not exist.", out revisedPlan, out error);
                    if (!TryEditText(gates[index], operation, GatePatchFields, op, out DecomposedGate? edited, out error) ||
                        edited is null)
                        return false;
                    if (!string.Equals(edited.GateId, targetId, StringComparison.Ordinal))
                        return Fail("editApprovalGateText cannot change a gate ID.", out revisedPlan, out error);
                    gates[index] = edited;
                    break;
                }

                case "addApprovalGate":
                    if (operation.ApprovalGate is null)
                        return Fail("addApprovalGate requires approvalGate.", out revisedPlan, out error);
                    if (gates.Any(gate => gate.GateId == operation.ApprovalGate.GateId))
                        return Fail($"Approval gate '{operation.ApprovalGate.GateId}' already exists.", out revisedPlan, out error);
                    gates.Add(operation.ApprovalGate);
                    break;

                case "removeApprovalGate":
                    if (!RequireTarget(targetId, op, out error)) return false;
                    if (gates.RemoveAll(gate => gate.GateId == targetId) == 0)
                        return Fail($"removeApprovalGate target '{targetId}' does not exist.", out revisedPlan, out error);
                    break;

                case "updateValidation":
                {
                    if (!RequireTarget(targetId, op, out error)) return false;
                    var index = validations.FindIndex(validation => validation.ValidationId == targetId);
                    if (index < 0)
                        return Fail($"updateValidation target '{targetId}' does not exist.", out revisedPlan, out error);
                    if (!TryPatch(validations[index], operation.Patch, ValidationPatchFields, op, out DecomposedValidationNode? updated, out error)) return false;
                    if (!string.Equals(updated!.ValidationId, targetId, StringComparison.Ordinal))
                        return Fail("updateValidation cannot change a validation ID.", out revisedPlan, out error);
                    validations[index] = updated;
                    break;
                }

                case "editValidationText":
                {
                    if (!RequireTarget(targetId, op, out error)) return false;
                    var index = validations.FindIndex(validation => validation.ValidationId == targetId);
                    if (index < 0)
                        return Fail($"editValidationText target '{targetId}' does not exist.", out revisedPlan, out error);
                    if (!TryEditText(validations[index], operation, ValidationPatchFields, op, out DecomposedValidationNode? edited, out error) ||
                        edited is null)
                        return false;
                    if (!string.Equals(edited.ValidationId, targetId, StringComparison.Ordinal))
                        return Fail("editValidationText cannot change a validation ID.", out revisedPlan, out error);
                    validations[index] = edited;
                    break;
                }

                case "addValidation":
                    if (operation.Validation is null)
                        return Fail("addValidation requires validation.", out revisedPlan, out error);
                    if (validations.Any(validation => validation.ValidationId == operation.Validation.ValidationId))
                        return Fail($"Validation '{operation.Validation.ValidationId}' already exists.", out revisedPlan, out error);
                    validations.Add(operation.Validation);
                    break;

                case "removeValidation":
                    if (!RequireTarget(targetId, op, out error)) return false;
                    if (validations.RemoveAll(validation => validation.ValidationId == targetId) == 0)
                        return Fail($"removeValidation target '{targetId}' does not exist.", out revisedPlan, out error);
                    break;
            }
        }

        revisedPlan = group with
        {
            Tasks = tasks,
            ApprovalGates = gates.Count == 0 ? null : gates,
            Validations = validations.Count == 0 ? null : validations,
        };
        return true;
    }

    private static bool CanChangeTask(
        Plan current,
        string taskId,
        IReadOnlySet<string> reopened,
        bool removing,
        out string? error)
    {
        error = null;
        var task = current.Tasks.FirstOrDefault(candidate => candidate.TaskId == taskId);
        if (task is null) return true;
        if (task.Status == PlanTaskStatus.Pending &&
            !string.Equals(current.Progress.ExecutingTaskId, taskId, StringComparison.Ordinal))
            return true;
        if (!removing && reopened.Contains(taskId) &&
            task.Status is PlanTaskStatus.Complete or PlanTaskStatus.Partial or
                PlanTaskStatus.Failed or PlanTaskStatus.HumanReviewRequired)
            return true;

        error = removing
            ? $"Task '{taskId}' has execution history and cannot be removed. Preserve it or add a replacement task."
            : $"Task '{taskId}' is {task.Status} and must be reopened before its definition can change.";
        return false;
    }

    private static bool TryPatch<T>(
        T source,
        JsonElement? patch,
        IReadOnlySet<string> allowedFields,
        string operation,
        out T? updated,
        out string? error)
    {
        updated = default;
        error = null;
        if (patch is not { ValueKind: JsonValueKind.Object } patchObject)
        {
            error = $"{operation} requires an object-valued patch.";
            return false;
        }

        foreach (var property in patchObject.EnumerateObject())
        {
            if (!allowedFields.Contains(property.Name))
            {
                error = $"{operation} cannot patch '{property.Name}'. Allowed fields: {string.Join(", ", allowedFields)}.";
                return false;
            }
        }

        var node = JsonSerializer.SerializeToNode(source, JsonOptions) as JsonObject;
        if (node is null)
        {
            error = $"{operation} could not serialize its current target.";
            return false;
        }

        MergePatch(node, patchObject);
        try
        {
            updated = node.Deserialize<T>(JsonOptions);
            if (updated is not null) return true;
            error = $"{operation} produced an empty target.";
            return false;
        }
        catch (JsonException exception)
        {
            error = $"{operation} patch was invalid: {exception.Message}";
            return false;
        }
    }

    private static void MergePatch(JsonObject target, JsonElement patch)
    {
        foreach (var property in patch.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                target.Remove(property.Name);
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Object && target[property.Name] is JsonObject child)
            {
                MergePatch(child, property.Value);
                continue;
            }

            target[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        }
    }

    private static bool TryEditText<T>(
        T source,
        PlanRevisionOperation operation,
        IReadOnlySet<string> allowedRootFields,
        string operationName,
        out T? updated,
        out string? error)
    {
        updated = default;
        error = null;
        var path = operation.Path;
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/", StringComparison.Ordinal))
        {
            error = $"{operationName} requires a JSON pointer path beginning with '/'.";
            return false;
        }
        if (string.IsNullOrEmpty(operation.OldText))
        {
            error = $"{operationName} requires non-empty oldText.";
            return false;
        }
        if (operation.NewText is null)
        {
            error = $"{operationName} requires newText; use an empty string to delete matched text.";
            return false;
        }

        var segments = path.Split('/').Skip(1).Select(DecodePointerSegment).ToArray();
        if (segments.Length == 0 || segments.Any(segment => segment is null))
        {
            error = $"{operationName} path '{path}' is not a valid JSON pointer.";
            return false;
        }
        var decoded = segments.Cast<string>().ToArray();
        if (!allowedRootFields.Contains(decoded[0]))
        {
            error = $"{operationName} cannot edit '{decoded[0]}'. Allowed roots: {string.Join(", ", allowedRootFields)}.";
            return false;
        }

        var root = JsonSerializer.SerializeToNode(source, JsonOptions);
        if (root is null || !TryResolvePointerParent(root, decoded, out var parent, out var leaf, out error))
            return false;

        string? currentText;
        Action<string> assign;
        if (parent is JsonObject objectParent)
        {
            if (!objectParent.TryGetPropertyValue(leaf, out var value) ||
                value is not JsonValue jsonValue ||
                !jsonValue.TryGetValue<string>(out currentText))
            {
                error = $"{operationName} path '{path}' does not identify a string property.";
                return false;
            }
            assign = value => objectParent[leaf] = value;
        }
        else if (parent is JsonArray arrayParent &&
                 int.TryParse(leaf, out var index) &&
                 index >= 0 && index < arrayParent.Count &&
                 arrayParent[index] is JsonValue arrayValue &&
                 arrayValue.TryGetValue<string>(out currentText))
        {
            assign = value => arrayParent[index] = value;
        }
        else
        {
            error = $"{operationName} path '{path}' does not identify a string value.";
            return false;
        }

        var occurrences = CountOccurrences(currentText!, operation.OldText);
        if (occurrences != 1)
        {
            error = $"{operationName} oldText must occur exactly once at '{path}', but occurred {occurrences} times.";
            return false;
        }
        assign(currentText!.Replace(operation.OldText, operation.NewText, StringComparison.Ordinal));
        try
        {
            updated = root.Deserialize<T>(JsonOptions);
            if (updated is not null) return true;
            error = $"{operationName} produced an empty target.";
            return false;
        }
        catch (JsonException exception)
        {
            error = $"{operationName} edit was invalid: {exception.Message}";
            return false;
        }
    }

    private static bool TryResolvePointerParent(
        JsonNode root,
        IReadOnlyList<string> segments,
        out JsonNode? parent,
        out string leaf,
        out string? error)
    {
        parent = root;
        leaf = segments[^1];
        error = null;
        for (var index = 0; index < segments.Count - 1; index++)
        {
            var segment = segments[index];
            parent = parent switch
            {
                JsonObject currentObject when currentObject.TryGetPropertyValue(segment, out var child) => child,
                JsonArray currentArray when int.TryParse(segment, out var arrayIndex) &&
                                            arrayIndex >= 0 && arrayIndex < currentArray.Count => currentArray[arrayIndex],
                _ => null,
            };
            if (parent is not null) continue;
            error = $"Text edit path could not resolve segment '{segment}'.";
            return false;
        }
        return true;
    }

    private static string? DecodePointerSegment(string segment)
    {
        var builder = new System.Text.StringBuilder(segment.Length);
        for (var index = 0; index < segment.Length; index++)
        {
            if (segment[index] != '~')
            {
                builder.Append(segment[index]);
                continue;
            }
            if (++index >= segment.Length) return null;
            var decoded = segment[index] switch
            {
                '0' => '~',
                '1' => '/',
                _ => '\0',
            };
            if (decoded == '\0') return null;
            builder.Append(decoded);
        }
        return builder.ToString();
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }
        return count;
    }

    private static bool RequireTarget(string? targetId, string operation, out string? error)
    {
        error = string.IsNullOrWhiteSpace(targetId) ? $"{operation} requires targetId." : null;
        return error is null;
    }

    private static bool Fail(string message, out DecomposedTaskGroup? revisedPlan, out string? error)
    {
        revisedPlan = null;
        error = message;
        return false;
    }

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}
