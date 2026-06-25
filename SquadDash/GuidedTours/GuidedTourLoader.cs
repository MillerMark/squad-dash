using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace SquadDash.GuidedTours;

/// <summary>
/// Loads guided tours from the workspace override (.squad/guided-tours.json) or,
/// if not found, from the embedded application resource.
/// </summary>
internal static class GuidedTourLoader
{
    private const string EmbeddedResourceName = "SquadDash.Assets.guided-tours.json";
    private const string WorkspaceFileName     = "guided-tours.json";
    private const string WorkspaceDirName      = ".squad";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
    };

    /// <summary>
    /// Loads the list of guided tours.
    /// Checks <c>.squad/guided-tours.json</c> inside <paramref name="workspaceFolderPath"/> first,
    /// then falls back to the embedded resource.
    /// </summary>
    internal static List<GuidedTour> Load(string? workspaceFolderPath)
    {
        if (!string.IsNullOrWhiteSpace(workspaceFolderPath))
        {
            var workspacePath = Path.Combine(workspaceFolderPath, WorkspaceDirName, WorkspaceFileName);
            if (File.Exists(workspacePath))
            {
                try
                {
                    var json = File.ReadAllText(workspacePath);
                    var tours = JsonSerializer.Deserialize<List<GuidedTour>>(json, JsonOptions);
                    if (tours is { Count: > 0 })
                        return tours;
                }
                catch { /* fall through to embedded */ }
            }
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
            return JsonSerializer.Deserialize<List<GuidedTour>>(json, JsonOptions)
                   ?? new List<GuidedTour>();
        }
        catch
        {
            return new List<GuidedTour>();
        }
    }
}
