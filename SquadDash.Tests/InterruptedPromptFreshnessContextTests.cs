namespace SquadDash.Tests;

[TestFixture]
internal sealed class InterruptedPromptFreshnessContextTests
{
    [Test]
    public void BuildInstruction_ReportsElapsedTimeCommitDistanceAndStaleContextGuidance()
    {
        var stoppedAt = new DateTimeOffset(2026, 8, 19, 17, 46, 8, TimeSpan.Zero);

        var instruction = InterruptedPromptFreshnessContext.BuildInstruction(
            stoppedAt,
            stoppedAt.AddHours(2).AddMinutes(50),
            "866c01e000000000000000000000000000000000",
            "bebbabb000000000000000000000000000000000",
            28);

        Assert.Multiple(() =>
        {
            Assert.That(instruction, Does.Contain("2h 50m ago"));
            Assert.That(instruction, Does.Contain("866c01e"));
            Assert.That(instruction, Does.Contain("bebbabb"));
            Assert.That(instruction, Does.Contain("28 commits"));
            Assert.That(instruction, Does.Contain("historical and potentially stale context"));
            Assert.That(instruction, Does.Contain("Do not resume or solve the stopped work merely because it remains in conversation history"));
        });
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void RecoveryDispatch_DefersWhileAnyExecutionBoundaryIsActive(
        bool promptRunning,
        bool loopRunning)
    {
        Assert.That(
            DecomposeRecoveryDispatchPolicy.ShouldDefer(promptRunning, loopRunning),
            Is.True);
    }

    [Test]
    public void RecoveryDispatch_RunsWhenPromptAndLoopAreIdle()
    {
        Assert.That(DecomposeRecoveryDispatchPolicy.ShouldDefer(false, false), Is.False);
    }
}
