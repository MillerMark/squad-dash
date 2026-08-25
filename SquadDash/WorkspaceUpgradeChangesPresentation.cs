using System.Text;

namespace SquadDash;

internal static class WorkspaceUpgradeChangesPresentation
{
    internal static string Build(
        WorkspaceUpgradeDryRunResult result,
        string? gitStatus,
        string? gitDiff,
        bool simulated)
    {
        var text = new StringBuilder();
        text.AppendLine(simulated
            ? "SIMULATED WORKSPACE-UPGRADE FILES"
            : "WORKSPACE-UPGRADE FILES");
        text.AppendLine(simulated
            ? "These are representative filenames from the safe simulation. No workspace files were changed."
            : "These filenames were reported by the workspace-upgrade operation.");
        text.AppendLine();
        AppendCategory(text, "Safe additions or refreshes", result.SafeAdditionsOrRefreshes);
        AppendCategory(text, "Tracked local changes at risk", result.TrackedLocalChangesAtRisk);
        AppendCategory(text, "Generated files at risk", result.GeneratedFilesAtRisk);

        if (simulated)
            return text.ToString().TrimEnd();

        text.AppendLine("GIT STATUS");
        text.AppendLine(string.IsNullOrWhiteSpace(gitStatus) ? "(clean)" : gitStatus.TrimEnd());
        text.AppendLine();
        text.AppendLine("GIT DIFF");
        text.AppendLine(string.IsNullOrWhiteSpace(gitDiff)
            ? "(No tracked diff. Untracked files, if any, are listed in Git status.)"
            : gitDiff.TrimEnd());
        return text.ToString().TrimEnd();
    }

    private static void AppendCategory(
        StringBuilder text,
        string label,
        IReadOnlyList<string> files)
    {
        text.AppendLine(label.ToUpperInvariant());
        if (files.Count == 0)
        {
            text.AppendLine("(none)");
        }
        else
        {
            foreach (var file in files)
                text.AppendLine(file);
        }

        text.AppendLine();
    }
}
