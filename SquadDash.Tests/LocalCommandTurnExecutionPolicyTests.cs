namespace SquadDash.Tests;

[TestFixture]
internal sealed class LocalCommandTurnExecutionPolicyTests {
    [Test]
    public void Create_WhenPromptIsRunningAndTurnExists_RestoresSuspendedTurnAndDefersLeadRefresh() {
        var suspendedTurn = new object();

        var plan = LocalCommandTurnExecutionPolicy.Create(isPromptRunning: true, suspendedTurn);

        Assert.Multiple(() => {
            Assert.That(plan.SuspendedTurn, Is.SameAs(suspendedTurn));
            Assert.That(plan.ShouldRestoreSuspendedTurn, Is.True);
            Assert.That(plan.ShouldRefreshLeadStatusAfterCompletion, Is.False);
        });
    }

    [Test]
    public void Create_WhenPromptIsRunningWithoutTurn_DoesNotRestoreButStillDefersLeadRefresh() {
        var plan = LocalCommandTurnExecutionPolicy.Create<object>(isPromptRunning: true, currentTurn: null);

        Assert.Multiple(() => {
            Assert.That(plan.SuspendedTurn, Is.Null);
            Assert.That(plan.ShouldRestoreSuspendedTurn, Is.False);
            Assert.That(plan.ShouldRefreshLeadStatusAfterCompletion, Is.False);
        });
    }

    [Test]
    public void Create_WhenNoPromptIsRunning_AllowsLeadRefreshWithoutRestoringATurn() {
        var suspendedTurn = new object();

        var plan = LocalCommandTurnExecutionPolicy.Create(isPromptRunning: false, suspendedTurn);

        Assert.Multiple(() => {
            Assert.That(plan.SuspendedTurn, Is.SameAs(suspendedTurn));
            Assert.That(plan.ShouldRestoreSuspendedTurn, Is.False);
            Assert.That(plan.ShouldRefreshLeadStatusAfterCompletion, Is.True);
        });
    }
}
