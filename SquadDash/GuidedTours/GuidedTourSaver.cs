using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SquadDash.GuidedTours;

/// <summary>
/// Persists guided tours to the workspace override file at
/// <c>.squad/guided-tours.json</c>.
/// </summary>
internal static class GuidedTourSaver
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Serialises <paramref name="tours"/> to
    /// <c>&lt;workspaceFolderPath&gt;/.squad/guided-tours.json</c>,
    /// creating the directory if necessary.
    /// </summary>
    internal static void Save(List<GuidedTour> tours, string workspaceFolderPath)
    {
        var path = Path.Combine(workspaceFolderPath, ".squad", "guided-tours.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(tours, JsonOptions));
    }
}
