using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PromptQueueTests {

    // ── Enqueue / Count ───────────────────────────────────────────────────────

    [Test]
    public void Enqueue_AddsItemWithCorrectTextAndSequenceNumber() {
        var queue = new PromptQueue();
        queue.Enqueue("hello", 1);

        Assert.That(queue.Count,          Is.EqualTo(1));
        Assert.That(queue.Items[0].Text,  Is.EqualTo("hello"));
        Assert.That(queue.Items[0].SequenceNumber, Is.EqualTo(1));
    }

    [Test]
    public void Enqueue_MultipleItems_PreservesOrder() {
        var queue = new PromptQueue();
        queue.Enqueue("first",  1);
        queue.Enqueue("second", 2);
        queue.Enqueue("third",  3);

        Assert.That(queue.Count, Is.EqualTo(3));
        Assert.That(queue.Items[0].Text, Is.EqualTo("first"));
        Assert.That(queue.Items[1].Text, Is.EqualTo("second"));
        Assert.That(queue.Items[2].Text, Is.EqualTo("third"));
    }

    // ── DequeueFirstReady ─────────────────────────────────────────────────────

    [Test]
    public void DequeueFirstReady_EmptyQueue_ReturnsNull() {
        var queue = new PromptQueue();
        Assert.That(queue.DequeueFirstReady(), Is.Null);
    }

    [Test]
    public void DequeueFirstReady_ReturnsFirstItem_AndRemovesIt() {
        var queue = new PromptQueue();
        queue.Enqueue("first",  1);
        queue.Enqueue("second", 2);

        var item = queue.DequeueFirstReady();

        Assert.That(item,        Is.Not.Null);
        Assert.That(item!.Text,  Is.EqualTo("first"));
        Assert.That(queue.Count, Is.EqualTo(1));
        Assert.That(queue.Items[0].Text, Is.EqualTo("second"));
    }

    [Test]
    public void DequeueFirstReady_SkipsEditingItems() {
        var queue = new PromptQueue();
        queue.Enqueue("first",  1);
        queue.Enqueue("second", 2);
        queue.Items[0].IsEditing = true;

        var item = queue.DequeueFirstReady();

        Assert.That(item!.Text,  Is.EqualTo("second"));
        Assert.That(queue.Count, Is.EqualTo(1),  "editing item should remain");
        Assert.That(queue.Items[0].Text, Is.EqualTo("first"));
    }

    [Test]
    public void DequeueFirstReady_AllEditing_ReturnsNull() {
        var queue = new PromptQueue();
        queue.Enqueue("first", 1);
        queue.Items[0].IsEditing = true;

        Assert.That(queue.DequeueFirstReady(), Is.Null);
        Assert.That(queue.Count, Is.EqualTo(1));
    }

    // ── Remove ────────────────────────────────────────────────────────────────

    [Test]
    public void Remove_ExistingId_RemovesItem() {
        var queue = new PromptQueue();
        queue.Enqueue("hello", 1);
        var id = queue.Items[0].Id;

        queue.Remove(id);

        Assert.That(queue.Count, Is.EqualTo(0));
    }

    [Test]
    public void Remove_UnknownId_DoesNotThrow() {
        var queue = new PromptQueue();
        queue.Enqueue("hello", 1);

        Assert.DoesNotThrow(() => queue.Remove("nonexistent-id"));
        Assert.That(queue.Count, Is.EqualTo(1));
    }

    // ── HasReadyItems ─────────────────────────────────────────────────────────

    [Test]
    public void HasReadyItems_EmptyQueue_ReturnsFalse() {
        var queue = new PromptQueue();
        Assert.That(queue.HasReadyItems, Is.False);
    }

    [Test]
    public void HasReadyItems_AllEditing_ReturnsFalse() {
        var queue = new PromptQueue();
        queue.Enqueue("x", 1);
        queue.Items[0].IsEditing = true;

        Assert.That(queue.HasReadyItems, Is.False);
    }

    [Test]
    public void HasReadyItems_AtLeastOneReady_ReturnsTrue() {
        var queue = new PromptQueue();
        queue.Enqueue("first",  1);
        queue.Enqueue("second", 2);
        queue.Items[0].IsEditing = true;

        Assert.That(queue.HasReadyItems, Is.True);
    }

    // ── FIFO drain order ──────────────────────────────────────────────────────

    [Test]
    public void DequeueFirstReady_DrainsFifo() {
        var queue = new PromptQueue();
        queue.Enqueue("a", 1);
        queue.Enqueue("b", 2);
        queue.Enqueue("c", 3);

        Assert.That(queue.DequeueFirstReady()!.Text, Is.EqualTo("a"));
        Assert.That(queue.DequeueFirstReady()!.Text, Is.EqualTo("b"));
        Assert.That(queue.DequeueFirstReady()!.Text, Is.EqualTo("c"));
        Assert.That(queue.DequeueFirstReady(),        Is.Null);
    }

    // ── IsDictated flag ───────────────────────────────────────────────────────

    [Test]
    public void Enqueue_IsDictated_DefaultsToFalse() {
        var queue = new PromptQueue();
        queue.Enqueue("hello", 1);

        Assert.That(queue.Items[0].IsDictated, Is.False);
    }

    [Test]
    public void Enqueue_WithIsDictated_True_SetsFlag() {
        var queue = new PromptQueue();
        queue.Enqueue("dictated text", 1, isDictated: true);

        Assert.That(queue.Items[0].IsDictated, Is.True);
    }

    [Test]
    public void DequeueFirstReady_PreservesIsDictatedFlag() {
        var queue = new PromptQueue();
        queue.Enqueue("typed",    1, isDictated: false);
        queue.Enqueue("dictated", 2, isDictated: true);

        var first  = queue.DequeueFirstReady();
        var second = queue.DequeueFirstReady();

        Assert.That(first!.IsDictated,  Is.False);
        Assert.That(second!.IsDictated, Is.True);
    }

    // ── Items is read-only view ───────────────────────────────────────────────

    [Test]
    public void Items_ReflectsCurrentState_AfterDequeue() {
        var queue = new PromptQueue();
        queue.Enqueue("x", 1);
        queue.Enqueue("y", 2);

        queue.DequeueFirstReady();

        Assert.That(queue.Items.Count,       Is.EqualTo(1));
        Assert.That(queue.Items[0].Text, Is.EqualTo("y"));
    }
}
