using System;
using System.IO;
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
    public void TryResolve_VerifiesHostValidatedNamedAgentToolHandleOutsidePlanAttempt() {
        using var document = JsonDocument.Parse("""
            {
              "agent_handle": "lyra-morn",
              "name": "profile-routing",
              "description": "Review model profile routing",
              "prompt": "Inspect the named-agent provider selection."
            }
            """);

        var resolved = BackgroundAgentLaunchInfoResolver.TryResolve(
            "tool-lyra",
            document.RootElement,
            [new TeamAgentDescriptor("Lyra Morn", "lyra-morn", "Model Profiles")],
            launchedByCoordinator: true,
            trustedNamedAgentHandle: "lyra-morn");

        Assert.That(resolved, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(resolved!.DisplayName, Is.EqualTo("Lyra Morn"));
            Assert.That(resolved.AccentKey, Is.EqualTo("lyra-morn"));
            Assert.That(resolved.AssignedAgentHandle, Is.EqualTo("lyra-morn"));
            Assert.That(resolved.IsVerifiedRosterAssignment, Is.True);
        });
    }

    [Test]
    public void TryResolve_DoesNotVerifyUnknownHostValidatedNamedAgentHandle() {
        using var document = JsonDocument.Parse("""
            {
              "agent_handle": "invented-agent",
              "description": "Attempt an unknown named launch",
              "prompt": "Do the work."
            }
            """);

        var resolved = BackgroundAgentLaunchInfoResolver.TryResolve(
            "tool-invented",
            document.RootElement,
            [new TeamAgentDescriptor("Lyra Morn", "lyra-morn", "Model Profiles")],
            launchedByCoordinator: true,
            trustedNamedAgentHandle: "invented-agent");

        Assert.That(resolved, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(resolved!.DisplayName, Is.EqualTo("Temporary Agent"));
            Assert.That(resolved.AccentKey, Is.Null);
            Assert.That(resolved.IsVerifiedRosterAssignment, Is.False);
        });
    }

    [Test]
    public void TryResolve_RequiresAssignmentEnvelopeWhenPlanAttemptIsActive() {
        using var document = JsonDocument.Parse("""
            {
              "agent_handle": "lyra-morn",
              "description": "Implement a plan step",
              "prompt": "Implement the profile routing step."
            }
            """);
        var assignment = new PlanExecutionAssignmentAttempt(
            "lyra-morn", "implementer", false, "capability", "charter.md", "hash", []);
        var attempt = new PlanExecutionAttemptState(
            "attempt-1", "PLAN-1", "PLAN-1-001", "rev-1", "C:\\workspace",
            DateTimeOffset.UtcNow, [assignment]);

        var resolved = BackgroundAgentLaunchInfoResolver.TryResolve(
            "tool-plan-lyra",
            document.RootElement,
            [new TeamAgentDescriptor("Lyra Morn", "lyra-morn", "Model Profiles")],
            attempt,
            launchedByCoordinator: true,
            trustedNamedAgentHandle: "lyra-morn");

        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved!.IsVerifiedRosterAssignment, Is.False);
        Assert.That(resolved.DisplayName, Is.EqualTo("Temporary Agent"));
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
    public void TryResolve_DoesNotVerifyModelAuthoredEnvelopeWithoutHostAttempt() {
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
            Assert.That(resolved!.DisplayName, Is.EqualTo("Temporary Agent"));
            Assert.That(resolved.AccentKey, Is.Null);
            Assert.That(resolved.AssignedTaskId, Is.EqualTo("PLAN-20260728-001"));
            Assert.That(resolved.AssignedPlanRevision, Is.EqualTo("rev-1"));
            Assert.That(resolved.AssignedAgentHandle, Is.EqualTo("talia-rune"));
            Assert.That(resolved.IsVerifiedRosterAssignment, Is.False);
        });
    }

    [Test]
    public void TryResolve_VerifiesHostAttemptOnlyForCoordinatorLaunchWithCapability()
    {
        var temp = Path.Combine(Path.GetTempPath(), "squaddash-attempt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var charterPath = Path.Combine(temp, "charter.md");
            const string charter = "# Talia charter\nOwn SDK implementation.";
            File.WriteAllText(charterPath, charter);
            var authorization = new PlanExecutionAssignmentAttempt(
                "talia-rune", "implementer", false, "host-capability", charterPath,
                PlanExecutionAttemptState.Sha256(charter), []);
            var attempt = new PlanExecutionAttemptState(
                "attempt-1", "PLAN-20260728", "PLAN-20260728-001", "rev-1", temp,
                DateTimeOffset.UtcNow, [authorization]);
            var envelope = JsonSerializer.Serialize(new {
                attemptId = attempt.AttemptId,
                taskId = attempt.TaskId,
                revision = attempt.Revision,
                agentHandle = authorization.AgentHandle,
                role = authorization.Role,
                allowGenericChildren = authorization.AllowGenericChildren,
                capability = authorization.Capability,
                charterSha256 = authorization.CharterSha256
            });
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(new {
                agent_type = "general-purpose",
                name = "talia-implementation",
                prompt = $"SQUADDASH_AGENT_ASSIGNMENT_JSON:\n{envelope}\n{charter}"
            }));

            var resolved = BackgroundAgentLaunchInfoResolver.TryResolve(
                "tool-verified",
                document.RootElement,
                [new TeamAgentDescriptor("Talia Rune", "talia-rune", "SDK Bridge")],
                attempt,
                launchedByCoordinator: true);

            Assert.That(resolved, Is.Not.Null);
            Assert.Multiple(() => {
                Assert.That(resolved!.DisplayName, Is.EqualTo("Talia Rune"));
                Assert.That(resolved.AssignedAttemptId, Is.EqualTo("attempt-1"));
                Assert.That(resolved.IsVerifiedRosterAssignment, Is.True);
            });

            var nested = BackgroundAgentLaunchInfoResolver.TryResolve(
                "tool-nested",
                document.RootElement,
                [new TeamAgentDescriptor("Talia Rune", "talia-rune", "SDK Bridge")],
                attempt,
                launchedByCoordinator: false);
            Assert.That(nested!.IsVerifiedRosterAssignment, Is.False);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Test]
    public void TryResolve_DoesNotDependOnModelCopyingCharterTextExactly()
    {
        var temp = Path.Combine(Path.GetTempPath(), "squaddash-attempt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var charterPath = Path.Combine(temp, "charter.md");
            const string charter = "# Talia charter\r\nOwn SDK implementation.\r\n";
            File.WriteAllText(charterPath, charter);
            var authorization = new PlanExecutionAssignmentAttempt(
                "talia-rune", "implementer", false, "host-capability", charterPath,
                PlanExecutionAttemptState.Sha256(charter), []);
            var attempt = new PlanExecutionAttemptState(
                "attempt-1", "PLAN-20260728", "PLAN-20260728-001", "rev-1", temp,
                DateTimeOffset.UtcNow, [authorization]);
            var envelope = JsonSerializer.Serialize(new {
                attemptId = attempt.AttemptId,
                taskId = attempt.TaskId,
                revision = attempt.Revision,
                agentHandle = authorization.AgentHandle,
                role = authorization.Role,
                allowGenericChildren = authorization.AllowGenericChildren,
                capability = authorization.Capability,
                charterSha256 = authorization.CharterSha256
            });

            BackgroundAgentLaunchInfo? Resolve(string transportedCharter)
            {
                using var document = JsonDocument.Parse(JsonSerializer.Serialize(new {
                    agent_type = "general-purpose",
                    name = "talia-implementation",
                    prompt = $"SQUADDASH_AGENT_ASSIGNMENT_JSON:\n{envelope}\n{transportedCharter}"
                }));
                return BackgroundAgentLaunchInfoResolver.TryResolve(
                    "tool-verified",
                    document.RootElement,
                    [new TeamAgentDescriptor("Talia Rune", "talia-rune", "SDK Bridge")],
                    attempt,
                    launchedByCoordinator: true);
            }

            var normalized = Resolve("# Talia charter\nOwn SDK implementation.");
            var modified = Resolve("# Talia charter\nOwn UI implementation.");
            var omitted = Resolve("Read the authoritative charter file before working.");

            Assert.Multiple(() => {
                Assert.That(normalized!.DisplayName, Is.EqualTo("Talia Rune"));
                Assert.That(normalized.IsVerifiedRosterAssignment, Is.True);
                Assert.That(modified!.DisplayName, Is.EqualTo("Talia Rune"));
                Assert.That(modified.IsVerifiedRosterAssignment, Is.True);
                Assert.That(omitted!.DisplayName, Is.EqualTo("Talia Rune"));
                Assert.That(omitted.IsVerifiedRosterAssignment, Is.True);
            });
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
