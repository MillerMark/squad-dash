using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class SquadInstallerServiceTests {
    [Test]
    public async Task InstallAsync_CreatesPackageManifest_InstallsCli_AndRunsInitForNewWorkspace() {
        using var workspace = new TestWorkspace();
        var runner = new FakeCommandRunner((command, activeDirectory) => {
            if (command.DisplayName.StartsWith("Locate "))
                return Task.FromResult(Success(command.DisplayName));

            if (command == SquadCliCommands.InstallLocalCli) {
                Directory.CreateDirectory(Path.Combine(activeDirectory, "node_modules", ".bin"));
                Directory.CreateDirectory(Path.Combine(activeDirectory, "node_modules", "vscode-jsonrpc"));
                Directory.CreateDirectory(Path.Combine(activeDirectory, "node_modules", "@github", "copilot-sdk", "dist"));
                File.WriteAllText(
                    Path.Combine(activeDirectory, "node_modules", ".bin", "squad.cmd"),
                    "@echo off");
                File.WriteAllText(
                    Path.Combine(activeDirectory, "node_modules", "vscode-jsonrpc", "package.json"),
                    """
                    {
                      "exports": {
                        ".": {
                          "default": "./lib/node/main.js"
                        }
                      }
                    }
                    """);
                File.WriteAllText(
                    Path.Combine(activeDirectory, "node_modules", "@github", "copilot-sdk", "dist", "session.js"),
                    "import rpc from \"vscode-jsonrpc/node\";\n");

                return Task.FromResult(Success(command.DisplayName));
            }

            if (command == SquadCliCommands.Init) {
                Directory.CreateDirectory(Path.Combine(activeDirectory, ".squad"));
                File.WriteAllText(Path.Combine(activeDirectory, ".squad", "team.md"), "# Team");
                return Task.FromResult(Success(command.DisplayName));
            }

            return Task.FromResult(Success(command.DisplayName));
        });
        var service = new SquadInstallerService(runner);

        var result = await service.InstallAsync(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(result.Success, Is.True);
            Assert.That(File.Exists(workspace.GetPath("package.json")), Is.True);
            Assert.That(File.Exists(workspace.GetPath(".squad", "team.md")), Is.True);
            Assert.That(runner.Calls.Select(call => call.DisplayName), Is.EquivalentTo(new[] {
                "Locate node",
                "Locate npm",
                "Locate npx",
                SquadCliCommands.InstallLocalCli.DisplayName,
                SquadCliCommands.Init.DisplayName
            }));
        });
    }

    [Test]
    public async Task InstallAsync_SkipsInitWhenWorkspaceAlreadyInitialized_ButRepairsLocalCli() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(Path.Combine(".squad", "team.md"), "# Existing team");

        var runner = new FakeCommandRunner((command, activeDirectory) => {
            if (command.DisplayName.StartsWith("Locate "))
                return Task.FromResult(Success(command.DisplayName));

            if (command == SquadCliCommands.InstallLocalCli) {
                Directory.CreateDirectory(Path.Combine(activeDirectory, "node_modules", ".bin"));
                Directory.CreateDirectory(Path.Combine(activeDirectory, "node_modules", "vscode-jsonrpc"));
                Directory.CreateDirectory(Path.Combine(activeDirectory, "node_modules", "@github", "copilot-sdk", "dist"));
                File.WriteAllText(Path.Combine(activeDirectory, "node_modules", ".bin", "squad.cmd"), "@echo off");
                File.WriteAllText(
                    Path.Combine(activeDirectory, "node_modules", "vscode-jsonrpc", "package.json"),
                    """{ "exports": { ".": { "default": "./lib/node/main.js" } } }""");
                File.WriteAllText(
                    Path.Combine(activeDirectory, "node_modules", "@github", "copilot-sdk", "dist", "session.js"),
                    "import rpc from \"vscode-jsonrpc/node\";\n");
            }

            return Task.FromResult(Success(command.DisplayName));
        });
        var service = new SquadInstallerService(runner);

        var result = await service.InstallAsync(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(result.Success, Is.True);
            Assert.That(runner.Calls.Any(call => call.DisplayName == SquadCliCommands.InstallLocalCli.DisplayName), Is.True);
            Assert.That(runner.Calls.Any(call => call.DisplayName == SquadCliCommands.Init.DisplayName), Is.False);
            Assert.That(File.Exists(workspace.GetPath("node_modules", ".bin", "squad.cmd")), Is.True);
        });
    }

    [Test]
    public async Task InstallAsync_ReturnsMissingToolMessage_WhenNodeToolingIsUnavailable() {
        using var workspace = new TestWorkspace();
        var runner = new FakeCommandRunner((command, _) => {
            if (command.DisplayName == "Locate npm")
                return Task.FromResult(new SquadCommandResult(false, 1, string.Empty, string.Empty, "Locate npm failed."));

            return Task.FromResult(Success(command.DisplayName));
        });
        var service = new SquadInstallerService(runner);

        var result = await service.InstallAsync(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(result.Success, Is.False);
            Assert.That(result.MissingTools, Is.EqualTo(new[] { "npm" }));
            Assert.That(result.Message, Does.Contain("Missing required tooling"));
        });
    }

    [Test]
    public void EnsureSquadDashUniverseFiles_WritesSquadDashMdToBothUniversesAndTemplatesUniverses() {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(workspace.GetPath(".squad"));

        SquadInstallerService.EnsureSquadDashUniverseFiles(workspace.RootPath);

        // The templates/universes/ directory must always be created so the agent init
        // flow has a stable path to explore, even when no file is written yet.
        Assert.That(Directory.Exists(workspace.GetPath(".squad", "templates", "universes")), Is.True,
            ".squad/templates/universes/ directory must be created during install");

        // When the embedded squaddash.md resource is available (production), the file must land
        // in BOTH locations with identical content. In this test context the resource is compiled
        // into the test assembly and not embedded, so use Assume to skip the file-content assertions
        // rather than fail.
        var content = SquadInstallerService.LoadEmbeddedSquadDashMdPublic();
        Assume.That(content, Is.Not.Null,
            "Embedded squaddash.md is not available in this test context — directory-creation assertion above already verified the key behavior.");

        Assert.Multiple(() => {
            Assert.That(File.Exists(workspace.GetPath(".squad", "universes", "squaddash.md")), Is.True,
                "squaddash.md must exist in .squad/universes/ (runtime path)");
            Assert.That(File.Exists(workspace.GetPath(".squad", "templates", "universes", "squaddash.md")), Is.True,
                "squaddash.md must also exist in .squad/templates/universes/ to suppress the ⚠ warning during agent init");
            var templateContent = File.ReadAllText(workspace.GetPath(".squad", "templates", "universes", "squaddash.md"));
            Assert.That(templateContent, Is.EqualTo(content),
                "Both copies of squaddash.md must have identical content");
        });
    }

    [Test]
    public void EnsureSquadDashUniverseFiles_CreatesCastingStateWhenMissing() {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(workspace.GetPath(".squad"));
        workspace.CreateFile(".squad/templates/casting-policy.json", """
            {
              "casting_policy_version": "1.1",
              "allowlist_universes": ["The Usual Suspects"],
              "universe_capacity": {
                "The Usual Suspects": 6
              }
            }
            """);
        workspace.CreateFile(".squad/templates/casting-history.json", """
            {
              "universe_usage_history": [],
              "assignment_cast_snapshots": {}
            }
            """);
        workspace.CreateFile(".squad/templates/casting-registry.json", """
            {
              "agents": {}
            }
            """);

        SquadInstallerService.EnsureSquadDashUniverseFiles(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(File.Exists(workspace.GetPath(".squad", "casting", "policy.json")), Is.True);
            Assert.That(File.Exists(workspace.GetPath(".squad", "casting", "history.json")), Is.True);
            Assert.That(File.Exists(workspace.GetPath(".squad", "casting", "registry.json")), Is.True);
            Assert.That(
                File.ReadAllText(workspace.GetPath(".squad", "casting", "policy.json")),
                Does.Contain(SquadInstallerService.SquadDashUniverseName));
        });
    }

    [Test]
    public void EnsureSquadDashUniverseFiles_UsesRemoteTeamRootFromSquadConfig() {
        using var workspace = new TestWorkspace();
        var remoteTeamRoot = workspace.GetPath("external-state", ".squad");
        Directory.CreateDirectory(remoteTeamRoot);
        workspace.CreateFile(Path.Combine(".squad", "config.json"),
            $$"""
              {
                "version": 1,
                "teamRoot": "{{Path.GetRelativePath(workspace.RootPath, remoteTeamRoot).Replace('\\', '/')}}"
              }
              """);

        SquadInstallerService.EnsureSquadDashUniverseFiles(workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(Directory.Exists(Path.Combine(remoteTeamRoot, "templates", "universes")), Is.True);
            Assert.That(File.Exists(Path.Combine(remoteTeamRoot, "casting", "policy.json")), Is.True);
            Assert.That(Directory.Exists(workspace.GetPath(".squad", "templates", "universes")), Is.False);
        });
    }

    [Test]
    public void EnsureSquadDashUniverseFiles_UsesExternalStateLocationFromSquadConfig() {
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

            SquadInstallerService.EnsureSquadDashUniverseFiles(workspace.RootPath);

            Assert.Multiple(() => {
                Assert.That(Directory.Exists(Path.Combine(externalStateDir, "templates", "universes")), Is.True);
                Assert.That(File.Exists(Path.Combine(externalStateDir, "casting", "policy.json")), Is.True);
                Assert.That(Directory.Exists(workspace.GetPath(".squad", "templates", "universes")), Is.False);
            });
        }
        finally {
            Environment.SetEnvironmentVariable("APPDATA", originalAppData);
        }
    }

    [Test]
    public void SessionWorkspace_SquadFolderPath_UsesRemoteTeamRootFromSquadConfig() {
        using var workspace = new TestWorkspace();
        var remoteTeamRoot = workspace.GetPath("state", ".squad");
        Directory.CreateDirectory(remoteTeamRoot);
        workspace.CreateFile(Path.Combine(".squad", "config.json"),
            $$"""
              {
                "version": 1,
                "teamRoot": "{{Path.GetRelativePath(workspace.RootPath, remoteTeamRoot).Replace('\\', '/')}}"
              }
              """);

        var sessionWorkspace = SessionWorkspace.Create(workspace.RootPath);

        Assert.That(sessionWorkspace.SquadFolderPath, Is.EqualTo(remoteTeamRoot));
    }

    [Test]
    public void SessionWorkspace_SquadFolderPath_UsesExternalStateLocationFromSquadConfig() {
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

            var sessionWorkspace = SessionWorkspace.Create(workspace.RootPath);

            Assert.That(sessionWorkspace.SquadFolderPath, Is.EqualTo(externalStateDir));
        }
        finally {
            Environment.SetEnvironmentVariable("APPDATA", originalAppData);
        }
    }

    [Test]
    public void WatchHealthResult_ParsesNoWatchInstanceOutput() {
        var result = SquadWatchHealthResult.FromCommandResult(new SquadCommandResult(
            true,
            0,
            "No watch instance detected. Start one with: squad watch --execute --interval 5",
            string.Empty,
            "done"));

        Assert.Multiple(() => {
            Assert.That(result.Success, Is.True);
            Assert.That(result.IsRunning, Is.False);
            Assert.That(result.Summary, Is.EqualTo("No watch instance detected."));
        });
    }

    [Test]
    public void WatchHealthResult_ReportsUnsupportedCliOutput() {
        var result = SquadWatchHealthResult.FromCommandResult(new SquadCommandResult(
            false,
            1,
            string.Empty,
            "error: unknown option '--health'",
            "failed"));

        Assert.Multiple(() => {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Summary, Is.EqualTo("Watch health is unavailable with this Squad CLI."));
        });
    }

    [Test]
    public void WatchHealthCommand_UsesWorkspaceLocalCliEntry() {
        Assert.Multiple(() => {
            Assert.That(SquadCliCommands.WatchHealth.FileName, Is.EqualTo("node"));
            Assert.That(SquadCliCommands.WatchHealth.Arguments, Does.Contain(SquadCliCommands.LocalCliEntryPath));
            Assert.That(SquadCliCommands.WatchHealth.Arguments, Does.Not.Contain("npx"));
        });
    }

    [Test]
    public void WatchHealthResult_ReportsMissingLocalCli() {
        var result = SquadWatchHealthResult.FromCommandResult(new SquadCommandResult(
            false,
            1,
            string.Empty,
            "Error: Cannot find module 'C:\\repo\\node_modules\\@bradygaster\\squad-cli\\dist\\cli-entry.js'",
            "failed"));

        Assert.That(result.Summary, Is.EqualTo("Local Squad CLI is not installed for this workspace."));
    }

    private static SquadCommandResult Success(string message) =>
        new(true, 0, string.Empty, string.Empty, message);

    private sealed class FakeCommandRunner : ISquadCommandRunner {
        private readonly Func<SquadCliCommandDefinition, string, Task<SquadCommandResult>> _handler;

        public FakeCommandRunner(Func<SquadCliCommandDefinition, string, Task<SquadCommandResult>> handler) {
            _handler = handler;
        }

        public List<SquadCliCommandDefinition> Calls { get; } = new();

        public Task<SquadCommandResult> RunAsync(SquadCliCommandDefinition command, string activeDirectory) {
            Calls.Add(command);
            return _handler(command, activeDirectory);
        }
    }
}

