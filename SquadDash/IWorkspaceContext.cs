namespace SquadDash;

internal interface IWorkspaceContext {
    SessionWorkspace? GetCurrentWorkspace();
    ApplicationSettingsSnapshot GetSettingsSnapshot();
}
