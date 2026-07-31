using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class RecoveryActionValidationTests
{
    [Test]
    public void ParseCommitRange_ParsesOldestToNewestRows()
    {
        var entries = RecoveryCommitValidator.ParseCommitRange(
            "4bf1c1c000000000\tInitial matrix\n" +
            "b6d69d9000000000\tComplete matrix\n");

        Assert.Multiple(() =>
        {
            Assert.That(entries.Select(entry => entry.Commit),
                Is.EqualTo(new[] { "4bf1c1c000000000", "b6d69d9000000000" }));
            Assert.That(entries[1].Subject, Is.EqualTo("Complete matrix"));
        });
    }

    [TestCase("not-a-row")]
    [TestCase("nothex00\tSubject")]
    public void ParseCommitRange_MalformedRow_Throws(string log)
    {
        Assert.Throws<InvalidOperationException>(() =>
            RecoveryCommitValidator.ParseCommitRange(log));
    }

    [Test]
    public void FindNewestRecordedCommit_ReturnsNewestReachableCompletedTaskCommit()
    {
        var resolved = RecoveryCommitValidator.FindNewestRecordedCommit(
            ["ccccccc000000000", "bbbbbbb000000000", "aaaaaaa000000000"],
            ["aaaaaaa", "bbbbbbb"]);

        Assert.That(resolved, Is.EqualTo("bbbbbbb000000000"));
    }

    [Test]
    public void FindNewestRecordedCommit_IgnoresUnrecordedHistory()
    {
        var resolved = RecoveryCommitValidator.FindNewestRecordedCommit(
            ["ccccccc000000000", "bbbbbbb000000000"],
            ["aaaaaaa"]);

        Assert.That(resolved, Is.Null);
    }

    [Test]
    public void FindNewestRecordedCommit_DoesNotResolveAmbiguousAbbreviation()
    {
        var resolved = RecoveryCommitValidator.FindNewestRecordedCommit(
            ["abcdefa000000000", "abcdefb000000000"],
            ["abcdef"]);

        Assert.That(resolved, Is.Null);
    }

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

    // ── ContainsOnlyNonHostChanges ───────────────────────────────────────────

    [Test]
    public void ContainsOnlyNonHostChanges_AllHostOwned_ReturnsFalse()
    {
        var changedPaths  = new[] { ".squad/tasks.md", ".squad/plans/plan.json" };
        var hostOwnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".squad/tasks.md", ".squad/plans/plan.json" };
        var result = RecoveryCommitValidator.ContainsOnlyNonHostChanges(changedPaths, hostOwnedPaths);
        Assert.That(result, Is.False);
    }

    [Test]
    public void ContainsOnlyNonHostChanges_MixedSourceAndHostOwned_ReturnsFalse()
    {
        var changedPaths  = new[] { ".squad/tasks.md", "src/Feature.cs" };
        var hostOwnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".squad/tasks.md" };
        var result = RecoveryCommitValidator.ContainsOnlyNonHostChanges(changedPaths, hostOwnedPaths);
        Assert.That(result, Is.False);
    }

    [Test]
    public void ContainsOnlyNonHostChanges_SourceOnly_ReturnsTrue()
    {
        var result = RecoveryCommitValidator.ContainsOnlyNonHostChanges(
            ["src/Feature.cs", "tests/FeatureTests.cs"],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".squad/" });
        Assert.That(result, Is.True);
    }

    [Test]
    public void ContainsOnlyNonHostChanges_DirectoryPrefixMatchesDescendant_ReturnsFalse()
    {
        var result = RecoveryCommitValidator.ContainsOnlyNonHostChanges(
            [".squad/plans/plan.json"],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".squad/" });
        Assert.That(result, Is.False);
    }

    [Test]
    public void ContainsOnlyNonHostChanges_SimilarPrefixOutsideDirectory_ReturnsTrue()
    {
        var result = RecoveryCommitValidator.ContainsOnlyNonHostChanges(
            [".squad-tools/Feature.cs"],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".squad/" });
        Assert.That(result, Is.True);
    }

    [Test]
    public void ContainsOnlyNonHostChanges_EmptyChanges_ReturnsFalse()
    {
        var result = RecoveryCommitValidator.ContainsOnlyNonHostChanges(
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
