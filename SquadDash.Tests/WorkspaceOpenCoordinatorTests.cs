using System.Diagnostics;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class WorkspaceOpenCoordinatorTests {
    [Test]
    public void ReserveOrActivate_WithCurrentLease_ReturnsAlreadyOpenHere() {
        using var workspace = new TestWorkspace();
        var appRoot = workspace.GetPath("app-root");
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(repo);

        Assert.That(
            WorkspaceOwnershipLease.TryAcquire(appRoot, repo, out var lease),
            Is.True);
        Assert.That(lease, Is.Not.Null);

        using (lease) {
            var coordinator = new WorkspaceOpenCoordinator(new RunningInstanceRegistry(workspace.RootPath));

            var decision = coordinator.ReserveOrActivate(
                appRoot,
                repo,
                currentProcessId: -1,
                currentProcessStartedAtUtcTicks: -1,
                currentLease: lease);

            Assert.Multiple(() => {
                Assert.That(decision.Disposition, Is.EqualTo(WorkspaceOpenDisposition.AlreadyOpenHere));
                Assert.That(decision.Lease, Is.Null);
                Assert.That(decision.ExistingOwner, Is.Null);
            });
        }
    }

    [Test]
    public void ReserveOrActivate_WhenExistingOwnerIsRegistered_ActivatesExistingInstance() {
        using var workspace = new TestWorkspace();
        var registry = new RunningInstanceRegistry(workspace.RootPath);
        using var process = Process.GetCurrentProcess();
        var appRoot = workspace.GetPath("app-root");
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(repo);

        registry.Upsert(new RunningInstanceRecord(
            appRoot,
            repo,
            process.Id,
            process.StartTime.ToUniversalTime().Ticks,
            DateTimeOffset.UtcNow.Ticks) {
            ActiveWorkspaceFolder = repo
        });

        var activationRequests = 0;
        var coordinator = new WorkspaceOpenCoordinator(
            registry,
            (_, owner, _) => {
                activationRequests++;
                return owner.ProcessId == process.Id;
            });

        var decision = coordinator.ReserveOrActivate(
            appRoot,
            repo,
            currentProcessId: -1,
            currentProcessStartedAtUtcTicks: -1);

        Assert.Multiple(() => {
            Assert.That(decision.Disposition, Is.EqualTo(WorkspaceOpenDisposition.ActivatedExisting));
            Assert.That(activationRequests, Is.GreaterThan(0));
            Assert.That(decision.Lease, Is.Null);
            Assert.That(decision.ExistingOwner, Is.Not.Null);
            Assert.That(decision.ExistingOwner!.ProcessId, Is.EqualTo(process.Id));
        });
    }

    [Test]
    public void ReserveOrActivate_WhenWorkspaceIsFree_ReturnsLeaseForLocalOpen() {
        using var workspace = new TestWorkspace();
        var appRoot = workspace.GetPath("app-root");
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(repo);

        var coordinator = new WorkspaceOpenCoordinator(new RunningInstanceRegistry(workspace.RootPath));
        var decision = coordinator.ReserveOrActivate(
            appRoot,
            repo,
            currentProcessId: -1,
            currentProcessStartedAtUtcTicks: -1);

        Assert.Multiple(() => {
            Assert.That(decision.Disposition, Is.EqualTo(WorkspaceOpenDisposition.OpenHere));
            Assert.That(decision.Lease, Is.Not.Null);
            Assert.That(decision.ExistingOwner, Is.Null);
        });

        decision.Lease?.Dispose();
    }

    [Test]
    public void ReserveOrActivate_WhenExistingOwnerCannotBeActivated_DoesNotOpenDuplicate() {
        using var workspace = new TestWorkspace();
        var registry = new RunningInstanceRegistry(workspace.RootPath);
        using var process = Process.GetCurrentProcess();
        var appRoot = workspace.GetPath("app-root");
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(repo);

        registry.Upsert(new RunningInstanceRecord(
            appRoot,
            repo,
            process.Id,
            process.StartTime.ToUniversalTime().Ticks,
            DateTimeOffset.UtcNow.Ticks) {
            ActiveWorkspaceFolder = repo
        });

        var activationRequests = 0;
        var coordinator = new WorkspaceOpenCoordinator(
            registry,
            (_, _, _) => {
                activationRequests++;
                return false;
            });

        var decision = coordinator.ReserveOrActivate(
            appRoot,
            repo,
            currentProcessId: -1,
            currentProcessStartedAtUtcTicks: -1);

        Assert.Multiple(() => {
            Assert.That(decision.Disposition, Is.EqualTo(WorkspaceOpenDisposition.Blocked));
            Assert.That(activationRequests, Is.GreaterThan(0));
            Assert.That(decision.Lease, Is.Null);
            Assert.That(decision.ExistingOwner, Is.Not.Null);
        });
    }
}
