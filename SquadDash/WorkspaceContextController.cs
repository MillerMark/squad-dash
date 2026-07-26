using System;

namespace SquadDash;

/// <summary>
/// Routes <see cref="IWorkspaceContext"/> calls to MainWindow state via injected delegates.
/// MainWindow holds one instance and passes it wherever an <see cref="IWorkspaceContext"/> is required.
/// </summary>
internal sealed class WorkspaceContextController : IWorkspaceContext
{
    private readonly Func<SessionWorkspace?>              _getCurrentWorkspace;
    private readonly Func<ApplicationSettingsSnapshot>    _getSettingsSnapshot;

    internal WorkspaceContextController(
        Func<SessionWorkspace?>           getCurrentWorkspace,
        Func<ApplicationSettingsSnapshot> getSettingsSnapshot)
    {
        _getCurrentWorkspace = getCurrentWorkspace ?? throw new ArgumentNullException(nameof(getCurrentWorkspace));
        _getSettingsSnapshot = getSettingsSnapshot ?? throw new ArgumentNullException(nameof(getSettingsSnapshot));
    }

    // ── IWorkspaceContext ─────────────────────────────────────────────────
    SessionWorkspace?              IWorkspaceContext.GetCurrentWorkspace() => _getCurrentWorkspace();
    ApplicationSettingsSnapshot    IWorkspaceContext.GetSettingsSnapshot() => _getSettingsSnapshot();
}
