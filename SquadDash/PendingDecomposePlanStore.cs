using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SquadDash;

internal sealed record PendingDecomposePlan(string Revision, DecomposedTaskGroup Group);

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
        var plan = new PendingDecomposePlan(ComputeRevision(group), group);
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
        var json = JsonSerializer.Serialize(group);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant()[..16];
    }
}
