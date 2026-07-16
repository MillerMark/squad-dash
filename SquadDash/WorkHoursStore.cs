using System.IO;

namespace SquadDash;

/// <summary>
/// Loads and saves <see cref="WorkHoursSettings"/> to disk.
/// Global path: <c>%LocalAppData%\SquadDash\work-hours.json</c>.
/// Workspace path (optional): <c>&lt;workspaceStateDirectory&gt;\work-hours.json</c>.
/// </summary>
internal sealed class WorkHoursStore
{
    private const string FileName = "work-hours.json";
    private static readonly string GlobalPath =
        Path.Combine(SquadDashPaths.AppData, FileName);

    /// <summary>
    /// Loads settings: workspace file first, then global, then <see cref="WorkHoursSettings.Default"/>.
    /// </summary>
    public WorkHoursSettings Load(string? workspaceStateDirectory)
    {
        if (workspaceStateDirectory is not null)
        {
            var workspacePath = Path.Combine(workspaceStateDirectory, FileName);
            if (File.Exists(workspacePath))
                return JsonFileStorage.ReadOrDefault(workspacePath, WorkHoursSettings.Default);
        }

        if (File.Exists(GlobalPath))
            return JsonFileStorage.ReadOrDefault(GlobalPath, WorkHoursSettings.Default);

        return WorkHoursSettings.Default;
    }

    /// <summary>
    /// Saves settings to the global path and, if provided, to the workspace path.
    /// </summary>
    public void Save(WorkHoursSettings settings, string? workspaceStateDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(GlobalPath)!);
        JsonFileStorage.AtomicWrite(GlobalPath, settings);

        if (workspaceStateDirectory is not null)
        {
            Directory.CreateDirectory(workspaceStateDirectory);
            JsonFileStorage.AtomicWrite(
                Path.Combine(workspaceStateDirectory, FileName),
                settings);
        }
    }
}
