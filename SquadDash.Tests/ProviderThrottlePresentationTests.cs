using System.Threading;
using System.Windows.Documents;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ProviderThrottlePresentationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    [TestCase(3001, "Throttled 4s")]
    [TestCase(3000, "Throttled 3s")]
    [TestCase(1, "Throttled 1s")]
    [TestCase(0, "Retrying…")]
    [TestCase(-1, "Retrying…")]
    public void BuildCardStatus_CountsDownUsingCeilingMilliseconds(
        int remainingMilliseconds,
        string expected)
    {
        Assert.That(
            ProviderThrottlePresentation.BuildCardStatus(
                Now.AddMilliseconds(remainingMilliseconds),
                Now),
            Is.EqualTo(expected));
    }

    [Test]
    public void BuildCardStatus_WithoutDeadline_RemainsTruthful()
    {
        Assert.That(
            ProviderThrottlePresentation.BuildCardStatus(null, Now),
            Is.EqualTo("Throttled"));
    }

    [Test]
    public void ResolveRetryAt_UsesEventDeadlineWhenPresent()
    {
        var expected = Now.AddSeconds(47);

        Assert.That(
            ProviderThrottlePresentation.ResolveRetryAt(
                expected.ToString("O"),
                30_000,
                Now),
            Is.EqualTo(expected));
    }

    [Test]
    public void ResolveRetryAt_FallsBackToObservedDelay()
    {
        Assert.That(
            ProviderThrottlePresentation.ResolveRetryAt(null, 30_500, Now),
            Is.EqualTo(Now.AddMilliseconds(30_500)));
    }

    [TestCase(30_001, "Provider reported a rate limit. Throttled; retrying in 31s.")]
    [TestCase(30_000, "Provider reported a rate limit. Throttled; retrying in 30s.")]
    [TestCase(null, "Provider reported a rate limit. Throttled; waiting before retrying.")]
    public void BuildTranscriptNotice_ReportsTheActualWait(int? retryDelayMs, string expected)
    {
        Assert.That(
            ProviderThrottlePresentation.BuildTranscriptNotice(retryDelayMs),
            Is.EqualTo(expected));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TranscriptNotice_IsHostMetadataAndDoesNotMutateAgentResponse()
    {
        var thread = new TranscriptThreadState(
            "agent-1",
            TranscriptThreadKind.Agent,
            "Lyra Morn",
            Now);
        var narrative = new Section();
        thread.Document.Blocks.Add(narrative);
        var turn = new TranscriptTurnView(thread, "prompt", Now, narrative, [narrative]);
        thread.CurrentTurn = turn;

        ProviderThrottleTranscriptBlockFactory.Append(
            thread,
            "Provider reported a rate limit. Throttled; retrying in 30s.",
            Now,
            3);

        Assert.Multiple(() =>
        {
            Assert.That(turn.ResponseTextBuilder.Length, Is.Zero);
            Assert.That(turn.ResponseEntries, Is.Empty);
            Assert.That(turn.HostNotices, Has.Count.EqualTo(1));
            Assert.That(turn.HostNotices[0].Sequence, Is.EqualTo(3));
            Assert.That(narrative.Blocks.LastBlock, Is.TypeOf<BlockUIContainer>());
        });
    }
}
