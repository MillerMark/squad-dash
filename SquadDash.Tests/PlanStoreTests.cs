using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanStoreTests
{
    private TestWorkspace _workspace = null!;
    private PlanStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _workspace = new TestWorkspace();
        // PlanStore receives the .squad folder path
        var squadFolder = _workspace.GetPath(".squad");
        Directory.CreateDirectory(squadFolder);
        _store = new PlanStore(squadFolder);
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    // ─── Existence ───────────────────────────────────────────────────────────

    [Test]
    public void Exists_PlanNotYetSaved_ReturnsFalse()
    {
        Assert.That(_store.Exists("PROJ-20260101"), Is.False);
    }

    [Test]
    public void Exists_AfterSave_ReturnsTrue()
    {
        _store.Save(MakePlan("PROJ-20260101"));
        Assert.That(_store.Exists("PROJ-20260101"), Is.True);
    }

    // ─── Load ────────────────────────────────────────────────────────────────

    [Test]
    public void Load_FileNotPresent_ReturnsNull()
    {
        Assert.That(_store.Load("PROJ-20260101"), Is.Null);
    }

    [Test]
    public void Load_AfterSave_ReturnsSamePlan()
    {
        var plan = MakePlan("PROJ-20260101");
        _store.Save(plan);

        var loaded = _store.Load("PROJ-20260101");

        Assert.Multiple(() =>
        {
            Assert.That(loaded,                   Is.Not.Null);
            Assert.That(loaded!.PlanId,           Is.EqualTo("PROJ-20260101"));
            Assert.That(loaded.Revision,          Is.EqualTo(plan.Revision));
            Assert.That(loaded.LifecycleStatus,   Is.EqualTo(PlanLifecycleStatus.Staged));
            Assert.That(loaded.Title,             Is.EqualTo(plan.Title));
        });
    }

    [Test]
    public void Load_CorruptJson_ReturnsNull()
    {
        var planPath = _workspace.GetPath(".squad", "plans", "PROJ-20260101.json");
        Directory.CreateDirectory(Path.GetDirectoryName(planPath)!);
        File.WriteAllText(planPath, "{ not valid json ]]]]");

        Assert.That(_store.Load("PROJ-20260101"), Is.Null);
    }

    [Test]
    public void Load_PlanIdMismatch_ReturnsNull()
    {
        // Serialize a plan with one ID but store it under a different filename
        var plan = MakePlan("PROJ-20260101");
        var json = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
        var wrongPath = _workspace.GetPath(".squad", "plans", "OTHER-20260101.json");
        Directory.CreateDirectory(Path.GetDirectoryName(wrongPath)!);
        File.WriteAllText(wrongPath, json);

        Assert.That(_store.Load("OTHER-20260101"), Is.Null);
    }

    [Test]
    public void Load_UnknownLifecycleStatus_ReturnsNull()
    {
        var plan = MakePlan("PROJ-20260101") with { LifecycleStatus = "unknown-status" };
        var json = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
        var path = _workspace.GetPath(".squad", "plans", "PROJ-20260101.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);

        Assert.That(_store.Load("PROJ-20260101"), Is.Null);
    }

    // ─── LoadAll ─────────────────────────────────────────────────────────────

    [Test]
    public void LoadAll_EmptyFolder_ReturnsEmptyList()
    {
        Assert.That(_store.LoadAll(), Is.Empty);
    }

    [Test]
    public void LoadAll_FolderAbsent_ReturnsEmptyList()
    {
        // No .squad/plans folder was created
        var emptySquadFolder = _workspace.GetPath(".squad2");
        Directory.CreateDirectory(emptySquadFolder);
        var store = new PlanStore(emptySquadFolder);

        Assert.That(store.LoadAll(), Is.Empty);
    }

    [Test]
    public void LoadAll_MultiplePlans_ReturnsAll()
    {
        _store.Save(MakePlan("PROJ-20260101"));
        _store.Save(MakePlan("PROJ-20260201"));
        _store.Save(MakePlan("PROJ-20260301"));

        var all = _store.LoadAll();

        Assert.That(all, Has.Count.EqualTo(3));
    }

    [Test]
    public void LoadAll_SkipsCorruptFiles_ReturnsValid()
    {
        _store.Save(MakePlan("PROJ-20260101"));

        var corrupt = _workspace.GetPath(".squad", "plans", "CORRUPT-20260202.json");
        File.WriteAllText(corrupt, "{ broken");

        var all = _store.LoadAll();

        Assert.That(all, Has.Count.EqualTo(1));
        Assert.That(all[0].PlanId, Is.EqualTo("PROJ-20260101"));
    }

    // ─── Save ────────────────────────────────────────────────────────────────

    [Test]
    public void Save_CreatesPlansFolder_WhenAbsent()
    {
        var squadFolder = _workspace.GetPath(".squad2");
        Directory.CreateDirectory(squadFolder);
        var store = new PlanStore(squadFolder);

        store.Save(MakePlan("PROJ-20260101"));

        Assert.That(Directory.Exists(Path.Combine(squadFolder, "plans")), Is.True);
    }

    [Test]
    public void Save_Overwrites_ExistingPlan()
    {
        var original = MakePlan("PROJ-20260101");
        _store.Save(original);

        var updated = original with { LifecycleStatus = PlanLifecycleStatus.Executing };
        _store.Save(updated);

        var loaded = _store.Load("PROJ-20260101");
        Assert.That(loaded!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
    }

    [Test]
    public void Save_ReturnsPlanUnchanged()
    {
        var plan = MakePlan("PROJ-20260101");
        var returned = _store.Save(plan);
        Assert.That(returned, Is.SameAs(plan));
    }

    // ─── Delete ──────────────────────────────────────────────────────────────

    [Test]
    public void Delete_ExistingPlan_RemovesFile()
    {
        _store.Save(MakePlan("PROJ-20260101"));
        _store.Delete("PROJ-20260101");
        Assert.That(_store.Exists("PROJ-20260101"), Is.False);
    }

    [Test]
    public void Delete_NonExistentPlan_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _store.Delete("MISSING-20260101"));
    }

    // ─── Lifecycle round-trip ─────────────────────────────────────────────────

    [Test]
    public void Plan_WithInterruptionData_RoundTrips()
    {
        var plan = MakePlan("PROJ-20260101") with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            InterruptionData = new PlanInterruptionData(
                Reason:              "Unexpected build failure",
                RecoveryState:       PlanRecoveryState.PendingRecovery,
                LoopIteration:       2,
                InterruptedTaskId:   "PROJ-20260101-002",
                LastCompletedTaskId: "PROJ-20260101-001",
                LastCommit:          "cafe1234",
                AffectedPaths:       ["src/Core.cs"],
                PartialWorkEvidence: "Tests written; build failed"),
        };
        _store.Save(plan);

        var loaded = _store.Load("PROJ-20260101");

        Assert.Multiple(() =>
        {
            Assert.That(loaded!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Interrupted));
            Assert.That(loaded.InterruptionData!.Reason, Is.EqualTo("Unexpected build failure"));
            Assert.That(loaded.InterruptionData.LastCommit, Is.EqualTo("cafe1234"));
            Assert.That(loaded.InterruptionData.LoopIteration, Is.EqualTo(2));
        });
    }

    [Test]
    public void Plan_WithApprovalGates_RoundTrips()
    {
        var plan = MakePlan("PROJ-20260101") with
        {
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
            ApprovalGates =
            [
                new PlanApprovalGate(
                    GateId:       "gate-review",
                    Message:      "Peer review before deploy",
                    AfterTaskIds: ["PROJ-20260101-001"],
                    BeforeTaskIds:["PROJ-20260101-002"],
                    Status:       PlanGateStatus.AwaitingApproval,
                    RequestedAt:  DateTimeOffset.UtcNow),
            ],
        };
        _store.Save(plan);

        var loaded = _store.Load("PROJ-20260101");

        Assert.Multiple(() =>
        {
            Assert.That(loaded!.ApprovalGates, Has.Count.EqualTo(1));
            Assert.That(loaded.ApprovalGates[0].GateId, Is.EqualTo("gate-review"));
            Assert.That(loaded.ApprovalGates[0].Status, Is.EqualTo(PlanGateStatus.AwaitingApproval));
        });
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static Plan MakePlan(string planId) => new(
        PlanId:          planId,
        Revision:        "abc123def456789a",
        Source:          PlanSource.TasksJson,
        LifecycleStatus: PlanLifecycleStatus.Staged,
        Title:           $"Test plan {planId}",
        Branch:          "feature/test",
        Summary:         "Created for unit tests",
        Tasks:
        [
            new PlanTask(
                TaskId:      $"{planId}-001",
                Title:       "First step",
                Description: "Initial work",
                DependsOn:   [],
                Priority:    "high",
                Status:      PlanTaskStatus.Pending),
            new PlanTask(
                TaskId:      $"{planId}-002",
                Title:       "Second step",
                Description: "Follow-up work",
                DependsOn:   [$"{planId}-001"],
                Priority:    "high",
                Status:      PlanTaskStatus.Pending),
        ],
        ApprovalGates: [],
        Progress:       new PlanProgress(CompletedCount: 0, TotalCount: 2),
        Timestamps:     new PlanTimestamps(CreatedAt: DateTimeOffset.UtcNow));
}
