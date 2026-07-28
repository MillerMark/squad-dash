using System;
using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class BackgroundAgentLaunchInfoResolverTests {
    [Test]
    public void TryResolve_DoesNotTreatRosterLikeTaskNameAsVerifiedAssignment() {
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
            Assert.That(resolved.DisplayName, Is.EqualTo("Temporary Agent"));
            Assert.That(resolved.AccentKey, Is.Null);
            Assert.That(resolved.RoleText, Is.Null);
            Assert.That(resolved.IsVerifiedRosterAssignment, Is.False);
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
            Assert.That(resolved!.DisplayName, Is.EqualTo("Temporary Agent"));
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
        Assert.That(resolved!.DisplayName, Is.EqualTo("Temporary Agent"));
    }

    [Test]
    public void TryResolve_DoesNotTrustRosterNameInFreeFormPrompt() {
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
            Assert.That(resolved!.DisplayName, Is.EqualTo("Temporary Agent"));
            Assert.That(resolved.AccentKey, Is.Null);
            Assert.That(resolved.RoleText, Is.Null);
            Assert.That(resolved.IsVerifiedRosterAssignment, Is.False);
        });
    }

    [Test]
    public void TryResolve_DoesNotTrustCharterInstructionWithoutAssignmentEnvelope() {
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
            Assert.That(resolved!.DisplayName, Is.EqualTo("Temporary Agent"));
            Assert.That(resolved.AccentKey, Is.Null);
            Assert.That(resolved.RoleText, Is.Null);
            Assert.That(resolved.IsVerifiedRosterAssignment, Is.False);
        });
    }

    [Test]
    public void TryResolve_VerifiesExactRosterHandleFromAssignmentEnvelope() {
        using var document = JsonDocument.Parse("""
            {
              "agent_type": "general-purpose",
              "name": "talia-implementation",
              "description": "Implement SDK work",
              "prompt": "SQUADDASH_AGENT_ASSIGNMENT_JSON:\n{\"taskId\":\"PLAN-20260728-001\",\"revision\":\"rev-1\",\"agentHandle\":\"talia-rune\",\"role\":\"implementer\"}"
            }
            """);

        var resolved = BackgroundAgentLaunchInfoResolver.TryResolve(
            "tool-verified",
            document.RootElement,
            [new TeamAgentDescriptor("Talia Rune", "talia-rune", "SDK Bridge")]);

        Assert.That(resolved, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(resolved!.DisplayName, Is.EqualTo("Talia Rune"));
            Assert.That(resolved.AccentKey, Is.EqualTo("talia-rune"));
            Assert.That(resolved.AssignedTaskId, Is.EqualTo("PLAN-20260728-001"));
            Assert.That(resolved.AssignedPlanRevision, Is.EqualTo("rev-1"));
            Assert.That(resolved.AssignedAgentHandle, Is.EqualTo("talia-rune"));
            Assert.That(resolved.IsVerifiedRosterAssignment, Is.True);
        });
    }
}
