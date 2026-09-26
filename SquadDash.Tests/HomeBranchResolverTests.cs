namespace SquadDash.Tests;

[TestFixture]
internal sealed class HomeBranchResolverTests
{
    [TestCase("master")]
    [TestCase("main")]
    public void OnlyConventionalBranch_ResolvesActualLocalName(string branch)
    {
        var result = HomeBranchResolver.Resolve(null, null, [branch, "feat/simulation-v1"]);
        Assert.Multiple(() =>
        {
            Assert.That(result.Branch, Is.EqualTo(branch));
            Assert.That(result.Source, Is.EqualTo(HomeBranchResolutionSource.Conventional));
            Assert.That(result.IsResolved, Is.True);
            Assert.That(result.StaleExplicitSetting, Is.False);
        });
    }

    [TestCase("master", false)]
    [TestCase("main", false)]
    [TestCase("master", true)]
    [TestCase("main", true)]
    [TestCase("trunk", true)]
    public void OriginHead_SelectsItsLocalBranch(string target, bool bothConventionalBranches)
    {
        string[] branches = bothConventionalBranches ? ["main", "master", target] : [target];
        var result = HomeBranchResolver.Resolve(null, target, branches);
        Assert.Multiple(() =>
        {
            Assert.That(result.Branch, Is.EqualTo(target));
            Assert.That(result.Source, Is.EqualTo(HomeBranchResolutionSource.OriginHead));
            Assert.That(result.IsResolved, Is.True);
        });
    }

    [TestCase(null)]
    [TestCase("remote-only")]
    public void BothConventionalBranches_WithoutUsableOriginHead_PreferMain(string? originHead)
    {
        var result = HomeBranchResolver.Resolve(null, originHead, ["master", "main"]);
        Assert.That(result.Branch, Is.EqualTo("main"));
    }

    [Test]
    public void ValidExplicitBranch_OverridesDiscovery()
    {
        var result = HomeBranchResolver.Resolve(" release/stable ", "master", ["master", "release/stable"]);
        Assert.Multiple(() =>
        {
            Assert.That(result.Branch, Is.EqualTo("release/stable"));
            Assert.That(result.Source, Is.EqualTo(HomeBranchResolutionSource.ExplicitSetting));
            Assert.That(result.StaleExplicitSetting, Is.False);
        });
    }

    [TestCase("master")]
    [TestCase(null)]
    public void StaleExplicitBranch_FlagsSettingAndUsesExistingFallback(string? originHead)
    {
        var result = HomeBranchResolver.Resolve("deleted", originHead, ["master"]);
        Assert.Multiple(() =>
        {
            Assert.That(result.Branch, Is.EqualTo("master").And.Not.EqualTo("deleted"));
            Assert.That(result.IsResolved, Is.True);
            Assert.That(result.StaleExplicitSetting, Is.True);
            Assert.That(result.Notice, Does.Contain("no longer exists").And.Contain("'master'"));
            Assert.That(new HomeBranchActions(result, "feature").CheckoutArguments, Does.Not.Contain("deleted"));
        });
    }

    [TestCase(null, null, false)]
    [TestCase("deleted", null, true)]
    [TestCase(null, "remote-only", false)]
    [TestCase(" ", null, false)]
    public void NoLocalDefault_DoesNotInventTarget(string? configured, string? originHead, bool stale)
    {
        var result = HomeBranchResolver.Resolve(configured, originHead, ["feature"]);
        Assert.Multiple(() =>
        {
            Assert.That(result.Branch, Is.Null);
            Assert.That(result.IsResolved, Is.False);
            Assert.That(result.Source, Is.EqualTo(HomeBranchResolutionSource.None));
            Assert.That(result.StaleExplicitSetting, Is.EqualTo(stale));
            Assert.That(result.Notice, Does.Contain("Set as home branch"));
            Assert.That(() => new HomeBranchActions(result, "feature"), Throws.ArgumentException);
        });
    }

    [Test]
    public void BranchNames_AreCaseSensitiveAndNotAliases()
    {
        var result = HomeBranchResolver.Resolve("MASTER", null, ["master", "main"]);
        Assert.Multiple(() =>
        {
            Assert.That(result.StaleExplicitSetting, Is.True);
            Assert.That(result.IsHomeBranch("main"), Is.True);
            Assert.That(result.IsHomeBranch("master"), Is.False);
            Assert.That(result.IsHomeBranch("MAIN"), Is.False);
            Assert.That(HomeBranchResolver.Resolve(null, null, ["MAIN"]).IsResolved, Is.False);
        });
    }

