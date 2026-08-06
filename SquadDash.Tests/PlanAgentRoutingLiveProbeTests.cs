using System.Text.Json;

namespace SquadDash.Tests;

/// <summary>
/// Live integration probe for the explicit generic-primary execution path.
/// Uses production classes without mocking frameworks or live AI/CLI invocations.
/// </summary>
[TestFixture]
internal sealed class PlanAgentRoutingLiveProbeTests
{
    private const string PlanId = "ROUTEPROBE-20260728";
    private const string TaskId = "ROUTEPROBE-20260728-001";
    private const string Revision = "600e50cd82fe3eb9";
    private const string GenericToolCallId = "tool-generic-primary-1";

    private TestWorkspace _workspace = null!;
    private string _workspacePath = null!;

    [SetUp]
    public void SetUp()
    {
        _workspace = new TestWorkspace();
        _workspacePath = _workspace.GetPath("repo");
        Directory.CreateDirectory(_workspacePath);
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    // ------------------------------------------------------------------
    // Test 1: accepted full lifecycle
    // ------------------------------------------------------------------

    [Test]
    public void GenericAttempt_FullLifecycleAllFieldsSet_ValidateGenericReturnsNull()
    {
        var attempt = PlanExecutionAttemptState.CreateGeneric(
            PlanId, TaskId, Revision, _workspacePath);

        Assert.That(attempt.AllowsGenericPrimary, Is.True);
        Assert.That(attempt.AttemptId, Is.Not.Empty);

        // Record generic primary launch
        var launch = new BackgroundAgentLaunchInfo(
            ToolCallId: GenericToolCallId,
            TaskName: null,
            Mode: "background",
            DisplayName: "Generic Agent",
            AccentKey: null,
            RoleText: null,
            Description: null,
            AgentType: "general-purpose",
            Prompt: null,
            AssignedTaskId: null,
            AssignedPlanRevision: null,
            AssignedAgentHandle: null,
            IsVerifiedRosterAssignment: false,
            StartedAt: DateTimeOffset.UtcNow);
        attempt = PlanExecutionEvidenceRecorder.RecordLaunch(
            attempt, launch, launchedByCoordinator: true, ownerPrimaryToolCallId: null);

        Assert.That(attempt.GenericPrimaryToolCallId, Is.EqualTo(GenericToolCallId));

        // Record successful completion
        attempt = attempt.RecordPrimaryCompletion(
            GenericToolCallId,
            DateTimeOffset.UtcNow,
            succeeded: true);

        // Persist and reload to exercise WorkspaceConversationStore round-trip
        attempt = PersistAndReload(attempt);

        Assert.That(attempt.GenericSucceeded, Is.True);
        Assert.That(attempt.GenericCompletedAt, Is.Not.Null);

        // Build a result and verify DecomposeStepResultParser parses it
        var resultText = BuildGenericResult(attempt.AttemptId);
        Assert.That(
            DecomposeStepResultParser.TryParse(resultText, out var result, out var parseError),
            Is.True, parseError);

        // ValidateGeneric must return null for a fully-satisfied attempt
        var validationError = PlanAgentAssignmentValidator.ValidateGeneric(
            TaskId,
            Revision,
            attempt,
            result!.ExecutionAttemptId,
            result.AgentExecutions);

        Assert.That(validationError, Is.Null,
            $"Expected ValidateGeneric to accept the complete attempt, but got: {validationError}");
    }

    // ------------------------------------------------------------------
    // Test 2: fail-closed — second generic primary tool call ID
    // ------------------------------------------------------------------

    [Test]
    public void GenericAttempt_SecondPrimaryToolCallId_PreservesWorkAndReportsAdvisory()
    {
        var attempt = PlanExecutionAttemptState.CreateGeneric(
            PlanId, TaskId, Revision, _workspacePath);

        var firstLaunch = new BackgroundAgentLaunchInfo(
            ToolCallId: GenericToolCallId,
            TaskName: null,
            Mode: "background",
            DisplayName: "Generic Agent",
            AccentKey: null,
            RoleText: null,
            Description: null,
            AgentType: "general-purpose",
            Prompt: null,
            AssignedTaskId: null,
            AssignedPlanRevision: null,
            AssignedAgentHandle: null,
            IsVerifiedRosterAssignment: false,
            StartedAt: DateTimeOffset.UtcNow);
        attempt = PlanExecutionEvidenceRecorder.RecordLaunch(
            attempt, firstLaunch, launchedByCoordinator: true, ownerPrimaryToolCallId: null);

        // A second coordinator-launched generic primary (different tool call ID)
        var secondLaunch = new BackgroundAgentLaunchInfo(
            ToolCallId: "tool-generic-primary-2",
            TaskName: null,
            Mode: "background",
            DisplayName: "Generic Agent",
            AccentKey: null,
            RoleText: null,
            Description: null,
            AgentType: "general-purpose",
            Prompt: null,
            AssignedTaskId: null,
            AssignedPlanRevision: null,
            AssignedAgentHandle: null,
            IsVerifiedRosterAssignment: false,
            StartedAt: DateTimeOffset.UtcNow);
        attempt = PlanExecutionEvidenceRecorder.RecordLaunch(
            attempt, secondLaunch, launchedByCoordinator: true, ownerPrimaryToolCallId: null);

        // Complete both so any completion checks are satisfied
        attempt = attempt.RecordPrimaryCompletion(GenericToolCallId, DateTimeOffset.UtcNow, succeeded: true);

        var validationError = PlanAgentAssignmentValidator.ValidateGeneric(
            TaskId,
            Revision,
            attempt,
            attempt.AttemptId,
            null);

        Assert.Multiple(() =>
        {
            Assert.That(validationError, Is.Null,
                "Additional coordinator helpers must not discard a successfully completed generic primary's work.");
            Assert.That(PlanAgentAssignmentValidator.GetAdvisories(null, attempt),
                Has.Some.Contains("additional helper"));
        });
    }

    // ------------------------------------------------------------------
    // Test 3: fail-closed — child tool call ID on a generic attempt
    // ------------------------------------------------------------------

    [Test]
    public void GenericAttempt_ChildToolCallId_ValidateGenericReturnsError()
    {
        var attempt = PlanExecutionAttemptState.CreateGeneric(
            PlanId, TaskId, Revision, _workspacePath);

        var primaryLaunch = new BackgroundAgentLaunchInfo(
            ToolCallId: GenericToolCallId,
            TaskName: null,
            Mode: "background",
            DisplayName: "Generic Agent",
            AccentKey: null,
            RoleText: null,
            Description: null,
            AgentType: "general-purpose",
            Prompt: null,
            AssignedTaskId: null,
            AssignedPlanRevision: null,
            AssignedAgentHandle: null,
            IsVerifiedRosterAssignment: false,
            StartedAt: DateTimeOffset.UtcNow);
        attempt = PlanExecutionEvidenceRecorder.RecordLaunch(
            attempt, primaryLaunch, launchedByCoordinator: true, ownerPrimaryToolCallId: null);

        // A child agent launched under the primary worker
        var childLaunch = new BackgroundAgentLaunchInfo(
            ToolCallId: "tool-generic-child-1",
            TaskName: null,
            Mode: "background",
            DisplayName: "Generic Child Agent",
            AccentKey: null,
            RoleText: null,
            Description: null,
            AgentType: "general-purpose",
            Prompt: null,
            AssignedTaskId: null,
            AssignedPlanRevision: null,
            AssignedAgentHandle: null,
            IsVerifiedRosterAssignment: false,
            StartedAt: DateTimeOffset.UtcNow);
        attempt = PlanExecutionEvidenceRecorder.RecordLaunch(
            attempt, childLaunch, launchedByCoordinator: false, ownerPrimaryToolCallId: GenericToolCallId);

        attempt = attempt.RecordPrimaryCompletion(GenericToolCallId, DateTimeOffset.UtcNow, succeeded: true);

        var validationError = PlanAgentAssignmentValidator.ValidateGeneric(
            TaskId,
            Revision,
            attempt,
            attempt.AttemptId,
            null);

        Assert.That(validationError, Is.Not.Null,
            "ValidateGeneric must reject an attempt where the generic primary launched child workers.");
        Assert.That(validationError, Does.Contain(TaskId));
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private PlanExecutionAttemptState PersistAndReload(PlanExecutionAttemptState attempt)
    {
        var store = new WorkspaceConversationStore(_workspace.GetPath("state"));
        store.Save(
            _workspacePath,
            WorkspaceConversationState.Empty with
            {
                ActiveLoopExecution = new ActiveLoopExecutionState(
                    Path.Combine(_workspacePath, ".squad", "loop-executing-plan.md"),
                    PlanId,
                    PlanId,
                    Revision,
                    attempt)
            });
        return store.Load(_workspacePath).ActiveLoopExecution?.PlanExecutionAttempt
               ?? throw new AssertionException("Host execution attempt was not restored after round-trip.");
    }

    private static string BuildGenericResult(string attemptId) =>
        DecomposeStepResultParser.Marker + "\n" + JsonSerializer.Serialize(new
        {
            groupId = PlanId,
            taskId = TaskId,
            revision = Revision,
            status = "complete",
            commit = "abc1234",
            summary = "Generic routing probe work completed.",
            remainingWork = Array.Empty<string>(),
            verification = new
            {
                status = "passed",
                command = "probe-verification",
                summary = "All generic-routing contract checks passed."
            },
            executionAttemptId = attemptId,
            agentExecutions = (object[]?)null
        });
}
