using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Dense validation fixture rendering tests. Verifies that shields, titles, task spinners,
/// approval controls, and connectors do not overlap across a variety of challenging layout
/// scenarios.
///
/// <para><b>Manual UI Review:</b></para>
/// <list type="bullet">
///   <item>Fixture A (3 stacked milestone validations): Load a plan with 3 Stage-anchored
///     validations at StageIndex 0 into PlanViewerWindow. Verify vertical stacking with no overlap.</item>
///   <item>Fixture B (task-entry + task-exit validations): Load a plan with 2 Before + 2 After
///     anchored validations on task "T2". Verify shields appear below the task without collision.</item>
///   <item>Fixture C (ALL-boundary stack): Load a plan with 2 All-anchored validations at gate
///     "GATE-1". Verify shields stack below the gate center without overlap.</item>
///   <item>Fixture D (final validation at rail): Load a plan with a single Rail-anchored
///     validation. Verify it appears in the top rail area with correct vertical offset.</item>
///   <item>Fixture E (mixed states): Load a plan with 5 validations in Passed/Failed/Ready/
///     Validating/Pending states at different anchor positions. Verify distinct visual states and
///     no layout collisions.</item>
///   <item>Fixture F (narrow columns + long titles): Load a plan with nodeWidth=80 and titles
///     exceeding 28 characters. Verify truncation with ellipsis and no shield overlap.</item>
/// </list>
/// </summary>
[TestFixture]
internal sealed class DenseValidationFixtureRenderingTests
{
    // ── Shared layout constants ───────────────────────────────────────────────

    private const double DefaultScaleFactor = 1.0;
    private const double DefaultNodeWidth = 200;
    private const double DefaultNodeHeight = 48;
    private const double DefaultGraphTop = 180;
    private const double DefaultBaseRowSpacing = 80;
    private const double NarrowNodeWidth = 80;

    private static readonly IReadOnlyList<double> DefaultStageBoundaryXs =
        new[] { 250.0, 500.0, 750.0 };

    private static readonly IReadOnlyDictionary<string, (double X, double Y)> DefaultTaskPositions =
        new Dictionary<string, (double, double)>
        {
            ["T1"] = (100, 200),
            ["T2"] = (350, 200),
            ["T3"] = (600, 200),
            ["T4"] = (100, 320),
            ["T5"] = (350, 320),
        };

    private static readonly IReadOnlyList<(double CenterX, double CenterY, string AllKey)> DefaultGateCenters =
        new[] { (375.0, 140.0, "GATE-1"), (625.0, 140.0, "GATE-2") };

    // ── Fixture A: 3 stacked milestone (Stage) validations ────────────────────

