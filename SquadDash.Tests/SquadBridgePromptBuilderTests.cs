namespace SquadDash.Tests;

[TestFixture]
internal sealed class SquadBridgePromptBuilderTests {
    [Test]
    public void Build_AppendsQuickReplyInstructionAndRoutingInstruction() {
        using var workspace = new TestWorkspace();

        var built = SquadBridgePromptBuilder.Build(
            "Review the docs",
            "Quick replies enabled.",
            "Continue with Mira Quill.",
            null,
            null,
            workspace.RootPath).PromptText;

        Assert.That(built, Does.Contain("Continue with Mira Quill."));
        Assert.That(built, Does.EndWith("Quick replies enabled."));
    }

    [Test]
    public void Build_CanAppendStructuredQuickReplyInstruction() {
        using var workspace = new TestWorkspace();

        var built = SquadBridgePromptBuilder.Build(
            "Review the docs",
            "QUICK_REPLIES_JSON:",
            null,
            null,
            null,
            workspace.RootPath).PromptText;

        Assert.That(built, Does.EndWith("QUICK_REPLIES_JSON:"));
    }

    [Test]
    public void Build_AddsCustomUniverseHiringContextForHiringPrompts() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/universes/squaddash.md", "# SquadDash Universe");

        var built = SquadBridgePromptBuilder.Build(
            "Please hire a lead architect for this investigation.",
            "Quick replies enabled.",
            null,
            null,
            null,
            workspace.RootPath).PromptText;

