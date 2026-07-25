using System.IO;
using System.Text.Json;

namespace SquadDash;

internal sealed class PendingDecomposePlanStore(string squadFolderPath)
{
    // Pending approvals are transient host state, not accepted backlog content. Keep them under
    // .squad/tmp so they do not appear as project changes before the user has approved the plan.
    private readonly string _folder = Path.Combine(squadFolderPath, "tmp", "decompose");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    internal string Save(DecomposedTaskGroup group)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, group.GroupId + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(group, Options));
        return path;
    }

    internal DecomposedTaskGroup? Load(string groupId)
    {
        var path = Path.Combine(_folder, groupId + ".json");
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<DecomposedTaskGroup>(File.ReadAllText(path), Options); }
        catch (JsonException) { return null; }
    }

    internal void Delete(string groupId)
    {
        var path = Path.Combine(_folder, groupId + ".json");
        if (File.Exists(path)) File.Delete(path);
    }
}
