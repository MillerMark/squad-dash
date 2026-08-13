using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// End-to-end pure-logic scenario tests covering the full PLANUX-20260728 feature set:
/// loop cadence, poll coalescing, envelope repair, execution log, preflight,
/// PlanStore consistency, and recovery inbox.
///
/// Every test uses real production classes — no mocking frameworks, no WPF, no network.
/// </summary>
[TestFixture]
internal sealed class PlanExecutionScenarioTests
{
    // ── Group A: Loop cadence and boundary diagnostics ────────────────────────

    [Test]
    public void LoopClock_SystemClock_ReturnsCurrentTime()
    {
        var before = DateTimeOffset.UtcNow;
        var result = SystemLoopClock.Instance.UtcNow;
        var after  = DateTimeOffset.UtcNow;

        Assert.That(result, Is.GreaterThanOrEqualTo(before),
            "SystemLoopClock.UtcNow must be at least as recent as the time sampled just before the call.");
        Assert.That(result, Is.LessThanOrEqualTo(after),
            "SystemLoopClock.UtcNow must not exceed the time sampled just after the call.");
    }

    [Test]
    public void LoopBoundaryDiagnostics_FieldsStoredOnConstruction()
    {
        var roundCompleted   = new DateTimeOffset(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);
        var waitStarted      = roundCompleted.AddMilliseconds(5);
        var iterationStarted = waitStarted.AddSeconds(6);
        var configured       = TimeSpan.FromMinutes(0.1);
        var actual           = TimeSpan.FromSeconds(6.012);

        var diag = new LoopBoundaryDiagnostics(
            Iteration:          3,
            RoundCompletedAt:   roundCompleted,
            WaitStartedAt:      waitStarted,
            IterationStartedAt: iterationStarted,
            DelaySource:        "config",
            ConfiguredDelay:    configured,
            ActualDelay:        actual,
            QueueDrainOccurred: true);

        Assert.Multiple(() =>
        {
            Assert.That(diag.Iteration,          Is.EqualTo(3));
            Assert.That(diag.RoundCompletedAt,   Is.EqualTo(roundCompleted));
            Assert.That(diag.WaitStartedAt,      Is.EqualTo(waitStarted));
            Assert.That(diag.IterationStartedAt, Is.EqualTo(iterationStarted));
            Assert.That(diag.DelaySource,        Is.EqualTo("config"));
            Assert.That(diag.ConfiguredDelay,    Is.EqualTo(configured));
            Assert.That(diag.ActualDelay,        Is.EqualTo(actual));
            Assert.That(diag.QueueDrainOccurred, Is.True);
        });
    }

    [Test]
    public void LoopBoundaryDiagnostics_BuildTraceMessage_ContainsExpectedComponents()
    {
        var diag = new LoopBoundaryDiagnostics(
            Iteration:          7,
            RoundCompletedAt:   DateTimeOffset.UtcNow,
            WaitStartedAt:      DateTimeOffset.UtcNow,
            IterationStartedAt: DateTimeOffset.UtcNow,
            DelaySource:        "queue-drain",
            ConfiguredDelay:    TimeSpan.FromSeconds(6),
            ActualDelay:        TimeSpan.FromSeconds(6.1),
            QueueDrainOccurred: true);

        var msg = diag.BuildTraceMessage();

        Assert.Multiple(() =>
        {
            Assert.That(msg, Does.Contain("iter=7"),       "Trace message must contain the iteration number.");
            Assert.That(msg, Does.Contain("configured="),  "Trace message must contain the configured delay.");
            Assert.That(msg, Does.Contain("actual="),      "Trace message must contain the actual delay.");
            Assert.That(msg, Does.Contain("queue-drain"),  "Trace message must contain the DelaySource.");
            Assert.That(msg, Does.Contain("queueDrain="),  "Trace message must include the queue-drain flag.");
        });
    }

    [Test]
    public void LoopMdParser_ParseFromContent_IntervalPointOne_ParsesCorrectly()
    {
        // ParseFromContent requires a fenced frontmatter block and configured: true
        const string content =
            "---\ninterval: 0.1\ntimeout: 30\nconfigured: true\n---\n";

        var config = LoopMdParser.ParseFromContent(content);

        Assert.That(config, Is.Not.Null, "ParseFromContent must return a non-null LoopMdConfig.");
        Assert.That(config!.IntervalMinutes, Is.EqualTo(0.1).Within(0.0001),
            "interval: 0.1 must parse to IntervalMinutes == 0.1.");

        var asTimeSpan = TimeSpan.FromMinutes(config.IntervalMinutes);
        Assert.That(asTimeSpan.TotalSeconds, Is.EqualTo(6.0).Within(0.001),
            "0.1 minutes must convert to exactly 6 seconds.");
    }

