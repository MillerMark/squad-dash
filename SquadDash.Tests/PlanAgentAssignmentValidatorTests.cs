using System;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanAgentAssignmentValidatorTests
{
    private static readonly DecomposedAgentAssignment Expected =
        new("talia-rune", "SDK", true);

    [Test]
    public void Validate_RejectsMissingOrStaleAttemptEvidence()
    {
        Assert.That(PlanAgentAssignmentValidator.Validate(
            "TASK-20260728-001", "rev-1", [Expected], null), Does.Contain("no host-owned"));

        var stale = Attempt(taskId: "TASK-20260728-001", revision: "old-revision", launched: true);
        Assert.That(PlanAgentAssignmentValidator.Validate(
            "TASK-20260728-001", "rev-1", [Expected], stale), Does.Contain("stale"));
    }

    [Test]
    public void Validate_AcceptsCurrentHostObservedLaunchAndContextReads()
    {
        var attempt = Attempt(launched: true, requiredContext: ["C:\\repo\\.squad\\history.md"]);
        attempt = attempt.RecordContextRead("tool-primary", "C:\\repo\\.squad\\history.md");

        Assert.That(PlanAgentAssignmentValidator.Validate(
            attempt.TaskId, attempt.Revision, [Expected], attempt), Is.Null);
    }

    [Test]
    public void Validate_RejectsUnexpectedCoordinatorPrimaryAndForbiddenChildren()
    {
        var unexpected = Attempt(launched: true).RecordUnexpectedPrimaryLaunch("tool-generic");
        Assert.That(PlanAgentAssignmentValidator.Validate(
            unexpected.TaskId, unexpected.Revision, [Expected], unexpected), Does.Contain("undeclared"));

        var noChildrenExpected = Expected with { AllowGenericChildren = false };
        var child = Attempt(launched: true, allowChildren: false)
            .RecordChildLaunch("tool-primary", "tool-child");
        Assert.That(PlanAgentAssignmentValidator.Validate(
            child.TaskId, child.Revision, [noChildrenExpected], child), Does.Contain("forbids"));
    }

    [Test]
    public void RecordPrimaryLaunch_DoesNotOverwriteFirstVerifiedLaunch()
    {
        var attempt = Attempt();
        var first = new BackgroundAgentLaunchInfo(
            "tool-first", "worker", "background", "Talia Rune", "talia-rune", "SDK",
            null, "general-purpose", null, attempt.TaskId, attempt.Revision, "talia-rune", true,
            attempt.AttemptId);
        var second = first with { ToolCallId = "tool-second" };

        attempt = attempt.RecordPrimaryLaunch(first).RecordPrimaryLaunch(second);

        Assert.Multiple(() =>
        {
            Assert.That(attempt.Assignments.Single().PrimaryToolCallId, Is.EqualTo("tool-first"));
            Assert.That(attempt.UnexpectedPrimaryToolCallIds, Is.EqualTo(new[] { "tool-second" }));
        });
    }

    [Test]
    public void Validate_RequiresSuccessfulPrimaryLifecycleCompletion()
    {
        var running = Attempt();
        var launch = new BackgroundAgentLaunchInfo(
            "tool-primary", "worker", "background", "Talia Rune", "talia-rune", "SDK",
            null, "general-purpose", null, running.TaskId, running.Revision, "talia-rune", true,
            running.AttemptId);
        running = running.RecordPrimaryLaunch(launch);

        Assert.That(PlanAgentAssignmentValidator.Validate(
            running.TaskId, running.Revision, [Expected], running), Does.Contain("complete successfully"));

        var failed = running.RecordPrimaryCompletion("tool-primary", DateTimeOffset.UtcNow, succeeded: false);
        Assert.That(PlanAgentAssignmentValidator.Validate(
            failed.TaskId, failed.Revision, [Expected], failed), Does.Contain("complete successfully"));

        var completed = running.RecordPrimaryCompletion("tool-primary", DateTimeOffset.UtcNow, succeeded: true);
        Assert.That(PlanAgentAssignmentValidator.Validate(
            completed.TaskId, completed.Revision, [Expected], completed), Is.Null);
    }

    [Test]
    public void ValidateWrapUp_CorrelatesAttemptPrimaryAndChildToolCalls()
    {
        var attempt = Attempt(launched: true)
            .RecordChildLaunch("tool-primary", "tool-child");
        var valid = new[] {
            new DecomposeAgentExecution(
                "talia-rune", "talia-rune", ["tool-child"], "tool-primary")
        };

        Assert.That(PlanAgentAssignmentValidator.ValidateWrapUp(
            attempt.TaskId, [Expected], attempt, attempt.AttemptId, valid), Is.Null);

        var wrong = new[] {
            new DecomposeAgentExecution(
                "talia-rune", "talia-rune", ["invented-child"], "wrong-primary")
        };
        Assert.That(PlanAgentAssignmentValidator.ValidateWrapUp(
            attempt.TaskId, [Expected], attempt, attempt.AttemptId, wrong), Does.Contain("correlate"));
    }

    [Test]
    public void ValidateGeneric_RequiresOneCurrentPrimaryAndNoChildren()
    {
        var generic = PlanExecutionAttemptState.CreateGeneric(
            "TASK-20260728", "TASK-20260728-001", "rev-1", TestContext.CurrentContext.WorkDirectory);
        var launch = new BackgroundAgentLaunchInfo(
            "tool-generic", "worker", "background", "Temporary Agent", null, null, null,
            "general-purpose", null, null, null, null, false);
        generic = generic.RecordGenericPrimaryLaunch(launch)
            .RecordPrimaryCompletion("tool-generic", DateTimeOffset.UtcNow, succeeded: true);

        Assert.That(PlanAgentAssignmentValidator.ValidateGeneric(
            generic.TaskId, generic.Revision, generic, generic.AttemptId, null), Is.Null);

        var withChild = generic.RecordChildLaunch("tool-generic", "tool-child");
        Assert.That(PlanAgentAssignmentValidator.ValidateGeneric(
            withChild.TaskId, withChild.Revision, withChild, withChild.AttemptId, null), Does.Contain("child"));

        var secondPrimary = generic.RecordGenericPrimaryLaunch(launch with { ToolCallId = "tool-second" });
        Assert.That(PlanAgentAssignmentValidator.ValidateGeneric(
            secondPrimary.TaskId, secondPrimary.Revision, secondPrimary, secondPrimary.AttemptId, null), Does.Contain("more than one"));
    }

    private static PlanExecutionAttemptState Attempt(
        string taskId = "TASK-20260728-001",
        string revision = "rev-1",
        bool launched = false,
        bool allowChildren = true,
        string[]? requiredContext = null)
    {
        var assignment = new PlanExecutionAssignmentAttempt(
            "talia-rune", "SDK", allowChildren, "capability", "C:\\repo\\charter.md", "hash",
            requiredContext ?? [],
            PrimaryToolCallId: launched ? "tool-primary" : null,
            LaunchedAt: launched ? DateTimeOffset.UtcNow : null,
            CompletedAt: launched ? DateTimeOffset.UtcNow : null,
            Succeeded: launched ? true : null);
        return new PlanExecutionAttemptState(
            "attempt-1", "TASK-20260728", taskId, revision, "C:\\repo", DateTimeOffset.UtcNow,
            [assignment]);
    }
}