    [TestCase("master")]
    [TestCase("main")]
    [TestCase("release/stable")]
    public void OneResolution_DrivesEveryActionAndPresentation(string target)
    {
        const string feature = "feat/simulation-v1";
        var resolution = HomeBranchResolver.Resolve(null, target, [target, feature]);
        var actions = new HomeBranchActions(resolution, feature);
        Assert.Multiple(() =>
        {
            Assert.That(actions.Branch, Is.EqualTo(resolution.Branch).And.EqualTo(target));
            Assert.That(actions.MergeLabel, Is.EqualTo($"Merge this branch into {target} \u2192"));
            Assert.That(actions.SwitchLabel, Is.EqualTo($"Switch to {target}"));
            Assert.That(actions.Confirmation, Is.EqualTo($"Merge '{feature}' into '{target}' with --ff-only, then push?\n\nThis cannot be undone."));
            Assert.That(actions.QueuedMergePrompt, Does.Contain($"Merge current branch '{feature}' into '{target}'")
                .And.Contain($"push to origin '{target}'").And.Contain(actions.VerifyArguments));
            Assert.That(actions.QueuedSwitchPrompt, Does.Contain($"Switch git branch to '{target}'")
                .And.Contain(actions.VerifyArguments).And.Contain("if missing, stop"));
            Assert.That(actions.CheckoutArguments, Is.EqualTo($"checkout --no-guess \"{target}\" --"));
            Assert.That(actions.MergeArguments, Is.EqualTo($"merge --ff-only -- \"refs/heads/{feature}\""));
            Assert.That(actions.PushArguments, Is.EqualTo($"push origin \"refs/heads/{target}:refs/heads/{target}\""));
            Assert.That(actions.RestoreArguments, Is.EqualTo($"checkout --no-guess \"{feature}\" --"));
        });
    }

    [Test]
    public void StaleFallback_ConfirmationExplainsTheSetting()
    {
        var resolution = HomeBranchResolver.Resolve("deleted", null, ["master"]);
        Assert.That(new HomeBranchActions(resolution, "feature").Confirmation,
            Does.Contain(resolution.Notice!).And.Contain("Merge 'feature' into 'master'"));
    }

    [Test]
    public void GitArguments_EscapeQuotesInActualBranchNames()
    {
        var resolution = HomeBranchResolver.Resolve("release\"stable", null, ["release\"stable"]);
        var actions = new HomeBranchActions(resolution, "feature");
        Assert.Multiple(() =>
        {
            Assert.That(actions.CheckoutArguments, Is.EqualTo("checkout --no-guess \"release\\\"stable\" --"));
            Assert.That(actions.VerifyArguments, Is.EqualTo("show-ref --verify --quiet \"refs/heads/release\\\"stable\""));
        });
    }

    [TestCase(HomeBranchGitStep.VerifyTarget, "", HomeBranchFailureCategory.MissingOrStaleTarget)]
    [TestCase(HomeBranchGitStep.Checkout, "error: pathspec 'main' did not match any file(s) known to git", HomeBranchFailureCategory.MissingOrStaleTarget)]
    [TestCase(HomeBranchGitStep.Checkout, "fatal: invalid reference: main", HomeBranchFailureCategory.MissingOrStaleTarget)]
    [TestCase(HomeBranchGitStep.Checkout, "Your local changes would be overwritten by checkout", HomeBranchFailureCategory.DirtyWorktree)]
    [TestCase(HomeBranchGitStep.Checkout, "Please commit your changes or stash them before you switch branches.", HomeBranchFailureCategory.DirtyWorktree)]
    [TestCase(HomeBranchGitStep.Checkout, "could not lock index", HomeBranchFailureCategory.Checkout)]
    [TestCase(HomeBranchGitStep.Checkout, "non-fast-forward", HomeBranchFailureCategory.Checkout)]
    [TestCase(HomeBranchGitStep.FastForwardMerge, "fatal: Not possible to fast-forward, aborting.", HomeBranchFailureCategory.Divergence)]
    [TestCase(HomeBranchGitStep.FastForwardMerge, "non-fast-forward", HomeBranchFailureCategory.Divergence)]
    [TestCase(HomeBranchGitStep.FastForwardMerge, "could not write index", HomeBranchFailureCategory.Merge)]
    [TestCase(HomeBranchGitStep.Push, "rejected (non-fast-forward)", HomeBranchFailureCategory.Push)]
    [TestCase(HomeBranchGitStep.Push, "permission denied", HomeBranchFailureCategory.Push)]
    public void FailureClassification_UsesTheActualFailingStep(
        HomeBranchGitStep step, string error, HomeBranchFailureCategory expected)
    {
        var actions = new HomeBranchActions(HomeBranchResolver.Resolve(null, null, ["master"]), "feature");
        Assert.Multiple(() =>
        {
            Assert.That(HomeBranchFailure.Classify(step, error), Is.EqualTo(expected));
            Assert.That(HomeBranchFailure.Describe(step, error, actions), Does.Contain("master").And.Contain(step.ToString()));
            if (expected != HomeBranchFailureCategory.Divergence)
                Assert.That(HomeBranchFailure.Describe(step, error, actions), Does.Not.Contain("diverged").And.Not.Contain("Rebase"));
        });
    }

