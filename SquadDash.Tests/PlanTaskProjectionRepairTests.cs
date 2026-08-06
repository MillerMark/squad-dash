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

    [Test]
    public void EnsureAfterHostTopologyMigration_RewritesOnlyLegacyAmendmentShape()
    {
        var legacy = MakeAmendmentPlan(migrated: false);
        Assert.That(PlanTaskProjectionRepair.Ensure(_tasksPath, legacy).Outcome,
            Is.EqualTo(PlanTaskProjectionRepairOutcome.Repaired));
        File.AppendAllText(_tasksPath, "\n# User backlog\n\n- [ ] Preserve me\n");

        var migrated = MakeAmendmentPlan(migrated: true);
        var result = PlanTaskProjectionRepair.EnsureAfterHostTopologyMigration(_tasksPath, migrated);
        var parsed = PlanTaskProjectionRepair.ReadManagedProjection(_tasksPath, migrated.PlanId)!;
        var group = parsed.DecomposeGroups[migrated.PlanId];

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanTaskProjectionRepairOutcome.Repaired));
            Assert.That(group.HostRevision, Is.EqualTo(migrated.Revision));
            Assert.That(group.Tasks.Select(task => task.Id), Is.EqualTo(new[]
            {
                "AMEND-001-001", "AMEND-001-AMD-001", "AMEND-001-002",
            }));
            Assert.That(group.Tasks.Single(task => task.Id == "AMEND-001-002").DependsOn,
                Is.EqualTo(new[] { "AMEND-001-AMD-001" }));
            Assert.That(File.ReadAllText(_tasksPath), Does.Contain("Preserve me"));
        });
    }

    [Test]
    public void EnsureAfterHostTopologyMigration_DoesNotOverwriteChangedTaskContract()
    {
        var legacy = MakeAmendmentPlan(migrated: false);
        Assert.That(PlanTaskProjectionRepair.Ensure(_tasksPath, legacy).Outcome,
            Is.EqualTo(PlanTaskProjectionRepairOutcome.Repaired));
        var changed = File.ReadAllText(_tasksPath).Replace(
            "Future task", "User changed task", StringComparison.Ordinal);
        File.WriteAllText(_tasksPath, changed);

        var result = PlanTaskProjectionRepair.EnsureAfterHostTopologyMigration(
            _tasksPath,
            MakeAmendmentPlan(migrated: true));

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanTaskProjectionRepairOutcome.Conflict));
            Assert.That(File.ReadAllText(_tasksPath), Is.EqualTo(changed));
        });
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

    private static Plan MakeAmendmentPlan(bool migrated)
    {
        var amendment = new PlanTask(
            "AMEND-001-AMD-001", "Amendment", "Add the requested amendment.",
            ["AMEND-001-001"], "high", PlanTaskStatus.Executing,
            AmendmentGateId: "AMEND-001-G01");
        var future = new PlanTask(
            "AMEND-001-002", "Future task", "Continue after approval.",
            migrated ? [amendment.TaskId] : ["AMEND-001-001"],
            "normal", PlanTaskStatus.Pending,
            Outputs: [new PlanTaskOutput("future-output", "Output retained only in the durable plan.")],
            Inputs: ["reviewed-output"],
            ProofRequirements:
            [
                new PlanTaskProofRequirement("future-proof", "test", "Prove the future task."),
            ]);
        var tasks = migrated
            ? new[]
            {
                new PlanTask("AMEND-001-001", "Reviewed", "Reviewed work.", [], "high", PlanTaskStatus.Complete,
                    Commit: "abc1234", CompletionSummary: "Done."),
                amendment,
                future,
            }
            : new[]
            {
                new PlanTask("AMEND-001-001", "Reviewed", "Reviewed work.", [], "high", PlanTaskStatus.Complete,
                    Commit: "abc1234", CompletionSummary: "Done."),
                future,
                amendment,
            };
        var revision = migrated ? "migrated-revision" : "legacy-revision";
        return new Plan(
            PlanId: "AMEND-001",
            Revision: revision,
            Source: PlanSource.Inbox,
            LifecycleStatus: PlanLifecycleStatus.Interrupted,
            Title: "Amendment migration",
            Branch: "feature/amendment",
            Summary: "Migrate the amendment boundary.",
            Tasks: tasks,
            ApprovalGates:
            [
                new PlanApprovalGate(
                    "AMEND-001-G01", "Review amendment",
                    ["AMEND-001-001", amendment.TaskId], [future.TaskId], PlanGateStatus.Pending),
            ],
            Progress: new PlanProgress(1, 3, amendment.TaskId),
            Timestamps: new PlanTimestamps(DateTimeOffset.UtcNow),
            HostRevision: revision);
    }
}
