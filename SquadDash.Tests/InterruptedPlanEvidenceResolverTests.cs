using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class InterruptedPlanEvidenceResolverTests
{
    [Test]
    public void FindTaskResults_PrefersOriginalHandoffBeforeLaterRepairAndFiltersIdentity()
    {
        var turns = new[]
        {
            Turn(ResultJson("PLAN-1", "TASK-1", "REV-1", "aaaaaaa", "attempt-1")),
            Turn(ResultJson("OTHER", "TASK-1", "REV-1", "bbbbbbb", "attempt-1")),
            Turn(ResultJson("PLAN-1", "TASK-1", "REV-1", "ccccccc", "attempt-1")),
        };

        var results = InterruptedPlanEvidenceResolver.FindTaskResults(
            turns, "PLAN-1", "REV-1", "TASK-1");

        Assert.That(results.Select(result => result.Commit),
            Is.EqualTo(new[] { "aaaaaaa", "ccccccc" }));
    }

    [Test]
    public void FindTaskResults_DeduplicatesRepeatedRepairCommit()
    {
        var turns = new[]
        {
            Turn(ResultJson("PLAN-1", "TASK-1", "REV-1", "aaaaaaa", "attempt-1")),
            Turn(ResultJson("PLAN-1", "TASK-1", "REV-1", "aaaaaaa", "attempt-1")),
        };

        var results = InterruptedPlanEvidenceResolver.FindTaskResults(
            turns, "PLAN-1", "REV-1", "TASK-1");

        Assert.That(results, Has.Count.EqualTo(1));
    }

    [Test]
    public void FindTaskResults_RecoversProtocolNamedFenceFromPersistedRawTextAfterRestart()
    {
        var rawResponse = $$"""
            Verification finished successfully.

            ```DECOMPOSE_STEP_RESULT_JSON
            {{ResultJson("PLAN-1", "TASK-1", "REV-1", "dfcac58", "attempt-1")}}
            ```
            """;
        var turn = new TranscriptTurnRecord(
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "prompt",
            string.Empty,
            string.Empty,
            ThinkingCollapsed: false,
            Tools: Array.Empty<TranscriptToolRecord>(),
            ResponseSegments: [new TranscriptResponseSegmentRecord(rawResponse)]);

        var results = InterruptedPlanEvidenceResolver.FindTaskResults(
            [turn], "PLAN-1", "REV-1", "TASK-1");

        Assert.That(results.Select(result => result.Commit), Is.EqualTo(new[] { "dfcac58" }));
    }

    [Test]
    public void RestartRecovery_KeepsRecoveredEvidenceWhileBackfillingRepairAssignment()
    {
        const string planId = "PLAN-1";
        const string taskId = "PLAN-1-AMD-001";
        const string revision = "REV-1";
        const string commit = "dfcac58";
        var rawResponse = $$"""
            ```DECOMPOSE_STEP_RESULT_JSON
            {{ResultJson(planId, taskId, revision, commit, "attempt-14a")}}
            ```
            """;
        var turn = new TranscriptTurnRecord(
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "prompt",
            string.Empty,
            string.Empty,
            ThinkingCollapsed: false,
            Tools: Array.Empty<TranscriptToolRecord>(),
            ResponseSegments: [new TranscriptResponseSegmentRecord(rawResponse)]);
        var result = InterruptedPlanEvidenceResolver.FindTaskResults(
            [turn], planId, revision, taskId).Single();
        var origin = new PlanTask(
            "PLAN-1-015", "Package milestone", "Package milestone", [taskId], "high",
            PlanTaskStatus.Pending,
            AgentAssignments: [new PlanAgentAssignment("talia-rune", "primary")],
            DisplayStepLabel: "15");
        var repair = new PlanTask(
            taskId, "Stabilize tests", "Stabilize tests", [], "high",
            PlanTaskStatus.Executing,
            AgentRoutingMode: "generic",
            GenericAgentReason: "This prerequisite was added by the coordinator during execution and has no preapproved roster assignment.",
            DisplayStepLabel: "14a",
            ExecutionOrigin: new PlanTaskExecutionOrigin(
                origin.TaskId, "coordinator", "Tests required stabilization.",
                DateTimeOffset.UtcNow, "attempt-insert"));
        var evidence = new PlanTaskCommitEvidence(
            taskId, result.ExecutionAttemptId, "300baeb", commit,
            result.Summary, result.Verification);
        var interrupted = new Plan(
            planId, revision, PlanSource.DecomposeDecision, PlanLifecycleStatus.Interrupted,
            "Plan", "feature/plan", "Summary", [repair, origin], [],
            new PlanProgress(0, 2, taskId), new PlanTimestamps(DateTimeOffset.UtcNow),
            new PlanInterruptionData(
                "Stopped", PlanRecoveryState.PendingRecovery, 1,
                InterruptedTaskId: taskId,
                LastCommit: "300baeb",
                TaskCommitEvidence: evidence,
                AttentionCategory: PlanAttentionCategory.AutomaticWorkExhausted));

        // Startup reconciles transcript evidence first, then repairs older plan metadata.
        var repaired = PlanStoreUpdater.RepairInconsistentState(interrupted);
        var actions = PlanRecoveryActionSetBuilder.Build(repaired, taskId, currentHead: commit);

        Assert.Multiple(() =>
        {
            Assert.That(repaired.InterruptionData?.TaskCommitEvidence?.Commit, Is.EqualTo(commit));
            Assert.That(repaired.Tasks.Single(task => task.TaskId == taskId)
                .AgentAssignments?.Single().AgentHandle, Is.EqualTo("talia-rune"));
            Assert.That(actions.Primary?.Kind, Is.EqualTo(PlanRecoverySurfaceActionKind.AcceptStep));
            Assert.That(actions.Primary?.Label, Is.EqualTo("Step 14a Is Complete & Continue"));
            Assert.That(PlanRecoveryLifecycleControlPolicy.ShouldShowOrdinaryControls(repaired, actions),
                Is.False);
        });
    }

    private static TranscriptTurnRecord Turn(string json) => new(
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        "prompt",
        string.Empty,
        string.Empty,
        ThinkingCollapsed: false,
        Tools: Array.Empty<TranscriptToolRecord>(),
        ResponseSegments:
        [
            new TranscriptResponseSegmentRecord(string.Empty)
            {
                ProtocolJsonBlocks =
                [
                    new TranscriptProtocolJsonBlock(
                        DecomposeStepResultParser.Marker.TrimEnd(':'),
                        json),
                ],
            },
        ]);

    private static string ResultJson(
        string planId,
        string taskId,
        string revision,
        string commit,
        string attemptId) =>
        $$"""
        {
          "groupId": "{{planId}}",
          "taskId": "{{taskId}}",
          "revision": "{{revision}}",
          "status": "complete",
          "commit": "{{commit}}",
          "summary": "Completed work.",
          "remainingWork": [],
          "verification": {
            "status": "passed",
            "command": "test",
            "summary": "Tests passed."
          },
          "executionAttemptId": "{{attemptId}}"
        }
        """;
}
