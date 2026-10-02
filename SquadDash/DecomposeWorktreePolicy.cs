using System.IO;

namespace SquadDash;

internal static class DecomposeWorktreePolicy
{
    private static readonly string[] HostOwnedRuntimeDirectories =
    [
        "diagnostics/",
        "logs/",
        "plans/",
        "tmp/",
        "archive/agent-artifacts/",
        "instructions/",
    ];

    private static readonly string[] HostOwnedRuntimeFiles =
    [
        "loop-executing-plan.md",
        "tasks.md",
        "code-health-state.json",
        "code-health-state.json.backup",
        "code-health-state.json.tmp",
        "codehealth-stub-state.json",
        "codehealth-stub-state.json.backup",
        "codehealth-stub-state.json.tmp",
    ];

    internal static string? GetRepositoryRelativePath(
        string? repositoryRootOutput,
        string absolutePath)
    {
        var repositoryRoot = repositoryRootOutput?.Trim();
        if (string.IsNullOrWhiteSpace(repositoryRoot) || string.IsNullOrWhiteSpace(absolutePath))
            return null;

        var relative = Path.GetRelativePath(repositoryRoot, absolutePath).Replace('\\', '/');
        return relative.Equals("..", StringComparison.Ordinal) ||
               relative.StartsWith("../", StringComparison.Ordinal) ||
               Path.IsPathRooted(relative)
            ? null
            : relative;
    }

    internal static bool HasOnlyAllowedChanges(
        string? porcelainStatus,
        IReadOnlyCollection<string> allowedRepositoryRelativePaths,
        out IReadOnlyList<string> disallowedPaths)
    {
        var allowed = allowedRepositoryRelativePaths
            .Select(Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var disallowed = new List<string>();
        foreach (var rawLine in (porcelainStatus ?? string.Empty)
                     .Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawLine.Length < 4) continue;
            var path = rawLine[3..].Trim().Trim('"');
            var renameSeparator = path.LastIndexOf(" -> ", StringComparison.Ordinal);
            if (renameSeparator >= 0) path = path[(renameSeparator + 4)..];
            path = Normalize(path);
            if (!allowed.Contains(path) && !IsHostOwnedRuntimePath(path))
                disallowed.Add(path);
        }
        disallowedPaths = disallowed;
        return disallowed.Count == 0;
    }

    /// <summary>
    /// Filters <paramref name="candidatePaths"/> by checking whether each has genuine
    /// content changes vs. the HEAD commit. Paths whose working-tree content matches
    /// HEAD (stat-cache or timestamp noise) are removed from the returned list.
    /// Staged changes and untracked files are always retained as genuinely dirty.
    /// </summary>
    /// <param name="candidatePaths">Paths that failed the allowed-list check.</param>
    /// <param name="runGit">Delegate that runs a git command and returns its stdout.</param>
    /// <returns>Paths that have genuine content changes (not metadata-only).</returns>
    internal static async Task<IReadOnlyList<string>> FilterMetadataOnlyAsync(
        IReadOnlyList<string> candidatePaths,
        Func<string, Task<string>> runGit)
    {
        if (candidatePaths.Count == 0) return [];

        var genuine = new List<string>();
        foreach (var path in candidatePaths)
        {
            var diffOutput = await runGit($"diff-index HEAD -- \"{path}\"");
            if (!string.IsNullOrWhiteSpace(diffOutput))
                genuine.Add(path);
        }
        return genuine;
    }

    internal static bool MatchesConfirmedPaths(
        IReadOnlyCollection<string> currentPaths,
        IReadOnlyCollection<string> confirmedPaths)
    {
        var current = currentPaths.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var confirmed = confirmedPaths.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return current.SetEquals(confirmed);
    }

    /// <summary>
    /// Returns whether a dirty path is generated and maintained by SquadDash itself. These
    /// files are execution state, diagnostics, projections, or agent transport artifacts;
    /// they are not user source changes and must never block plan continuation or be described
    /// as uncommitted task work.
    /// </summary>
    internal static bool IsHostOwnedRuntimePath(string path)
    {
        var normalized = Normalize(path);
        const string squadPrefix = "squad/";
        if (!normalized.StartsWith(squadPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var relativePath = normalized[squadPrefix.Length..];
        return HostOwnedRuntimeDirectories.Any(directory =>
                   relativePath.StartsWith(directory, StringComparison.OrdinalIgnoreCase)) ||
               HostOwnedRuntimeFiles.Any(file =>
                   relativePath.Equals(file, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns only the generated runtime paths beneath a repository-relative Squad folder.
    /// User-authored Squad files, such as team.md and decisions.md, are deliberately absent.
    /// </summary>
    internal static IReadOnlyList<string> GetHostOwnedRuntimePaths(string squadRootRelativePath)
    {
        var root = squadRootRelativePath.Replace('\\', '/').Trim().TrimEnd('/');
        if (root.Length == 0) return [];

        return HostOwnedRuntimeDirectories.Select(directory => $"{root}/{directory}")
            .Concat(HostOwnedRuntimeFiles.Select(file => $"{root}/{file}"))
            .ToArray();
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('.', '/');
}
