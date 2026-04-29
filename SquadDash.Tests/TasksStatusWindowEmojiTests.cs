using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class TasksStatusWindowEmojiTests {

    // ── Known priority emoji → correct resource key ───────────────────────

    [TestCase("🔴", "TaskPriorityHigh")]
    [TestCase("🟡", "TaskPriorityMid")]
    [TestCase("🟢", "TaskPriorityLow")]
    public void EmojiResourceKey_KnownEmoji_ReturnsCorrectResourceKey(string emoji, string expectedKey) {
        var result = TasksStatusWindow.EmojiResourceKey(emoji);
        Assert.That(result, Is.EqualTo(expectedKey));
    }

    // ── Non-priority segments → null ──────────────────────────────────────

    [TestCase("high")]
    [TestCase("- [ ] Task")]
    [TestCase("")]
    [TestCase("🟠")] // orange — not in the priority set
    public void EmojiResourceKey_NonPrioritySegment_ReturnsNull(string segment) {
        var result = TasksStatusWindow.EmojiResourceKey(segment);
        Assert.That(result, Is.Null);
    }
}
