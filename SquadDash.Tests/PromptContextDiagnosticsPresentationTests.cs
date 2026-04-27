namespace SquadDash.Tests;

[TestFixture]
internal sealed class PromptContextDiagnosticsPresentationTests {
    [Test]
    public void BuildTraceSummary_EmitsHighRiskBandForLargeOldTranscript() {
        var diagnostics = new PromptContextDiagnostics(
            SessionId: "session-9",
            SessionUpdatedAt: new DateTimeOffset(2026, 4, 22, 17, 0, 0, TimeSpan.Zero),
            TranscriptStartedAt: new DateTimeOffset(2026, 4, 22, 15, 0, 0, TimeSpan.Zero),
            CoordinatorTurnCount: 18,
            AgentThreadCount: 3,
            AgentThreadTurnCount: 14,
            PromptHistoryCount: 22,
            RecentSessionCount: 4,
            CoordinatorPromptChars: 9_000,
            CoordinatorResponseChars: 31_000,
            CoordinatorThinkingChars: 18_000,
            AgentPromptChars: 8_000,
            AgentResponseChars: 42_000,
            AgentThinkingChars: 16_000);

        var summary = PromptContextDiagnosticsPresentation.BuildTraceSummary(
            diagnostics,
            new DateTimeOffset(2026, 4, 22, 17, 30, 0, TimeSpan.Zero));

        Assert.Multiple(() => {
            Assert.That(summary, Does.Contain("riskBand=high"));
            Assert.That(summary, Does.Contain("totalChars=124000"));
            Assert.That(summary, Does.Contain("transcriptAgeMs="));
        });
    }
}
