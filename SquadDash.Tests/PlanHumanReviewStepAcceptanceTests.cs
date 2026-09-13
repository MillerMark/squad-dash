using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanHumanReviewStepAcceptanceTests
{
    private const string FirstCommit = "ddc9136669";
    private const string Head = "f8f3dd89b9";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-13T11:00:00Z");

    [Test]
    public void AwaitingApproval_CurrentHeadAndOwnHandoffWithoutInterruption_Accepts()
    {
        var plan = MakePlan(Head);

        var updated = PlanHumanReviewStepAcceptance.ApplyTasks(plan, Attentions(plan), currentHead: Head);

        Assert.Multiple(() =>
        {
            Assert.That(plan.InterruptionData, Is.Null);
            Assert.That(updated.Tasks[0].Status, Is.EqualTo(PlanTaskStatus.Complete));
            Assert.That(updated.Tasks[0].Commit, Is.EqualTo(Head));
            Assert.That(plan.Tasks[0].Status, Is.EqualTo(PlanTaskStatus.HumanReviewRequired));
        });
    }

    [Test]
    public void AwaitingApproval_CurrentHeadDiffersFromOwnHandoff_Rejects()
    {
        var plan = MakePlan(FirstCommit);

        AssertRejectedWithoutMutation(plan, () =>
            PlanHumanReviewStepAcceptance.ApplyTasks(plan, Attentions(plan), currentHead: Head));
    }

    [Test]
    public void MultipleTasks_ValidatedPerTaskCommitsMayDifferFromHead_AcceptsEachOwnCommit()
    {
        var plan = MakePlan(FirstCommit, Head);
        var map = ReviewedCommits(plan);

        var updated = PlanHumanReviewStepAcceptance.ApplyTasks(
            plan, Attentions(plan), currentHead: Head, reviewedTaskCommits: map);

        Assert.Multiple(() =>
        {
            Assert.That(updated.Tasks.Select(task => task.Status), Is.All.EqualTo(PlanTaskStatus.Complete));
            Assert.That(updated.Tasks.Select(task => task.Commit), Is.EqualTo(new[] { FirstCommit, Head }));
            Assert.That(updated.Progress.CompletedCount, Is.EqualTo(2));
            Assert.That(plan.Tasks.Select(task => task.Status), Is.All.EqualTo(PlanTaskStatus.HumanReviewRequired));
        });
    }

    [Test]
    public void SharedCheckpoint_PrimaryActionAcceptsBothTasksWithTheirOwnDifferingCommits()
    {
        var plan = MakeSharedGatePlan();
        var gate = plan.ApprovalGates.Single();
        var action = PlanCheckpointPrimaryActionPresentationResolver.Resolve(plan, [gate.GateId]);

        Assert.Multiple(() =>
        {
            Assert.That(gate.AfterTaskIds, Is.EqualTo(new[] { "TASK-1", "TASK-2" }));
            Assert.That(action.Kind, Is.EqualTo(PlanCheckpointPrimaryActionKind.CompleteHumanReviewTasks));
            Assert.That(action.ReviewAttentions.Select(attention => attention.Task.TaskId),
                Is.EqualTo(gate.AfterTaskIds));
            Assert.That(action.ReviewAttentions.Select(attention => attention.Gate), Is.All.SameAs(gate));
        });

        var updated = PlanHumanReviewStepAcceptance.ApplyTasks(
            plan, action.ReviewAttentions, currentHead: Head, reviewedTaskCommits: ReviewedCommits(plan));

        Assert.Multiple(() =>
        {
            Assert.That(updated.Tasks.Select(task => task.Status), Is.All.EqualTo(PlanTaskStatus.Complete));
            Assert.That(updated.Tasks.Select(task => task.Commit), Is.EqualTo(new[] { FirstCommit, Head }));
            Assert.That(updated.Progress.CompletedCount, Is.EqualTo(2));
            Assert.That(updated.ApprovalGates.Single().Status, Is.EqualTo(PlanGateStatus.AwaitingApproval),
                "Task preparation leaves gate approval to the atomic runtime transition.");
            Assert.That(plan.Tasks.Select(task => task.Status), Is.All.EqualTo(PlanTaskStatus.HumanReviewRequired));
        });
    }

    [Test]
    public void SharedCheckpoint_UnrelatedDescendantHeadIsNotOwnedByReviewedTasks()
    {
        var plan = MakeSharedGatePlan();
        var gateIds = plan.ApprovalGates.Select(gate => gate.GateId).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(PlanHumanReviewCheckpointCommitPolicy.OwnsCurrentHead(plan, gateIds, Head), Is.True);
            Assert.That(PlanHumanReviewCheckpointCommitPolicy.OwnsCurrentHead(
                plan, gateIds, "unrelated-descendant"), Is.False);
        });
    }

    [Test]
    public void MixedCheckpoint_CompletedSiblingMayOwnHeadForSingleReviewedTask()
    {
        var plan = MakeSharedGatePlan();
        plan = plan with
        {
            Tasks =
            [
                plan.Tasks[0],
                plan.Tasks[1] with
                {
                    Status = PlanTaskStatus.Complete,
                    Commit = Head,
                    Handoff = null,
                },
            ],
            Progress = plan.Progress with { CompletedCount = 1 },
        };
        var gate = plan.ApprovalGates.Single();
        var action = PlanCheckpointPrimaryActionPresentationResolver.Resolve(plan, [gate.GateId]);

        Assert.Multiple(() =>
        {
            Assert.That(action.ReviewAttentions, Has.Count.EqualTo(1));
            Assert.That(action.ReviewAttentions[0].Task.TaskId, Is.EqualTo("TASK-1"));
            Assert.That(PlanHumanReviewCheckpointCommitPolicy.ResolveDurableCommits(plan, [gate.GateId]),
                Is.EquivalentTo(new[] { FirstCommit, Head }));
            Assert.That(PlanHumanReviewCheckpointCommitPolicy.OwnsCurrentHead(plan, [gate.GateId], Head), Is.True);
            Assert.That(PlanHumanReviewCheckpointCommitPolicy.OwnsCurrentHead(
                plan, [gate.GateId], "unrelated-descendant"), Is.False);
        });
    }

    [Test]
    public void MixedCheckpoint_CommitFromUnrepresentedSiblingCannotOwnHead()
    {
        var plan = MakePlan(FirstCommit, Head);
        plan = plan with
        {
            Tasks =
            [
                plan.Tasks[0],
                plan.Tasks[1] with { Status = PlanTaskStatus.Complete, Commit = Head, Handoff = null },
            ],
        };

        Assert.That(PlanHumanReviewCheckpointCommitPolicy.OwnsCurrentHead(
            plan, [plan.ApprovalGates[0].GateId], Head), Is.False);
    }

    [Test]
    public void CompletedVerifiedNoOp_UsesEvaluatedCommitAsCheckpointEvidence()
    {
        var plan = MakeNoOpPlan(Head);
        plan = plan with
        {
            Tasks =
            [
                plan.Tasks[0] with
                {
                    Status = PlanTaskStatus.Complete,
                    Handoff = null,
                    CompletionKind = DecomposeStepCompletionKind.VerifiedNoOp,
                    EvaluatedCommit = Head,
                },
            ],
        };

        Assert.That(PlanHumanReviewCheckpointCommitPolicy.ResolveDurableCommits(
            plan, [plan.ApprovalGates[0].GateId]), Is.EqualTo(new[] { Head }));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void SharedCheckpoint_OneMismatchedCommitRejectsWithoutTaskOrGateMutation(int mismatchedIndex)
    {
        var plan = MakeSharedGatePlan();
        var action = PlanCheckpointPrimaryActionPresentationResolver.Resolve(
            plan, [plan.ApprovalGates.Single().GateId]);
        var commits = ReviewedCommits(plan);
        commits[plan.Tasks[mismatchedIndex].TaskId] = "unreviewed-commit";
        Assert.That(action.ReviewAttentions, Has.Count.EqualTo(2));

        AssertRejectedWithoutMutation(plan, () => PlanHumanReviewStepAcceptance.ApplyTasks(
            plan, action.ReviewAttentions, currentHead: Head, reviewedTaskCommits: commits));
    }

    [TestCase(PlanTaskStatus.HumanReviewRequired, false, true)]
    [TestCase(PlanTaskStatus.Complete, true, true)]
    [TestCase(PlanTaskStatus.Superseded, true, true)]
    [TestCase(PlanTaskStatus.Pending, false, false)]
    public void SharedCheckpoint_GatePolicyRequiresExplicitOptInForPendingHumanReviewSibling(
        string siblingStatus, bool defaultCombines, bool attentionCombines)
    {
        var original = MakeSharedGatePlan();
        var plan = original with
        {
            Tasks = [original.Tasks[0], original.Tasks[1] with { Status = siblingStatus }],
        };
        var gate = plan.ApprovalGates.Single();

        Assert.Multiple(() =>
        {
            Assert.That(PlanHumanReviewGatePolicy.FindCombinableGate(plan, "TASK-1"),
                Is.EqualTo(defaultCombines ? gate : null));
            Assert.That(PlanHumanReviewGatePolicy.FindCombinableGate(
                    plan, "TASK-1", allowPendingReviewSiblings: true),
                Is.EqualTo(attentionCombines ? gate : null));
            Assert.That(PlanReviewAttentionPresentationResolver.Resolve(plan, "TASK-1")?.Gate,
                Is.EqualTo(attentionCombines ? gate : null));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    public void MultipleTasks_OneMapMismatchRejectsEntireImmutableTransition(int mismatchedIndex)
    {
        var plan = MakePlan(FirstCommit, Head);
        var map = ReviewedCommits(plan);
        map[plan.Tasks[mismatchedIndex].TaskId] = "unreviewed-commit";

        AssertRejectedWithoutMutation(plan, () => PlanHumanReviewStepAcceptance.ApplyTasks(
            plan, Attentions(plan), currentHead: Head, reviewedTaskCommits: map));
    }

    [Test]
    public void MultipleTasks_MissingMapEntryRejectsEntireTransition()
    {
        var plan = MakePlan(FirstCommit, Head);
        var map = ReviewedCommits(plan);
        map.Remove("TASK-2");

        AssertRejectedWithoutMutation(plan, () => PlanHumanReviewStepAcceptance.ApplyTasks(
            plan, Attentions(plan), currentHead: Head, reviewedTaskCommits: map));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void ValidatedMap_WithoutCurrentHeadRejects(string? currentHead)
    {
        var plan = MakePlan(FirstCommit, Head);

        AssertRejectedWithoutMutation(plan, () => PlanHumanReviewStepAcceptance.ApplyTasks(
            plan, Attentions(plan), currentHead: currentHead, reviewedTaskCommits: ReviewedCommits(plan)));
    }

    [Test]
    public void MultipleTasks_WithoutMapCannotTreatEarlierHandoffAsCurrentHead()
    {
        var plan = MakePlan(FirstCommit, Head);

        AssertRejectedWithoutMutation(plan, () =>
            PlanHumanReviewStepAcceptance.ApplyTasks(plan, Attentions(plan), currentHead: Head));
    }

    [Test]
    public void StaleAttentionAndMapMatchingEachOtherCannotOverrideCurrentDurableHandoff()
    {
        var displayed = MakePlan(FirstCommit, Head);
        var durable = displayed with
        {
            Tasks = [displayed.Tasks[0] with
            {
                Handoff = displayed.Tasks[0].Handoff! with { Commit = "new-durable-commit" },
            }, displayed.Tasks[1]],
        };

        AssertRejectedWithoutMutation(durable, () => PlanHumanReviewStepAcceptance.ApplyTasks(
            durable, Attentions(displayed), currentHead: Head, reviewedTaskCommits: ReviewedCommits(displayed)));
    }

    [Test]
    public void CurrentMapCannotRescueAttentionCapturedBeforeDurableHandoffChanged()
    {
        var displayed = MakePlan(FirstCommit);
        var durable = displayed with
        {
            Tasks = [displayed.Tasks[0] with { Handoff = displayed.Tasks[0].Handoff! with { Commit = Head } }],
        };

        AssertRejectedWithoutMutation(durable, () => PlanHumanReviewStepAcceptance.ApplyTasks(
            durable, Attentions(displayed), currentHead: Head, reviewedTaskCommits: ReviewedCommits(durable)));
    }

    [TestCase(PlanLifecycleStatus.Interrupted)]
    [TestCase(PlanLifecycleStatus.Blocked)]
    public void InterruptedSibling_DifferentEvidenceDoesNotRejectOtherReviewedTask(string lifecycle)
    {
        var plan = WithInterruption(MakePlan(FirstCommit, Head), lifecycle, "TASK-1", "TASK-1", FirstCommit);

        var updated = PlanHumanReviewStepAcceptance.ApplyTasks(
            plan, [Attentions(plan)[1]], currentHead: Head);

        Assert.Multiple(() =>
        {
            Assert.That(updated.Tasks[0].Status, Is.EqualTo(PlanTaskStatus.HumanReviewRequired));
            Assert.That(updated.Tasks[1].Status, Is.EqualTo(PlanTaskStatus.Complete));
            Assert.That(updated.Tasks[1].Commit, Is.EqualTo(Head));
            Assert.That(updated.InterruptionData, Is.SameAs(plan.InterruptionData),
                "Accepting a sibling must preserve the interrupted task's recovery evidence.");
        });
    }

    [TestCase(PlanLifecycleStatus.Interrupted)]
    [TestCase(PlanLifecycleStatus.Blocked)]
    public void MatchingInterruptedTask_CannotBorrowSiblingEvidenceEvenWhenCommitMatches(string lifecycle)
    {
        var plan = WithInterruption(MakePlan(Head, Head), lifecycle, "TASK-1", "TASK-2", Head);

        AssertRejectedWithoutMutation(plan, () => PlanHumanReviewStepAcceptance.ApplyTasks(
            plan, [Attentions(plan)[0]], currentHead: Head, reviewedTaskCommits: ReviewedCommits(plan)));
    }

    [TestCase(PlanLifecycleStatus.Interrupted)]
    [TestCase(PlanLifecycleStatus.Blocked)]
    public void MatchingInterruptedTask_StaleInterruptionEvidenceStillRejectsWithValidatedMap(string lifecycle)
    {
        var plan = WithInterruption(MakePlan(Head), lifecycle, "TASK-1", "TASK-1", FirstCommit);

        AssertRejectedWithoutMutation(plan, () => PlanHumanReviewStepAcceptance.ApplyTasks(
            plan, Attentions(plan), currentHead: Head, reviewedTaskCommits: ReviewedCommits(plan)));
    }

    [Test]
    public void MultipleInterruptedTasks_OnlyInterruptedTaskRequiresOwnInterruptionMatch()
    {
        var plan = WithInterruption(
            MakePlan(FirstCommit, Head), PlanLifecycleStatus.Interrupted, "TASK-1", "TASK-1", FirstCommit);

        var updated = PlanHumanReviewStepAcceptance.ApplyTasks(
            plan, Attentions(plan), currentHead: Head, reviewedTaskCommits: ReviewedCommits(plan));

        Assert.That(updated.Tasks.Select(task => task.Commit), Is.EqualTo(new[] { FirstCommit, Head }));
        Assert.That(updated.Tasks.Select(task => task.Status), Is.All.EqualTo(PlanTaskStatus.Complete));
        Assert.That(updated.InterruptionData, Is.Null);
    }

    [Test]
    public void AwaitingApproval_HistoricalInterruptionDataDoesNotRequireRecoveryEvidenceMatch()
    {
        var plan = WithInterruption(
            MakePlan(Head), PlanLifecycleStatus.AwaitingApproval, "TASK-1", "TASK-1", FirstCommit);

        var updated = PlanHumanReviewStepAcceptance.ApplyTasks(plan, Attentions(plan), currentHead: Head);

        Assert.That(updated.Tasks[0].Status, Is.EqualTo(PlanTaskStatus.Complete));
    }

    [Test]
    public void VerifiedNoOp_ValidatedEvaluatedCommitAcceptsWithoutChangedCommit()
    {
        var plan = MakeNoOpPlan(FirstCommit);

        var updated = PlanHumanReviewStepAcceptance.ApplyTasks(
            plan, Attentions(plan), currentHead: Head,
            reviewedTaskCommits: new Dictionary<string, string> { ["TASK-1"] = FirstCommit });

        Assert.Multiple(() =>
        {
            Assert.That(updated.Tasks[0].Status, Is.EqualTo(PlanTaskStatus.Complete));
            Assert.That(updated.Tasks[0].Commit, Is.Null);
            Assert.That(updated.Tasks[0].CompletionKind, Is.EqualTo(DecomposeStepCompletionKind.VerifiedNoOp));
            Assert.That(updated.Tasks[0].EvaluatedCommit, Is.EqualTo(FirstCommit));
        });
    }

    [Test]
    public void VerifiedNoOp_StaleEvaluatedCommitRejects()
    {
        var plan = MakeNoOpPlan(FirstCommit);

        AssertRejectedWithoutMutation(plan, () => PlanHumanReviewStepAcceptance.ApplyTasks(
            plan, Attentions(plan), currentHead: Head,
            reviewedTaskCommits: new Dictionary<string, string> { ["TASK-1"] = Head }));
    }

    [Test]
    public async Task FailedGitSnapshot_CannotAuthorizePreviouslyReviewedTaskMap()
    {
        var query = new PlanRecoveryGitQuery((_, _, _) =>
            Task.FromResult(PlanRecoveryGitQuery.CommandResult.Failed));
        var snapshot = await query.ReadSnapshotAsync(@"C:\Source\Repo", [FirstCommit])
            .WaitAsync(TimeSpan.FromSeconds(5));
        var plan = MakePlan(FirstCommit);

        AssertRejectedWithoutMutation(plan, () => PlanHumanReviewStepAcceptance.ApplyTasks(
            plan, Attentions(plan), currentHead: snapshot.Head, reviewedTaskCommits: ReviewedCommits(plan)));
    }

    private static void AssertRejectedWithoutMutation(Plan plan, TestDelegate transition)
    {
        var before = JsonSerializer.Serialize(plan);
        var exception = Assert.Throws<InvalidOperationException>(transition);
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.Not.Empty);
            Assert.That(JsonSerializer.Serialize(plan), Is.EqualTo(before),
                "Rejection must preserve every task, gate, progress value, and interruption record.");
        });
    }

    private static Dictionary<string, string> ReviewedCommits(Plan plan) =>
        plan.Tasks.ToDictionary(task => task.TaskId, task => task.Handoff!.Commit!, StringComparer.Ordinal);

    private static PlanReviewAttentionPresentation[] Attentions(Plan plan) =>
        plan.Tasks.Select(task =>
        {
            var attention = PlanReviewAttentionPresentationResolver.Resolve(plan, task.TaskId);
            Assert.That(attention, Is.Not.Null, "Fixture must resolve through the production checkpoint policy.");
            return attention!;
        }).ToArray();

    private static Plan MakeNoOpPlan(string evaluatedCommit)
    {
        var plan = MakePlan(evaluatedCommit);
        return plan with
        {
            Tasks = [plan.Tasks[0] with
            {
                Handoff = plan.Tasks[0].Handoff! with
                {
                    Commit = null,
                    CompletionKind = DecomposeStepCompletionKind.VerifiedNoOp,
                    EvaluatedCommit = evaluatedCommit,
                    NoOpEvidence = [new PlanTaskNoOpEvidence("Footer", "Already implemented.", ["Footer.cs"])],
                },
            }],
        };
    }

    private static Plan WithInterruption(
        Plan plan, string lifecycle, string interruptedTaskId, string evidenceTaskId, string commit) =>
        plan with
        {
            LifecycleStatus = lifecycle,
            InterruptionData = new PlanInterruptionData(
                "Human review required", PlanRecoveryState.PendingRecovery, 1,
                InterruptedTaskId: interruptedTaskId,
                TaskCommitEvidence: new PlanTaskCommitEvidence(
                    evidenceTaskId, "attempt", "baseline", commit, "Implemented task.")),
        };

    private static Plan MakePlan(params string[] commits)
    {
        var tasks = commits.Select((commit, index) => new PlanTask(
            $"TASK-{index + 1}", $"Step {index + 1}", "Work", [], "high", PlanTaskStatus.HumanReviewRequired,
            Handoff: new PlanTaskHandoff(
                commit, "Implemented task.", ["Footer.cs"],
                new DecomposeStepVerification("passed", "dotnet test", "98/98 tests passed"), Now))).ToArray();
        var gates = tasks.Select(task => new PlanApprovalGate(
            $"GATE-{task.TaskId}", "Review behavior", [task.TaskId], [], PlanGateStatus.AwaitingApproval,
            ProofRequirements: [new PlanTaskProofRequirement(
                $"proof-{task.TaskId}", "live-ui-observation", "Observe behavior.", "Does it render correctly?")]))
            .ToArray();
        return new Plan(
            "PLAN-1", "rev", PlanSource.Manual, PlanLifecycleStatus.AwaitingApproval,
            "Plan", "feature/plan", "Summary", tasks, gates,
            new PlanProgress(0, tasks.Length), new PlanTimestamps(Now));
    }

    private static Plan MakeSharedGatePlan()
    {
        var plan = MakePlan(FirstCommit, Head);
        return plan with
        {
            ApprovalGates = [plan.ApprovalGates[0] with
            {
                GateId = "SHARED-GATE",
                AfterTaskIds = plan.Tasks.Select(task => task.TaskId).ToArray(),
            }],
        };
    }
}
