using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

internal sealed record StagedPlanRevisionOutline(
    [property: JsonPropertyName("groupId")] string GroupId,
    [property: JsonPropertyName("baseRevision")] string BaseRevision,
    [property: JsonPropertyName("sizeClass")] string SizeClass,
    [property: JsonPropertyName("serializedBytes")] int SerializedBytes,
    [property: JsonPropertyName("revisionStrategy")] string RevisionStrategy,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("summaryPreview")] string SummaryPreview,
    [property: JsonPropertyName("summaryChars")] int SummaryChars,
    [property: JsonPropertyName("tasks")] IReadOnlyList<StagedPlanTaskOutline> Tasks,
    [property: JsonPropertyName("approvalGates")] IReadOnlyList<StagedPlanBoundaryOutline> ApprovalGates,
    [property: JsonPropertyName("validations")] IReadOnlyList<StagedPlanBoundaryOutline> Validations);

internal sealed record StagedPlanTaskOutline(
    [property: JsonPropertyName("ref")] string Ref,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("dependsOn")] IReadOnlyList<string> DependsOn,
    [property: JsonPropertyName("downstream")] IReadOnlyList<string> Downstream,
    [property: JsonPropertyName("relatedGates")] IReadOnlyList<string> RelatedGates,
    [property: JsonPropertyName("relatedValidations")] IReadOnlyList<string> RelatedValidations,
    [property: JsonPropertyName("outputIds")] IReadOnlyList<string> OutputIds,
    [property: JsonPropertyName("inputIds")] IReadOnlyList<string> InputIds,
    [property: JsonPropertyName("descriptionChars")] int DescriptionChars,
    [property: JsonPropertyName("source")] string Source);

internal sealed record StagedPlanBoundaryOutline(
    [property: JsonPropertyName("ref")] string Ref,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("after")] IReadOnlyList<string> After,
    [property: JsonPropertyName("before")] IReadOnlyList<string> Before,
    [property: JsonPropertyName("textChars")] int TextChars,
    [property: JsonPropertyName("source")] string Source);