    [TestCase("master", "master", true)]
    [TestCase("master", null, false)]
    [TestCase("master", "main", false)]
    [TestCase(null, null, false)]
    public void TargetRevalidation_NeverSilentlyRetargets(string? expectedBranch, string? currentBranch, bool valid)
    {
        var expected = HomeBranchResolver.Resolve(null, expectedBranch, expectedBranch is null ? [] : [expectedBranch]);
        var current = HomeBranchResolver.Resolve(null, currentBranch, currentBranch is null ? [] : [currentBranch]);
        if (valid)
            Assert.That(() => HomeBranchRepositoryReader.VerifyUnchangedTarget(expected, current), Throws.Nothing);
        else
            Assert.That(() => HomeBranchRepositoryReader.VerifyUnchangedTarget(expected, current),
                Throws.InvalidOperationException.With.Message.Contains("Set as home branch"));
    }

    [Test]
    public void NewlyStaleSetting_RequiresReopeningMenuEvenWhenFallbackIsUnchanged()
    {
        var expected = HomeBranchResolver.Resolve(null, null, ["master"]);
        var current = HomeBranchResolver.Resolve("deleted", null, ["master"]);
        Assert.That(() => HomeBranchRepositoryReader.VerifyUnchangedTarget(expected, current), Throws.InvalidOperationException);
    }

    [TestCase("refs/heads/master \nrefs/heads/feature \nrefs/remotes/origin/HEAD refs/remotes/origin/master\n", "master")]
    [TestCase("refs/heads/main \nrefs/heads/master \nrefs/remotes/origin/HEAD refs/remotes/origin/master\n", "master")]
    [TestCase("refs/heads/release/stable \r\nrefs/remotes/origin/HEAD refs/remotes/origin/release/stable\r\n", "release/stable")]
    [TestCase("refs/heads/master \n", "master")]
    [TestCase("refs/heads/feature \nrefs/remotes/origin/HEAD refs/remotes/origin/remote-only\n", null)]
    [TestCase("", null)]
    public async Task RepositoryReader_UsesOnlyLocalPlumbing(string output, string? expected)
    {
        var commands = new List<string>();
        var result = await HomeBranchRepositoryReader.ResolveAsync("workspace", null, (workspace, command) =>
        {
            Assert.That(workspace, Is.EqualTo("workspace"));
            commands.Add(command);
            return Task.FromResult(output);
        });
        Assert.Multiple(() =>
        {
            Assert.That(commands, Is.EqualTo(new[] {
                "for-each-ref --format=\"%(refname) %(symref)\" refs/heads refs/remotes/origin/HEAD" }));
            Assert.That(result.Branch, Is.EqualTo(expected));
        });
    }

    [Test]
    public void RepositoryReader_PropagatesMetadataErrors()
    {
        Assert.That(async () => await HomeBranchRepositoryReader.ResolveAsync("workspace", null,
                (_, _) => Task.FromException<string>(new InvalidOperationException("not a git repository"))),
            Throws.InvalidOperationException.With.Message.EqualTo("not a git repository"));
    }
}
