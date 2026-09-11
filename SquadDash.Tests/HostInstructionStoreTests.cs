namespace SquadDash.Tests;

[TestFixture]
internal sealed class HostInstructionStoreTests
{
    [Test]
    public void EnsureAvailable_SharesContentAddressedCanonicalCopyAcrossWorkspaces()
    {
        using var fixture = new InstructionFixture();

        var first = HostInstructionStore.EnsureAvailable(
            fixture.FirstSquadFolder,
            HostInstructionKind.DecomposePlanning,
            fixture.CentralRoot);
        var second = HostInstructionStore.EnsureAvailable(
            fixture.SecondSquadFolder,
            HostInstructionKind.DecomposePlanning,
            fixture.CentralRoot);

        Assert.Multiple(() =>
        {
            Assert.That(first.Sha256, Has.Length.EqualTo(64));
            Assert.That(second.Sha256, Is.EqualTo(first.Sha256));
            Assert.That(second.CentralPath, Is.EqualTo(first.CentralPath));
            Assert.That(second.WorkspaceProjectionPath, Is.Not.EqualTo(first.WorkspaceProjectionPath));
            Assert.That(File.ReadAllText(first.CentralPath), Is.EqualTo(File.ReadAllText(first.WorkspaceProjectionPath)));
            Assert.That(File.ReadAllText(second.WorkspaceProjectionPath), Is.EqualTo(File.ReadAllText(first.CentralPath)));
            Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(first.CentralPath)!, "manifest.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(first.WorkspaceProjectionPath)!, "manifest.json")), Is.True);
        });
    }

    [Test]
    public void EnsureAvailable_RefreshesStaleLegacyProjectionWithoutUsingItAsRuntimeSource()
    {
        using var fixture = new InstructionFixture();
        var legacyPath = Path.Combine(fixture.FirstSquadFolder, "instructions", "decompose-planning.md");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath, "<!-- schema-version: 8 -->\nstale");

        var location = HostInstructionStore.EnsureAvailable(
            fixture.FirstSquadFolder,
            HostInstructionKind.DecomposePlanning,
            fixture.CentralRoot);

        Assert.Multiple(() =>
        {
            Assert.That(location.WorkspaceProjectionPath, Is.Not.EqualTo(legacyPath));
            Assert.That(File.ReadAllText(location.WorkspaceProjectionPath), Does.Contain("schema-version: 9"));
            Assert.That(File.ReadAllText(legacyPath), Is.EqualTo(File.ReadAllText(location.WorkspaceProjectionPath)));
        });
    }

    [Test]
    public void EnsureAvailable_PreservesCustomizedWorkspacePlanningCharter()
    {
        using var fixture = new InstructionFixture();
        var customCharterPath = Path.Combine(fixture.FirstSquadFolder, "agents", "rory", "charter.md");
        Directory.CreateDirectory(Path.GetDirectoryName(customCharterPath)!);
        File.WriteAllText(customCharterPath, "# Rory — Workspace-specific custom planning notes");

        var location = HostInstructionStore.EnsureAvailable(
            fixture.FirstSquadFolder,
            HostInstructionKind.PlanningSpecialistCharter,
            fixture.CentralRoot);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(customCharterPath), Is.EqualTo("# Rory — Workspace-specific custom planning notes"));
            Assert.That(File.ReadAllText(location.WorkspaceProjectionPath), Does.Contain("# Planning Architect"));
            Assert.That(location.LegacyWorkspacePath, Is.Null);
        });
    }

    [Test]
    public void EnsureAvailable_CentralStoreFailureDoesNotBlockWorkspaceRuntimeProjection()
    {
        using var fixture = new InstructionFixture();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.CentralRoot)!);
        File.WriteAllText(fixture.CentralRoot, "This file prevents creation of the central directory.");

        var location = HostInstructionStore.EnsureAvailable(
            fixture.FirstSquadFolder,
            HostInstructionKind.DecomposePlanning,
            fixture.CentralRoot);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(location.CentralPath), Is.False);
            Assert.That(File.Exists(location.WorkspaceProjectionPath), Is.True);
            Assert.That(File.ReadAllText(location.WorkspaceProjectionPath), Does.Contain("schema-version: 9"));
        });
    }

    [Test]
    public void EnsureWorkspaceBundle_MaterializesEveryShippedInstructionAndLegacyCompatibilityFile()
    {
        using var fixture = new InstructionFixture();

        var locations = HostInstructionStore.EnsureWorkspaceBundle(
            fixture.FirstSquadFolder,
            fixture.CentralRoot);

        Assert.Multiple(() =>
        {
            Assert.That(locations.Select(location => location.Kind), Is.EquivalentTo(Enum.GetValues<HostInstructionKind>()));
            Assert.That(locations.All(location => File.Exists(location.CentralPath)), Is.True);
            Assert.That(locations.All(location => File.Exists(location.WorkspaceProjectionPath)), Is.True);
            Assert.That(File.Exists(Path.Combine(fixture.FirstSquadFolder, "instructions", "decompose-planning.md")), Is.True);
            Assert.That(File.Exists(Path.Combine(fixture.FirstSquadFolder, "loop-executing-plan.md")), Is.True);
        });
    }

    private sealed class InstructionFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "host-instructions-" + Guid.NewGuid().ToString("N"));

        internal string CentralRoot => Path.Combine(_root, "central");
        internal string FirstSquadFolder => Path.Combine(_root, "workspace-one", ".squad");
        internal string SecondSquadFolder => Path.Combine(_root, "workspace-two", ".squad");

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
