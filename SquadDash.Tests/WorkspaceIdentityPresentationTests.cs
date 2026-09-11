namespace SquadDash.Tests;

[TestFixture]
internal sealed class WorkspaceIdentityPresentationTests
{
    [Test]
    public void Resolve_NestedWorkspace_ShowsFullWorkspaceRepositoryAndBranch()
    {
        using var workspace = new TestWorkspace();
        var repositoryRoot = Path.Combine(workspace.RootPath, "repo");
        var nestedWorkspace = Path.Combine(repositoryRoot, "Package");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, ".git"));
        Directory.CreateDirectory(nestedWorkspace);

        var presentation = WorkspaceIdentityPresentation.Resolve(
            nestedWorkspace,
            "feature/blazor-aigen-support");
        var tooltip = presentation.BuildTooltip(includeRevealHint: true);

        Assert.Multiple(() => {
            Assert.That(presentation.RepositoryRoot, Is.EqualTo(repositoryRoot));
            Assert.That(tooltip, Does.Contain($"Workspace: {nestedWorkspace}"));
            Assert.That(tooltip, Does.Contain($"Repository root: {repositoryRoot}"));
            Assert.That(tooltip, Does.Contain("Branch: feature/blazor-aigen-support"));
            Assert.That(tooltip, Does.Contain("Click to reveal"));
        });
    }

    [Test]
    public void Resolve_LinkedWorktreeMarker_TreatsMarkerFolderAsRepositoryRoot()
    {
        using var workspace = new TestWorkspace();
        var worktree = Path.Combine(workspace.RootPath, "linked-worktree");
        var gitDirectory = Path.Combine(workspace.RootPath, "repo", ".git", "worktrees", "linked-worktree");
        Directory.CreateDirectory(worktree);
        Directory.CreateDirectory(gitDirectory);
        File.WriteAllText(Path.Combine(gitDirectory, "HEAD"), "ref: refs/heads/codex/blazor-aigen-master");
        File.WriteAllText(Path.Combine(worktree, ".git"), $"gitdir: {gitDirectory}");

        var presentation = WorkspaceIdentityPresentation.Resolve(worktree, branch: null);

        Assert.Multiple(() => {
            Assert.That(presentation.RepositoryRoot, Is.EqualTo(worktree));
            Assert.That(presentation.Branch, Is.EqualTo("codex/blazor-aigen-master"));
            Assert.That(presentation.GitDirectory, Is.EqualTo(gitDirectory));
        });
    }

    [Test]
    public void Resolve_LinkedWorktree_UsesCommonRepositoryConfig()
    {
        using var workspace = new TestWorkspace();
        var worktree = Path.Combine(workspace.RootPath, "linked-worktree");
        var commonGitDirectory = Path.Combine(workspace.RootPath, "repo", ".git");
        var gitDirectory = Path.Combine(commonGitDirectory, "worktrees", "linked-worktree");
        Directory.CreateDirectory(worktree);
        Directory.CreateDirectory(gitDirectory);
        File.WriteAllText(Path.Combine(gitDirectory, "HEAD"), "ref: refs/heads/feature/worktree");
        File.WriteAllText(Path.Combine(gitDirectory, "commondir"), "../..");
        File.WriteAllText(Path.Combine(commonGitDirectory, "config"), "[remote \"origin\"]");
        File.WriteAllText(Path.Combine(worktree, ".git"), $"gitdir: {gitDirectory}");

        var presentation = WorkspaceIdentityPresentation.Resolve(worktree, branch: null);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.Branch, Is.EqualTo("feature/worktree"));
            Assert.That(presentation.GitDirectory, Is.EqualTo(gitDirectory));
            Assert.That(presentation.GitConfigPath, Is.EqualTo(Path.Combine(commonGitDirectory, "config")));
        });
    }
}
