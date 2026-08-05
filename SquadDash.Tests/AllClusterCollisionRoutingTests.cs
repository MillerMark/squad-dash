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
        Assert.That(rect.Width, Is.EqualTo(58).Within(0.01),
            "A bare ALL badge must not reserve the width of an absent validation title.");
    }

    [Test]
    public void StackAllClusterCenters_UpperValidationStackClearsLowerBadge()
    {
        var items = new[]
        {
            new ValidationShieldPresenter.AllClusterStackItem(200, 1),
            new ValidationShieldPresenter.AllClusterStackItem(220, 0),
        };

        var centers = ValidationShieldPresenter.StackAllClusterCenters(items, 1.0);
        var upper = ValidationShieldPresenter.ComputeAllClusterFootprint(400, centers[0], 1, 1.0);
        var lower = ValidationShieldPresenter.ComputeAllClusterFootprint(400, centers[1], 0, 1.0);

        Assert.That(lower.Top, Is.GreaterThanOrEqualTo(
            upper.Bottom + ValidationShieldPresenter.BaseClusterConnectorClearance - 0.01));
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

        var clearance = ValidationShieldPresenter.BaseClusterConnectorClearance;
        Assert.Multiple(() =>
        {
            Assert.That(detour.Any(point => point.Y <= 100 - clearance), Is.True);
            Assert.That(ValidationShieldPresenter.IsConnectorRouteForwardOnly(detour), Is.True);
            Assert.That(ValidationShieldPresenter.IsConnectorRouteClear(detour, footprints), Is.True);
            Assert.That(detour[^2].Y, Is.EqualTo(detour[^1].Y).Within(0.01));
            Assert.That(detour[^2].X, Is.LessThan(detour[^1].X));
        });
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
        Assert.Multiple(() =>
        {
            Assert.That(ValidationShieldPresenter.IsConnectorRouteForwardOnly(detour), Is.True);
            Assert.That(ValidationShieldPresenter.IsConnectorRouteClear(detour, footprints), Is.True);
        });
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
        Assert.That(detour!.Any(point => point.Y <= expectedMaxY + 0.01), Is.True,
            $"A route lane at scale {scale} should be at or above {expectedMaxY}");
        Assert.That(ValidationShieldPresenter.IsConnectorRouteClear(detour, footprints), Is.True);
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
        var clearance = ValidationShieldPresenter.BaseClusterConnectorClearance;
        Assert.Multiple(() =>
        {
            Assert.That(detour!.Any(point => point.Y <= fp1.Top - clearance), Is.True);
            Assert.That(ValidationShieldPresenter.IsConnectorRouteForwardOnly(detour), Is.True);
            Assert.That(ValidationShieldPresenter.IsConnectorRouteClear(detour, footprints), Is.True);
        });
    }

    [Test]
    public void ComputeConnectorDetour_PrefersShorterLowerLane()
    {
        var footprints = new[]
        {
            new ValidationShieldPresenter.LayoutRect(300, 100, 144, 100)
        };

        var detour = ValidationShieldPresenter.ComputeConnectorDetour(
            (100, 185), (600, 185), footprints, 1.0);

        Assert.That(detour, Is.Not.Null.And.Not.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(detour!.Any(point => point.Y >= 214), Is.True);
            Assert.That(ValidationShieldPresenter.IsConnectorRouteForwardOnly(detour), Is.True);
            Assert.That(ValidationShieldPresenter.IsConnectorRouteClear(detour, footprints), Is.True);
        });
    }

    [Test]
    public void ComputeConnectorDetour_NarrowHorizontalSpace_NeverBacktracks()
    {
        var footprints = new[]
        {
            new ValidationShieldPresenter.LayoutRect(120, 100, 60, 100)
        };

        var detour = ValidationShieldPresenter.ComputeConnectorDetour(
            (100, 150), (220, 150), footprints, 1.0);

        Assert.That(detour, Is.Not.Null.And.Not.Empty);
        Assert.That(ValidationShieldPresenter.IsConnectorRouteForwardOnly(detour!), Is.True);
        Assert.That(ValidationShieldPresenter.IsConnectorRouteClear(detour!, footprints), Is.True);
    }

    [Test]
    public void ComputeConnectorDetour_BareAllBetweenNearbyTasks_FindsCleanRoute()
    {
        // Mirrors a same-row task connector crossing a bare ALL badge at the adjacent
        // stage boundary. The badge is narrow enough to leave approach lanes on both sides.
        var footprints = new[]
        {
            ValidationShieldPresenter.ComputeAllClusterFootprint(332, 208, 0, 1.0),
        };

        var detour = ValidationShieldPresenter.ComputeConnectorDetour(
            (262, 208), (402, 208), footprints, 1.0);

        Assert.That(detour, Is.Not.Null.And.Not.Empty);
        Assert.That(ValidationShieldPresenter.IsConnectorRouteForwardOnly(detour!), Is.True);
        Assert.That(ValidationShieldPresenter.IsConnectorRouteClear(detour!, footprints), Is.True);
    }

    [Test]
    public void ComputeRoundedRouteCorners_UsesHalfOfShorterAdjacentSegment()
    {
        var route = new (double X, double Y)[]
        {
            (0, 0),
            (20, 0),
            (20, 10),
            (50, 10),
        };

        var corners = ValidationShieldPresenter.ComputeRoundedRouteCorners(route);

        Assert.That(corners, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(corners[0].Radius, Is.EqualTo(5).Within(0.001));
            Assert.That(corners[0].Entry, Is.EqualTo((15d, 0d)));
            Assert.That(corners[0].Exit, Is.EqualTo((20d, 5d)));
            Assert.That(corners[1].Radius, Is.EqualTo(5).Within(0.001));
            Assert.That(corners[1].Entry, Is.EqualTo((20d, 5d)));
            Assert.That(corners[1].Exit, Is.EqualTo((25d, 10d)));
        });
    }

    [Test]
    public void ComputeRoundedRouteCorners_FourTurnDetour_RoundsEveryCorner()
    {
        var route = new (double X, double Y)[]
        {
            (262, 208),
            (289, 208),
            (289, 177),
            (375, 177),
            (375, 208),
            (402, 208),
        };

        var corners = ValidationShieldPresenter.ComputeRoundedRouteCorners(route);

        Assert.That(corners, Has.Count.EqualTo(4));
        Assert.That(corners.All(corner => corner.Radius > 0), Is.True);
    }
}
