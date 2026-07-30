using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Cross-component integration tests exercising the approval failure matrix.
/// Wires up <see cref="ApprovalGateReadinessEvaluator"/>,
/// <see cref="ApprovalActionCoordinator"/>, and <see cref="DurableApprovalRequestManager"/>
/// to test end-to-end flows, failure modes, and lifecycle invariants.
/// </summary>
[TestFixture]
internal sealed class ApprovalIntegrationTests
{
    private string _tempDir = null!;
    private InboxStore _inbox = null!;
    private ApprovalActionCoordinator _coordinator = null!;
    private DurableApprovalRequestManager _durableManager = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"squad-integ-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _inbox = new InboxStore(_tempDir);
        _coordinator = new ApprovalActionCoordinator();
        _durableManager = new DurableApprovalRequestManager(_inbox);
    }

    [TearDown]
    public void TearDown()
    {
        _coordinator.ClearAll();
        _durableManager.ClearLocks();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a plan with configurable task statuses and gates.
    ///   T1 ──┐
    ///         ├── [GATE-A] ──► T3 ──► T4
    ///   T2 ──┘
    ///   T5 (ungated, depends on T1 only)
    /// </summary>
    private static Plan MakePlan(
        string planId = "PLAN-001",
        string revision = "rev1",
        string t1Status = PlanTaskStatus.Pending,
        string t2Status = PlanTaskStatus.Pending,
        string t3Status = PlanTaskStatus.Pending,
        string t4Status = PlanTaskStatus.Pending,
        string t5Status = PlanTaskStatus.Pending,
        string gateAStatus = PlanGateStatus.Pending,
        string? t1Commit = null,
        string? t2Commit = null,
        IReadOnlyList<PlanApprovalGate>? extraGates = null,
        string lifecycleStatus = PlanLifecycleStatus.Executing)
    {
        var tasks = new List<PlanTask>
        {
            new("T1", "Task 1", "desc", [], "high", t1Status, Commit: t1Commit,
                CompletedAt: t1Status == PlanTaskStatus.Complete ? DateTimeOffset.UtcNow.AddMinutes(-10) : null),
            new("T2", "Task 2", "desc", [], "high", t2Status, Commit: t2Commit,
                CompletedAt: t2Status == PlanTaskStatus.Complete ? DateTimeOffset.UtcNow.AddMinutes(-5) : null),
            new("T3", "Task 3", "desc", ["T1", "T2"], "high", t3Status),
            new("T4", "Task 4", "desc", ["T3"], "high", t4Status),
            new("T5", "Task 5", "desc", ["T1"], "mid", t5Status),
        };
        var gates = new List<PlanApprovalGate>
        {
            new("GATE-A", "Review T1+T2 before T3", ["T1", "T2"], ["T3"], gateAStatus),
        };
        if (extraGates is not null)
            gates.AddRange(extraGates);

        var completed = tasks.Count(t => t.Status == PlanTaskStatus.Complete);
        return new Plan(
            planId, revision, PlanSource.DecomposeDecision,
            lifecycleStatus, "Integration Test Plan", "main", "Summary",
            tasks, gates,
            new PlanProgress(completed, tasks.Count),
            new PlanTimestamps(DateTimeOffset.UtcNow));
    }

    private static ApprovalReviewSnapshot MakeSnapshot(
        string planId = "PLAN-001",
        string gateId = "GATE-A",
        int completedTaskCount = 2,
        int totalTaskCount = 5) =>
        new(planId, "Integration Test Plan", completedTaskCount, totalTaskCount,
            PlanLifecycleStatus.Executing,
            gateId, "Review T1+T2 before T3", ["T1", "T2"], ["T3"],
            [], [], [], [], DateTimeOffset.UtcNow);

    // ═══════════════════════════════════════════════════════════════════════
    // 1. Stable approval-message identity across multiple checkpoints
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task StableMessageIdentity_AcrossMultipleCheckpoints_SameIdReturned()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var snap = MakeSnapshot();

        var id1 = await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snap);

        // Resolve first gate, then add a second gate
        await _durableManager.ResolveCheckpointAsync(plan, "GATE-A", "Approved");

        var gate2 = new PlanApprovalGate("GATE-B", "Second review", ["T3"], ["T4"], PlanGateStatus.AwaitingApproval);
        var planWithGate2 = MakePlan(extraGates: [gate2],
            t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var snap2 = MakeSnapshot(gateId: "GATE-B");

        var id2 = await _durableManager.AppendCheckpointAsync(planWithGate2, gate2, snap2);

        Assert.That(id1, Is.EqualTo(id2), "Message ID must be stable across all checkpoints for the same plan");
        Assert.That(id1, Is.EqualTo("approval-gate-PLAN-001"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Atomic live updates — version increments on every mutation
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task AtomicLiveUpdates_VersionIncrementsOnEveryMutation()
    {
        var token1 = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);
        Assert.That(token1.RequestVersion, Is.EqualTo(1));

        var token2 = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A", "GATE-B"]);
        Assert.That(token2.RequestVersion, Is.EqualTo(2));

        // Approve with token2 increments version again
        await _coordinator.TryApproveAsync(token2, ["GATE-A"]);
        var token3 = _coordinator.GetCurrentToken("PLAN-001");
        Assert.That(token3!.RequestVersion, Is.EqualTo(3));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. Open-window spinner and disabled actions
    //    (token is stale once state mutates ⇒ UI must re-fetch)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task OpenWindow_OldTokenDisabledAfterRefresh()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);

        // Simulate live update (e.g. evidence refresh): re-register bumps version
        await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);

        var result = await _coordinator.TryApproveAsync(token, ["GATE-A"]);
        Assert.That(result, Is.EqualTo(ApprovalClickResult.StaleRejected),
            "Old token must be rejected after any state mutation");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Update-versus-approval races
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task UpdateVsApprovalRace_ConcurrentRegisterAndApprove_OnlyOneWins()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);

        // Simulate race: register (update) vs approve with same token
        var approveTask = _coordinator.TryApproveAsync(token, ["GATE-A"]);
        var updateTask = _coordinator.RegisterAsync("PLAN-001", "rev2", ["GATE-A"]);

        await Task.WhenAll(approveTask, updateTask);
        var approveResult = await approveTask;

        // Either approve succeeded (ran first) or was stale (update ran first)
        Assert.That(approveResult,
            Is.EqualTo(ApprovalClickResult.Approved).Or.EqualTo(ApprovalClickResult.StaleRejected));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 5. Plan revision, request version, and exact gate-set validation
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task PlanRevisionValidation_ChangedRevision_RejectsClick()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);
        await _coordinator.RegisterAsync("PLAN-001", "rev2", ["GATE-A"]);

        var result = await _coordinator.TryApproveAsync(token, ["GATE-A"]);
        Assert.That(result, Is.EqualTo(ApprovalClickResult.StaleRejected));
    }

    [Test]
    public async Task RequestVersionValidation_AppendedGate_RejectsOldToken()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);
        await _coordinator.AppendGateAsync("PLAN-001", "rev1", "GATE-B");

        var result = await _coordinator.TryApproveAsync(token, ["GATE-A"]);
        Assert.That(result, Is.EqualTo(ApprovalClickResult.StaleRejected));
    }

    [Test]
    public async Task ExactGateSetValidation_DifferentGateSet_RejectsClick()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A", "GATE-B"]);
        // Update with different gate set
        await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);

        var result = await _coordinator.TryApproveAsync(token, ["GATE-A"]);
        Assert.That(result, Is.EqualTo(ApprovalClickResult.StaleRejected));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 6. Approval from Inbox and transcript
    //    (both surfaces use same coordinator ⇒ same token validation)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ApprovalFromMultipleSurfaces_SameCoordinator_FirstWins()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);

        // Surface 1 (Inbox) approves
        var inboxResult = await _coordinator.TryApproveAsync(token, ["GATE-A"], "From Inbox");
        Assert.That(inboxResult, Is.EqualTo(ApprovalClickResult.Approved));

        // Surface 2 (Transcript) tries same token — gate is already resolved
        // but token is stale because version incremented
        var transcriptResult = await _coordinator.TryApproveAsync(token, ["GATE-A"], "From Transcript");
        Assert.That(transcriptResult, Is.EqualTo(ApprovalClickResult.StaleRejected));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 7. Cross-surface control invalidation
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task CrossSurfaceInvalidation_EventFiredOnApproval()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A", "GATE-B"]);

        var resolvedEvents = new List<ApprovalResolvedEventArgs>();
        _coordinator.ApprovalResolved += (_, args) => resolvedEvents.Add(args);

        await _coordinator.TryApproveAsync(token, ["GATE-A"]);

        Assert.That(resolvedEvents, Has.Count.EqualTo(1));
        Assert.That(resolvedEvents[0].PlanId, Is.EqualTo("PLAN-001"));
        Assert.That(resolvedEvents[0].ResolvedGateIds, Does.Contain("GATE-A"));
        Assert.That(resolvedEvents[0].AllGatesResolved, Is.False,
            "GATE-B still active, AllGatesResolved must be false");
    }

    [Test]
    public async Task CrossSurfaceInvalidation_AllResolved_SetsAllGatesResolvedTrue()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);

        var resolvedEvents = new List<ApprovalResolvedEventArgs>();
        _coordinator.ApprovalResolved += (_, args) => resolvedEvents.Add(args);

        await _coordinator.TryApproveAsync(token, ["GATE-A"]);

        Assert.That(resolvedEvents, Has.Count.EqualTo(1));
        Assert.That(resolvedEvents[0].AllGatesResolved, Is.True);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 8. Resolved / read / archive behavior
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ResolvedReadArchive_AllGatesResolved_MessageArchivedAndRead()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var snap = MakeSnapshot();

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snap);
        await _durableManager.ResolveCheckpointAsync(plan, "GATE-A", "LGTM");

        var state = _durableManager.GetState("PLAN-001");
        Assert.That(state!.Archived, Is.True);
        Assert.That(state.ActiveGateIds, Is.Empty);
        Assert.That(state.ResolvedCheckpoints, Has.Count.EqualTo(1));
        Assert.That(state.ResolvedCheckpoints[0].GateId, Is.EqualTo("GATE-A"));

        var msg = _inbox.GetById("approval-gate-PLAN-001");
        Assert.That(msg!.Read, Is.True);
        Assert.That(msg.Actions, Is.Empty);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 9. Later unarchive using the same ID
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Unarchive_SameId_AfterNewGateArrival()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var snap = MakeSnapshot();

        var id = await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snap);
        await _durableManager.ResolveCheckpointAsync(plan, "GATE-A");
        Assert.That(_durableManager.IsArchived("PLAN-001"), Is.True);

        // New gate arrives on same plan — unarchive
        var gate2 = new PlanApprovalGate("GATE-B", "Second review", ["T3"], ["T4"], PlanGateStatus.AwaitingApproval);
        var planWithGate2 = MakePlan(extraGates: [gate2],
            t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var id2 = await _durableManager.AppendCheckpointAsync(planWithGate2, gate2, MakeSnapshot(gateId: "GATE-B"));

        Assert.That(id2, Is.EqualTo(id), "Same stable ID after unarchive");
        Assert.That(_durableManager.IsArchived("PLAN-001"), Is.False);

        var state = _durableManager.GetState("PLAN-001");
        Assert.That(state!.ActiveGateIds, Does.Contain("GATE-B"));
        Assert.That(state.ResolvedCheckpoints, Has.Count.EqualTo(1),
            "History of resolved gates is preserved across archive/unarchive");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 10. One and many reviewed commits
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ReviewSnapshot_SingleCommit_LinksCorrectly()
    {
        var commit = new CommitLink("abc1234", "abc1234567890123456789012345678901234567890", "Add feature");
        var changedFiles = new List<ChangedFileEntry>
        {
            new("src/Feature.cs", FileChangeStatus.Added, 50, 0, commit.FullSha,
                new FileLink("src/Feature.cs", commit.FullSha)),
        };
        var entry = new ReviewCommitEntry(commit, VerificationPassed: true, changedFiles);
        var taskEntry = new ReviewTaskEntry("T1", "Task 1", "Done", [entry]);

        Assert.That(taskEntry.Commits, Has.Count.EqualTo(1));
        Assert.That(taskEntry.Commits[0].Link.ShortSha, Is.EqualTo("abc1234"));
        Assert.That(taskEntry.Commits[0].Link.InternalUri, Does.StartWith("app://commit-diff:"));
    }

    [Test]
    public void ReviewSnapshot_MultipleCommits_AllTracked()
    {
        var commits = Enumerable.Range(0, 5).Select(i =>
        {
            var sha = $"{i + 1}a2b3c4d5e6f7890{i}abcdef1234567890abcd";
            var shortSha = sha[..7];
            var link = new CommitLink(shortSha, sha, $"Commit {i}");
            return new ReviewCommitEntry(link, VerificationPassed: true, []);
        }).ToList();

        var taskEntry = new ReviewTaskEntry("T1", "Task 1", "Done", commits);

        Assert.That(taskEntry.Commits, Has.Count.EqualTo(5));
        Assert.That(taskEntry.Commits.Select(c => c.Link.ShortSha).Distinct().Count(),
            Is.EqualTo(5), "Each commit should have a unique short SHA");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 11. Commit and historical-file links
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void CommitLink_InternalUri_FollowsAppScheme()
    {
        var link = new CommitLink("abc1234", "abc1234567890full", "Some commit");
        Assert.That(link.InternalUri, Is.EqualTo("app://commit-diff:abc1234567890full"));
    }

    [Test]
    public void FileLink_ReviewedVersionUri_ContainsCommitAndPath()
    {
        var fileLink = new FileLink("src/Module.cs", "deadbeef12345");
        Assert.That(fileLink.ReviewedVersionUri, Is.EqualTo("app://file-at-commit:deadbeef12345:src/Module.cs"));
        Assert.That(fileLink.WorkspaceFileUri, Is.EqualTo("app://open-workspace-file:src/Module.cs"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 12. Changed-file grouping
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ChangedFileEntry_GroupsByStatus()
    {
        var files = new List<ChangedFileEntry>
        {
            new("a.cs", FileChangeStatus.Added, 10, 0, "sha1", new FileLink("a.cs", "sha1")),
            new("b.cs", FileChangeStatus.Modified, 5, 3, "sha1", new FileLink("b.cs", "sha1")),
            new("c.cs", FileChangeStatus.Deleted, 0, 20, "sha1", new FileLink("c.cs", "sha1")),
            new("d.cs", FileChangeStatus.Modified, 8, 2, "sha2", new FileLink("d.cs", "sha2")),
            new("e.cs", FileChangeStatus.Added, 15, 0, "sha2", new FileLink("e.cs", "sha2")),
        };

        var grouped = files.GroupBy(f => f.Status).ToDictionary(g => g.Key, g => g.ToList());

        Assert.That(grouped[FileChangeStatus.Added], Has.Count.EqualTo(2));
        Assert.That(grouped[FileChangeStatus.Modified], Has.Count.EqualTo(2));
        Assert.That(grouped[FileChangeStatus.Deleted], Has.Count.EqualTo(1));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 13. Early approval while independent work continues
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void EarlyApproval_UngatedTaskContinues_WhileGateIsReady()
    {
        // T1, T2 complete; gate-A is ready. T5 (ungated) should still be eligible.
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            t5Status: PlanTaskStatus.Pending);

        var gateStates = ApprovalGateReadinessEvaluator.EvaluateGates(plan);
        Assert.That(gateStates, Has.Count.EqualTo(1));
        Assert.That(gateStates[0].IsReady, Is.True);

        // T5 depends on T1 (complete) and is NOT behind the gate
        var nextTask = ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan, gateStates);
        Assert.That(nextTask, Is.EqualTo("T5"),
            "Ungated task T5 must remain eligible while gate is awaiting approval");
    }

    [Test]
    public void EarlyApproval_GatedTaskBlocked_UngatedTaskEligible()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete);

        var blockedIds = ApprovalGateReadinessEvaluator.ComputeAllBlockedTaskIds(plan);

        Assert.That(blockedIds, Does.Contain("T3"), "T3 is directly behind GATE-A");
        Assert.That(blockedIds, Does.Contain("T4"), "T4 is transitively behind GATE-A");
        Assert.That(blockedIds, Does.Not.Contain("T5"), "T5 is ungated");
        Assert.That(blockedIds, Does.Not.Contain("T1"), "T1 is before the gate");
        Assert.That(blockedIds, Does.Not.Contain("T2"), "T2 is before the gate");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 14. Fully blocked transition
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void FullyBlocked_WhenNoUngatedWorkRemains_ShouldStopForApproval()
    {
        // T1, T2 complete; T5 also complete; only T3, T4 remain (gated)
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            t5Status: PlanTaskStatus.Complete);

        var shouldStop = ApprovalGateReadinessEvaluator.ShouldStopForApproval(plan);

        Assert.That(shouldStop, Is.True,
            "Must stop when all remaining work is behind unapproved gates");
    }

    [Test]
    public void FullyBlocked_PlanStoreUpdater_TransitionsToAwaitingApproval()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            t5Status: PlanTaskStatus.Complete);

        var updated = PlanStoreUpdater.ApplyFullStopAtGates(plan, ["GATE-A"]);

        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval));
        Assert.That(updated.Progress.ExecutingTaskId, Is.Null);
        Assert.That(updated.ApprovalGates[0].Status, Is.EqualTo(PlanGateStatus.AwaitingApproval));
    }

    [Test]
    public void NotBlocked_WhenUngatedWorkExists_ShouldNotStop()
    {
        // T1, T2 complete; T5 still pending (ungated work remains)
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            t5Status: PlanTaskStatus.Pending);

        var shouldStop = ApprovalGateReadinessEvaluator.ShouldStopForApproval(plan);

        Assert.That(shouldStop, Is.False,
            "Should not stop when ungated T5 is still eligible");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 15. Restart restoration
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task RestartRestoration_ActivePlansResumedFromInbox()
    {
        var plan1 = MakePlan("PLAN-A", t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var plan2 = MakePlan("PLAN-B", t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var snap1 = MakeSnapshot("PLAN-A");
        var snap2 = MakeSnapshot("PLAN-B");

        await _durableManager.AppendCheckpointAsync(plan1, plan1.ApprovalGates[0], snap1);
        await _durableManager.AppendCheckpointAsync(plan2, plan2.ApprovalGates[0], snap2);

        // Resolve one plan's gates — it gets archived
        await _durableManager.ResolveCheckpointAsync(plan2, "GATE-A");

        // Simulate restart: new manager reads inbox
        var freshManager = new DurableApprovalRequestManager(_inbox);
        var activePlanIds = freshManager.RestoreActivePlanIds();

        Assert.That(activePlanIds, Does.Contain("PLAN-A"), "Active plan must survive restart");
        Assert.That(activePlanIds, Does.Not.Contain("PLAN-B"), "Archived plan must not be restored");
    }

    [Test]
    public async Task RestartRestoration_CoordinatorCanReRegisterFromDurableState()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var snap = MakeSnapshot();

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snap);

        // Read state back (simulating restart)
        var state = _durableManager.GetState("PLAN-001");
        Assert.That(state, Is.Not.Null);

        // Re-register into coordinator
        var token = await _coordinator.RegisterAsync(
            "PLAN-001", plan.Revision, state!.ActiveGateIds.ToList());

        Assert.That(token.GateIds, Is.EqualTo(new[] { "GATE-A" }));
        Assert.That(_coordinator.HasActiveGates("PLAN-001"), Is.True);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 16. Notification deduplication
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task NotificationDedup_TryMarkNotified_OnlyFirstCallReturnsTrue()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var snap = MakeSnapshot();
        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snap);

        var first = await _durableManager.TryMarkNotifiedAsync("PLAN-001");
        var second = await _durableManager.TryMarkNotifiedAsync("PLAN-001");
        var third = await _durableManager.TryMarkNotifiedAsync("PLAN-001");

        Assert.That(first, Is.True, "First notification should proceed");
        Assert.That(second, Is.False, "Subsequent notifications should be deduped");
        Assert.That(third, Is.False, "Subsequent notifications should be deduped");
    }

    [Test]
    public async Task NotificationDedup_ConcurrentCalls_OnlyOneSucceeds()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var snap = MakeSnapshot();
        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snap);

        var results = await Task.WhenAll(
            _durableManager.TryMarkNotifiedAsync("PLAN-001"),
            _durableManager.TryMarkNotifiedAsync("PLAN-001"),
            _durableManager.TryMarkNotifiedAsync("PLAN-001"));

        Assert.That(results.Count(r => r), Is.EqualTo(1), "Exactly one concurrent call should succeed");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // KEY INVARIANT: Unseen newly ready gate is never approved by older button
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task UnseenGate_NeverApprovedByOlderToken()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);

        // Approve GATE-A
        await _coordinator.TryApproveAsync(token, ["GATE-A"]);

        // New gate arrives (unseen by the old button)
        await _coordinator.AppendGateAsync("PLAN-001", "rev1", "GATE-B");

        // Try to approve GATE-B with the old token — must be rejected
        var result = await _coordinator.TryApproveAsync(token, ["GATE-B"]);
        Assert.That(result, Is.EqualTo(ApprovalClickResult.StaleRejected),
            "An older token must NEVER be able to approve a newly arrived gate");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // KEY INVARIANT: No downstream boundary crossed early
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void DownstreamBoundary_NeverCrossedBeforeApproval()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.Pending);

        var gateStates = ApprovalGateReadinessEvaluator.EvaluateGates(plan);
        var blocked = ApprovalGateReadinessEvaluator.ComputeAllBlockedTaskIds(plan, gateStates);
        var nextTask = ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan, gateStates);

        // T3 is the direct downstream task — must be blocked
        Assert.That(blocked, Does.Contain("T3"));
        Assert.That(blocked, Does.Contain("T4"));

        // Next task is T5 (ungated), never T3 or T4
        if (nextTask is not null)
        {
            Assert.That(blocked, Does.Not.Contain(nextTask),
                "Selected next task must not be behind an unapproved gate");
        }
    }

    [Test]
    public void DownstreamBoundary_ApprovalReleasesGatedTasks()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.Approved);

        var gateStates = ApprovalGateReadinessEvaluator.EvaluateGates(plan);

        // Approved gate should not be evaluated
        Assert.That(gateStates, Is.Empty);

        // T3 should now be eligible
        var blocked = ApprovalGateReadinessEvaluator.ComputeAllBlockedTaskIds(plan, gateStates);
        Assert.That(blocked, Is.Empty, "No tasks should be blocked after gate approval");

        var nextTask = ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan, gateStates);
        Assert.That(nextTask, Is.EqualTo("T3").Or.EqualTo("T5"),
            "T3 or T5 should be eligible after gate approval");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // KEY INVARIANT: Unrelated eligible work continues
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void UnrelatedWork_ContinuesDuringApprovalWait()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Pending,
            t5Status: PlanTaskStatus.Pending);

        // Gate-A is NOT ready (T2 not complete), so gate doesn't block
        var gateStates = ApprovalGateReadinessEvaluator.EvaluateGates(plan);
        Assert.That(gateStates[0].IsReady, Is.False);

        var nextTask = ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan, gateStates);

        // T2 has no dependencies → eligible. T5 depends on T1 (complete) → eligible.
        Assert.That(nextTask, Is.EqualTo("T2").Or.EqualTo("T5"),
            "Unrelated work must continue while gate prerequisites are pending");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // KEY INVARIANT: No completed work reruns
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void CompletedWork_NeverReselectedForExecution()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            t5Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.Approved);

        // After gate approval, only T3 and T4 remain
        var nextTask = ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan);

        Assert.That(nextTask, Is.Not.EqualTo("T1"));
        Assert.That(nextTask, Is.Not.EqualTo("T2"));
        Assert.That(nextTask, Is.Not.EqualTo("T5"));
        Assert.That(nextTask, Is.EqualTo("T3"),
            "Only T3 (first pending ungated task with satisfied deps) should be next");
    }

    [Test]
    public void CompletedWork_SupersededTasksAlsoExcluded()
    {
        var tasks = new List<PlanTask>
        {
            new("T1", "Task 1", "desc", [], "high", PlanTaskStatus.Superseded),
            new("T2", "Task 2", "desc", [], "high", PlanTaskStatus.Complete),
            new("T3", "Task 3", "desc", ["T1", "T2"], "high", PlanTaskStatus.Pending),
        };
        var gate = new PlanApprovalGate("GATE-A", "Review", ["T1", "T2"], ["T3"], PlanGateStatus.Approved);
        var plan = new Plan("PLAN-001", "rev1", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.Executing, "Test", "main", "Summary",
            tasks, [gate], new PlanProgress(2, 3), new PlanTimestamps(DateTimeOffset.UtcNow));

        var terminal = ApprovalGateReadinessEvaluator.GetTerminalTaskIds(plan);
        Assert.That(terminal, Does.Contain("T1"), "Superseded counts as terminal");
        Assert.That(terminal, Does.Contain("T2"), "Complete counts as terminal");

        var nextTask = ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan);
        Assert.That(nextTask, Is.EqualTo("T3"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Full end-to-end flow: gate ready → request published → click validates
    //                       → resolution propagates
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task FullFlow_GateReady_RequestPublished_ClickValidated_ResolutionPropagates()
    {
        // Step 1: Build plan with gate becoming ready
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete);

        // Step 2: Evaluate readiness
        var gateStates = ApprovalGateReadinessEvaluator.EvaluateGates(plan);
        Assert.That(gateStates, Has.Count.EqualTo(1));
        Assert.That(gateStates[0].IsReady, Is.True);

        var readyGateIds = ApprovalGateReadinessEvaluator.GetReadyGateIds(gateStates);
        Assert.That(readyGateIds, Does.Contain("GATE-A"));

        // Step 3: Register in coordinator
        var token = await _coordinator.RegisterAsync(
            plan.PlanId, plan.Revision, readyGateIds);

        // Step 4: Publish to durable manager (Inbox)
        var snap = MakeSnapshot();
        var messageId = await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snap);
        Assert.That(messageId, Is.Not.Null);

        // Step 5: Click validates and resolves
        ApprovalResolvedEventArgs? resolvedArgs = null;
        _coordinator.ApprovalResolved += (_, args) => resolvedArgs = args;

        var clickResult = await _coordinator.TryApproveAsync(token, ["GATE-A"], "Reviewed and approved");
        Assert.That(clickResult, Is.EqualTo(ApprovalClickResult.Approved));

        // Step 6: Resolution propagates
        Assert.That(resolvedArgs, Is.Not.Null);
        Assert.That(resolvedArgs!.AllGatesResolved, Is.True);

        // Step 7: Resolve in durable manager
        await _durableManager.ResolveCheckpointAsync(plan, "GATE-A", "Reviewed and approved");

        var state = _durableManager.GetState("PLAN-001");
        Assert.That(state!.Archived, Is.True);
        Assert.That(state.ActiveGateIds, Is.Empty);

        // Step 8: Apply gate approved in plan store
        var updatedPlan = PlanStoreUpdater.ApplyGateApproved(
            plan with { ApprovalGates = [plan.ApprovalGates[0] with { Status = PlanGateStatus.AwaitingApproval }] },
            "GATE-A", "Reviewed and approved");
        Assert.That(updatedPlan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));

        // Step 9: Released tasks become eligible
        var releasedTasks = ApprovalGateReadinessEvaluator.GetReleasedTaskIds(updatedPlan, "GATE-A");
        Assert.That(releasedTasks, Does.Contain("T3"),
            "T3 should be released after gate approval");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Lifecycle: archive → unarchive → re-approve (end-to-end)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Lifecycle_ArchiveUnarchiveReApprove()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var snap = MakeSnapshot();

        // Initial checkpoint → approve → archive
        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snap);
        await _durableManager.ResolveCheckpointAsync(plan, "GATE-A", "Round 1");
        Assert.That(_durableManager.IsArchived("PLAN-001"), Is.True);

        // New gate arrives → unarchive
        var gate2 = new PlanApprovalGate("GATE-B", "Round 2", ["T3"], ["T4"], PlanGateStatus.AwaitingApproval);
        var planWithGate2 = MakePlan(extraGates: [gate2],
            t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete,
            t3Status: PlanTaskStatus.Complete);
        await _durableManager.AppendCheckpointAsync(planWithGate2, gate2, MakeSnapshot(gateId: "GATE-B"));
        Assert.That(_durableManager.IsArchived("PLAN-001"), Is.False);

        // Re-approve → archive again
        await _durableManager.ResolveCheckpointAsync(planWithGate2, "GATE-B", "Round 2");
        Assert.That(_durableManager.IsArchived("PLAN-001"), Is.True);

        var state = _durableManager.GetState("PLAN-001");
        Assert.That(state!.ResolvedCheckpoints, Has.Count.EqualTo(2));
        Assert.That(state.ResolvedCheckpoints[0].GateId, Is.EqualTo("GATE-A"));
        Assert.That(state.ResolvedCheckpoints[1].GateId, Is.EqualTo("GATE-B"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Stale clicks from multiple failure vectors
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task StaleClick_AllVectors_Rejected()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);

        // Vector 1: revision change
        await _coordinator.RegisterAsync("PLAN-001", "rev2", ["GATE-A"]);
        Assert.That(await _coordinator.TryApproveAsync(token, ["GATE-A"]),
            Is.EqualTo(ApprovalClickResult.StaleRejected), "Revision-stale click");

        // Vector 2: re-register with new gate set
        var token2 = await _coordinator.RegisterAsync("PLAN-001", "rev2", ["GATE-A"]);
        await _coordinator.RegisterAsync("PLAN-001", "rev2", ["GATE-A", "GATE-B"]);
        Assert.That(await _coordinator.TryApproveAsync(token2, ["GATE-A"]),
            Is.EqualTo(ApprovalClickResult.StaleRejected), "Gate-set-stale click");

        // Vector 3: version bump via append
        var token3 = await _coordinator.RegisterAsync("PLAN-001", "rev2", ["GATE-A"]);
        await _coordinator.AppendGateAsync("PLAN-001", "rev2", "GATE-C");
        Assert.That(await _coordinator.TryApproveAsync(token3, ["GATE-A"]),
            Is.EqualTo(ApprovalClickResult.StaleRejected), "Version-stale click");

        // Vector 4: unregistered plan
        _coordinator.Unregister("PLAN-001");
        Assert.That(await _coordinator.TryApproveAsync(token, ["GATE-A"]),
            Is.EqualTo(ApprovalClickResult.StaleRejected), "Unregistered-plan click");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Sequential gates (nested): gate-A before gate-B
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void SequentialGates_InnerGateBlocksUntilOuterApproved()
    {
        var tasks = new[]
        {
            new PlanTask("T1", "Task 1", "desc", [], "high", PlanTaskStatus.Complete),
            new PlanTask("T2", "Task 2", "desc", ["T1"], "high", PlanTaskStatus.Pending),
            new PlanTask("T3", "Task 3", "desc", ["T2"], "high", PlanTaskStatus.Pending),
        };
        var gateA = new PlanApprovalGate("GATE-A", "First gate",
            ["T1"], ["T2"], PlanGateStatus.Pending);
        var gateB = new PlanApprovalGate("GATE-B", "Second gate",
            ["T2"], ["T3"], PlanGateStatus.Pending);
        var plan = new Plan("PLAN-S", "rev1", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.Executing, "Sequential", "main", "",
            tasks, [gateA, gateB], new PlanProgress(1, 3),
            new PlanTimestamps(DateTimeOffset.UtcNow));

        var gateStates = ApprovalGateReadinessEvaluator.EvaluateGates(plan);

        // Gate-A is ready (T1 complete), Gate-B is not ready (T2 pending)
        Assert.That(gateStates.Count(g => g.IsReady), Is.EqualTo(1));
        Assert.That(gateStates.First(g => g.IsReady).GateId, Is.EqualTo("GATE-A"));
        Assert.That(gateStates.First(g => !g.IsReady).GateId, Is.EqualTo("GATE-B"));

        // All tasks behind any gate are blocked
        var blocked = ApprovalGateReadinessEvaluator.ComputeAllBlockedTaskIds(plan, gateStates);
        Assert.That(blocked, Does.Contain("T2"));
        Assert.That(blocked, Does.Contain("T3"));

        // No ungated task available
        var nextTask = ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan, gateStates);
        Assert.That(nextTask, Is.Null, "No ungated work remains");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Concurrent gate appends are serialized
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ConcurrentGateAppends_AreSerialized()
    {
        await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);

        var tasks = Enumerable.Range(0, 20)
            .Select(i => _coordinator.AppendGateAsync("PLAN-001", "rev1", $"GATE-{i}"))
            .ToArray();

        await Task.WhenAll(tasks);

        var activeGates = _coordinator.GetActiveGateIds("PLAN-001");
        // GATE-A + 20 new gates (but some may duplicate GATE-A range overlap)
        Assert.That(activeGates.Count, Is.GreaterThanOrEqualTo(20));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DurableManager + Coordinator integration
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task DurableAndCoordinator_ApprovalSynced()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var snap = MakeSnapshot();

        // Register in both systems
        var token = await _coordinator.RegisterAsync(plan.PlanId, plan.Revision, ["GATE-A"]);
        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snap);

        // Approve via coordinator
        var result = await _coordinator.TryApproveAsync(token, ["GATE-A"], "LGTM");
        Assert.That(result, Is.EqualTo(ApprovalClickResult.Approved));

        // Propagate to durable manager
        await _durableManager.ResolveCheckpointAsync(plan, "GATE-A", "LGTM");

        // Verify both are consistent
        Assert.That(_coordinator.HasActiveGates("PLAN-001"), Is.False);
        Assert.That(_durableManager.IsArchived("PLAN-001"), Is.True);

        var durableState = _durableManager.GetState("PLAN-001");
        Assert.That(durableState!.ResolvedCheckpoints, Has.Count.EqualTo(1));
        Assert.That(durableState.ResolvedCheckpoints[0].ResolutionNote, Is.EqualTo("LGTM"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ApprovalCardNotificationCoordinator.BuildApproveLabel
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void BuildApproveLabel_SingleGate_UseSingularForm()
    {
        var label = ApprovalCardNotificationCoordinator.BuildApproveLabel(1);
        Assert.That(label, Does.Contain("Approve Checkpoint"));
        Assert.That(label, Does.Not.Contain("2"));
    }

    [Test]
    public void BuildApproveLabel_MultipleGates_ShowsCount()
    {
        var label = ApprovalCardNotificationCoordinator.BuildApproveLabel(3);
        Assert.That(label, Does.Contain("3"));
        Assert.That(label, Does.Contain("Checkpoints"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Body and actions reflect current state
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void BuildBody_ActiveGatesShown_ResolvedGatesShown()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var resolved = new List<ResolvedCheckpointEntry>
        {
            new("GATE-OLD", DateTimeOffset.UtcNow, "Previously approved"),
        };

        var body = DurableApprovalRequestManager.BuildBody(plan, ["GATE-A"], resolved);

        Assert.That(body, Does.Contain("GATE-A"));
        Assert.That(body, Does.Contain("GATE-OLD"));
        Assert.That(body, Does.Contain("Previously approved"));
        Assert.That(body, Does.Contain("1 checkpoint(s) awaiting approval"));
        Assert.That(body, Does.Contain("1 resolved checkpoint(s)"));
    }

    [Test]
    public void BuildBody_NoActiveGates_ShowsArchived()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var resolved = new List<ResolvedCheckpointEntry>
        {
            new("GATE-A", DateTimeOffset.UtcNow, "Done"),
        };

        var body = DurableApprovalRequestManager.BuildBody(plan, [], resolved);

        Assert.That(body, Does.Contain("archived"));
        Assert.That(body, Does.Not.Contain("awaiting approval"));
    }
}
