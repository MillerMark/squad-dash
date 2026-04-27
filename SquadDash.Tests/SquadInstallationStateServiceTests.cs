using System.IO;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class SquadInstallationStateServiceTests {
    [TestCase(false, false, false)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, true, true)]
    public void GetState_RequiresWorkspaceInitializationAndLocalCli(
        bool hasTeamFile,
        bool hasLocalCli,
        bool expectedInstalled) {
        using var workspace = new TestWorkspace();

        if (hasTeamFile)
            workspace.CreateFile(Path.Combine(".squad", "team.md"), "# Team");

        if (hasLocalCli)
            workspace.CreateFile(Path.Combine("node_modules", ".bin", "squad.cmd"), "@echo off");

        var service = new SquadInstallationStateService();

        var state = service.GetState(workspace.RootPath + Path.DirectorySeparatorChar);

        Assert.Multiple(() => {
            Assert.That(state.ActiveDirectory, Is.EqualTo(workspace.RootPath));
            Assert.That(state.IsWorkspaceInitialized, Is.EqualTo(hasTeamFile));
            Assert.That(state.HasLocalCliCommand, Is.EqualTo(hasLocalCli));
            Assert.That(state.IsSquadInstalledForActiveDirectory, Is.EqualTo(expectedInstalled));
        });
    }

    [Test]
    public void GetState_DetectsPackageManifestSeparately() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile("package.json", "{}");

        var service = new SquadInstallationStateService();

        var state = service.GetState(workspace.RootPath);

        Assert.That(state.HasPackageManifest, Is.True);
        Assert.That(state.IsSquadInstalledForActiveDirectory, Is.False);
    }
}
