using System;
using System.IO;
using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanExecutionAttemptTests
{
    [Test]
    public void FindAuthorization_IsBoundToAttemptTaskRevisionHandleAndCapability()
    {
        var authorization = new PlanExecutionAssignmentAttempt(
            "talia-rune", "SDK", false, "capability", "charter.md", "hash", []);
        var attempt = new PlanExecutionAttemptState(
            "attempt-1", "PLAN-20260728", "PLAN-20260728-001", "rev-1",
            TestContext.CurrentContext.WorkDirectory, DateTimeOffset.UtcNow, [authorization]);

        Assert.That(attempt.FindAuthorization(
            "attempt-1", "PLAN-20260728-001", "rev-1", "talia-rune", "capability"), Is.Not.Null);
        Assert.That(attempt.FindAuthorization(
            "old-attempt", "PLAN-20260728-001", "rev-1", "talia-rune", "capability"), Is.Null);
        Assert.That(attempt.FindAuthorization(
            "attempt-1", "PLAN-20260728-001", "rev-1", "talia-rune", "copied-wrong-capability"), Is.Null);
    }

    [Test]
    public void ContextEvidence_AcceptsOnlyStructuredFileReadTools()
    {
        using var readArgs = JsonDocument.Parse("""{"path":".squad/agents/talia-rune/history.md"}""");
        var workspace = TestContext.CurrentContext.WorkDirectory;
        var read = PlanContextReadEvidence.TryResolveFullPath(new SquadSdkEvent {
            ToolName = "view",
            Args = readArgs.RootElement.Clone()
        }, workspace);
        var shell = PlanContextReadEvidence.TryResolveFullPath(new SquadSdkEvent {
            ToolName = "exec_command",
            Command = "type .squad\\agents\\talia-rune\\history.md",
            Args = readArgs.RootElement.Clone()
        }, workspace);

        Assert.Multiple(() => {
            Assert.That(read, Does.EndWith("history.md"));
            Assert.That(shell, Is.Null, "Shell text is model-controlled and must not count as structured read evidence.");
        });
    }

    [Test]
    public void RoutingContext_IsBoundToTheCurrentIterationAttempt()
    {
        var squad = Path.Combine(Path.GetTempPath(), "squaddash-routing-attempt-" + Guid.NewGuid().ToString("N"));
        var agentFolder = Path.Combine(squad, "agents", "talia-rune");
        Directory.CreateDirectory(agentFolder);
        try
        {
            File.WriteAllText(Path.Combine(squad, "team.md"), """
                | Name | Role | Charter | Status |
                |---|---|---|---|
                | Talia Rune | SDK | agents/talia-rune/charter.md | active |
                """);
            const string charter = "# Talia\nOwn the SDK boundary.";
            var charterPath = Path.Combine(agentFolder, "charter.md");
            File.WriteAllText(charterPath, charter);
            var authorization = new PlanExecutionAssignmentAttempt(
                "talia-rune", "SDK", false, "cap", charterPath,
                PlanExecutionAttemptState.Sha256(charter), []);
            var attempt = new PlanExecutionAttemptState(
                "attempt-task-2", "PLAN-20260728", "PLAN-20260728-002", "rev-2",
                Path.GetDirectoryName(squad)!, DateTimeOffset.UtcNow, [authorization]);

            var context = DecomposePlanningInstructions.BuildPlanStepRoutingContext(
                squad,
                "PLAN-20260728-002",
                "Implement task two",
                "Task two details",
                [new DecomposedAgentAssignment("talia-rune", "SDK", false)],
                "rev-2",
                attempt);

            Assert.Multiple(() => {
                Assert.That(context, Does.Contain("attempt-task-2"));
                Assert.That(context, Does.Contain("PLAN-20260728-002"));
                Assert.That(context, Does.Not.Contain("PLAN-20260728-001"));
                Assert.That(context, Does.Contain(charter));
            });
        }
        finally
        {
            Directory.Delete(squad, recursive: true);
        }
    }
}
