namespace SquadDash;

internal sealed class HomeBranchActions
{
    public HomeBranchActions(HomeBranchResolution resolution, string featureBranch)
    {
        Branch = resolution.Branch ?? throw new ArgumentException("A resolved home branch is required.", nameof(resolution));
        FeatureBranch = featureBranch;
        Notice = resolution.Notice;
    }

    public string Branch { get; }
    public string FeatureBranch { get; }
    private string? Notice { get; }
    public string MergeLabel => $"Merge this branch into {Branch} \u2192";
    public string SwitchLabel => $"Switch to {Branch}";
    public string Confirmation => $"{(Notice is null ? "" : Notice + "\n\n")}Merge '{FeatureBranch}' into '{Branch}' with --ff-only, then push?\n\nThis cannot be undone.";
    private string VerifyPrompt => $"First verify that local branch '{Branch}' exists with {VerifyArguments}; if missing, stop and ask the user to choose 'Set as home branch'. Do not substitute another target.";
    public string QueuedMergePrompt => $"{VerifyPrompt} Merge current branch '{FeatureBranch}' into '{Branch}' with --ff-only and push to origin '{Branch}'.";
    public string QueuedSwitchPrompt => $"{VerifyPrompt} Switch git branch to '{Branch}'.";
    public string VerifyArguments => LocalBranchVerificationArguments(Branch);
    public string CheckoutArguments => $"checkout --no-guess {Quote(Branch)} --";
    public string MergeArguments => $"merge --ff-only -- {Quote($"refs/heads/{FeatureBranch}")}";
    public string PushArguments => $"push origin {Quote($"refs/heads/{Branch}:refs/heads/{Branch}")}";
    public string RestoreArguments => $"checkout --no-guess {Quote(FeatureBranch)} --";

    public static string LocalBranchVerificationArguments(string branch) =>
        $"show-ref --verify --quiet {Quote($"refs/heads/{branch}")}";

    // Git ref names cannot contain backslashes, but can contain double quotes.
    private static string Quote(string branch) => $"\"{branch.Replace("\"", "\\\"")}\"";
}

internal enum HomeBranchGitStep { VerifyTarget, Checkout, FastForwardMerge, Push }
internal enum HomeBranchFailureCategory { MissingOrStaleTarget, DirtyWorktree, Checkout, Divergence, Merge, Push }

internal static class HomeBranchFailure
{
    public static HomeBranchFailureCategory Classify(HomeBranchGitStep step, string error)
    {
        bool Contains(string value) => error.Contains(value, StringComparison.OrdinalIgnoreCase);
        if (step == HomeBranchGitStep.VerifyTarget)
            return HomeBranchFailureCategory.MissingOrStaleTarget;
        if (step == HomeBranchGitStep.Push)
            return HomeBranchFailureCategory.Push;
        if (step == HomeBranchGitStep.Checkout)
        {
            if (Contains("pathspec") || Contains("did not match") || Contains("invalid reference"))
                return HomeBranchFailureCategory.MissingOrStaleTarget;
            if (Contains("would be overwritten") || Contains("commit your changes or stash"))
                return HomeBranchFailureCategory.DirtyWorktree;
            return HomeBranchFailureCategory.Checkout;
        }
        if (Contains("not possible to fast-forward") || Contains("non-fast-forward"))
            return HomeBranchFailureCategory.Divergence;
        return HomeBranchFailureCategory.Merge;
    }

    public static string Describe(HomeBranchGitStep step, string error, HomeBranchActions actions)
    {
        var message = Classify(step, error) switch
        {
            HomeBranchFailureCategory.MissingOrStaleTarget =>
                $"Could not verify local home branch '{actions.Branch}'. It may be missing or the setting is stale. Choose 'Set as home branch' on a valid local branch.",
            HomeBranchFailureCategory.DirtyWorktree =>
                $"Checkout of '{actions.Branch}' was blocked by uncommitted changes. Commit or stash them before switching.",
            HomeBranchFailureCategory.Checkout => $"Checkout of '{actions.Branch}' failed.",
            HomeBranchFailureCategory.Divergence =>
                $"Cannot fast-forward '{actions.FeatureBranch}' into '{actions.Branch}': the branches have diverged. Rebase onto '{actions.Branch}' or merge manually.",
            HomeBranchFailureCategory.Merge => $"Merge of '{actions.FeatureBranch}' into '{actions.Branch}' failed.",
            HomeBranchFailureCategory.Push =>
                $"Push of '{actions.Branch}' to origin failed. The local merge succeeded; check the remote before retrying the push.",
            _ => throw new ArgumentOutOfRangeException(nameof(step))
        };
        return $"{message}\n\nFailing operation: {step}\n\nDetails: {error}";
    }
}
