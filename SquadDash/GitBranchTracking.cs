namespace SquadDash;

internal sealed record GitBranchTracking(
    string Branch,
    string? Upstream,
    GitUpstreamCounts? Counts)
{
    internal string? TrackedBranch =>
        string.IsNullOrWhiteSpace(Upstream) ? null : Upstream.Trim();

    internal string ExpectedOriginUpstream => $"origin/{Branch}";

    internal bool HasMismatchedUpstream =>
        TrackedBranch is { } upstream
        && !string.Equals(upstream, ExpectedOriginUpstream, StringComparison.Ordinal);

    internal string IndicatorText => TrackedBranch ?? "no upstream";

    internal string TrackingDescription => TrackedBranch is { } upstream
        ? HasMismatchedUpstream
            ? $"Tracking: {upstream}\nWarning: Pushes should go to {ExpectedOriginUpstream}."
            : $"Tracking: {upstream}"
        : $"Tracking: none\nA push will target {ExpectedOriginUpstream} and set it as the upstream.";

    internal string PullLabel => Counts is { Behind: > 0 } counts && TrackedBranch is { } upstream
        ? $"Pull from {upstream} ({counts.Behind} behind)"
        : TrackedBranch is { } trackedBranch
            ? $"Pull from {trackedBranch}"
            : "Pull (no upstream)";

    internal string PushLabel => TrackedBranch switch
    {
        null => $"Push to {ExpectedOriginUpstream} and set tracking",
        _ when HasMismatchedUpstream => $"Push to {ExpectedOriginUpstream} and repair tracking",
        _ => $"Push to {ExpectedOriginUpstream}",
    };

    internal string PushArguments
    {
        get
        {
            var refspec = Quote($"refs/heads/{Branch}:refs/heads/{Branch}");
            return TrackedBranch is null || HasMismatchedUpstream
                ? $"push --set-upstream origin {refspec}"
                : $"push origin {refspec}";
        }
    }

    internal string RepairConfirmation =>
        $"The local branch '{Branch}' currently tracks '{TrackedBranch}'.\n\n"
        + $"Push it to '{ExpectedOriginUpstream}' and change its tracking branch to match?";

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