internal static class StagedPlanRevisionOutlineStore
{
    internal const int SmallPlanMaxBytes = 12 * 1024;
    internal const int LargePlanMinBytes = 32 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static string Save(string squadFolderPath, PendingDecomposePlan pending)
    {
        var folder = Path.Combine(squadFolderPath, "tmp", "decompose", "outlines");
        var path = Path.Combine(folder, pending.Group.GroupId + ".json");
        try
        {
            Directory.CreateDirectory(folder);
            JsonFileStorage.AtomicWrite(path, Build(pending), Options);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"Could not materialize staged-plan outline for '{pending.Group.GroupId}': {exception.Message}");
        }
        return path;
    }

    internal static void Delete(string squadFolderPath, string groupId)
    {
        var path = GetPath(squadFolderPath, groupId);
        if (File.Exists(path)) File.Delete(path);
        var backup = path + ".backup";
        if (File.Exists(backup)) File.Delete(backup);
    }

    internal static string GetRelativePath(string groupId) =>
        $".squad/tmp/decompose/outlines/{groupId}.json";

    internal static string GetPath(string squadFolderPath, string groupId) =>
        Path.Combine(squadFolderPath, "tmp", "decompose", "outlines", groupId + ".json");

    internal static StagedPlanRevisionOutline Build(PendingDecomposePlan pending)
    {
        var group = pending.Group;
        var serializedBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(group));
        var sizeClass = Classify(serializedBytes, group.Tasks.Count);
        var taskRefs = group.Tasks
            .Select((task, index) => (task.Id, Ref: $"T{index + 1}"))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(grouping => grouping.Key, grouping => grouping.First().Ref, StringComparer.Ordinal);
        var gateEntries = (group.ApprovalGates ?? [])
            .Select((gate, index) => (Gate: gate, Ref: $"G{index + 1}"))
            .ToArray();
        var validationEntries = (group.Validations ?? [])
            .Select((validation, index) => (Validation: validation, Ref: $"V{index + 1}"))
            .ToArray();

        var tasks = group.Tasks.Select((task, index) => new StagedPlanTaskOutline(
            $"T{index + 1}",
            Preview(task.Title ?? task.Description, 100),
            MapTaskRefs(task.DependsOn, taskRefs),
            group.Tasks
                .Where(candidate => candidate.DependsOn.Contains(task.Id, StringComparer.Ordinal))
                .Select(candidate => taskRefs[candidate.Id])
                .ToArray(),
            gateEntries.Where(entry => ReferencesTask(
                    entry.Gate.AfterTaskIds, entry.Gate.BeforeTaskIds, task.Id))
                .Select(entry => entry.Ref)
                .ToArray(),
            validationEntries.Where(entry => ReferencesTask(
                    entry.Validation.AfterTaskIds, entry.Validation.BeforeTaskIds, task.Id))
                .Select(entry => entry.Ref)
                .ToArray(),
            (task.Outputs ?? []).Select(output => output.OutputId).ToArray(),
            (task.Inputs ?? []).ToArray(),
            task.Description?.Length ?? 0,
            $"Group.tasks[{index}]"
        )).ToArray();

        var gates = (group.ApprovalGates ?? []).Select((gate, index) =>
            new StagedPlanBoundaryOutline(
                $"G{index + 1}",
                Preview(gate.Question ?? gate.Message, 100),
                MapTaskRefs(gate.AfterTaskIds ?? [], taskRefs),
                MapTaskRefs(gate.BeforeTaskIds ?? [], taskRefs),
                (gate.Message?.Length ?? 0) + (gate.Question?.Length ?? 0),
                $"Group.approvalGates[{index}]"
            )).ToArray();
        var validations = (group.Validations ?? []).Select((validation, index) =>
            new StagedPlanBoundaryOutline(
                $"V{index + 1}",
                Preview(validation.Title, 100),
                MapTaskRefs(validation.AfterTaskIds, taskRefs),
                MapTaskRefs(validation.BeforeTaskIds, taskRefs),
                (validation.Description?.Length ?? 0) + validation.Assertions.Sum(value => value?.Length ?? 0),
                $"Group.validations[{index}]"
            )).ToArray();

        return new StagedPlanRevisionOutline(
            group.GroupId,
            pending.Revision,
            sizeClass,
            serializedBytes,
            Strategy(sizeClass),
            group.GroupTitle,
            Preview(group.Summary, 600),
            group.Summary?.Length ?? 0,
            tasks,
            gates,
            validations);
    }

    internal static bool TryResolveTargetRef(
        DecomposedTaskGroup group,
        string targetRef,
        string operation,
        out string? targetId,
        out string? error)
    {
        targetId = null;
        error = null;
        var normalized = targetRef.Trim().ToUpperInvariant();
        if (normalized.Length < 2 || !int.TryParse(normalized[1..], out var ordinal) || ordinal < 1)
        {
            error = $"targetRef '{targetRef}' is invalid. Use T1, G1, or V1 from the current outline.";
            return false;
        }

        IReadOnlyList<string> candidates;
        char requiredPrefix;
        if (operation is "updateTask" or "editTaskText" or "removeTask")
        {
            requiredPrefix = 'T';
            candidates = group.Tasks.Select(task => task.Id).ToArray();
        }
        else if (operation is "updateApprovalGate" or "editApprovalGateText" or "removeApprovalGate")
        {
            requiredPrefix = 'G';
            candidates = (group.ApprovalGates ?? []).Select(gate => gate.GateId).ToArray();
        }
        else if (operation is "updateValidation" or "editValidationText" or "removeValidation")
        {
            requiredPrefix = 'V';
            candidates = (group.Validations ?? []).Select(validation => validation.ValidationId).ToArray();
        }
        else
        {
            error = $"Operation '{operation}' does not accept targetRef.";
            return false;
        }

        if (normalized[0] != requiredPrefix)
        {
            error = $"Operation '{operation}' requires a {requiredPrefix}-reference, not '{targetRef}'.";
            return false;
        }
        if (ordinal > candidates.Count)
        {
            error = $"targetRef '{targetRef}' does not exist in the current outline.";
            return false;
        }
        targetId = candidates[ordinal - 1];
        return true;
    }

    private static string Classify(int serializedBytes, int taskCount) =>
        serializedBytes <= SmallPlanMaxBytes && taskCount <= 5
            ? "small"
            : serializedBytes >= LargePlanMinBytes || taskCount >= 12
                ? "large"
                : "medium";

    private static string Strategy(string sizeClass) => sizeClass switch
    {
        "small" => "Prefer a delta; a replacement TASKS_JSON is acceptable for a broad rewrite affecting most of the plan.",
        "medium" => "Strongly prefer a delta; use replacement TASKS_JSON only for a sweeping graph rewrite.",
        _ => "Use a compact delta for targeted changes. Replace TASKS_JSON only for a genuine near-total rewrite.",
    };

    private static IReadOnlyList<string> MapTaskRefs(
        IReadOnlyList<string> ids,
        IReadOnlyDictionary<string, string> refs) =>
        ids.Select(id => refs.TryGetValue(id, out var compact) ? compact : "?").ToArray();

    private static bool ReferencesTask(
        IReadOnlyList<string>? after,
        IReadOnlyList<string>? before,
        string taskId) =>
        (after?.Contains(taskId, StringComparer.Ordinal) ?? false) ||
        (before?.Contains(taskId, StringComparer.Ordinal) ?? false);

    private static string Preview(string? value, int maxChars)
    {
        var normalized = string.Join(" ", (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxChars ? normalized : normalized[..(maxChars - 1)] + "…";
    }
}
