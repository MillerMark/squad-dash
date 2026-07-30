using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
public class TranscriptApprovalCardBuilderTests
{
    private static ApprovalReviewSnapshot BuildTestSnapshot(
        int completedTaskCount = 3,
        int totalTaskCount = 5,
        int changedFileCount = 0,
        int downstreamTaskCount = 0)
    {
        var completedTasks = new List<ReviewTaskEntry>
        {
            new("task-1", "Implement auth module", "Added JWT auth",
                new List<ReviewCommitEntry>
                {
                    new(new CommitLink("abc1234", "abc1234567890", "Add JWT auth"),
                        VerificationPassed: true,
                        ChangedFiles: []),
                }),
            new("task-2", "Add user model", null,
                new List<ReviewCommitEntry>
                {
                    new(new CommitLink("def5678", "def5678901234", "Add user model"),
                        VerificationPassed: null,
                        ChangedFiles: []),
                }),
        };

        var changedFiles = Enumerable.Range(0, changedFileCount)
            .Select(i => new ChangedFileEntry(
                $"src/file{i}.cs",
                i % 3 == 0 ? FileChangeStatus.Added : FileChangeStatus.Modified,
                Insertions: 10 + i,
                Deletions: i,
                CommitSha: "abc1234567890",
                Link: new FileLink($"src/file{i}.cs", "abc1234567890")))
            .ToList();

        var downstreamTasks = Enumerable.Range(0, downstreamTaskCount)
            .Select(i => new DownstreamTaskEntry($"ds-{i}", $"Downstream task {i}", "pending"))
            .ToList();

        return new ApprovalReviewSnapshot(
            PlanId: "PLAN-001",
            PlanTitle: "Test Plan",
            CompletedTaskCount: completedTaskCount,
            TotalTaskCount: totalTaskCount,
            CurrentStage: null,
            GateId: "gate-1",
            GateReason: "Review completed tasks before continuing",
            AfterTaskIds: ["task-1", "task-2"],
            BeforeTaskIds: ["task-3", "task-4"],
            CompletedTasks: completedTasks,
            DownstreamTasks: downstreamTasks,
            AllChangedFiles: changedFiles,
            IndependentWork: [],
            BuiltAt: DateTimeOffset.UtcNow);
    }

    [Test]
    public void BuildApproveLabel_SingleGate_ReturnsCheckpointLabel()
    {
        var label = ApprovalCardNotificationCoordinator.BuildApproveLabel(1);
        Assert.That(label, Does.Contain("Approve Checkpoint"));
        Assert.That(label, Does.Not.Contain("2"));
    }

    [Test]
    public void BuildApproveLabel_MultipleGates_IncludesCount()
    {
        var label = ApprovalCardNotificationCoordinator.BuildApproveLabel(3);
        Assert.That(label, Does.Contain("3"));
        Assert.That(label, Does.Contain("Ready Checkpoints"));
    }

    [Test]
    public void Snapshot_ContainsExpectedFields()
    {
        var snapshot = BuildTestSnapshot(changedFileCount: 5, downstreamTaskCount: 2);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.PlanId, Is.EqualTo("PLAN-001"));
            Assert.That(snapshot.PlanTitle, Is.EqualTo("Test Plan"));
            Assert.That(snapshot.CompletedTasks, Has.Count.EqualTo(2));
            Assert.That(snapshot.AllChangedFiles, Has.Count.EqualTo(5));
            Assert.That(snapshot.DownstreamTasks, Has.Count.EqualTo(2));
            Assert.That(snapshot.GateReason, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void Snapshot_CommitLink_HasExpectedProperties()
    {
        var snapshot = BuildTestSnapshot();
        var commit = snapshot.CompletedTasks[0].Commits[0];

        Assert.Multiple(() =>
        {
            Assert.That(commit.Link.ShortSha, Is.EqualTo("abc1234"));
            Assert.That(commit.Link.FullSha, Is.EqualTo("abc1234567890"));
            Assert.That(commit.Link.Subject, Is.EqualTo("Add JWT auth"));
            Assert.That(commit.Link.InternalUri, Is.EqualTo("app://commit-diff:abc1234567890"));
            Assert.That(commit.VerificationPassed, Is.True);
        });
    }

    [Test]
    public void Snapshot_ChangedFileEntry_StatusMapping()
    {
        var added = new ChangedFileEntry("a.cs", FileChangeStatus.Added, 10, 0, "sha", new FileLink("a.cs", "sha"));
        var deleted = new ChangedFileEntry("b.cs", FileChangeStatus.Deleted, 0, 5, "sha", new FileLink("b.cs", "sha"));
        var modified = new ChangedFileEntry("c.cs", FileChangeStatus.Modified, 3, 2, "sha", new FileLink("c.cs", "sha"));

        Assert.Multiple(() =>
        {
            Assert.That(added.Status, Is.EqualTo(FileChangeStatus.Added));
            Assert.That(deleted.Status, Is.EqualTo(FileChangeStatus.Deleted));
            Assert.That(modified.Status, Is.EqualTo(FileChangeStatus.Modified));
        });
    }

    [Test]
    public void TranscriptApprovalCardTag_Identity()
    {
        var tag1 = new TranscriptApprovalCardTag("plan-1", "gate-1", 3);
        var tag2 = new TranscriptApprovalCardTag("plan-1", "gate-1", 3);
        var tag3 = new TranscriptApprovalCardTag("plan-1", "gate-2", 3);

        Assert.Multiple(() =>
        {
            Assert.That(tag1, Is.EqualTo(tag2));
            Assert.That(tag1, Is.Not.EqualTo(tag3));
            Assert.That(tag1.PlanId, Is.EqualTo("plan-1"));
            Assert.That(tag1.GateId, Is.EqualTo("gate-1"));
            Assert.That(tag1.Version, Is.EqualTo(3));
        });
    }
}
