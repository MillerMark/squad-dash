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
/// Regression matrix: additional edge cases and boundary conditions not covered
/// by <see cref="ApprovalIntegrationTests"/> or <see cref="ApprovalIntegrationMatrixTests"/>.
/// Focuses on serialization round-trips, coordinator state invariants,
/// concurrency across plan boundaries, deep dependency chains, and parser robustness.
/// </summary>
[TestFixture]
internal sealed class ApprovalRegressionMatrixTests
{
    private string _tempDir = null!;
    private InboxStore _inbox = null!;
    private ApprovalActionCoordinator _coordinator = null!;
    private DurableApprovalRequestManager _durableManager = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"squad-regr-{Guid.NewGuid():N}");
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
            lifecycleStatus, "Regression Test Plan", "main", "Summary",
            tasks, gates,
            new PlanProgress(completed, tasks.Count),
            new PlanTimestamps(DateTimeOffset.UtcNow));
    }

    private static ApprovalReviewSnapshot MakeSnapshot(
        string planId = "PLAN-001",
        string gateId = "GATE-A",
        int completedTaskCount = 2,
        int totalTaskCount = 5) =>
        new(planId, "Regression Test Plan", completedTaskCount, totalTaskCount,
            PlanLifecycleStatus.Executing,
            gateId, "Review T1+T2 before T3", ["T1", "T2"], ["T3"],
            [], [], [], [], DateTimeOffset.UtcNow);

    // ═══════════════════════════════════════════════════════════════════════
    // DurableApprovalState serialization round-trip
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void DurableApprovalState_SerializationRoundTrip_PreservesAllFields()
    {
        var resolved = new ResolvedCheckpointEntry("GATE-A", DateTimeOffset.UtcNow, "LGTM");
        var state = new DurableApprovalState(
            "PLAN-001",
            ["GATE-B", "GATE-C"],
            [resolved],
            DateTimeOffset.UtcNow,
            Archived: false,
            Version: 7);

        var json = JsonSerializer.Serialize(state);
        var deserialized = JsonSerializer.Deserialize<DurableApprovalState>(json)!;

        Assert.That(deserialized.PlanId, Is.EqualTo("PLAN-001"));
        Assert.That(deserialized.ActiveGateIds, Is.EqualTo(new[] { "GATE-B", "GATE-C" }));
        Assert.That(deserialized.ResolvedCheckpoints, Has.Count.EqualTo(1));
        Assert.That(deserialized.ResolvedCheckpoints[0].GateId, Is.EqualTo("GATE-A"));
        Assert.That(deserialized.ResolvedCheckpoints[0].ResolutionNote, Is.EqualTo("LGTM"));
        Assert.That(deserialized.Archived, Is.False);
        Assert.That(deserialized.Version, Is.EqualTo(7));
        Assert.That(deserialized.LastNotifiedAt, Is.Not.Null);
    }

    [Test]
    public void DurableApprovalState_NullOptionalFields_OmittedInJson()
    {
        var state = new DurableApprovalState("P1", [], []);
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });

        Assert.That(json, Does.Not.Contain("lastNotifiedAt"),
            "Null LastNotifiedAt must be omitted when using WhenWritingNull");
    }

    [Test]
    public void ResolvedCheckpointEntry_NullResolutionNote_OmittedInJson()
    {
        var entry = new ResolvedCheckpointEntry("GATE-X", DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });

        Assert.That(json, Does.Not.Contain("resolutionNote"),
            "Null ResolutionNote must be omitted");
        Assert.That(json, Does.Contain("gateId"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ApprovalPlanState.BuildToken creates proper snapshot
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ApprovalPlanState_BuildToken_SnapshotsGateIds()
    {
        var state = new ApprovalPlanState("rev1", ["GATE-A", "GATE-B"]);
        var token = state.BuildToken("PLAN-001");

        // Mutate original state's gate list
        state.ActiveGateIds.Add("GATE-C");

        Assert.That(token.GateIds, Has.Count.EqualTo(2),
            "Token must snapshot gate IDs — mutations to state must not affect the token");
        Assert.That(token.GateIds, Does.Not.Contain("GATE-C"));
    }

    [Test]
    public void ApprovalPlanState_InitialVersion_IsOne()
    {
        var state = new ApprovalPlanState("rev1", ["GATE-A"]);
        Assert.That(state.RequestVersion, Is.EqualTo(1));
        Assert.That(state.IsFullyResolved, Is.False);
    }

    [Test]
    public void ApprovalPlanState_EmptyGates_IsFullyResolved()
    {
        var state = new ApprovalPlanState("rev1", Array.Empty<string>());
        Assert.That(state.IsFullyResolved, Is.True);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Coordinator: re-registration clears resolved gate history
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Register_AfterApproval_ResolvedGatesPreserved()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);
        await _coordinator.TryApproveAsync(token, ["GATE-A"]);

        // Re-register with GATE-A in active list — but it's already resolved in state
        var newToken = await _coordinator.RegisterAsync("PLAN-001", "rev2", ["GATE-A"]);
        var result = await _coordinator.TryApproveAsync(newToken, ["GATE-A"]);

        // RegisterAsync preserves ResolvedGateIds — GATE-A is already resolved
        Assert.That(result, Is.EqualTo(ApprovalClickResult.AlreadyResolved),
            "Re-registering with same gate after approval: resolved state is preserved");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Concurrent register + approve across DIFFERENT plans don't interfere
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ConcurrentCrossPlan_RegisterAndApprove_Independent()
    {
        var planIds = Enumerable.Range(1, 10).Select(i => $"PLAN-{i:D3}").ToList();

        // Register all plans concurrently
        var registerTasks = planIds.Select(id =>
            _coordinator.RegisterAsync(id, "rev1", ["GATE-A"]));
        var tokens = await Task.WhenAll(registerTasks);

        // Approve all concurrently
        var approveTasks = tokens.Select(t =>
            _coordinator.TryApproveAsync(t, ["GATE-A"]));
        var results = await Task.WhenAll(approveTasks);

        Assert.That(results, Has.All.EqualTo(ApprovalClickResult.Approved),
            "All independent plans must approve successfully in parallel");
        foreach (var id in planIds)
            Assert.That(_coordinator.HasActiveGates(id), Is.False);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Deep transitive dependency chain in downstream frontier
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void DownstreamFrontier_DeepChain_AllTransitivelyBlocked()
    {
        // T1 → [GATE] → T2 → T3 → T4 → T5 → T6 (depth-5 chain)
        var tasks = Enumerable.Range(1, 6).Select(i =>
            new PlanTask($"T{i}", $"Task {i}", "desc",
                i > 1 ? [$"T{i - 1}"] : [],
                "high", i == 1 ? PlanTaskStatus.Complete : PlanTaskStatus.Pending))
            .ToArray();

        var gate = new PlanApprovalGate("GATE-A", "Review", ["T1"], ["T2"], PlanGateStatus.Pending);
        var plan = new Plan("P-DEEP", "rev1", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.Executing, "Deep chain", "main", "",
            tasks, [gate], new PlanProgress(1, 6), new PlanTimestamps(DateTimeOffset.UtcNow));

        var frontier = ApprovalGateReadinessEvaluator.ComputeDownstreamFrontier(plan, gate);

        Assert.That(frontier, Has.Count.EqualTo(5), "All T2–T6 must be blocked");
        for (int i = 2; i <= 6; i++)
            Assert.That(frontier, Does.Contain($"T{i}"),
                $"T{i} must be transitively blocked");
        Assert.That(frontier, Does.Not.Contain("T1"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DownstreamFrontier with isolated (no-dependency) tasks
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void DownstreamFrontier_IsolatedTask_NotBlocked()
    {
        var tasks = new[]
        {
            new PlanTask("T1", "Task 1", "desc", [], "high", PlanTaskStatus.Complete),
            new PlanTask("T2", "Task 2", "desc", ["T1"], "high", PlanTaskStatus.Pending),
            new PlanTask("T3", "Task 3", "desc", [], "high", PlanTaskStatus.Pending), // isolated
        };
        var gate = new PlanApprovalGate("GATE-A", "Review", ["T1"], ["T2"], PlanGateStatus.Pending);
        var plan = new Plan("P-ISO", "rev1", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.Executing, "Isolated", "main", "",
            tasks, [gate], new PlanProgress(1, 3), new PlanTimestamps(DateTimeOffset.UtcNow));

        var frontier = ApprovalGateReadinessEvaluator.ComputeDownstreamFrontier(plan, gate);

        Assert.That(frontier, Does.Contain("T2"));
        Assert.That(frontier, Does.Not.Contain("T3"),
            "T3 is independent — must not appear in downstream frontier");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // EvaluateGates: AwaitingApproval with incomplete prerequisites
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void EvaluateGates_AwaitingApproval_IncompletePrereqs_NotReady()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Pending,
            gateAStatus: PlanGateStatus.AwaitingApproval);

        var gateStates = ApprovalGateReadinessEvaluator.EvaluateGates(plan);

        Assert.That(gateStates, Has.Count.EqualTo(1));
        Assert.That(gateStates[0].IsReady, Is.False,
            "Gate with AwaitingApproval status but incomplete prereqs is not ready");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // EvaluateGates: superseded prereqs count as terminal
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void EvaluateGates_SupersededPrereqs_CountAsTerminal()
    {
        var tasks = new[]
        {
            new PlanTask("T1", "Task 1", "desc", [], "high", PlanTaskStatus.Superseded),
            new PlanTask("T2", "Task 2", "desc", [], "high", PlanTaskStatus.Complete),
            new PlanTask("T3", "Task 3", "desc", ["T1", "T2"], "high", PlanTaskStatus.Pending),
        };
        var gate = new PlanApprovalGate("GATE-A", "Review", ["T1", "T2"], ["T3"], PlanGateStatus.Pending);
        var plan = new Plan("P-SUP", "rev1", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.Executing, "Test", "main", "",
            tasks, [gate], new PlanProgress(2, 3), new PlanTimestamps(DateTimeOffset.UtcNow));

        var gateStates = ApprovalGateReadinessEvaluator.EvaluateGates(plan);

        Assert.That(gateStates, Has.Count.EqualTo(1));
        Assert.That(gateStates[0].IsReady, Is.True,
            "Superseded tasks count as terminal — gate should be ready");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetReleasedTaskIds with unknown gate ID
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetReleasedTaskIds_UnknownGate_ReturnsEmpty()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.Approved);

        var released = ApprovalGateReadinessEvaluator.GetReleasedTaskIds(plan, "GATE-NONEXISTENT");
        Assert.That(released, Is.Empty,
            "Unknown gate ID must return empty released list");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetTerminalTaskIds with mixed statuses
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetTerminalTaskIds_MixedStatuses_OnlyCompleteAndSuperseded()
    {
        var tasks = new[]
        {
            new PlanTask("T1", "t", "d", [], "h", PlanTaskStatus.Complete),
            new PlanTask("T2", "t", "d", [], "h", PlanTaskStatus.Superseded),
            new PlanTask("T3", "t", "d", [], "h", PlanTaskStatus.Pending),
            new PlanTask("T4", "t", "d", [], "h", PlanTaskStatus.Executing),
            new PlanTask("T5", "t", "d", [], "h", PlanTaskStatus.Failed),
        };
        var plan = new Plan("P-MIX", "rev1", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.Executing, "Mixed", "main", "",
            tasks, [], new PlanProgress(2, 5), new PlanTimestamps(DateTimeOffset.UtcNow));

        var terminal = ApprovalGateReadinessEvaluator.GetTerminalTaskIds(plan);

        Assert.That(terminal, Does.Contain("T1"));
        Assert.That(terminal, Does.Contain("T2"));
        Assert.That(terminal, Does.Not.Contain("T3"));
        Assert.That(terminal, Does.Not.Contain("T4"));
        Assert.That(terminal, Does.Not.Contain("T5"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ParseShowOutput robustness: malformed lines, blank lines, binary files
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ParseShowOutput_BlankLines_Ignored()
    {
        var output = """
            COMMIT:abc1234567890 Feature

            10	0	src/Feature.cs

            """;

        var result = new Dictionary<string, List<ChangedFileEntry>>(StringComparer.OrdinalIgnoreCase);
        ApprovalReviewSnapshotBuilder.ParseShowOutput(output, result);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result["abc1234567890"], Has.Count.EqualTo(1));
    }

    [Test]
    public void ParseShowOutput_BinaryFile_DashInsertionsAndDeletions()
    {
        var output = """
            COMMIT:abc1234567890 Add image
            -	-	images/logo.png
            """;

        var result = new Dictionary<string, List<ChangedFileEntry>>(StringComparer.OrdinalIgnoreCase);
        ApprovalReviewSnapshotBuilder.ParseShowOutput(output, result);

        Assert.That(result["abc1234567890"], Has.Count.EqualTo(1));
        var file = result["abc1234567890"][0];
        Assert.That(file.FilePath, Is.EqualTo("images/logo.png"));
        Assert.That(file.Insertions, Is.EqualTo(0), "Dash parsed as 0 for binary file");
        Assert.That(file.Deletions, Is.EqualTo(0), "Dash parsed as 0 for binary file");
    }

    [Test]
    public void ParseShowOutput_NoCommitHeader_NoFilesParsed()
    {
        var output = """
            10	0	src/Feature.cs
            5	3	src/Helper.cs
            """;

        var result = new Dictionary<string, List<ChangedFileEntry>>(StringComparer.OrdinalIgnoreCase);
        ApprovalReviewSnapshotBuilder.ParseShowOutput(output, result);

        Assert.That(result, Is.Empty,
            "Lines without a preceding COMMIT: header must be ignored");
    }

    [Test]
    public void ParseShowOutput_ConsecutiveCommitHeaders_FlushesCorrectly()
    {
        var output = """
            COMMIT:sha1111111111 First commit
            COMMIT:sha2222222222 Second commit
            5	3	src/File.cs
            """;

        var result = new Dictionary<string, List<ChangedFileEntry>>(StringComparer.OrdinalIgnoreCase);
        ApprovalReviewSnapshotBuilder.ParseShowOutput(output, result);

        Assert.That(result.ContainsKey("sha1111111111"), Is.True,
            "First commit should be flushed even with no files");
        Assert.That(result["sha1111111111"], Is.Empty);
        Assert.That(result["sha2222222222"], Has.Count.EqualTo(1));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ParseShowOutputWithSubjects: commit with no files
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ParseShowOutputWithSubjects_CommitNoFiles_SubjectPreserved()
    {
        var output = """
            COMMIT:abc1234567890 Docs only update
            """;

        var (files, subjects) = ApprovalReviewSnapshotBuilder.ParseShowOutputWithSubjects(output);

        Assert.That(subjects["abc1234567890"], Is.EqualTo("Docs only update"));
        Assert.That(files.ContainsKey("abc1234567890"), Is.True);
        Assert.That(files["abc1234567890"], Is.Empty);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // InferStatus: both insertions and deletions zero
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ParseShowOutput_ZeroInsertionsZeroDeletions_InfersModified()
    {
        var output = """
            COMMIT:abc1234567890 Empty change
            0	0	src/EmptyDiff.cs
            """;

        var result = new Dictionary<string, List<ChangedFileEntry>>(StringComparer.OrdinalIgnoreCase);
        ApprovalReviewSnapshotBuilder.ParseShowOutput(output, result);

        Assert.That(result["abc1234567890"][0].Status, Is.EqualTo(FileChangeStatus.Modified),
            "Zero insertions and zero deletions (non-rename) should infer Modified");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BuildApproveLabel boundary values
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void BuildApproveLabel_ZeroGates_UsesSingularForm()
    {
        // Boundary: zero active gates shouldn't happen in practice, but verify behavior
        var label = ApprovalCardNotificationCoordinator.BuildApproveLabel(0);
        Assert.That(label, Does.Contain("Approve Checkpoint"),
            "Zero or one gate should use singular form");
    }

    [Test]
    public void BuildApproveLabel_ExactlyOne_UsesSingularForm()
    {
        var label = ApprovalCardNotificationCoordinator.BuildApproveLabel(1);
        Assert.That(label, Does.Contain("Approve Checkpoint"));
        Assert.That(label, Does.Not.Contain("Checkpoints"));
    }

    [Test]
    public void BuildApproveLabel_Two_UsesPluralFormWithCount()
    {
        var label = ApprovalCardNotificationCoordinator.BuildApproveLabel(2);
        Assert.That(label, Does.Contain("2"));
        Assert.That(label, Does.Contain("Checkpoints"));
    }

    [Test]
    public void BuildApproveLabel_LargeNumber_ShowsCount()
    {
        var label = ApprovalCardNotificationCoordinator.BuildApproveLabel(99);
        Assert.That(label, Does.Contain("99"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Concurrent resolve on same gate (idempotent)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ConcurrentResolve_SameGate_IdempotentNoError()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());

        // Race: 5 concurrent resolves of the same gate
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => _durableManager.ResolveCheckpointAsync(plan, "GATE-A", "OK"))
            .ToArray();

        Assert.DoesNotThrowAsync(async () => await Task.WhenAll(tasks),
            "Concurrent resolve of the same gate must not throw");

        var state = _durableManager.GetState("PLAN-001");
        Assert.That(state!.ResolvedCheckpoints, Has.Count.EqualTo(1),
            "Gate must be resolved exactly once regardless of concurrency");
        Assert.That(state.Archived, Is.True);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Coordinator: approve then AppendGate reactivates plan
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task AppendGate_AfterFullResolution_ReactivatesPlan()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);
        await _coordinator.TryApproveAsync(token, ["GATE-A"]);
        Assert.That(_coordinator.HasActiveGates("PLAN-001"), Is.False);

        // Append new gate — plan should be reactivated
        await _coordinator.AppendGateAsync("PLAN-001", "rev1", "GATE-B");
        Assert.That(_coordinator.HasActiveGates("PLAN-001"), Is.True);

        var newToken = _coordinator.GetCurrentToken("PLAN-001")!;
        Assert.That(newToken.GateIds, Does.Contain("GATE-B"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Coordinator: GetActiveGateIds returns empty for unknown plan
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetActiveGateIds_UnknownPlan_ReturnsEmpty()
    {
        var gates = _coordinator.GetActiveGateIds("NONEXISTENT");
        Assert.That(gates, Is.Empty);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ApprovalClickToken.Matches: different PlanId returns false
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ApprovalClickToken_Matches_DifferentPlanId_ReturnsFalse()
    {
        var token1 = new ApprovalClickToken("P1", "rev1", 1, ["GATE-A"]);
        var token2 = new ApprovalClickToken("P2", "rev1", 1, ["GATE-A"]);
        Assert.That(token1.Matches(token2), Is.False);
    }

    [Test]
    public void ApprovalClickToken_Matches_SubsetGates_ReturnsFalse()
    {
        var token1 = new ApprovalClickToken("P1", "rev1", 1, ["GATE-A", "GATE-B"]);
        var token2 = new ApprovalClickToken("P1", "rev1", 1, ["GATE-A"]);
        Assert.That(token1.Matches(token2), Is.False,
            "Different gate count must not match");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ShouldStopForApproval: all tasks complete, no gates
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ShouldStopForApproval_AllTasksComplete_NoGates_ReturnsFalse()
    {
        var tasks = new[]
        {
            new PlanTask("T1", "Task 1", "desc", [], "high", PlanTaskStatus.Complete),
            new PlanTask("T2", "Task 2", "desc", ["T1"], "high", PlanTaskStatus.Complete),
        };
        var plan = new Plan("P-DONE", "rev1", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.Executing, "Done", "main", "",
            tasks, [], new PlanProgress(2, 2), new PlanTimestamps(DateTimeOffset.UtcNow));

        Assert.That(ApprovalGateReadinessEvaluator.ShouldStopForApproval(plan), Is.False);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ShouldStopForApproval: gate not ready yet
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ShouldStopForApproval_GateNotReady_NoUngatedWork_ReturnsFalse()
    {
        // T1 pending, T2 pending — gate's prereqs not met; no ungated work
        var tasks = new[]
        {
            new PlanTask("T1", "Task 1", "desc", [], "high", PlanTaskStatus.Pending),
            new PlanTask("T2", "Task 2", "desc", ["T1"], "high", PlanTaskStatus.Pending),
        };
        var gate = new PlanApprovalGate("GATE-A", "Review", ["T1"], ["T2"], PlanGateStatus.Pending);
        var plan = new Plan("P-NR", "rev1", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.Executing, "Not Ready", "main", "",
            tasks, [gate], new PlanProgress(0, 2), new PlanTimestamps(DateTimeOffset.UtcNow));

        // T1 is pending and ungated — the evaluator should find it
        var shouldStop = ApprovalGateReadinessEvaluator.ShouldStopForApproval(plan);
        Assert.That(shouldStop, Is.False,
            "T1 is ungated and eligible — should not stop");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PlanStoreUpdater: ApplyGateApproved returns same instance for unknown gate
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ApplyGateApproved_UnknownGate_ReturnsSameInstance()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval);

        var updated = PlanStoreUpdater.ApplyGateApproved(plan, "GATE-NONEXISTENT", "OK");

        Assert.That(updated, Is.SameAs(plan),
            "Approving a gate that doesn't exist must return the plan unchanged");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PlanStoreUpdater: ApplyFullStopAtGates with multiple gates
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ApplyFullStopAtGates_MultipleGates_AllTransitioned()
    {
        var gateB = new PlanApprovalGate("GATE-B", "Second", ["T3"], ["T4"], PlanGateStatus.Pending);
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            t3Status: PlanTaskStatus.Complete,
            t5Status: PlanTaskStatus.Complete,
            extraGates: [gateB]);

        var updated = PlanStoreUpdater.ApplyFullStopAtGates(plan, ["GATE-A", "GATE-B"]);

        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval));

        var updatedGateA = updated.ApprovalGates.First(g => g.GateId == "GATE-A");
        var updatedGateB = updated.ApprovalGates.First(g => g.GateId == "GATE-B");
        Assert.That(updatedGateA.Status, Is.EqualTo(PlanGateStatus.AwaitingApproval));
        Assert.That(updatedGateB.Status, Is.EqualTo(PlanGateStatus.AwaitingApproval));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DurableManager: archived plan's inbox message has correct actions count
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ArchivedPlan_InboxMessage_NoActions()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        await _durableManager.ResolveCheckpointAsync(plan, "GATE-A", "Done");

        var msg = _inbox.GetById("approval-gate-PLAN-001");
        Assert.That(msg, Is.Not.Null);
        Assert.That(msg!.Actions, Is.Empty,
            "Archived plan's message must have no action buttons");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DurableManager: GetState returns null for unknown plan
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetState_UnknownPlan_ReturnsNull()
    {
        var state = _durableManager.GetState("NEVER-SEEN");
        Assert.That(state, Is.Null);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DurableManager: IsArchived returns false for unknown plan
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void IsArchived_UnknownPlan_ReturnsFalse()
    {
        Assert.That(_durableManager.IsArchived("NEVER-SEEN"), Is.False);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BuildBody: multiple resolved checkpoints show all notes
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void BuildBody_MultipleResolvedCheckpoints_AllNotesShown()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var resolved = new List<ResolvedCheckpointEntry>
        {
            new("GATE-1", DateTimeOffset.UtcNow.AddHours(-2), "First review OK"),
            new("GATE-2", DateTimeOffset.UtcNow.AddHours(-1), "Second review OK"),
            new("GATE-3", DateTimeOffset.UtcNow, "Third review OK"),
        };

        var body = DurableApprovalRequestManager.BuildBody(plan, ["GATE-4"], resolved);

        Assert.That(body, Does.Contain("First review OK"));
        Assert.That(body, Does.Contain("Second review OK"));
        Assert.That(body, Does.Contain("Third review OK"));
        Assert.That(body, Does.Contain("3 resolved checkpoint(s)"));
        Assert.That(body, Does.Contain("1 checkpoint(s) awaiting approval"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetReadyGateIds: no ready gates returns empty
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetReadyGateIds_NoReadyGates_ReturnsEmpty()
    {
        var gateStates = new List<GateReadinessState>
        {
            new("GATE-A", false, new HashSet<string> { "T2" }),
            new("GATE-B", false, new HashSet<string> { "T3" }),
        };

        var ready = ApprovalGateReadinessEvaluator.GetReadyGateIds(gateStates);
        Assert.That(ready, Is.Empty);
    }

    [Test]
    public void GetReadyGateIds_AllReady_ReturnsAll()
    {
        var gateStates = new List<GateReadinessState>
        {
            new("GATE-A", true, new HashSet<string> { "T2" }),
            new("GATE-B", true, new HashSet<string> { "T3" }),
        };

        var ready = ApprovalGateReadinessEvaluator.GetReadyGateIds(gateStates);
        Assert.That(ready, Has.Count.EqualTo(2));
        Assert.That(ready, Does.Contain("GATE-A"));
        Assert.That(ready, Does.Contain("GATE-B"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CommitLink and FileLink model invariants
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void CommitLink_ShortShaPreserved()
    {
        var link = new CommitLink("1234567", "1234567890abcdef", "Msg");
        Assert.That(link.ShortSha, Is.EqualTo("1234567"));
        Assert.That(link.FullSha, Is.EqualTo("1234567890abcdef"));
        Assert.That(link.Subject, Is.EqualTo("Msg"));
    }

    [Test]
    public void FileLink_SpecialCharactersInPath_PreservedInUri()
    {
        var link = new FileLink("src/My File (2).cs", "deadbeef");
        Assert.That(link.ReviewedVersionUri, Does.Contain("My File (2).cs"));
        Assert.That(link.WorkspaceFileUri, Does.Contain("My File (2).cs"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Coordinator: TryApprove with cancellation token (no deadlock)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task RegisterAsync_CancelledToken_ThrowsOrTaskCanceled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"], cts.Token);
            Assert.Fail("Expected an OperationCanceledException or TaskCanceledException");
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Coordinator: many sequential approvals with version tracking
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task SequentialApprovals_VersionIncreasesMonotonically()
    {
        await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A", "GATE-B", "GATE-C"]);

        var versions = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            var token = _coordinator.GetCurrentToken("PLAN-001")!;
            versions.Add(token.RequestVersion);

            var gate = token.GateIds[0];
            await _coordinator.TryApproveAsync(token, [gate]);
        }

        Assert.That(versions, Is.Ordered,
            "Versions must increase monotonically across sequential approvals");
        Assert.That(versions.Distinct().Count(), Is.EqualTo(3),
            "Each approval must produce a unique version");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // End-to-end: partial resolution → refresh evidence → final resolution
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task EndToEnd_PartialResolve_RefreshEvidence_FinalResolve()
    {
        var gateB = new PlanApprovalGate("GATE-B", "Second", ["T3"], ["T4"],
            PlanGateStatus.AwaitingApproval);
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            extraGates: [gateB]);

        // Append both gates
        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        await _durableManager.AppendCheckpointAsync(plan, gateB, MakeSnapshot(gateId: "GATE-B"));

        // Resolve GATE-A only
        await _durableManager.ResolveCheckpointAsync(plan, "GATE-A", "First pass");
        var state1 = _durableManager.GetState("PLAN-001")!;
        Assert.That(state1.Archived, Is.False);
        Assert.That(state1.ActiveGateIds, Does.Contain("GATE-B"));
        Assert.That(state1.ResolvedCheckpoints, Has.Count.EqualTo(1));

        // Refresh evidence with new progress
        var updatedSnap = MakeSnapshot(completedTaskCount: 4);
        await _durableManager.RefreshEvidenceAsync(plan, updatedSnap);

        // State should be unchanged after refresh
        var state2 = _durableManager.GetState("PLAN-001")!;
        Assert.That(state2.ActiveGateIds, Does.Contain("GATE-B"));
        Assert.That(state2.Archived, Is.False);

        // Resolve GATE-B — full archive
        await _durableManager.ResolveCheckpointAsync(plan, "GATE-B", "Second pass");
        var state3 = _durableManager.GetState("PLAN-001")!;
        Assert.That(state3.Archived, Is.True);
        Assert.That(state3.ResolvedCheckpoints, Has.Count.EqualTo(2));
        Assert.That(state3.ActiveGateIds, Is.Empty);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ComputeAllBlockedTaskIds with empty gate states
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ComputeAllBlockedTaskIds_EmptyGateStates_ReturnsEmpty()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete);
        var blocked = ApprovalGateReadinessEvaluator.ComputeAllBlockedTaskIds(plan,
            new List<GateReadinessState>());

        Assert.That(blocked, Is.Empty);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SelectNextUngatedTask: all tasks in terminal states
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void SelectNextUngatedTask_AllTerminal_ReturnsNull()
    {
        var tasks = new[]
        {
            new PlanTask("T1", "t", "d", [], "h", PlanTaskStatus.Complete),
            new PlanTask("T2", "t", "d", [], "h", PlanTaskStatus.Superseded),
        };
        var plan = new Plan("P-ALL", "rev1", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.Executing, "All done", "main", "",
            tasks, [], new PlanProgress(2, 2), new PlanTimestamps(DateTimeOffset.UtcNow));

        Assert.That(ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan), Is.Null);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ApprovalReviewSnapshot model: record immutability
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ApprovalReviewSnapshot_WithExpression_PreservesOtherFields()
    {
        var original = MakeSnapshot();
        var updated = original with { CompletedTaskCount = 99 };

        Assert.That(updated.CompletedTaskCount, Is.EqualTo(99));
        Assert.That(updated.PlanId, Is.EqualTo(original.PlanId));
        Assert.That(updated.GateId, Is.EqualTo(original.GateId));
        Assert.That(updated.TotalTaskCount, Is.EqualTo(original.TotalTaskCount));
        Assert.That(updated.GateReason, Is.EqualTo(original.GateReason));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Multiple independent restores after interleaved archive cycles
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task RestoreActivePlanIds_AfterMultipleArchiveCycles_OnlyCurrentActiveReturned()
    {
        var plan = MakePlan("PLAN-CYCLE", t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete);

        // Cycle 1: append → resolve → archive
        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot("PLAN-CYCLE"));
        await _durableManager.ResolveCheckpointAsync(plan, "GATE-A");

        // Cycle 2: new gate → unarchive
        var gate2 = new PlanApprovalGate("GATE-B", "Round 2", ["T3"], ["T4"],
            PlanGateStatus.AwaitingApproval);
        var planWithGate2 = MakePlan("PLAN-CYCLE",
            extraGates: [gate2],
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete);
        await _durableManager.AppendCheckpointAsync(planWithGate2, gate2,
            MakeSnapshot("PLAN-CYCLE", "GATE-B"));

        // Cycle 2: resolve → archive again
        await _durableManager.ResolveCheckpointAsync(planWithGate2, "GATE-B");

        // Verify fully archived
        var fresh = new DurableApprovalRequestManager(_inbox);
        var active = fresh.RestoreActivePlanIds();
        Assert.That(active, Does.Not.Contain("PLAN-CYCLE"),
            "Fully archived plan must not appear in restore");
    }
}
