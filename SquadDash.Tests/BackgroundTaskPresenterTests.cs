using System;
using System.Collections.Generic;
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

    private static BackgroundTaskPresenter MakePresenter() {
        var registry = MakeRegistry();

        return new BackgroundTaskPresenter(
            agentThreadRegistry:          registry,
            appendLine:                   (_, _) => { },
            syncAgentCards:               () => { },
            isPromptRunning:              () => false,
            currentTurn:                  () => null,
            themeBrush:                   _ => Brushes.Transparent,
            tryPostToUi:                  (action, _) => action(),
            isClosing:                    () => false,
            updateLeadAgent:              (_, _, _) => { },
            updateSessionState:           _ => { },
            persistAgentThreadSnapshot:   _ => { },
            currentTurnSnapshot:          () => new CurrentTurnStatusSnapshot(false, false, false),
            agentActiveDisplayLinger:     TimeSpan.FromSeconds(30),
            dynamicAgentHistoryRetention: TimeSpan.FromDays(7));
    }

    private static TranscriptThreadState MakeThread(string threadId, DateTimeOffset startedAt) =>
        new(threadId, TranscriptThreadKind.Agent, "Lyra Morn", startedAt);
}
