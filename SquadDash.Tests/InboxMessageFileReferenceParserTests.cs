namespace SquadDash.Tests;

[TestFixture]
internal sealed class InboxMessageFileReferenceParserTests
{
    [Test]
    public void TryExtract_ValidReference_ReturnsVisibleTextAroundBlock()
    {
        const string text = """
            Report stored in inbox.

            INBOX_MESSAGE_JSON_FILE:
            { "path": ".squad/tmp/agent-artifacts/inbox.json", "sha256": "abc123" }

            trailing note
            """;

        var result = InboxMessageFileReferenceParser.TryExtract(text, out var extraction);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(extraction, Is.Not.Null);
            Assert.That(extraction!.Reference.Path, Is.EqualTo(".squad/tmp/agent-artifacts/inbox.json"));
            Assert.That(extraction.Reference.Sha256, Is.EqualTo("abc123"));
            Assert.That(extraction.VisibleText, Is.EqualTo("""
                Report stored in inbox.

                trailing note
                """));
        });
    }

    [Test]
    public void ExtractAll_TwoReferences_ReturnsBothInSourceOrderAndStripsBoth()
    {
        const string text = """
            Reports stored in inbox.

            INBOX_MESSAGE_JSON_FILE:
            { "path": ".squad/tmp/agent-artifacts/lyra.json" }

            INBOX_MESSAGE_JSON_FILE:
            { "path": ".squad/tmp/agent-artifacts/vesper.json" }
            """;

        var extractions = InboxMessageFileReferenceParser.ExtractAll(text, out var visibleText);

        Assert.Multiple(() =>
        {
            Assert.That(extractions.Select(item => item.Reference.Path), Is.EqualTo(new[]
            {
                ".squad/tmp/agent-artifacts/lyra.json",
                ".squad/tmp/agent-artifacts/vesper.json"
            }));
            Assert.That(visibleText, Is.EqualTo("Reports stored in inbox."));
        });
    }
}
