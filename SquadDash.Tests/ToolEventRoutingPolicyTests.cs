namespace SquadDash.Tests;

[TestFixture]
internal sealed class ToolEventRoutingPolicyTests
{
    [Test]
    public void Resolve_LateCompletionUsesRegisteredEntryAfterTurnClosed()
    {
        var result = ToolEventRoutingPolicy.Resolve(
            hasToolCallId: true,
            hasExistingEntry: true,
            hasActiveTurn: false);

        Assert.That(result, Is.EqualTo(ToolEventRoutingDecision.UseExistingEntry));
    }

    [TestCase(false, false, false, ToolEventRoutingDecision.Ignore)]
    [TestCase(true, false, false, ToolEventRoutingDecision.Ignore)]
    [TestCase(true, false, true, ToolEventRoutingDecision.CreateEntry)]
    [TestCase(true, true, true, ToolEventRoutingDecision.UseExistingEntry)]
    public void Resolve_RequiresAnActiveTurnOnlyWhenCreatingANewEntry(
        bool hasToolCallId,
        bool hasExistingEntry,
        bool hasActiveTurn,
        ToolEventRoutingDecision expected)
    {
        Assert.That(
            ToolEventRoutingPolicy.Resolve(hasToolCallId, hasExistingEntry, hasActiveTurn),
            Is.EqualTo(expected));
    }
}
