using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Focused tests for ALL cluster footprint computation, connector clearance checks,
/// connector detour waypoint generation, and scale factor variations.
/// </summary>
[TestFixture]
internal sealed class AllClusterCollisionRoutingTests
{
    // ── ComputeAllClusterFootprint ───────────────────────────────────────────

    [Test]
    public void ComputeAllClusterFootprint_SingleShield_CorrectBounds()
    {
        var rect = ValidationShieldPresenter.ComputeAllClusterFootprint(
            gateCenterX: 400, gateCenterY: 200, shieldCount: 1, scaleFactor: 1.0);

        Assert.Multiple(() =>
        {
            // Width: max(29, 72) * 2 = 144
            Assert.That(rect.Width, Is.EqualTo(144).Within(0.01));
            // Left: 400 - 72 = 328
            Assert.That(rect.Left, Is.EqualTo(328).Within(0.01));
            // Top: 200 - 17 = 183
            Assert.That(rect.Top, Is.EqualTo(183).Within(0.01));
            // Bottom: shield at offset 24 + 1*66 = 90 below gate center → 200 + 90 = 290
            Assert.That(rect.Bottom, Is.EqualTo(290).Within(0.01));
        });
    }

    [Test]
    public void ComputeAllClusterFootprint_TwoShields_TallerThanOne()
    {
        var one = ValidationShieldPresenter.ComputeAllClusterFootprint(400, 200, 1, 1.0);
        var two = ValidationShieldPresenter.ComputeAllClusterFootprint(400, 200, 2, 1.0);

        Assert.That(two.Height, Is.GreaterThan(one.Height));
        Assert.That(two.Width, Is.EqualTo(one.Width));
        Assert.That(two.Left, Is.EqualTo(one.Left));
    }

    [Test]
    public void ComputeAllClusterFootprint_ThreeShields_StacksCorrectly()
    {
        var rect = ValidationShieldPresenter.ComputeAllClusterFootprint(400, 200, 3, 1.0);

        // Bottom: gateCenterY + (24 + 3*66) = 200 + 222 = 422; height = 422 - 183 = 239
        var expectedTop = 200 - ValidationShieldPresenter.BaseAllBadgeHalfHeight;
        var expectedBottom = 200 + (ValidationShieldPresenter.BaseAllValidationTopOffset +
                                    3 * ValidationShieldPresenter.BaseShieldStackSpacing);
        Assert.Multiple(() =>
        {
            Assert.That(rect.Top, Is.EqualTo(expectedTop).Within(0.01));
            Assert.That(rect.Bottom, Is.EqualTo(expectedBottom).Within(0.01));
        });
    }

    [Test]
    public void ComputeAllClusterFootprint_ZeroShields_BadgeOnly()
    {
        var rect = ValidationShieldPresenter.ComputeAllClusterFootprint(400, 200, 0, 1.0);

        Assert.That(rect.Height, Is.EqualTo(ValidationShieldPresenter.BaseAllBadgeHalfHeight * 2).Within(0.01));
    }

    [TestCase(1.0)]
    [TestCase(1.25)]
    [TestCase(1.5)]
    [TestCase(2.0)]
    public void ComputeAllClusterFootprint_ScalesLinearly(double scale)
    {
        var baseline = ValidationShieldPresenter.ComputeAllClusterFootprint(400, 200, 2, 1.0);
        var scaled = ValidationShieldPresenter.ComputeAllClusterFootprint(400, 200, 2, scale);

        Assert.Multiple(() =>
        {
            Assert.That(scaled.Width, Is.EqualTo(baseline.Width * scale).Within(0.01));
            Assert.That(scaled.Height, Is.EqualTo(baseline.Height * scale).Within(0.01));
        });
    }

    // ── IsConnectorPathClear ─────────────────────────────────────────────────

    [Test]
    public void IsConnectorPathClear_NoFootprints_ReturnsTrue()
    {
        var clear = ValidationShieldPresenter.IsConnectorPathClear(
            (100, 200), (600, 200),
            Array.Empty<ValidationShieldPresenter.LayoutRect>());

        Assert.That(clear, Is.True);
    }

    [Test]
    public void IsConnectorPathClear_ConnectorMissesCluster_ReturnsTrue()
    {
        // Cluster at Y=100-200, connector at Y=300
        var footprints = new[]
        {
            new ValidationShieldPresenter.LayoutRect(300, 100, 144, 100)
        };

        var clear = ValidationShieldPresenter.IsConnectorPathClear(
            (100, 300), (600, 300), footprints);

        Assert.That(clear, Is.True);
    }

