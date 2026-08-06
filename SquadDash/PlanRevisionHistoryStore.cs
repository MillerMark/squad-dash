using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace SquadDash;

/// <summary>Stores immutable preceding definitions for future revision navigation.</summary>
internal sealed class PlanRevisionHistoryStore(string squadFolderPath)
{
    private readonly string _root = Path.Combine(squadFolderPath, "plans", "history");
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal string SaveSnapshot(Plan plan)
    {
        var folder = Path.Combine(_root, plan.PlanId);
        Directory.CreateDirectory(folder);
        var ordinal = Math.Max(1, plan.RevisionNumber);
        var path = Path.Combine(folder, $"{ordinal:D4}-{Sanitize(plan.Revision)}.json");
        if (!File.Exists(path)) JsonFileStorage.AtomicWrite(path, plan, Options);
        return path;
    }

    internal IReadOnlyList<Plan> LoadSnapshots(string planId)
    {
        var folder = Path.Combine(_root, planId);
        if (!Directory.Exists(folder)) return [];
        var result = new List<Plan>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.json").OrderBy(path => path, StringComparer.Ordinal))
        {
            try
            {
                var plan = JsonSerializer.Deserialize<Plan>(File.ReadAllText(path), Options);
                if (plan is not null && string.Equals(plan.PlanId, planId, StringComparison.Ordinal)) result.Add(plan);
            }
            catch (JsonException) { }
        }
        return result;
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }
}