    [Test]
    public void FixtureA_ThreeStackedStageValidations_NoOverlap()
    {
        var anchors = new[]
        {
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Stage, StageIndex: 0),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Stage, StageIndex: 0),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Stage, StageIndex: 0),
        };

        var positions = ComputePositionsForAnchors(anchors);
        AssertNoOverlap(positions, "Fixture A: stage-stacked shields");
    }

    [Test]
    public void FixtureA_RailHeight_SufficientForThreeStackedStage()
    {
        var anchors = new[]
        {
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Stage, StageIndex: 0),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Stage, StageIndex: 0),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Stage, StageIndex: 0),
        };

        var railHeight = ValidationShieldPresenter.ComputeValidationRailHeight(anchors, DefaultScaleFactor);

        // Rail must cover padding + 3 stacked shields
        var expected = (ValidationShieldPresenter.BaseRailTopPadding +
                        3 * ValidationShieldPresenter.BaseShieldStackSpacing) * DefaultScaleFactor;
        Assert.That(railHeight, Is.EqualTo(expected));
    }

    // ── Fixture B: 2 Before + 2 After validations on same task ────────────────

    [Test]
    public void FixtureB_TaskEntryAndExitValidations_NoOverlap()
    {
        var anchors = new[]
        {
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Before, TaskId: "T2"),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Before, TaskId: "T2"),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.After, TaskId: "T2"),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.After, TaskId: "T2"),
        };

        var positions = ComputePositionsForAnchors(anchors);
        AssertNoOverlap(positions, "Fixture B: task-entry/exit shields");
    }

    [Test]
    public void FixtureB_AttachedSpacing_CoversAllAttachedShields()
    {
        // 2 Before + 2 After = 4 attached validations per task
        int attachedCount = 4;
        var spacing = ValidationShieldPresenter.ComputeAttachedTaskSpacing(
            attachedCount, DefaultNodeHeight, DefaultBaseRowSpacing, DefaultScaleFactor);

        // Each shield at stackIndex i is placed at nodeHeight + (8 + i * 66) * scale.
        // The bottom of the last shield (stackIndex = 3) is:
        //   nodeHeight + (8 + 3*66)*scale + BaseShieldVisualHeight*scale
        var lastShieldBottom = DefaultNodeHeight +
            (8 + 3 * ValidationShieldPresenter.BaseShieldStackSpacing) * DefaultScaleFactor +
            ValidationShieldPresenter.BaseShieldVisualHeight * DefaultScaleFactor;

        Assert.That(spacing, Is.GreaterThanOrEqualTo(lastShieldBottom),
            "Attached task spacing must cover all stacked shields including their height");
    }

    // ── Fixture C: ALL-boundary stack with 2 validations ──────────────────────

    [Test]
    public void FixtureC_AllBoundaryStack_NoOverlap()
    {
        var anchors = new[]
        {
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.All, AllKey: "GATE-1"),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.All, AllKey: "GATE-1"),
        };

        var positions = ComputePositionsForAnchors(anchors);
        AssertNoOverlap(positions, "Fixture C: ALL-boundary stack");
    }

    [Test]
    public void FixtureC_AllBoundaryStack_PositionedRelativeToGateCenter()
    {
        var anchor = new ValidationShieldPresenter.ShieldAnchor(
            ValidationShieldPresenter.AnchorKind.All, AllKey: "GATE-1");

        double fallback = 0;
        var pos = ValidationShieldPresenter.ComputeShieldPosition(
            anchor, stackIndex: 0, DefaultScaleFactor,
            DefaultStageBoundaryXs, DefaultTaskPositions, DefaultNodeWidth, DefaultNodeHeight,
            DefaultGraphTop, DefaultGateCenters, ref fallback);

        // Should be centered horizontally on the gate
        var expectedLeft = 375.0 - 72.0 * DefaultScaleFactor;
        Assert.That(pos.Left, Is.EqualTo(expectedLeft).Within(0.01));
    }

    // ── Fixture D: Final validation (Rail anchor) ─────────────────────────────

    [Test]
    public void FixtureD_RailAnchor_UsesTopRailFallbackPosition()
    {
        var anchor = new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Rail);

        double fallback = 800;
        var pos = ValidationShieldPresenter.ComputeShieldPosition(
            anchor, stackIndex: 0, DefaultScaleFactor,
            DefaultStageBoundaryXs, DefaultTaskPositions, DefaultNodeWidth, DefaultNodeHeight,
            DefaultGraphTop, gateCenters: null, ref fallback);

        // Rail falls through to default case — uses fallback position
        Assert.That(pos.Left, Is.EqualTo(800));
        Assert.That(pos.Top, Is.EqualTo(28 * DefaultScaleFactor));
    }

    [Test]
    public void FixtureD_RailHeight_IncludesRailAnchor()
    {
        var anchors = new[]
        {
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Rail),
        };

        var railHeight = ValidationShieldPresenter.ComputeValidationRailHeight(anchors, DefaultScaleFactor);

        var expected = (ValidationShieldPresenter.BaseRailTopPadding +
                        1 * ValidationShieldPresenter.BaseShieldStackSpacing) * DefaultScaleFactor;
        Assert.That(railHeight, Is.EqualTo(expected));
    }

    // ── Fixture E: Mixed validation states ────────────────────────────────────

    [Test]
    public void FixtureE_MixedStates_DeriveCorrectVisualStates()
    {
        var statuses = new[]
        {
            (PlanValidationStatus.Passed, ValidationShieldPresenter.ShieldVisualState.Passed),
            (PlanValidationStatus.Failed, ValidationShieldPresenter.ShieldVisualState.Failed),
            (PlanValidationStatus.Ready, ValidationShieldPresenter.ShieldVisualState.Ready),
            (PlanValidationStatus.Validating, ValidationShieldPresenter.ShieldVisualState.Validating),
            (PlanValidationStatus.Pending, ValidationShieldPresenter.ShieldVisualState.Pending),
        };

        Assert.Multiple(() =>
        {
            foreach (var (status, expected) in statuses)
                Assert.That(ValidationShieldPresenter.DeriveVisualState(status), Is.EqualTo(expected),
                    $"Status '{status}' should map to {expected}");
        });
    }

    [Test]
    public void FixtureE_MixedStates_TooltipContent_CorrectLabels()
    {
        var tasks = new[]
        {
            new PlanTask("T1", "Setup Infra", "Provisions cloud", [], "high", PlanTaskStatus.Complete),
            new PlanTask("T2", "Deploy Service", "Deploys API", ["T1"], "high", PlanTaskStatus.Executing),
            new PlanTask("T3", "Run Integration", "Runs tests", ["T2"], "high", PlanTaskStatus.Pending),
        };

        var validations = new[]
        {
            MakeValidation("V1", "Post-setup check", PlanValidationStatus.Passed, afterIds: ["T1"], beforeIds: ["T2"]),
            MakeValidation("V2", "Deploy boundary", PlanValidationStatus.Failed, afterIds: ["T2"], beforeIds: ["T3"]),
            MakeValidation("V3", "Readiness gate", PlanValidationStatus.Ready, afterIds: ["T1", "T2"], beforeIds: ["T3"]),
            MakeValidation("V4", "Active check", PlanValidationStatus.Validating, afterIds: ["T1"], beforeIds: []),
            MakeValidation("V5", "Pending gate", PlanValidationStatus.Pending, afterIds: [], beforeIds: ["T3"]),
        };

        Assert.Multiple(() =>
        {
            var c1 = ValidationShieldPresenter.BuildTooltipContent(validations[0], tasks);
            Assert.That(c1.PrerequisiteLabels, Has.Count.EqualTo(1));
            Assert.That(c1.PrerequisiteLabels[0], Does.Contain("Setup Infra"));
            Assert.That(c1.BlockedLabels[0], Does.Contain("Deploy Service"));
            Assert.That(c1.StatusLabel, Is.EqualTo("Passed"));

            var c2 = ValidationShieldPresenter.BuildTooltipContent(validations[1], tasks);
            Assert.That(c2.StatusLabel, Is.EqualTo("Failed"));

            var c3 = ValidationShieldPresenter.BuildTooltipContent(validations[2], tasks);
            Assert.That(c3.PrerequisiteLabels, Has.Count.EqualTo(2));
            Assert.That(c3.StatusLabel, Is.EqualTo("Ready to validate"));

            var c4 = ValidationShieldPresenter.BuildTooltipContent(validations[3], tasks);
            Assert.That(c4.BlockedLabels, Is.Empty);
            Assert.That(c4.StatusLabel, Is.EqualTo("Validating now"));

            var c5 = ValidationShieldPresenter.BuildTooltipContent(validations[4], tasks);
            Assert.That(c5.PrerequisiteLabels, Is.Empty);
            Assert.That(c5.StatusLabel, Is.EqualTo("Waiting for prerequisite tasks"));
        });
    }

    [Test]
    public void FixtureE_MixedAnchors_NoOverlap()
    {
        // Place each validation at a different anchor to cover mixed layout
        var anchors = new[]
        {
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Stage, StageIndex: 0),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Before, TaskId: "T1"),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.After, TaskId: "T2"),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.All, AllKey: "GATE-1"),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Rail),
        };

        var positions = ComputePositionsForAnchors(anchors);
        AssertNoOverlap(positions, "Fixture E: mixed anchors");
    }

    // ── Fixture F: Narrow columns + long titles ───────────────────────────────

    [Test]
    public void FixtureF_LongTitles_TruncatedCorrectly()
    {
        var titles = new[]
        {
            "Integration Boundary Validation Gate Alpha",   // 44 chars — truncate
            "Cross-Service Contract Verification Point",    // 41 chars — truncate
            "Environmental Configuration Prerequisites",    // 41 chars — truncate
            "Exactly twenty-eight chars!",                  // 27 chars — no truncate
            "This title is 28 characters!",                 // 28 chars — no truncate
            "This title is 29 characters!!",                // 29 chars — truncate
        };

        Assert.Multiple(() =>
        {
            Assert.That(ValidationShieldPresenter.TruncateTitle(titles[0]),
                Has.Length.EqualTo(ValidationShieldPresenter.MaxTitleLength));
            Assert.That(ValidationShieldPresenter.TruncateTitle(titles[0]),
                Does.EndWith("…"));

            Assert.That(ValidationShieldPresenter.TruncateTitle(titles[1]),
                Has.Length.EqualTo(ValidationShieldPresenter.MaxTitleLength));
            Assert.That(ValidationShieldPresenter.TruncateTitle(titles[1]),
                Does.EndWith("…"));

            Assert.That(ValidationShieldPresenter.TruncateTitle(titles[2]),
                Has.Length.EqualTo(ValidationShieldPresenter.MaxTitleLength));

            // 27 chars — no truncation
            Assert.That(ValidationShieldPresenter.TruncateTitle(titles[3]),
                Is.EqualTo(titles[3]));

            // 28 chars — no truncation (equal to max)
            Assert.That(ValidationShieldPresenter.TruncateTitle(titles[4]),
                Is.EqualTo(titles[4]));

            // 29 chars — truncation
            Assert.That(ValidationShieldPresenter.TruncateTitle(titles[5]),
                Has.Length.EqualTo(ValidationShieldPresenter.MaxTitleLength));
            Assert.That(ValidationShieldPresenter.TruncateTitle(titles[5]),
                Does.EndWith("…"));
        });
    }

    [Test]
    public void FixtureF_NullAndEmptyTitles_ReturnEmpty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ValidationShieldPresenter.TruncateTitle(null), Is.EqualTo(string.Empty));
            Assert.That(ValidationShieldPresenter.TruncateTitle(""), Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void FixtureF_NarrowColumns_SameTaskStackedShieldsDoNotOverlap()
    {
        // Use narrow node width (80px) — shields wider than nodes.
        // Stacking multiple Before validations on the same task must not overlap.
        var narrowTaskPositions = new Dictionary<string, (double X, double Y)>
        {
            ["T1"] = (50, 200),
            ["T2"] = (140, 200),
            ["T3"] = (230, 200),
        };

        var anchors = new[]
        {
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Before, TaskId: "T2"),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Before, TaskId: "T2"),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Before, TaskId: "T2"),
        };

        var positions = new List<ValidationShieldPresenter.ShieldLayoutPosition>();
        double fallback = 0;
        for (int i = 0; i < anchors.Length; i++)
        {
            positions.Add(ValidationShieldPresenter.ComputeShieldPosition(
                anchors[i], i, DefaultScaleFactor,
                DefaultStageBoundaryXs, narrowTaskPositions, NarrowNodeWidth, DefaultNodeHeight,
                DefaultGraphTop, DefaultGateCenters, ref fallback));
        }

        AssertNoOverlap(positions, "Fixture F: narrow columns same-task stack");
    }

    [Test]
    public void FixtureF_LargeScaleFactor_ShieldsStillSeparated()
    {
        // Simulate environmental large font (scaleFactor 1.5)
        double scale = 1.5;
        var anchors = new[]
        {
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Stage, StageIndex: 1),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Stage, StageIndex: 1),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Stage, StageIndex: 1),
        };

        var positions = new List<ValidationShieldPresenter.ShieldLayoutPosition>();
        double fallback = 0;
        for (int i = 0; i < anchors.Length; i++)
        {
            positions.Add(ValidationShieldPresenter.ComputeShieldPosition(
                anchors[i], i, scale,
                DefaultStageBoundaryXs, DefaultTaskPositions, DefaultNodeWidth, DefaultNodeHeight,
                DefaultGraphTop, DefaultGateCenters, ref fallback));
        }

        AssertNoOverlap(positions, "Fixture F: large scale factor", scale);
    }

    // ── Overlap assertion helper ──────────────────────────────────────────────

    private static List<ValidationShieldPresenter.ShieldLayoutPosition> ComputePositionsForAnchors(
        IReadOnlyList<ValidationShieldPresenter.ShieldAnchor> anchors)
    {
        var positions = new List<ValidationShieldPresenter.ShieldLayoutPosition>();
        double fallback = 0;

        // Group by anchor identity to compute per-group stack indices
        var groupCounts = new Dictionary<string, int>();
        foreach (var anchor in anchors)
        {
            var key = AnchorGroupKey(anchor);
            groupCounts.TryGetValue(key, out int count);
            positions.Add(ValidationShieldPresenter.ComputeShieldPosition(
                anchor, count, DefaultScaleFactor,
                DefaultStageBoundaryXs, DefaultTaskPositions, DefaultNodeWidth, DefaultNodeHeight,
                DefaultGraphTop, DefaultGateCenters, ref fallback));
            groupCounts[key] = count + 1;
        }

        return positions;
    }

    private static string AnchorGroupKey(ValidationShieldPresenter.ShieldAnchor anchor) =>
        anchor.Kind switch
        {
            ValidationShieldPresenter.AnchorKind.Stage => $"Stage:{anchor.StageIndex}",
            ValidationShieldPresenter.AnchorKind.Before => $"Before:{anchor.TaskId}",
            ValidationShieldPresenter.AnchorKind.After => $"After:{anchor.TaskId}",
            ValidationShieldPresenter.AnchorKind.All => $"All:{anchor.AllKey}",
            _ => "Rail",
        };

    private static void AssertNoOverlap(
        IReadOnlyList<ValidationShieldPresenter.ShieldLayoutPosition> positions,
        string fixtureLabel,
        double scaleFactor = DefaultScaleFactor)
    {
        double w = ValidationShieldPresenter.BaseShieldVisualWidth * scaleFactor;
        double h = ValidationShieldPresenter.BaseShieldVisualHeight * scaleFactor;

        for (int i = 0; i < positions.Count; i++)
        {
            for (int j = i + 1; j < positions.Count; j++)
            {
                var a = positions[i];
                var b = positions[j];

                bool overlapsX = a.Left < b.Left + w && b.Left < a.Left + w;
                bool overlapsY = a.Top < b.Top + h && b.Top < a.Top + h;

                Assert.That(overlapsX && overlapsY, Is.False,
                    $"{fixtureLabel}: Shield {i} at ({a.Left:F1},{a.Top:F1}) overlaps Shield {j} at ({b.Left:F1},{b.Top:F1}) " +
                    $"[size {w:F0}×{h:F0}]");
            }
        }
    }

    // ── Model factory helpers ─────────────────────────────────────────────────

    private static PlanValidationNode MakeValidation(
        string id, string title, string status,
        IReadOnlyList<string>? afterIds = null,
        IReadOnlyList<string>? beforeIds = null) =>
        new(id, title, $"Description for {title}",
            afterIds ?? [], beforeIds ?? [],
            ["Assert something"], [], "evidence", [], false, status);
}
