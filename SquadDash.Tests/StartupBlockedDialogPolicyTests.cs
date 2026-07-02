using System;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class StartupBlockedDialogPolicyTests {
    private TestWorkspace _workspace = null!;
    private RestartCoordinatorStateStore _store = null!;

    [SetUp]
    public void SetUp() {
        _workspace = new TestWorkspace();
        _store = new RestartCoordinatorStateStore(_workspace.RootPath);
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    [Test]
    public void HasPendingRestartRequest_WhenRestartRequestExists_ReturnsTrue() {
        const string applicationRoot = @"D:\Drive\Source\SquadDash";

        _store.SaveRequest(new RestartRequestState(
            applicationRoot,
            "restart-123",
            DateTimeOffset.UtcNow));
        _store.SavePlan(new RestartPlanState(
            applicationRoot,
            "restart-123",
            DateTimeOffset.UtcNow,
            Array.Empty<RunningInstanceRecord>()));

        var result = StartupBlockedDialogPolicy.HasPendingRestartRequest(applicationRoot, _store);

        Assert.That(result, Is.True);
    }

    [Test]
    public void HasPendingRestartRequest_WhenRequestHasNoMatchingPlan_ReturnsFalseAndClearsRequest() {
        const string applicationRoot = @"D:\Drive\Source\SquadDash";

        _store.SaveRequest(new RestartRequestState(
            applicationRoot,
            "restart-without-plan",
            DateTimeOffset.UtcNow));

        var result = StartupBlockedDialogPolicy.HasPendingRestartRequest(applicationRoot, _store);

        Assert.Multiple(() => {
            Assert.That(result, Is.False);
            Assert.That(_store.LoadRequest(applicationRoot), Is.Null);
        });
    }

    [Test]
    public void HasPendingRestartRequest_WhenRestartRequestDoesNotExist_ReturnsFalse() {
        var result = StartupBlockedDialogPolicy.HasPendingRestartRequest(
            @"D:\Drive\Source\OtherSquadDash",
            _store);

        Assert.That(result, Is.False);
    }
}