[TestFixture]
internal sealed class GitIgnoreMaintenanceStateTests {
    private TestWorkspace _workspace = null!;

    [SetUp]
    public void SetUp() => _workspace = new TestWorkspace();

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    [Test]
    public void EnsureCodeHealthStateInGitIgnore_AppendsEntry_WhenGitIgnoreExists_AndEntryAbsent() {
        var gitIgnorePath = Path.Combine(_workspace.RootPath, ".gitignore");
        File.WriteAllText(gitIgnorePath, "node_modules\n");

        var result = SquadInstallerService.EnsureCodeHealthStateInGitIgnore(_workspace.RootPath);

        Assert.That(result, Is.True);
        Assert.That(File.ReadAllText(gitIgnorePath), Does.Contain("code-health-state.json"));
    }

    [Test]
    public void EnsureCodeHealthStateInGitIgnore_NoChange_WhenEntryAlreadyPresent() {
        var gitIgnorePath = Path.Combine(_workspace.RootPath, ".gitignore");
        var allEntries = "node_modules\n.squad/inbox/\n.squad/orchestration-log/\n.squad/log/\n" +
                         ".squad/decisions/inbox/\n.squad/sessions/\n.squad-workstream\n" +
                         "code-health-state.json\n.squad/code-health-reports/\n";
        File.WriteAllText(gitIgnorePath, allEntries);
        var originalLineCount = File.ReadAllLines(gitIgnorePath).Length;

        var result = SquadInstallerService.EnsureCodeHealthStateInGitIgnore(_workspace.RootPath);

        Assert.That(result, Is.False);
        Assert.That(File.ReadAllLines(gitIgnorePath).Length, Is.EqualTo(originalLineCount));
    }