        Assert.That(built, Does.Contain(".squad/universes/squaddash.md"));
        Assert.That(built, Does.Contain(".squad/casting/policy.json"));
    }

    [Test]
    public void Build_DoesNotAddCustomUniverseHiringContextForNormalPrompts() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/universes/squaddash.md", "# SquadDash Universe");

        var built = SquadBridgePromptBuilder.Build(
            "Sort the transcript headers by recency.",
            "Quick replies enabled.",
            null,
            null,
            null,
            workspace.RootPath).PromptText;

        Assert.That(built, Does.Not.Contain(".squad/universes/squaddash.md"));
    }

    [Test]
    public void Build_DoesNotAddCustomUniverseHiringContextForHireAgentWindowUiPrompt() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/universes/squaddash.md", "# SquadDash Universe");

        var built = SquadBridgePromptBuilder.Build(
            "Hey, in the Hire a new agent window there's a black border around the inner panel under the light theme.",
            "Quick replies enabled.",
            null,
            null,
            null,
            workspace.RootPath).PromptText;

        Assert.That(built, Does.Not.Contain(".squad/universes/squaddash.md"));
        Assert.That(built, Does.Not.Contain(".squad/casting/policy.json"));
    }

    [Test]
    public void Build_AddsCustomUniverseHiringContextForCanWeGetAnotherAgentPrompt() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/universes/squaddash.md", "# SquadDash Universe");

        var built = SquadBridgePromptBuilder.Build(
            "Can we get another agent to focus on merge conflicts?",
            "Quick replies enabled.",
            null,
            null,
            null,
            workspace.RootPath).PromptText;

        Assert.That(built, Does.Contain(".squad/universes/squaddash.md"));
        Assert.That(built, Does.Contain(".squad/casting/policy.json"));
    }

    [Test]
    public void Build_UsesSupplementalInstruction_WhenVisiblePromptIsHidden() {
        using var workspace = new TestWorkspace();

        var built = SquadBridgePromptBuilder.Build(
            string.Empty,
            "Quick replies enabled.",
            null,
            null,
            "Repair `.squad/routing.md` without delegating.",
            workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(built.PromptText, Does.Contain("Repair `.squad/routing.md` without delegating."));
            Assert.That(built.PromptText, Does.EndWith("Quick replies enabled."));
            Assert.That(built.PromptText, Does.Not.StartWith(Environment.NewLine));
        });
    }

    [Test]
    public void Build_AddsGenericRoutingGuidance_WhenTeamAndRoutingFilesExist() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/team.md", """
            # Squad Team

            ## Members

            | Name | Role | Charter | Status |
            |------|------|---------|--------|
            | Lyra Morn | UI Specialist | agents/lyra-morn/charter.md | active |
            """);
        workspace.CreateFile(".squad/routing.md", """
            # Work Routing

            ## Routing Table

            | Work Type | Route To | Examples |
            |-----------|----------|----------|
            | UI | Lyra Morn | `MainWindow.xaml` |
            """);

        var built = SquadBridgePromptBuilder.Build(
            "Please update the transcript layout.",
            "Quick replies enabled.",
            null,
            null,
            null,
            workspace.RootPath);

        Assert.That(built.PromptText, Does.Contain(".squad/team.md"));
        Assert.That(built.PromptText, Does.Contain(".squad/routing.md"));
        Assert.That(built.PromptText, Does.Contain("When you name owners in plans, delegated follow-up work, backlog breakdowns, reviews, or recommendations"));
        Assert.That(built.PromptText, Does.Contain("keep testing, QA, verification, and coverage work with the testing owner"));
        Assert.That(built.RoutingSummary, Does.Contain("generic"));
    }

    [Test]
    public void Build_AddsExplicitMentionRouting_WhenPromptMentionsKnownAgentHandle() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/team.md", """
            # Squad Team

            ## Members

            | Name | Role | Charter | Status |
            |------|------|---------|--------|
            | Lyra Morn | UI Specialist | agents/lyra-morn/charter.md | active |
            """);
        workspace.CreateFile(".squad/routing.md", """
            # Work Routing

            ## Routing Table

            | Work Type | Route To | Examples |
            |-----------|----------|----------|
            | UI | Lyra Morn | `MainWindow.xaml` |
            """);

        var built = SquadBridgePromptBuilder.Build(
            "@lyra-morn please update the transcript layout.",
            "Quick replies enabled.",
            null,
            null,
            null,
            workspace.RootPath);

        Assert.That(built.PromptText, Does.Contain("The user explicitly addressed @lyra-morn."));
        Assert.That(built.PromptText, Does.Contain("Route this request to Lyra Morn"));
        Assert.That(built.RoutingSummary, Does.Contain("explicit-mention"));
    }

    [Test]
    public void Build_AddsStrongRoutingHint_WhenPromptContainsUniqueOwnershipToken() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/team.md", """
            # Squad Team

            ## Members

            | Name | Role | Charter | Status |
            |------|------|---------|--------|
            | Lyra Morn | UI Specialist | agents/lyra-morn/charter.md | active |
            | Arjun Sen | Backend Specialist | agents/arjun-sen/charter.md | active |
            """);
        workspace.CreateFile(".squad/routing.md", """
            # Work Routing

            ## Routing Table

            | Work Type | Route To | Examples |
            |-----------|----------|----------|
            | UI | Lyra Morn | `MainWindow.xaml`, transcript rendering |
            | Backend | Arjun Sen | `WorkspaceConversationStore` |
            """);
        workspace.CreateFile(".squad/agents/lyra-morn/charter.md", """
            # Lyra Morn — UI Specialist

            ## Responsibilities

            - Own `MainWindow.xaml` and `MainWindow.xaml.cs`
            """);
        workspace.CreateFile(".squad/agents/arjun-sen/charter.md", """
            # Arjun Sen — Backend Specialist

            ## Responsibilities

            - Own `WorkspaceConversationStore`
            """);

        var built = SquadBridgePromptBuilder.Build(
            "Please update MainWindow.xaml to adjust the transcript chrome.",
            "Quick replies enabled.",
            null,
            null,
            null,
            workspace.RootPath);

        Assert.That(built.PromptText, Does.Contain("direct ownership clues for Lyra Morn"));
        Assert.That(built.PromptText, Does.Contain("`MainWindow.xaml`"));
        Assert.That(built.RoutingSummary, Does.Contain("strong-match"));
    }

    [Test]
    public void Build_DoesNotAddStrongRoutingHint_WhenQuickReplyAlreadyStartsNamedAgent() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/team.md", """
            # Squad Team

            ## Members

            | Name | Role | Charter | Status |
            |------|------|---------|--------|
            | Lyra Morn | UI Specialist | agents/lyra-morn/charter.md | active |
            | Vesper Knox | Testing Specialist | agents/vesper-knox/charter.md | active |
            """);
        workspace.CreateFile(".squad/routing.md", """
            # Work Routing

            ## Routing Table

            | Work Type | Route To | Examples |
            |-----------|----------|----------|
            | UI | Lyra Morn | `MainWindow.xaml`, transcript rendering |
            | Testing | Vesper Knox | `SquadDash.Tests/`, coverage |
            """);
        workspace.CreateFile(".squad/agents/lyra-morn/charter.md", """
            # Lyra Morn
            - Own `MainWindow.xaml`
            """);
        workspace.CreateFile(".squad/agents/vesper-knox/charter.md", """
            # Vesper Knox
            - Coordinate with Lyra Morn on coverage for new UI features
            """);

        var built = SquadBridgePromptBuilder.Build(
            "Implement it - hand to Lyra Morn",
            "Quick replies enabled.",
            null,
            "start_named_agent",
            "@lyra-morn Take ownership now.",
            workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(built.PromptText, Does.Not.Contain("direct ownership clues for"));
            Assert.That(built.RoutingSummary, Does.Contain("quick-reply-start-named-agent"));
            Assert.That(built.PromptText, Does.Contain("@lyra-morn Take ownership now."));
        });
    }

    [Test]
    public void Build_IgnoresTeammateNamesFromCharterProse_WhenMatchingOwnershipSignals() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/team.md", """
            # Squad Team

            ## Members

            | Name | Role | Charter | Status |
            |------|------|---------|--------|
            | Lyra Morn | UI Specialist | agents/lyra-morn/charter.md | active |
            | Vesper Knox | Testing Specialist | agents/vesper-knox/charter.md | active |
            """);
        workspace.CreateFile(".squad/routing.md", """
            # Work Routing

            ## Routing Table

            | Work Type | Route To | Examples |
            |-----------|----------|----------|
            | UI | Lyra Morn | `MainWindow.xaml`, transcript rendering |
            | Testing | Vesper Knox | `SquadDash.Tests/`, coverage |
            """);
        workspace.CreateFile(".squad/agents/lyra-morn/charter.md", """
            # Lyra Morn
            - Own `MainWindow.xaml`
            """);
        workspace.CreateFile(".squad/agents/vesper-knox/charter.md", """
            # Vesper Knox
            - Write new tests whenever Arjun Sen, Jae Min Kade, or Lyra Morn add new functionality
            - Coordinate with Arjun Sen, Lyra Morn, Jae Min Kade, and Talia Rune to understand new features needing coverage
            """);

        var built = SquadBridgePromptBuilder.Build(
            "Implement it - hand to Lyra Morn",
            "Quick replies enabled.",
            null,
            null,
            null,
            workspace.RootPath);

        Assert.That(built.PromptText, Does.Not.Contain("direct ownership clues for Vesper Knox"));
    }
}
