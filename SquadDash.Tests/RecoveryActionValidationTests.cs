using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class RecoveryActionValidationTests
{
    // ── ExtractSingleCandidateCommit ─────────────────────────────────────────

    [Test]
    public void ExtractSingleCandidateCommit_OneCommit_ReturnsSha()
    {
        const string log = "a1b2c3d Add feature X";
        var sha = RecoveryCommitValidator.ExtractSingleCandidateCommit(log);
        Assert.That(sha, Is.EqualTo("a1b2c3d"));
    }

    [Test]
    public void ExtractSingleCandidateCommit_ZeroCommits_ReturnsNull()
    {
        var sha = RecoveryCommitValidator.ExtractSingleCandidateCommit(string.Empty);
        Assert.That(sha, Is.Null);
    }

    [Test]
    public void ExtractSingleCandidateCommit_MultipleCommits_ThrowsInvalidOperation()
    {
        const string log = "a1b2c3d First commit\nb4c5d6e Second commit";
        Assert.Throws<InvalidOperationException>(() =>
            RecoveryCommitValidator.ExtractSingleCandidateCommit(log));
    }

    [Test]
    public void ExtractSingleCandidateCommit_WhitespaceOnly_ReturnsNull()
    {
        var sha = RecoveryCommitValidator.ExtractSingleCandidateCommit("   \n\t\n  ");
        Assert.That(sha, Is.Null);
    }

    // ── FindDownstreamCompletedDependents ────────────────────────────────────

    [Test]
    public void FindDownstreamCompletedDependents_NoDependents_ReturnsEmpty()
    {
        var tasks = new List<PlanTask>
        {
            MakeTask("T-02", dependsOn: ["T-99"], status: PlanTaskStatus.Complete),
        };
        var result = RecoveryCommitValidator.FindDownstreamCompletedDependents(tasks, "T-01");
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void FindDownstreamCompletedDependents_CompletedDependent_ReturnsIt()
    {
        var tasks = new List<PlanTask>
        {
            MakeTask("T-02", dependsOn: ["T-01"], status: PlanTaskStatus.Complete),
        };
        var result = RecoveryCommitValidator.FindDownstreamCompletedDependents(tasks, "T-01");
        Assert.That(result, Is.EqualTo(new[] { "T-02" }));
    }

    [Test]
    public void FindDownstreamCompletedDependents_PendingDependent_ReturnsEmpty()
    {
        var tasks = new List<PlanTask>
        {
            MakeTask("T-02", dependsOn: ["T-01"], status: PlanTaskStatus.Pending),
        };
        var result = RecoveryCommitValidator.FindDownstreamCompletedDependents(tasks, "T-01");
        Assert.That(result, Is.Empty);
    }

    // ── HasNonHostChanges ────────────────────────────────────────────────────

    [Test]
    public void HasNonHostChanges_AllHostOwned_ReturnsFalse()
    {
        var changedPaths  = new[] { ".squad/tasks.md", ".squad/plans/plan.json" };
        var hostOwnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".squad/tasks.md", ".squad/plans/plan.json" };
        var result = RecoveryCommitValidator.HasNonHostChanges(changedPaths, hostOwnedPaths);
        Assert.That(result, Is.False);
    }

    [Test]
    public void HasNonHostChanges_SomeNonHost_ReturnsTrue()
    {
        var changedPaths  = new[] { ".squad/tasks.md", "src/Feature.cs" };
        var hostOwnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".squad/tasks.md" };
        var result = RecoveryCommitValidator.HasNonHostChanges(changedPaths, hostOwnedPaths);
        Assert.That(result, Is.True);
    }

    [Test]
    public void HasNonHostChanges_EmptyChanges_ReturnsFalse()
    {
        var result = RecoveryCommitValidator.HasNonHostChanges(
            [],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".squad/tasks.md" });
        Assert.That(result, Is.False);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PlanTask MakeTask(
        string taskId,
        IReadOnlyList<string> dependsOn,
        string status) =>
        new(
            TaskId:      taskId,
            Title:       null,
            Description: "Test task",
            DependsOn:   dependsOn,
            Priority:    "medium",
            Status:      status);
}
