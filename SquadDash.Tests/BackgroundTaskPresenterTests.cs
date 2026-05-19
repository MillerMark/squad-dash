using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Documents;
using System.Threading.Tasks;
using System.Windows.Media;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class BackgroundTaskPresenterTests {

    // ── Static: BuildBackgroundAgentLabel(SquadBackgroundAgentInfo) ──────────

    [Test]
    public void BuildBackgroundAgentLabel_PrefersAgentDisplayNameOverDescription() {
        var agent = new SquadBackgroundAgentInfo {
            AgentDisplayName = "Squad",
            AgentName        = "assemble-team",
            Description      = "Your AI team. Describe what you're building, get a team of specialists that live in your repo.",
            AgentId          = "assemble-team"
        };

        var label = BackgroundTaskPresenter.BuildBackgroundAgentLabel(agent);

        Assert.That(label, Is.EqualTo("Squad"));
    }

    [Test]
    public void BuildBackgroundAgentLabel_PrefersHumanizedAgentNameWhenDisplayNameMissing() {
        var agent = new SquadBackgroundAgentInfo {
            AgentName   = "lyra-morn",
            Description = "Writing unit tests",
            AgentId     = "lyra-morn"
        };

        var label = BackgroundTaskPresenter.BuildBackgroundAgentLabel(agent);

        Assert.That(label, Is.EqualTo("lyra morn"));
    }

    [Test]
    public void BuildBackgroundAgentLabel_BothDescriptionAndAgentId_NotContained_ReturnsCombined() {
        var agent = new SquadBackgroundAgentInfo {
            Description = "Refactoring routing logic",
            AgentId     = "vesper-routing-fix"
        };

        var label = BackgroundTaskPresenter.BuildBackgroundAgentLabel(agent);

        Assert.That(label, Is.EqualTo("Refactoring routing logic (vesper-routing-fix)"));
    }

    [Test]
    public void BuildBackgroundAgentLabel_DescriptionContainsAgentId_ReturnsDescriptionOnly() {
        var agent = new SquadBackgroundAgentInfo {
            Description = "Running vesper-routing-fix task",
            AgentId     = "vesper-routing-fix"
        };

        var label = BackgroundTaskPresenter.BuildBackgroundAgentLabel(agent);

        Assert.That(label, Is.EqualTo("Running vesper-routing-fix task"));
    }

    [Test]
    public void BuildBackgroundAgentLabel_OnlyDescription_ReturnsDescription() {
        var agent = new SquadBackgroundAgentInfo {
            Description = "Writing unit tests",
            AgentId     = null
        };

        Assert.That(BackgroundTaskPresenter.BuildBackgroundAgentLabel(agent),
            Is.EqualTo("Writing unit tests"));
    }

    [Test]
    public void BuildBackgroundAgentLabel_OnlyAgentId_ReturnsPrefixedAgentId() {
        var agent = new SquadBackgroundAgentInfo {
            Description = null,
            AgentId     = "lyra-composer"
        };

        Assert.That(BackgroundTaskPresenter.BuildBackgroundAgentLabel(agent),
            Is.EqualTo("Agent lyra-composer"));
    }

    [Test]
    public void BuildBackgroundAgentLabel_NeitherDescriptionNorAgentId_ReturnsDefault() {
        var agent = new SquadBackgroundAgentInfo {
            Description = null,
            AgentId     = null
        };

        Assert.That(BackgroundTaskPresenter.BuildBackgroundAgentLabel(agent),
            Is.EqualTo("Background agent"));
    }

    [Test]
    public void BuildBackgroundAgentLabel_WhitespaceDescriptionAndId_ReturnsDefault() {
        var agent = new SquadBackgroundAgentInfo {
            Description = "   ",
            AgentId     = "  "
        };

        Assert.That(BackgroundTaskPresenter.BuildBackgroundAgentLabel(agent),
            Is.EqualTo("Background agent"));
    }

    // ── Instance: HasBackgroundTasks ─────────────────────────────────────────

    [Test]
    public void HasBackgroundTasks_ReturnsFalse_WhenNoAgentsOrShells() {
        var presenter = MakePresenter();

        Assert.That(presenter.HasBackgroundTasks(), Is.False);
    }

    [Test]
    public void HasBackgroundTasks_ReturnsTrue_WhenBackgroundAgentPresent() {
        var presenter = MakePresenter();
        presenter.BackgroundAgents = [new SquadBackgroundAgentInfo { AgentId = "lyra-morn" }];

        Assert.That(presenter.HasBackgroundTasks(), Is.True);
    }

    [Test]
    public void HasBackgroundTasks_ReturnsTrue_WhenBackgroundShellPresent() {
        var presenter = MakePresenter();
        presenter.BackgroundShells = [new SquadBackgroundShellInfo { ShellId = "shell-1" }];

        Assert.That(presenter.HasBackgroundTasks(), Is.True);
    }

    [Test]
    public void HasRestartBlockingBackgroundWork_ReturnsTrue_ForCurrentNonTerminalAgentThread() {
        var registry = MakeRegistry();
        var presenter = MakePresenter(registry);
        var thread = registry.GetOrCreateAgentThread(
            toolCallId: "tool-bruce",
            agentId: "brucebanner",
            agentName: "brucebanner",
            agentDisplayName: "Bruce Banner",
            agentDescription: null,
            status: "running",
            prompt: "Fix both remaining failures",
            startedAt: DateTimeOffset.UtcNow.ToString("O"));
        thread.WasObservedAsBackgroundTask = true;
        thread.IsCurrentBackgroundRun = true;
        thread.StatusText = "Running";

        Assert.That(presenter.HasRestartBlockingBackgroundWork(), Is.True);
    }

    [Test]
    public void HasRestartBlockingBackgroundWork_ReturnsFalse_ForTerminalAgentThread() {
        var registry = MakeRegistry();
        var presenter = MakePresenter(registry);
        var thread = registry.GetOrCreateAgentThread(
            toolCallId: "tool-bruce",
            agentId: "brucebanner",
            agentName: "brucebanner",
            agentDisplayName: "Bruce Banner",
            agentDescription: null,
            status: "completed",
            prompt: "Fix both remaining failures",
            startedAt: DateTimeOffset.UtcNow.ToString("O"));
        thread.WasObservedAsBackgroundTask = true;
        thread.IsCurrentBackgroundRun = false;
        thread.StatusText = "Completed";
        thread.CompletedAt = DateTimeOffset.UtcNow;

        Assert.That(presenter.HasRestartBlockingBackgroundWork(), Is.False);
    }

    [Test]
    public void HasRestartBlockingBackgroundWork_ReturnsFalse_ForStaleFallbackThread_WhenPromptIsRunning() {
        var registry = MakeRegistry();
        var now = DateTimeOffset.UtcNow;
        var presenter = MakePresenter(registry, isPromptRunning: true);
        var thread = registry.GetOrCreateAgentThread(
            toolCallId: "tool-talia",
            agentId: "talia-rune",
            agentName: "talia-rune",
            agentDisplayName: "Talia Rune",
            agentDescription: null,
            status: "running",
            prompt: "Recover stale handoff",
            startedAt: now.AddMinutes(-30).ToString("O"));
        thread.WasObservedAsBackgroundTask = true;
        thread.IsCurrentBackgroundRun = true;
        thread.StatusText = "Running";
        thread.LastObservedActivityAt = now.AddMinutes(-11);

        Assert.That(presenter.HasRestartBlockingBackgroundWork(), Is.False);
    }

    [Test]
    public void RecoverStaleBackgroundThreads_MarksInterruptedAndClearsCurrentRun() {
        var registry = MakeRegistry();
        var now = new DateTimeOffset(2026, 5, 19, 18, 30, 0, TimeSpan.Zero);
        var lastActivity = now.AddMinutes(-12);
        var persistedThreads = new List<TranscriptThreadState>();
        var presenter = MakePresenter(registry, persistedThreads: persistedThreads);
        var thread = registry.GetOrCreateAgentThread(
            toolCallId: "tool-talia",
            agentId: "talia-rune",
            agentName: "talia-rune",
            agentDisplayName: "Talia Rune",
            agentDescription: null,
            status: "running",
            prompt: "Recover stale handoff",
            startedAt: now.AddMinutes(-30).ToString("O"));
        thread.WasObservedAsBackgroundTask = true;
        thread.IsCurrentBackgroundRun = true;
        thread.StatusText = "Running";
        thread.LastObservedActivityAt = lastActivity;

        var recovered = presenter.RecoverStaleBackgroundThreads(now, "test");

        Assert.Multiple(() => {
            Assert.That(recovered, Is.EqualTo(1));
            Assert.That(thread.StatusText, Is.EqualTo("Interrupted"));
            Assert.That(thread.IsCurrentBackgroundRun, Is.False);
            Assert.That(thread.CompletedAt, Is.EqualTo(lastActivity));
            Assert.That(thread.DetailText, Does.Contain("stopped reporting"));
            Assert.That(persistedThreads, Is.EquivalentTo(new[] { thread }));
            Assert.That(presenter.HasRestartBlockingBackgroundWork(), Is.False);
        });
    }

    // ── Instance: ClearState ─────────────────────────────────────────────────

    [Test]
    public void ClearState_ResetsAgentsAndShellsToEmpty() {
        var presenter = MakePresenter();
        presenter.BackgroundAgents = [new SquadBackgroundAgentInfo { AgentId = "arjun-sen" }];
        presenter.BackgroundShells = [new SquadBackgroundShellInfo { ShellId = "shell-9" }];

        presenter.ClearState();

        Assert.Multiple(() => {
            Assert.That(presenter.BackgroundAgents, Is.Empty);
            Assert.That(presenter.BackgroundShells, Is.Empty);
            Assert.That(presenter.HasBackgroundTasks(), Is.False);
        });
    }

    [Test]
    public void ClearState_ResetsSkipNextBackgroundCompletionFallback() {
        var presenter = MakePresenter();
        presenter.SkipNextBackgroundCompletionFallback = true;

        presenter.ClearState();

        Assert.That(presenter.SkipNextBackgroundCompletionFallback, Is.False);
    }

    [Test]
    public void GetAbortTargets_ReturnsAllNonTerminalAgentsAndShells() {
        var presenter = MakePresenter();
        var agentStartedAt = new DateTimeOffset(2026, 4, 28, 14, 28, 0, TimeSpan.FromHours(-4));
        var shellStartedAt = agentStartedAt.AddMinutes(1);
        presenter.BackgroundAgents = [
            new SquadBackgroundAgentInfo { AgentId = "lyra-live", AgentDisplayName = "Lyra", Status = "running", StartedAt = agentStartedAt.ToString("O") },
            new SquadBackgroundAgentInfo { AgentId = "done-agent", AgentDisplayName = "Done", Status = "completed" }
        ];
        presenter.BackgroundShells = [
            new SquadBackgroundShellInfo { ShellId = "shell-live", Description = "Build check", Status = "running", StartedAt = shellStartedAt.ToString("O") },
            new SquadBackgroundShellInfo { ShellId = "shell-done", Description = "Finished", Status = "cancelled" }
        ];

        var targets = presenter.GetAbortTargets();

        Assert.Multiple(() => {
            Assert.That(targets.Select(target => target.TaskId), Is.EquivalentTo(new[] { "lyra-live", "shell-live" }));
            Assert.That(targets.Single(target => target.TaskId == "lyra-live").TaskKind, Is.EqualTo("agent"));
            Assert.That(targets.Single(target => target.TaskId == "lyra-live").StartedAt, Is.EqualTo(agentStartedAt));
            Assert.That(targets.Single(target => target.TaskId == "shell-live").TaskKind, Is.EqualTo("shell"));
            Assert.That(targets.Single(target => target.TaskId == "shell-live").StartedAt, Is.EqualTo(shellStartedAt));
        });
    }

    [Test]
    public void GetAbortTargets_DeduplicatesTaskIds() {
        var presenter = MakePresenter();
        presenter.BackgroundAgents = [
            new SquadBackgroundAgentInfo { AgentId = "shared-task", AgentDisplayName = "Lyra", Status = "running" },
            new SquadBackgroundAgentInfo { AgentId = "shared-task", AgentDisplayName = "Lyra Duplicate", Status = "running" }
        ];
        presenter.BackgroundShells = [
            new SquadBackgroundShellInfo { ShellId = "shared-task", Description = "Same id", Status = "running" }
        ];

        var targets = presenter.GetAbortTargets();

        Assert.That(targets.Select(target => target.TaskId), Is.EqualTo(new[] { "shared-task" }));
    }

    [Test]
    public void GetAbortTargets_IncludesActiveFallbackAgentThreads_WhenSnapshotIsEmpty() {
        var registry = MakeRegistry();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var thread = registry.GetOrCreateAgentThread(
            toolCallId: "call-lyra",
            agentId: "lyra-live",
            agentName: "lyra-morn",
            agentDisplayName: "Lyra",
            agentDescription: "Writing tests",
            status: "running",
            prompt: "Write tests",
            startedAt: startedAt.ToString("O"));
        thread.WasObservedAsBackgroundTask = true;
        thread.IsCurrentBackgroundRun = true;
        thread.StatusText = "Running";

        var presenter = MakePresenter(registry, isPromptRunning: true);

        var targets = presenter.GetAbortTargets();

        Assert.Multiple(() => {
            Assert.That(targets.Select(target => target.TaskId), Is.EqualTo(new[] { "lyra-live" }));
            Assert.That(targets.Single().TaskIdSource, Is.EqualTo("thread:agentId"));
            Assert.That(targets.Single().DisplayLabel, Is.EqualTo("Lyra (lyra-live)"));
            Assert.That(targets.Single().StartedAt, Is.EqualTo(startedAt));
        });
    }

    [Test]
    public void GetAbortTargets_IncludesRecentlyActiveFallbackAgentThreads_WhenPromptIsIdle() {
        var registry = MakeRegistry();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var thread = registry.GetOrCreateAgentThread(
            toolCallId: "call-lyra",
            agentId: "lyra-live",
            agentName: "lyra-morn",
            agentDisplayName: "Lyra",
            agentDescription: "Writing tests",
            status: "running",
            prompt: "Write tests",
            startedAt: startedAt.ToString("O"));
        thread.WasObservedAsBackgroundTask = true;
        thread.IsCurrentBackgroundRun = true;
        thread.StatusText = "Running";
        thread.LastObservedActivityAt = DateTimeOffset.Now;

        var presenter = MakePresenter(registry, isPromptRunning: false);

        var targets = presenter.GetAbortTargets();

        Assert.Multiple(() => {
            Assert.That(targets.Select(target => target.TaskId), Is.EqualTo(new[] { "lyra-live" }));
            Assert.That(targets.Single().TaskIdSource, Is.EqualTo("thread:agentId"));
            Assert.That(targets.Single().DisplayLabel, Is.EqualTo("Lyra (lyra-live)"));
        });
    }

    [Test]
    public void GetAbortTargets_PrefersBackgroundTaskId_ForFallbackAgentThreads() {
        var registry = MakeRegistry();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var thread = registry.GetOrCreateAgentThread(
            toolCallId: "toolu_bdrk_014NnSZxJapiCigA8z7DfSS3",
            agentId: "toolu_bdrk_014NnSZxJapiCigA8z7DfSS3",
            agentName: "general-purpose",
            agentDisplayName: "Talia Rune",
            agentDescription: "Writing tests",
            status: "running",
            prompt: "Write tests",
            startedAt: startedAt.ToString("O"));
        thread.BackgroundTaskId = "queue-held-restart-bug";
        thread.WasObservedAsBackgroundTask = true;
        thread.IsCurrentBackgroundRun = true;
        thread.StatusText = "Running";

        var presenter = MakePresenter(registry, isPromptRunning: true);

        var targets = presenter.GetAbortTargets();

        Assert.Multiple(() => {
            Assert.That(targets.Select(target => target.TaskId), Is.EqualTo(new[] { "queue-held-restart-bug" }));
            Assert.That(targets.Single().TaskIdSource, Is.EqualTo("thread:backgroundTaskId"));
            Assert.That(targets.Single().AgentId, Is.EqualTo("toolu_bdrk_014NnSZxJapiCigA8z7DfSS3"));
            Assert.That(targets.Single().ToolCallId, Is.EqualTo("toolu_bdrk_014NnSZxJapiCigA8z7DfSS3"));
        });
    }

    [Test]
    public void GetAbortTargets_FallsBackToToolCallId_ForTransientAgentIdWithoutTaskId() {
        var registry = MakeRegistry();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var thread = registry.GetOrCreateAgentThread(
            toolCallId: "toolu_bdrk_01N6Pgma8Mqqgu34DzQBMt2U",
            agentId: "toolu_bdrk_01N6Pgma8Mqqgu34DzQBMt2U",
            agentName: "general-purpose",
            agentDisplayName: "Explore Held State",
            agentDescription: "Exploring",
            status: "running",
            prompt: "Explore",
            startedAt: startedAt.ToString("O"));
        thread.WasObservedAsBackgroundTask = true;
        thread.IsCurrentBackgroundRun = true;
        thread.StatusText = "Running";

        var presenter = MakePresenter(registry, isPromptRunning: true);

        var targets = presenter.GetAbortTargets();

        Assert.Multiple(() => {
            Assert.That(targets.Select(target => target.TaskId), Is.EqualTo(new[] { "toolu_bdrk_01N6Pgma8Mqqgu34DzQBMt2U" }));
            Assert.That(targets.Single().TaskIdSource, Is.EqualTo("thread:toolCallId"));
        });
    }

    [Test]
    public void GetAbortTargets_DeduplicatesSnapshotAndFallbackThreadByToolCallId() {
        var registry = MakeRegistry();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var thread = registry.GetOrCreateAgentThread(
            toolCallId: "call-lyra",
            agentId: "lyra-live",
            agentName: "lyra-morn",
            agentDisplayName: "Lyra",
            agentDescription: "Writing tests",
            status: "running",
            prompt: "Write tests",
            startedAt: startedAt.ToString("O"));
        thread.WasObservedAsBackgroundTask = true;
        thread.IsCurrentBackgroundRun = true;
        thread.StatusText = "Running";

        var presenter = MakePresenter(registry, isPromptRunning: true);
        presenter.BackgroundAgents = [
            new SquadBackgroundAgentInfo {
                AgentId = "lyra-live",
                ToolCallId = "call-lyra",
                AgentDisplayName = "Lyra",
                Status = "running",
                StartedAt = startedAt.ToString("O")
            }
        ];

        var targets = presenter.GetAbortTargets();

        Assert.That(targets.Select(target => target.TaskId), Is.EqualTo(new[] { "lyra-live" }));
    }

    [Test]
    public void IsThreadCurrentRunForDisplay_ReturnsTrue_ForCurrentNonTerminalThread() {
        var presenter = MakePresenter();
        var thread = MakeThread("lyra-live", startedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        thread.StatusText = "Running";
        thread.IsCurrentBackgroundRun = true;

        Assert.That(presenter.IsThreadCurrentRunForDisplay(thread), Is.True);
    }

    [Test]
    public void IsThreadCurrentRunForDisplay_ReturnsFalse_ForCompletedThread() {
        var presenter = MakePresenter();
        var thread = MakeThread("lyra-done", startedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        thread.StatusText = "Completed";
        thread.IsCurrentBackgroundRun = true;

        Assert.That(presenter.IsThreadCurrentRunForDisplay(thread), Is.False);
    }

    [Test]
    public void IsThreadStalledForDisplay_ReturnsTrue_AfterTwoMinutesOfSilence() {
        var presenter = MakePresenter();
        var now = new DateTimeOffset(2026, 4, 23, 11, 54, 0, TimeSpan.Zero);
        var thread = MakeThread("lyra-stalled", startedAt: now.AddMinutes(-10));
        thread.StatusText = "Running";
        thread.IsCurrentBackgroundRun = true;
        thread.LastObservedActivityAt = now.AddMinutes(-3);

        Assert.Multiple(() => {
            Assert.That(presenter.IsThreadStalledForDisplay(thread, now), Is.True);
            Assert.That(presenter.BuildStalledStatusText(thread, now), Does.StartWith("Stalled? Quiet for 3m"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void PromoteBackgroundAgentReportNow_AppendsReportDuringActiveCoordinatorTurn() {
        var registry = MakeRegistry();
        var startedAt = new DateTimeOffset(2026, 5, 3, 21, 50, 6, TimeSpan.FromHours(-4));
        var thread = registry.GetOrCreateAgentThread(
            toolCallId: "call-lyra",
            agentId: "lyra-morn",
            agentName: "lyra-morn",
            agentDisplayName: "Lyra Morn",
            agentDescription: "WPF implementation specialist",
            status: "completed",
            prompt: "Looks good - have Lyra implement it",
            startedAt: startedAt.ToString("O"));
        thread.WasObservedAsBackgroundTask = true;
        thread.StatusText = "Completed";
        thread.LatestResponse = "Done. Here's what was implemented:\n\n**Files changed:**\n- `PreferencesWindow.cs`";

        var coordinatorThread = new TranscriptThreadState(
            "coordinator",
            TranscriptThreadKind.Coordinator,
            "Squad",
            startedAt);
        var activeTurn = new TranscriptTurnView(
            coordinatorThread,
            "Looks good - have Lyra implement it",
            startedAt,
            new Section(),
            []);
        var appendedLines = new List<string>();
        var persistedThreads = new List<TranscriptThreadState>();
        var presenter = MakePresenter(
            registry,
            isPromptRunning: true,
            currentTurn: activeTurn,
            appendedLines: appendedLines,
            persistedThreads: persistedThreads);

        var promoted = presenter.PromoteBackgroundAgentReportNow(thread, "subagent_completed");

        Assert.Multiple(() => {
            Assert.That(promoted, Is.True);
            Assert.That(appendedLines, Has.Count.EqualTo(1));
            Assert.That(appendedLines[0], Does.StartWith("Lyra Morn (lyra-morn) reported back:"));
            Assert.That(appendedLines[0], Does.Contain("**Files changed:**"));
            Assert.That(thread.LastCoordinatorAnnouncedResponse, Is.EqualTo(thread.LatestResponse));
            Assert.That(persistedThreads, Is.EquivalentTo(new[] { thread }));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void PromoteDeferredBackgroundAgentReports_FlushesOnce_WhenCoordinatorBecomesIdle() {
        var registry = MakeRegistry();
        var startedAt = new DateTimeOffset(2026, 5, 8, 17, 10, 42, TimeSpan.FromHours(-4));
        var thread = registry.GetOrCreateAgentThread(
            toolCallId: "call-arjun",
            agentId: "arjun-sen",
            agentName: "arjun-sen",
            agentDisplayName: "Arjun Sen",
            agentDescription: "Testing specialist",
            status: "completed",
            prompt: "Verify timeout recovery",
            startedAt: startedAt.ToString("O"));
        thread.WasObservedAsBackgroundTask = true;
        thread.StatusText = "Completed";
        thread.LatestResponse = "Finished verification and left the working tree clean.";

        var isPromptRunning = true;
        var appendedLines = new List<string>();
        var presenter = MakePresenter(
            registry,
            isPromptRunningAccessor: () => isPromptRunning,
            appendedLines: appendedLines);

        var promotedWhileBusy = presenter.PromoteBackgroundAgentReportNow(
            thread,
            "subagent_completed:deferred:deferred");
        var promotedAgainWhileBusy = presenter.PromoteBackgroundAgentReportNow(
            thread,
            "subagent_completed:deferred:deferred:deferred");

        isPromptRunning = false;
        var flushedCount = presenter.PromoteDeferredBackgroundAgentReports("coordinator_idle");

        Assert.Multiple(() => {
            Assert.That(promotedWhileBusy, Is.False);
            Assert.That(promotedAgainWhileBusy, Is.False);
            Assert.That(flushedCount, Is.EqualTo(1));
            Assert.That(presenter.PendingBackgroundReportPromotionCount, Is.EqualTo(0));
            Assert.That(appendedLines, Has.Count.EqualTo(1));
            Assert.That(appendedLines[0], Does.StartWith("Arjun Sen (arjun-sen) reported back:"));
            Assert.That(appendedLines[0], Does.Contain("Finished verification"));
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AgentThreadRegistry MakeRegistry() =>
        new AgentThreadRegistry(
            beginTranscriptTurn:              (_, _) => { },
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
            getKnownTeamAgentDescriptors:     () => Array.Empty<TeamAgentDescriptor>(),
            updateTranscriptThreadBadge:      () => { },
            isThreadActiveForDisplay:         _ => false,
            observeBackgroundAgentActivity:   (_, _) => { },
            renderConversationHistory:        (_, _) => Task.CompletedTask,
            resolveBackgroundAgentDisplayLabel: _ => string.Empty,
            buildAgentLabel:                  _ => string.Empty);

    private static BackgroundTaskPresenter MakePresenter(
        AgentThreadRegistry? registry = null,
        bool isPromptRunning = false,
        Func<bool>? isPromptRunningAccessor = null,
        TranscriptTurnView? currentTurn = null,
        List<string>? appendedLines = null,
        List<TranscriptThreadState>? persistedThreads = null) {
        registry ??= MakeRegistry();

        return new BackgroundTaskPresenter(
            agentThreadRegistry:          registry,
            appendLine:                   (text, _) => appendedLines?.Add(text),
            syncAgentCards:               () => { },
            isPromptRunning:              isPromptRunningAccessor ?? (() => isPromptRunning),
            currentTurn:                  () => currentTurn,
            themeBrush:                   _ => Brushes.Transparent,
            tryPostToUi:                  (action, _) => action(),
            isClosing:                    () => false,
            updateLeadAgent:              (_, _, _) => { },
            updateSessionState:           _ => { },
            persistAgentThreadSnapshot:   thread => persistedThreads?.Add(thread),
            currentTurnSnapshot:          () => new CurrentTurnStatusSnapshot(false, false, false),
            agentActiveDisplayLinger:     TimeSpan.FromSeconds(30),
            dynamicAgentHistoryRetention: TimeSpan.FromDays(7));
    }

    private static TranscriptThreadState MakeThread(string threadId, DateTimeOffset startedAt) =>
        new(threadId, TranscriptThreadKind.Agent, "Lyra Morn", startedAt);
}
