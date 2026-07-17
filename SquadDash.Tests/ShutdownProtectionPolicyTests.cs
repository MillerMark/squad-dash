using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ShutdownProtectionPolicyTests
{
    [TestCase(false, false, false, false, TestName = "Empty queue item does not block shutdown")]
    [TestCase(true, true, false, false, TestName = "Manually paused queue does not block shutdown")]
    [TestCase(true, false, true, false, TestName = "Active rightmost tab holding queue does not block shutdown")]
    [TestCase(true, false, false, true, TestName = "Executable auto-dispatch queue work blocks shutdown")]
    public void HasQueueWorkThatCanStart_ReflectsActualDispatchRisk(
        bool hasExecutableQueueItem,
        bool queueManuallyPaused,
        bool rightmostQueueTabActive,
        bool expected)
    {
        Assert.That(
            ShutdownProtectionPolicy.HasQueueWorkThatCanStart(
                hasExecutableQueueItem,
                queueManuallyPaused,
                rightmostQueueTabActive),
            Is.EqualTo(expected));
    }
}
