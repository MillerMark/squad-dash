using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
public class DurableApprovalRequestManagerTests
{
    private string _tempDir = null!;
    private InboxStore _inbox = null!;
    private DurableApprovalRequestManager _manager = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"squad-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _inbox = new InboxStore(_tempDir);
        _manager = new DurableApprovalRequestManager(_inbox);
    }

    [TearDown]
    public void TearDown()
    {
        _manager.ClearLocks();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Plan MakePlan(string planId = "PLAN-001", string title = "Test Plan",
        IReadOnlyList<PlanApprovalGate>? gates = null, IReadOnlyList<PlanTask>? tasks = null)
    {
        tasks ??=
        [
            new PlanTask("T1", "Task 1", "Desc", [], "high", PlanTaskStatus.Complete),
            new PlanTask("T2", "Task 2", "Desc", ["T1"], "high", PlanTaskStatus.Pending),
            new PlanTask("T3", "Task 3", "Desc", ["T1"], "high", PlanTaskStatus.Pending),
        ];
        gates ??=
        [
            new PlanApprovalGate("GATE-001", "Review after T1", ["T1"], ["T2", "T3"], PlanGateStatus.AwaitingApproval),
        ];
        return new Plan(
            planId, "rev1", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.AwaitingApproval, title, "main", "Summary",
            tasks, gates,
            new PlanProgress(1, 3),
            new PlanTimestamps(DateTimeOffset.UtcNow));
    }

    private static ApprovalReviewSnapshot MakeSnapshot(string planId = "PLAN-001", string gateId = "GATE-001") =>
        new(planId, "Test Plan", 1, 3, PlanLifecycleStatus.AwaitingApproval,
            gateId, "Review after T1", ["T1"], ["T2", "T3"],
            [], [], [], [], DateTimeOffset.UtcNow);

    // ── Tests ────────────────────────────────────────────────────────────────

    [Test]
    public async Task AppendCheckpoint_CreatesNewMessage()
    {
        var plan = MakePlan();
        var snapshot = MakeSnapshot();

        var messageId = await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);

        Assert.That(messageId, Is.EqualTo("approval-gate-PLAN-001"));
        var msg = _inbox.GetById(messageId);
        Assert.That(msg, Is.Not.Null);
        Assert.That(msg!.Subject, Does.Contain("Test Plan"));
        Assert.That(msg.Read, Is.False);
        Assert.That(msg.Priority, Is.EqualTo("high"));
        Assert.That(msg.Actions, Is.Empty,
            "The Inbox message itself is the review surface; approval appears after its checks are complete");
    }

    [Test]
    public async Task AppendCheckpoint_ExposesPlanLinkButKeepsPersistenceAttachmentsInternal()
    {
        var plan = MakePlan();
        var messageId = await _manager.AppendCheckpointAsync(
            plan, plan.ApprovalGates[0], MakeSnapshot());

        var msg = _inbox.GetById(messageId)!;
        var visible = msg.Attachments
            .Where(DurableApprovalRequestManager.IsPresentationAttachment)
            .ToArray();

        Assert.That(visible, Has.Length.EqualTo(1));
        Assert.That(visible[0].Type, Is.EqualTo(DecomposePlanInbox.AttachmentType));
        Assert.That(visible[0].Label, Is.EqualTo("View plan and dependencies"));
        Assert.That(visible[0].PlanGroupId, Is.EqualTo(plan.PlanId));
        Assert.That(DecomposePlanInbox.TryReadSnapshot(visible[0], out var pending), Is.True);
        Assert.That(pending!.Group.GroupId, Is.EqualTo(plan.PlanId));

        Assert.That(msg.Attachments.Any(a => a.Type == DurableApprovalRequestManager.AttachmentType), Is.True);
        Assert.That(msg.Attachments.Any(a => a.Type == "approval-snapshot"), Is.True);
    }

    [Test]
    public async Task AppendCheckpoint_IsIdempotent()
    {
        var plan = MakePlan();
        var snapshot = MakeSnapshot();

        var id1 = await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);
        var id2 = await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);

        Assert.That(id1, Is.EqualTo(id2));
        var state = _manager.GetState("PLAN-001");
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.ActiveGateIds, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AppendCheckpoint_AppendsSecondGate()
    {
        var gate2 = new PlanApprovalGate("GATE-002", "Review after T2", ["T2"], ["T3"], PlanGateStatus.AwaitingApproval);
        var plan = MakePlan(gates: [
            new PlanApprovalGate("GATE-001", "Review after T1", ["T1"], ["T2", "T3"], PlanGateStatus.AwaitingApproval),
            gate2,
        ]);
        var snapshot1 = MakeSnapshot();
        var snapshot2 = MakeSnapshot(gateId: "GATE-002");

        await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot1);
        await _manager.AppendCheckpointAsync(plan, gate2, snapshot2);

        var state = _manager.GetState("PLAN-001");
        Assert.That(state!.ActiveGateIds, Has.Count.EqualTo(2));
        Assert.That(state.ActiveGateIds, Does.Contain("GATE-001"));
        Assert.That(state.ActiveGateIds, Does.Contain("GATE-002"));
    }

    [Test]
    public async Task AppendCheckpoint_RefreshesAggregatedMessageTimestamp()
    {
        var plan = MakePlan();
        await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        var messageId = DurableApprovalRequestManager.BuildMessageId(plan.PlanId);
        var oldTimestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        _inbox.Save(_inbox.GetById(messageId)! with { Timestamp = oldTimestamp });

        var gate2 = new PlanApprovalGate(
            "GATE-002", "Review after T2", ["T2"], ["T3"], PlanGateStatus.AwaitingApproval);
        var expanded = MakePlan(gates: [plan.ApprovalGates[0], gate2]);
        await _manager.AppendCheckpointAsync(expanded, gate2, MakeSnapshot(gateId: "GATE-002"));

        Assert.That(_inbox.GetById(messageId)!.Timestamp, Is.GreaterThan(oldTimestamp));
    }

    [Test]
    public async Task ResolveCheckpoint_MovesToHistory()
    {
        var plan = MakePlan();
        var snapshot = MakeSnapshot();
        await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);

        await _manager.ResolveCheckpointAsync(plan, "GATE-001", "Looks good");

        var state = _manager.GetState("PLAN-001");
        Assert.That(state!.ActiveGateIds, Is.Empty);
        Assert.That(state.ResolvedCheckpoints, Has.Count.EqualTo(1));
        Assert.That(state.ResolvedCheckpoints[0].GateId, Is.EqualTo("GATE-001"));
        Assert.That(state.ResolvedCheckpoints[0].ResolutionNote, Is.EqualTo("Looks good"));
        Assert.That(state.Archived, Is.True);
    }

    [Test]
    public async Task ResolveCheckpoint_ArchivesWhenNoActiveGates()
    {
        var plan = MakePlan();
        var snapshot = MakeSnapshot();
        await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);

        await _manager.ResolveCheckpointAsync(plan, "GATE-001");

        var msg = _inbox.GetById("approval-gate-PLAN-001");
        Assert.That(msg, Is.Not.Null);
        Assert.That(msg!.Read, Is.True);
        Assert.That(msg.Actions, Is.Empty);
        Assert.That(msg.Body, Does.Contain("archived"));
    }

    [Test]
    public async Task AppendCheckpoint_UnarchivesResolvedMessage()
    {
        var plan = MakePlan();
        var snapshot = MakeSnapshot();
        await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);
        await _manager.ResolveCheckpointAsync(plan, "GATE-001");

        // New gate arrives — unarchive
        var gate2 = new PlanApprovalGate("GATE-002", "Review after T2", ["T2"], ["T3"], PlanGateStatus.AwaitingApproval);
        var planWithGate2 = MakePlan(gates: [plan.ApprovalGates[0], gate2]);
        var snapshot2 = MakeSnapshot(gateId: "GATE-002");
        await _manager.AppendCheckpointAsync(planWithGate2, gate2, snapshot2);

        var state = _manager.GetState("PLAN-001");
        Assert.That(state!.Archived, Is.False);
        Assert.That(state.ActiveGateIds, Does.Contain("GATE-002"));
        Assert.That(state.ResolvedCheckpoints, Has.Count.EqualTo(1));

        var msg = _inbox.GetById("approval-gate-PLAN-001");
        Assert.That(msg!.Read, Is.False);
        Assert.That(msg.Actions, Is.Empty);
    }

    [Test]
    public async Task RefreshEvidence_UpdatesSnapshotWithoutAddingProgressNoiseToDecisionBody()
    {
        var plan = MakePlan();
        var snapshot = MakeSnapshot();
        await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);

        var updatedPlan = plan with { Progress = new PlanProgress(2, 3) };
        var updatedSnapshot = MakeSnapshot() with { CompletedTaskCount = 2 };
        await _manager.RefreshEvidenceAsync(updatedPlan, updatedSnapshot);

        var msg = _inbox.GetById("approval-gate-PLAN-001");
        var snapshotAttachment = msg!.Attachments.Single(attachment => attachment.Type == "approval-snapshot");
        Assert.Multiple(() =>
        {
            Assert.That(msg.Body, Does.Not.Contain("2/3"));
            Assert.That(snapshotAttachment.Content, Does.Contain("\"CompletedTaskCount\":2"));
        });
    }

    [Test]
    public async Task TryMarkNotified_ReturnsTrueOnce()
    {
        var plan = MakePlan();
        var snapshot = MakeSnapshot();
        await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);

        var first = await _manager.TryMarkNotifiedAsync("PLAN-001");
        var second = await _manager.TryMarkNotifiedAsync("PLAN-001");

        Assert.That(first, Is.True);
        Assert.That(second, Is.False);
    }

    [Test]
    public async Task AppendLaterCheckpoint_AdvancesVersionAndAllowsOneNewNotification()
    {
        var firstGate = new PlanApprovalGate(
            "GATE-001", "First review", ["T1"], ["T2"], PlanGateStatus.AwaitingApproval);
        var secondGate = new PlanApprovalGate(
            "GATE-002", "Second review", ["T2"], ["T3"], PlanGateStatus.AwaitingApproval);
        var plan = MakePlan(gates: [firstGate, secondGate]);
        await _manager.AppendCheckpointAsync(plan, firstGate, MakeSnapshot());
        Assert.That(await _manager.TryMarkNotifiedAsync(plan.PlanId), Is.True);
        var version1 = _manager.GetState(plan.PlanId)!.Version;

        await _manager.AppendCheckpointAsync(
            plan,
            secondGate,
            MakeSnapshot() with { GateId = secondGate.GateId, GateReason = secondGate.Message });

        var state = _manager.GetState(plan.PlanId)!;
        Assert.Multiple(() =>
        {
            Assert.That(state.Version, Is.GreaterThan(version1));
            Assert.That(state.ActiveGateIds, Is.EqualTo(new[] { "GATE-001", "GATE-002" }));
        });
        Assert.That(await _manager.TryMarkNotifiedAsync(plan.PlanId), Is.True);
        Assert.That(await _manager.TryMarkNotifiedAsync(plan.PlanId), Is.False);
    }

    [Test]
    public async Task RestoreActivePlanIds_FindsActiveMessages()
    {
        var plan1 = MakePlan("PLAN-A", "Plan A");
        var plan2 = MakePlan("PLAN-B", "Plan B");
        var snap1 = MakeSnapshot("PLAN-A");
        var snap2 = MakeSnapshot("PLAN-B");

        await _manager.AppendCheckpointAsync(plan1, plan1.ApprovalGates[0], snap1);
        await _manager.AppendCheckpointAsync(plan2, plan2.ApprovalGates[0], snap2);
        await _manager.ResolveCheckpointAsync(plan2, "GATE-001");

        var active = _manager.RestoreActivePlanIds();
        Assert.That(active, Does.Contain("PLAN-A"));
        Assert.That(active, Does.Not.Contain("PLAN-B"));
    }

    [Test]
    public async Task StableIdentity_SameIdAcrossLifecycle()
    {
        var plan = MakePlan();
        var snapshot = MakeSnapshot();

        var id1 = await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);
        await _manager.ResolveCheckpointAsync(plan, "GATE-001");

        var gate2 = new PlanApprovalGate("GATE-002", "Review after T2", ["T2"], ["T3"], PlanGateStatus.AwaitingApproval);
        var planWithGate2 = MakePlan(gates: [plan.ApprovalGates[0], gate2]);
        var id2 = await _manager.AppendCheckpointAsync(planWithGate2, gate2, MakeSnapshot(gateId: "GATE-002"));

        Assert.That(id1, Is.EqualTo(id2), "Message ID must be stable across archive/unarchive");
    }

    [Test]
    public void BuildMessageId_IsDeterministic()
    {
        var id1 = DurableApprovalRequestManager.BuildMessageId("PLAN-001");
        var id2 = DurableApprovalRequestManager.BuildMessageId("PLAN-001");
        Assert.That(id1, Is.EqualTo(id2));
        Assert.That(id1, Is.EqualTo("approval-gate-PLAN-001"));
    }

    [Test]
    public async Task GetState_ReturnsNullForUnknownPlan()
    {
        var state = _manager.GetState("UNKNOWN");
        Assert.That(state, Is.Null);
    }

    [Test]
    public async Task IsArchived_ReturnsCorrectState()
    {
        var plan = MakePlan();
        var snapshot = MakeSnapshot();
        await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);

        Assert.That(_manager.IsArchived("PLAN-001"), Is.False);

        await _manager.ResolveCheckpointAsync(plan, "GATE-001");
        Assert.That(_manager.IsArchived("PLAN-001"), Is.True);
    }

    [Test]
    public async Task ConcurrentAppends_AreSerialized()
    {
        var plan = MakePlan();
        var snapshot = MakeSnapshot();

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot))
            .ToArray();

        await Task.WhenAll(tasks);

        // All should return the same ID; state should be consistent
        var ids = tasks.Select(t => t.Result).Distinct().ToList();
        Assert.That(ids, Has.Count.EqualTo(1));
        var state = _manager.GetState("PLAN-001");
        Assert.That(state!.ActiveGateIds, Has.Count.EqualTo(1));
    }

    [Test]
    public void BuildBody_ShowsActiveAndResolved()
    {
        var plan = MakePlan();
        var resolved = new List<ResolvedCheckpointEntry>
        {
            new("GATE-000", DateTimeOffset.UtcNow, "LGTM"),
        };
        var body = DurableApprovalRequestManager.BuildBody(plan, ["GATE-001"], resolved);

        Assert.That(body, Does.Contain("Review after T1"));
        Assert.That(body, Does.Not.Contain("`GATE-001`"));
        Assert.That(body, Does.Contain("GATE-000"));
        Assert.That(body, Does.Contain("LGTM"));
        Assert.That(body, Does.Contain("Review this checkpoint:"));
        Assert.That(body, Does.Contain("1 resolved checkpoint(s)"));
    }

    [Test]
    public void BuildBody_ShowsApprovalQuestionWhenSupplied()
    {
        var original = MakePlan();
        var plan = original with
        {
            ApprovalGates =
            [
                original.ApprovalGates[0] with
                {
                    Question = "Does clicking the item populate the detail panel?",
                },
            ],
        };

        var body = DurableApprovalRequestManager.BuildBody(plan, ["GATE-001"], []);

        Assert.That(body, Does.Contain("**Confirm each observed result**")
            .And.Contain("☐ Does clicking the item populate the detail panel?")
            .And.Not.Contain("**Manual test instructions**"));
    }

    [Test]
    public async Task AppendCheckpoint_PutsGeneratedProcedureAndCheckboxInOneMessage()
    {
        var original = MakePlan();
        var gate = original.ApprovalGates[0] with
        {
            ProofRequirements =
            [
                new PlanTaskProofRequirement(
                    "new-result",
                    "live-ui-observation",
                    "Observe the generated result.",
                    "Does the new result appear?",
                    HumanVerificationAccessMode.Recipe,
                    new HumanVerificationRecipe(
                        "Build and install the prepared package.",
                        "Open the sample and invoke the feature.",
                        "The new result appears.",
                        "Remove the sample and temporary package."),
                    Title: "Generated result"),
            ],
        };
        var plan = original with { ApprovalGates = [gate] };

        var messageId = await _manager.AppendCheckpointAsync(plan, gate, MakeSnapshot());
        var message = _inbox.GetById(messageId)!;
        var instructions = message.Attachments.Single(attachment =>
            attachment.Type == DurableApprovalRequestManager.OperatorGuideAttachmentType).Content!;

        Assert.Multiple(() =>
        {
            Assert.That(message.Body, Does.Contain("☐ Does the new result appear?")
                .And.Contain("## Generated result")
                .And.Not.Contain("## Does the new result appear?")
                .And.Contain("**Set up:**")
                .And.Contain("**Open and perform:**")
                .And.Contain("**Observe:**")
                .And.Contain("**Clean up:**")
                .And.Not.Contain("Open the validation instructions"));
        });

        var setup = instructions.IndexOf("**Set up:**", StringComparison.Ordinal);
        var open = instructions.IndexOf("**Open and perform:**", StringComparison.Ordinal);
        var observe = instructions.IndexOf("**Observe:**", StringComparison.Ordinal);
        var cleanup = instructions.IndexOf("**Clean up:**", StringComparison.Ordinal);
        Assert.That(new[] { setup, open, observe, cleanup }, Is.Ordered.And.All.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task AppendCheckpoint_GroupsSharedWorkflowButKeepsIndependentCheckboxes()
    {
        var original = MakePlan();
        var sharedSetup = "Build and install the prepared package.";
        var sharedAction = "Open the sample and invoke the feature once.";
        var sharedCleanup = "Remove the sample and temporary package.";
        var gate = original.ApprovalGates[0] with
        {
            ProofRequirements =
            [
                new PlanTaskProofRequirement(
                    "editor-result",
                    "live-ui-observation",
                    "Observe the editor result.",
                    "Does the result appear in the editor?",
                    HumanVerificationAccessMode.Recipe,
                    new HumanVerificationRecipe(
                        sharedSetup,
                        sharedAction,
                        "The result appears in the editor.",
                        sharedCleanup),
                    Title: "Editor result"),
                new PlanTaskProofRequirement(
                    "completion-status",
                    "live-ui-observation",
                    "Observe completion status.",
                    "Does the status report completion?",
                    HumanVerificationAccessMode.Recipe,
                    new HumanVerificationRecipe(
                        sharedSetup,
                        sharedAction,
                        "The status reports completion.",
                        sharedCleanup),
                    Title: "Completion status"),
            ],
        };
        var plan = original with { ApprovalGates = [gate] };

        var messageId = await _manager.AppendCheckpointAsync(plan, gate, MakeSnapshot());
        var body = _inbox.GetById(messageId)!.Body;

        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain("## Shared verification workflow"));
            Assert.That(body.Split(sharedSetup).Length - 1, Is.EqualTo(1));
            Assert.That(body.Split(sharedAction).Length - 1, Is.EqualTo(1));
            Assert.That(body.Split(sharedCleanup).Length - 1, Is.EqualTo(1));
            Assert.That(body, Does.Contain("**Observe — Editor result:** The result appears in the editor.")
                .And.Contain("☐ Does the result appear in the editor?")
                .And.Contain("**Observe — Completion status:** The status reports completion.")
                .And.Contain("☐ Does the status report completion?"));
        });
    }

    [TestCase(2, TestName = "AppendCheckpoint_RecipeOnlyProofsComposeAndAllowApproval")]
    [TestCase(0, TestName = "AppendCheckpoint_DirectOnlyProofsComposeAndAllowApproval")]
    [TestCase(1, TestName = "AppendCheckpoint_MixedAccessProofsComposeAndAllowApproval")]
    public async Task AppendCheckpoint_ProofAccessModesComposeAndAllowApproval(int recipeCount)
    {
        var original = MakePlan();
        var requirements = Enumerable.Range(1, 2)
            .Select(index => new PlanTaskProofRequirement(
                $"result-{index}",
                "live-ui-observation",
                $"Observe result {index} in the running app.",
                $"Does result {index} appear?",
                index <= recipeCount
                    ? HumanVerificationAccessMode.Recipe
                    : HumanVerificationAccessMode.DirectlyReachable,
                index <= recipeCount
                    ? new HumanVerificationRecipe(
                        $"Prepare sample {index}.",
                        $"Open sample {index}.",
                        $"Result {index} appears.",
                        $"Remove sample {index}.")
                    : null,
                Title: $"Result {index}"))
            .ToArray();
        var gate = original.ApprovalGates[0] with { ProofRequirements = requirements };
        var plan = original with { ApprovalGates = [gate] };

        var messageId = await _manager.AppendCheckpointAsync(plan, gate, MakeSnapshot());
        var message = _inbox.GetById(messageId)!;
        var guide = message.Attachments.Single(attachment =>
            attachment.Type == DurableApprovalRequestManager.OperatorGuideAttachmentType).Content!;

        Assert.Multiple(() =>
        {
            Assert.That(message.Body, Does.Not.Contain(ManualTestKitChecklistComposer.SetupErrorMarker));
            Assert.That(message.Actions, Is.Empty, "Review selections are still required before continuing.");
            foreach (var requirement in requirements)
            {
                Assert.That(message.Body, Does.Contain($"☐ {requirement.Question}"));
                Assert.That(guide, Does.Contain($"## {requirement.Title}")
                    .And.Contain($"{{{{approval-check:{requirement.RequirementId}}}}}"));
                if (requirement.VerificationRecipe is { } recipe)
                {
                    Assert.That(guide.IndexOf(recipe.Observe, StringComparison.Ordinal),
                        Is.LessThan(guide.IndexOf($"{{{{approval-check:{requirement.RequirementId}}}}}", StringComparison.Ordinal)));
                    Assert.That(guide.IndexOf($"{{{{approval-check:{requirement.RequirementId}}}}}", StringComparison.Ordinal),
                        Is.LessThan(guide.IndexOf(recipe.Cleanup, StringComparison.Ordinal)));
                }
            }
        });

        gate = gate with
        {
            HumanReviewSelections = PlanHumanReviewChecklist.Build(plan, [gate])
                .Select(item => new PlanHumanReviewSelection(
                    item.ItemId, true, item.CandidateCommit, DateTimeOffset.UtcNow,
                    ReviewTarget: item.ReviewTarget))
                .ToArray(),
        };
        plan = plan with { ApprovalGates = [gate] };
        await _manager.RefreshEvidenceAsync(plan, MakeSnapshot());
        var refreshed = _inbox.GetById(messageId)!;

        Assert.Multiple(() =>
        {
            Assert.That(refreshed.Body, Does.Not.Contain(ManualTestKitChecklistComposer.SetupErrorMarker));
            Assert.That(refreshed.Actions, Has.Count.EqualTo(1));
            Assert.That(refreshed.Actions.Select(action => action.RouteMode),
                Does.Contain(DurableApprovalRequestManager.ApprovalRouteMode));
        });
    }

    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(0, true)]
    [TestCase(1, true)]
    public async Task AppendCheckpoint_MergesSuppliedKitWithUncoveredGate(
        int suppliedGateIndex,
        bool missingRecipe)
    {
        var gates = Enumerable.Range(0, 2)
            .Select(index => new PlanApprovalGate(
                $"GATE-00{index + 1}", "Review results", ["T1"], ["T2"],
                PlanGateStatus.AwaitingApproval,
                ProofRequirements:
                [
                    new PlanTaskProofRequirement(
                        $"prepared-{index}", "live-ui-observation", "Observe the prepared result.",
                        $"Does prepared result {index} appear?", HumanVerificationAccessMode.Recipe,
                        missingRecipe && index != suppliedGateIndex
                            ? null
                            : new HumanVerificationRecipe(
                                $"Prepare sample {index}.", $"Open sample {index}.",
                                $"Prepared result {index} appears.", $"Remove sample {index}."),
                        Title: $"Prepared result {index}"),
                    new PlanTaskProofRequirement(
                        $"direct-{index}", "live-ui-observation",
                        $"Observe direct result {index} in the running app.",
                        $"Does direct result {index} appear?", HumanVerificationAccessMode.DirectlyReachable,
                        Title: $"Direct result {index}"),
                ]))
            .ToArray();
        var plan = MakePlan(gates: gates);
        plan = plan with
        {
            ApprovalGates = gates.Select(gate => gate with
            {
                HumanReviewSelections = PlanHumanReviewChecklist.Build(plan, [gate])
                    .Select(item => new PlanHumanReviewSelection(
                        item.ItemId, true, item.CandidateCommit, DateTimeOffset.UtcNow,
                        ReviewTarget: item.ReviewTarget))
                    .ToArray(),
            }).ToArray(),
        };
        var kit = ManualTestKitApprovalBinding.BindForCompletedTask(
            new InboxMessage
            {
                Id = "single-gate-kit",
                Subject = "Review results",
                Body = "## Supplied workflow\nOpen the supplied sample and inspect both results.\n" +
                    $"{{{{approval-check:prepared-{suppliedGateIndex}}}}}\n" +
                    $"{{{{approval-check:direct-{suppliedGateIndex}}}}}\n" +
                    "Restore the supplied sample.",
                Timestamp = DateTimeOffset.UtcNow,
            },
            plan,
            "T1");
        Assert.That(ManualTestKitApprovalBinding.Read(kit)!.GateIds,
            Is.EqualTo(new[] { gates[suppliedGateIndex].GateId }),
            "Only real proof identities determine coverage, not shared task IDs or gate labels.");
        Assert.That(PlanHumanReviewChecklist.Build(plan, plan.ApprovalGates).All(item => item.IsChecked),
            Is.True);
        _inbox.Save(kit);

        var messageId = await _manager.AppendCheckpointAsync(
            plan, plan.ApprovalGates[0], MakeSnapshot());
        await _manager.AppendCheckpointAsync(
            plan, plan.ApprovalGates[1], MakeSnapshot(gateId: gates[1].GateId));

        AssertMergedRequest();
        await _manager.RefreshEvidenceAsync(plan, MakeSnapshot());
        AssertMergedRequest();
        await _manager.RefreshFromPlanAsync(plan);
        AssertMergedRequest();

        void AssertMergedRequest()
        {
            var message = _inbox.GetById(messageId)!;
            var guide = message.Attachments.Single(attachment =>
                attachment.Type == DurableApprovalRequestManager.OperatorGuideAttachmentType).Content!;
            var checklist = PlanHumanReviewChecklist.Build(plan, plan.ApprovalGates);
            var composedGuide = ManualTestKitChecklistComposer.Compose(guide, checklist);
            Assert.Multiple(() =>
            {
                Assert.That(_manager.GetState(plan.PlanId)!.ActiveGateIds, Has.Count.EqualTo(2));
                Assert.That(guide.Split(kit.Body).Length - 1, Is.EqualTo(1));
                Assert.That(guide, Does.Not.Contain($"Prepare sample {suppliedGateIndex}.")
                    .And.Not.Contain($"Observe direct result {suppliedGateIndex} in the running app."));
                foreach (var item in checklist)
                {
                    var missing = missingRecipe && item.RequirementId == $"prepared-{1 - suppliedGateIndex}";
                    Assert.That(guide.Split($"{{{{approval-check:{item.RequirementId}}}}}").Length - 1,
                        Is.EqualTo(missing ? 0 : 1));
                    Assert.That(message.Body.Split($"- ☐ {item.Text}").Length - 1,
                        Is.EqualTo(missing ? 0 : 1));
                    Assert.That(composedGuide.Split($"- ☐ {item.Text}").Length - 1,
                        Is.EqualTo(missing ? 0 : 1));
                }
                Assert.That(message.Body.Contains(ManualTestKitChecklistComposer.SetupErrorMarker),
                    Is.EqualTo(missingRecipe));
                Assert.That(composedGuide.Contains(ManualTestKitChecklistComposer.SetupErrorMarker),
                    Is.EqualTo(missingRecipe));
                Assert.That(message.Actions, Has.Count.EqualTo(missingRecipe ? 0 : 1));
                if (!missingRecipe)
                    Assert.That(message.Actions[0].RouteMode,
                        Is.EqualTo(DurableApprovalRequestManager.ApprovalRouteMode));
            });
        }
    }

    [TestCase(HumanVerificationAccessMode.Recipe)]
    [TestCase(HumanVerificationAccessMode.Fixture)]
    public async Task AppendCheckpoint_MissingRequiredRecipeStillSuppressesApproval(string accessMode)
    {
        var original = MakePlan();
        var gate = original.ApprovalGates[0] with
        {
            ProofRequirements =
            [
                new PlanTaskProofRequirement(
                    "prepared-result", "live-ui-observation", "Observe the prepared result.",
                    "Does the prepared result appear?", HumanVerificationAccessMode.Recipe,
                    new HumanVerificationRecipe("Prepare the sample.", "Open the sample.",
                        "The prepared result appears.", "Remove the sample.")),
                new PlanTaskProofRequirement(
                    "missing-recipe", "live-ui-observation", "Observe the inaccessible result.",
                    "Does the inaccessible result appear?", accessMode),
            ],
        };
        var plan = original with { ApprovalGates = [gate] };
        gate = gate with
        {
            HumanReviewSelections = PlanHumanReviewChecklist.Build(plan, [gate])
                .Select(item => new PlanHumanReviewSelection(
                    item.ItemId, true, item.CandidateCommit, DateTimeOffset.UtcNow,
                    ReviewTarget: item.ReviewTarget))
                .ToArray(),
        };
        plan = plan with { ApprovalGates = [gate] };

        var messageId = await _manager.AppendCheckpointAsync(plan, gate, MakeSnapshot());
        var message = _inbox.GetById(messageId)!;

        Assert.Multiple(() =>
        {
            Assert.That(message.Body, Does.Contain(ManualTestKitChecklistComposer.SetupErrorMarker));
            Assert.That(message.Actions, Is.Empty);
        });
    }

    [Test]
    public async Task AppendCheckpoint_MovesDetailedEvidenceToProvenanceAttachment()
    {
        var plan = MakePlan();
        var changedFile = new ChangedFileEntry(
            "src/SimulationSessionManager.cs",
            FileChangeStatus.Modified,
            42,
            3,
            "abc1234567890",
            new FileLink("src/SimulationSessionManager.cs", "abc1234567890"));
        var commit = new ReviewCommitEntry(
            new CommitLink("abc1234", "abc1234567890", "Implement simulation session runtime"),
            VerificationPassed: true,
            ChangedFiles: [changedFile]);
        var snapshot = MakeSnapshot() with
        {
            CompletedTasks =
            [
                new ReviewTaskEntry(
                    "T1",
                    "Implement session runtime",
                    "Added exact artifact ownership and cleanup.",
                    [commit],
                    "Claims and focused tests matched the changed files."),
            ],
            DownstreamTasks = [new DownstreamTaskEntry("T2", "Add Plan fixtures", "pending")],
            AllChangedFiles = [changedFile],
        };

        var messageId = await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);
        var message = _inbox.GetById(messageId)!;
        var evidence = message.Attachments.Single(attachment =>
            attachment.Label == DurableApprovalRequestManager.ProvenanceAttachmentLabel).Content!;

        Assert.Multiple(() =>
        {
            Assert.That(message.Body, Does.Not.Contain("Added exact artifact ownership and cleanup.")
                .And.Not.Contain("src/SimulationSessionManager.cs")
                .And.Not.Contain("abc1234"));
            Assert.That(evidence, Does.Contain("Completed work ready for review")
                .And.Contain("Added exact artifact ownership and cleanup.")
                .And.Contain("Claims and focused tests matched the changed files.")
                .And.Contain("abc1234")
                .And.Contain("Changed files (1)")
                .And.Contain("src/SimulationSessionManager.cs")
                .And.Contain("Approval allows this work to continue")
                .And.Contain("Add Plan fixtures")
                .And.Not.Contain("GATE-001`:"));
        });
    }

    [Test]
    public async Task AppendCheckpoint_AbsorbsBoundManualTestKitIntoSingleApprovalMessage()
    {
        var original = MakePlan();
        var gate = original.ApprovalGates[0] with
        {
            ProofRequirements =
            [
                new PlanTaskProofRequirement(
                    "native-result",
                    "live-ui-observation",
                    "Observe the generated result.",
                    "Does the native result appear?",
                    HumanVerificationAccessMode.Fixture,
                    new HumanVerificationRecipe(
                        "Prepare the fixture.",
                        "Open and invoke it.",
                        "Observe the result.",
                        "Restore the fixture."),
                    Title: "Native result"),
            ],
        };
        var plan = original with { ApprovalGates = [gate] };
        var kit = ManualTestKitApprovalBinding.BindForCompletedTask(
            new InboxMessage
            {
                Id = "native-validation-kit",
                Subject = "Native validation kit",
                Body = "## Does the native result appear?\n\n{{approval-check:native-result}}",
                Timestamp = DateTimeOffset.UtcNow,
            },
            plan,
            "T1");
        _inbox.Save(kit);

        var messageId = await _manager.AppendCheckpointAsync(plan, gate, MakeSnapshot());
        var message = _inbox.GetById(messageId)!;
        var guide = message.Attachments.Single(attachment =>
            attachment.Type == DurableApprovalRequestManager.OperatorGuideAttachmentType);

        Assert.Multiple(() =>
        {
            Assert.That(guide.Content, Is.EqualTo(kit.Body));
            Assert.That(_inbox.LoadAll(), Has.None.Matches<InboxMessage>(candidate => candidate.Id == kit.Id));
            Assert.That(_inbox.LoadAll(includeApprovalKitSources: true),
                Has.Some.Matches<InboxMessage>(candidate => candidate.Id == kit.Id));
            Assert.That(message.Body, Does.Contain("## Native result")
                .And.Not.Contain("## Does the native result appear?")
                .And.Contain("☐ Does the native result appear?"));
        });
    }

    [Test]
    public async Task RefreshEvidence_RetainsNewestValidProcedureAfterItsHiddenSourceIsArchived()
    {
        var original = MakePlan();
        var gate = original.ApprovalGates[0] with
        {
            ProofRequirements =
            [
                new PlanTaskProofRequirement(
                    "native-result",
                    "live-ui-observation",
                    "Observe the generated result.",
                    "Does the native result appear?",
                    HumanVerificationAccessMode.Fixture,
                    new HumanVerificationRecipe(
                        "Follow the delivered Runbook.md Setup instructions.",
                        "Follow the Inbox Open steps.",
                        "Observe the result.",
                        "Clean up through the kit instructions."),
                    Title: "Native result"),
            ],
        };
        var plan = original with { ApprovalGates = [gate] };
        var procedure = ManualTestKitApprovalBinding.BindForCompletedTask(
            new InboxMessage
            {
                Id = "concise-procedure",
                Subject = "Native validation procedure",
                Body = "## Native result\nOpen the sample directly and observe the result.\n{{approval-check:native-result}}",
                Timestamp = DateTimeOffset.UtcNow,
            },
            plan,
            "T1");
        _inbox.Save(procedure);

        var messageId = await _manager.AppendCheckpointAsync(plan, gate, MakeSnapshot());
        _inbox.Archive(procedure.Id);
        await _manager.RefreshEvidenceAsync(plan, MakeSnapshot());
        var message = _inbox.GetById(messageId)!;

        Assert.Multiple(() =>
        {
            Assert.That(_inbox.LoadAll(includeApprovalKitSources: true),
                Has.None.Matches<InboxMessage>(candidate => candidate.Id == procedure.Id));
            Assert.That(_inbox.LoadAll(includeApprovalKitSources: true, includeArchive: true),
                Has.Some.Matches<InboxMessage>(candidate => candidate.Id == procedure.Id));
            Assert.That(message.Body, Does.Contain("Open the sample directly")
                .And.Not.Contain("Follow the delivered Runbook")
                .And.Not.Contain("Follow the Inbox Open steps"));
            Assert.That(message.Attachments.Single(attachment =>
                    attachment.Type == DurableApprovalRequestManager.OperatorGuideAttachmentType).Content,
                Is.EqualTo(procedure.Body));
        });
    }

    [Test]
    public async Task AppendCheckpoint_KeepsExternalRunbookAsOptionalTroubleshooting()
    {
        var original = MakePlan();
        var gate = original.ApprovalGates[0] with
        {
            ProofRequirements =
            [
                new PlanTaskProofRequirement(
                    "native-result",
                    "live-ui-observation",
                    "Observe the result.",
                    "Does the native result appear?",
                    HumanVerificationAccessMode.Fixture),
            ],
        };
        var plan = original with { ApprovalGates = [gate] };
        var kit = ManualTestKitApprovalBinding.BindForCompletedTask(
            new InboxMessage
            {
                Id = "file-backed-validation-kit",
                Subject = "Native validation kit",
                Body = "{{approval-check:native-result}}",
                Timestamp = DateTimeOffset.UtcNow,
                Attachments =
                [
                    new InboxAttachment
                    {
                        Type = "file",
                        Label = "Validation runbook",
                        Path = @"C:\validation\Runbook.md",
                    },
                ],
            },
            plan,
            "T1");
        _inbox.Save(kit);

        var messageId = await _manager.AppendCheckpointAsync(plan, gate, MakeSnapshot());
        var message = _inbox.GetById(messageId)!;
        var runbook = message.Attachments.Single(attachment =>
            attachment.Label == "Detailed troubleshooting (optional)");

        Assert.Multiple(() =>
        {
            Assert.That(runbook.Type, Is.EqualTo("file"));
            Assert.That(runbook.Path, Is.EqualTo(@"C:\validation\Runbook.md"));
            Assert.That(runbook.InboxMessageId, Is.Null);
            Assert.That(message.Body, Does.Contain("☐ Does the native result appear?"));
        });
    }

    [Test]
    public void BuildActions_ReturnsEmptyWhenNoActiveGates()
    {
        var plan = MakePlan();
        var actions = DurableApprovalRequestManager.BuildActions(plan, []);
        Assert.That(actions, Is.Empty);
    }

    [Test]
    public void BuildActions_ReturnsNoSeparateReviewActionWhileChecksRemain()
    {
        var plan = MakePlan();
        var actions = DurableApprovalRequestManager.BuildActions(plan, ["GATE-001", "GATE-002"]);
        Assert.That(actions, Is.Empty);
    }

    [Test]
    public void BuildActions_ExposesExplicitContinueOnlyAfterEveryHumanCheckIsSelected()
    {
        var original = MakePlan();
        var gate = original.ApprovalGates[0] with
        {
            Question = "Does the result appear?",
            HumanReviewSelections =
            [
                new PlanHumanReviewSelection(
                    "legacy#1", true, PlanHumanReviewChecklist.UncommittedCandidate, DateTimeOffset.UtcNow),
            ],
        };
        var plan = original with { ApprovalGates = [gate] };
        var state = new DurableApprovalState(plan.PlanId, [gate.GateId], [], Version: 3);

        var actions = DurableApprovalRequestManager.BuildActions(plan, state);

        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(actions[0].RouteMode, Is.EqualTo(DurableApprovalRequestManager.ApprovalRouteMode));
        Assert.That(actions[0].Hint, Does.Contain("does not continue automatically"));
    }
}
