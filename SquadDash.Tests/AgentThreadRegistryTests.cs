using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class AgentThreadRegistryTests {

    // ── IsTerminalBackgroundStatus ────────────────────────────────────────────

    [TestCase("Completed",  ExpectedResult = true)]
    [TestCase("Failed",     ExpectedResult = true)]
    [TestCase("Cancelled",  ExpectedResult = true)]
    [TestCase("Running",    ExpectedResult = false)]
    [TestCase("",           ExpectedResult = false)]
    [TestCase(null,         ExpectedResult = false)]
    public bool IsTerminalBackgroundStatus_MatchesExpected(string? status) =>
        AgentThreadRegistry.IsTerminalBackgroundStatus(status);

    // ── HumanizeThreadStatus ─────────────────────────────────────────────────

    [TestCase("running",    ExpectedResult = "Running")]
    [TestCase("completed",  ExpectedResult = "Completed")]
    [TestCase("failed",     ExpectedResult = "Failed")]
    [TestCase("killed",     ExpectedResult = "Cancelled")]
    [TestCase("cancelled",  ExpectedResult = "Cancelled")]
    [TestCase("idle",       ExpectedResult = "Waiting")]
    [TestCase(null,         ExpectedResult = "")]
    [TestCase("",           ExpectedResult = "")]
    public string HumanizeThreadStatus_MatchesExpected(string? status) =>
        AgentThreadRegistry.HumanizeThreadStatus(status);

    // ── ResolveThreadDisplayName ─────────────────────────────────────────────

    [Test]
    public void ResolveThreadDisplayName_PrefersDisplayName() {
        var result = AgentThreadRegistry.ResolveThreadDisplayName(
            "Lyra Morn", "general-purpose", "gp-agent-1");

        Assert.That(result, Is.EqualTo("Lyra Morn"));
    }

    [Test]
    public void ResolveThreadDisplayName_FallsBackToHumanizedAgentName() {
        var result = AgentThreadRegistry.ResolveThreadDisplayName(
            null, "general-purpose", "gp-agent-1");

        Assert.That(result, Is.EqualTo(AgentNameHumanizer.Humanize("general-purpose")));
    }

    [Test]
    public void ResolveThreadDisplayName_FallsBackToHumanizedAgentId() {
        var result = AgentThreadRegistry.ResolveThreadDisplayName(
            null, null, "lyra-theme-plan");

        Assert.That(result, Is.EqualTo(AgentNameHumanizer.Humanize("lyra-theme-plan")));
    }

    [Test]
    public void ResolveThreadDisplayName_AllNullReturnsDefaultLabel() {
        var result = AgentThreadRegistry.ResolveThreadDisplayName(null, null, null);

        Assert.That(result, Is.EqualTo("Background Agent"));
    }

    [Test]
    public void ResolveSecondaryTranscriptDisplayName_UsesOriginFirstNameForGeneralPurposeAgent() {
        var thread = MakeThread("gpa-thread", "General Purpose Agent");
        thread.AgentDisplayName = "General Purpose Agent";
        thread.AgentType = "general-purpose";
        thread.OriginAgentDisplayName = "Lyra Morn";

        var result = AgentThreadRegistry.ResolveSecondaryTranscriptDisplayName(thread, "General Purpose Agent");

        Assert.That(result, Is.EqualTo("Lyra's GPA"));
    }

    [Test]
    public void ResolveSecondaryTranscriptDisplayName_FallsBackForNonGenericAgent() {
        var thread = MakeThread("named-thread", "Orion Vale");
        thread.AgentDisplayName = "Orion Vale";
        thread.OriginAgentDisplayName = "Lyra Morn";

        var result = AgentThreadRegistry.ResolveSecondaryTranscriptDisplayName(thread, "Orion Vale");

        Assert.That(result, Is.EqualTo("Orion Vale"));
    }

    [Test]
    public void ResolveSecondaryTranscriptDisplayName_DoesNotRenameRosterBackedAgentWhenAgentTypeIsGeneric() {
        var thread = MakeThread("vesper-thread", "Vesper Knox");
        thread.AgentDisplayName = "Vesper Knox";
        thread.AgentType = "general-purpose";
        thread.AgentCardKey = "vesper-knox";
        thread.OriginAgentDisplayName = "Background";

        var result = AgentThreadRegistry.ResolveSecondaryTranscriptDisplayName(thread, "Vesper Knox");

        Assert.That(result, Is.EqualTo("Vesper Knox"));
    }

    // ── HumanizeAgentName ────────────────────────────────────────────────────

    [Test]
    public void HumanizeAgentName_DelegatesToAgentNameHumanizer() {
        var expected = AgentNameHumanizer.Humanize("vesper-knox");

        Assert.That(AgentThreadRegistry.HumanizeAgentName("vesper-knox"), Is.EqualTo(expected));
    }

    // ── Instance: GetOrCreateAgentThread ────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void GetOrCreateAgentThread_CreatesNewThread_WhenToolCallIdIsNew() {
        var registry = MakeRegistry();

        var thread = registry.GetOrCreateAgentThread(
            toolCallId: "tool-abc",
            agentId: "lyra-design",
            agentName: null,
            agentDisplayName: null,
            agentDescription: null,
            status: "running",
            prompt: null,
            startedAt: null);

        Assert.Multiple(() => {
            Assert.That(thread, Is.Not.Null);
            Assert.That(thread.ToolCallId, Is.EqualTo("tool-abc"));
            Assert.That(thread.AgentId, Is.EqualTo("lyra-design"));
            Assert.That(registry.ThreadOrder, Has.Count.EqualTo(1));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void GetOrCreateAgentThread_SameToolCallId_ReturnsSameThread() {
        var registry = MakeRegistry();

        var first = registry.GetOrCreateAgentThread(
            "tool-xyz", "agent-1", null, null, null, null, null, null);
        var second = registry.GetOrCreateAgentThread(
            "tool-xyz", "agent-1", null, null, null, "running", null, null);

        Assert.That(ReferenceEquals(first, second), Is.True);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void GetOrCreateAgentThread_DifferentToolCallIds_CreateDistinctThreads() {
        var registry = MakeRegistry();

        var t1 = registry.GetOrCreateAgentThread("tool-1", "agent-a", null, null, null, null, null, null);
        var t2 = registry.GetOrCreateAgentThread("tool-2", "agent-b", null, null, null, null, null, null);

        Assert.Multiple(() => {
            Assert.That(registry.ThreadOrder, Has.Count.EqualTo(2));
            Assert.That(ReferenceEquals(t1, t2), Is.False);
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void GetOrCreateAgentThread_DifferentToolCallIdsForSameNamedAgent_CreateDistinctThreads() {
        var registry = MakeRegistry(getKnownTeamAgentDescriptors: () => [
            new TeamAgentDescriptor("Orion Vale", "orion-vale", "Architect")
        ]);

        var first = registry.GetOrCreateAgentThread(
            toolCallId: "tool-orion-yesterday",
            agentId: "orion-vale",
            agentName: "orion-vale",
            agentDisplayName: "Orion Vale",
            agentDescription: null,
            status: "completed",
            prompt: "Review the old architecture.",
            startedAt: "2026-05-01T19:25:00Z");
        first.CompletedAt = new DateTimeOffset(2026, 5, 1, 20, 0, 0, TimeSpan.Zero);

        var second = registry.GetOrCreateAgentThread(
            toolCallId: "tool-orion-quick-reply",
            agentId: "orion-vale",
            agentName: "orion-vale",
            agentDisplayName: "Orion Vale",
            agentDescription: null,
            status: "running",
            prompt: "Do the quick reply work.",
            startedAt: "2026-05-02T14:00:00Z");

        Assert.Multiple(() => {
            Assert.That(ReferenceEquals(first, second), Is.False);
            Assert.That(registry.ThreadOrder, Has.Count.EqualTo(2));
            Assert.That(second.ToolCallId, Is.EqualTo("tool-orion-quick-reply"));
            Assert.That(second.Prompt, Is.EqualTo("Do the quick reply work."));
            Assert.That(second.CompletedAt, Is.Null);
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void UpdateAgentThreadLifecycle_SetsCurrentBackgroundRun_ForNonTerminalStatus() {
        var registry = MakeRegistry();
        var thread = registry.GetOrCreateAgentThread("tool-live", "lyra-morn", null, null, null, null, null, null);

        registry.UpdateAgentThreadLifecycle(
            thread,
            new SquadSdkEvent { AgentId = "lyra-morn", Status = "running" },
            statusText: "Running",
            detailText: "Working");

        Assert.That(thread.IsCurrentBackgroundRun, Is.True);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void UpdateAgentThreadLifecycle_ClearsCurrentBackgroundRun_ForCompletedStatus() {
        var registry = MakeRegistry();
        var thread = registry.GetOrCreateAgentThread("tool-done", "lyra-morn", null, null, null, null, null, null);
        thread.IsCurrentBackgroundRun = true;

        registry.UpdateAgentThreadLifecycle(
            thread,
            new SquadSdkEvent { AgentId = "lyra-morn", Status = "completed" },
            statusText: "Completed",
            detailText: "Done");

        Assert.That(thread.IsCurrentBackgroundRun, Is.False);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void NormalizeThreadAgentIdentity_PromotesRosterDisplayName_WhenIdentityWasGeneric() {
        var registry = MakeRegistry(getKnownTeamAgentDescriptors: () => [
            new TeamAgentDescriptor("Lyra Morn", "lyra-morn", "WPF specialist")
        ]);

        var thread = registry.GetOrCreateAgentThread(
            toolCallId: "tool-lyra-promote",
            agentId: "lyra-space4-resize",
            agentName: "Squad",
            agentDisplayName: "Squad",
            agentDescription: "Your AI team. Describe what you're building, get a team of specialists that live in your repo.",
            status: "running",
            prompt: null,
            startedAt: null);

        Assert.Multiple(() => {
            Assert.That(thread.AgentCardKey, Is.EqualTo("lyra-morn"));
            Assert.That(thread.AgentDisplayName, Is.EqualTo("Lyra Morn"));
            Assert.That(thread.Title, Is.EqualTo("Lyra Morn"));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void EnsureAgentThreadTurnStarted_DoesNotUseAgentDescriptionAsPromptFallback() {
        string? capturedPrompt = null;
        var registry = MakeRegistry(beginTranscriptTurn: (_, prompt) => capturedPrompt = prompt);
        var thread = registry.GetOrCreateAgentThread(
            toolCallId: "tool-generic-prompt",
            agentId: null,
            agentName: "Squad",
            agentDisplayName: "Squad",
            agentDescription: "Your AI team. Describe what you're building, get a team of specialists that live in your repo.",
            status: "running",
            prompt: null,
            startedAt: null);

        registry.EnsureAgentThreadTurnStarted(thread);

        Assert.That(capturedPrompt, Is.EqualTo("Background task"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyOriginMetadata_CopiesParentDisplayNameAndToolCallId() {
        var registry = MakeRegistry();
        var childThread = MakeThread("child-thread", "General Purpose Agent");
        var parentThread = MakeThread("parent-thread", "Lyra Morn");
        parentThread.AgentDisplayName = "Lyra Morn";

        registry.ApplyOriginMetadata(childThread, parentThread, "tool-parent");

        Assert.Multiple(() => {
            Assert.That(childThread.OriginAgentDisplayName, Is.EqualTo("Lyra Morn"));
            Assert.That(childThread.OriginParentToolCallId, Is.EqualTo("tool-parent"));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void CaptureBackgroundAgentLaunchInfo_PromotesNestedScribeTaskToRosterIdentity() {
        var registry = MakeRegistry(getKnownTeamAgentDescriptors: () => [
            new TeamAgentDescriptor("Scribe", "scribe", "Session Logger")
        ]);

        using var document = System.Text.Json.JsonDocument.Parse("""
            {
              "agent_type": "general-purpose",
              "description": "📋 Scribe: Log session & merge decisions",
              "mode": "background",
              "model": "claude-haiku-4.5",
              "name": "scribe-docs-panel-log",
              "prompt": "You are the Scribe. Read .squad/agents/scribe/charter.md."
            }
            """);

        registry.CaptureBackgroundAgentLaunchInfo(new SquadSdkEvent {
            ToolName = "task",
            ToolCallId = "tool-scribe",
            Args = document.RootElement.Clone()
        });

        var thread = registry.GetOrCreateAgentThread(
            toolCallId: "tool-scribe",
            agentId: "general-purpose",
            agentName: "General Purpose Agent",
            agentDisplayName: "General Purpose Agent",
            agentDescription: "Full-capability agent running in a subprocess.",
            status: "running",
            prompt: null,
            startedAt: null);

        Assert.Multiple(() => {
            Assert.That(thread.AgentCardKey, Is.EqualTo("scribe"));
            Assert.That(thread.AgentDisplayName, Is.EqualTo("Scribe"));
            Assert.That(thread.Title, Is.EqualTo("Scribe"));
        });
    }

    // ── Instance: AliasThreadKeys ────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void AliasThreadKeys_MakesThreadFindableByAlias() {
        var registry = MakeRegistry();

        var thread = registry.GetOrCreateAgentThread(
            "tool-alias-test", "vesper-knox", null, null, null, null, null, null);

        // AliasThreadKeys is called internally by GetOrCreateAgentThread; the
        // thread should be reachable by both its tool-call key and the agent key.
        Assert.Multiple(() => {
            Assert.That(registry.ThreadsByKey.ContainsKey("tool-alias-test"), Is.True);
            Assert.That(registry.ThreadsByToolCallId.ContainsKey("tool-alias-test"), Is.True);
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void AliasThreadKeys_ManualAlias_AppearInThreadsByKey() {
        var registry = MakeRegistry();

        var thread = registry.GetOrCreateAgentThread(
            "tool-original", "some-agent", null, null, null, null, null, null);

        registry.AliasThreadKeys(thread, "tool-alias", null, null, null);

        Assert.That(registry.ThreadsByKey.ContainsKey("tool-alias"), Is.True);
        Assert.That(ReferenceEquals(registry.ThreadsByKey["tool-alias"], thread), Is.True);
    }

    // ── Instance: ThreadsByToolCallId ────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void ThreadsByToolCallId_ContainsThreadAfterCreation() {
        var registry = MakeRegistry();

        registry.GetOrCreateAgentThread("tool-99", "arjun-sen", null, null, null, null, null, null);

        Assert.That(registry.ThreadsByToolCallId.ContainsKey("tool-99"), Is.True);
    }

    // ── Static: GetThreadLastActivityAt ─────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void GetThreadLastActivityAt_PrefersLastObservedActivityAt() {
        var thread = MakeThread("t1", "Test Thread");
        var observed   = new DateTimeOffset(2026, 4, 15, 10, 0, 0, TimeSpan.Zero);
        var completed  = new DateTimeOffset(2026, 4, 15, 11, 0, 0, TimeSpan.Zero);
        thread.LastObservedActivityAt = observed;
        thread.CompletedAt = completed;

        Assert.That(AgentThreadRegistry.GetThreadLastActivityAt(thread), Is.EqualTo(observed));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void GetThreadLastActivityAt_FallsBackToCompletedAt() {
        var thread = MakeThread("t2", "Test Thread");
        var completed = new DateTimeOffset(2026, 4, 15, 11, 0, 0, TimeSpan.Zero);
        thread.LastObservedActivityAt = null;
        thread.CompletedAt = completed;

        Assert.That(AgentThreadRegistry.GetThreadLastActivityAt(thread), Is.EqualTo(completed));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void GetThreadLastActivityAt_FallsBackToStartedAt() {
        var startedAt = new DateTimeOffset(2026, 4, 15, 9, 0, 0, TimeSpan.Zero);
        var thread = new TranscriptThreadState("t3", TranscriptThreadKind.Agent, "Test", startedAt);

        Assert.That(AgentThreadRegistry.GetThreadLastActivityAt(thread), Is.EqualTo(startedAt));
    }

    // ── Static: HasPersistableThreadContent ─────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void HasPersistableThreadContent_ReturnsFalse_ForPlaceholderThread() {
        var thread = MakeThread("pt", "Placeholder");
        thread.IsPlaceholderThread = true;

        Assert.That(AgentThreadRegistry.HasPersistableThreadContent(thread), Is.False);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void HasPersistableThreadContent_ReturnsTrue_WhenPromptIsSet() {
        var thread = MakeThread("ct", "Content Thread");
        thread.Prompt = "Do something useful.";

        Assert.That(AgentThreadRegistry.HasPersistableThreadContent(thread), Is.True);
    }

    // ── Static: HasRosterBackedIdentity ─────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void HasRosterBackedIdentity_ReturnsFalse_WhenAgentCardKeyIsNull() {
        var thread = MakeThread("r1", "Dynamic Agent");
        thread.AgentCardKey = null;

        Assert.That(AgentThreadRegistry.HasRosterBackedIdentity(thread), Is.False);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void HasRosterBackedIdentity_ReturnsTrue_WhenAgentCardKeyIsSet() {
        var thread = MakeThread("r2", "Vesper Knox");
        thread.AgentCardKey = "vesper-knox";

        Assert.That(AgentThreadRegistry.HasRosterBackedIdentity(thread), Is.True);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AgentThreadRegistry MakeRegistry(
        Func<TeamAgentDescriptor[]>? getKnownTeamAgentDescriptors = null,
        Action<TranscriptThreadState, string?>? beginTranscriptTurn = null) =>
        new AgentThreadRegistry(
            beginTranscriptTurn:              beginTranscriptTurn ?? ((_, _) => { }),
            finalizeCurrentTurnResponse:      _ => { },
            collapseCurrentTurnThinking:      _ => { },
            renderToolEntry:                  _ => { },
            updateToolSpinnerState:           () => { },
            syncActiveToolName:               () => { },
            syncThreadChip:                   _ => { },
            syncTaskToolTranscriptLink:       _ => { },
            appendText:                       (_, _) => { },
            syncAgentCards:                   () => { },
            syncAgentCardsWithThreads:        () => { },
            getKnownTeamAgentDescriptors:     getKnownTeamAgentDescriptors ?? (() => Array.Empty<TeamAgentDescriptor>()),
            updateTranscriptThreadBadge:      () => { },
            isThreadActiveForDisplay:         _ => false,
            observeBackgroundAgentActivity:   (_, _) => { },
            renderConversationHistory:        (_, _) => Task.CompletedTask,
            resolveBackgroundAgentDisplayLabel: _ => string.Empty,
            buildAgentLabel:                  _ => string.Empty);

    private static TranscriptThreadState MakeThread(string threadId, string title) =>
        new TranscriptThreadState(
            threadId,
            TranscriptThreadKind.Agent,
            title,
            DateTimeOffset.UtcNow);
}
