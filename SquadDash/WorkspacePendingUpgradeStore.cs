using System;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash;

internal interface IWorkspacePendingUpgradeStore
{
    Task<WorkspacePendingUpgradeState> GetAsync(string workspaceDir);
    Task SavePendingAsync(string workspaceDir, string recordedCliVersion);
    Task SaveResolvedAsync(string workspaceDir);
}

internal sealed class WorkspacePendingUpgradeStore : IWorkspacePendingUpgradeStore
{
    private readonly ApplicationSettingsStore _applicationSettingsStore;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WorkspacePendingUpgradeStore(ApplicationSettingsStore applicationSettingsStore)
    {
        _applicationSettingsStore = applicationSettingsStore
            ?? throw new ArgumentNullException(nameof(applicationSettingsStore));
    }

    public async Task<WorkspacePendingUpgradeState> GetAsync(string workspaceDir)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return _applicationSettingsStore.GetWorkspacePendingUpgrade(workspaceDir).Normalize();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SavePendingAsync(string workspaceDir, string recordedCliVersion)
    {
        if (string.IsNullOrWhiteSpace(recordedCliVersion))
            throw new ArgumentException("Recorded CLI version cannot be empty.", nameof(recordedCliVersion));

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _applicationSettingsStore.SaveWorkspacePendingUpgrade(
                workspaceDir,
                new WorkspacePendingUpgradeState(
                    IsPending: true,
                    RecordedCliVersion: recordedCliVersion.Trim(),
                    ResolvedAt: null));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveResolvedAsync(string workspaceDir)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var current = _applicationSettingsStore.GetWorkspacePendingUpgrade(workspaceDir).Normalize();
            _applicationSettingsStore.SaveWorkspacePendingUpgrade(
                workspaceDir,
                current with {
                    IsPending = false,
                    ResolvedAt = DateTimeOffset.UtcNow
                });
        }
        finally
        {
            _gate.Release();
        }
    }
}

