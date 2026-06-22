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

    [Test]
    public void GetState_UsesRemoteTeamRootFromSquadConfig() {
        using var workspace = new TestWorkspace();
        var remoteTeamRoot = workspace.GetPath("remote-state", ".squad");
        Directory.CreateDirectory(remoteTeamRoot);
        workspace.CreateFile(Path.Combine(".squad", "config.json"),
            $$"""
              {
                "version": 1,
                "teamRoot": "{{Path.GetRelativePath(workspace.RootPath, remoteTeamRoot).Replace('\\', '/')}}",
                "stateBackend": "orphan"
              }
              """);
        workspace.CreateFile(Path.Combine("remote-state", ".squad", "team.md"), "# Remote Team");
        workspace.CreateFile(Path.Combine("node_modules", ".bin", "squad.cmd"), "@echo off");

        var service = new SquadInstallationStateService();

        var state = service.GetState(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(state.IsWorkspaceInitialized, Is.True);
            Assert.That(state.IsSquadInstalledForActiveDirectory, Is.True);
            Assert.That(state.UsesRemoteTeamRoot, Is.True);
            Assert.That(state.StateBackend, Is.EqualTo("orphan"));
            Assert.That(state.SquadFolderPath, Is.EqualTo(remoteTeamRoot));
            Assert.That(state.TeamFilePath, Is.EqualTo(Path.Combine(remoteTeamRoot, "team.md")));
        });
    }

    [Test]
    public void GetState_UsesExternalStateLocationFromSquadConfig() {
        using var workspace = new TestWorkspace();
        var originalAppData = Environment.GetEnvironmentVariable("APPDATA");
        var appData = workspace.GetPath("appdata");
        Environment.SetEnvironmentVariable("APPDATA", appData);

        try {
            var externalStateDir = Path.Combine(appData, "squad", "projects", "external-repo");
            Directory.CreateDirectory(externalStateDir);
            workspace.CreateFile(Path.Combine(".squad", "config.json"),
                """
                {
                  "version": 1,
                  "teamRoot": ".",
                  "projectKey": "external-repo",
                  "stateLocation": "external"
                }
                """);
            File.WriteAllText(Path.Combine(externalStateDir, "team.md"), "# External Team");
            workspace.CreateFile(Path.Combine("node_modules", ".bin", "squad.cmd"), "@echo off");

            var service = new SquadInstallationStateService();

            var state = service.GetState(workspace.RootPath);

            Assert.Multiple(() => {
                Assert.That(state.IsWorkspaceInitialized, Is.True);
                Assert.That(state.IsSquadInstalledForActiveDirectory, Is.True);
                Assert.That(state.UsesRemoteTeamRoot, Is.True);
                Assert.That(state.StateLocation, Is.EqualTo("external"));
                Assert.That(state.ProjectKey, Is.EqualTo("external-repo"));
                Assert.That(state.SquadFolderPath, Is.EqualTo(externalStateDir));
                Assert.That(state.TeamFilePath, Is.EqualTo(Path.Combine(externalStateDir, "team.md")));
            });
        }
        finally {
            Environment.SetEnvironmentVariable("APPDATA", originalAppData);
        }
    }

    [Test]
    public void GetState_DoesNotTreatExternalStateMarkerWithoutProjectKeyAsRepoRoot() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(Path.Combine(".squad", "config.json"),
            """
            {
              "version": 1,
              "teamRoot": ".",
              "stateLocation": "external"
            }
            """);
        workspace.CreateFile(Path.Combine(".squad", "team.md"), "# Local Marker Team");
        workspace.CreateFile(Path.Combine("node_modules", ".bin", "squad.cmd"), "@echo off");

        var service = new SquadInstallationStateService();

        var state = service.GetState(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(state.IsWorkspaceInitialized, Is.True);
            Assert.That(state.IsSquadInstalledForActiveDirectory, Is.True);
            Assert.That(state.SquadFolderPath, Is.EqualTo(workspace.GetPath(".squad")));
            Assert.That(state.TeamFilePath, Is.EqualTo(workspace.GetPath(".squad", "team.md")));
        });
    }

    [Test]
    public void GetState_IgnoresRemoteTeamRootConfigWithoutVersion() {
        using var workspace = new TestWorkspace();
        var remoteTeamRoot = workspace.GetPath("remote-state", ".squad");
        Directory.CreateDirectory(remoteTeamRoot);
        workspace.CreateFile(Path.Combine(".squad", "config.json"),
            $$"""
              {
                "teamRoot": "{{Path.GetRelativePath(workspace.RootPath, remoteTeamRoot).Replace('\\', '/')}}"
              }
              """);
        workspace.CreateFile(Path.Combine("remote-state", ".squad", "team.md"), "# Remote Team");

        var service = new SquadInstallationStateService();

        var state = service.GetState(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(state.UsesRemoteTeamRoot, Is.False);
            Assert.That(state.SquadFolderPath, Is.EqualTo(workspace.GetPath(".squad")));
            Assert.That(state.IsWorkspaceInitialized, Is.False);
        });
    }
}
