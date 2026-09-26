namespace SquadDash;

internal static class HomeBranchRepositoryReader
{
    public static async Task<HomeBranchResolution> ResolveAsync(
        string workspace, string? configuredBranch, Func<string, string, Task<string>> runGit)
    {
        // symref reads the local origin/HEAD target without fetching or assuming the remote's default.
        var refs = await runGit(workspace,
            "for-each-ref --format=\"%(refname) %(symref)\" refs/heads refs/remotes/origin/HEAD");
        var localBranches = new List<string>();
        string? originHead = null;
        const string localPrefix = "refs/heads/";
        const string originHeadPrefix = "refs/remotes/origin/HEAD refs/remotes/origin/";
        foreach (var line in refs.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith(localPrefix, StringComparison.Ordinal))
                localBranches.Add(line[localPrefix.Length..]);
            else if (line.StartsWith(originHeadPrefix, StringComparison.Ordinal))
                originHead = line[originHeadPrefix.Length..];
        }
        return HomeBranchResolver.Resolve(configuredBranch, originHead, localBranches);
    }

    public static void VerifyUnchangedTarget(HomeBranchResolution expected, HomeBranchResolution current)
    {
        if (!expected.IsResolved || !current.IsResolved
            || (current.StaleExplicitSetting && !expected.StaleExplicitSetting)
            || !string.Equals(expected.Branch, current.Branch, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Home branch '{expected.Branch}' is missing or the home-branch selection changed. Reopen the branch menu or choose 'Set as home branch'.");
    }
}
