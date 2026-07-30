using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ApprovalReviewSnapshotBuilderTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Plan MakePlan(
        IReadOnlyList<PlanTask> tasks,
        IReadOnlyList<PlanApprovalGate>? gates = null)
    {
        var completed = tasks.Count(t => t.Status == PlanTaskStatus.Complete);
        return new Plan(
            PlanId:          "TEST-20260101",
            Revision:        "rev1",
            Source:          PlanSource.TasksJson,
            LifecycleStatus: PlanLifecycleStatus.AwaitingApproval,
            Title:           "Test Plan",
            Branch:          "feature/test",
            Summary:         "Test plan for snapshot builder",
            Tasks:           tasks,
            ApprovalGates:   gates ?? [],
            Progress:        new PlanProgress(completed, tasks.Count),
            Timestamps:      new PlanTimestamps(DateTimeOffset.UtcNow));
    }

    private static PlanTask MakeTask(
        string taskId,
        string status,
        string[]? dependsOn = null,
        string? commit = null,
        string? completionSummary = null,
        DateTimeOffset? completedAt = null)
    {
        return new PlanTask(
            TaskId:            taskId,
            Title:             $"Task {taskId}",
            Description:       $"Description for {taskId}",
            DependsOn:         dependsOn ?? [],
            Priority:          "mid",
            Status:            status,
            Commit:            commit,
            CompletedAt:       completedAt,
            CompletionSummary: completionSummary);
    }

    private static PlanApprovalGate MakeGate(
        string gateId,
        string[] afterTaskIds,
        string[] beforeTaskIds,
        string message = "Review before proceeding")
    {
        return new PlanApprovalGate(
            GateId:       gateId,
            Message:      message,
            AfterTaskIds: afterTaskIds,
            BeforeTaskIds: beforeTaskIds,
            Status:       PlanGateStatus.AwaitingApproval);
    }

    private static GitCommandRunner FakeGit(string output = "")
    {
        return (_, _) => Task.FromResult(output);
    }

    // ── BuildAsync ────────────────────────────────────────────────────────────

    [Test]
    public async Task BuildAsync_PopulatesPlanProgressAndGateBoundary()
    {
        var tasks = new[]
        {
            MakeTask("T1", PlanTaskStatus.Complete, commit: "abc1234", completionSummary: "Did T1"),
            MakeTask("T2", PlanTaskStatus.Complete, commit: "def5678", dependsOn: ["T1"], completionSummary: "Did T2"),
            MakeTask("T3", PlanTaskStatus.Pending, dependsOn: ["T2"]),
        };
        var gate = MakeGate("G1", ["T1", "T2"], ["T3"]);
        var plan = MakePlan(tasks, [gate]);

        var builder = new ApprovalReviewSnapshotBuilder(FakeGit());
        var snapshot = await builder.BuildAsync(plan, gate);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.PlanId, Is.EqualTo("TEST-20260101"));
            Assert.That(snapshot.PlanTitle, Is.EqualTo("Test Plan"));
            Assert.That(snapshot.CompletedTaskCount, Is.EqualTo(2));
            Assert.That(snapshot.TotalTaskCount, Is.EqualTo(3));
            Assert.That(snapshot.GateId, Is.EqualTo("G1"));
            Assert.That(snapshot.GateReason, Is.EqualTo("Review before proceeding"));
            Assert.That(snapshot.AfterTaskIds, Is.EqualTo(new[] { "T1", "T2" }));
            Assert.That(snapshot.BeforeTaskIds, Is.EqualTo(new[] { "T3" }));
        });
    }

    [Test]
    public async Task BuildAsync_IncludesCompletedTasksWithCommitsAndSummaries()
    {
        var tasks = new[]
        {
            MakeTask("T1", PlanTaskStatus.Complete, commit: "abc1234567890", completionSummary: "Implemented feature A"),
            MakeTask("T2", PlanTaskStatus.Pending, dependsOn: ["T1"]),
        };
        var gate = MakeGate("G1", ["T1"], ["T2"]);
        var plan = MakePlan(tasks, [gate]);

        var builder = new ApprovalReviewSnapshotBuilder(FakeGit());
        var snapshot = await builder.BuildAsync(plan, gate);

        Assert.That(snapshot.CompletedTasks, Has.Count.EqualTo(1));
        var task = snapshot.CompletedTasks[0];
        Assert.Multiple(() =>
        {
            Assert.That(task.TaskId, Is.EqualTo("T1"));
            Assert.That(task.Title, Is.EqualTo("Task T1"));
            Assert.That(task.CompletionSummary, Is.EqualTo("Implemented feature A"));
            Assert.That(task.Commits, Has.Count.EqualTo(1));
            Assert.That(task.Commits[0].Link.ShortSha, Is.EqualTo("abc1234"));
            Assert.That(task.Commits[0].Link.FullSha, Is.EqualTo("abc1234567890"));
        });
    }

    [Test]
    public async Task BuildAsync_DownstreamTasksArePopulated()
    {
        var tasks = new[]
        {
            MakeTask("T1", PlanTaskStatus.Complete, commit: "abc1234"),
            MakeTask("T2", PlanTaskStatus.Pending, dependsOn: ["T1"]),
            MakeTask("T3", PlanTaskStatus.Pending, dependsOn: ["T1"]),
        };
        var gate = MakeGate("G1", ["T1"], ["T2", "T3"]);
        var plan = MakePlan(tasks, [gate]);

        var builder = new ApprovalReviewSnapshotBuilder(FakeGit());
        var snapshot = await builder.BuildAsync(plan, gate);

        Assert.That(snapshot.DownstreamTasks, Has.Count.EqualTo(2));
        Assert.That(snapshot.DownstreamTasks.Select(d => d.TaskId),
            Is.EquivalentTo(new[] { "T2", "T3" }));
        Assert.That(snapshot.DownstreamTasks.All(d => d.Status == PlanTaskStatus.Pending));
    }

    [Test]
    public async Task BuildAsync_TaskWithNoCommit_HasEmptyCommitsList()
    {
        var tasks = new[]
        {
            MakeTask("T1", PlanTaskStatus.Complete, completionSummary: "Manual work"),
            MakeTask("T2", PlanTaskStatus.Pending, dependsOn: ["T1"]),
        };
        var gate = MakeGate("G1", ["T1"], ["T2"]);
        var plan = MakePlan(tasks, [gate]);

        var builder = new ApprovalReviewSnapshotBuilder(FakeGit());
        var snapshot = await builder.BuildAsync(plan, gate);

        Assert.That(snapshot.CompletedTasks[0].Commits, Is.Empty);
    }

    [Test]
    public async Task BuildAsync_VerificationResultsAreCarried()
    {
        var tasks = new[]
        {
            MakeTask("T1", PlanTaskStatus.Complete, commit: "abc1234"),
            MakeTask("T2", PlanTaskStatus.Pending, dependsOn: ["T1"]),
        };
        var gate = MakeGate("G1", ["T1"], ["T2"]);
        var plan = MakePlan(tasks, [gate]);
        var verifications = new Dictionary<string, bool?> { ["abc1234"] = true };

        var builder = new ApprovalReviewSnapshotBuilder(FakeGit());
        var snapshot = await builder.BuildAsync(plan, gate, verificationResults: verifications);

        Assert.That(snapshot.CompletedTasks[0].Commits[0].VerificationPassed, Is.True);
    }

    [Test]
    public async Task BuildAsync_OrdersTasksByCompletedAtThenTaskId()
    {
        var t1 = MakeTask("T2", PlanTaskStatus.Complete, commit: "sha2",
            completedAt: new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var t2 = MakeTask("T1", PlanTaskStatus.Complete, commit: "sha1",
            completedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var t3 = MakeTask("T3", PlanTaskStatus.Pending, dependsOn: ["T1", "T2"]);

        var gate = MakeGate("G1", ["T1", "T2"], ["T3"]);
        var plan = MakePlan([t1, t2, t3], [gate]);

        var builder = new ApprovalReviewSnapshotBuilder(FakeGit());
        var snapshot = await builder.BuildAsync(plan, gate);

        Assert.That(snapshot.CompletedTasks.Select(t => t.TaskId).ToArray(),
            Is.EqualTo(new[] { "T1", "T2" }));
    }

    [Test]
    public async Task BuildAsync_EmptyIndependentWorkInitially()
    {
        var tasks = new[]
        {
            MakeTask("T1", PlanTaskStatus.Complete, commit: "abc1234"),
            MakeTask("T2", PlanTaskStatus.Pending, dependsOn: ["T1"]),
        };
        var gate = MakeGate("G1", ["T1"], ["T2"]);
        var plan = MakePlan(tasks, [gate]);

        var builder = new ApprovalReviewSnapshotBuilder(FakeGit());
        var snapshot = await builder.BuildAsync(plan, gate);

        Assert.That(snapshot.IndependentWork, Is.Empty);
    }

    // ── UpdateWithIndependentWorkAsync ─────────────────────────────────────────

    [Test]
    public async Task UpdateWithIndependentWork_AppendsNewlyCompletedTasksOutsideGate()
    {
        var tasks = new[]
        {
            MakeTask("T1", PlanTaskStatus.Complete, commit: "sha1"),
            MakeTask("T2", PlanTaskStatus.Pending, dependsOn: ["T1"]),
            MakeTask("T3", PlanTaskStatus.Pending),  // independent
        };
        var gate = MakeGate("G1", ["T1"], ["T2"]);
        var plan = MakePlan(tasks, [gate]);

        var builder = new ApprovalReviewSnapshotBuilder(FakeGit());
        var snapshot = await builder.BuildAsync(plan, gate);

        // Now T3 completes independently.
        var updatedTasks = new[]
        {
            tasks[0],
            tasks[1],
            MakeTask("T3", PlanTaskStatus.Complete, commit: "sha3", completionSummary: "Done independently"),
        };
        var updatedPlan = MakePlan(updatedTasks, [gate]);

        var updated = await builder.UpdateWithIndependentWorkAsync(snapshot, updatedPlan, gate);

        Assert.That(updated.IndependentWork, Has.Count.EqualTo(1));
        Assert.That(updated.IndependentWork[0].TaskId, Is.EqualTo("T3"));
        Assert.That(updated.IndependentWork[0].CompletionSummary, Is.EqualTo("Done independently"));
    }

    [Test]
    public async Task UpdateWithIndependentWork_DoesNotDuplicateGatedTasks()
    {
        var tasks = new[]
        {
            MakeTask("T1", PlanTaskStatus.Complete, commit: "sha1"),
            MakeTask("T2", PlanTaskStatus.Pending, dependsOn: ["T1"]),
        };
        var gate = MakeGate("G1", ["T1"], ["T2"]);
        var plan = MakePlan(tasks, [gate]);

        var builder = new ApprovalReviewSnapshotBuilder(FakeGit());
        var snapshot = await builder.BuildAsync(plan, gate);

        // T1 is already in CompletedTasks — it should NOT appear in IndependentWork.
        var updated = await builder.UpdateWithIndependentWorkAsync(snapshot, plan, gate);

        Assert.That(updated.IndependentWork, Is.Empty);
    }

    [Test]
    public async Task UpdateWithIndependentWork_ReturnsSameSnapshotWhenNoNewWork()
    {
        var tasks = new[]
        {
            MakeTask("T1", PlanTaskStatus.Complete, commit: "sha1"),
            MakeTask("T2", PlanTaskStatus.Pending, dependsOn: ["T1"]),
        };
        var gate = MakeGate("G1", ["T1"], ["T2"]);
        var plan = MakePlan(tasks, [gate]);

        var builder = new ApprovalReviewSnapshotBuilder(FakeGit());
        var snapshot = await builder.BuildAsync(plan, gate);
        var updated = await builder.UpdateWithIndependentWorkAsync(snapshot, plan, gate);

        Assert.That(updated, Is.SameAs(snapshot));
    }

    // ── ParseShowOutput ──────────────────────────────────────────────────────

    [Test]
    public void ParseShowOutput_SingleCommit_ParsesFilesCorrectly()
    {
        var output = """
            COMMIT:abc123fullsha Add new feature
            
            3	2	src/foo.cs
            1	0	src/bar.cs
            
            """;
        var result = new Dictionary<string, List<ChangedFileEntry>>(StringComparer.OrdinalIgnoreCase);
        ApprovalReviewSnapshotBuilder.ParseShowOutput(output, result);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result.ContainsKey("abc123fullsha"), Is.True);
        var files = result["abc123fullsha"];
        Assert.That(files, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(files[0].FilePath, Is.EqualTo("src/foo.cs"));
            Assert.That(files[0].Insertions, Is.EqualTo(3));
            Assert.That(files[0].Deletions, Is.EqualTo(2));
            Assert.That(files[0].CommitSha, Is.EqualTo("abc123fullsha"));
            Assert.That(files[1].FilePath, Is.EqualTo("src/bar.cs"));
            Assert.That(files[1].Insertions, Is.EqualTo(1));
            Assert.That(files[1].Deletions, Is.EqualTo(0));
        });
    }

    [Test]
    public void ParseShowOutput_MultipleCommits_ParsedCorrectly()
    {
        var output = """
            COMMIT:sha1 First commit
            
            2	1	file1.cs
            
            COMMIT:sha2 Second commit
            
            5	3	file2.cs
            0	10	file3.cs
            
            """;
        var result = new Dictionary<string, List<ChangedFileEntry>>(StringComparer.OrdinalIgnoreCase);
        ApprovalReviewSnapshotBuilder.ParseShowOutput(output, result);

        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result["sha1"], Has.Count.EqualTo(1));
            Assert.That(result["sha2"], Has.Count.EqualTo(2));
            Assert.That(result["sha1"][0].FilePath, Is.EqualTo("file1.cs"));
            Assert.That(result["sha2"][1].Status, Is.EqualTo(FileChangeStatus.Deleted));
        });
    }

    [Test]
    public void ParseShowOutput_EmptyOutput_ReturnsEmpty()
    {
        var result = new Dictionary<string, List<ChangedFileEntry>>(StringComparer.OrdinalIgnoreCase);
        ApprovalReviewSnapshotBuilder.ParseShowOutput("", result);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ParseShowOutput_BinaryFile_CountedAsFileChanged()
    {
        var output = """
            COMMIT:sha1 Add binary
            
            -	-	image.png
            2	0	readme.md
            
            """;
        var result = new Dictionary<string, List<ChangedFileEntry>>(StringComparer.OrdinalIgnoreCase);
        ApprovalReviewSnapshotBuilder.ParseShowOutput(output, result);

        Assert.That(result["sha1"], Has.Count.EqualTo(2));
        var binary = result["sha1"][0];
        Assert.Multiple(() =>
        {
            Assert.That(binary.FilePath, Is.EqualTo("image.png"));
            Assert.That(binary.Insertions, Is.EqualTo(0));
            Assert.That(binary.Deletions, Is.EqualTo(0));
        });
    }

    // ── ParseShowOutputWithSubjects ──────────────────────────────────────────

    [Test]
    public void ParseShowOutputWithSubjects_ExtractsSubjects()
    {
        var output = """
            COMMIT:sha1 Implement auth flow
            
            3	1	src/auth.cs
            
            COMMIT:sha2 Fix login bug
            
            1	1	src/login.cs
            
            """;
        var (files, subjects) = ApprovalReviewSnapshotBuilder.ParseShowOutputWithSubjects(output);

        Assert.Multiple(() =>
        {
            Assert.That(subjects["sha1"], Is.EqualTo("Implement auth flow"));
            Assert.That(subjects["sha2"], Is.EqualTo("Fix login bug"));
            Assert.That(files, Has.Count.EqualTo(2));
        });
    }

    // ── Link models ─────────────────────────────────────────────────────────

    [Test]
    public void CommitLink_InternalUri_UsesAppScheme()
    {
        var link = new CommitLink("abc1234", "abc1234567890abcdef", "Add feature");
        Assert.That(link.InternalUri, Is.EqualTo("app://commit-diff:abc1234567890abcdef"));
    }

    [Test]
    public void FileLink_ReviewedVersionUri_IncludesCommitAndPath()
    {
        var link = new FileLink("src/foo.cs", "abc123");
        Assert.Multiple(() =>
        {
            Assert.That(link.ReviewedVersionUri, Is.EqualTo("app://file-at-commit:abc123:src/foo.cs"));
            Assert.That(link.WorkspaceFileUri, Is.EqualTo("app://open-workspace-file:src/foo.cs"));
        });
    }
}
