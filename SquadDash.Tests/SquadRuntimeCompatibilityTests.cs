using System.IO;
using System.Text.Json.Nodes;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class SquadRuntimeCompatibilityTests {
    [Test]
    public void Repair_FailsWhenNodeModulesDirectoryIsMissing() {
        using var workspace = new TestWorkspace();

        var result = SquadRuntimeCompatibility.Repair(workspace.RootPath, "Workspace Squad CLI");

        Assert.Multiple(() => {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("dependencies were not found"));
        });
    }

    [Test]
    public void Repair_PatchesBrokenDependencies() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(
            Path.Combine("node_modules", "vscode-jsonrpc", "package.json"),
            """
            {
              "name": "vscode-jsonrpc",
              "exports": {
                ".": {
                  "default": "./lib/node/main.js"
                }
              }
            }
            """);
        workspace.CreateFile(
            Path.Combine("node_modules", "@github", "copilot-sdk", "dist", "session.js"),
            "import rpc from \"vscode-jsonrpc/node\";\n");

        var result = SquadRuntimeCompatibility.Repair(workspace.RootPath, "Workspace Squad CLI");
        var packageJson = JsonNode.Parse(File.ReadAllText(
            workspace.GetPath("node_modules", "vscode-jsonrpc", "package.json")))!;
        var sessionJs = File.ReadAllText(
            workspace.GetPath("node_modules", "@github", "copilot-sdk", "dist", "session.js"));

        Assert.Multiple(() => {
            Assert.That(result.Success, Is.True);
            Assert.That(result.StandardOutput, Does.Contain("Patched vscode-jsonrpc exports"));
            Assert.That(result.StandardOutput, Does.Contain("Patched @github/copilot-sdk session.js import"));
            Assert.That(packageJson["exports"]?["./node"], Is.Not.Null);
            Assert.That(packageJson["exports"]?["./node.js"], Is.Not.Null);
            Assert.That(sessionJs, Does.Contain("\"vscode-jsonrpc/node.js\""));
        });
    }

    [Test]
    public void Repair_ReportsNoOpWhenDependenciesAreAlreadyPatched() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(
            Path.Combine("node_modules", "vscode-jsonrpc", "package.json"),
            """
            {
              "exports": {
                ".": {
                  "default": "./lib/node/main.js"
                },
                "./node": {
                  "node": "./lib/node/main.js"
                },
                "./node.js": {
                  "node": "./lib/node/main.js"
                }
              }
            }
            """);
        workspace.CreateFile(
            Path.Combine("node_modules", "@github", "copilot-sdk", "dist", "session.js"),
            "import rpc from \"vscode-jsonrpc/node.js\";\n");

        var result = SquadRuntimeCompatibility.Repair(workspace.RootPath, "Workspace Squad CLI");

        Assert.Multiple(() => {
            Assert.That(result.Success, Is.True);
            Assert.That(result.StandardOutput, Does.Contain("already patched"));
        });
    }

    [TestCase("0.11.9", false)]
    [TestCase("0.12.0", true)]
    [TestCase("v0.12.0", true)]
    [TestCase("0.12.5-insider.1", true)]
    [TestCase(null, false)]
    [TestCase("unknown", false)]
    public void Supports012Capabilities_UsesSharedVersionBoundary(string? version, bool expected) {
        Assert.Multiple(() => {
            Assert.That(SquadRuntimeCompatibility.SupportsUpdateCheckJson(version), Is.EqualTo(expected));
            Assert.That(SquadRuntimeCompatibility.SupportsModelsRefresh(version), Is.EqualTo(expected));
            Assert.That(SquadRuntimeCompatibility.SupportsWorkspaceUpgradeDryRunPreview(version), Is.EqualTo(expected));
            Assert.That(SquadRuntimeCompatibility.SupportsReasoningEffortPassThrough(version), Is.EqualTo(expected));
        });
    }
}
