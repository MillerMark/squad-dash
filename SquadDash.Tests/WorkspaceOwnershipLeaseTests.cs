namespace SquadDash.Tests;

[TestFixture]
internal sealed class WorkspaceOwnershipLeaseTests {
    [Test]
    public void TryAcquire_WhenFree_ReturnsTrue() {
        using var workspace = new TestWorkspace();
        var appRoot = workspace.GetPath("app");
        var folder = workspace.GetPath("repo");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(folder);

        var acquired = WorkspaceOwnershipLease.TryAcquire(appRoot, folder, out var lease);
        using (lease) {
            Assert.That(acquired, Is.True);
            Assert.That(lease, Is.Not.Null);
        }
    }

    [Test]
    public void TryAcquire_WhenAlreadyHeld_ReturnsFalse() {
        using var workspace = new TestWorkspace();
        var appRoot = workspace.GetPath("app");
        var folder = workspace.GetPath("repo");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(folder);

        WorkspaceOwnershipLease.TryAcquire(appRoot, folder, out var lease1);
        using (lease1) {
            var acquired = WorkspaceOwnershipLease.TryAcquire(appRoot, folder, out var lease2);

            Assert.That(acquired, Is.False);
            Assert.That(lease2, Is.Null);
        }
    }

    [Test]
    public void TryAcquire_AfterDispose_CanBeAcquiredAgain() {
        using var workspace = new TestWorkspace();
        var appRoot = workspace.GetPath("app");
        var folder = workspace.GetPath("repo");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(folder);

        WorkspaceOwnershipLease.TryAcquire(appRoot, folder, out var lease1);
        lease1!.Dispose();

        var acquired = WorkspaceOwnershipLease.TryAcquire(appRoot, folder, out var lease2);
        using (lease2) {
            Assert.That(acquired, Is.True);
        }
    }

    [Test]
    public void Lease_ExposesNormalizedApplicationRootAndWorkspaceFolder() {
        using var workspace = new TestWorkspace();
        var appRoot = workspace.GetPath("app") + Path.DirectorySeparatorChar;
        var folder = workspace.GetPath("repo") + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(folder);

        WorkspaceOwnershipLease.TryAcquire(appRoot, folder, out var lease);
        using (lease) {
            Assert.Multiple(() => {
                Assert.That(lease!.ApplicationRoot,
                    Is.EqualTo(WorkspaceOwnershipLease.NormalizePath(appRoot)));
                Assert.That(lease.WorkspaceFolder,
                    Is.EqualTo(WorkspaceOwnershipLease.NormalizePath(folder)));
            });
        }
    }

    [Test]
    public void Matches_ReturnsTrueForSamePaths() {
        using var workspace = new TestWorkspace();
        var appRoot = workspace.GetPath("app");
        var folder = workspace.GetPath("repo");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(folder);

        WorkspaceOwnershipLease.TryAcquire(appRoot, folder, out var lease);
        using (lease) {
            Assert.That(lease!.Matches(appRoot, folder), Is.True);
        }
    }

    [Test]
    public void Matches_ReturnsTrueRegardlessOfCase() {
        using var workspace = new TestWorkspace();
        var appRoot = workspace.GetPath("App");
        var folder = workspace.GetPath("Repo");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(folder);

        WorkspaceOwnershipLease.TryAcquire(appRoot, folder, out var lease);
        using (lease) {
            Assert.That(lease!.Matches(appRoot.ToUpperInvariant(), folder.ToUpperInvariant()), Is.True);
        }
    }

    [Test]
    public void Matches_ReturnsFalseForDifferentWorkspaceFolder() {
        using var workspace = new TestWorkspace();
        var appRoot = workspace.GetPath("app");
        var folder = workspace.GetPath("repo");
        var otherFolder = workspace.GetPath("other-repo");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(otherFolder);

        WorkspaceOwnershipLease.TryAcquire(appRoot, folder, out var lease);
        using (lease) {
            Assert.That(lease!.Matches(appRoot, otherFolder), Is.False);
        }
    }

    [Test]
    public void Matches_ReturnsFalseForDifferentApplicationRoot() {
        using var workspace = new TestWorkspace();
        var appRoot = workspace.GetPath("app");
        var otherAppRoot = workspace.GetPath("other-app");
        var folder = workspace.GetPath("repo");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(otherAppRoot);
        Directory.CreateDirectory(folder);

        WorkspaceOwnershipLease.TryAcquire(appRoot, folder, out var lease);
        using (lease) {
            Assert.That(lease!.Matches(otherAppRoot, folder), Is.False);
        }
    }

    [Test]
    public void NormalizePath_MatchesStartupWorkspaceResolverNormalization() {
        var path = Path.Combine(Path.GetTempPath(), "SomeRepo") + Path.DirectorySeparatorChar;

        Assert.That(
            WorkspaceOwnershipLease.NormalizePath(path),
            Is.EqualTo(StartupWorkspaceResolver.NormalizePath(path)));
    }
}
