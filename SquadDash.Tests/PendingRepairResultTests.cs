using System;
using System.Text.Json;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PendingRepairResultTests
{
    private static DecomposeStepResult CreateValidResult(
        string groupId = "group-1",
        string taskId = "task-1",
        string revision = "rev-abc",
        string? attemptId = null) =>
        new(groupId, taskId, revision, "complete", "abc1234", "did the work",
            Array.Empty<string>(),
            new DecomposeStepVerification("passed", "dotnet test", "all pass"),
            null, attemptId);

    private static ActiveLoopExecutionState CreateExecution(
        string groupId = "group-1",
        string revision = "rev-abc",
        string? recoveryTaskId = "task-1",
        string? attemptId = "attempt-1",
        PendingRepairResult? pendingResult = null) =>
        new("loop.md", "*.cs",
            DecomposeGroupId: groupId,
            DecomposeRevision: revision,
            PlanExecutionAttempt: new PlanExecutionAttemptState(
                attemptId ?? "attempt-1", groupId, "task-1", revision, @"C:\ws",
                DateTimeOffset.UtcNow, Array.Empty<PlanExecutionAssignmentAttempt>()),
            RecoveryTaskId: recoveryTaskId,
            RecoveryAttemptId: attemptId,
            PendingRepairResult: pendingResult);

    private static PendingRepairResult CreatePending(
        string groupId = "group-1",
        string revision = "rev-abc",
        string taskId = "task-1",
        string? attemptId = "attempt-1",
        DecomposeStepResult? result = null,
        string? errorText = null)
    {
        var json = result is not null ? JsonSerializer.Serialize(result) : null;
        return new PendingRepairResult(groupId, revision, taskId, attemptId, json, errorText);
    }

    // ── 1. Normal consumption ──────────────────────────────────────────────────

    [Test]
    public void MatchingPendingResult_FinalizesWithoutDispatchingTaskAgain()
    {
        var execution = CreateExecution(pendingResult: CreatePending(
            result: CreateValidResult(attemptId: "attempt-1")));

        Assert.That(PlanRepairReplayPolicy.ShouldFinalizeWithoutDispatch(
            execution, "group-1", "rev-abc", "task-1"), Is.True);
    }

    [Test]
    public void CrossAttemptPendingResult_NeverSuppressesTaskDispatch()
    {
        var execution = CreateExecution(pendingResult: CreatePending(
            attemptId: "old-attempt",
            result: CreateValidResult(attemptId: "old-attempt")));

        Assert.That(PlanRepairReplayPolicy.ShouldFinalizeWithoutDispatch(
            execution, "group-1", "rev-abc", "task-1"), Is.False);
    }

    [Test]
    public void VerificationEnvelopeRepair_IsNeverConsumedAsTaskResultReplay()
    {
        var execution = CreateExecution(pendingResult: CreatePending(
            result: CreateValidResult(attemptId: "attempt-1"))) with
        {
            ActiveVerificationTaskId = "task-1",
            PendingTaskVerification = new PendingTaskVerification(
                "group-1", "task-1", "rev-abc", "{}", "abc0000", [], DateTimeOffset.UtcNow),
            VerificationEnvelopeRepairCount = 1,
        };

        Assert.Multiple(() =>
        {
            Assert.That(PlanRepairReplayPolicy.ShouldFinalizeWithoutDispatch(
                execution, "group-1", "rev-abc", "task-1"), Is.False);
            Assert.That(PlanRepairReplayPolicy.ShouldPersistTaskRepairResponse(execution), Is.False);
        });
    }

    [Test]
    public void ValidationResponse_IsNeverPersistedAsTaskRepairResult()
    {
        var execution = CreateExecution() with { ActiveValidationId = "validation-1" };

        Assert.That(PlanRepairReplayPolicy.ShouldPersistTaskRepairResponse(execution), Is.False);
    }

    [Test]
    public void NormalConsumption_PersistThenConsume_ClearsAfter()
    {
        var result = CreateValidResult(attemptId: "attempt-1");
        var pending = CreatePending(result: result);
        var execution = CreateExecution(pendingResult: pending);

        // Verify the pending result is present
        Assert.That(execution.PendingRepairResult, Is.Not.Null);
        Assert.That(execution.PendingRepairResult!.ResultJson, Is.Not.Null);

        // Simulate finalization consuming it
        var deserialized = JsonSerializer.Deserialize<DecomposeStepResult>(
            execution.PendingRepairResult.ResultJson!);
        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.GroupId, Is.EqualTo("group-1"));
        Assert.That(deserialized.TaskId, Is.EqualTo("task-1"));

        // Clear after consumption
        var cleared = execution with { PendingRepairResult = null };
        Assert.That(cleared.PendingRepairResult, Is.Null);
    }

    // ── 2. Restart replay ──────────────────────────────────────────────────────

    [Test]
    public void RestartReplay_PersistAndReload_ResultAvailable()
    {
        var result = CreateValidResult(attemptId: "attempt-1");
        var pending = CreatePending(result: result);
        var execution = CreateExecution(pendingResult: pending);

        // Simulate persist via JSON round-trip (as the store would do)
        var json = JsonSerializer.Serialize(execution);
        var reloaded = JsonSerializer.Deserialize<ActiveLoopExecutionState>(json);

        Assert.That(reloaded, Is.Not.Null);
        Assert.That(reloaded!.PendingRepairResult, Is.Not.Null);
        Assert.That(reloaded.PendingRepairResult!.ResultJson, Is.Not.Null);

        var deserializedResult = JsonSerializer.Deserialize<DecomposeStepResult>(
            reloaded.PendingRepairResult.ResultJson!);
        Assert.That(deserializedResult!.GroupId, Is.EqualTo("group-1"));
        Assert.That(deserializedResult.TaskId, Is.EqualTo("task-1"));
        Assert.That(deserializedResult.Status, Is.EqualTo("complete"));
    }

    // ── 3. Duplicate results (idempotent) ──────────────────────────────────────

    [Test]
    public void DuplicateResults_SameData_Idempotent()
    {
        var result = CreateValidResult(attemptId: "attempt-1");
        var pending1 = CreatePending(result: result);
        var pending2 = CreatePending(result: result);
        var execution = CreateExecution(pendingResult: pending1);

        // Persisting same result again produces equivalent state
        var updated = execution with { PendingRepairResult = pending2 };
        Assert.That(updated.PendingRepairResult!.ResultJson,
            Is.EqualTo(execution.PendingRepairResult!.ResultJson));
        Assert.That(updated.PendingRepairResult.GroupId,
            Is.EqualTo(execution.PendingRepairResult.GroupId));
    }

    // ── 4. Malformed results ───────────────────────────────────────────────────

    [Test]
    public void MalformedResults_NullResultJson_StoresErrorOnly()
    {
        var pending = CreatePending(
            result: null,
            errorText: "The response did not contain a valid payload.");
        var execution = CreateExecution(pendingResult: pending);

        Assert.That(execution.PendingRepairResult!.ResultJson, Is.Null);
        Assert.That(execution.PendingRepairResult.ErrorText, Is.Not.Null);
    }

    [Test]
    public void MalformedResults_CorruptJson_DeserializeFails()
    {
        var pending = new PendingRepairResult(
            "group-1", "rev-abc", "task-1", "attempt-1",
            "{ this is not valid json }", null);
        var execution = CreateExecution(pendingResult: pending);

        Assert.That(execution.PendingRepairResult!.ResultJson, Is.Not.Null);
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<DecomposeStepResult>(
                execution.PendingRepairResult.ResultJson!));
    }

    // ── 5. Stale attempt ───────────────────────────────────────────────────────

    [Test]
    public void StaleAttempt_MismatchedAttemptId_MatchReturnsFalse()
    {
        var pending = CreatePending(attemptId: "old-attempt");
        Assert.That(pending.Matches("group-1", "rev-abc", "new-attempt"), Is.False);
    }

    [Test]
    public void StaleAttempt_NullAttemptId_MatchesAny()
    {
        // If the pending result has no attempt ID, it matches any current attempt
        var pending = CreatePending(attemptId: null);
        Assert.That(pending.Matches("group-1", "rev-abc", "any-attempt"), Is.True);
    }

    // ── 6. Fresh retry clears pending result ───────────────────────────────────

    [Test]
    public void FreshRetry_NewAttemptCreation_ClearsPendingResult()
    {
        var pending = CreatePending(attemptId: "attempt-1");
        var execution = CreateExecution(pendingResult: pending);

        // Simulate fresh attempt creation (host clears pending result)
        var freshExecution = execution with {
            PlanExecutionAttempt = new PlanExecutionAttemptState(
                "attempt-2", "group-1", "task-1", "rev-abc", @"C:\ws",
                DateTimeOffset.UtcNow, Array.Empty<PlanExecutionAssignmentAttempt>()),
            RecoveryAttemptId = "attempt-2",
            FreshAttemptCount = 1,
            RepairRequestCount = 0,
            PendingRepairResult = null
        };

        Assert.That(freshExecution.PendingRepairResult, Is.Null);
        // Old execution still has it
        Assert.That(execution.PendingRepairResult, Is.Not.Null);
    }

    // ── 7. Two workspaces ──────────────────────────────────────────────────────

    [Test]
    public void TwoWorkspaces_NoCrossContamination()
    {
        var resultA = CreateValidResult(groupId: "group-A");
        var pendingA = CreatePending(groupId: "group-A", result: resultA);
        var executionA = CreateExecution(groupId: "group-A", pendingResult: pendingA);

        var executionB = CreateExecution(groupId: "group-B", pendingResult: null);

        Assert.That(executionA.PendingRepairResult, Is.Not.Null);
        Assert.That(executionB.PendingRepairResult, Is.Null);

        // Even if someone incorrectly tries to match A's result against B's context
        Assert.That(pendingA.Matches("group-B", "rev-abc", "attempt-1"), Is.False);
    }

    // ── 8. Group/revision mismatch ─────────────────────────────────────────────

    [Test]
    public void GroupRevisionMismatch_ConsumeRejects()
    {
        var pending = CreatePending(groupId: "group-1", revision: "rev-1");

        // Different group
        Assert.That(pending.Matches("group-2", "rev-1", "attempt-1"), Is.False);
        // Different revision
        Assert.That(pending.Matches("group-1", "rev-2", "attempt-1"), Is.False);
        // Both different
        Assert.That(pending.Matches("group-2", "rev-2", "attempt-1"), Is.False);
    }

    [Test]
    public void GroupRevisionMismatch_NormalizeDiscardsStalePending()
    {
        var pending = CreatePending(groupId: "group-old", revision: "rev-old");
        var execution = new ActiveLoopExecutionState(
            "loop.md", "*.cs",
            DecomposeGroupId: "group-new",
            DecomposeRevision: "rev-new",
            PendingRepairResult: pending);

        var normalized = ActiveLoopExecutionState.Normalize(execution);

        // Normalize should discard the pending result because group/revision don't match
        Assert.That(normalized, Is.Not.Null);
        Assert.That(normalized!.PendingRepairResult, Is.Null);
    }

    // ── Backward compatibility ─────────────────────────────────────────────────

    [Test]
    public void BackwardCompatibility_OldSerializedData_DeserializesWithNullPending()
    {
        // Simulate old JSON that doesn't have PendingRepairResult
        var oldJson = """
            {
                "LoopPath": "loop.md",
                "FilterText": "*.cs",
                "DecomposeGroupId": "group-1",
                "DecomposeRevision": "rev-abc",
                "LastCompletedIteration": 3,
                "RepairRequestCount": 1,
                "FreshAttemptCount": 0
            }
            """;

        var deserialized = JsonSerializer.Deserialize<ActiveLoopExecutionState>(oldJson);
        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.PendingRepairResult, Is.Null);
        Assert.That(deserialized.DecomposeGroupId, Is.EqualTo("group-1"));
        Assert.That(deserialized.LastCompletedIteration, Is.EqualTo(3));
    }

    [Test]
    public void Normalize_NullPendingResult_PassesThrough()
    {
        var execution = CreateExecution(pendingResult: null);
        var normalized = ActiveLoopExecutionState.Normalize(execution);
        Assert.That(normalized, Is.Not.Null);
        Assert.That(normalized!.PendingRepairResult, Is.Null);
    }

    [Test]
    public void Normalize_MatchingPendingResult_Preserved()
    {
        var pending = CreatePending(groupId: "group-1", revision: "rev-abc");
        var execution = CreateExecution(pendingResult: pending);
        var normalized = ActiveLoopExecutionState.Normalize(execution);
        Assert.That(normalized, Is.Not.Null);
        Assert.That(normalized!.PendingRepairResult, Is.Not.Null);
        Assert.That(normalized.PendingRepairResult!.GroupId, Is.EqualTo("group-1"));
    }
}
