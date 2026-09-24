using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
public sealed class PlanApprovalRuntimeTests
{
    private string _tempDir = null!;
    private InboxStore _inbox = null!;
    private DurableApprovalRequestManager _requests = null!;
    private ApprovalActionCoordinator _actions = null!;
    private PlanApprovalRuntime _runtime = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"approval-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _inbox = new InboxStore(_tempDir);
        _requests = new DurableApprovalRequestManager(_inbox);
        _actions = new ApprovalActionCoordinator();
        _runtime = new PlanApprovalRuntime(_requests, _actions, BuildSnapshotAsync);
    }

    [TearDown]
    public void TearDown()
    {
        _requests.ClearLocks();
        _actions.ClearAll();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [TestCase(false, false, false, null, "ProbeWorkspace")]
    [TestCase(false, true, false, null, "Wait")]
    [TestCase(false, false, true, "OTHER-PLAN", "Wait")]
    [TestCase(false, false, true, "RUNTIME-PLAN", "AlreadyRunning")]
    [TestCase(true, false, false, null, "Cancel")]
    public void ApprovalResumeRetryPolicy_WaitsForSafeExclusiveExecution(
        bool isClosing,
        bool isPromptRunning,
        bool isLoopRunning,
        string? activePlanId,
        string expected)
    {
        var plan = MakePlan(PlanTaskStatus.Pending);

        var decision = ApprovalResumeRetryPolicy.Resolve(
            plan,
            plan.PlanId,
            isClosing,
            isPromptRunning,
            isLoopRunning,
            activePlanId);

        Assert.That(decision.ToString(), Is.EqualTo(expected));
    }

    [Test]
    public void ApprovalResumeRetryPolicy_CancelsWhenDurablePlanStopsExecuting()
    {
        var plan = MakePlan(PlanTaskStatus.Pending) with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
        };

        Assert.That(
            ApprovalResumeRetryPolicy.Resolve(
                plan, plan.PlanId, false, false, false, null),
            Is.EqualTo(ApprovalResumeRetryDecision.Cancel));
    }

    [Test]
    public void ApprovalResumeRetryPolicy_WaitsWhileApprovedPlanHasActivePrompt()
    {
        var plan = MakePlan(PlanTaskStatus.Pending) with
        {
            LifecycleStatus = PlanLifecycleStatus.Approved,
        };

        Assert.That(
            ApprovalResumeRetryPolicy.Resolve(
                plan, plan.PlanId, false, true, false, null),
            Is.EqualTo(ApprovalResumeRetryDecision.Wait));
        Assert.That(
            ApprovalResumeRetryPolicy.Resolve(
                plan, plan.PlanId, false, false, false, null),
            Is.EqualTo(ApprovalResumeRetryDecision.ProbeWorkspace));
    }

    [Test]
    public async Task Advance_ReadyGateWithIndependentWork_OpensWindowWithoutStopping()
    {
        var plan = MakePlan(taskBStatus: PlanTaskStatus.Pending);

        var result = await _runtime.AdvanceAsync(plan);

        Assert.Multiple(() =>
        {
            Assert.That(result.NewlyReadyGates.Select(g => g.GateId), Is.EqualTo(new[] { "GATE-AC" }));
            Assert.That(result.MustStop, Is.False);
            Assert.That(result.NextUngatedTaskId, Is.EqualTo("B"));
            Assert.That(result.UpdatedPlan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(result.ClickToken, Is.Not.Null);
        });
        var message = _inbox.GetById(DurableApprovalRequestManager.BuildMessageId(plan.PlanId));
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Actions, Is.Empty);
    }

    [Test]
    public async Task Advance_WhenIndependentWorkFinishes_StopsAtExistingApproval()
    {
        var first = await _runtime.AdvanceAsync(MakePlan(taskBStatus: PlanTaskStatus.Pending));
        var afterIndependentWork = first.UpdatedPlan with
        {
            Tasks = first.UpdatedPlan.Tasks.Select(task => task.TaskId == "B"
                ? task with { Status = PlanTaskStatus.Complete }
                : task).ToArray(),
            Progress = new PlanProgress(2, 3),
        };

        var result = await _runtime.AdvanceAsync(afterIndependentWork);

        Assert.Multiple(() =>
        {
            Assert.That(result.NewlyReadyGates, Is.Empty);
            Assert.That(result.MustStop, Is.True);
            Assert.That(result.NextUngatedTaskId, Is.Null);
            Assert.That(result.UpdatedPlan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval));
            Assert.That(result.ReviewSnapshot, Is.Not.Null);
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    public async Task Advance_CombinedSnapshotPreservesInstructionsFromEitherGate(int instructionGateIndex)
    {
        var original = MakePlan(taskBStatus: PlanTaskStatus.Complete);
        var gates = new[]
        {
            original.ApprovalGates[0],
            new PlanApprovalGate("GATE-BC", "Review B before C", ["B"], ["C"], PlanGateStatus.Pending),
        };
        gates[instructionGateIndex] = gates[instructionGateIndex] with
        {
            ProofRequirements =
            [
                new PlanTaskProofRequirement(
                    "visual", "human-observation", "Confirm the highlight.", "Is the row highlighted?",
                    HumanVerificationAccessMode.DirectlyReachable,
                    new HumanVerificationRecipe(
                        "Launch the sample.", "Select the row.", "Confirm the highlight.", "Close the sample.")),
            ],
        };
        var builder = new ApprovalReviewSnapshotBuilder((_, _) => Task.FromResult(""), _inbox);
        var runtime = new PlanApprovalRuntime(_requests, _actions,
            (plan, gate, cancellationToken) => builder.BuildAsync(plan, gate, cancellationToken: cancellationToken));

        var result = await runtime.AdvanceAsync(original with { ApprovalGates = gates });
        var message = _inbox.GetById(DurableApprovalRequestManager.BuildMessageId(original.PlanId))!;
        var persisted = System.Text.Json.JsonSerializer.Deserialize<ApprovalReviewSnapshot>(
            message.Attachments.Single(attachment => attachment.Type == "approval-snapshot").Content!);

        Assert.Multiple(() =>
        {
            Assert.That(result.NewlyReadyGates, Has.Count.EqualTo(2));
            Assert.That(result.ReviewSnapshot!.HasOperatorInstructions, Is.True);
            Assert.That(persisted!.HasOperatorInstructions, Is.True);
        });
    }

    [Test]
    public async Task Advance_RecoveredValidationBoundary_OpensApprovalWithoutRerunningCompletedWork()
    {
        var interrupted = MakePlan(taskBStatus: PlanTaskStatus.Complete) with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            InterruptionData = new PlanInterruptionData(
                "Plan execution stopped before the current task was accepted.",
                "pending-recovery",
                0),
        };

        var recovered = PlanStoreUpdater.ApplyApprovalBoundaryRecovery(interrupted);
        var result = await _runtime.AdvanceAsync(recovered);

        Assert.Multiple(() =>
        {
            Assert.That(result.MustStop, Is.True);
            Assert.That(result.UpdatedPlan.LifecycleStatus,
                Is.EqualTo(PlanLifecycleStatus.AwaitingApproval));
            Assert.That(result.UpdatedPlan.InterruptionData, Is.Null);
            Assert.That(result.UpdatedPlan.Tasks.Count(task =>
                task.Status == PlanTaskStatus.Complete), Is.EqualTo(2));
            Assert.That(result.UpdatedPlan.ApprovalGates.Single().Status,
                Is.EqualTo(PlanGateStatus.AwaitingApproval));
            Assert.That(result.ClickToken, Is.Not.Null);
        });
    }

    [Test]
    public async Task Approve_VersionedAggregateAction_PersistsThenDisablesInboxAction()
    {
        var stopped = await _runtime.AdvanceAsync(MakePlan(taskBStatus: PlanTaskStatus.Complete));
        Plan? persisted = null;

        var resolution = await _runtime.ApproveAsync(
            stopped.ClickToken!,
            stopped.UpdatedPlan,
            "Reviewed",
            plan => { persisted = plan; return true; });

        Assert.Multiple(() =>
        {
            Assert.That(resolution.Result, Is.EqualTo(ApprovalClickResult.Approved));
            Assert.That(resolution.ShouldResume, Is.True);
            Assert.That(persisted, Is.Not.Null);
            Assert.That(persisted!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Approved));
            Assert.That(persisted!.ApprovalGates.Single().Status, Is.EqualTo(PlanGateStatus.Approved));
        });
        var message = _inbox.GetById(DurableApprovalRequestManager.BuildMessageId(stopped.UpdatedPlan.PlanId));
        Assert.That(message!.Actions, Is.Empty);
        Assert.That(message.Read, Is.True);
    }

    [Test]
    public async Task Approve_WhenPlanPersistenceFails_LeavesRequestActionable()
    {
        var stopped = await _runtime.AdvanceAsync(MakePlan(taskBStatus: PlanTaskStatus.Complete));

        var resolution = await _runtime.ApproveAsync(
            stopped.ClickToken!, stopped.UpdatedPlan, null, _ => false);

        Assert.That(resolution.Result, Is.EqualTo(ApprovalClickResult.PersistenceFailed));
        Assert.That(_actions.HasActiveGates(stopped.UpdatedPlan.PlanId), Is.True);
        var state = _requests.GetState(stopped.UpdatedPlan.PlanId);
        Assert.That(state!.ActiveGateIds, Is.EqualTo(new[] { "GATE-AC" }));
    }

    [Test]
    public async Task Approve_PartialChecklistChangedAfterConfirmation_RequiresReconfirmationOnCurrentTarget()
    {
        var confirmedAt = DateTimeOffset.Parse("2026-09-23T12:00:00Z");
        var task = new PlanTask(
            "REVIEW", "Review UI", "Verify the UI", [], "high", PlanTaskStatus.Complete,
            Commit: "abcdef123456");
        var gate = new PlanApprovalGate(
            "GATE-REVIEW", "Confirm the UI", [task.TaskId], [], PlanGateStatus.AwaitingApproval,
            ProofRequirements:
            [
                new PlanTaskProofRequirement(
                    "first", "live-ui-observation", "Observe first", "Does the first result work?"),
                new PlanTaskProofRequirement(
                    "second", "live-ui-observation", "Observe second", "Does the second result work?"),
            ],
            HumanReviewSelections:
            [new PlanHumanReviewSelection("first", true, task.Commit!, confirmedAt)]);
        var plan = new Plan(
            "REVIEW-TARGET-PLAN", "rev-1", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.AwaitingApproval, "Review plan", "feature/review", "Review",
            [task], [gate], new PlanProgress(1, 1), new PlanTimestamps(confirmedAt));
        await _requests.AppendCheckpointAsync(
            plan, gate, await BuildSnapshotAsync(plan, gate, CancellationToken.None));
        var state = _requests.GetState(plan.PlanId)!;
        var token = await _actions.RestoreAsync(
            plan.PlanId, plan.Revision, state.Version, state.ActiveGateIds);

        var corrected = PlanStoreUpdater.ApplyManualReviewActivity(
            plan,
            [task.TaskId],
            "Rebuilt the reviewed package after a relevant fix.",
            confirmedAt.AddMinutes(5));
        await _requests.RefreshFromPlanAsync(corrected);
        var uncertainItems = PlanHumanReviewChecklist.Build(corrected, [corrected.ApprovalGates.Single()]);
        var uncertain = uncertainItems.Single(item => item.ItemId == "first");
        var second = uncertainItems.Single(item => item.ItemId == "second");
        var stalePersistCalls = 0;

        var staleResult = await _runtime.ApproveAsync(
            token, corrected, null, _ => { stalePersistCalls++; return true; });

        Assert.Multiple(() =>
        {
            Assert.That(uncertain.WasPreviouslyChecked, Is.True);
            Assert.That(uncertain.IsChecked, Is.False);
            Assert.That(staleResult.Result, Is.EqualTo(ApprovalClickResult.StaleRejected));
            Assert.That(stalePersistCalls, Is.Zero);
            Assert.That(_inbox.GetById(DurableApprovalRequestManager.BuildMessageId(plan.PlanId))!.Actions,
                Is.Empty);
        });

        var reconfirmed = PlanStoreUpdater.ApplyGateHumanReviewSelection(
            corrected, gate.GateId, uncertain.ItemId, true,
            uncertain.CandidateCommit, uncertain.ReviewTarget, confirmedAt.AddMinutes(6));
        reconfirmed = PlanStoreUpdater.ApplyGateHumanReviewSelection(
            reconfirmed, gate.GateId, second.ItemId, true,
            second.CandidateCommit, second.ReviewTarget, confirmedAt.AddMinutes(7));
        var resolution = await _runtime.ApproveAsync(token, reconfirmed, null, _ => true);

        Assert.Multiple(() =>
        {
            Assert.That(PlanHumanReviewChecklist.Build(reconfirmed, [reconfirmed.ApprovalGates.Single()])
                .All(item => item.IsChecked), Is.True);
            Assert.That(resolution.Result, Is.EqualTo(ApprovalClickResult.Approved));
        });
    }

    [Test]
    public async Task Approve_HumanReviewCompletion_PersistsTaskAndGateAtomically()
    {
        const string commit = "abcdef1234567890";
        var task = new PlanTask(
            "REVIEW", "Review UI", "Verify the UI", [], "high",
            PlanTaskStatus.HumanReviewRequired,
            Handoff: new PlanTaskHandoff(
                commit,
                "Implemented the reviewed UI.",
                ["Feature.cs"],
                new DecomposeStepVerification("passed", "dotnet test", "Tests passed."),
                DateTimeOffset.UtcNow),
            DisplayStepLabel: "4");
        var gate = new PlanApprovalGate(
            "GATE-REVIEW",
            "Confirm the UI.",
            [task.TaskId],
            [],
            PlanGateStatus.AwaitingApproval,
            ProofRequirements:
            [new PlanTaskProofRequirement(
                "visual", "live-ui-observation", "Observe the UI.", "Does the UI work?")],
            HumanReviewSelections:
            [new PlanHumanReviewSelection("visual", true, commit, DateTimeOffset.UtcNow)]);
        var plan = new Plan(
            "REVIEW-PLAN", "rev-1", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.AwaitingApproval, "Review plan", "feature/review", "Review",
            [task], [gate], new PlanProgress(0, 1, task.TaskId),
            new PlanTimestamps(DateTimeOffset.UtcNow));
        await _requests.AppendCheckpointAsync(plan, gate, await BuildSnapshotAsync(plan, gate, CancellationToken.None));
        var state = _requests.GetState(plan.PlanId)!;
        var token = await _actions.RestoreAsync(
            plan.PlanId, plan.Revision, state.Version, state.ActiveGateIds);
        var presentation = PlanCheckpointPrimaryActionPresentationResolver.Resolve(plan, token.GateIds);
        Plan? persisted = null;

        var resolution = await _runtime.ApproveAsync(
            token,
            plan,
            null,
            updated => { persisted = updated; return true; },
            resolvedBy: "Reviewer",
            prepareResolution: current =>
                PlanHumanReviewStepAcceptance.ApplyTasks(
                    current,
                    presentation.ReviewAttentions,
                    currentHead: commit));

        Assert.Multiple(() =>
        {
            Assert.That(presentation.Kind,
                Is.EqualTo(PlanCheckpointPrimaryActionKind.CompleteHumanReviewTasks));
            Assert.That(presentation.Label, Is.EqualTo("Step 4 Is Complete & Continue"));
            Assert.That(resolution.Result, Is.EqualTo(ApprovalClickResult.Approved));
            Assert.That(resolution.ShouldResume, Is.True);
            Assert.That(persisted, Is.Not.Null);
            Assert.That(persisted!.Tasks.Single().Status, Is.EqualTo(PlanTaskStatus.Complete));
            Assert.That(persisted.ApprovalGates.Single().Status, Is.EqualTo(PlanGateStatus.Approved));
            Assert.That(persisted.Progress.CompletedCount, Is.EqualTo(1));
            Assert.That(persisted.Progress.ExecutingTaskId, Is.Null);
            Assert.That(_actions.HasActiveGates(plan.PlanId), Is.False);
            Assert.That(_requests.GetState(plan.PlanId)?.Archived, Is.True);
            Assert.That(_requests.GetState(plan.PlanId)?.ActiveGateIds, Is.Empty);
        });
    }

    [Test]
    public async Task Approve_SharedCheckpoint_PersistsBothReviewedTasksAndGateInOneWrite()
    {
        var (plan, token, presentation) = await PrepareSharedReviewAsync();
        var writes = new List<Plan>();
        var reviewedCommits = plan.Tasks.ToDictionary(task => task.TaskId, task => task.Handoff!.Commit!);

        var resolution = await _runtime.ApproveAsync(
            token, plan, null,
            updated => { writes.Add(updated); return true; },
            resolvedBy: "Reviewer",
            prepareResolution: current => PlanHumanReviewStepAcceptance.ApplyTasks(
                current, presentation.ReviewAttentions,
                currentHead: "bbbbbbbbbb", reviewedTaskCommits: reviewedCommits));

        Assert.That(writes, Has.Count.EqualTo(1), "No intermediate task-only or gate-only state may be persisted.");
        var persisted = writes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(resolution.Result, Is.EqualTo(ApprovalClickResult.Approved));
            Assert.That(resolution.UpdatedPlan, Is.SameAs(persisted));
            Assert.That(persisted.Tasks.Select(task => task.Status), Is.All.EqualTo(PlanTaskStatus.Complete));
            Assert.That(persisted.Tasks.Select(task => task.Commit),
                Is.EqualTo(new[] { "aaaaaaaaaa", "bbbbbbbbbb" }));
            Assert.That(persisted.ApprovalGates.Single().Status, Is.EqualTo(PlanGateStatus.Approved));
            Assert.That(persisted.Progress.CompletedCount, Is.EqualTo(2));
            Assert.That(persisted.Progress.ExecutingTaskId, Is.Null);
            Assert.That(plan.Tasks.Select(task => task.Status), Is.All.EqualTo(PlanTaskStatus.HumanReviewRequired));
            Assert.That(plan.ApprovalGates.Single().Status, Is.EqualTo(PlanGateStatus.AwaitingApproval));
            Assert.That(_actions.HasActiveGates(plan.PlanId), Is.False);
            Assert.That(_requests.GetState(plan.PlanId)?.Archived, Is.True);
            Assert.That(_requests.GetState(plan.PlanId)?.ActiveGateIds, Is.Empty);
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    public async Task Approve_SharedCheckpointMismatchedCommit_DoesNotPersistOrResolveGate(int mismatchedIndex)
    {
        var (plan, token, presentation) = await PrepareSharedReviewAsync();
        var before = System.Text.Json.JsonSerializer.Serialize(plan);
        var requestBefore = System.Text.Json.JsonSerializer.Serialize(_requests.GetState(plan.PlanId));
        var messageBefore = System.Text.Json.JsonSerializer.Serialize(
            _inbox.GetById(DurableApprovalRequestManager.BuildMessageId(plan.PlanId)));
        var writes = new List<Plan>();
        var reviewedCommits = plan.Tasks.ToDictionary(task => task.TaskId, task => task.Handoff!.Commit!);
        reviewedCommits[plan.Tasks[mismatchedIndex].TaskId] = "unreviewed-commit";

        Assert.ThrowsAsync<InvalidOperationException>(async () => await _runtime.ApproveAsync(
            token, plan, null,
            updated => { writes.Add(updated); return true; },
            prepareResolution: current => PlanHumanReviewStepAcceptance.ApplyTasks(
                current, presentation.ReviewAttentions,
                currentHead: "bbbbbbbbbb", reviewedTaskCommits: reviewedCommits)));

        Assert.Multiple(() =>
        {
            Assert.That(writes, Is.Empty);
            Assert.That(System.Text.Json.JsonSerializer.Serialize(plan), Is.EqualTo(before));
            Assert.That(_actions.HasActiveGates(plan.PlanId), Is.True);
            Assert.That(System.Text.Json.JsonSerializer.Serialize(_requests.GetState(plan.PlanId)),
                Is.EqualTo(requestBefore));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(
                _inbox.GetById(DurableApprovalRequestManager.BuildMessageId(plan.PlanId))),
                Is.EqualTo(messageBefore));
        });
    }

    private async Task<(Plan Plan, ApprovalClickToken Token, PlanCheckpointPrimaryActionPresentation Presentation)>
        PrepareSharedReviewAsync()
    {
        var tasks = new[] { "aaaaaaaaaa", "bbbbbbbbbb" }.Select((commit, index) => new PlanTask(
            $"REVIEW-{index}", $"Review step {index}", "Verify the UI", [], "high",
            PlanTaskStatus.HumanReviewRequired,
            Handoff: new PlanTaskHandoff(
                commit, "Implemented UI.", ["Feature.cs"],
                new DecomposeStepVerification("passed", "dotnet test", "Tests passed."),
                DateTimeOffset.UtcNow))).ToArray();
        var gate = new PlanApprovalGate(
            "SHARED-GATE", "Confirm both steps.", tasks.Select(task => task.TaskId).ToArray(), [],
            PlanGateStatus.AwaitingApproval,
            ProofRequirements:
            [new PlanTaskProofRequirement("visual", "live-ui-observation", "Observe both steps.", "Do both work?")],
            HumanReviewSelections:
            [new PlanHumanReviewSelection(
                "visual", true, "aaaaaaaaaa+bbbbbbbbbb", DateTimeOffset.UtcNow)]);
        var plan = new Plan(
            "SHARED-REVIEW-PLAN", "rev-1", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.AwaitingApproval, "Review plan", "feature/review", "Review",
            tasks, [gate], new PlanProgress(0, 2, tasks[0].TaskId), new PlanTimestamps(DateTimeOffset.UtcNow));
        await _requests.AppendCheckpointAsync(plan, gate, await BuildSnapshotAsync(plan, gate, CancellationToken.None));
        var state = _requests.GetState(plan.PlanId)!;
        var token = await _actions.RestoreAsync(plan.PlanId, plan.Revision, state.Version, state.ActiveGateIds);
        var presentation = PlanCheckpointPrimaryActionPresentationResolver.Resolve(plan, token.GateIds);
        Assert.Multiple(() =>
        {
            Assert.That(token.GateIds, Is.EqualTo(new[] { gate.GateId }));
            Assert.That(presentation.Kind, Is.EqualTo(PlanCheckpointPrimaryActionKind.CompleteHumanReviewTasks));
            Assert.That(presentation.ReviewAttentions.Select(attention => attention.Task.TaskId),
                Is.EqualTo(gate.AfterTaskIds));
        });
        return (plan, token, presentation);
    }

    [Test]
    public async Task RequestRework_FromAiCompatibilityResponse_ReopensTaskAndInvalidatesValidationAtomically()
    {
        var validation = new PlanValidationNode(
            "VAL-A",
            "Validate A",
            "Validate A before C.",
            ["A"],
            ["C"],
            ["A is integrated."],
            null,
            "ai",
            null,
            false,
            PlanValidationStatus.Passed,
            CompletedAt: DateTimeOffset.UtcNow,
            ValidatedCommit: "abc1234",
            Summary: "A was integrated.",
            Evidence: ["Integration test passed."]);
        var stopped = await _runtime.AdvanceAsync(MakePlan(taskBStatus: PlanTaskStatus.Complete) with
        {
            Validations = [validation],
        });
        var rawResponse = $$"""
            PLAN_GATE_RESPONSE_JSON:
            {
              "planId": "{{stopped.UpdatedPlan.PlanId}}",
              "gateId": "GATE-AC",
              "revision": "{{stopped.UpdatedPlan.Revision}}",
              "requestVersion": {{stopped.ClickToken!.RequestVersion}},
              "disposition": "request-rework",
              "reworkTasks": [
                { "taskId": "A", "instructions": "Correct the integration." }
              ]
            }
            """;
        Assert.That(PlanGateResponseParser.TryParse(rawResponse, out var response), Is.True);
        Plan? persisted = null;

        var result = await _runtime.RequestReworkAsync(
            stopped.ClickToken!,
            stopped.UpdatedPlan,
            "GATE-AC",
            response!.TaskIds!,
            response.Instructions!,
            plan => { persisted = plan; return true; });

        Assert.Multiple(() =>
        {
            Assert.That(result.Result, Is.EqualTo(ApprovalClickResult.Approved));
            Assert.That(persisted, Is.Not.Null);
            Assert.That(persisted!.Tasks.Single(task => task.TaskId == "A").Status,
                Is.EqualTo(PlanTaskStatus.Pending));
            Assert.That(persisted.Validations!.Single().Status,
                Is.EqualTo(PlanValidationStatus.Stale));
            Assert.That(persisted.ApprovalGates.Single().Status,
                Is.EqualTo(PlanGateStatus.Pending));
            Assert.That(persisted.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
        });
    }

    [Test]
    public async Task RequestAmendment_PreservesAcceptedTaskAndConsumesApprovalVersionAtomically()
    {
        var stopped = await _runtime.AdvanceAsync(MakePlan(taskBStatus: PlanTaskStatus.Complete));
        Plan? persisted = null;

        var result = await _runtime.RequestAmendmentAsync(
            stopped.ClickToken!,
            stopped.UpdatedPlan,
            "GATE-AC",
            ["A"],
            "Add joined-result cleanup",
            "Clean up on every exit path.",
            plan => { persisted = plan; return true; });

        Assert.Multiple(() =>
        {
            Assert.That(result.Result, Is.EqualTo(ApprovalClickResult.Approved));
            Assert.That(persisted, Is.Not.Null);
            Assert.That(persisted!.Tasks.Single(task => task.TaskId == "A").Status,
                Is.EqualTo(PlanTaskStatus.Complete));
            Assert.That(persisted.Tasks.Single(task => task.AmendmentGateId == "GATE-AC").Status,
                Is.EqualTo(PlanTaskStatus.Pending));
            Assert.That(persisted.ApprovalGates.Single().Status, Is.EqualTo(PlanGateStatus.Pending));
            Assert.That(_actions.GetCurrentToken(persisted.PlanId)!.GateIds, Does.Not.Contain("GATE-AC"),
                "The old approval action must no longer include the amended gate while its task runs.");
        });
    }

    [Test]
    public async Task RequestAmendment_EquivalentTokenRefreshSurvivesVersionOnlyRace()
    {
        var stopped = await _runtime.AdvanceAsync(MakePlan(taskBStatus: PlanTaskStatus.Complete));
        var originalToken = stopped.ClickToken!;
        var refreshedToken = await _actions.RegisterAsync(
            stopped.UpdatedPlan.PlanId,
            stopped.UpdatedPlan.Revision,
            originalToken.GateIds);
        Plan? persisted = null;

        var result = await _runtime.RequestAmendmentAsync(
            originalToken,
            stopped.UpdatedPlan,
            "GATE-AC",
            ["A"],
            "Add joined-result cleanup",
            "Clean up on every exit path.",
            plan => { persisted = plan; return true; },
            allowEquivalentTokenRefresh: true);

        Assert.Multiple(() =>
        {
            Assert.That(refreshedToken.RequestVersion, Is.GreaterThan(originalToken.RequestVersion));
            Assert.That(result.Result, Is.EqualTo(ApprovalClickResult.Approved));
            Assert.That(persisted, Is.Not.Null);
        });
    }

    [Test]
    public async Task RequestAmendment_TerminalHumanReviewGatePersistsAndSchedulesFixture()
    {
        var reviewed = new PlanTask(
            "TASK-REVIEW", "Reviewed work", "Work", [], "high",
            PlanTaskStatus.HumanReviewRequired);
        var gate = new PlanApprovalGate(
            "GATE-HUMAN", "Verify reviewed work", [reviewed.TaskId], [],
            PlanGateStatus.AwaitingApproval);
        var plan = new Plan(
            "PLAN-TERMINAL", "rev", PlanSource.Manual, PlanLifecycleStatus.AwaitingApproval,
            "Plan", "feature/plan", "Summary", [reviewed], [gate],
            new PlanProgress(0, 1), new PlanTimestamps(DateTimeOffset.UtcNow));
        await _requests.AppendCheckpointAsync(
            plan, gate, await BuildSnapshotAsync(plan, gate, default));
        var state = _requests.GetState(plan.PlanId)!;
        var token = await _actions.RestoreAsync(
            plan.PlanId, plan.Revision, state.Version, state.ActiveGateIds);
        Plan? persisted = null;

        var result = await _runtime.RequestAmendmentAsync(
            token,
            plan,
            gate.GateId,
            [reviewed.TaskId],
            "Add review simulation",
            "Make the human verification reproducible.",
            updated => { persisted = updated; return true; },
            allowEquivalentTokenRefresh: true);

        var amendment = persisted!.Tasks.Single(task => task.AmendmentGateId == gate.GateId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Result, Is.EqualTo(ApprovalClickResult.Approved));
            Assert.That(persisted.Tasks.Single(task => task.TaskId == reviewed.TaskId).Status,
                Is.EqualTo(PlanTaskStatus.Complete),
                "The amendment moves the existing human-proof boundary after the fixture task.");
            Assert.That(persisted.ApprovalGates.Single().Status, Is.EqualTo(PlanGateStatus.Pending));
            Assert.That(ApprovalGateReadinessEvaluator.SelectNextUngatedTask(persisted),
                Is.EqualTo(amendment.TaskId));
        });
    }

    [Test]
    public async Task Approve_OneGateFromViewer_LeavesOtherGateVersionedAndActive()
    {
        var basePlan = MakePlan(taskBStatus: PlanTaskStatus.Complete);
        var secondGate = new PlanApprovalGate(
            "GATE-BC", "Review B before C", ["B"], ["C"], PlanGateStatus.Pending);
        var stopped = await _runtime.AdvanceAsync(basePlan with
        {
            ApprovalGates = basePlan.ApprovalGates.Append(secondGate).ToArray(),
        });

        var resolution = await _runtime.ApproveAsync(
            stopped.ClickToken!,
            stopped.UpdatedPlan,
            null,
            _ => true,
            gateIdsToResolve: ["GATE-AC"]);

        Assert.Multiple(() =>
        {
            Assert.That(resolution.Result, Is.EqualTo(ApprovalClickResult.Approved));
            Assert.That(resolution.ShouldResume, Is.False);
            Assert.That(resolution.NextClickToken, Is.Not.Null);
            Assert.That(resolution.NextClickToken!.GateIds, Is.EqualTo(new[] { "GATE-BC" }));
            Assert.That(resolution.NextClickToken.RequestVersion,
                Is.GreaterThan(stopped.ClickToken!.RequestVersion));
            Assert.That(resolution.UpdatedPlan!.LifecycleStatus,
                Is.EqualTo(PlanLifecycleStatus.AwaitingApproval));
        });
    }

    [Test]
    public async Task Restore_PreservesDurableVersionAndRejectsOldAction()
    {
        var first = await _runtime.AdvanceAsync(MakePlan(taskBStatus: PlanTaskStatus.Pending));
        var oldToken = first.ClickToken!;
        var secondGate = new PlanApprovalGate(
            "GATE-B", "Review B", ["B"], ["C"], PlanGateStatus.AwaitingApproval);
        var expanded = first.UpdatedPlan with
        {
            ApprovalGates = first.UpdatedPlan.ApprovalGates.Append(secondGate).ToArray(),
        };
        await _requests.AppendCheckpointAsync(expanded, secondGate, await BuildSnapshotAsync(expanded, secondGate, default));

        var freshActions = new ApprovalActionCoordinator();
        var restored = new PlanApprovalRuntime(_requests, freshActions, BuildSnapshotAsync);
        await restored.RestoreAsync([expanded]);

        var current = freshActions.GetCurrentToken(expanded.PlanId);
        Assert.Multiple(() =>
        {
            Assert.That(current, Is.Not.Null);
            Assert.That(current!.RequestVersion, Is.GreaterThan(oldToken.RequestVersion));
            Assert.That(current.GateIds, Is.EqualTo(new[] { "GATE-AC", "GATE-B" }));
        });
        Assert.That(
            await freshActions.TryApproveAsync(oldToken, oldToken.GateIds),
            Is.EqualTo(ApprovalClickResult.StaleRejected));
    }

    [Test]
    public async Task Restore_RebindsRevisedPlanActionAndAllowsOnlyFreshApproval()
    {
        var first = await _runtime.AdvanceAsync(MakePlan(taskBStatus: PlanTaskStatus.Pending));
        var oldToken = first.ClickToken!;
        var revised = first.UpdatedPlan with { Revision = "rev-2" };

        await _runtime.RestoreAsync([revised]);

        var freshToken = _runtime.Actions.GetCurrentToken(revised.PlanId);
        var message = _inbox.GetById(DurableApprovalRequestManager.BuildMessageId(revised.PlanId));
        Assert.Multiple(() =>
        {
            Assert.That(freshToken, Is.Not.Null);
            Assert.That(freshToken!.PlanRevision, Is.EqualTo("rev-2"));
            Assert.That(message!.Actions, Is.Empty,
                "The current token is owned by the runtime until the inline checklist is complete");
        });

        Assert.That(
            (await _runtime.ApproveAsync(oldToken, revised, null, _ => true)).Result,
            Is.EqualTo(ApprovalClickResult.StaleRejected));
        Assert.That(
            (await _runtime.ApproveAsync(freshToken!, revised, null, _ => true)).Result,
            Is.EqualTo(ApprovalClickResult.Approved));
    }

    [Test]
    public async Task Restore_UpgradesLegacyApprovalBodyFromFreshReviewEvidence()
    {
        var first = await _runtime.AdvanceAsync(MakePlan(taskBStatus: PlanTaskStatus.Pending));
        var messageId = DurableApprovalRequestManager.BuildMessageId(first.UpdatedPlan.PlanId);
        var legacy = _inbox.GetById(messageId)!;
        _inbox.Save(legacy with { Body = "Approval needed. Open the plan for details." });

        static Task<ApprovalReviewSnapshot> BuildDetailedSnapshot(
            Plan plan,
            PlanApprovalGate gate,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var commit = new ReviewCommitEntry(
                new CommitLink("abc1234", "abc1234567890", "Complete Task A"),
                true,
                []);
            return Task.FromResult(new ApprovalReviewSnapshot(
                plan.PlanId, plan.Title, plan.Progress.CompletedCount, plan.Progress.TotalCount,
                plan.LifecycleStatus, gate.GateId, gate.Message, gate.AfterTaskIds, gate.BeforeTaskIds,
                [new ReviewTaskEntry("A", "Task A", "Connected Task A to production.", [commit],
                    "The claim and focused tests matched.")],
                [new DownstreamTaskEntry("C", "Task C", "pending")],
                [], [], DateTimeOffset.UtcNow));
        }

        var restored = new PlanApprovalRuntime(
            _requests,
            new ApprovalActionCoordinator(),
            BuildDetailedSnapshot);
        await restored.RestoreAsync([first.UpdatedPlan]);

        var upgraded = _inbox.GetById(messageId)!;
        var evidence = upgraded.Attachments.Single(attachment =>
            attachment.Label == DurableApprovalRequestManager.ProvenanceAttachmentLabel).Content!;
        Assert.Multiple(() =>
        {
            Assert.That(upgraded.Body, Does.Not.Contain("Connected Task A to production.")
                .And.Not.Contain("abc1234"));
            Assert.That(evidence, Does.Contain("Completed work ready for review")
                .And.Contain("Connected Task A to production.")
                .And.Contain("The claim and focused tests matched.")
                .And.Contain("abc1234"));
            Assert.That(upgraded.Body, Does.Not.Contain("Open the plan for details"));
        });
    }

    private static Plan MakePlan(string taskBStatus)
    {
        var tasks = new[]
        {
            new PlanTask("A", "Task A", "Complete A", [], "high", PlanTaskStatus.Complete),
            new PlanTask("B", "Task B", "Independent B", [], "high", taskBStatus),
            new PlanTask("C", "Task C", "Gated C", ["A"], "high", PlanTaskStatus.Pending),
        };
        var completed = tasks.Count(task => task.Status == PlanTaskStatus.Complete);
        return new Plan(
            "RUNTIME-PLAN", "rev-1", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.Executing, "Runtime plan", "feature/runtime", "Runtime integration",
            tasks,
            [new PlanApprovalGate("GATE-AC", "Review A before C", ["A"], ["C"], PlanGateStatus.Pending)],
            new PlanProgress(completed, tasks.Length),
            new PlanTimestamps(DateTimeOffset.UtcNow));
    }

    private static Task<ApprovalReviewSnapshot> BuildSnapshotAsync(
        Plan plan,
        PlanApprovalGate gate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ApprovalReviewSnapshot(
            plan.PlanId, plan.Title, plan.Progress.CompletedCount, plan.Progress.TotalCount,
            plan.LifecycleStatus, gate.GateId, gate.Message, gate.AfterTaskIds, gate.BeforeTaskIds,
            [], [], [], [], DateTimeOffset.UtcNow));
    }
}
