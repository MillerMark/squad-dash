using System;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanAgentAssignmentValidatorTests
{
    [Test]
    public void Validate_RequiresEveryAssignmentForTheExactTaskRevision()
    {
        var expected = new[] {
            new DecomposedAgentAssignment("talia-rune", "SDK", true),
            new DecomposedAgentAssignment("arjun-sen", "C#", true)
        };
        var observed = new[] {
            Launch("talia-rune", "TASK-20260728-001", "rev-1", verified: true),
            Launch("arjun-sen", "TASK-20260728-001", "old-revision", verified: true)
        };

        var error = PlanAgentAssignmentValidator.Validate(
            "TASK-20260728-001", "rev-1", expected, observed);

        Assert.That(error, Does.Contain("arjun-sen"));
    }

    [Test]
    public void Validate_AcceptsMultipleVerifiedAssignmentsAndIgnoresGenericChildren()
    {
        var expected = new[] {
            new DecomposedAgentAssignment("talia-rune", "SDK", true),
            new DecomposedAgentAssignment("arjun-sen", "C#", true)
        };
        var observed = new[] {
            Launch("talia-rune", "TASK-20260728-001", "rev-1", verified: true),
            Launch("arjun-sen", "TASK-20260728-001", "rev-1", verified: true),
            Launch(null, null, null, verified: false)
        };

        Assert.That(PlanAgentAssignmentValidator.Validate(
            "TASK-20260728-001", "rev-1", expected, observed), Is.Null);
    }

    [Test]
    public void ValidateWrapUp_RequiresActualPrimaryToMatchRequestedRosterHandle()
    {
        var expected = new[] { new DecomposedAgentAssignment("talia-rune", "SDK", true) };
        var reported = new[] {
            new DecomposeAgentExecution("talia-rune", "temporary-agent", ["research-child"])
        };

        Assert.That(PlanAgentAssignmentValidator.ValidateWrapUp(
            "TASK-20260728-001", expected, reported), Does.Contain("talia-rune"));
    }

    private static BackgroundAgentLaunchInfo Launch(
        string? handle,
        string? taskId,
        string? revision,
        bool verified) => new(
            ToolCallId: Guid.NewGuid().ToString("N"),
            TaskName: "worker",
            Mode: "background",
            DisplayName: verified ? "Roster Agent" : "Temporary Agent",
            AccentKey: handle,
            RoleText: null,
            Description: null,
            AgentType: "general-purpose",
            Prompt: null,
            AssignedTaskId: taskId,
            AssignedPlanRevision: revision,
            AssignedAgentHandle: handle,
            IsVerifiedRosterAssignment: verified);
}
