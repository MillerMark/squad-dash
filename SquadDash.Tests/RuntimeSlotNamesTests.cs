namespace SquadDash.Tests;

[TestFixture]
internal sealed class RuntimeSlotNamesTests {
    // ── Toggle ─────────────────────────────────────────────────────────────

    [TestCase("A", ExpectedResult = "B")]
    [TestCase("B", ExpectedResult = "A")]
    public string Toggle_KnownSlot_ReturnsOppositeSlot(string activeSlot) {
        return RuntimeSlotNames.Toggle(activeSlot);
    }

    [Test]
    public void Toggle_Null_ReturnsDefaultSlot() {
        var result = RuntimeSlotNames.Toggle(null);

        Assert.That(result, Is.EqualTo(RuntimeSlotNames.SlotA));
    }

    [Test]
    public void Toggle_UnexpectedValue_ReturnsDefaultSlot() {
        var result = RuntimeSlotNames.Toggle("C");

        Assert.That(result, Is.EqualTo(RuntimeSlotNames.SlotA));
    }

    // ── Normalize ──────────────────────────────────────────────────────────

    [TestCase("A", ExpectedResult = "A")]
    [TestCase("a", ExpectedResult = "A")]
    [TestCase("B", ExpectedResult = "B")]
    [TestCase("b", ExpectedResult = "B")]
    public string Normalize_KnownSlot_ReturnsUppercaseCanonicalName(string slotName) {
        return RuntimeSlotNames.Normalize(slotName);
    }

    [Test]
    public void Normalize_Null_ReturnsDefaultSlot() {
        var result = RuntimeSlotNames.Normalize(null);

        Assert.That(result, Is.EqualTo(RuntimeSlotNames.SlotA));
    }

    [Test]
    public void Normalize_UnexpectedValue_ReturnsDefaultSlot() {
        var result = RuntimeSlotNames.Normalize("C");

        Assert.That(result, Is.EqualTo(RuntimeSlotNames.SlotA));
    }
}
