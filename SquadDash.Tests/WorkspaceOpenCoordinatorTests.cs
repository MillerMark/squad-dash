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

    // ── Lease matching ────────────────────────────────────────────────────────

    [Test]
    public void ReserveOrActivate_WhenCurrentLeaseIsForDifferentWorkspace_ProceedsToNormalFlow() {
        // A lease for workspace-A must not short-circuit a request to open
        // workspace-B — the Matches() check is workspace-specific.
        using var workspace = new TestWorkspace();
        var registry = new RunningInstanceRegistry(workspace.RootPath);
        using var process = Process.GetCurrentProcess();
        var appRoot = workspace.GetPath("app-root");
        var repoA   = workspace.GetPath("repo-a");
        var repoB   = workspace.GetPath("repo-b");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(repoA);
        Directory.CreateDirectory(repoB);

        Assert.That(WorkspaceOwnershipLease.TryAcquire(appRoot, repoA, out var leaseForA), Is.True);
        using (leaseForA) {
            // Register a live owner for workspace-B that can be activated.
            registry.Upsert(new RunningInstanceRecord(
                appRoot, repoB,
                process.Id,
                process.StartTime.ToUniversalTime().Ticks,
                DateTimeOffset.UtcNow.Ticks) {
                ActiveWorkspaceFolder = repoB
            });

            var coordinator = new WorkspaceOpenCoordinator(
                registry,
                (_, owner, _) => owner.ProcessId == process.Id);

            // Ask for workspace-B while carrying a lease that's only for workspace-A.
            var decision = coordinator.ReserveOrActivate(
                appRoot, repoB,
                currentProcessId: -1,
                currentProcessStartedAtUtcTicks: -1,
                currentLease: leaseForA);   // wrong workspace — must NOT short-circuit

            Assert.Multiple(() => {
                Assert.That(decision.Disposition, Is.EqualTo(WorkspaceOpenDisposition.ActivatedExisting),
                    "The coordinator must proceed through normal flow when the current lease is for a different workspace.");
                Assert.That(decision.ExistingOwner, Is.Not.Null);
                Assert.That(decision.ExistingOwner!.ProcessId, Is.EqualTo(process.Id));
            });
        }
    }

    // ── Owner filtering ───────────────────────────────────────────────────────

    [Test]
    public void ReserveOrActivate_WhenOwnerHasNullActiveWorkspaceFolder_IsNotConsideredOwner() {
        // An instance that is registered but has no active workspace folder
        // (e.g. it is still on the splash screen) must not block a fresh open.
        using var workspace = new TestWorkspace();
        var registry = new RunningInstanceRegistry(workspace.RootPath);
        using var process = Process.GetCurrentProcess();
        var appRoot = workspace.GetPath("app-root");
        var repo    = workspace.GetPath("repo");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(repo);

        registry.Upsert(new RunningInstanceRecord(
            appRoot, repo,
            process.Id,
            process.StartTime.ToUniversalTime().Ticks,
            DateTimeOffset.UtcNow.Ticks) {
            ActiveWorkspaceFolder = null    // ← no workspace active
        });

        var activationRequests = 0;
        var coordinator = new WorkspaceOpenCoordinator(
            registry,
            (_, _, _) => { activationRequests++; return true; });

        var decision = coordinator.ReserveOrActivate(
            appRoot, repo,
            currentProcessId: -1,
            currentProcessStartedAtUtcTicks: -1);

        Assert.Multiple(() => {
            Assert.That(decision.Disposition, Is.EqualTo(WorkspaceOpenDisposition.OpenHere));
            Assert.That(decision.Lease, Is.Not.Null);
            Assert.That(activationRequests, Is.Zero,
                "Activation must not be attempted for an instance that has no active workspace.");
        });

        decision.Lease?.Dispose();
    }

    [Test]
    public void ReserveOrActivate_WhenOwnerIsForDifferentWorkspaceFolder_IsNotConsideredOwnerForTarget() {
        // An instance owning workspace-B must not prevent workspace-A from opening.
        using var workspace = new TestWorkspace();
        var registry = new RunningInstanceRegistry(workspace.RootPath);
        using var process = Process.GetCurrentProcess();
        var appRoot = workspace.GetPath("app-root");
        var repoA   = workspace.GetPath("repo-a");
        var repoB   = workspace.GetPath("repo-b");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(repoA);
        Directory.CreateDirectory(repoB);

        // Register a live instance whose active workspace is repo-b, not repo-a.
        registry.Upsert(new RunningInstanceRecord(
            appRoot, repoA,
            process.Id,
            process.StartTime.ToUniversalTime().Ticks,
            DateTimeOffset.UtcNow.Ticks) {
            ActiveWorkspaceFolder = repoB   // ← owns repo-b
        });

        var activationRequests = 0;
        var coordinator = new WorkspaceOpenCoordinator(
            registry,
            (_, _, _) => { activationRequests++; return true; });

        // We want to open repo-a.
        var decision = coordinator.ReserveOrActivate(
            appRoot, repoA,
            currentProcessId: -1,
            currentProcessStartedAtUtcTicks: -1);

        Assert.Multiple(() => {
            Assert.That(decision.Disposition, Is.EqualTo(WorkspaceOpenDisposition.OpenHere));
            Assert.That(decision.Lease, Is.Not.Null);
            Assert.That(activationRequests, Is.Zero,
                "An owner of a different workspace must not be considered an owner of the target workspace.");
        });

        decision.Lease?.Dispose();
    }

    [Test]
    public void ReserveOrActivate_CurrentProcessIsNotConsideredItsOwnOwner() {
        // FindExistingOwner must exclude the calling process so an instance
        // never treats itself as a competing owner and blocks its own startup.
        using var workspace = new TestWorkspace();
        var registry = new RunningInstanceRegistry(workspace.RootPath);
        using var process = Process.GetCurrentProcess();
        var appRoot = workspace.GetPath("app-root");
        var repo    = workspace.GetPath("repo");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(repo);

        var actualStartTicks = process.StartTime.ToUniversalTime().Ticks;
        registry.Upsert(new RunningInstanceRecord(
            appRoot, repo,
            process.Id,
            actualStartTicks,
            DateTimeOffset.UtcNow.Ticks) {
            ActiveWorkspaceFolder = repo
        });

        var activationRequests = 0;
        var coordinator = new WorkspaceOpenCoordinator(
            registry,
            (_, _, _) => { activationRequests++; return false; });

        // Call with the exact same PID + start ticks — the process must exclude itself.
        var decision = coordinator.ReserveOrActivate(
            appRoot, repo,
            currentProcessId: process.Id,
            currentProcessStartedAtUtcTicks: actualStartTicks);

        Assert.Multiple(() => {
            Assert.That(decision.Disposition, Is.EqualTo(WorkspaceOpenDisposition.OpenHere),
                "A process must not consider itself a competing owner.");
            Assert.That(activationRequests, Is.Zero,
                "No activation attempt should be made when the only candidate is the calling process itself.");
            Assert.That(decision.Lease, Is.Not.Null);
        });

        decision.Lease?.Dispose();
    }

    // ── Activation failure ────────────────────────────────────────────────────

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
