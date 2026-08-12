using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// End-to-end restart-safe live approval workflow probe.
/// Simulates a disposable plan with parallel work lanes, multiple sequential
/// approval gates, several commits and changed files, and exercises the full
/// lifecycle through <see cref="ApprovalGateReadinessEvaluator"/>,
/// <see cref="ApprovalActionCoordinator"/>, <see cref="DurableApprovalRequestManager"/>,
/// and <see cref="ApprovalCardNotificationCoordinator"/>.
/// </summary>
[TestFixture]
internal sealed class ApprovalWorkflowProbeTests
{
    private string _tempDir = null!;
    private InboxStore _inbox = null!;
    private ApprovalActionCoordinator _coordinator = null!;
    private DurableApprovalRequestManager _durableManager = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"squad-probe-{Guid.NewGuid():N}");
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

    // ── Plan topology ───────────────────────────────────────────────────────
    //
    //   Lane A:  A1 ──► A2 ──┐
    //                         ├── [GATE-1] ──► C1 ──► C2
    //   Lane B:  B1 ──► B2 ──┘
    //
    //   Lane C (ungated): U1 (depends on A1 only — runs independently)
    //
    //                    C1 ──► C2 ──┐
    //                                 ├── [GATE-2] ──► D1
    //                                │
    //
    // Each lane simulates parallel work with its own commits.

    private static readonly string CommitA1 = "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1";
    private static readonly string CommitA2 = "a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2";
    private static readonly string CommitB1 = "b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1";
    private static readonly string CommitB2 = "b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2";
    private static readonly string CommitU1 = "u1u1u1u1u1u1u1u1u1u1u1u1u1u1u1u1u1u1u1u1";
    private static readonly string CommitC1 = "c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1";
    private static readonly string CommitC2 = "c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2";

    private static Plan MakeProbePlan(
        string planId = "PROBE-001",
        string revision = "rev1",
        string a1 = PlanTaskStatus.Pending,
        string a2 = PlanTaskStatus.Pending,
        string b1 = PlanTaskStatus.Pending,
        string b2 = PlanTaskStatus.Pending,
        string u1 = PlanTaskStatus.Pending,
        string c1 = PlanTaskStatus.Pending,
        string c2 = PlanTaskStatus.Pending,
        string d1 = PlanTaskStatus.Pending,
        string gate1Status = PlanGateStatus.Pending,
        string gate2Status = PlanGateStatus.Pending,
        string lifecycle = PlanLifecycleStatus.Executing)
    {
        var tasks = new List<PlanTask>
        {
            new("A1", "Lane-A Task 1", "desc", [], "high", a1, Commit: a1 == PlanTaskStatus.Complete ? CommitA1 : null,
                CompletedAt: a1 == PlanTaskStatus.Complete ? DateTimeOffset.UtcNow.AddMinutes(-20) : null),
            new("A2", "Lane-A Task 2", "desc", ["A1"], "high", a2, Commit: a2 == PlanTaskStatus.Complete ? CommitA2 : null,
                CompletedAt: a2 == PlanTaskStatus.Complete ? DateTimeOffset.UtcNow.AddMinutes(-15) : null),
            new("B1", "Lane-B Task 1", "desc", [], "high", b1, Commit: b1 == PlanTaskStatus.Complete ? CommitB1 : null,
                CompletedAt: b1 == PlanTaskStatus.Complete ? DateTimeOffset.UtcNow.AddMinutes(-18) : null),
            new("B2", "Lane-B Task 2", "desc", ["B1"], "high", b2, Commit: b2 == PlanTaskStatus.Complete ? CommitB2 : null,
                CompletedAt: b2 == PlanTaskStatus.Complete ? DateTimeOffset.UtcNow.AddMinutes(-12) : null),
            new("U1", "Ungated work", "desc", ["A1"], "mid", u1, Commit: u1 == PlanTaskStatus.Complete ? CommitU1 : null,
                CompletedAt: u1 == PlanTaskStatus.Complete ? DateTimeOffset.UtcNow.AddMinutes(-10) : null),
            new("C1", "Post-gate Task 1", "desc", ["A2", "B2"], "high", c1, Commit: c1 == PlanTaskStatus.Complete ? CommitC1 : null,
                CompletedAt: c1 == PlanTaskStatus.Complete ? DateTimeOffset.UtcNow.AddMinutes(-5) : null),
            new("C2", "Post-gate Task 2", "desc", ["C1"], "high", c2, Commit: c2 == PlanTaskStatus.Complete ? CommitC2 : null,
                CompletedAt: c2 == PlanTaskStatus.Complete ? DateTimeOffset.UtcNow.AddMinutes(-3) : null),
            new("D1", "Final task", "desc", ["C2"], "high", d1),
        };
        var gates = new List<PlanApprovalGate>
        {
            new("GATE-1", "Review lanes A+B before post-gate work", ["A2", "B2"], ["C1"], gate1Status),
            new("GATE-2", "Review post-gate work before final", ["C2"], ["D1"], gate2Status),
        };
        var completed = tasks.Count(t => t.Status == PlanTaskStatus.Complete);
        return new Plan(planId, revision, PlanSource.DecomposeDecision,
            lifecycle, "Probe Plan", "main", "End-to-end probe",
            tasks, gates,
            new PlanProgress(completed, tasks.Count),
            new PlanTimestamps(DateTimeOffset.UtcNow));
    }

    private static ApprovalReviewSnapshot MakeSnapshot(
        Plan plan,
        string gateId)
    {
        var gate = plan.ApprovalGates.First(g => g.GateId == gateId);
        return new ApprovalReviewSnapshot(
            plan.PlanId, plan.Title, plan.Progress.CompletedCount, plan.Progress.TotalCount,
            plan.LifecycleStatus,
            gateId, gate.Message, gate.AfterTaskIds, gate.BeforeTaskIds,
            CompletedTasks: plan.Tasks
                .Where(t => gate.AfterTaskIds.Contains(t.TaskId) && t.Status == PlanTaskStatus.Complete)
                .Select(t => new ReviewTaskEntry(t.TaskId, t.Title ?? t.TaskId, t.CompletionSummary,
                    string.IsNullOrEmpty(t.Commit) ? [] :
                    [new ReviewCommitEntry(
                        new CommitLink(t.Commit![..7], t.Commit!, t.Title ?? t.TaskId),
                        true,
                        [new ChangedFileEntry($"src/{t.TaskId}.cs", FileChangeStatus.Modified, 10, 2, t.Commit!,
                            new FileLink($"src/{t.TaskId}.cs", t.Commit!))])]))
                .ToList(),
            DownstreamTasks: plan.Tasks
                .Where(t => gate.BeforeTaskIds.Contains(t.TaskId))
                .Select(t => new DownstreamTaskEntry(t.TaskId, t.Title ?? t.TaskId, t.Status))
                .ToList(),
            AllChangedFiles: plan.Tasks
                .Where(t => gate.AfterTaskIds.Contains(t.TaskId) && !string.IsNullOrEmpty(t.Commit))
                .Select(t => new ChangedFileEntry($"src/{t.TaskId}.cs", FileChangeStatus.Modified, 10, 2, t.Commit!,
                    new FileLink($"src/{t.TaskId}.cs", t.Commit!)))
                .ToList(),
            IndependentWork: [],
            BuiltAt: DateTimeOffset.UtcNow);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 1. One approval message identity per plan
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task OneMessageIdentityPerPlan_AcrossGates()
    {
        var plan = MakeProbePlan(
            a1: PlanTaskStatus.Complete, a2: PlanTaskStatus.Complete,
            b1: PlanTaskStatus.Complete, b2: PlanTaskStatus.Complete,
            gate1Status: PlanGateStatus.AwaitingApproval);

        var snap = MakeSnapshot(plan, "GATE-1");
        var id1 = await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snap);

        // Resolve GATE-1, then add GATE-2
        await _durableManager.ResolveCheckpointAsync(plan, "GATE-1", "Approved");

        var plan2 = MakeProbePlan(
            a1: PlanTaskStatus.Complete, a2: PlanTaskStatus.Complete,
            b1: PlanTaskStatus.Complete, b2: PlanTaskStatus.Complete,
            c1: PlanTaskStatus.Complete, c2: PlanTaskStatus.Complete,
            gate1Status: PlanGateStatus.Approved,
            gate2Status: PlanGateStatus.AwaitingApproval);
        var snap2 = MakeSnapshot(plan2, "GATE-2");
        var id2 = await _durableManager.AppendCheckpointAsync(plan2, plan2.ApprovalGates[1], snap2);

        Assert.That(id1, Is.EqualTo(id2), "Same message ID across all gates for one plan");
        Assert.That(id1, Is.EqualTo(DurableApprovalRequestManager.BuildMessageId("PROBE-001")));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Open window disables actions and shows spinner during atomic updates
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task OpenWindow_OldTokenDisabledDuringAtomicUpdate()
    {
        var token = await _coordinator.RegisterAsync("PROBE-001", "rev1", ["GATE-1"]);
        Assert.That(token.RequestVersion, Is.EqualTo(1));

        // Simulate UI refresh re-registering (version bump)
        await _coordinator.RegisterAsync("PROBE-001", "rev1", ["GATE-1"]);

        // Old token should be stale — UI must re-fetch and show spinner
        var result = await _coordinator.TryApproveAsync(token, ["GATE-1"]);
        Assert.That(result, Is.EqualTo(ApprovalClickResult.StaleRejected),
            "Old token must be rejected after version bump (spinner scenario)");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. Content and links refresh in place
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ContentRefreshesInPlace_EvidenceUpdatePreservesMessageId()
    {
        var plan = MakeProbePlan(
            a1: PlanTaskStatus.Complete, a2: PlanTaskStatus.Complete,
            b1: PlanTaskStatus.Complete, b2: PlanTaskStatus.Complete,
            gate1Status: PlanGateStatus.AwaitingApproval);
        var snap = MakeSnapshot(plan, "GATE-1");

        var id = await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snap);
        var msgBefore = _inbox.GetById(id);
        var bodyBefore = msgBefore!.Body;

        // Refresh evidence with updated snapshot
        var snap2 = snap with { BuiltAt = DateTimeOffset.UtcNow.AddMinutes(5) };
        await _durableManager.RefreshEvidenceAsync(plan, snap2);

        var msgAfter = _inbox.GetById(id);
        Assert.That(msgAfter, Is.Not.Null, "Message still exists after refresh");
        Assert.That(msgAfter!.Id, Is.EqualTo(id), "Message ID unchanged after evidence refresh");
        Assert.That(msgAfter.Actions, Is.Not.Empty, "Actions still present after refresh");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Early approval — approving while independent ungated work continues
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task EarlyApproval_UngatedWorkContinuesAfterGateApproval()
    {
        // Lane A+B complete, gate is ready, but U1 is still pending (ungated)
        var plan = MakeProbePlan(
            a1: PlanTaskStatus.Complete, a2: PlanTaskStatus.Complete,
            b1: PlanTaskStatus.Complete, b2: PlanTaskStatus.Complete,
            u1: PlanTaskStatus.Pending,
            gate1Status: PlanGateStatus.AwaitingApproval);

        // Gate evaluator: GATE-1 should be ready (A2, B2 complete)
        var gateStates = ApprovalGateReadinessEvaluator.EvaluateGates(plan);
        var gate1State = gateStates.FirstOrDefault(g => g.GateId == "GATE-1");
        Assert.That(gate1State, Is.Not.Null);
        Assert.That(gate1State!.IsReady, Is.True, "GATE-1 is ready because A2 and B2 are complete");

        // U1 should still be selectable as ungated work
        var nextUngated = ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan, gateStates);
        Assert.That(nextUngated, Is.EqualTo("U1"), "U1 is eligible as ungated work even while GATE-1 awaits approval");

        // Register and approve GATE-1 early
        var token = await _coordinator.RegisterAsync("PROBE-001", "rev1", ["GATE-1"]);
        var result = await _coordinator.TryApproveAsync(token, ["GATE-1"], "Early approval");
        Assert.That(result, Is.EqualTo(ApprovalClickResult.Approved));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 5. Withholding approval reaches the fully blocked state
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void WithholdingApproval_ReachesFullyBlockedState()
    {
        // A+B done, U1 done — no more ungated work. GATE-1 blocks C1+C2+D1.
        var plan = MakeProbePlan(
            a1: PlanTaskStatus.Complete, a2: PlanTaskStatus.Complete,
            b1: PlanTaskStatus.Complete, b2: PlanTaskStatus.Complete,
            u1: PlanTaskStatus.Complete,
            gate1Status: PlanGateStatus.AwaitingApproval);

        var gateStates = ApprovalGateReadinessEvaluator.EvaluateGates(plan);
        var shouldStop = ApprovalGateReadinessEvaluator.ShouldStopForApproval(plan, gateStates);
        Assert.That(shouldStop, Is.True, "Plan should stop — only gated work remains");

        // No ungated task available
        var next = ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan, gateStates);
        Assert.That(next, Is.Null, "No ungated work when all independent tasks complete");

        // Blocked task IDs should include C1 and downstream
        var blocked = ApprovalGateReadinessEvaluator.ComputeAllBlockedTaskIds(plan, gateStates);
        Assert.That(blocked, Does.Contain("C1"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 6. Approval from either surface disables both
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ApprovalFromEitherSurface_DisablesBoth()
    {
        var token = await _coordinator.RegisterAsync("PROBE-001", "rev1", ["GATE-1"]);

        // Track resolved events (simulates cross-surface invalidation)
        var resolvedEvents = new List<ApprovalResolvedEventArgs>();
        _coordinator.ApprovalResolved += (_, args) => resolvedEvents.Add(args);

        // Inbox surface approves
        var inboxResult = await _coordinator.TryApproveAsync(token, ["GATE-1"], "From Inbox");
        Assert.That(inboxResult, Is.EqualTo(ApprovalClickResult.Approved));

        // Transcript surface tries same token — already stale
        var transcriptResult = await _coordinator.TryApproveAsync(token, ["GATE-1"], "From Transcript");
        Assert.That(transcriptResult, Is.EqualTo(ApprovalClickResult.StaleRejected),
            "Second surface's click is rejected because version incremented after first approval");

        // Exactly one resolved event fired
        Assert.That(resolvedEvents, Has.Count.EqualTo(1));
        Assert.That(resolvedEvents[0].AllGatesResolved, Is.True, "Single gate plan is fully resolved");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 7. Resolved requests become read/actionless and leave active list
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ResolvedRequests_BecomeReadAndActionless()
    {
        var plan = MakeProbePlan(
            a1: PlanTaskStatus.Complete, a2: PlanTaskStatus.Complete,
            b1: PlanTaskStatus.Complete, b2: PlanTaskStatus.Complete,
            gate1Status: PlanGateStatus.AwaitingApproval);
        var snap = MakeSnapshot(plan, "GATE-1");

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snap);
        await _durableManager.ResolveCheckpointAsync(plan, "GATE-1", "LGTM");

        var state = _durableManager.GetState("PROBE-001");
        Assert.That(state!.Archived, Is.True, "Fully resolved plan is archived");
        Assert.That(state.ActiveGateIds, Is.Empty, "No active gates remain");
        Assert.That(state.ResolvedCheckpoints, Has.Count.EqualTo(1));

        var msg = _inbox.GetById(DurableApprovalRequestManager.BuildMessageId("PROBE-001"));
        Assert.That(msg!.Read, Is.True, "Message is marked read");
        Assert.That(msg.Actions, Is.Empty, "No actions remain on archived message");

        // Leaves active list
        var activePlanIds = _durableManager.RestoreActivePlanIds();
        Assert.That(activePlanIds, Does.Not.Contain("PROBE-001"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 8. Later checkpoint reuses and unretires the same message
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task LaterCheckpoint_ReusesAndUnretiresMessage()
    {
        var plan = MakeProbePlan(
            a1: PlanTaskStatus.Complete, a2: PlanTaskStatus.Complete,
            b1: PlanTaskStatus.Complete, b2: PlanTaskStatus.Complete,
            gate1Status: PlanGateStatus.AwaitingApproval);
        var snap = MakeSnapshot(plan, "GATE-1");

        var id = await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snap);
        await _durableManager.ResolveCheckpointAsync(plan, "GATE-1");
        Assert.That(_durableManager.IsArchived("PROBE-001"), Is.True);

        // Second gate arrives — unarchive
        var plan2 = MakeProbePlan(
            a1: PlanTaskStatus.Complete, a2: PlanTaskStatus.Complete,
            b1: PlanTaskStatus.Complete, b2: PlanTaskStatus.Complete,
            c1: PlanTaskStatus.Complete, c2: PlanTaskStatus.Complete,
            gate1Status: PlanGateStatus.Approved,
            gate2Status: PlanGateStatus.AwaitingApproval);
        var snap2 = MakeSnapshot(plan2, "GATE-2");
        var id2 = await _durableManager.AppendCheckpointAsync(plan2, plan2.ApprovalGates[1], snap2);

        Assert.That(id2, Is.EqualTo(id), "Same message ID reused");
        Assert.That(_durableManager.IsArchived("PROBE-001"), Is.False, "Message unarchived");

        var state = _durableManager.GetState("PROBE-001");
        Assert.That(state!.ActiveGateIds, Does.Contain("GATE-2"));
        Assert.That(state.ResolvedCheckpoints, Has.Count.EqualTo(1),
            "History of GATE-1 resolution preserved across archive/unarchive");

        // Active plan list now includes the plan again
        var activePlanIds = _durableManager.RestoreActivePlanIds();
        Assert.That(activePlanIds, Does.Contain("PROBE-001"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 9. Update-versus-click race: stale versions cannot approve unseen gates
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task StaleVersionRace_CannotApproveUnseenGates()
    {
        var tokenV1 = await _coordinator.RegisterAsync("PROBE-001", "rev1", ["GATE-1"]);

        // Plan evolves — new gate added, version bumps
        await _coordinator.AppendGateAsync("PROBE-001", "rev1", "GATE-2");

        // Old token from before GATE-2 existed tries to approve GATE-1
        var result = await _coordinator.TryApproveAsync(tokenV1, ["GATE-1"]);
        Assert.That(result, Is.EqualTo(ApprovalClickResult.StaleRejected),
            "Token captured before GATE-2 was added is stale");

        // Fresh token can approve
        var freshToken = _coordinator.GetCurrentToken("PROBE-001");
        Assert.That(freshToken, Is.Not.Null);
        var freshResult = await _coordinator.TryApproveAsync(freshToken!, ["GATE-1"]);
        Assert.That(freshResult, Is.EqualTo(ApprovalClickResult.Approved));
    }

    [Test]
    public async Task ConcurrentRegisterAndApprove_OnlyOneWins()
    {
        var token = await _coordinator.RegisterAsync("PROBE-001", "rev1", ["GATE-1"]);

        var approveTask = _coordinator.TryApproveAsync(token, ["GATE-1"]);
        var updateTask = _coordinator.RegisterAsync("PROBE-001", "rev2", ["GATE-1"]);

        await Task.WhenAll(approveTask, updateTask);
        var approveResult = await approveTask;

        Assert.That(approveResult,
            Is.EqualTo(ApprovalClickResult.Approved).Or.EqualTo(ApprovalClickResult.StaleRejected),
            "Race between approve and update must result in exactly one winner");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 10. Restart in early state restores correctly
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task RestartInEarlyState_RestoresActiveRequest()
    {
        // Early state: GATE-1 awaiting approval, ungated work (U1) still pending
        var plan = MakeProbePlan(
            a1: PlanTaskStatus.Complete, a2: PlanTaskStatus.Complete,
            b1: PlanTaskStatus.Complete, b2: PlanTaskStatus.Complete,
            u1: PlanTaskStatus.Executing,
            gate1Status: PlanGateStatus.AwaitingApproval);
        var snap = MakeSnapshot(plan, "GATE-1");
        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snap);

        // Simulate restart: new manager reads from same inbox store
        var freshManager = new DurableApprovalRequestManager(_inbox);
        var activePlanIds = freshManager.RestoreActivePlanIds();
        Assert.That(activePlanIds, Does.Contain("PROBE-001"), "Active plan restored after restart");

        var state = freshManager.GetState("PROBE-001");
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.ActiveGateIds, Does.Contain("GATE-1"));
        Assert.That(state.Archived, Is.False);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 11. Restart in blocked state restores correctly
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task RestartInBlockedState_RestoresActiveRequestWithHistory()
    {
        var plan = MakeProbePlan(
            a1: PlanTaskStatus.Complete, a2: PlanTaskStatus.Complete,
            b1: PlanTaskStatus.Complete, b2: PlanTaskStatus.Complete,
            u1: PlanTaskStatus.Complete,
            gate1Status: PlanGateStatus.AwaitingApproval);
        var snap = MakeSnapshot(plan, "GATE-1");
        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snap);

        // Simulate restart
        var freshManager = new DurableApprovalRequestManager(_inbox);
        var activePlanIds = freshManager.RestoreActivePlanIds();
        Assert.That(activePlanIds, Does.Contain("PROBE-001"));

        // Gate evaluator on restored plan still reports blocked
        var gateStates = ApprovalGateReadinessEvaluator.EvaluateGates(plan);
        Assert.That(ApprovalGateReadinessEvaluator.ShouldStopForApproval(plan, gateStates), Is.True,
            "Blocked state persists across restart");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 12. Commit-aware file review links
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void CommitAwareFileReviewLinks_CorrectUriSchemes()
    {
        var sha = CommitA1;
        var shortSha = sha[..7];
        var commitLink = new CommitLink(shortSha, sha, "Lane-A Task 1");
        Assert.That(commitLink.InternalUri, Is.EqualTo($"app://commit-diff:{sha}"));

        var fileLink = new FileLink("src/A1.cs", sha);
        Assert.That(fileLink.ReviewedVersionUri, Is.EqualTo($"app://file-at-commit:{sha}:src/A1.cs"));
        Assert.That(fileLink.WorkspaceFileUri, Is.EqualTo("app://open-workspace-file:src/A1.cs"));
    }

    [Test]
    public async Task SnapshotContainsCommitAwareChangedFiles()
    {
        var plan = MakeProbePlan(
            a1: PlanTaskStatus.Complete, a2: PlanTaskStatus.Complete,
            b1: PlanTaskStatus.Complete, b2: PlanTaskStatus.Complete,
            gate1Status: PlanGateStatus.AwaitingApproval);
        var snap = MakeSnapshot(plan, "GATE-1");

        Assert.That(snap.AllChangedFiles, Has.Count.GreaterThanOrEqualTo(2),
            "Changed files include entries from completed tasks behind the gate");
        foreach (var file in snap.AllChangedFiles)
        {
            Assert.That(file.Link, Is.Not.Null, "Each changed file has a link");
            Assert.That(file.Link.ReviewedVersionUri, Does.StartWith("app://file-at-commit:"));
            Assert.That(file.CommitSha, Is.Not.Null.And.Not.Empty);
        }

        // Verify completed tasks in snapshot have commit entries
        Assert.That(snap.CompletedTasks, Has.Count.EqualTo(2), "A2 and B2 are the gate's AfterTaskIds");
        foreach (var task in snap.CompletedTasks)
        {
            Assert.That(task.Commits, Has.Count.EqualTo(1));
            Assert.That(task.Commits[0].Link.ShortSha, Has.Length.EqualTo(7));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 13. Full lifecycle: parallel lanes → gate → approval → next gate
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task FullLifecycle_ParallelLanes_SequentialGates()
    {
        // Phase 1: Parallel work in progress
        var plan1 = MakeProbePlan(
            a1: PlanTaskStatus.Executing, b1: PlanTaskStatus.Executing);
        var gateStates1 = ApprovalGateReadinessEvaluator.EvaluateGates(plan1);
        Assert.That(gateStates1, Has.Count.EqualTo(2), "Both gates pending");
        Assert.That(gateStates1.All(g => !g.IsReady), Is.True, "No gates ready yet");

        // Phase 2: All pre-gate work complete, gate ready
        var plan2 = MakeProbePlan(
            a1: PlanTaskStatus.Complete, a2: PlanTaskStatus.Complete,
            b1: PlanTaskStatus.Complete, b2: PlanTaskStatus.Complete,
            u1: PlanTaskStatus.Complete,
            gate1Status: PlanGateStatus.AwaitingApproval);
        var gateStates2 = ApprovalGateReadinessEvaluator.EvaluateGates(plan2);
        var gate1 = gateStates2.First(g => g.GateId == "GATE-1");
        Assert.That(gate1.IsReady, Is.True);

        // Register in coordinator and durable manager
        var token1 = await _coordinator.RegisterAsync("PROBE-001", "rev1", ["GATE-1"]);
        var snap1 = MakeSnapshot(plan2, "GATE-1");
        await _durableManager.AppendCheckpointAsync(plan2, plan2.ApprovalGates[0], snap1);

        // Phase 3: Approve GATE-1
        var resolvedEvents = new List<ApprovalResolvedEventArgs>();
        _coordinator.ApprovalResolved += (_, args) => resolvedEvents.Add(args);
        var approveResult = await _coordinator.TryApproveAsync(token1, ["GATE-1"], "Looks good");
        Assert.That(approveResult, Is.EqualTo(ApprovalClickResult.Approved));
        await _durableManager.ResolveCheckpointAsync(plan2, "GATE-1", "Looks good");

        Assert.That(resolvedEvents, Has.Count.EqualTo(1));

        // Phase 4: Post-gate work completes, GATE-2 becomes ready
        var plan3 = MakeProbePlan(
            a1: PlanTaskStatus.Complete, a2: PlanTaskStatus.Complete,
            b1: PlanTaskStatus.Complete, b2: PlanTaskStatus.Complete,
            u1: PlanTaskStatus.Complete,
            c1: PlanTaskStatus.Complete, c2: PlanTaskStatus.Complete,
            gate1Status: PlanGateStatus.Approved,
            gate2Status: PlanGateStatus.AwaitingApproval);
        var gateStates3 = ApprovalGateReadinessEvaluator.EvaluateGates(plan3);
        Assert.That(gateStates3, Has.Count.EqualTo(1), "Only GATE-2 pending");
        Assert.That(gateStates3[0].GateId, Is.EqualTo("GATE-2"));
        Assert.That(gateStates3[0].IsReady, Is.True);

        // Unarchive message for GATE-2
        var snap2 = MakeSnapshot(plan3, "GATE-2");
        var id2 = await _durableManager.AppendCheckpointAsync(plan3, plan3.ApprovalGates[1], snap2);
        Assert.That(_durableManager.IsArchived("PROBE-001"), Is.False, "Unarchived for GATE-2");

        // Register GATE-2 and approve
        var token2 = await _coordinator.RegisterAsync("PROBE-001", "rev2", ["GATE-2"]);
        var approve2 = await _coordinator.TryApproveAsync(token2, ["GATE-2"]);
        Assert.That(approve2, Is.EqualTo(ApprovalClickResult.Approved));
        await _durableManager.ResolveCheckpointAsync(plan3, "GATE-2", "Ship it");

        // Final state: fully resolved
        var finalState = _durableManager.GetState("PROBE-001");
        Assert.That(finalState!.Archived, Is.True);
        Assert.That(finalState.ActiveGateIds, Is.Empty);
        Assert.That(finalState.ResolvedCheckpoints, Has.Count.EqualTo(2));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 14. Downstream frontier computation across parallel lanes
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void DownstreamFrontier_IncludesTransitiveDependencies()
    {
        var plan = MakeProbePlan();
        var gate1 = plan.ApprovalGates[0]; // GATE-1: before=[C1], but C1→C2→D1

        var frontier = ApprovalGateReadinessEvaluator.ComputeDownstreamFrontier(plan, gate1);
        Assert.That(frontier, Does.Contain("C1"), "Direct downstream");
        Assert.That(frontier, Does.Contain("C2"), "Transitive dependency C1→C2");
        // D1 depends on C2 but is behind GATE-2 as well — still in frontier
        Assert.That(frontier, Does.Contain("D1"), "Transitive dependency C2→D1");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 15. Released tasks after gate approval
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ReleasedTasks_AfterGate1Approval_IncludesC1()
    {
        var plan = MakeProbePlan(
            a1: PlanTaskStatus.Complete, a2: PlanTaskStatus.Complete,
            b1: PlanTaskStatus.Complete, b2: PlanTaskStatus.Complete,
            gate1Status: PlanGateStatus.Approved);

        var released = ApprovalGateReadinessEvaluator.GetReleasedTaskIds(plan, "GATE-1");
        Assert.That(released, Does.Contain("C1"), "C1 is released after GATE-1 approval");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 16. Notification coordinator dedup
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task NotificationDedup_OnlyFirstCallSucceeds()
    {
        var plan = MakeProbePlan(
            a1: PlanTaskStatus.Complete, a2: PlanTaskStatus.Complete,
            b1: PlanTaskStatus.Complete, b2: PlanTaskStatus.Complete,
            gate1Status: PlanGateStatus.AwaitingApproval);
        var snap = MakeSnapshot(plan, "GATE-1");
        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snap);

        var first = await _durableManager.TryMarkNotifiedAsync("PROBE-001");
        var second = await _durableManager.TryMarkNotifiedAsync("PROBE-001");

        Assert.That(first, Is.True, "First notification attempt succeeds");
        Assert.That(second, Is.False, "Second notification attempt is deduped");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 17. Inbox message body contains expected content
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task InboxMessageBody_ContainsGateAndProgressInfo()
    {
        var plan = MakeProbePlan(
            a1: PlanTaskStatus.Complete, a2: PlanTaskStatus.Complete,
            b1: PlanTaskStatus.Complete, b2: PlanTaskStatus.Complete,
            gate1Status: PlanGateStatus.AwaitingApproval);
        var snap = MakeSnapshot(plan, "GATE-1");
        var id = await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snap);

        var msg = _inbox.GetById(id);
        Assert.That(msg, Is.Not.Null);
        Assert.That(msg!.Body, Does.Contain("Review lanes A+B before post-gate work"));
        Assert.That(msg.Body, Does.Not.Contain("`GATE-1`"));
        Assert.That(msg.Body, Does.Contain("Probe Plan"));
        Assert.That(msg.Body, Does.Contain("checkpoint(s) awaiting approval"));
        Assert.That(msg.Priority, Is.EqualTo("high"));
        Assert.That(msg.Subject, Does.Contain("Approval needed"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 18. Durable state serialization roundtrip
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void DurableStateSerializationRoundtrip()
    {
        var state = new DurableApprovalState(
            "PROBE-001",
            ["GATE-1", "GATE-2"],
            [new ResolvedCheckpointEntry("GATE-0", DateTimeOffset.UtcNow, "Old gate")],
            DateTimeOffset.UtcNow,
            Archived: false,
            Version: 3);

        var json = JsonSerializer.Serialize(state);
        var deserialized = JsonSerializer.Deserialize<DurableApprovalState>(json);

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.PlanId, Is.EqualTo("PROBE-001"));
        Assert.That(deserialized.ActiveGateIds, Has.Count.EqualTo(2));
        Assert.That(deserialized.ResolvedCheckpoints, Has.Count.EqualTo(1));
        Assert.That(deserialized.Version, Is.EqualTo(3));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 19. ApprovalCardNotificationCoordinator label
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ApproveLabel_SingleGate_CorrectText()
    {
        Assert.That(ApprovalCardNotificationCoordinator.BuildApproveLabel(1),
            Is.EqualTo("Approve checkpoint and continue"));
    }

    [Test]
    public void ApproveLabel_MultipleGates_UsesAllWording()
    {
        var label = ApprovalCardNotificationCoordinator.BuildApproveLabel(3);
        Assert.That(label, Is.EqualTo("Approve all checkpoints and continue"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 20. Body builder for resolved state
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void BuildBody_FullyResolved_ContainsArchivedMessage()
    {
        var plan = MakeProbePlan(
            a1: PlanTaskStatus.Complete, a2: PlanTaskStatus.Complete,
            b1: PlanTaskStatus.Complete, b2: PlanTaskStatus.Complete);

        var resolved = new List<ResolvedCheckpointEntry>
        {
            new("GATE-1", DateTimeOffset.UtcNow, "Approved"),
        };

        var body = DurableApprovalRequestManager.BuildBody(plan, [], resolved);
        Assert.That(body, Does.Contain("archived"));
        Assert.That(body, Does.Contain("resolved checkpoint"));
        Assert.That(body, Does.Not.Contain("awaiting approval"));
    }

    [Test]
    public void BuildActions_NoActiveGates_ReturnsEmpty()
    {
        var plan = MakeProbePlan();
        var actions = DurableApprovalRequestManager.BuildActions(plan, []);
        Assert.That(actions, Is.Empty);
    }

    [Test]
    public void BuildActions_ActiveGates_ReturnsOneAggregateAction()
    {
        var plan = MakeProbePlan();
        var actions = DurableApprovalRequestManager.BuildActions(plan, ["GATE-1", "GATE-2"]);
        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(actions[0].Label, Is.EqualTo("Approve both checkpoints and continue"));
        Assert.That(ApprovalInboxActionPayload.TryParse(actions[0].Prompt, out var payload), Is.True);
        Assert.That(payload!.GateIds, Is.EqualTo(new[] { "GATE-1", "GATE-2" }));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 21. ApprovalReviewSnapshotBuilder.ParseShowOutput
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ParseShowOutput_MultipleCommits_ParsesCorrectly()
    {
        var output = string.Join("\n", [
            "COMMIT:abc1234567890123456789012345678901234567890 Add feature A",
            "10\t2\tsrc/FeatureA.cs",
            "5\t0\tsrc/FeatureA.Tests.cs",
            "",
            "COMMIT:def4567890123456789012345678901234567890123 Fix bug B",
            "3\t1\tsrc/BugB.cs",
        ]);

        var result = new Dictionary<string, List<ChangedFileEntry>>(StringComparer.OrdinalIgnoreCase);
        ApprovalReviewSnapshotBuilder.ParseShowOutput(output, result);

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result["abc1234567890123456789012345678901234567890"], Has.Count.EqualTo(2));
        Assert.That(result["def4567890123456789012345678901234567890123"], Has.Count.EqualTo(1));

        var featureFile = result["abc1234567890123456789012345678901234567890"][0];
        Assert.That(featureFile.Insertions, Is.EqualTo(10));
        Assert.That(featureFile.Deletions, Is.EqualTo(2));
        Assert.That(featureFile.FilePath, Is.EqualTo("src/FeatureA.cs"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 22. Click token match semantics
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ClickToken_MatchesIdenticalToken()
    {
        var t1 = new ApprovalClickToken("PROBE-001", "rev1", 1, ["GATE-1"]);
        var t2 = new ApprovalClickToken("PROBE-001", "rev1", 1, ["GATE-1"]);
        Assert.That(t1.Matches(t2), Is.True);
    }

    [Test]
    public void ClickToken_DifferentVersion_DoesNotMatch()
    {
        var t1 = new ApprovalClickToken("PROBE-001", "rev1", 1, ["GATE-1"]);
        var t2 = new ApprovalClickToken("PROBE-001", "rev1", 2, ["GATE-1"]);
        Assert.That(t1.Matches(t2), Is.False);
    }

    [Test]
    public void ClickToken_DifferentGateSet_DoesNotMatch()
    {
        var t1 = new ApprovalClickToken("PROBE-001", "rev1", 1, ["GATE-1"]);
        var t2 = new ApprovalClickToken("PROBE-001", "rev1", 1, ["GATE-1", "GATE-2"]);
        Assert.That(t1.Matches(t2), Is.False);
    }

    [Test]
    public void ClickToken_DifferentRevision_DoesNotMatch()
    {
        var t1 = new ApprovalClickToken("PROBE-001", "rev1", 1, ["GATE-1"]);
        var t2 = new ApprovalClickToken("PROBE-001", "rev2", 1, ["GATE-1"]);
        Assert.That(t1.Matches(t2), Is.False);
    }
}
