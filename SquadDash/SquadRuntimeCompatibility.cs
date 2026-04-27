using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Generic;

namespace SquadDash;

internal static class SquadRuntimeCompatibility {
    public static SquadCommandResult Repair(string rootDirectory, string displayName) {
        var normalizedDirectory = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var nodeModulesDirectory = Path.Combine(normalizedDirectory, "node_modules");
        var output = new StringBuilder();

        if (!Directory.Exists(nodeModulesDirectory)) {
            return new SquadCommandResult(
                false,
                null,
                string.Empty,
                string.Empty,
                $"{displayName} dependencies were not found under {nodeModulesDirectory}.");
        }

        try {
            var patchedAnything = false;
            patchedAnything |= PatchVscodeJsonrpcExports(nodeModulesDirectory, output);
            patchedAnything |= PatchCopilotSdkSessionImport(nodeModulesDirectory, output);

            if (!patchedAnything && output.Length == 0)
                output.AppendLine("No compatibility patches were needed.");

            return new SquadCommandResult(
                true,
                0,
                output.ToString().TrimEnd(),
                string.Empty,
                $"{displayName} compatibility checks completed.");
        }
        catch (Exception ex) {
            return new SquadCommandResult(
                false,
                null,
                output.ToString().TrimEnd(),
                ex.Message,
                $"{displayName} compatibility repair failed: {ex.Message}");
        }
    }

    private static bool PatchVscodeJsonrpcExports(string nodeModulesDirectory, StringBuilder output) {
        var packageJsonPath = Path.Combine(nodeModulesDirectory, "vscode-jsonrpc", "package.json");
        if (!File.Exists(packageJsonPath)) {
            output.AppendLine("vscode-jsonrpc package was not found.");
            return false;
        }

        var packageNode = JsonNode.Parse(File.ReadAllText(packageJsonPath))?.AsObject();
        if (packageNode is null)
            throw new InvalidOperationException($"Unable to parse {packageJsonPath}.");

        var exportsNode = packageNode["exports"] as JsonObject;
        if (exportsNode is not null &&
            exportsNode.ContainsKey("./node") &&
            exportsNode.ContainsKey("./node.js")) {
            output.AppendLine("vscode-jsonrpc exports are already patched.");
            return false;
        }

        packageNode["exports"] = new JsonObject {
            ["."] = new JsonObject {
                ["types"] = "./lib/common/api.d.ts",
                ["default"] = "./lib/node/main.js"
            },
            ["./node"] = new JsonObject {
                ["node"] = "./lib/node/main.js",
                ["types"] = "./lib/node/main.d.ts"
            },
            ["./node.js"] = new JsonObject {
                ["node"] = "./lib/node/main.js",
                ["types"] = "./lib/node/main.d.ts"
            },
            ["./browser"] = new JsonObject {
                ["types"] = "./lib/browser/main.d.ts",
                ["browser"] = "./lib/browser/main.js"
            }
        };

        File.WriteAllText(
            packageJsonPath,
            packageNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
            Encoding.UTF8);
        output.AppendLine("Patched vscode-jsonrpc exports for Node ESM compatibility.");
        return true;
    }

    private static bool PatchCopilotSdkSessionImport(string nodeModulesDirectory, StringBuilder output) {
        var sessionJsPath = Path.Combine(nodeModulesDirectory, "@github", "copilot-sdk", "dist", "session.js");
        if (!File.Exists(sessionJsPath)) {
            output.AppendLine("@github/copilot-sdk session.js was not found.");
            return false;
        }

        var content = File.ReadAllText(sessionJsPath);
        const string brokenImport = "\"vscode-jsonrpc/node\"";
        const string fixedImport = "\"vscode-jsonrpc/node.js\"";

        if (!content.Contains(brokenImport, StringComparison.Ordinal)) {
            output.AppendLine("@github/copilot-sdk session.js is already patched.");
            return false;
        }

        File.WriteAllText(
            sessionJsPath,
            content.Replace(brokenImport, fixedImport, StringComparison.Ordinal),
            Encoding.UTF8);
        output.AppendLine("Patched @github/copilot-sdk session.js import.");
        return true;
    }
}
