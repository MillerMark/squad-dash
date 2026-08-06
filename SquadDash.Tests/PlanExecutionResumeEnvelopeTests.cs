namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanExecutionResumeEnvelopeTests
{
    [Test]
    public void RestartReclaim_PreservesAttemptRecoveryLimitsAndTaskBaseline()
    {
        var attempt = PlanExecutionAttemptState.CreateGeneric(
            "PLAN-1", "PLAN-1-007", "revision-1", "C:\\repo");
        var prior = new ActiveLoopExecutionState(
            "old-loop.md",
            "PLAN-1",
            "PLAN-1",
            "revision-1",
            attempt,
            LastCompletedIteration: 6,
            RecoveryTaskId: "PLAN-1-007",
            RecoveryAttemptId: attempt.AttemptId,
            RepairRequestCount: 1,
            FreshAttemptCount: 1,
            TaskBaselineCommit: "abc1234");

        var resumed = PlanExecutionResumeEnvelope.Create(
            "new-loop.md", "PLAN-1", "revision-1", 6, prior, true);

        Assert.Multiple(() =>
        {
            Assert.That(resumed.PlanExecutionAttempt?.AttemptId, Is.EqualTo(attempt.AttemptId));
            Assert.That(resumed.RepairRequestCount, Is.EqualTo(1));
            Assert.That(resumed.FreshAttemptCount, Is.EqualTo(1));
            Assert.That(resumed.TaskBaselineCommit, Is.EqualTo("abc1234"));
            Assert.That(resumed.LoopPath, Is.EqualTo("new-loop.md"));
        });
    }

    [Test]
    public void ManualStart_ArchivesPriorAttemptAndStartsWithCleanRecoveryBudget()
    {
        var attempt = PlanExecutionAttemptState.CreateGeneric(
            "PLAN-1", "PLAN-1-007", "revision-1", "C:\\repo");
        var prior = new ActiveLoopExecutionState(
            "old-loop.md", "PLAN-1", "PLAN-1", "revision-1", attempt,
            RecoveryTaskId: "PLAN-1-007",
            RepairRequestCount: 1,
            FreshAttemptCount: 1,
            TaskBaselineCommit: "abc1234");

        var started = PlanExecutionResumeEnvelope.Create(
            "new-loop.md", "PLAN-1", "revision-1", 0, prior, false);

        Assert.Multiple(() =>
        {
            Assert.That(started.PlanExecutionAttempt, Is.Null);
            Assert.That(started.PreviousPlanExecutionAttempts?.Single().Status, Is.EqualTo("interrupted"));
            Assert.That(started.RepairRequestCount, Is.Zero);
            Assert.That(started.FreshAttemptCount, Is.Zero);
            Assert.That(started.TaskBaselineCommit, Is.Null);
        });
    }

    [Test]
    public void AssessedPartialRecovery_IsPersistedInFreshExecutionAndRestartReclaim()
    {
        var assessed = new AssessedRecoveryContinuationState(
            "PLAN-1",
            "PLAN-1-001",
            "Most of the task is already implemented.",
            ["Set the attachment file reference."]);

        var started = PlanExecutionResumeEnvelope.Create(
            "loop.md", "PLAN-1", "revision-1", 0, null, false, assessed);
        var json = System.Text.Json.JsonSerializer.Serialize(started);
        var reloaded = ActiveLoopExecutionState.Normalize(
            System.Text.Json.JsonSerializer.Deserialize<ActiveLoopExecutionState>(json));
        var resumed = PlanExecutionResumeEnvelope.Create(
            "loop.md", "PLAN-1", "revision-1", 1, reloaded, true);

        Assert.Multiple(() =>
        {
            Assert.That(resumed.AssessedRecoveryContinuation?.TaskId, Is.EqualTo("PLAN-1-001"));
            Assert.That(resumed.AssessedRecoveryContinuation?.Summary,
                Is.EqualTo("Most of the task is already implemented."));
            Assert.That(resumed.AssessedRecoveryContinuation?.RemainingWork,
                Is.EqualTo(new[] { "Set the attachment file reference." }));
        });
    }
}
