namespace SquadDash.Tests;

[TestFixture]
internal sealed class SessionResumeDiagnosticsPresentationTests {
    [Test]
    public void BuildSummary_IncludesResumePathAgePromptCountAndContext() {
        var evt = new SquadSdkEvent {
            Type = "session_ready",
            SessionReuseKind = "provider_resume",
            SessionAcquireDurationMs = 1250,
            SessionResumeDurationMs = 1180,
            SessionAgeMs = 3723000,
            SessionPromptCountBeforeCurrent = 8,
            SessionPromptCountIncludingCurrent = 9,
            CachedAssistantChars = 2048,
            BackgroundAgentCount = 0,
            BackgroundShellCount = 1,
            KnownSubagentCount = 2,
            ActiveToolCount = 0,
            RestoredContextSummary = "messageCount=42, summaryCount=3"
        };

        var summary = SessionResumeDiagnosticsPresentation.BuildSummary(evt);

        Assert.That(summary, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(summary, Does.Contain("path provider resume"));
            Assert.That(summary, Does.Contain("acquire"));
            Assert.That(summary, Does.Contain("provider resume"));
            Assert.That(summary, Does.Contain("prompt #9 (8 prior)"));
            Assert.That(summary, Does.Contain("cached assistant 2048 chars"));
            Assert.That(summary, Does.Contain("bridge state 0 bg agents, 1 bg shells, 2 known subagents, 0 active tools"));
            Assert.That(summary, Does.Contain("restored context messageCount=42, summaryCount=3"));
        });
    }

    [Test]
    public void BuildSummary_ReturnsNull_WhenNoDiagnosticsExist() {
        var summary = SessionResumeDiagnosticsPresentation.BuildSummary(new SquadSdkEvent { Type = "session_ready" });

        Assert.That(summary, Is.Null);
    }
}
