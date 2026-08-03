using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

internal sealed record PendingDecomposePlan(
    string Revision,
    DecomposedTaskGroup Group,
    DateTimeOffset? CreatedAt = null);

internal sealed class PendingDecomposePlanStore(string squadFolderPath)
{
    // Pending approvals are transient host state, not accepted backlog content. Keep them under
    // .squad/tmp so they do not appear as project changes before the user has approved the plan.
    private readonly string _folder = Path.Combine(squadFolderPath, "tmp", "decompose");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    internal PendingDecomposePlan Save(DecomposedTaskGroup group)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, group.GroupId + ".json");
        var plan = new PendingDecomposePlan(ComputeRevision(group), group, DateTimeOffset.UtcNow);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(plan, Options));
        File.Move(tempPath, path, overwrite: true);
        return plan;
    }

    internal PendingDecomposePlan? Load(string groupId)
    {
        var path = Path.Combine(_folder, groupId + ".json");
        if (!File.Exists(path)) return null;
        try
        {
            var plan = JsonSerializer.Deserialize<PendingDecomposePlan>(File.ReadAllText(path), Options);
            if (plan is null || plan.Group is null ||
                !string.Equals(plan.Revision, ComputeRevision(plan.Group), StringComparison.Ordinal))
            {
                SquadDashTrace.Write(TraceCategory.General,
                    $"Pending decompose plan '{groupId}' failed revision validation.");
                return null;
            }
            return plan;
        }
        catch (JsonException ex)
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"Pending decompose plan '{groupId}' contains invalid JSON: {ex.Message}");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"Pending decompose plan '{groupId}' could not be read: {ex.Message}");
            return null;
        }
    }

    internal void Delete(string groupId)
    {
        var path = Path.Combine(_folder, groupId + ".json");
        if (File.Exists(path)) File.Delete(path);
    }

    internal IReadOnlyList<PendingDecomposePlan> LoadAll()
    {
        if (!Directory.Exists(_folder)) return [];
        var plans = new List<PendingDecomposePlan>();
        foreach (var path in Directory.EnumerateFiles(_folder, "*.json"))
        {
            var groupId = Path.GetFileNameWithoutExtension(path);
            var plan = Load(groupId);
            if (plan is not null) plans.Add(plan);
        }
        return plans;
    }

    internal static string ComputeRevision(DecomposedTaskGroup group)
    {
        // Plans saved before task titles existed must retain their original revision. New plans
        // include titles in the approval contract because the title is user-visible work data.
        // Plans with approval gates use V3 so gate boundaries are included in the revision.
        object payload;
        if (group.Validations is { Count: > 0 })
        {
            payload = new RevisionPayloadV4(
                group.GroupId,
                group.GroupTitle,
                group.Branch,
                group.Summary,
                group.Tasks,
                group.ApprovalGates ?? [],
                group.Validations);
        }
        else if (group.ApprovalGates is { Count: > 0 })
        {
            payload = new RevisionPayloadV3(
                group.GroupId,
                group.GroupTitle,
                group.Branch,
                group.Summary,
                group.Tasks,
                group.ApprovalGates);
        }
        else if (group.Tasks.All(task => string.IsNullOrWhiteSpace(task.Title)))
        {
            payload = new RevisionPayloadV1(
                group.GroupId,
                group.GroupTitle,
                group.Branch,
                group.Summary,
                group.Tasks.Select(task => new RevisionTaskV1(
                    task.Id, task.Description, task.DependsOn, task.Priority)).ToArray());
        }
        else
        {
            payload = new RevisionPayloadV2(
                group.GroupId,
                group.GroupTitle,
                group.Branch,
                group.Summary,
                group.Tasks);
        }
        var json = JsonSerializer.Serialize(payload);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant()[..16];
    }

    private sealed record RevisionPayloadV1(
        [property: JsonPropertyName("groupId")] string GroupId,
        [property: JsonPropertyName("groupTitle")] string GroupTitle,
        [property: JsonPropertyName("branch")] string Branch,
        [property: JsonPropertyName("summary")] string Summary,
        [property: JsonPropertyName("tasks")] IReadOnlyList<RevisionTaskV1> Tasks);

    private sealed record RevisionTaskV1(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("dependsOn")] IReadOnlyList<string> DependsOn,
        [property: JsonPropertyName("priority")] string Priority);

    private sealed record RevisionPayloadV2(
        [property: JsonPropertyName("groupId")] string GroupId,
        [property: JsonPropertyName("groupTitle")] string GroupTitle,
        [property: JsonPropertyName("branch")] string Branch,
        [property: JsonPropertyName("summary")] string Summary,
        [property: JsonPropertyName("tasks")] IReadOnlyList<DecomposedSubTask> Tasks);

    private sealed record RevisionPayloadV3(
        [property: JsonPropertyName("groupId")] string GroupId,
        [property: JsonPropertyName("groupTitle")] string GroupTitle,
        [property: JsonPropertyName("branch")] string Branch,
        [property: JsonPropertyName("summary")] string Summary,
        [property: JsonPropertyName("tasks")] IReadOnlyList<DecomposedSubTask> Tasks,
        [property: JsonPropertyName("approvalGates")] IReadOnlyList<DecomposedGate> ApprovalGates);

    private sealed record RevisionPayloadV4(
        [property: JsonPropertyName("groupId")] string GroupId,
        [property: JsonPropertyName("groupTitle")] string GroupTitle,
        [property: JsonPropertyName("branch")] string Branch,
        [property: JsonPropertyName("summary")] string Summary,
        [property: JsonPropertyName("tasks")] IReadOnlyList<DecomposedSubTask> Tasks,
        [property: JsonPropertyName("approvalGates")] IReadOnlyList<DecomposedGate> ApprovalGates,
        [property: JsonPropertyName("validations")] IReadOnlyList<DecomposedValidationNode> Validations);
}
