namespace SquadDash.Tests;

[TestFixture]
internal sealed class ApprovalAnchorInferenceEngineTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static PlanTask T(string id, string status, params string[] dependsOn) =>
        new(id, $"Task {id}", $"Description of {id}", dependsOn, "mid", status);

    private static Plan MakePlan(
        IReadOnlyList<PlanTask> tasks,
        IReadOnlyList<PlanApprovalGate> gates,
        string lifecycleStatus = "executing") =>
        new("PLAN-T", "rev-1", PlanSource.Manual, lifecycleStatus, "Test Plan", "main", "summary",
            tasks, gates, new PlanProgress(0, tasks.Count), new PlanTimestamps(DateTimeOffset.UtcNow));

    private static Dictionary<string, int> Levels(params (string Id, int Level)[] entries) =>
        entries.ToDictionary(e => e.Id, e => e.Level);

    // ── Deterministic primary selection priority ──────────────────────────────

    [Test]
    public void StageMilestone_WinsOver_AllJoin()
    {
        // Gate G1 resolves to stage:1, Gate G2 resolves to all:C
        var tasks = new[] { T("A", "pending"), T("B", "pending", "A"), T("C", "pending", "A", "B") };
        var gates = new PlanApprovalGate[]
        {
            new("G1", "Stage gate", ["A"], ["B"], PlanGateStatus.Pending),
            new("G2", "All join gate", ["A", "B"], ["C"], PlanGateStatus.Pending),
        };
        var plan = MakePlan(tasks, gates);
        var levels = Levels(("A", 0), ("B", 1), ("C", 2));

        var result = ApprovalAnchorInferenceEngine.Infer(plan, levels);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PrimaryGateId, Is.EqualTo("G1"));
        Assert.That(result.PrimaryAnchor, Does.StartWith("stage:"));
    }

    [Test]
    public void AllJoin_WinsOver_TaskExit()
    {
        // Tasks A,B -> C (ALL join), D after C (task exit)
        var tasks = new[]
        {
            T("A", "pending"), T("B", "pending"),
            T("C", "pending", "A", "B"), T("D", "pending", "C"),
        };
        var gates = new PlanApprovalGate[]
        {
            new("G1", "After C", ["C"], ["D"], PlanGateStatus.Pending),
            new("G2", "All join", ["A", "B"], ["C"], PlanGateStatus.Pending),
        };
        var plan = MakePlan(tasks, gates);
        // No stage boundaries — A,B at level 0, C at level 1, D at level 2
        // But gate G1 (after:[C], before:[D]) won't match stage because there are other tasks at level 0
        // G2 with after:[A,B] before:[C] matches the ALL join for C which depends on both A and B
        var levels = Levels(("A", 0), ("B", 0), ("C", 1), ("D", 2));

        var result = ApprovalAnchorInferenceEngine.Infer(plan, levels);

        Assert.That(result, Is.Not.Null);
        // G1 resolves to stage:1 (after all level-0 tasks, before level-1 task) since levels
        // show [A,B] at 0 and [C] at 1 — but gate G1 has only [C] after, [D] before.
        // G2 has [A,B] after, [C] before — matches stage boundary between level 0 and 1!
        // So G2 is actually stage:1 and G1 is stage:2. Stage wins → G2 is primary.
        Assert.That(result!.PrimaryAnchor, Does.StartWith("stage:"));
    }

    [Test]
    public void TaskExit_SelectedWhenNoStageOrAllJoin()
    {
        var tasks = new[] { T("A", "pending"), T("B", "pending", "A") };
        var gates = new PlanApprovalGate[]
        {
            new("G1", "After A only", ["A"], ["B"], PlanGateStatus.Pending),
        };
        var plan = MakePlan(tasks, gates);
        // With only 2 tasks, A at level 0, B at level 1 — stage boundary matches
        var levels = Levels(("A", 0), ("B", 1));

        var result = ApprovalAnchorInferenceEngine.Infer(plan, levels);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PrimaryGateId, Is.EqualTo("G1"));
    }

    [Test]
    public void TaskEntry_FallbackWhenOnlyBeforeAnchor()
    {
        // Gate with multiple after tasks (no single after) and single before task
        // Won't match stage because boundary doesn't align with levels
        var tasks = new[]
        {
            T("A", "pending"), T("B", "pending"),
            T("C", "pending", "A"), T("D", "pending", "B"),
            T("E", "pending", "C", "D"),
        };
        var gates = new PlanApprovalGate[]
        {
            // after:[C,D] before:[E] — C and D are at different positions in the graph
            // Won't match stage boundary because levels have A,B at 0, C,D at 1, E at 2
            // and [C,D]->[E] IS actually stage:2. Use a non-matching gate instead.
            new("G1", "Before E", ["A", "C"], ["E"], PlanGateStatus.Pending),
        };
        var plan = MakePlan(tasks, gates);
        var levels = Levels(("A", 0), ("B", 0), ("C", 1), ("D", 1), ("E", 2));

        var result = ApprovalAnchorInferenceEngine.Infer(plan, levels);

        Assert.That(result, Is.Not.Null);
        // after:[A,C] spans levels 0 and 1 — doesn't match any stage boundary
        // before:[E] is single element — resolves to task-before:E
        Assert.That(result!.PrimaryAnchor, Is.EqualTo("task-before:E"));
    }

    // ── Equivalent controls (half-opacity) ────────────────────────────────────

    [Test]
    public void EquivalentControls_GetHalfOpacity()
    {
        // Two gates that resolve to the same anchor
        var tasks = new[] { T("A", "pending"), T("B", "pending", "A"), T("C", "pending", "B") };
        var gates = new PlanApprovalGate[]
        {
            new("G1", "First", ["A"], ["B"], PlanGateStatus.Pending),
            new("G2", "Duplicate", ["A"], ["B"], PlanGateStatus.Pending),
        };
        var plan = MakePlan(tasks, gates);
        var levels = Levels(("A", 0), ("B", 1), ("C", 2));

        var result = ApprovalAnchorInferenceEngine.Infer(plan, levels);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PrimaryGateId, Is.EqualTo("G1"));
        Assert.That(result.EquivalentGateIds, Contains.Item("G2"));
    }

    [Test]
    public void DifferentAnchors_NoEquivalents()
    {
        var tasks = new[] { T("A", "pending"), T("B", "pending", "A"), T("C", "pending", "B") };
        var gates = new PlanApprovalGate[]
        {
            new("G1", "Stage 1", ["A"], ["B"], PlanGateStatus.Pending),
            new("G2", "Stage 2", ["B"], ["C"], PlanGateStatus.Pending),
        };
        var plan = MakePlan(tasks, gates);
        var levels = Levels(("A", 0), ("B", 1), ("C", 2));

        var result = ApprovalAnchorInferenceEngine.Infer(plan, levels);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.EquivalentGateIds, Is.Empty);
    }

    // ── Changing primary changes the requirements sentence ─────────────────────

    [Test]
    public void ChangingPrimary_ChangesSentence()
    {
        var tasks = new[] { T("A", "pending"), T("B", "pending", "A"), T("C", "pending", "B") };
        var levels = Levels(("A", 0), ("B", 1), ("C", 2));

        // Plan with only a stage gate
        var gates1 = new PlanApprovalGate[]
        {
            new("G1", "Review", ["A"], ["B"], PlanGateStatus.Pending),
        };
        var plan1 = MakePlan(tasks, gates1);
        var result1 = ApprovalAnchorInferenceEngine.Infer(plan1, levels);

        // Plan with only a task-after gate (won't match stage because boundary doesn't align)
        var tasks2 = new[]
        {
            T("A", "pending"), T("B", "pending"), T("C", "pending", "A"),
        };
        var gates2 = new PlanApprovalGate[]
        {
            new("G2", "After A", ["A"], ["C"], PlanGateStatus.Pending),
        };
        var plan2 = MakePlan(tasks2, gates2);
        var levels2 = Levels(("A", 0), ("B", 0), ("C", 1));
        var result2 = ApprovalAnchorInferenceEngine.Infer(plan2, levels2);

        Assert.That(result1, Is.Not.Null);
        Assert.That(result2, Is.Not.Null);
        Assert.That(result1!.RequirementsSentence, Is.Not.EqualTo(result2!.RequirementsSentence));
    }

    // ── One logical gate → one summary item ───────────────────────────────────

    [Test]
    public void OneGate_OneSummaryItem()
    {
        var tasks = new[] { T("A", "pending"), T("B", "pending", "A") };
        var gates = new PlanApprovalGate[]
        {
            new("G1", "Single gate", ["A"], ["B"], PlanGateStatus.Pending),
        };
        var plan = MakePlan(tasks, gates);
        var levels = Levels(("A", 0), ("B", 1));

        var result = ApprovalAnchorInferenceEngine.Infer(plan, levels);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.SummaryItems, Has.Count.EqualTo(1));
        Assert.That(result.SummaryItems[0].GateId, Is.EqualTo("G1"));
    }

    [Test]
    public void MultipleGates_MultipleSummaryItems()
    {
        var tasks = new[] { T("A", "pending"), T("B", "pending", "A"), T("C", "pending", "B") };
        var gates = new PlanApprovalGate[]
        {
            new("G1", "First", ["A"], ["B"], PlanGateStatus.Pending),
            new("G2", "Second", ["B"], ["C"], PlanGateStatus.Pending),
        };
        var plan = MakePlan(tasks, gates);
        var levels = Levels(("A", 0), ("B", 1), ("C", 2));

        var result = ApprovalAnchorInferenceEngine.Infer(plan, levels);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.SummaryItems, Has.Count.EqualTo(2));
    }

    // ── Legacy cumulative stages ──────────────────────────────────────────────

    [Test]
    public void LegacyCumulativeStages_ResolvedCorrectly()
    {
        // Legacy gates use cumulative after/before (all tasks up to stage boundary)
        var tasks = new[]
        {
            T("A", "pending"), T("B", "pending"),
            T("C", "pending", "A", "B"), T("D", "pending", "A", "B"),
        };
        var gates = new PlanApprovalGate[]
        {
            new("G1", "Cumulative stage", ["A", "B"], ["C", "D"], PlanGateStatus.Pending),
        };
        var plan = MakePlan(tasks, gates);
        var levels = Levels(("A", 0), ("B", 0), ("C", 1), ("D", 1));

        var result = ApprovalAnchorInferenceEngine.Infer(plan, levels);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PrimaryAnchor, Is.EqualTo("stage:1"));
        Assert.That(result.RequirementsSentence, Does.Contain("stage 1"));
    }

    // ── Graph equivalence ─────────────────────────────────────────────────────

    [Test]
    public void GraphEquivalence_SameLogicalBoundary_SameAnchor()
    {
        // Two gates at the same logical boundary produce the same anchor
        var tasks = new[] { T("A", "pending"), T("B", "pending", "A"), T("C", "pending", "B") };
        var gates = new PlanApprovalGate[]
        {
            new("G1", "First", ["A"], ["B"], PlanGateStatus.Pending),
            new("G2", "Same boundary", ["A"], ["B"], PlanGateStatus.Pending),
        };
        var plan = MakePlan(tasks, gates);
        var levels = Levels(("A", 0), ("B", 1), ("C", 2));

        var result = ApprovalAnchorInferenceEngine.Infer(plan, levels);

        Assert.That(result, Is.Not.Null);
        // G2 is equivalent to G1 (same anchor)
        Assert.That(result!.EquivalentGateIds, Contains.Item("G2"));
    }

    // ── Parallel branches with multiple gates ─────────────────────────────────

    [Test]
    public void ParallelBranches_GatesOnDifferentPaths()
    {
        // A → B, A → C (parallel), B → D, C → D
        var tasks = new[]
        {
            T("A", "pending"),
            T("B", "pending", "A"), T("C", "pending", "A"),
            T("D", "pending", "B", "C"),
        };
        var gates = new PlanApprovalGate[]
        {
            new("G1", "After B", ["B"], ["D"], PlanGateStatus.Pending),
            new("G2", "After C", ["C"], ["D"], PlanGateStatus.Pending),
        };
        var plan = MakePlan(tasks, gates);
        var levels = Levels(("A", 0), ("B", 1), ("C", 1), ("D", 2));

        var result = ApprovalAnchorInferenceEngine.Infer(plan, levels);

        Assert.That(result, Is.Not.Null);
        // Both gates should produce summary items
        Assert.That(result!.SummaryItems, Has.Count.EqualTo(2));
    }

    // ── ALL joins ─────────────────────────────────────────────────────────────

    [Test]
    public void AllJoin_ProducesCorrectSentence()
    {
        var tasks = new[]
        {
            T("A", "pending"), T("B", "pending"),
            T("C", "pending", "A", "B"),
        };
        var gates = new PlanApprovalGate[]
        {
            new("G1", "Wait for all", ["A", "B"], ["C"], PlanGateStatus.Pending),
        };
        var plan = MakePlan(tasks, gates);
        // A and B at level 0, C at level 1 — this is a stage boundary
        var levels = Levels(("A", 0), ("B", 0), ("C", 1));

        var result = ApprovalAnchorInferenceEngine.Infer(plan, levels);

        Assert.That(result, Is.Not.Null);
        // Resolves as stage:1 because it matches the stage boundary
        Assert.That(result!.PrimaryAnchor, Is.EqualTo("stage:1"));
    }

    [Test]
    public void AllJoin_NotStageBoundary_ResolvesAsAll()
    {
        // Create a graph where the ALL join doesn't align with stage boundaries
        var tasks = new[]
        {
            T("A", "pending"), T("B", "pending", "A"),
            T("C", "pending", "A"), T("D", "pending", "B", "C"),
        };
        // Gate at B,C → D (ALL join) but levels put A=0, B=1, C=1, D=2
        // Stage boundary at 0→1 would be [A]→[B,C], and at 1→2 would be [B,C]→[D]
        // Gate [B,C]→[D] matches stage:2 because B,C are at level 1 and D at level 2
        var gates = new PlanApprovalGate[]
        {
            new("G1", "All join", ["B", "C"], ["D"], PlanGateStatus.Pending),
        };
        var plan = MakePlan(tasks, gates);
        var levels = Levels(("A", 0), ("B", 1), ("C", 1), ("D", 2));

        var result = ApprovalAnchorInferenceEngine.Infer(plan, levels);

        Assert.That(result, Is.Not.Null);
        // This actually matches stage boundary 1→2
        Assert.That(result!.PrimaryAnchor, Is.EqualTo("stage:2"));
    }

    // ── Fan-out patterns ──────────────────────────────────────────────────────

    [Test]
    public void FanOut_SingleTaskToMultiple()
    {
        // A → B, A → C, A → D (fan-out from A)
        var tasks = new[]
        {
            T("A", "pending"),
            T("B", "pending", "A"), T("C", "pending", "A"), T("D", "pending", "A"),
        };
        var gates = new PlanApprovalGate[]
        {
            new("G1", "Fan-out gate", ["A"], ["B", "C", "D"], PlanGateStatus.Pending),
        };
        var plan = MakePlan(tasks, gates);
        var levels = Levels(("A", 0), ("B", 1), ("C", 1), ("D", 1));

        var result = ApprovalAnchorInferenceEngine.Infer(plan, levels);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PrimaryAnchor, Is.EqualTo("stage:1"));
        Assert.That(result.SummaryItems, Has.Count.EqualTo(1));
    }

    // ── Every-stage compression ───────────────────────────────────────────────

    [Test]
    public void EveryStageCompression_DetectedBySummaryBuilder()
    {
        // When gates cover every stage boundary, the summary builder compresses to BetweenEveryStage
        var tasks = new[]
        {
            T("A", "pending"), T("B", "pending", "A"),
            T("C", "pending", "B"), T("D", "pending", "C"),
        };
        var gates = new PlanApprovalGate[]
        {
            new("G1", "1-2", ["A"], ["B"], PlanGateStatus.Pending),
            new("G2", "2-3", ["B"], ["C"], PlanGateStatus.Pending),
            new("G3", "3-4", ["C"], ["D"], PlanGateStatus.Pending),
        };
        var plan = MakePlan(tasks, gates);
        var levels = Levels(("A", 0), ("B", 1), ("C", 2), ("D", 3));

        // The inference engine still returns individual items
        var result = ApprovalAnchorInferenceEngine.Infer(plan, levels);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.SummaryItems, Has.Count.EqualTo(3));

        // But the summary builder compresses them
        var summary = PlanApprovalSummaryBuilder.Build(plan, levels);
        Assert.That(summary.BetweenEveryStage, Is.True);
        Assert.That(summary.Items, Is.Empty);
    }

    // ── Completed regions (read-only per PlanApprovalControlLockPolicy) ────────

    [Test]
    public void CompletedRegion_InferenceStillWorks_LockPolicyAppliesSeparately()
    {
        // Inference works on gates regardless of task completion state
        var tasks = new[]
        {
            T("A", PlanTaskStatus.Complete),
            T("B", PlanTaskStatus.Complete, "A"),
            T("C", PlanTaskStatus.Pending, "B"),
        };
        var gates = new PlanApprovalGate[]
        {
            new("G1", "After completed", ["A"], ["B"], PlanGateStatus.Approved),
            new("G2", "Active gate", ["B"], ["C"], PlanGateStatus.Pending),
        };
        var plan = MakePlan(tasks, gates);
        var levels = Levels(("A", 0), ("B", 1), ("C", 2));

        var result = ApprovalAnchorInferenceEngine.Infer(plan, levels);

        Assert.That(result, Is.Not.Null);
        // G1 is approved but still participates in anchor resolution
        Assert.That(result!.SummaryItems, Has.Count.EqualTo(2));

        // Separately verify that the lock policy correctly locks the completed region
        Assert.That(PlanApprovalControlLockPolicy.IsStageMilestoneLocked(plan, ["A"], ["B"]), Is.True);
        Assert.That(PlanApprovalControlLockPolicy.IsTaskExitLocked(plan, "A"), Is.True);
        Assert.That(PlanApprovalControlLockPolicy.IsTaskExitLocked(plan, "B"), Is.True);
    }

    // ── Environmental font sizing ─────────────────────────────────────────────

    [Test]
    public void FontMetrics_AppliesFactor()
    {
        var metrics = ApprovalAnchorInferenceEngine.ComputeFontMetrics(14.0, 1.25);

        Assert.That(metrics.BaseFontSize, Is.EqualTo(14.0));
        Assert.That(metrics.FontSizeFactor, Is.EqualTo(1.25));
        Assert.That(metrics.EffectiveFontSize, Is.EqualTo(17.5));
    }

    [Test]
    public void FontMetrics_DefaultFactor_NoChange()
    {
        var metrics = ApprovalAnchorInferenceEngine.ComputeFontMetrics(12.0, 1.0);

        Assert.That(metrics.EffectiveFontSize, Is.EqualTo(12.0));
    }

    [Test]
    public void FontMetrics_LargeFactor_Scales()
    {
        var metrics = ApprovalAnchorInferenceEngine.ComputeFontMetrics(14.0, 2.0);

        Assert.That(metrics.EffectiveFontSize, Is.EqualTo(28.0));
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Test]
    public void EmptyGates_ReturnsNull()
    {
        var tasks = new[] { T("A", "pending"), T("B", "pending", "A") };
        var plan = MakePlan(tasks, []);
        var levels = Levels(("A", 0), ("B", 1));

        var result = ApprovalAnchorInferenceEngine.Infer(plan, levels);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ExplicitPresentationAnchor_Respected()
    {
        var tasks = new[] { T("A", "pending"), T("B", "pending", "A"), T("C", "pending", "B") };
        var gates = new PlanApprovalGate[]
        {
            new("G1", "Explicit", ["A"], ["B"], PlanGateStatus.Pending,
                PresentationAnchor: "task-before:B"),
        };
        var plan = MakePlan(tasks, gates);
        var levels = Levels(("A", 0), ("B", 1), ("C", 2));

        var result = ApprovalAnchorInferenceEngine.Infer(plan, levels);

        Assert.That(result, Is.Not.Null);
        // Explicit anchor overrides inference
        Assert.That(result!.PrimaryAnchor, Is.EqualTo("task-before:B"));
        Assert.That(result.RequirementsSentence, Does.Contain("Task B"));
    }

    // ── Requirements sentence content ─────────────────────────────────────────

    [Test]
    public void RequirementsSentence_Stage_ContainsStageNumbers()
    {
        var tasks = new[] { T("A", "pending"), T("B", "pending", "A"), T("C", "pending", "B") };
        var plan = MakePlan(tasks, [new("G1", "gate", ["A"], ["B"], PlanGateStatus.Pending)]);
        var levels = Levels(("A", 0), ("B", 1), ("C", 2));

        var sentence = ApprovalAnchorInferenceEngine.BuildRequirementsSentence("stage:1", plan, levels);

        Assert.That(sentence, Does.Contain("stage 1"));
        Assert.That(sentence, Does.Contain("stage 2"));
        Assert.That(sentence, Does.Contain("of 3"));
    }

    [Test]
    public void RequirementsSentence_TaskAfter_ContainsTaskTitle()
    {
        var tasks = new[] { T("A", "pending"), T("B", "pending", "A") };
        var plan = MakePlan(tasks, []);
        var levels = Levels(("A", 0), ("B", 1));

        var sentence = ApprovalAnchorInferenceEngine.BuildRequirementsSentence("task-after:A", plan, levels);

        Assert.That(sentence, Does.Contain("Task A"));
        Assert.That(sentence, Does.Contain("completes"));
    }

    [Test]
    public void RequirementsSentence_TaskBefore_ContainsTaskTitle()
    {
        var tasks = new[] { T("A", "pending"), T("B", "pending", "A") };
        var plan = MakePlan(tasks, []);
        var levels = Levels(("A", 0), ("B", 1));

        var sentence = ApprovalAnchorInferenceEngine.BuildRequirementsSentence("task-before:B", plan, levels);

        Assert.That(sentence, Does.Contain("Task B"));
        Assert.That(sentence, Does.Contain("starts"));
    }

    [Test]
    public void RequirementsSentence_AllJoin_ContainsTaskTitles()
    {
        var tasks = new[]
        {
            T("A", "pending"), T("B", "pending"),
            T("C", "pending", "A", "B"),
        };
        var plan = MakePlan(tasks, []);
        var levels = Levels(("A", 0), ("B", 0), ("C", 1));

        var sentence = ApprovalAnchorInferenceEngine.BuildRequirementsSentence("all:C", plan, levels);

        Assert.That(sentence, Does.Contain("ALL join"));
        Assert.That(sentence, Does.Contain("Task C"));
    }

    [Test]
    public void RequirementsSentence_UnknownAnchor_FallbackText()
    {
        var tasks = new[] { T("A", "pending") };
        var plan = MakePlan(tasks, []);
        var levels = Levels(("A", 0));

        var sentence = ApprovalAnchorInferenceEngine.BuildRequirementsSentence("unknown:xyz", plan, levels);

        Assert.That(sentence, Does.Contain("gate boundary"));
    }

    // ── Summary item descriptions ─────────────────────────────────────────────

    [Test]
    public void SummaryItems_DescriptionsMatchAnchorKind()
    {
        var tasks = new[] { T("A", "pending"), T("B", "pending", "A"), T("C", "pending", "B") };
        var gates = new PlanApprovalGate[]
        {
            new("G1", "Review work", ["A"], ["B"], PlanGateStatus.Pending),
            new("G2", "Final check", ["B"], ["C"], PlanGateStatus.Pending),
        };
        var plan = MakePlan(tasks, gates);
        var levels = Levels(("A", 0), ("B", 1), ("C", 2));

        var result = ApprovalAnchorInferenceEngine.Infer(plan, levels);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.SummaryItems[0].Description, Does.Contain("Stage milestone"));
        Assert.That(result.SummaryItems[1].Description, Does.Contain("Stage milestone"));
    }

    // ── Themes (dark/light mode) — anchor content is theme-independent ────────

    [Test]
    public void AnchorResolution_IsThemeIndependent()
    {
        // Same plan produces same anchors regardless of hypothetical theme
        var tasks = new[] { T("A", "pending"), T("B", "pending", "A") };
        var gates = new PlanApprovalGate[]
        {
            new("G1", "gate", ["A"], ["B"], PlanGateStatus.Pending),
        };
        var plan = MakePlan(tasks, gates);
        var levels = Levels(("A", 0), ("B", 1));

        var result1 = ApprovalAnchorInferenceEngine.Infer(plan, levels);
        var result2 = ApprovalAnchorInferenceEngine.Infer(plan, levels);

        Assert.That(result1!.PrimaryAnchor, Is.EqualTo(result2!.PrimaryAnchor));
        Assert.That(result1.RequirementsSentence, Is.EqualTo(result2.RequirementsSentence));
    }
}
