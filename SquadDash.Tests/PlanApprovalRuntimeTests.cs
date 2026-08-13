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
        Assert.That(message!.Actions, Has.Count.EqualTo(1));
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
        Assert.Multiple(() =>
        {
            Assert.That(upgraded.Body, Does.Contain("Completed work ready for review"));
            Assert.That(upgraded.Body, Does.Contain("Connected Task A to production."));
            Assert.That(upgraded.Body, Does.Contain("The claim and focused tests matched."));
            Assert.That(upgraded.Body, Does.Contain("abc1234"));
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