    [Test]
    public void IsConnectorPathClear_ConnectorCrossesCluster_ReturnsFalse()
    {
        // Cluster covers X=300-444, Y=100-200; connector at Y=150 crosses through
        var footprints = new[]
        {
            new ValidationShieldPresenter.LayoutRect(300, 100, 144, 100)
        };

        var clear = ValidationShieldPresenter.IsConnectorPathClear(
            (100, 150), (600, 150), footprints);

        Assert.That(clear, Is.False);
    }

    [Test]
    public void IsConnectorPathClear_DiagonalConnectorCrosses_ReturnsFalse()
    {
        // Cluster at center, diagonal connector passes through
        var footprints = new[]
        {
            new ValidationShieldPresenter.LayoutRect(250, 150, 100, 100)
        };

        var clear = ValidationShieldPresenter.IsConnectorPathClear(
            (100, 100), (500, 300), footprints);

        Assert.That(clear, Is.False);
    }

    [Test]
    public void IsConnectorPathClear_ConnectorAboveCluster_ReturnsTrue()
    {
        var footprints = new[]
        {
            new ValidationShieldPresenter.LayoutRect(300, 200, 144, 100)
        };

        var clear = ValidationShieldPresenter.IsConnectorPathClear(
            (100, 50), (600, 50), footprints);

        Assert.That(clear, Is.True);
    }

    [Test]
    public void IsConnectorPathClear_MultipleFootprints_AnyIntersectionReturnsFalse()
    {
        var footprints = new[]
        {
            new ValidationShieldPresenter.LayoutRect(100, 100, 50, 50),  // misses
            new ValidationShieldPresenter.LayoutRect(300, 140, 144, 100) // hits at Y=150
        };

        var clear = ValidationShieldPresenter.IsConnectorPathClear(
            (100, 150), (600, 150), footprints);

        Assert.That(clear, Is.False);
    }

    // ── ComputeConnectorDetour ───────────────────────────────────────────────

    [Test]
    public void ComputeConnectorDetour_NullWhenPathClear()
    {
        var footprints = new[]
        {
            new ValidationShieldPresenter.LayoutRect(300, 100, 144, 100)
        };

        var detour = ValidationShieldPresenter.ComputeConnectorDetour(
            (100, 300), (600, 300), footprints, 1.0);

        Assert.That(detour, Is.Null);
    }

    [Test]
    public void ComputeConnectorDetour_RoutesAboveCluster()
    {
        // Cluster at Y=100-200, connector at Y=150 needs to go above
        var footprints = new[]
        {
            new ValidationShieldPresenter.LayoutRect(300, 100, 144, 100)
        };

        var detour = ValidationShieldPresenter.ComputeConnectorDetour(
            (100, 150), (600, 150), footprints, 1.0);

        Assert.That(detour, Is.Not.Null);
        Assert.That(detour!.Count, Is.GreaterThanOrEqualTo(4)); // start + 2 waypoints + end

        // All intermediate waypoints should be above the cluster top (100) minus clearance
        var clearance = ValidationShieldPresenter.BaseClusterConnectorClearance;
        for (int i = 1; i < detour.Count - 1; i++)
        {
            Assert.That(detour[i].Y, Is.LessThanOrEqualTo(100 - clearance),
                $"Waypoint {i} at Y={detour[i].Y} is not above cluster");
        }
    }

    [Test]
    public void ComputeConnectorDetour_StartsAndEndsAtOriginalPoints()
    {
        var footprints = new[]
        {
            new ValidationShieldPresenter.LayoutRect(300, 100, 144, 100)
        };

        var detour = ValidationShieldPresenter.ComputeConnectorDetour(
            (100, 150), (600, 150), footprints, 1.0);

        Assert.That(detour, Is.Not.Null);
        Assert.That(detour![0], Is.EqualTo((100.0, 150.0)));
        Assert.That(detour[^1], Is.EqualTo((600.0, 150.0)));
    }

