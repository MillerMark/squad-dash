using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanConnectorAnchorDistributorTests
{
    [Test]
    public void ResolveY_DuplicateDestinations_UseDistinctFanOutSlots()
    {
        var distributor = new PlanConnectorAnchorDistributor();
        distributor.Register("task", 200);
        distributor.Register("task", 200);
        distributor.Sort();

        Assert.Multiple(() =>
        {
            Assert.That(distributor.ResolveY("task", 200, 100, 90), Is.EqualTo(130));
            Assert.That(distributor.ResolveY("task", 200, 100, 90), Is.EqualTo(160));
        });
    }

    [Test]
    public void ResolveY_MixedDestinations_PreservesVerticalOrderingAndDuplicateSlots()
    {
        var distributor = new PlanConnectorAnchorDistributor();
        distributor.Register("task", 300);
        distributor.Register("task", 100);
        distributor.Register("task", 300);
        distributor.Sort();

        Assert.Multiple(() =>
        {
            Assert.That(distributor.ResolveY("task", 100, 0, 100), Is.EqualTo(25));
            Assert.That(distributor.ResolveY("task", 300, 0, 100), Is.EqualTo(50));
            Assert.That(distributor.ResolveY("task", 300, 0, 100), Is.EqualTo(75));
        });
    }

    [Test]
    public void ResolveY_OneConnector_RemainsCentered()
    {
        var distributor = new PlanConnectorAnchorDistributor();
        distributor.Register("task", 200);
        distributor.Sort();

        Assert.That(distributor.ResolveY("task", 200, 40, 80), Is.EqualTo(80));
    }
}
