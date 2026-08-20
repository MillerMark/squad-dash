using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SquadDash;

internal sealed class PlanVisualizerLayoutStore(string squadFolderPath)
{
    private readonly string _folder = Path.Combine(squadFolderPath, "tmp", "decompose", "layout");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    internal void Save(string groupId, Dictionary<string, double> nudges)
    {
        Directory.CreateDirectory(_folder);
        JsonFileStorage.AtomicWrite(Path.Combine(_folder, groupId + ".json"), nudges, Options);
    }

    internal Dictionary<string, double>? Load(string groupId)
    {
        var path = Path.Combine(_folder, groupId + ".json");
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(path), Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            SquadDashTrace.Write("PlanLayout", $"Could not load layout nudges for {groupId}: {ex.Message}");
            return null;
        }
    }

    internal void Delete(string groupId)
    {
        var path = Path.Combine(_folder, groupId + ".json");
        if (File.Exists(path)) File.Delete(path);
    }
}
