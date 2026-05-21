using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class RestartCoordinatorStateStoreTests {
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
    public void SaveAndLoad_RequestAndPlan_RoundTrip() {
        const string applicationRoot = @"D:\Drive\Source\SquadUI";
        const string requestId = "request-123";
        var requestedAt = new DateTimeOffset(2026, 4, 7, 19, 30, 0, TimeSpan.FromHours(-4));
        var plan = new RestartPlanState(
            applicationRoot,
            requestId,
            requestedAt,
            [
                new RunningInstanceRecord(
                    applicationRoot,
                    @"D:\Drive\Source\SquadUI",
                    1234,
                    5678,
                    9999)
            ]);

        _store.SaveRequest(new RestartRequestState(applicationRoot, requestId, requestedAt));
        _store.SavePlan(plan);

        var request = _store.LoadRequest(applicationRoot);
        var loadedPlan = _store.LoadPlan(applicationRoot, requestId);

        Assert.Multiple(() => {
            Assert.That(request, Is.Not.Null);
            Assert.That(request!.ApplicationRoot, Is.EqualTo(applicationRoot));
            Assert.That(request.RequestId, Is.EqualTo(requestId));
            Assert.That(request.RequestedAt, Is.EqualTo(requestedAt.ToUniversalTime()));

            Assert.That(loadedPlan, Is.Not.Null);
            Assert.That(loadedPlan!.ApplicationRoot, Is.EqualTo(applicationRoot));
            Assert.That(loadedPlan.RequestId, Is.EqualTo(requestId));
            Assert.That(loadedPlan.Instances, Has.Count.EqualTo(1));
            Assert.That(loadedPlan.Instances.Single().WorkspaceFolder, Is.EqualTo(applicationRoot));
        });
    }

    [Test]
    public void ClearRequestAndPlan_RemovesPersistedState() {
        const string applicationRoot = @"D:\Drive\Source\SquadUI";
        const string requestId = "request-456";
        var requestedAt = DateTimeOffset.UtcNow;

        _store.SaveRequest(new RestartRequestState(applicationRoot, requestId, requestedAt));
        _store.SavePlan(new RestartPlanState(
            applicationRoot,
            requestId,
            requestedAt,
            Array.Empty<RunningInstanceRecord>()));

        _store.ClearRequest(applicationRoot);
        _store.ClearPlan(applicationRoot, requestId);

        Assert.Multiple(() => {
            Assert.That(_store.LoadRequest(applicationRoot), Is.Null);
            Assert.That(_store.LoadPlan(applicationRoot, requestId), Is.Null);
        });
    }

    [Test]
    public void LoadPlan_ReleasesMutexBeforeReturning() {
        const string applicationRoot = @"D:\Drive\Source\SquadUI";
        const string requestId = "request-789";
        var requestedAt = DateTimeOffset.UtcNow;

        _store.SaveRequest(new RestartRequestState(applicationRoot, requestId, requestedAt));
        _store.SavePlan(new RestartPlanState(
            applicationRoot,
            requestId,
            requestedAt,
            Array.Empty<RunningInstanceRecord>()));

        using var loadPlanReturned = new ManualResetEventSlim(false);
        using var allowWorkerExit = new ManualResetEventSlim(false);
        using var loadRequestCompleted = new ManualResetEventSlim(false);
        RestartPlanState? loadedPlan = null;
        RestartRequestState? loadedRequest = null;
        Exception? loadPlanError = null;
        Exception? loadRequestError = null;

        var worker = new Thread(() => {
            try {
                loadedPlan = _store.LoadPlan(applicationRoot, requestId);
            }
            catch (Exception ex) {
                loadPlanError = ex;
            }
            finally {
                loadPlanReturned.Set();
            }

            allowWorkerExit.Wait();
        });

        var reader = new Thread(() => {
            try {
                loadedRequest = _store.LoadRequest(applicationRoot);
            }
            catch (Exception ex) {
                loadRequestError = ex;
            }
            finally {
                loadRequestCompleted.Set();
            }
        });

        worker.Start();

        try {
            Assert.That(loadPlanReturned.Wait(TimeSpan.FromSeconds(2)), Is.True, "LoadPlan should complete promptly.");

            reader.Start();

            Assert.That(
                loadRequestCompleted.Wait(TimeSpan.FromSeconds(2)),
                Is.True,
                "LoadRequest should not block after LoadPlan returns.");
        }
        finally {
            allowWorkerExit.Set();
            worker.Join(TimeSpan.FromSeconds(2));
            reader.Join(TimeSpan.FromSeconds(2));
        }

        Assert.Multiple(() => {
            Assert.That(loadPlanError, Is.Null);
            Assert.That(loadRequestError, Is.Null);
            Assert.That(loadedPlan, Is.Not.Null);
            Assert.That(loadedRequest, Is.Not.Null);
            Assert.That(loadedPlan!.RequestId, Is.EqualTo(requestId));
            Assert.That(loadedRequest!.RequestId, Is.EqualTo(requestId));
        });
    }

    [Test]
    public void LoadRequest_WhenNoFileExists_ReturnsNull() {
        var result = _store.LoadRequest(@"D:\Drive\Source\NeverSaved");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void LoadRequest_CorruptJson_ReturnsNull() {
        const string applicationRoot = @"D:\Drive\Source\CorruptApp";
        var requestPath = _store.GetRequestPathForWatcher(applicationRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(requestPath)!);
        File.WriteAllText(requestPath, "{ not valid json [[[");

        var result = _store.LoadRequest(applicationRoot);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void LoadPlan_WhenNoFileExists_ReturnsNull() {
        var result = _store.LoadPlan(@"D:\Drive\Source\NeverSaved", "req-999");
        Assert.That(result, Is.Null);
    }
}
