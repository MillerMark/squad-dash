namespace SquadDash.Tests;

[TestFixture]
internal sealed class ThinkingTraceDiagnosticsTests
{
    [Test]
    public void DescribeText_EscapesWhitespaceAndCountsEdges()
    {
        var summary = ThinkingTraceDiagnostics.DescribeText("chunk", " \nSince ");

        Assert.Multiple(() =>
        {
            Assert.That(summary, Does.Contain("chunkLen=8"));
            Assert.That(summary, Does.Contain("chunkLeadingWs=2"));
            Assert.That(summary, Does.Contain("chunkTrailingWs=1"));
            Assert.That(summary, Does.Contain("chunkHead=\"[sp]\\nSince[sp]\""));
        });
    }

    [Test]
    public void ShouldTraceAppendBoundary_FlagsSentencePunctuationFollowedByCapital()
    {
        Assert.That(
            ThinkingTraceDiagnostics.ShouldTraceAppendBoundary("Working around it.", "Since node_modules"),
            Is.True);
    }

    [Test]
    public void ShouldTraceAppendBoundary_DoesNotFlagMidWordContinuation()
    {
        Assert.That(
            ThinkingTraceDiagnostics.ShouldTraceAppendBoundary("node_mod", "ules"),
            Is.False);
    }
}
