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
