using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ValidationPlacementLayoutTests
{
    private const double Scale = 1.0;
    private const double NodeWidth = 220;
    private const double NodeHeight = 112;
    private const double BaseRowSpacing = 152;

    // ── Shield position computation ───────────────────────────────────────────

    [Test]
    public void ComputeShieldPosition_StageAnchor_PositionsAboveBoundary()
    {
        var anchor = new ValidationShieldPresenter.ShieldAnchor(
            ValidationShieldPresenter.AnchorKind.Stage, StageIndex: 0);
        var stageBoundaryXs = new[] { 400.0 };
        var taskPositions = new Dictionary<string, (double X, double Y)>();
        var fallback = 100.0;

        var pos = ValidationShieldPresenter.ComputeShieldPosition(
            anchor, stackIndex: 0, Scale, stageBoundaryXs,
            taskPositions, NodeWidth, NodeHeight, graphTop: 200,
            gateCenters: null, ref fallback);

        Assert.Multiple(() =>
        {
            Assert.That(pos.Left, Is.EqualTo(400 - 72).Within(0.01));
            Assert.That(pos.Top, Is.EqualTo(200 - 90).Within(0.01));
            Assert.That(pos.StackIndex, Is.EqualTo(0));
            Assert.That(pos.Anchor.Kind, Is.EqualTo(ValidationShieldPresenter.AnchorKind.Stage));
        });
    }

    [Test]
    public void ComputeShieldPosition_StageAnchor_StacksVerticallyAboveBoundary()
    {
        var anchor = new ValidationShieldPresenter.ShieldAnchor(
            ValidationShieldPresenter.AnchorKind.Stage, StageIndex: 0);
        var stageBoundaryXs = new[] { 400.0 };
        var taskPositions = new Dictionary<string, (double X, double Y)>();
        var fallback = 100.0;

        var pos0 = ValidationShieldPresenter.ComputeShieldPosition(
            anchor, stackIndex: 0, Scale, stageBoundaryXs,
            taskPositions, NodeWidth, NodeHeight, graphTop: 200,
            gateCenters: null, ref fallback);

        var pos1 = ValidationShieldPresenter.ComputeShieldPosition(
            anchor, stackIndex: 1, Scale, stageBoundaryXs,
            taskPositions, NodeWidth, NodeHeight, graphTop: 200,
            gateCenters: null, ref fallback);

        // Stack index 1 should be higher (lower top value) than stack index 0
        Assert.That(pos1.Top, Is.LessThan(pos0.Top));
        Assert.That(pos0.Top - pos1.Top,
            Is.EqualTo(ValidationShieldPresenter.BaseShieldStackSpacing).Within(0.01));
    }

    [Test]
    public void ComputeShieldPosition_BeforeAnchor_PositionsBelowTaskStart()
    {
        var anchor = new ValidationShieldPresenter.ShieldAnchor(
            ValidationShieldPresenter.AnchorKind.Before, TaskId: "TASK-1");
        var stageBoundaryXs = Array.Empty<double>();
        var taskPositions = new Dictionary<string, (double X, double Y)>
        {
            ["TASK-1"] = (300, 200),
        };
        var fallback = 100.0;

        var pos = ValidationShieldPresenter.ComputeShieldPosition(
            anchor, stackIndex: 0, Scale, stageBoundaryXs,
            taskPositions, NodeWidth, NodeHeight, graphTop: 100,
            gateCenters: null, ref fallback);

        Assert.Multiple(() =>
        {
            Assert.That(pos.Left,
                Is.EqualTo(300 -
                    (ValidationShieldPresenter.BaseShieldVisualWidth -
                     ValidationShieldPresenter.BaseShieldIconWidth) / 2).Within(0.01));
            Assert.That(pos.Top, Is.EqualTo(200 + NodeHeight + 8).Within(0.01));
        });
    }

    [Test]
    public void ComputeShieldPosition_AfterAnchor_PositionsBelowTaskEnd()
    {
        var anchor = new ValidationShieldPresenter.ShieldAnchor(
            ValidationShieldPresenter.AnchorKind.After, TaskId: "TASK-1");
        var stageBoundaryXs = Array.Empty<double>();
        var taskPositions = new Dictionary<string, (double X, double Y)>
        {
            ["TASK-1"] = (300, 200),
        };
        var fallback = 100.0;

        var pos = ValidationShieldPresenter.ComputeShieldPosition(
            anchor, stackIndex: 0, Scale, stageBoundaryXs,
            taskPositions, NodeWidth, NodeHeight, graphTop: 100,
            gateCenters: null, ref fallback);

        Assert.Multiple(() =>
        {
            Assert.That(pos.Left,
                Is.EqualTo(300 + NodeWidth -
                    (ValidationShieldPresenter.BaseShieldVisualWidth +
                     ValidationShieldPresenter.BaseShieldIconWidth) / 2).Within(0.01));
            Assert.That(pos.Top, Is.EqualTo(200 + NodeHeight + 8).Within(0.01));
        });
    }

    [Test]
    public void ComputeShieldPosition_AllAnchor_PositionsBelowJoinCenter()
    {
        var allKey = "A|B";
        var anchor = new ValidationShieldPresenter.ShieldAnchor(
            ValidationShieldPresenter.AnchorKind.All, AllKey: allKey);
        var stageBoundaryXs = Array.Empty<double>();
        var taskPositions = new Dictionary<string, (double X, double Y)>();
        var gateCenters = new[] { (CenterX: 500.0, CenterY: 300.0, AllKey: allKey) };
        var fallback = 100.0;

        var pos = ValidationShieldPresenter.ComputeShieldPosition(
            anchor, stackIndex: 0, Scale, stageBoundaryXs,
            taskPositions, NodeWidth, NodeHeight, graphTop: 100,
            gateCenters: gateCenters, ref fallback);

        Assert.Multiple(() =>
        {
            Assert.That(pos.Left, Is.EqualTo(500 - 72).Within(0.01));
            Assert.That(pos.Top, Is.EqualTo(300 + 24).Within(0.01));
        });
    }

    [Test]
    public void ComputeShieldPosition_RailFallback_AdvancesFallbackLeft()
    {
        var anchor = new ValidationShieldPresenter.ShieldAnchor(
            ValidationShieldPresenter.AnchorKind.Rail);
        var stageBoundaryXs = Array.Empty<double>();
        var taskPositions = new Dictionary<string, (double X, double Y)>();
        var fallback = 100.0;

        var pos = ValidationShieldPresenter.ComputeShieldPosition(
            anchor, stackIndex: 0, Scale, stageBoundaryXs,
            taskPositions, NodeWidth, NodeHeight, graphTop: 200,
            gateCenters: null, ref fallback);

        Assert.Multiple(() =>
        {
            Assert.That(pos.Left, Is.EqualTo(100).Within(0.01));
            Assert.That(pos.Top, Is.EqualTo(28).Within(0.01));
            Assert.That(fallback, Is.EqualTo(256).Within(0.01)); // 100 + 156
        });
    }

    [Test]
    public void ComputeShieldPosition_TaskBeforeStacking_IncrementsTopWithStackIndex()
    {
        var anchor = new ValidationShieldPresenter.ShieldAnchor(
            ValidationShieldPresenter.AnchorKind.Before, TaskId: "TASK-1");
        var taskPositions = new Dictionary<string, (double X, double Y)>
        {
            ["TASK-1"] = (300, 200),
        };
        var fallback = 100.0;

        var pos0 = ValidationShieldPresenter.ComputeShieldPosition(
            anchor, stackIndex: 0, Scale, Array.Empty<double>(),
            taskPositions, NodeWidth, NodeHeight, graphTop: 100,
            gateCenters: null, ref fallback);

        var pos1 = ValidationShieldPresenter.ComputeShieldPosition(
            anchor, stackIndex: 1, Scale, Array.Empty<double>(),
            taskPositions, NodeWidth, NodeHeight, graphTop: 100,
            gateCenters: null, ref fallback);

        Assert.That(pos1.Top - pos0.Top,
            Is.EqualTo(ValidationShieldPresenter.BaseShieldStackSpacing).Within(0.01));
    }

    // ── Rail height computation ───────────────────────────────────────────────

    [Test]
    public void ComputeValidationRailHeight_NoAnchors_ReturnsZero()
    {
        var anchors = Array.Empty<ValidationShieldPresenter.ShieldAnchor>();
        Assert.That(ValidationShieldPresenter.ComputeValidationRailHeight(anchors, Scale),
            Is.EqualTo(0));
    }

    [Test]
    public void ComputeValidationRailHeight_SingleStageAnchor_ReturnsMinimumHeight()
    {
        var anchors = new[]
        {
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Stage, StageIndex: 0),
        };
        var height = ValidationShieldPresenter.ComputeValidationRailHeight(anchors, Scale);
        Assert.That(height, Is.EqualTo(42 + 66).Within(0.01)); // BaseRailTopPadding + 1 * BaseShieldStackSpacing
    }

    [Test]
    public void ComputeValidationRailHeight_MultipleAtSameStage_IncreasesByStackSpacing()
    {
        var anchors = new[]
        {
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Stage, StageIndex: 0),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Stage, StageIndex: 0),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Stage, StageIndex: 0),
        };
        var height = ValidationShieldPresenter.ComputeValidationRailHeight(anchors, Scale);
        Assert.That(height, Is.EqualTo(42 + 3 * 66).Within(0.01));
    }

    [Test]
    public void ComputeValidationRailHeight_MixedAnchors_UsesMaxStack()
    {
        var anchors = new[]
        {
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Stage, StageIndex: 0),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Stage, StageIndex: 0),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Stage, StageIndex: 1),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Before, TaskId: "T1"),
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Rail),
        };
        var height = ValidationShieldPresenter.ComputeValidationRailHeight(anchors, Scale);
        // Max is stage:0 with 2 items vs stage:1 with 1 vs rail with 1 → max is 2
        Assert.That(height, Is.EqualTo(42 + 2 * 66).Within(0.01));
    }

    [Test]
    public void ComputeValidationRailHeight_ScaleFactor_AppliesCorrectly()
    {
        var anchors = new[]
        {
            new ValidationShieldPresenter.ShieldAnchor(ValidationShieldPresenter.AnchorKind.Stage, StageIndex: 0),
        };
        var height = ValidationShieldPresenter.ComputeValidationRailHeight(anchors, 1.5);
        Assert.That(height, Is.EqualTo((42 + 66) * 1.5).Within(0.01));
    }

    // ── Attached task spacing ─────────────────────────────────────────────────

    [Test]
    public void ComputeAttachedTaskSpacing_NoValidations_ReturnsBaseRowSpacing()
    {
        var spacing = ValidationShieldPresenter.ComputeAttachedTaskSpacing(
            0, NodeHeight, BaseRowSpacing, Scale);
        Assert.That(spacing, Is.EqualTo(BaseRowSpacing).Within(0.01));
    }

    [Test]
    public void ComputeAttachedTaskSpacing_OneValidation_ExpandsBeyondBase()
    {
        var spacing = ValidationShieldPresenter.ComputeAttachedTaskSpacing(
            1, NodeHeight, BaseRowSpacing, Scale);
        var expected = Math.Max(BaseRowSpacing, NodeHeight + (18 + 1 * 66));
        Assert.That(spacing, Is.EqualTo(expected).Within(0.01));
    }

    [Test]
    public void ComputeAttachedTaskSpacing_ThreeValidations_ReservesAdequateSpace()
    {
        var spacing = ValidationShieldPresenter.ComputeAttachedTaskSpacing(
            3, NodeHeight, BaseRowSpacing, Scale);
        var expected = Math.Max(BaseRowSpacing, NodeHeight + (18 + 3 * 66));
        Assert.That(spacing, Is.EqualTo(expected).Within(0.01));
        // Should be well above base row spacing
        Assert.That(spacing, Is.GreaterThan(BaseRowSpacing));
    }

    [Test]
    public void ComputeShieldPosition_RailWithStageIndex_StacksAboveMilestone()
    {
        var anchor = new ValidationShieldPresenter.ShieldAnchor(
            ValidationShieldPresenter.AnchorKind.Rail, StageIndex: 0);
        var stageBoundaryXs = new[] { 400.0 };
        var fallback = 100.0;

        var lower = ValidationShieldPresenter.ComputeShieldPosition(
            anchor, stackIndex: 0, Scale, stageBoundaryXs,
            new Dictionary<string, (double X, double Y)>(), NodeWidth, NodeHeight,
            graphTop: 240, gateCenters: null, ref fallback);
        var upper = ValidationShieldPresenter.ComputeShieldPosition(
            anchor, stackIndex: 1, Scale, stageBoundaryXs,
            new Dictionary<string, (double X, double Y)>(), NodeWidth, NodeHeight,
            graphTop: 240, gateCenters: null, ref fallback);

        Assert.Multiple(() =>
        {
            Assert.That(lower.Left, Is.EqualTo(400 - 72).Within(0.01));
            Assert.That(upper.Left, Is.EqualTo(lower.Left).Within(0.01));
            Assert.That(lower.Top - upper.Top,
                Is.EqualTo(ValidationShieldPresenter.BaseShieldStackSpacing).Within(0.01));
            Assert.That(fallback, Is.EqualTo(100).Within(0.01));
        });
    }

    [Test]
    public void InferComplexValidationStageIndex_UsesLatestPrerequisiteStage()
    {
        var stageIndex = ValidationShieldPresenter.InferComplexValidationStageIndex(
            afterLevels: [0, 1],
            beforeLevels: [2],
            stageCount: 3);

        Assert.That(stageIndex, Is.EqualTo(1));
    }

    [Test]
    public void InferComplexValidationStageIndex_ParallelEarlierTasks_UsesTheirSharedBoundary()
    {
        var stageIndex = ValidationShieldPresenter.InferComplexValidationStageIndex(
            afterLevels: [0, 0],
            beforeLevels: [2],
            stageCount: 3);

        Assert.That(stageIndex, Is.EqualTo(0));
    }

    [Test]
    public void AllCluster_ForeignConnectorThroughValidationStack_MovesWholeClusterAboveIt()
    {
        var adjusted = ValidationShieldPresenter.AvoidConnectorOverlapForAllCluster(
            initialCenterY: 276,
            attachedValidationCount: 1,
            foreignConnectorYs: [324],
            scaleFactor: 1.0);

        var clusterBottom = adjusted +
            ValidationShieldPresenter.BaseAllValidationTopOffset +
            ValidationShieldPresenter.BaseShieldStackSpacing;
        Assert.That(clusterBottom, Is.LessThanOrEqualTo(
            324 - ValidationShieldPresenter.BaseClusterConnectorClearance));
    }

    [Test]
    public void AllCluster_NoCrossingConnector_PreservesNaturalCenter()
    {
        var adjusted = ValidationShieldPresenter.AvoidConnectorOverlapForAllCluster(
            initialCenterY: 276,
            attachedValidationCount: 2,
            foreignConnectorYs: [520],
            scaleFactor: 1.0);

        Assert.That(adjusted, Is.EqualTo(276));
    }

    // ── Title truncation ──────────────────────────────────────────────────────

    [Test]
    public void TruncateTitle_ShortTitle_ReturnsUnchanged()
    {
        Assert.That(ValidationShieldPresenter.TruncateTitle("API Contract"),
            Is.EqualTo("API Contract"));
    }

    [Test]
    public void TruncateTitle_ExactMaxLength_ReturnsUnchanged()
    {
        var title = new string('A', ValidationShieldPresenter.MaxTitleLength);
        Assert.That(ValidationShieldPresenter.TruncateTitle(title), Is.EqualTo(title));
    }

    [Test]
    public void TruncateTitle_LongTitle_TruncatesWithEllipsis()
    {
        var title = "This is a very long validation title that exceeds the limit";
        var result = ValidationShieldPresenter.TruncateTitle(title);
        Assert.Multiple(() =>
        {
            Assert.That(result.Length, Is.EqualTo(ValidationShieldPresenter.MaxTitleLength));
            Assert.That(result, Does.EndWith("…"));
            Assert.That(result[..^1], Is.EqualTo(title[..(ValidationShieldPresenter.MaxTitleLength - 1)]));
        });
    }

    [Test]
    public void TruncateTitle_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ValidationShieldPresenter.TruncateTitle(null), Is.EqualTo(string.Empty));
            Assert.That(ValidationShieldPresenter.TruncateTitle(""), Is.EqualTo(string.Empty));
        });
    }
}
