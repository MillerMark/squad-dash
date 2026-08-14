using System;
using System.IO;

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
}
