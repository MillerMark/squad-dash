using System.Linq;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class BackgroundTaskStateResolverTests {
    [Test]
    public void IsFallbackLiveThread_ReturnsTrue_WhenPromptIsRunning_AndSnapshotIsEmpty() {
        var thread = new BackgroundTaskThreadSnapshot(
            ThreadId: "thread-1",
            Title: "Lyra Morn",
            ToolCallId: "tool-1",
            AgentId: "lyra-theme-plan-revise",
            AgentCardKey: "lyra-morn",
            StatusText: "Running",
            WasObservedAsBackgroundTask: true,
            IsPlaceholderThread: false,
            StartedAt: new DateTimeOffset(2026, 4, 15, 20, 41, 38, TimeSpan.Zero),
            LastObservedActivityAt: new DateTimeOffset(2026, 4, 15, 20, 47, 37, TimeSpan.Zero),
            CompletedAt: null);

        var result = BackgroundTaskStateResolver.IsFallbackLiveThread(
            thread,
            [],
            isPromptRunning: true,
            now: new DateTimeOffset(2026, 4, 15, 20, 50, 40, TimeSpan.Zero),
            recentActivityLinger: TimeSpan.FromSeconds(20),
            resolveSnapshotLabel: agent => agent.AgentId ?? string.Empty,
            resolveThreadLabel: snapshot => snapshot.Title);

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsFallbackLiveThread_ReturnsFalse_WhenThreadIsBackedBySnapshot() {
        var thread = new BackgroundTaskThreadSnapshot(
            ThreadId: "thread-1",
            Title: "Lyra Morn",
            ToolCallId: "tool-1",
            AgentId: "lyra-theme-plan-revise",
            AgentCardKey: "lyra-morn",
            StatusText: "Running",
            WasObservedAsBackgroundTask: true,
            IsPlaceholderThread: false,
            StartedAt: DateTimeOffset.UtcNow,
            LastObservedActivityAt: DateTimeOffset.UtcNow,
            CompletedAt: null);
        var agents = new[] {
            new SquadBackgroundAgentInfo {
                ToolCallId = "tool-1",
                AgentId = "lyra-theme-plan-revise"
            }
        };

        var result = BackgroundTaskStateResolver.IsFallbackLiveThread(
            thread,
            agents,
            isPromptRunning: true,
            now: DateTimeOffset.UtcNow,
            recentActivityLinger: TimeSpan.FromSeconds(20),
            resolveSnapshotLabel: agent => agent.AgentId ?? string.Empty,
            resolveThreadLabel: snapshot => snapshot.Title);

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsFallbackLiveThread_ReturnsFalse_WhenPromptIsIdle_AndActivityExpired() {
        var thread = new BackgroundTaskThreadSnapshot(
            ThreadId: "thread-1",
            Title: "Lyra Morn",
            ToolCallId: "tool-1",
            AgentId: "lyra-theme-plan-revise",
            AgentCardKey: "lyra-morn",
            StatusText: "Running",
            WasObservedAsBackgroundTask: true,
            IsPlaceholderThread: false,
            StartedAt: new DateTimeOffset(2026, 4, 15, 20, 41, 38, TimeSpan.Zero),
            LastObservedActivityAt: new DateTimeOffset(2026, 4, 15, 20, 47, 37, TimeSpan.Zero),
            CompletedAt: null);

        var result = BackgroundTaskStateResolver.IsFallbackLiveThread(
            thread,
            [],
            isPromptRunning: false,
            now: new DateTimeOffset(2026, 4, 15, 20, 50, 40, TimeSpan.Zero),
            recentActivityLinger: TimeSpan.FromSeconds(20),
            resolveSnapshotLabel: agent => agent.AgentId ?? string.Empty,
            resolveThreadLabel: snapshot => snapshot.Title);

        Assert.That(result, Is.False);
    }

    [Test]
    public void GetFallbackLiveThreads_IgnoresTerminalAndPlaceholderThreads() {
        var now = new DateTimeOffset(2026, 4, 15, 20, 50, 40, TimeSpan.Zero);
        var threads = new[] {
            new BackgroundTaskThreadSnapshot(
                ThreadId: "running",
                Title: "Lyra Morn",
                ToolCallId: "tool-1",
                AgentId: "lyra-theme-plan-revise",
                AgentCardKey: "lyra-morn",
                StatusText: "Running",
                WasObservedAsBackgroundTask: true,
                IsPlaceholderThread: false,
                StartedAt: now.AddMinutes(-10),
                LastObservedActivityAt: now.AddMinutes(-1),
                CompletedAt: null),
            new BackgroundTaskThreadSnapshot(
                ThreadId: "completed",
                Title: "Vesper Knox",
                ToolCallId: "tool-2",
                AgentId: "vesper-tests",
                AgentCardKey: "vesper-knox",
                StatusText: "Completed",
                WasObservedAsBackgroundTask: true,
                IsPlaceholderThread: false,
                StartedAt: now.AddMinutes(-8),
                LastObservedActivityAt: now.AddMinutes(-2),
                CompletedAt: now.AddMinutes(-1)),
            new BackgroundTaskThreadSnapshot(
                ThreadId: "placeholder",
                Title: "Placeholder",
                ToolCallId: "tool-3",
                AgentId: "placeholder",
                AgentCardKey: null,
                StatusText: "Running",
                WasObservedAsBackgroundTask: true,
                IsPlaceholderThread: true,
                StartedAt: now.AddMinutes(-8),
                LastObservedActivityAt: now.AddMinutes(-1),
                CompletedAt: null)
        };

        var result = BackgroundTaskStateResolver.GetFallbackLiveThreads(
            [],
            threads,
            isPromptRunning: true,
            now: now,
            recentActivityLinger: TimeSpan.FromSeconds(20),
            resolveSnapshotLabel: agent => agent.AgentId ?? string.Empty,
            resolveThreadLabel: snapshot => snapshot.Title);

        Assert.That(result.Select(thread => thread.ThreadId), Is.EqualTo(new[] { "running" }));
    }
}
