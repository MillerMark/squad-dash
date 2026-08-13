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
        "proofRequirements", "amendmentGateId", "executionMode");
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
                "updateTask" or "removeTask" => "task:" + targetId,
                "addTask" => "task:" + operation.Task?.Id,
                "updateApprovalGate" or "removeApprovalGate" => "gate:" + targetId,
                "addApprovalGate" => "gate:" + operation.ApprovalGate?.GateId,
                "updateValidation" or "removeValidation" => "validation:" + targetId,
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
