using System;
using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanTaskProjectionRepairTests
{
    private string _directory = null!;
    private string _tasksPath = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "SquadDashProjectionRepair", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _tasksPath = Path.Combine(_directory, "tasks.md");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public void Ensure_MissingGroup_RecreatesItAndPreservesOtherContent()
    {
        File.WriteAllText(_tasksPath, "# User backlog\n\n- [ ] Keep this item\n");
        var plan = MakePlan(PlanTaskStatus.Complete);

        var result = PlanTaskProjectionRepair.Ensure(_tasksPath, plan);
        var text = File.ReadAllText(_tasksPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanTaskProjectionRepairOutcome.Repaired));
            Assert.That(text, Does.Contain("decompose-group: REPAIR-001"));
            Assert.That(text, Does.Contain("- [x] **[REPAIR-001-001]**"));
            Assert.That(text, Does.Contain("Keep this item"));
        });
    }

    [Test]
    public void Ensure_CurrentProjection_IsNoOp()
    {
        var plan = MakePlan();
        Assert.That(PlanTaskProjectionRepair.Ensure(_tasksPath, plan).Outcome,
            Is.EqualTo(PlanTaskProjectionRepairOutcome.Repaired));
        var before = File.ReadAllText(_tasksPath);

        var result = PlanTaskProjectionRepair.Ensure(_tasksPath, plan);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanTaskProjectionRepairOutcome.Current));
            Assert.That(File.ReadAllText(_tasksPath), Is.EqualTo(before));
        });
    }

    [Test]
    public void Ensure_ConflictingExistingGroup_DoesNotOverwriteIt()
    {
        var conflicting =
            "<!-- decompose-group: REPAIR-001 | branch: feature/repair | revision: other -->\n" +
            "**[REPAIR-001] Conflicting**\n> Existing\n\n" +
            "- [ ] **[REPAIR-001-999]** Different\n" +
            "  Group: REPAIR-001 | Branch: feature/repair | Priority: mid\n" +
            "  description: Different\n  dependsOn: (none)\n";
        File.WriteAllText(_tasksPath, conflicting);

        var result = PlanTaskProjectionRepair.Ensure(_tasksPath, MakePlan());

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanTaskProjectionRepairOutcome.Conflict));
            Assert.That(File.ReadAllText(_tasksPath), Is.EqualTo(conflicting));
        });
    }

    [Test]
    public void Ensure_SameRevisionManagedBlockDrift_RepairsOnlyThatBlock()
    {
        var plan = MakePlan();
        Assert.That(PlanTaskProjectionRepair.Ensure(_tasksPath, plan).Outcome,
            Is.EqualTo(PlanTaskProjectionRepairOutcome.Repaired));
        File.AppendAllText(_tasksPath, "\n# User backlog\n\n- [ ] Newly added by AI\n");
        var drifted = File.ReadAllText(_tasksPath)
            .Replace("**[REPAIR-001-001]** Restore", "**[REPAIR-001-999]** Drifted", StringComparison.Ordinal);
        File.WriteAllText(_tasksPath, drifted);

        var result = PlanTaskProjectionRepair.Ensure(_tasksPath, plan);
        var repaired = File.ReadAllText(_tasksPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanTaskProjectionRepairOutcome.Repaired));
            Assert.That(repaired, Does.Contain("**[REPAIR-001-001]** Restore"));
            Assert.That(repaired, Does.Not.Contain("REPAIR-001-999"));
            Assert.That(repaired, Does.Contain("Newly added by AI"));
        });
    }

    [Test]
    public void Ensure_UnrelatedMalformedGroup_DoesNotBlockCurrentManagedProjection()
    {
        var plan = MakePlan();
        Assert.That(PlanTaskProjectionRepair.Ensure(_tasksPath, plan).Outcome,
            Is.EqualTo(PlanTaskProjectionRepairOutcome.Repaired));
        File.AppendAllText(_tasksPath,
            "\n<!-- decompose-group: OTHER | branch: feature/other | revision: other -->\n" +
            "**[OTHER] Other**\n> Other\n\n" +
            "- [ ] **[OTHER-001]** Other\n" +
            "  Group: OTHER | Branch: feature/other | Priority: mid\n" +
            "  description: Other\n  dependsOn: (none)\n  agentAssignments: not-json\n");

        var result = PlanTaskProjectionRepair.Ensure(_tasksPath, plan);

        Assert.That(result.Outcome, Is.EqualTo(PlanTaskProjectionRepairOutcome.Current));
    }

    private static Plan MakePlan(string status = PlanTaskStatus.Pending) => new(
        PlanId: "REPAIR-001",
        Revision: "revision-1",
        Source: PlanSource.Inbox,
        LifecycleStatus: PlanLifecycleStatus.Interrupted,
        Title: "Repair projection",
        Branch: "feature/repair",
        Summary: "Restore a missing projection.",
        Tasks:
        [
            new PlanTask(
                "REPAIR-001-001",
                "Restore",
                "Restore it.",
                [],
                "mid",
                status,
                Commit: status == PlanTaskStatus.Complete ? "abc1234" : null,
                CompletionSummary: status == PlanTaskStatus.Complete ? "Done" : null),
        ],
        ApprovalGates: [],
        Progress: new PlanProgress(status == PlanTaskStatus.Complete ? 1 : 0, 1),
        Timestamps: new PlanTimestamps(DateTimeOffset.UtcNow),
        HostRevision: "revision-1");
}
