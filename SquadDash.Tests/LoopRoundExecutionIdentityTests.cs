namespace SquadDash.Tests;

[TestFixture]
internal sealed class LoopRoundExecutionIdentityTests
{
    [Test]
    public void CapturedRoundIdentity_RemainsOnCompletedTaskAfterSelectionAdvances()
    {
        var captured = new LoopRoundExecutionIdentity(
            "PLAN-1",
            "revision-1",
            "PLAN-1-001",
            "First task");
        var mutableCurrentTaskAfterFinalize = "PLAN-1-002";

        Assert.Multiple(() =>
        {
            Assert.That(captured.TaskId, Is.EqualTo("PLAN-1-001"));
            Assert.That(captured.TaskId, Is.Not.EqualTo(mutableCurrentTaskAfterFinalize));
        });
    }

    [Test]
    public void ResolveFailure_UsesCapturedIdentityAfterRuntimeStateWasCleared()
    {
        var captured = new LoopRoundExecutionIdentity(
            "PLAN-1", "revision-1", "PLAN-1-007", "Failure matrix");

        var resolved = LoopRoundExecutionIdentity.ResolveFailure(
            captured, null, null, null);

        Assert.That(resolved, Is.EqualTo(captured));
    }

    [Test]
    public void ResolveFailure_PrefersCapturedRoundOverAdvancedRuntimeIdentity()
    {
        var captured = new LoopRoundExecutionIdentity(
            "PLAN-OLD", "revision-old", "PLAN-OLD-001", "Old task");

        var resolved = LoopRoundExecutionIdentity.ResolveFailure(
            captured, "PLAN-1", "revision-1", "PLAN-1-007", "Failure matrix");

        Assert.That(resolved, Is.EqualTo(captured));
    }

    [Test]
    public void ResolveFailure_FallsBackToRuntimeIdentityWhenNoRoundWasCaptured()
    {
        var resolved = LoopRoundExecutionIdentity.ResolveFailure(
            null, "PLAN-1", "revision-1", "PLAN-1-007", "Failure matrix");

        Assert.That(
            resolved,
            Is.EqualTo(new LoopRoundExecutionIdentity(
                "PLAN-1", "revision-1", "PLAN-1-007", "Failure matrix")));
    }
}
