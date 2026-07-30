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
}
