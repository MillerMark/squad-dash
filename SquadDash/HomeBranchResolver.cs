namespace SquadDash;

internal enum HomeBranchResolutionSource { None, ExplicitSetting, OriginHead, Conventional }

internal sealed record HomeBranchResolution(
    string? Branch, HomeBranchResolutionSource Source, bool StaleExplicitSetting)
{
    public bool IsResolved => Branch is not null;

    public bool IsHomeBranch(string branch) =>
        IsResolved && string.Equals(Branch, branch, StringComparison.Ordinal);

    public string? Notice => StaleExplicitSetting
        ? $"The configured home branch no longer exists locally. {(IsResolved ? $"Using '{Branch}' instead." : "No home branch was resolved.")} Choose 'Set as home branch' to update the setting."
        : !IsResolved ? "No local home branch was resolved. Choose 'Set as home branch' on a local branch." : null;
}

internal static class HomeBranchResolver
{
    public static HomeBranchResolution Resolve(
        string? configuredBranch, string? originHeadBranch, IEnumerable<string> localBranches)
    {
        var branches = new HashSet<string>(localBranches, StringComparer.Ordinal);
        var configured = string.IsNullOrWhiteSpace(configuredBranch) ? null : configuredBranch.Trim();
        if (configured is not null && branches.Contains(configured))
            return new(configured, HomeBranchResolutionSource.ExplicitSetting, false);

        bool stale = configured is not null;
        if (originHeadBranch is not null && branches.Contains(originHeadBranch))
            return new(originHeadBranch, HomeBranchResolutionSource.OriginHead, stale);

        // When both conventional names exist without origin/HEAD, keep main for backward compatibility.
        if (branches.Contains("main"))
            return new("main", HomeBranchResolutionSource.Conventional, stale);
        if (branches.Contains("master"))
            return new("master", HomeBranchResolutionSource.Conventional, stale);
        return new(null, HomeBranchResolutionSource.None, stale);
    }
}
