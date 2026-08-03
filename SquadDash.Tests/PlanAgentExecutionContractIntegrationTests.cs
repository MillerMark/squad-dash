using System.Text.Json;

namespace SquadDash.Tests;

/// <summary>
/// Host-controlled end-to-end contract tests for assigned plan execution. These use the
/// production plan, prompt, launch-resolution, evidence, persistence, result-parsing, and
/// validation components without starting Squad CLI or a live model.
/// </summary>
[TestFixture]
internal sealed class PlanAgentExecutionContractIntegrationTests
{
    private const string PlanId = "SYNTHETIC-20260728";
    private const string TaskId = "SYNTHETIC-20260728-001";
    private const string Revision = "synthetic-revision-1";
    private const string ToolCallId = "tool-synthetic-primary";
    private const string Charter = "# Talia Rune\n\nOwn SDK-boundary implementation and verification.";

    private TestWorkspace _workspace = null!;
    private string _workspacePath = null!;
    private string _squadPath = null!;
    private DecomposedAgentAssignment _assignment = null!;
    private IReadOnlyList<RosterAgent> _roster = null!;
    private TeamAgentDescriptor[] _descriptors = null!;

    [SetUp]
    public void SetUp()
    {
        _workspace = new TestWorkspace();
        _workspacePath = _workspace.GetPath("repo");
        _squadPath = Path.Combine(_workspacePath, ".squad");
        Directory.CreateDirectory(_squadPath);
        _workspace.CreateFile("repo/.squad/team.md", """
            | Name | Role | Charter | Status |
            |---|---|---|---|
            | Talia Rune | SDK | agents/talia-rune/charter.md | active |
            """);
        _workspace.CreateFile("repo/.squad/agents/talia-rune/charter.md", Charter);
        _workspace.CreateFile("repo/.squad/agents/talia-rune/history.md", "# History\nPrevious SDK decisions.");
        _workspace.CreateFile("repo/.squad/decisions.md", "# Decisions\nKeep the host contract deterministic.");

        _assignment = new DecomposedAgentAssignment("talia-rune", "SDK implementer", false);
        _roster = PlanStepAgentResolver.ParseTeamMd(
            File.ReadAllText(Path.Combine(_squadPath, "team.md")));
        _descriptors = [new TeamAgentDescriptor("Talia Rune", "talia-rune", "SDK")];
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    [Test]
    public void AssignedTask_FullHostLifecycleAcrossRestart_AcceptsCoordinatorResult()
    {
        var group = BuildGroup();
        Assert.That(PlanAgentAssignmentCatalogValidator.TryValidate(
            group,
            _squadPath,
            out var catalogError,
            requireExplicitRouting: true), Is.True, catalogError);

        var attempt = CreateAttempt();
        attempt = PersistAndReload(attempt);

        var launch = ResolveLaunch(attempt, ToolCallId);
        Assert.That(launch.IsVerifiedRosterAssignment, Is.True);
        attempt = PlanExecutionEvidenceRecorder.RecordLaunch(
            attempt, launch, launchedByCoordinator: true, ownerPrimaryToolCallId: null);
        attempt = RecordRequiredContextReads(attempt);
        attempt = attempt.RecordPrimaryCompletion(
            ToolCallId,
            new DateTimeOffset(2026, 7, 28, 16, 0, 0, TimeSpan.Zero),
            succeeded: true);

        attempt = PersistAndReload(attempt);
        var resultText = BuildResult(attempt.AttemptId);
        Assert.That(DecomposeStepResultParser.TryParse(
            resultText, out var result, out var parseError), Is.True, parseError);

        Assert.Multiple(() =>
        {
            Assert.That(PlanAgentAssignmentValidator.Validate(
                TaskId, Revision, [_assignment], attempt), Is.Null);
            Assert.That(PlanAgentAssignmentValidator.ValidateWrapUp(
                TaskId,
                [_assignment],
                attempt,
                result!.ExecutionAttemptId,
                result.AgentExecutions), Is.Null);
            Assert.That(attempt.Assignments.Single().Succeeded, Is.True);
            Assert.That(attempt.Assignments.Single().ObservedContextPaths,
                Has.Count.EqualTo(attempt.Assignments.Single().RequiredContextPaths.Count));
        });
    }

    [Test]
    public void AssignedTask_DeterministicFailureMatrix_FailsClosed()
    {
        var attempt = CreateAttempt();
        var launch = ResolveLaunch(attempt, ToolCallId);
        var launched = PlanExecutionEvidenceRecorder.RecordLaunch(
            attempt, launch, launchedByCoordinator: true, ownerPrimaryToolCallId: null);
        var completeWithoutReads = launched.RecordPrimaryCompletion(
            ToolCallId, DateTimeOffset.UtcNow, succeeded: true);
        var failed = RecordRequiredContextReads(launched)
            .RecordPrimaryCompletion(ToolCallId, DateTimeOffset.UtcNow, succeeded: false);
        var valid = RecordRequiredContextReads(launched)
            .RecordPrimaryCompletion(ToolCallId, DateTimeOffset.UtcNow, succeeded: true);
        var duplicate = PlanExecutionEvidenceRecorder.RecordLaunch(
            valid,
            launch with { ToolCallId = "tool-duplicate-primary" },
            launchedByCoordinator: true,
            ownerPrimaryToolCallId: null);
        var child = PlanExecutionEvidenceRecorder.RecordLaunch(
            valid,
            launch with {
                ToolCallId = "tool-prohibited-child",
                IsVerifiedRosterAssignment = false
            },
            launchedByCoordinator: false,
            ownerPrimaryToolCallId: ToolCallId);

        var replayAttempt = CreateAttempt();
        var replayedLaunch = ResolveLaunch(
            replayAttempt,
            "tool-replayed-envelope",
            promptAttempt: attempt);
        var replayed = PlanExecutionEvidenceRecorder.RecordLaunch(
            replayAttempt,
            replayedLaunch,
            launchedByCoordinator: true,
            ownerPrimaryToolCallId: null);

        Assert.Multiple(() =>
        {
            Assert.That(PlanAgentAssignmentValidator.Validate(
                TaskId, Revision, [_assignment], launched), Does.Contain("complete successfully"));
            Assert.That(PlanAgentAssignmentValidator.Validate(
                TaskId, Revision, [_assignment], failed), Does.Contain("complete successfully"));
            Assert.That(PlanAgentAssignmentValidator.Validate(
                TaskId, Revision, [_assignment], completeWithoutReads), Does.Contain("host-observed reads"));
            Assert.That(PlanAgentAssignmentValidator.Validate(
                TaskId, Revision, [_assignment], duplicate), Does.Contain("undeclared"));
            Assert.That(PlanAgentAssignmentValidator.Validate(
                TaskId, Revision, [_assignment], child), Does.Contain("forbids"));
            Assert.That(replayedLaunch.IsVerifiedRosterAssignment, Is.False,
                "An envelope from an earlier host attempt must not verify in a retry.");
            Assert.That(PlanAgentAssignmentValidator.Validate(
                TaskId, Revision, [_assignment], replayed), Does.Contain("undeclared"));
            Assert.That(PlanAgentAssignmentValidator.ValidateWrapUp(
                TaskId,
                [_assignment],
                valid,
                "stale-attempt-id",
                [new DecomposeAgentExecution("talia-rune", "talia-rune", [], ToolCallId)]),
                Does.Contain("wrong host executionAttemptId"));
        });
    }

    private DecomposedTaskGroup BuildGroup() => new(
        PlanId,
        "Synthetic verified routing",
        "codex/synthetic-plan-contract",
        "Exercise the assigned-agent execution contract without live AI.",
        [new DecomposedSubTask(
            TaskId,
            "Implement and verify the synthetic SDK change.",
            [],
            "high",
            "Synthetic SDK change",
            AgentAssignments: [_assignment],
            AgentRoutingMode: "assigned")]);

    private PlanExecutionAttemptState CreateAttempt() =>
        PlanExecutionAttemptState.Create(
            PlanId,
            TaskId,
            Revision,
            _workspacePath,
            _squadPath,
            [_assignment],
            _roster);

    private BackgroundAgentLaunchInfo ResolveLaunch(
        PlanExecutionAttemptState activeAttempt,
        string toolCallId,
        PlanExecutionAttemptState? promptAttempt = null)
    {
        promptAttempt ??= activeAttempt;
        var prompt = DecomposePlanningInstructions.BuildPlanStepRoutingContext(
            _squadPath,
            TaskId,
            "Synthetic SDK change",
            "Implement and verify the synthetic SDK change.",
            [_assignment],
            Revision,
            promptAttempt);
        using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            name = "talia-synthetic-sdk",
            mode = "background",
            agent_type = "general-purpose",
            prompt
        }));
        return BackgroundAgentLaunchInfoResolver.TryResolve(
                   toolCallId,
                   args.RootElement,
                   _descriptors,
                   activeAttempt,
                   launchedByCoordinator: true,
                   startedAt: new DateTimeOffset(2026, 7, 28, 15, 55, 0, TimeSpan.Zero))
               ?? throw new AssertionException("The synthetic task launch was not resolved.");
    }

    private PlanExecutionAttemptState RecordRequiredContextReads(
        PlanExecutionAttemptState attempt)
    {
        foreach (var requiredPath in attempt.Assignments.Single().RequiredContextPaths)
        {
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                path = Path.GetRelativePath(_workspacePath, requiredPath)
            }));
            var readPath = PlanContextReadEvidence.TryResolveFullPath(
                new SquadSdkEvent {
                    ToolName = "view",
                    Args = args.RootElement.Clone()
                },
                _workspacePath);
            Assert.That(readPath, Is.Not.Null);
            attempt = attempt.RecordContextRead(ToolCallId, readPath!);
        }
        return attempt;
    }

    private PlanExecutionAttemptState PersistAndReload(PlanExecutionAttemptState attempt)
    {
        var store = new WorkspaceConversationStore(_workspace.GetPath("state"));
        store.Save(
            _workspacePath,
            WorkspaceConversationState.Empty with {
                ActiveLoopExecution = new ActiveLoopExecutionState(
                    Path.Combine(_squadPath, "loop-executing-plan.md"),
                    PlanId,
                    PlanId,
                    Revision,
                    attempt)
            });
        return store.Load(_workspacePath).ActiveLoopExecution?.PlanExecutionAttempt
               ?? throw new AssertionException("The host execution attempt was not restored.");
    }

    [Test]
    public void CrlfCharter_AfterTransportNormalization_IsAccepted()
    {
        // Overwrite charter with CRLF line endings and a trailing CRLF (Windows editor convention)
        var crlfCharter = Charter.Replace("\n", "\r\n") + "\r\n";
        _workspace.CreateFile("repo/.squad/agents/talia-rune/charter.md", crlfCharter);

        var attempt = CreateAttempt();
        var prompt = DecomposePlanningInstructions.BuildPlanStepRoutingContext(
            _squadPath,
            TaskId,
            "Synthetic SDK change",
            "Implement and verify the synthetic SDK change.",
            [_assignment],
            Revision,
            attempt);

        // Simulate prompt transport normalization: CRLF→LF and terminal-newline loss
        var normalizedPrompt = prompt.Replace("\r\n", "\n").TrimEnd('\n');

        var launch = ResolveLaunchWithPrompt(attempt, ToolCallId, normalizedPrompt);
        Assert.That(launch.IsVerifiedRosterAssignment, Is.True);

        attempt = PlanExecutionEvidenceRecorder.RecordLaunch(
            attempt, launch, launchedByCoordinator: true, ownerPrimaryToolCallId: null);
        attempt = RecordRequiredContextReads(attempt);
        attempt = attempt.RecordPrimaryCompletion(
            ToolCallId,
            new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero),
            succeeded: true);

        Assert.That(PlanAgentAssignmentValidator.Validate(
            TaskId, Revision, [_assignment], attempt), Is.Null);
    }

    [Test]
    public void ContentModifiedCharter_InPrompt_StillRequiresAuthoritativeCharterRead()
    {
        var crlfCharter = Charter.Replace("\n", "\r\n") + "\r\n";
        _workspace.CreateFile("repo/.squad/agents/talia-rune/charter.md", crlfCharter);

        var attempt = CreateAttempt();
        var prompt = DecomposePlanningInstructions.BuildPlanStepRoutingContext(
            _squadPath,
            TaskId,
            "Synthetic SDK change",
            "Implement and verify the synthetic SDK change.",
            [_assignment],
            Revision,
            attempt);

        // Simulate transport normalization then corrupt one character of the charter content
        var normalizedPrompt = prompt.Replace("\r\n", "\n").TrimEnd('\n');
        var modifiedPrompt = normalizedPrompt.Replace(
            "Own SDK-boundary implementation",
            "Own SDK-boundary implementaxion");

        var launch = ResolveLaunchWithPrompt(attempt, ToolCallId, modifiedPrompt);
        Assert.That(launch.IsVerifiedRosterAssignment, Is.True,
            "The host capability authenticates the launch; charter receipt is completion evidence.");

        attempt = PlanExecutionEvidenceRecorder.RecordLaunch(
            attempt, launch, launchedByCoordinator: true, ownerPrimaryToolCallId: null);
        foreach (var requiredPath in attempt.Assignments.Single().RequiredContextPaths
                     .Where(path => !path.EndsWith("charter.md", StringComparison.OrdinalIgnoreCase)))
        {
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                path = Path.GetRelativePath(_workspacePath, requiredPath)
            }));
            var readPath = PlanContextReadEvidence.TryResolveFullPath(
                new SquadSdkEvent { ToolName = "view", Args = args.RootElement.Clone() },
                _workspacePath);
            attempt = attempt.RecordContextRead(ToolCallId, readPath!);
        }
        attempt = attempt.RecordPrimaryCompletion(
            ToolCallId,
            new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero),
            succeeded: true);

        Assert.That(PlanAgentAssignmentValidator.Validate(
            TaskId, Revision, [_assignment], attempt),
            Does.Contain("host-observed reads"));
    }

    [Test]
    public void ContextReadRequired_MissingContextRead_ValidationFails()
    {
        var attempt = CreateAttempt();
        var launch = ResolveLaunch(attempt, ToolCallId);
        Assert.That(launch.IsVerifiedRosterAssignment, Is.True);

        attempt = PlanExecutionEvidenceRecorder.RecordLaunch(
            attempt, launch, launchedByCoordinator: true, ownerPrimaryToolCallId: null);
        // Deliberately omit RecordRequiredContextReads — leave required context paths unobserved
        attempt = attempt.RecordPrimaryCompletion(
            ToolCallId,
            new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero),
            succeeded: true);

        Assert.That(PlanAgentAssignmentValidator.Validate(
            TaskId, Revision, [_assignment], attempt),
            Does.Contain("host-observed reads"));
    }

    private static string BuildResult(string attemptId) =>
        DecomposeStepResultParser.Marker + "\n" + JsonSerializer.Serialize(new
        {
            groupId = PlanId,
            taskId = TaskId,
            revision = Revision,
            status = "complete",
            commit = "abc1234",
            summary = "Synthetic assigned work completed.",
            remainingWork = Array.Empty<string>(),
            verification = new {
                status = "passed",
                command = "synthetic-host-verification",
                summary = "All deterministic contract checks passed."
            },
            executionAttemptId = attemptId,
            agentExecutions = new[] {
                new {
                    requestedAgent = "talia-rune",
                    actualPrimaryAgent = "talia-rune"
                }
            }
        });

    private BackgroundAgentLaunchInfo ResolveLaunchWithPrompt(
        PlanExecutionAttemptState activeAttempt,
        string toolCallId,
        string prompt)
    {
        using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            name = "talia-synthetic-sdk",
            mode = "background",
            agent_type = "general-purpose",
            prompt
        }));
        return BackgroundAgentLaunchInfoResolver.TryResolve(
                   toolCallId,
                   args.RootElement,
                   _descriptors,
                   activeAttempt,
                   launchedByCoordinator: true,
                   startedAt: new DateTimeOffset(2026, 7, 28, 15, 55, 0, TimeSpan.Zero))
               ?? throw new AssertionException("The synthetic task launch was not resolved.");
    }
}
