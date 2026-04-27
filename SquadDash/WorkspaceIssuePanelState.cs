namespace SquadDash;

internal static class WorkspaceIssuePanelState {
    public static string? BuildDismissalKey(WorkspaceIssuePresentation? issue) {
        if (issue is null)
            return null;

        return string.Join(
            "|",
            issue.Title?.Trim() ?? string.Empty,
            issue.Message?.Trim() ?? string.Empty,
            issue.DetailText?.Trim() ?? string.Empty,
            issue.HelpButtonLabel?.Trim() ?? string.Empty,
            issue.HelpWindowTitle?.Trim() ?? string.Empty);
    }
}
