namespace SquadDash;

internal sealed record PlanTaskFileChange(string Path, FileChangeStatus Status);

internal sealed record PlanTaskCommitRange(string BaselineCommit, string TerminalCommit);

/// <summary>Resolves immutable task-range file evidence from Git commits, never the live worktree.</summary>
internal static class PlanTaskFileDiffResolver
{
    internal static PlanTaskCommitRange? ResolveRange(PlanTask task)
    {
        var terminal = task.Commit ?? task.Handoff?.Commit;
        if (string.IsNullOrWhiteSpace(terminal)) return null;

        var baseline = task.Handoff?.BaselineCommit;
        if (string.IsNullOrWhiteSpace(baseline))
        {
            var firstCommit = task.Commits?.FirstOrDefault()?.Commit ?? terminal;
            baseline = $"{firstCommit}^";
        }
        return new PlanTaskCommitRange(baseline, terminal);
    }

    internal static async Task<IReadOnlyList<PlanTaskFileChange>> ResolveAsync(
        string workspacePath,
        PlanTask task)
    {
        var range = ResolveRange(task);
        if (range is null) return [];
        var output = await CommitViewerGit.RunAsync(
            workspacePath,
            "diff",
            "--name-status",
            "--find-renames",
            range.BaselineCommit,
            range.TerminalCommit);
        var resolved = ParseNameStatus(output);
        var recordedPaths = task.Handoff?.ChangedFiles ?? [];
        if (recordedPaths.Count == 0) return resolved;
        var recordedOrder = recordedPaths
            .Select((path, index) => (path, index))
            .ToDictionary(item => item.path, item => item.index, StringComparer.OrdinalIgnoreCase);
        return resolved
            .OrderBy(file => recordedOrder.TryGetValue(file.Path, out var index) ? index : int.MaxValue)
            .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static IReadOnlyList<PlanTaskFileChange> ParseNameStatus(string output)
    {
        var result = new List<PlanTaskFileChange>();
        foreach (var line in output.Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            var code = parts[0];
            var path = code.StartsWith('R') || code.StartsWith('C')
                ? parts.Length >= 3 ? parts[2] : parts[1]
                : parts[1];
            var status = code[0] switch
            {
                'A' => FileChangeStatus.Added,
                'D' => FileChangeStatus.Deleted,
                'R' => FileChangeStatus.Renamed,
                'C' => FileChangeStatus.Copied,
                'M' or 'T' => FileChangeStatus.Modified,
                _ => FileChangeStatus.Unknown,
            };
            result.Add(new PlanTaskFileChange(path, status));
        }
        return result;
    }
}
