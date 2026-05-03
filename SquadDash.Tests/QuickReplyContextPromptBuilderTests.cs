namespace SquadDash.Tests;

[TestFixture]
internal sealed class QuickReplyContextPromptBuilderTests
{
    [Test]
    public void BuildHandoffContext_IncludesBoundedTranscriptAndAgentContext()
    {
        var context = QuickReplyContextPromptBuilder.BuildHandoffContext(
            "Run Vesper Knox for verification",
            "Vesper Knox",
            "start_named_agent",
            "vesper-knox",
            [
                new QuickReplyHandoffTurnContext(
                    "Coordinator",
                    "Make screenshot capture replace only the selected placeholder.",
                    "Arjun is adding fixtures.",
                    new DateTimeOffset(2026, 5, 1, 19, 0, 0, TimeSpan.Zero),
                    IsSourceTurn: false),
                new QuickReplyHandoffTurnContext(
                    "Coordinator",
                    "Use the fixture path for capture.",
                    """
                    Combined with Arjun's fixtures, the full scenario now works:
                    1. Right-click a placeholder in the Tasks docs -> "Capture image"
                    2. SquadDash applies the tasks-panel-populated fixture
                    Both branches should now build. Would you like Vesper Knox to run verification?
                    QUICK_REPLIES_JSON:
                    [
                      { "label": "Run Vesper Knox for verification", "routeMode": "start_named_agent", "targetAgent": "vesper-knox" }
                    ]
                    """,
                    new DateTimeOffset(2026, 5, 1, 20, 0, 0, TimeSpan.Zero),
                    IsSourceTurn: true)
            ],
            [
                new QuickReplyHandoffAgentContext(
                    "Arjun Sen",
                    "Create fixtures for screenshot capture.",
                    "Added tasks-panel-populated fixture.",
                    ["Fixture load completed"],
                    new DateTimeOffset(2026, 5, 1, 19, 45, 0, TimeSpan.Zero))
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("Run Vesper Knox for verification"));
            Assert.That(context, Does.Contain("Make screenshot capture replace only the selected placeholder."));
            Assert.That(context, Does.Contain("Combined with Arjun's fixtures"));
            Assert.That(context, Does.Contain("Arjun Sen"));
            Assert.That(context, Does.Contain("Added tasks-panel-populated fixture."));
            Assert.That(context, Does.Not.Contain("QUICK_REPLIES_JSON"));
            Assert.That(context, Does.Not.Contain("Do not broaden"));
            Assert.That(context, Does.Contain("authoritative task scope"));
        });
    }

    [Test]
    public void BuildHandoffContext_StripsSystemNotifications()
    {
        var context = QuickReplyContextPromptBuilder.BuildHandoffContext(
            "Ask Orion to review this architecture",
            "Orion Vale",
            "start_named_agent",
            "orion-vale",
            [
                new QuickReplyHandoffTurnContext(
                    "Coordinator",
                    "Review the docs panel architecture.",
                    "<system_notification>{\"notification\":\"done\"}</system_notification>\nReady for Orion?",
                    new DateTimeOffset(2026, 5, 1, 20, 0, 0, TimeSpan.Zero),
                    IsSourceTurn: true)
            ],
            []);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("Ready for Orion?"));
            Assert.That(context, Does.Not.Contain("system_notification"));
        });
    }

    [Test]
    public void BuildHandoffContext_PreservesConcreteSourceTurnScope()
    {
        var context = QuickReplyContextPromptBuilder.BuildHandoffContext(
            "Sorin - implement the 3 optimizations",
            "Sorin Pyre",
            "start_named_agent",
            "sorin-pyre",
            [
                new QuickReplyHandoffTurnContext(
                    "Coordinator",
                    "did the fixes (optimizations) get implemented?",
                    """
                    No - only Sorin's first pass landed. The three actual optimizations identified from trace data were not implemented:

                    1. ResolveSquadVersionAsync (1.5s) - cache or fire-and-forget so it doesn't hold up Loaded
                    2. OpenWorkspace (670ms) - profile what's slow inside it
                    3. Mutex timeout on settings save during shutdown - silent data-loss bug
                    """,
                    new DateTimeOffset(2026, 5, 3, 5, 23, 13, TimeSpan.Zero),
                    IsSourceTurn: true)
            ],
            []);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("Sorin - implement the 3 optimizations"));
            Assert.That(context, Does.Contain("ResolveSquadVersionAsync (1.5s)"));
            Assert.That(context, Does.Contain("OpenWorkspace (670ms)"));
            Assert.That(context, Does.Contain("Mutex timeout on settings save"));
            Assert.That(context, Does.Contain("authoritative task scope"));
        });
    }
}
