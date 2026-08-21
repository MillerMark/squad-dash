using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PendingPlanEditorTests
{
    private string _root = null!;
    private string _squad = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "PendingPlanEditor_" + Guid.NewGuid().ToString("N"));
        _squad = Path.Combine(_root, ".squad");
        Directory.CreateDirectory(_squad);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Test]
    public void Apply_PersistsRevisionOutlineAndInboxReplacementWhilePreservingReadState()
    {
        var pendingStore = new PendingDecomposePlanStore(_squad);
        var inboxStore = new InboxStore(_squad);
        var original = pendingStore.Save(StagedPlanEditorTests.MakeGroup());
        var oldMessage = DecomposePlanInbox.BuildMessage(
            original, DateTimeOffset.Parse("2026-08-14T12:00:00Z"), true, "main") with { Read = true };
        inboxStore.Save(oldMessage);
        var edited = StagedPlanEditor.UpdateTask(
            original.Group, original.Group.Tasks[0].Id, "Edited", "Edited result", "high", []);

        var result = PendingPlanEditor.Apply(
            edited, original.CreatedAt!.Value, oldMessage.Id, pendingStore, inboxStore, "main");

        Assert.Multiple(() =>
        {
            Assert.That(result.UpdatedPlan.Revision, Is.Not.EqualTo(original.Revision));
            Assert.That(pendingStore.Load(original.Group.GroupId)?.Revision,
                Is.EqualTo(result.UpdatedPlan.Revision));
            Assert.That(inboxStore.GetById(oldMessage.Id), Is.Null);
            Assert.That(inboxStore.GetById(result.NewInboxMessageId!)?.Read, Is.True);
            Assert.That(File.Exists(StagedPlanRevisionOutlineStore.GetPath(
                _squad, original.Group.GroupId)), Is.True);
        });
    }

    [Test]
    public void Apply_InvalidGraphLeavesPendingPlanAndInboxUntouched()
    {
        var pendingStore = new PendingDecomposePlanStore(_squad);
        var inboxStore = new InboxStore(_squad);
        var original = pendingStore.Save(StagedPlanEditorTests.MakeGroup());
        var message = DecomposePlanInbox.BuildMessage(
            original, DateTimeOffset.UtcNow, explicitlyRequested: true, activeBranch: "main");
        inboxStore.Save(message);
        var invalid = StagedPlanEditor.UpdateTask(
            original.Group,
            original.Group.Tasks[0].Id,
            "Cycle",
            "Cycle",
            "high",
            [original.Group.Tasks[^1].Id]);

        Assert.Throws<InvalidOperationException>(() => PendingPlanEditor.Apply(
            invalid, original.CreatedAt!.Value, message.Id, pendingStore, inboxStore, "main"));

        Assert.Multiple(() =>
        {
            Assert.That(pendingStore.Load(original.Group.GroupId)?.Revision, Is.EqualTo(original.Revision));
            Assert.That(inboxStore.GetById(message.Id), Is.Not.Null);
        });
    }

    [Test]
    public void Apply_NoContentChangeKeepsSameRevisionAddressedInboxMessage()
    {
        var pendingStore = new PendingDecomposePlanStore(_squad);
        var inboxStore = new InboxStore(_squad);
        var original = pendingStore.Save(StagedPlanEditorTests.MakeGroup());
        var message = DecomposePlanInbox.BuildMessage(
            original, DateTimeOffset.UtcNow, explicitlyRequested: true, activeBranch: "main");
        inboxStore.Save(message);

        var result = PendingPlanEditor.Apply(
            original.Group, original.CreatedAt!.Value, message.Id, pendingStore, inboxStore, "main");

        Assert.Multiple(() =>
        {
            Assert.That(result.UpdatedPlan.Revision, Is.EqualTo(original.Revision));
            Assert.That(result.NewInboxMessageId, Is.EqualTo(message.Id));
            Assert.That(inboxStore.GetById(message.Id), Is.Not.Null);
        });
    }

    [Test]
    public void Apply_DependencyEdgeEdits_RoundTripThroughRecomputeAndPersistence()
    {
        var pendingStore = new PendingDecomposePlanStore(_squad);
        var sourceGroup = StagedPlanEditorTests.MakeGroup();
        var pending = pendingStore.Save(sourceGroup);
        var target = pending.Group.Tasks[2];
        var title = target.Title ?? "Third";
        var description = target.Description ?? "Third result.";
        var priority = target.Priority;
        var firstId = pending.Group.Tasks[0].Id;
        var secondId = pending.Group.Tasks[1].Id;
        var targetId = target.Id;

        void AssertRoundTrip(IReadOnlyList<string> dependsOn)
        {
            var current = pendingStore.Load(sourceGroup.GroupId)!;
            var edited = StagedPlanEditor.UpdateTask(
                current.Group, targetId, title, description, priority, dependsOn);
            var recompute = DependencyEditRecomputer.Recompute(edited);
            Assert.That(recompute.Success, Is.True);
            Assert.That(recompute.RecomputedGroup, Is.Not.Null);

            PendingPlanEditor.Apply(
                recompute.RecomputedGroup!,
                current.CreatedAt!.Value,
                null,
                pendingStore,
                null,
                null);

            var reloaded = pendingStore.Load(sourceGroup.GroupId);
            Assert.That(reloaded, Is.Not.Null);
            Assert.That(PlanTopologyNormalizer.TryNormalize(
                recompute.RecomputedGroup!, out var canonical, out var normalizationError),
                Is.True, normalizationError);
            Assert.That(GetDependencyTopology(reloaded!.Group),
                Is.EqualTo(GetDependencyTopology(canonical!.Group)));
        }

        AssertRoundTrip([secondId, firstId]); // add edge
        AssertRoundTrip([secondId]);          // remove edge
        AssertRoundTrip([]);                               // clear edge
    }

    [Test]
    public void DependencyEditPath_WhenStageChangeCancelled_DoesNotInvokeApplyAndLeavesPlanUnchanged()
    {
        var pendingStore = new PendingDecomposePlanStore(_squad);
        var original = pendingStore.Save(StagedPlanEditorTests.MakeGroup());
        var beforeTopology = GetDependencyTopology(original.Group);
        var edited = StagedPlanEditor.UpdateTask(
            original.Group,
            original.Group.Tasks[2].Id,
            original.Group.Tasks[2].Title ?? "Third",
            original.Group.Tasks[2].Description ?? "Third result",
            original.Group.Tasks[2].Priority,
            [original.Group.Tasks[0].Id]);
        var recompute = DependencyEditRecomputer.Recompute(edited);
        Assert.That(recompute.Success, Is.True);

        var applyCallCount = 0;
        var applied = ApplyDependencyEditThroughConfirmation(
            shouldApply: () => false,
            applyEdit: () =>
            {
                applyCallCount++;
                PendingPlanEditor.Apply(
                    recompute.RecomputedGroup!,
                    original.CreatedAt!.Value,
                    null,
                    pendingStore,
                    null,
                    null);
            });

        var reloaded = pendingStore.Load(original.Group.GroupId)!;
        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(applyCallCount, Is.EqualTo(0));
            Assert.That(reloaded.Revision, Is.EqualTo(original.Revision));
            Assert.That(GetDependencyTopology(reloaded.Group), Is.EqualTo(beforeTopology));
        });
    }

    [Test]
    public void DependencyEditPath_WhenStageChangeConfirmed_InvokesApplyAndMutatesPlan()
    {
        var pendingStore = new PendingDecomposePlanStore(_squad);
        var original = pendingStore.Save(StagedPlanEditorTests.MakeGroup());
        var edited = StagedPlanEditor.UpdateTask(
            original.Group,
            original.Group.Tasks[2].Id,
            original.Group.Tasks[2].Title ?? "Third",
            original.Group.Tasks[2].Description ?? "Third result",
            original.Group.Tasks[2].Priority,
            [original.Group.Tasks[0].Id]);
        var recompute = DependencyEditRecomputer.Recompute(edited);
        Assert.That(recompute.Success, Is.True);
        Assert.That(recompute.RecomputedGroup, Is.Not.Null);

        var applyCallCount = 0;
        var applied = ApplyDependencyEditThroughConfirmation(
            shouldApply: () => true,
            applyEdit: () =>
            {
                applyCallCount++;
                PendingPlanEditor.Apply(
                    recompute.RecomputedGroup!,
                    original.CreatedAt!.Value,
                    null,
                    pendingStore,
                    null,
                    null);
            });

        var reloaded = pendingStore.Load(original.Group.GroupId)!;
        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(applyCallCount, Is.EqualTo(1));
            Assert.That(reloaded.Revision, Is.Not.EqualTo(original.Revision));
            Assert.That(reloaded.Group.Tasks.Single(task => task.Id == original.Group.Tasks[2].Id).DependsOn,
                Is.EqualTo(new[] { original.Group.Tasks[0].Id }));
        });
    }

    [Test]
    public void Apply_DependencyEdit_PreservesNodesAndSynchronizesValidationBoundary()
    {
        var pendingStore = new PendingDecomposePlanStore(_squad);
        var baseGroup = StagedPlanEditorTests.MakeGroup();
        var firstId = baseGroup.Tasks[0].Id;
        var secondId = baseGroup.Tasks[1].Id;
        var thirdId = baseGroup.Tasks[2].Id;
        var gate = new DecomposedGate(
            baseGroup.GroupId + "-GATE-001",
            "Review transition to downstream tasks.",
            [firstId],
            [secondId],
            Question: "Is the upstream output ready?",
            PresentationAnchor: "stage:1");
        var validation = new DecomposedValidationNode(
            baseGroup.GroupId + "-VAL-001",
            "Verify downstream contract",
            "Ensure the downstream contract remains valid.",
            [firstId],
            [thirdId],
            ["Downstream contract is still valid."],
            Mode: "evidence");
        var group = baseGroup with { ApprovalGates = [gate], Validations = [validation] };
        var pending = pendingStore.Save(group);
        var edited = StagedPlanEditor.UpdateTask(
            pending.Group, thirdId,
            pending.Group.Tasks[2].Title ?? "Third",
            pending.Group.Tasks[2].Description ?? "Third result",
            pending.Group.Tasks[2].Priority,
            [firstId]);
        var recompute = DependencyEditRecomputer.Recompute(edited);
        Assert.That(recompute.Success, Is.True);

        PendingPlanEditor.Apply(
            recompute.RecomputedGroup!,
            pending.CreatedAt!.Value,
            null,
            pendingStore,
            null,
            null);

        var reloaded = pendingStore.Load(group.GroupId)!;
        var reloadedGate = reloaded.Group.ApprovalGates![0];
        var reloadedValidation = reloaded.Group.Validations![0];
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Group.ApprovalGates, Has.Count.EqualTo(1));
            Assert.That(reloaded.Group.Validations, Has.Count.EqualTo(1));
            Assert.That(reloadedGate.GateId, Is.EqualTo(gate.GateId));
            Assert.That(reloadedGate.Message, Is.EqualTo(gate.Message));
            Assert.That(reloadedGate.AfterTaskIds, Is.EqualTo(gate.AfterTaskIds));
            Assert.That(reloadedGate.BeforeTaskIds, Is.EqualTo(gate.BeforeTaskIds));
            Assert.That(reloadedGate.Question, Is.EqualTo(gate.Question));
            Assert.That(reloadedGate.PresentationAnchor, Is.EqualTo($"task-before:{secondId}"));
            Assert.That(reloadedValidation.ValidationId, Is.EqualTo(validation.ValidationId));
            Assert.That(reloadedValidation.Title, Is.EqualTo(validation.Title));
            Assert.That(reloadedValidation.Description, Is.EqualTo(validation.Description));
            Assert.That(reloadedValidation.AfterTaskIds, Is.EqualTo(validation.AfterTaskIds));
            Assert.That(reloadedValidation.BeforeTaskIds, Is.EqualTo(new[] { secondId, thirdId }));
            Assert.That(reloadedValidation.Assertions, Is.EqualTo(validation.Assertions));
            Assert.That(reloadedValidation.Mode, Is.EqualTo(validation.Mode));
        });
    }

    [Test]
    public void Apply_DependencyEdit_DurableSaveReloadMatchesInMemoryState()
    {
        var pendingStore = new PendingDecomposePlanStore(_squad);
        var baseGroup = StagedPlanEditorTests.MakeGroup();
        var firstId = baseGroup.Tasks[0].Id;
        var secondId = baseGroup.Tasks[1].Id;
        var thirdId = baseGroup.Tasks[2].Id;
        var sourceGroup = baseGroup with
        {
            ApprovalGates =
            [
                new DecomposedGate(
                    baseGroup.GroupId + "-GATE-001",
                    "Review data layer before API implementation.",
                    [secondId],
                    [thirdId],
                    Question: "Is the data layer stable?",
                    PresentationAnchor: "stage:2"),
            ],
            Validations =
            [
                new DecomposedValidationNode(
                    baseGroup.GroupId + "-VAL-001",
                    "Validate integration boundary",
                    "Validate the integration hand-off.",
                    [secondId],
                    [thirdId],
                    ["Integration boundary remains valid."],
                    Mode: "evidence"),
            ],
        };
        var pending = pendingStore.Save(sourceGroup);
        var edited = StagedPlanEditor.UpdateTask(
            pending.Group,
            thirdId,
            pending.Group.Tasks[2].Title ?? "Third",
            pending.Group.Tasks[2].Description ?? "Third result",
            pending.Group.Tasks[2].Priority,
            [firstId]);
        var recompute = DependencyEditRecomputer.Recompute(edited);
        Assert.That(recompute.Success, Is.True);
        Assert.That(recompute.RecomputedGroup, Is.Not.Null);

        PendingPlanEditor.Apply(
            recompute.RecomputedGroup!,
            pending.CreatedAt!.Value,
            null,
            pendingStore,
            null,
            null);

        var reloaded = pendingStore.Load(sourceGroup.GroupId);
        Assert.That(reloaded, Is.Not.Null);
        Assert.That(Serialize(reloaded!.Group), Is.EqualTo(Serialize(recompute.RecomputedGroup!)));

        var reloadedRecompute = DependencyEditRecomputer.Recompute(reloaded.Group);
        Assert.That(reloadedRecompute.Success, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reloadedRecompute.StageAssignments, Is.EqualTo(recompute.StageAssignments));
            Assert.That(reloadedRecompute.DisplayLabels, Is.EqualTo(recompute.DisplayLabels));
            Assert.That(reloaded.Group.ApprovalGates![0].PresentationAnchor,
                Is.EqualTo($"task-before:{thirdId}"));
            Assert.That(HasDuplicateGateBoundary(reloaded.Group), Is.False);
        });
    }

    private static IReadOnlyDictionary<string, string> GetDependencyTopology(DecomposedTaskGroup group) =>
        group.Tasks.ToDictionary(
            task => task.Id,
            task => string.Join("|", task.DependsOn.OrderBy(value => value, StringComparer.Ordinal)),
            StringComparer.Ordinal);

    private static bool HasDuplicateGateBoundary(DecomposedTaskGroup group)
    {
        var gates = group.ApprovalGates ?? [];
        var boundaryCount = gates
            .GroupBy(gate => Serialize((gate.AfterTaskIds, gate.BeforeTaskIds)))
            .Count();
        return boundaryCount != gates.Count;
    }

    // PlanViewerWindow's stage-change confirmation currently calls MessageBox.Show directly
    // without an injectable test seam. This helper mirrors the same decision boundary.
    private static bool ApplyDependencyEditThroughConfirmation(Func<bool> shouldApply, Action applyEdit)
    {
        if (!shouldApply())
            return false;

        applyEdit();
        return true;
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value);
}