    [Test]
    public void ComputeConnectorDetour_MultipleClustersCrossed_AllAvoided()
    {
        var footprints = new[]
        {
            new ValidationShieldPresenter.LayoutRect(200, 100, 100, 100),
            new ValidationShieldPresenter.LayoutRect(400, 80, 100, 120),
        };

        var detour = ValidationShieldPresenter.ComputeConnectorDetour(
            (50, 150), (700, 150), footprints, 1.0);

        Assert.That(detour, Is.Not.Null);
        // Intermediate waypoints route above each intersected cluster minus clearance
        var clearance = ValidationShieldPresenter.BaseClusterConnectorClearance;
        for (int i = 1; i < detour!.Count - 1; i++)
        {
            // Each waypoint Y should be at or below (cluster.Top - clearance) for one of the clusters
            var lowestAllowedY = footprints.Min(f => f.Top) - clearance;
            Assert.That(detour[i].Y, Is.LessThanOrEqualTo(lowestAllowedY),
                $"Waypoint {i} at Y={detour[i].Y} should be above clusters (max allowed: {lowestAllowedY})");
        }
    }

    [TestCase(1.0)]
    [TestCase(1.5)]
    [TestCase(2.0)]
    public void ComputeConnectorDetour_ScaleFactorAdjustsClearance(double scale)
    {
        var footprints = new[]
        {
            new ValidationShieldPresenter.LayoutRect(300, 100, 144, 100)
        };

        var detour = ValidationShieldPresenter.ComputeConnectorDetour(
            (100, 150), (600, 150), footprints, scale);

        Assert.That(detour, Is.Not.Null);
        var expectedMaxY = 100 - ValidationShieldPresenter.BaseClusterConnectorClearance * scale;
        for (int i = 1; i < detour!.Count - 1; i++)
        {
            Assert.That(detour[i].Y, Is.LessThanOrEqualTo(expectedMaxY).Within(0.01),
                $"Waypoint {i} at scale {scale}: Y={detour[i].Y} should be <= {expectedMaxY}");
        }
    }

    // ── Multiple ALL clusters at same boundary ───────────────────────────────

    [Test]
    public void MultipleAllClusters_SameBoundary_FootprintsDontOverlap()
    {
        // Two ALL clusters at different Y positions on same X
        var fp1 = ValidationShieldPresenter.ComputeAllClusterFootprint(400, 150, 2, 1.0);
        var fp2 = ValidationShieldPresenter.ComputeAllClusterFootprint(400, 400, 2, 1.0);

        // Verify no vertical overlap
        bool overlapsX = fp1.Left < fp2.Right && fp2.Left < fp1.Right;
        bool overlapsY = fp1.Top < fp2.Bottom && fp2.Top < fp1.Bottom;

        Assert.That(overlapsX && overlapsY, Is.False,
            $"Cluster 1 ({fp1.Left},{fp1.Top})-({fp1.Right},{fp1.Bottom}) overlaps " +
            $"Cluster 2 ({fp2.Left},{fp2.Top})-({fp2.Right},{fp2.Bottom})");
    }

    [Test]
    public void MultipleAllClusters_DifferentX_FootprintsIndependent()
    {
        var fp1 = ValidationShieldPresenter.ComputeAllClusterFootprint(200, 200, 2, 1.0);
        var fp2 = ValidationShieldPresenter.ComputeAllClusterFootprint(500, 200, 2, 1.0);

        // Different X positions — should not overlap horizontally
        Assert.That(fp1.Right, Is.LessThan(fp2.Left),
            "Clusters at X=200 and X=500 should not overlap horizontally");
    }

    [Test]
    public void ConnectorAvoidsMultipleClustersAtSameBoundary()
    {
        var fp1 = ValidationShieldPresenter.ComputeAllClusterFootprint(300, 150, 1, 1.0);
        var fp2 = ValidationShieldPresenter.ComputeAllClusterFootprint(300, 300, 1, 1.0);
        var footprints = new[] { fp1, fp2 };

        // Connector through both clusters
        var detour = ValidationShieldPresenter.ComputeConnectorDetour(
            (100, 200), (500, 200), footprints, 1.0);

        Assert.That(detour, Is.Not.Null);
        // Waypoints should route above all intersected clusters
        var clearance = ValidationShieldPresenter.BaseClusterConnectorClearance;
        for (int i = 1; i < detour!.Count - 1; i++)
        {
            Assert.That(detour[i].Y, Is.LessThanOrEqualTo(fp1.Top - clearance),
                $"Waypoint {i} should be above the first cluster");
        }
    }
}