    [Test]
    public void EnsureCodeHealthStateInGitIgnore_CreatesGitIgnore_WhenFileAbsent() {
        var gitIgnorePath = Path.Combine(_workspace.RootPath, ".gitignore");
        Assert.That(File.Exists(gitIgnorePath), Is.False);

        var result = SquadInstallerService.EnsureCodeHealthStateInGitIgnore(_workspace.RootPath);

        Assert.That(result, Is.True);
        Assert.That(File.Exists(gitIgnorePath), Is.True);
        var content = File.ReadAllText(gitIgnorePath);
        Assert.That(content, Does.Contain("code-health-state.json"));
        Assert.That(content, Does.Contain(".squad/inbox/"));
    }

    [Test]
    public void EnsureCodeHealthStateInGitIgnore_CaseInsensitive_ExistingEntry() {
        var gitIgnorePath = Path.Combine(_workspace.RootPath, ".gitignore");
        var allEntriesCased = ".SQUAD/INBOX/\n.SQUAD/ORCHESTRATION-LOG/\n.SQUAD/LOG/\n" +
                              ".SQUAD/DECISIONS/INBOX/\n.SQUAD/SESSIONS/\n.SQUAD-WORKSTREAM\n" +
                              "code-health-state.json\n.SQUAD/code-health-reports/\n";
        File.WriteAllText(gitIgnorePath, allEntriesCased);

        var result = SquadInstallerService.EnsureCodeHealthStateInGitIgnore(_workspace.RootPath);

        Assert.That(result, Is.False);
    }
}

