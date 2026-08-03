using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanExecutionRecoveryPolicyTests
{
    private static readonly DecomposedAgentAssignment Assignment =
        new("vesper-knox", "Test verification", false);

    [Test]
    public void AdditionalCoordinatorHelpers_DoNotInvalidateCompletedNamedWork()
    {
        var contaminated = Attempt() with { UnexpectedPrimaryToolCallIds = ["tool-wrong"] };

        Assert.Multiple(() =>
        {
            Assert.That(
                PlanExecutionRecoveryPolicy.Resolve(contaminated, [Assignment], 0, 0),
                Is.EqualTo(PlanExecutionRecoveryAction.RequestRepair));
            Assert.That(
                PlanExecutionRecoveryPolicy.Resolve(contaminated, [Assignment], 0, 1),
                Is.EqualTo(PlanExecutionRecoveryAction.RequestRepair));
        });
    }

    [Test]
    public void OrdinaryEnvelopeRepair_IsBoundedAcrossRounds()
    {
        var clean = Attempt();

        Assert.Multiple(() =>
        {
            Assert.That(
                PlanExecutionRecoveryPolicy.Resolve(clean, [Assignment], 0, 0),
                Is.EqualTo(PlanExecutionRecoveryAction.RequestRepair));
            Assert.That(
                PlanExecutionRecoveryPolicy.Resolve(clean, [Assignment], 1, 0),
                Is.EqualTo(PlanExecutionRecoveryAction.Block));
        });
    }

    [Test]
    public void NamedAgentChildLaunch_IsAdvisoryNotTerminal()
    {
        var contaminated = Attempt() with
        {
            Assignments = [Attempt().Assignments[0] with { ChildToolCallIds = ["tool-child"] }]
        };

        Assert.That(
            PlanExecutionRecoveryPolicy.HasTerminalEvidenceContamination(contaminated, [Assignment]),
            Is.False);
    }

    [Test]
    public void AllowedChildLaunch_DoesNotContaminateAttempt()
    {
        var allowed = Assignment with { AllowGenericChildren = true };
        var attempt = Attempt() with
        {
            Assignments = [Attempt().Assignments[0] with { ChildToolCallIds = ["tool-child"] }]
        };

        Assert.That(
            PlanExecutionRecoveryPolicy.HasTerminalEvidenceContamination(attempt, [allowed]),
            Is.False);
    }

    [Test]
    public void FailedObservedPrimaryLaunch_RequiresFreshAttempt()
    {
        var attempt = Attempt() with
        {
            Assignments = [Attempt().Assignments[0] with
            {
                PrimaryToolCallId = "tool-primary",
                CompletedAt = DateTimeOffset.UtcNow,
                Succeeded = false
            }]
        };

        Assert.That(
            PlanExecutionRecoveryPolicy.Resolve(attempt, [Assignment], 0, 0),
            Is.EqualTo(PlanExecutionRecoveryAction.StartFreshAttempt));
    }

    [Test]
    public void CompletedPrimaryMissingObservedContext_UsesEnvelopeRepair()
    {
        var evidence = Attempt().Assignments[0] with
        {
            PrimaryToolCallId = "tool-primary",
            CompletedAt = DateTimeOffset.UtcNow,
            Succeeded = true,
            RequiredContextPaths = ["C:\\repo\\history.md"],
            ObservedContextPaths = []
        };
        var attempt = Attempt() with { Assignments = [evidence] };

        Assert.That(
            PlanExecutionRecoveryPolicy.Resolve(attempt, [Assignment], 0, 0),
            Is.EqualTo(PlanExecutionRecoveryAction.RequestRepair));
    }

    [Test]
    public void ArchiveRejectedAttempt_PreservesEvidenceWithoutCopyingItToFreshAttempt()
    {
        var rejected = Attempt() with { UnexpectedPrimaryToolCallIds = ["tool-wrong"] };
        var fresh = Attempt("attempt-fresh");

        var history = PlanExecutionRecoveryPolicy.ArchiveRejectedAttempt([], rejected, fresh);

        Assert.Multiple(() =>
        {
            Assert.That(history, Has.Count.EqualTo(1));
            Assert.That(history[0].AttemptId, Is.EqualTo("attempt-old"));
            Assert.That(history[0].Status, Is.EqualTo("rejected"));
            Assert.That(history[0].UnexpectedPrimaryToolCallIds, Is.EqualTo(new[] { "tool-wrong" }));
            Assert.That(fresh.UnexpectedPrimaryToolCallIds, Is.Null);
        });
    }

    [Test]
    public void ArchiveRejectedAttempt_RejectsSameAttemptId()
    {
        var attempt = Attempt();

        Assert.That(
            () => PlanExecutionRecoveryPolicy.ArchiveRejectedAttempt([], attempt, attempt),
            Throws.ArgumentException);
    }

    private static PlanExecutionAttemptState Attempt(string id = "attempt-old") =>
        new(
            id,
            "PLAN-1",
            "PLAN-1-007",
            "revision-1",
            "C:\\repo",
            DateTimeOffset.UtcNow,
            [new PlanExecutionAssignmentAttempt(
                "vesper-knox",
                "Test verification",
                false,
                "capability",
                "C:\\repo\\.squad\\agents\\vesper-knox\\charter.md",
                "hash",
                [])]);
}
