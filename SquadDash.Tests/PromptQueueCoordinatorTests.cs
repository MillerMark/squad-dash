using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PromptQueueCoordinatorTests
{
    // ── Constructor null guard ────────────────────────────────────────────────

    [Test]
    public void Constructor_NullQueue_ThrowsArgumentNullException()
    {
        Assert.That(() => new PromptQueueCoordinator(null!),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("promptQueue"));
    }

    [Test]
    public void Constructor_ValidQueue_ExposesQueueProperty()
    {
        var queue = new PromptQueue();
        var coordinator = new PromptQueueCoordinator(queue);
        Assert.That(coordinator.Queue, Is.SameAs(queue));
    }

    // ── Sequence-number helpers ───────────────────────────────────────────────

    [Test]
    public void NextSequenceNumber_StartsAtOne_AndIncrements()
    {
        var coordinator = new PromptQueueCoordinator(new PromptQueue());

        Assert.That(coordinator.NextSequenceNumber(), Is.EqualTo(1));
        Assert.That(coordinator.NextSequenceNumber(), Is.EqualTo(2));
        Assert.That(coordinator.NextSequenceNumber(), Is.EqualTo(3));
    }

    [Test]
    public void ResetSequenceNumber_ResetsCounterToZero()
    {
        var coordinator = new PromptQueueCoordinator(new PromptQueue());
        coordinator.NextSequenceNumber();
        coordinator.NextSequenceNumber();

        coordinator.ResetSequenceNumber();

        Assert.That(coordinator.NextSequenceNumber(), Is.EqualTo(1));
    }

    // ── Enqueue / dequeue via exposed Queue property ──────────────────────────

    [Test]
    public void Queue_Enqueue_AddsItemAndReflectsInCount()
    {
        var coordinator = new PromptQueueCoordinator(new PromptQueue());
        coordinator.Queue.Enqueue("hello", coordinator.NextSequenceNumber());

        Assert.That(coordinator.Queue.Count, Is.EqualTo(1));
        Assert.That(coordinator.Queue.Items[0].Text, Is.EqualTo("hello"));
        Assert.That(coordinator.Queue.Items[0].SequenceNumber, Is.EqualTo(1));
    }

    [Test]
    public void Queue_DequeueFirstReady_RemovesAndReturnsItem()
    {
        var coordinator = new PromptQueueCoordinator(new PromptQueue());
        coordinator.Queue.Enqueue("first",  coordinator.NextSequenceNumber());
        coordinator.Queue.Enqueue("second", coordinator.NextSequenceNumber());

        var item = coordinator.Queue.DequeueFirstReady();

        Assert.That(item,       Is.Not.Null);
        Assert.That(item!.Text, Is.EqualTo("first"));
        Assert.That(coordinator.Queue.Count, Is.EqualTo(1));
    }

    // ── BranchIndicatorUpdateRequested event wiring ───────────────────────────

    [Test]
    public void BranchIndicatorUpdateRequested_FiredWhenBranchIndicatorItemRemoved()
    {
        var coordinator = new PromptQueueCoordinator(new PromptQueue());
        coordinator.Queue.Enqueue("merge main", 1, sourceTag: "branch-indicator");

        var raised = 0;
        coordinator.BranchIndicatorUpdateRequested += () => raised++;

        coordinator.Queue.Remove(coordinator.Queue.Items[0].Id);

        Assert.That(raised, Is.EqualTo(1));
    }

    [Test]
    public void BranchIndicatorUpdateRequested_NotFiredForOtherSourceTags()
    {
        var coordinator = new PromptQueueCoordinator(new PromptQueue());
        coordinator.Queue.Enqueue("do something", 1, sourceTag: "some-other-tag");

        var raised = 0;
        coordinator.BranchIndicatorUpdateRequested += () => raised++;

        coordinator.Queue.Remove(coordinator.Queue.Items[0].Id);

        Assert.That(raised, Is.EqualTo(0));
    }

    [Test]
    public void BranchIndicatorUpdateRequested_NotFiredForItemWithNoSourceTag()
    {
        var coordinator = new PromptQueueCoordinator(new PromptQueue());
        coordinator.Queue.Enqueue("plain prompt", 1);

        var raised = 0;
        coordinator.BranchIndicatorUpdateRequested += () => raised++;

        coordinator.Queue.Remove(coordinator.Queue.Items[0].Id);

        Assert.That(raised, Is.EqualTo(0));
    }

    [Test]
    public void BranchIndicatorUpdateRequested_FiredForEachBranchIndicatorItemRemoved()
    {
        var coordinator = new PromptQueueCoordinator(new PromptQueue());
        coordinator.Queue.Enqueue("item 1", 1, sourceTag: "branch-indicator");
        coordinator.Queue.Enqueue("item 2", 2, sourceTag: "branch-indicator");

        var raised = 0;
        coordinator.BranchIndicatorUpdateRequested += () => raised++;

        coordinator.Queue.RemoveByTag("branch-indicator");

        Assert.That(raised, Is.EqualTo(2));
    }
}
