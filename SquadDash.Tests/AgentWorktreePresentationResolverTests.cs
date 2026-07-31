using System;
using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class AgentWorktreePresentationResolverTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "SquadDashWorktreePresentation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void Resolve_ActiveLinkedWorktree_ReturnsFolderNameAndRoot()
    {
        var main = Path.Combine(_root, "main");
        var worktree = Path.Combine(_root, "feature-worktree");
        var nested = Path.Combine(worktree, "src", "feature");
        Directory.CreateDirectory(Path.Combine(main, ".git", "worktrees", "feature-worktree"));
        Directory.CreateDirectory(nested);
        File.WriteAllText(
            Path.Combine(worktree, ".git"),
            $"gitdir: {Path.Combine(main, ".git", "worktrees", "feature-worktree")}");

        var result = AgentWorktreePresentationResolver.Resolve(nested, main, isAgentActive: true);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("feature-worktree"));
        Assert.That(result.RootPath, Is.EqualTo(worktree));
    }

    [Test]
    public void Resolve_InactiveAgent_ReturnsNull()
    {
        var worktree = CreateLinkedWorktree("inactive-worktree");

        Assert.That(
            AgentWorktreePresentationResolver.Resolve(worktree, Path.Combine(_root, "main"), isAgentActive: false),
            Is.Null);
    }

    [Test]
    public void Resolve_MainCheckout_ReturnsNull()
    {
        var main = Path.Combine(_root, "main");
        Directory.CreateDirectory(Path.Combine(main, ".git"));

        Assert.That(AgentWorktreePresentationResolver.Resolve(main, null, isAgentActive: true), Is.Null);
    }

    [Test]
    public void Resolve_ActiveWorkspaceIsSameWorktree_ReturnsNull()
    {
        var worktree = CreateLinkedWorktree("current-worktree");

        Assert.That(AgentWorktreePresentationResolver.Resolve(worktree, worktree, isAgentActive: true), Is.Null);
    }

    [Test]
    public void Resolve_MissingDirectory_ReturnsNull()
    {
        Assert.That(
            AgentWorktreePresentationResolver.Resolve(Path.Combine(_root, "missing"), null, isAgentActive: true),
            Is.Null);
    }

    private string CreateLinkedWorktree(string name)
    {
        var main = Path.Combine(_root, "main");
        var worktree = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(main, ".git", "worktrees", name));
        Directory.CreateDirectory(worktree);
        File.WriteAllText(
            Path.Combine(worktree, ".git"),
            $"gitdir: {Path.Combine(main, ".git", "worktrees", name)}");
        return worktree;
    }
}
