using System;
using System.IO;
using System.Threading.Tasks;

namespace SquadDash;

internal sealed class SquadCliSelectionStore : ISquadCliSelectionStore
{
    private readonly ApplicationSettingsStore _applicationSettingsStore;

    public SquadCliSelectionStore(ApplicationSettingsStore applicationSettingsStore)
    {
        _applicationSettingsStore = applicationSettingsStore
            ?? throw new ArgumentNullException(nameof(applicationSettingsStore));
    }

    public CliSelectionMode GetSelectionMode(string workspaceDir)
    {
        var normalizedWorkspace = NormalizeWorkspace(workspaceDir);
        var snapshot = _applicationSettingsStore.Load();
        return snapshot.CliSelectionByWorkspace.TryGetValue(normalizedWorkspace, out var selection)
            ? selection.Mode
            : CliSelectionMode.Auto;
    }

    public string? GetCustomPath(string workspaceDir)
    {
        var normalizedWorkspace = NormalizeWorkspace(workspaceDir);
        var snapshot = _applicationSettingsStore.Load();
        return snapshot.CliSelectionByWorkspace.TryGetValue(normalizedWorkspace, out var selection)
            ? selection.CustomPath
            : null;
    }

    public Task SaveSelectionAsync(string workspaceDir, CliSelectionMode mode, string? customPath)
    {
        _applicationSettingsStore.SaveCliWorkspaceSelection(workspaceDir, mode, customPath);
        return Task.CompletedTask;
    }

    private static string NormalizeWorkspace(string workspaceDir)
    {
        return Path.GetFullPath(workspaceDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