    // ── Group B: Agent poll coalescing ────────────────────────────────────────

    [Test]
    public void PollCoalescing_TryExtractAgentId_WithSinceTurnParam_ExtractsId()
    {
        // Production-realistic format: since_turn + wait:false alongside agent_id
        const string json   = @"{""agent_id"":""squad-abc-123"",""since_turn"":0,""wait"":false,""timeout"":30}";
        var          result = ReadAgentSatelliteCoalescer.TryExtractAgentId(json);
        Assert.That(result, Is.EqualTo("squad-abc-123"),
            "agent_id must be extracted even when since_turn and wait are present.");
    }

    [Test]
    public void PollCoalescing_TryExtractAgentId_AgentIdNotFirstField_ExtractsId()
    {
        // agent_id is the last field — extraction must not depend on field order
        const string json   = @"{""timeout"":60,""wait"":true,""agent_id"":""my-agent-007""}";
        var          result = ReadAgentSatelliteCoalescer.TryExtractAgentId(json);
        Assert.That(result, Is.EqualTo("my-agent-007"),
            "agent_id must be extractable regardless of its position in the JSON object.");
    }

    [Test]
    public void PollCoalescing_TryExtractAgentId_EmptyStringAgentId_ReturnsEmptyString()
    {
        // An empty agent_id string is technically valid JSON; TryExtractAgentId returns
        // the string value as-is (empty) rather than null.
        const string json   = @"{""agent_id"":""""}";
        var          result = ReadAgentSatelliteCoalescer.TryExtractAgentId(json);
        Assert.That(result, Is.EqualTo(string.Empty),
            "An empty-string agent_id must be returned as empty string, not null.");
    }

    // ── Group C: Step-result envelope repair ──────────────────────────────────

    [Test]
    public void EnvelopeRepair_BuildRepairPrompt_WithPlanFormatIds_ContainsAllFourFields()
    {
        const string groupId  = "PLAN-001";
        const string taskId   = "PLAN-001-003";
        const string revision = "rev123";
        const string reason   = "no result found";

        var prompt = DecomposeEnvelopeRepairPrompt.Build(groupId, taskId, revision, reason);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain(groupId),  "Repair prompt must include the groupId.");
            Assert.That(prompt, Does.Contain(taskId),   "Repair prompt must include the taskId.");
            Assert.That(prompt, Does.Contain(revision), "Repair prompt must include the revision.");
            Assert.That(prompt, Does.Contain(reason),   "Repair prompt must include the failure reason.");
        });
    }

    [Test]
    public void EnvelopeRepair_TryParse_ValidComplete_ExtractsCommitAndRevision()
    {
        var text = """
            DECOMPOSE_STEP_RESULT_JSON:
            {
              "groupId": "PLAN-001",
              "taskId": "PLAN-001-003",
              "revision": "rev123",
              "status": "complete",
              "commit": "abc1234",
              "summary": "Implemented the feature.",
              "remainingWork": [],
              "verification": { "status": "passed", "command": "dotnet test", "summary": "all pass" }
            }
            """;

        var ok = DecomposeStepResultParser.TryParse(text, out var result, out var error);

        Assert.That(ok, Is.True, error);
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Commit,   Is.EqualTo("abc1234"),  "Parsed Commit must match the envelope.");
            Assert.That(result!.Revision, Is.EqualTo("rev123"),   "Parsed Revision must match the envelope.");
            Assert.That(result!.GroupId,  Is.EqualTo("PLAN-001"), "Parsed GroupId must match the envelope.");
        });
    }

    [Test]
    public void EnvelopeRepair_TryParse_MissingTaskId_ReturnsFalse()
    {
        var text = """
            DECOMPOSE_STEP_RESULT_JSON:
            {
              "groupId": "PLAN-001",
              "revision": "rev1",
              "status": "complete",
              "commit": "abc1234",
              "summary": "Implemented the feature.",
              "remainingWork": [],
              "verification": { "status": "passed", "command": "dotnet test", "summary": "all pass" }
            }
            """;

        var ok = DecomposeStepResultParser.TryParse(text, out _, out var error);

        Assert.That(ok, Is.False, "A missing taskId must cause TryParse to fail.");
        Assert.That(error, Is.Not.Null);
    }

    // ── Group D: Execution log ────────────────────────────────────────────────

    private string _tempDir = null!;

    [SetUp]
    public void SetUp() =>
        _tempDir = Path.Combine(Path.GetTempPath(), "PlanExecScenario_" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* best-effort cleanup */ }
    }

    [Test]
    public void PlanExecutionLog_LogPath_EndsWithPlanExecutionNdjson()
    {
        var log = new PlanExecutionLog(_tempDir);

        Assert.That(log.LogPath, Does.EndWith("plan-execution.ndjson"),
            "LogPath must point to plan-execution.ndjson inside the .squad/logs subdirectory.");
        Assert.That(log.LogPath, Does.Contain(Path.Combine(".squad", "logs")),
            "LogPath must be nested under .squad/logs.");
    }

    // ── Group E: Plan preflight ───────────────────────────────────────────────

    [Test]
    public void PlanPreflightBlockedException_ChangedPaths_TypeIsIReadOnlyListOfString()
    {
        var paths = new List<string> { "src/Foo.cs", "src/Bar.cs" };
        var ex    = new PlanPreflightBlockedException("Uncommitted changes", paths, "feature/test");

        // ChangedPaths must be exposed as IReadOnlyList<string> (not just IEnumerable)
        Assert.That(ex.ChangedPaths, Is.InstanceOf<IReadOnlyList<string>>(),
            "ChangedPaths must be an IReadOnlyList<string> so callers can index and count without casting.");
        Assert.That(ex.ChangedPaths.Count, Is.EqualTo(2));
    }

    // ── Group F: PlanStore consistency ────────────────────────────────────────

    // Fixtures shared by tests 13-16

    private static DecomposedTaskGroup MakeScenarioGroup(int taskCount = 3)
    {
        var tasks = Enumerable.Range(1, taskCount)
            .Select(i => new DecomposedSubTask(
                Id:          $"SCN-001-00{i}",
                Description: $"Scenario task {i}",
                DependsOn:   i == 1 ? [] : [$"SCN-001-00{i - 1}"],
                Priority:    "mid",
                Title:       $"Task {i}"))
            .ToList();

        return new DecomposedTaskGroup(
            GroupId:    "SCN-001",
            GroupTitle: "Scenario Test Plan",
            Branch:     "feature/scenario",
            Summary:    "E2E scenario tests",
            Tasks:      tasks);
    }

    private static TaskItem MakeScenarioItem(string taskId, bool isChecked = false) =>
        new(Text:             taskId,
            Owner:            null,
            IsUserOwned:      false,
            IsChecked:        isChecked,
            Emoji:            "🟡",
            RawLine:          $"- [{(isChecked ? "x" : " ")}] **[{taskId}]** description",
            DecomposeGroupId: "SCN-001",
            TaskId:           taskId,
            IsFailed:         false,
            IsPartial:        false,
            IsSuperseded:     false);

    [Test]
    public void PlanStore_ThreeTaskPlan_AllCompleted_HasCompletedCountOfThree()
    {
        var group = MakeScenarioGroup(3);
        var items = new List<TaskItem>
        {
            MakeScenarioItem("SCN-001-001"),
            MakeScenarioItem("SCN-001-002"),
            MakeScenarioItem("SCN-001-003"),
        };

        // Start execution
        var plan = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items, "SCN-001-001");

        // Accept task 1
        items[0] = MakeScenarioItem("SCN-001-001", isChecked: true);
        plan = PlanStoreUpdater.ApplyStepAccepted(plan, items, "SCN-001-002");

        // Accept task 2
        items[1] = MakeScenarioItem("SCN-001-002", isChecked: true);
        plan = PlanStoreUpdater.ApplyStepAccepted(plan, items, "SCN-001-003");

        // Accept task 3
        items[2] = MakeScenarioItem("SCN-001-003", isChecked: true);
        plan = PlanStoreUpdater.ApplyStepAccepted(plan, items, nextExecutingTaskId: null);

        // Atomic completion
        plan = PlanStoreUpdater.ApplyCompleted(plan);

        Assert.That(plan.Progress.CompletedCount, Is.EqualTo(3),
            "After all three tasks are accepted and the plan is completed, CompletedCount must be 3.");
    }

    [Test]
    public void PlanStore_ThreeTaskPlan_AllCompleted_LifecycleStatusIsCompleted()
    {
        var group = MakeScenarioGroup(3);
        var items = new List<TaskItem>
        {
            MakeScenarioItem("SCN-001-001"),
            MakeScenarioItem("SCN-001-002"),
            MakeScenarioItem("SCN-001-003"),
        };

        var plan = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items, "SCN-001-001");

        items[0] = MakeScenarioItem("SCN-001-001", isChecked: true);
        plan = PlanStoreUpdater.ApplyStepAccepted(plan, items, "SCN-001-002");

        items[1] = MakeScenarioItem("SCN-001-002", isChecked: true);
        plan = PlanStoreUpdater.ApplyStepAccepted(plan, items, "SCN-001-003");

        items[2] = MakeScenarioItem("SCN-001-003", isChecked: true);
        plan = PlanStoreUpdater.ApplyStepAccepted(plan, items, nextExecutingTaskId: null);

        plan = PlanStoreUpdater.ApplyCompleted(plan);

        Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed),
            "ApplyCompleted must transition lifecycle to Completed.");
    }

    [Test]
    public void PlanStore_ExecutingPlan_IsNotTerminal()
    {
        Assert.That(PlanLifecycleStatus.IsTerminal(PlanLifecycleStatus.Executing), Is.False,
            "An Executing plan is not terminal — it can be interrupted, stopped, or completed.");
    }

    [Test]
    public void PlanStore_InterruptedThenStopped_IsBothStoppedAndTerminal()
    {
        var group = MakeScenarioGroup(2);
        var items = new List<TaskItem>
        {
            MakeScenarioItem("SCN-001-001"),
            MakeScenarioItem("SCN-001-002"),
        };

        var plan        = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items, "SCN-001-001");
        var interrupted = PlanStoreUpdater.ApplyInterrupted(plan, "process restart", loopIteration: 1);
        var stopped     = PlanStoreUpdater.ApplyStopped(interrupted);

        Assert.Multiple(() =>
        {
            Assert.That(stopped.LifecycleStatus,                  Is.EqualTo(PlanLifecycleStatus.Stopped),
                "ApplyStopped must set LifecycleStatus to Stopped.");
            Assert.That(PlanLifecycleStatus.IsTerminal(stopped.LifecycleStatus), Is.True,
                "Stopped is a terminal status — no further execution is possible.");
            Assert.That(stopped.Timestamps.StoppedAt, Is.Not.Null,
                "ApplyStopped must record a StoppedAt timestamp.");
            Assert.That(stopped.Timestamps.CompletedAt, Is.Null,
                "Stopped must not set CompletedAt — only Completed does.");
        });
    }

    // ── Group G: Recovery inbox ───────────────────────────────────────────────

    private static PendingDecomposePlan BuildRecoveryPlan(
        string groupId    = "PLAN-20260728",
        string groupTitle = "My Feature Plan",
        string branch     = "feature/recovery-test") =>
        new(
            "rev-abc123",
            new DecomposedTaskGroup(
                groupId,
                groupTitle,
                branch,
                "Implement the feature.",
                [new DecomposedSubTask("PLAN-20260728-001", "First task", [], "high")]));

    [Test]
    public void RecoveryInbox_BuildRecoveryMessage_HasCanonicalRecoveryActions()
    {
        var message = DecomposePlanInbox.BuildRecoveryMessage(
            BuildRecoveryPlan(),
            "PLAN-20260728-003",
            "The AI exceeded the context window.",
            DateTimeOffset.Parse("2026-07-28T10:00:00Z"));

        Assert.That(message.Actions.Select(action => action.Label), Is.EqualTo(new[]
        {
            "Assess & Continue",
            "✎ Revise Remaining Plan…",
        }));
    }

    [Test]
    public void RecoveryInbox_BuildRecoveryMessage_AllActionsShareRouteMode()
    {
        var message = DecomposePlanInbox.BuildRecoveryMessage(
            BuildRecoveryPlan(),
            "PLAN-20260728-003",
            "The AI exceeded the context window.",
            DateTimeOffset.Parse("2026-07-28T10:00:00Z"));

        var expectedRouteMode = DecomposePlanInbox.RecoveryRouteMode;

        Assert.That(
            message.Actions.All(a => a.RouteMode == expectedRouteMode), Is.True,
            "Every action in the recovery message must use DecomposePlanInbox.RecoveryRouteMode so they route correctly.");
    }
}
