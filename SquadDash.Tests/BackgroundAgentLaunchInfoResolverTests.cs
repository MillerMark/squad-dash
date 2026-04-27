using System;
using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class BackgroundAgentLaunchInfoResolverTests {
    [Test]
    public void TryResolve_MatchesRosterAgentFromTaskNamePrefix() {
        using var document = JsonDocument.Parse("""
            {
              "name": "wanda-review-3",
              "agent_type": "general-purpose",
              "description": "Review options page changes",
              "prompt": "Review the latest model options page changes."
            }
            """);

        var resolved = BackgroundAgentLaunchInfoResolver.TryResolve(
            "tool-1",
            document.RootElement,
            [
                new TeamAgentDescriptor("Wanda Maximoff", "wanda-maximoff", "Code Review")
            ]);

        Assert.That(resolved, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(resolved!.ToolCallId, Is.EqualTo("tool-1"));
            Assert.That(resolved.TaskName, Is.EqualTo("wanda-review-3"));
            Assert.That(resolved.DisplayName, Is.EqualTo("Wanda Maximoff"));
            Assert.That(resolved.AccentKey, Is.EqualTo("wanda-maximoff"));
            Assert.That(resolved.RoleText, Is.EqualTo("Code Review"));
        });
    }

    [Test]
    public void TryResolve_FallsBackToHumanizedTaskPrefixWhenRosterMatchIsMissing() {
        using var document = JsonDocument.Parse("""
            {
              "name": "wanda-layout",
              "agent_type": "general-purpose",
              "description": "Fix Gemini row heights"
            }
            """);

        var resolved = BackgroundAgentLaunchInfoResolver.TryResolve(
            "tool-2",
            document.RootElement,
            Array.Empty<TeamAgentDescriptor>());

        Assert.That(resolved, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(resolved!.DisplayName, Is.EqualTo("Wanda"));
            Assert.That(resolved.AccentKey, Is.Null);
            Assert.That(resolved.RoleText, Is.Null);
        });
    }

    [Test]
    public void TryResolve_UsesHumanizedTaskNameForGenericWorkers() {
        using var document = JsonDocument.Parse("""
            {
              "name": "code-review",
              "agent_type": "general-purpose",
              "description": "Review options page changes"
            }
            """);

        var resolved = BackgroundAgentLaunchInfoResolver.TryResolve(
            "tool-3",
            document.RootElement,
            Array.Empty<TeamAgentDescriptor>());

        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved!.DisplayName, Is.EqualTo("Code Review"));
    }

    [Test]
    public void TryResolve_PrefersRosterMatchFromPromptWhenTaskNameIsInternal() {
        using var document = JsonDocument.Parse("""
            {
              "name": "bruce-banner",
              "agent_type": "general-purpose",
              "description": "Full-capability agent running in a subprocess.",
              "prompt": "Have Ant-Man handle this task and report back."
            }
            """);

        var resolved = BackgroundAgentLaunchInfoResolver.TryResolve(
            "tool-4",
            document.RootElement,
            [
                new TeamAgentDescriptor("Ant Man", "ant-man", "Research")
            ]);

        Assert.That(resolved, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(resolved!.DisplayName, Is.EqualTo("Ant Man"));
            Assert.That(resolved.AccentKey, Is.EqualTo("ant-man"));
            Assert.That(resolved.RoleText, Is.EqualTo("Research"));
        });
    }

    [Test]
    public void TryResolve_MatchesScribeFromNestedTaskMetadata() {
        using var document = JsonDocument.Parse("""
            {
              "agent_type": "general-purpose",
              "description": "📋 Scribe: Log session & merge decisions",
              "mode": "background",
              "model": "claude-haiku-4.5",
              "name": "scribe-docs-panel-log",
              "prompt": "You are the Scribe. Read .squad/agents/scribe/charter.md."
            }
            """);

        var resolved = BackgroundAgentLaunchInfoResolver.TryResolve(
            "tool-scribe",
            document.RootElement,
            [
                new TeamAgentDescriptor("Scribe", "scribe", "Session Logger")
            ]);

        Assert.That(resolved, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(resolved!.DisplayName, Is.EqualTo("Scribe"));
            Assert.That(resolved.AccentKey, Is.EqualTo("scribe"));
            Assert.That(resolved.RoleText, Is.EqualTo("Session Logger"));
        });
    }
}
