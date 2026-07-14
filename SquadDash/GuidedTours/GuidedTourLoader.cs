using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Text.Json;

namespace SquadDash.GuidedTours;

/// <summary>
/// Loads guided tours from the tracked application asset when running from a
/// source workspace, or from the embedded application resource otherwise.
/// </summary>
internal static class GuidedTourLoader
{
    private const string EmbeddedResourceName = "SquadDash.Assets.guided-tours.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
    };

    /// <summary>
    /// Loads the list of guided tours.
    /// Checks the tracked source asset inside <paramref name="workspaceFolderPath"/> first,
    /// then falls back to the embedded resource in installed builds.
    /// </summary>
    internal static List<GuidedTour> Load(string? workspaceFolderPath)
    {
        if (!string.IsNullOrWhiteSpace(workspaceFolderPath))
        {
            var sourcePath = GuidedTourSaver.GetPath(workspaceFolderPath);
            if (File.Exists(sourcePath))
            {
                try
                {
                    var json = File.ReadAllText(sourcePath);
                    var tours = JsonSerializer.Deserialize<List<GuidedTour>>(json, JsonOptions);
                    if (tours is { Count: > 0 })
                    {
                        LogResult("source asset", sourcePath, tours, GuidedTourSaver.ComputeHash(json));
                        return tours;
                    }
                    SquadDashTrace.Write(TraceCategory.Callouts,
                        $"GuidedTourLoader.Load: source asset parse produced no tours, path=\"{sourcePath}\"; falling back to embedded");
                }
                catch (System.Exception ex)
                {
                    SquadDashTrace.Write(TraceCategory.Callouts,
                        $"GuidedTourLoader.Load: source asset load failed, path=\"{sourcePath}\", error={ex.Message}; falling back to embedded");
                }
            }
            else SquadDashTrace.Write(TraceCategory.Callouts,
                $"GuidedTourLoader.Load: source asset absent, path=\"{sourcePath}\"; loading embedded");
        }

        return LoadEmbedded();
    }

    private static List<GuidedTour> LoadEmbedded()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(EmbeddedResourceName);
            if (stream is null) return new List<GuidedTour>();

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var tours = JsonSerializer.Deserialize<List<GuidedTour>>(json, JsonOptions)
                        ?? new List<GuidedTour>();
            LogResult("embedded", EmbeddedResourceName, tours, GuidedTourSaver.ComputeHash(json));
            return tours;
        }
        catch (System.Exception ex)
        {
            SquadDashTrace.Write(TraceCategory.Callouts,
                $"GuidedTourLoader.Load: embedded load failed, resource=\"{EmbeddedResourceName}\", error={ex.Message}");
            return new List<GuidedTour>();
        }
    }

    private static void LogResult(string source, string path, List<GuidedTour> tours, string hash) =>
        SquadDashTrace.Write(TraceCategory.Callouts,
            $"GuidedTourLoader.Load: source={source}, path=\"{path}\", parse=success, tours={tours.Count}, steps=[{string.Join(",", tours.Select(t => t.Steps.Count))}], hash={hash}");
}
