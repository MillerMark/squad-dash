using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class SquadCliResolutionServiceTests
{
    [Test]
    public async Task ResolveAsync_PersistedGlobalSelection_OverridesWorkspaceLocalCli()
    {
        using var workspace = new TestWorkspace();
        var localPackage = workspace.GetPath("node_modules", "@bradygaster", "squad-cli");
        var localEntry = Path.Combine(localPackage, "dist", "cli-entry.js");
        Directory.CreateDirectory(Path.GetDirectoryName(localEntry)!);
        File.WriteAllText(localEntry, "console.log('local');");

        var npmPrefix = workspace.GetPath("global-npm");
        var nodeDir = workspace.GetPath("node");
        var globalPackage = Path.Combine(npmPrefix, "node_modules", "@bradygaster", "squad-cli");
        var globalEntry = Path.Combine(globalPackage, "dist", "cli-entry.js");
        Directory.CreateDirectory(Path.GetDirectoryName(globalEntry)!);
        Directory.CreateDirectory(nodeDir);
        File.WriteAllText(Path.Combine(npmPrefix, "squad.cmd"), "@echo off");
        File.WriteAllText(Path.Combine(nodeDir, "node.exe"), "node");
        File.WriteAllText(globalEntry, "console.log('global');");
        File.WriteAllText(
            Path.Combine(globalPackage, "package.json"),
            """
            {
              "bin": {
                "squad": "dist/cli-entry.js"
              }
            }
            """);

        var environment = new FakeEnvironmentProvider(
            processPath: string.Join(";", npmPrefix, nodeDir),
            userPath: null,
            machinePath: null);
        var runner = new FakeProcessRunner((fileName, args, _) =>
        {
            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["config", "get", "prefix"]))
            {
                return new CliInvocationResult(0, npmPrefix, string.Empty, false);
            }

            if (fileName.Equals(Path.Combine(nodeDir, "node.exe"), StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual([globalEntry, "--version"]))
            {
                return new CliInvocationResult(0, "0.13.0", string.Empty, false);
            }

            if (fileName.Equals(Path.Combine(nodeDir, "node.exe"), StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual([globalEntry, "update-check", "--json"]))
            {
                return new CliInvocationResult(
                    1,
                    """
                    {
                      "current": "0.13.0",
                      "channel": "stable",
                      "latest": "0.14.1",
                      "updateAvailable": true,
                      "cacheAge": "PT20S",
                      "checkedAt": "2026-08-26T10:00:00.000Z"
                    }
                    """,
                    string.Empty,
                    false);
            }

            throw new AssertionException($"Unexpected invocation: {fileName} {string.Join(" ", args)}");
        });
        var service = new SquadCliResolutionService(
            runner,
            environment,
            new StubSelectionStore(CliSelectionMode.GlobalNpm));

        var resolved = await service.ResolveAsync(workspace.RootPath);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.SelectionMode, Is.EqualTo(CliSelectionMode.GlobalNpm));
            Assert.That(resolved.DetectedSource, Is.EqualTo(CliInstallSource.GlobalNpm));
            Assert.That(resolved.EntryScriptPath, Is.EqualTo(globalEntry));
            Assert.That(resolved.Version, Is.EqualTo("0.13.0"));
            Assert.That(resolved.UpdateInstruction.TargetVersion, Is.EqualTo("0.14.1"));
            Assert.That(resolved.UpdateInstruction.CanAutoUpdate, Is.True);
        });
    }

    [Test]
    public async Task ProbeVersionAsync_NodeScript_UsesUpdateCheckJson_WhenSupported()
    {
        var nodePath = @"C:\tools\node.exe";
        var entryPath = @"C:\tools\squad\dist\cli-entry.js";
        var runner = new FakeProcessRunner((fileName, args, _) =>
        {
            if (fileName.Equals(nodePath, StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual([entryPath, "--version"]))
            {
                return new CliInvocationResult(0, "0.12.1", string.Empty, false);
            }

            if (fileName.Equals(nodePath, StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual([entryPath, "update-check", "--json"]))
            {
                return new CliInvocationResult(
                    1,
                    """
                    {
                      "current": "0.12.1",
                      "channel": "stable",
                      "latest": "0.12.3",
                      "updateAvailable": true,
                      "cacheAge": "PT5M",
                      "checkedAt": "2026-08-26T10:00:00.000Z"
                    }
                    """,
                    string.Empty,
                    false);
            }

            throw new AssertionException($"Unexpected invocation: {fileName} {string.Join(" ", args)}");
        });
        var service = new SquadCliResolutionService(
            runner,
            new FakeEnvironmentProvider(processPath: null, userPath: null, machinePath: null));
        var candidate = new ResolvedCliCandidate(
            SelectionMode: CliSelectionMode.GlobalNpm,
            DetectedSource: CliInstallSource.GlobalNpm,
            RuntimeType: CliRuntimeType.NodeScript,
            ExecutablePath: nodePath,
            EntryScriptPath: entryPath,
            Version: null,
            IsAvailable: true,
            AgentCmdSupport: CliCapabilityStatus.Unknown,
            ProbeError: null,
            UpdateInstruction: new CliUpdateInstruction(null, false, null, null));

        var resolved = await service.ProbeVersionAsync(candidate);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Version, Is.EqualTo("0.12.1"));
            Assert.That(resolved.UpdateInstruction.TargetVersion, Is.EqualTo("0.12.3"));
            Assert.That(resolved.UpdateInstruction.CanAutoUpdate, Is.True);
            Assert.That(resolved.UpdateInstruction.UpdateCommand, Is.EqualTo("npm install -g @bradygaster/squad-cli@0.12.3"));
            Assert.That(runner.Calls.Any(call =>
                call.FileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                call.Args.SequenceEqual(["view", "@bradygaster/squad-cli", "version"])), Is.False);
        });
    }

    [Test]
    public async Task ResolveWithModeAsync_NativeSystem_UsesMergedPathOrderWithDedup()
    {
        using var workspace = new TestWorkspace();
        var processDir = workspace.GetPath("path", "process");
        var userDir = workspace.GetPath("path", "user");
        var machineDir = workspace.GetPath("path", "machine");
        Directory.CreateDirectory(processDir);
        Directory.CreateDirectory(userDir);
        Directory.CreateDirectory(machineDir);
        File.WriteAllText(Path.Combine(processDir, "squad.exe"), "process");
        File.WriteAllText(Path.Combine(userDir, "squad.exe"), "user");

        var environment = new FakeEnvironmentProvider(
            processPath: string.Join(";", processDir, userDir),
            userPath: string.Join(";", userDir, machineDir),
            machinePath: machineDir);

        var runner = new FakeProcessRunner((fileName, args, _) =>
        {
            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["config", "get", "prefix"]))
            {
                return new CliInvocationResult(1, string.Empty, "npm missing", false);
            }

            if (fileName.Equals(Path.Combine(processDir, "squad.exe"), StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["--version"]))
            {
                return new CliInvocationResult(0, "0.9.6", string.Empty, false);
            }

            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["view", "@bradygaster/squad-cli", "version"]))
            {
                return new CliInvocationResult(0, "0.9.7", string.Empty, false);
            }

            throw new AssertionException($"Unexpected invocation: {fileName} {string.Join(" ", args)}");
        });

        var service = new SquadCliResolutionService(runner, environment);
        var resolved = await service.ResolveWithModeAsync(CliSelectionMode.NativeSystem, workspace.RootPath);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.IsAvailable, Is.True);
            Assert.That(resolved.ExecutablePath, Is.EqualTo(Path.Combine(processDir, "squad.exe")));
            Assert.That(resolved.DetectedSource, Is.EqualTo(CliInstallSource.NativeOther));
        });
    }

    [Test]
    public async Task ResolveWithModeAsync_Auto_PrefersNativeExeBeforeWorkspaceAndGlobalNpm()
    {
        using var workspace = new TestWorkspace();
        var nativeDir = workspace.GetPath("native");
        var npmPrefix = workspace.GetPath("npm-prefix");
        var nodeDir = workspace.GetPath("node");
        var localPackage = workspace.GetPath("node_modules", "@bradygaster", "squad-cli");
        var localEntry = Path.Combine(localPackage, "dist", "cli-entry.js");
        Directory.CreateDirectory(Path.GetDirectoryName(localEntry)!);
        Directory.CreateDirectory(nativeDir);
        Directory.CreateDirectory(npmPrefix);
        Directory.CreateDirectory(nodeDir);
        File.WriteAllText(localEntry, "console.log('local');");
        File.WriteAllText(Path.Combine(nativeDir, "squad.exe"), "native");
        File.WriteAllText(Path.Combine(npmPrefix, "squad.cmd"), "@echo off");
        File.WriteAllText(Path.Combine(nodeDir, "node.exe"), "node");

        var environment = new FakeEnvironmentProvider(
            processPath: string.Join(";", nativeDir, npmPrefix, nodeDir),
            userPath: null,
            machinePath: null);

        var runner = new FakeProcessRunner((fileName, args, _) =>
        {
            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["config", "get", "prefix"]))
            {
                return new CliInvocationResult(0, npmPrefix, string.Empty, false);
            }

            if (fileName.Equals(Path.Combine(nativeDir, "squad.exe"), StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["--version"]))
            {
                return new CliInvocationResult(0, "0.12.1", string.Empty, false);
            }

            if (fileName.Equals(Path.Combine(nativeDir, "squad.exe"), StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["update-check", "--json"]))
            {
                return new CliInvocationResult(0, "{}", string.Empty, false);
            }

            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["view", "@bradygaster/squad-cli", "version"]))
            {
                return new CliInvocationResult(0, "0.12.2", string.Empty, false);
            }

            throw new AssertionException($"Unexpected invocation: {fileName} {string.Join(" ", args)}");
        });

        var service = new SquadCliResolutionService(runner, environment);
        var resolved = await service.ResolveWithModeAsync(CliSelectionMode.Auto, workspace.RootPath);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.IsAvailable, Is.True);
            Assert.That(resolved.DetectedSource, Is.EqualTo(CliInstallSource.NativeOther));
            Assert.That(resolved.RuntimeType, Is.EqualTo(CliRuntimeType.NativeExecutable));
            Assert.That(resolved.ExecutablePath, Is.EqualTo(Path.Combine(nativeDir, "squad.exe")));
        });
    }

    [Test]
    public async Task ResolveWithModeAsync_Auto_FallsBackToWorkspaceNpmWhenNativeMissing()
    {
        using var workspace = new TestWorkspace();
        var nodeDir = workspace.GetPath("node");
        var nodePath = Path.Combine(nodeDir, "node.exe");
        var packageRoot = workspace.GetPath("node_modules", "@bradygaster", "squad-cli");
        Directory.CreateDirectory(Path.Combine(packageRoot, "dist"));
        Directory.CreateDirectory(nodeDir);
        File.WriteAllText(nodePath, "node");
        File.WriteAllText(Path.Combine(packageRoot, "dist", "cli-entry.js"), "console.log('workspace');");

        var environment = new FakeEnvironmentProvider(
            processPath: nodeDir,
            userPath: null,
            machinePath: null);
        var runner = new FakeProcessRunner((fileName, args, _) =>
        {
            if (fileName.Equals(nodePath, StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual([Path.Combine(packageRoot, "dist", "cli-entry.js"), "--version"]))
            {
                return new CliInvocationResult(0, "0.12.1", string.Empty, false);
            }

            if (fileName.Equals(nodePath, StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual([Path.Combine(packageRoot, "dist", "cli-entry.js"), "update-check", "--json"]))
            {
                return new CliInvocationResult(0, "{}", string.Empty, false);
            }

            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["config", "get", "prefix"]))
            {
                return new CliInvocationResult(1, string.Empty, "npm prefix unavailable", false);
            }

            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["view", "@bradygaster/squad-cli", "version"]))
            {
                return new CliInvocationResult(0, "0.12.2", string.Empty, false);
            }

            throw new AssertionException($"Unexpected invocation: {fileName} {string.Join(" ", args)}");
        });

        var service = new SquadCliResolutionService(runner, environment);
        var resolved = await service.ResolveWithModeAsync(CliSelectionMode.Auto, workspace.RootPath);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.IsAvailable, Is.True);
            Assert.That(resolved.DetectedSource, Is.EqualTo(CliInstallSource.WorkspaceNpm));
            Assert.That(resolved.RuntimeType, Is.EqualTo(CliRuntimeType.NodeScript));
            Assert.That(resolved.EntryScriptPath, Is.EqualTo(Path.Combine(packageRoot, "dist", "cli-entry.js")));
        });
    }

    [Test]
    public async Task ResolveWithModeAsync_GlobalNpm_UsesProvenanceAndBuildsGlobalUpdateInstruction()
    {
        using var workspace = new TestWorkspace();
        var npmPrefix = workspace.GetPath("npm-prefix");
        var nodeDir = workspace.GetPath("node");
        Directory.CreateDirectory(npmPrefix);
        Directory.CreateDirectory(nodeDir);

        var shimPath = Path.Combine(npmPrefix, "squad.cmd");
        var nodePath = Path.Combine(nodeDir, "node.exe");
        var packageRoot = Path.Combine(npmPrefix, "node_modules", "@bradygaster", "squad-cli");
        Directory.CreateDirectory(Path.Combine(packageRoot, "dist"));
        File.WriteAllText(shimPath, "@echo off");
        File.WriteAllText(nodePath, "node");
        File.WriteAllText(
            Path.Combine(packageRoot, "package.json"),
            """
            {
              "bin": {
                "squad": "dist/custom-entry.js"
              }
            }
            """);
        File.WriteAllText(Path.Combine(packageRoot, "dist", "custom-entry.js"), "console.log('squad');");

        var environment = new FakeEnvironmentProvider(
            processPath: string.Join(";", npmPrefix, nodeDir),
            userPath: null,
            machinePath: null);

        var runner = new FakeProcessRunner((fileName, args, _) =>
        {
            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["config", "get", "prefix"]))
            {
                return new CliInvocationResult(0, npmPrefix, string.Empty, false);
            }

            if (fileName.Equals(nodePath, StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual([Path.Combine(packageRoot, "dist", "custom-entry.js"), "--version"]))
            {
                return new CliInvocationResult(0, "0.9.5", string.Empty, false);
            }

            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["view", "@bradygaster/squad-cli", "version"]))
            {
                return new CliInvocationResult(0, "0.9.6", string.Empty, false);
            }

            throw new AssertionException($"Unexpected invocation: {fileName} {string.Join(" ", args)}");
        });

        var service = new SquadCliResolutionService(runner, environment);
        var resolved = await service.ResolveWithModeAsync(CliSelectionMode.GlobalNpm, workspace.RootPath);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.DetectedSource, Is.EqualTo(CliInstallSource.GlobalNpm));
            Assert.That(resolved.RuntimeType, Is.EqualTo(CliRuntimeType.NodeScript));
            Assert.That(resolved.ExecutablePath, Is.EqualTo(nodePath));
            Assert.That(resolved.EntryScriptPath, Is.EqualTo(Path.Combine(packageRoot, "dist", "custom-entry.js")));
            Assert.That(resolved.UpdateInstruction.CanAutoUpdate, Is.True);
            Assert.That(resolved.UpdateInstruction.UpdateCommand, Is.EqualTo("npm install -g @bradygaster/squad-cli@0.9.6"));
        });
    }

    [Test]
    public async Task ResolveWithModeAsync_GlobalNpm_WithPrefixMismatchClassifiesAsNativeOther()
    {
        using var workspace = new TestWorkspace();
        var npmPrefix = workspace.GetPath("npm-prefix");
        var pathDir = workspace.GetPath("shim");
        Directory.CreateDirectory(npmPrefix);
        Directory.CreateDirectory(pathDir);
        var shimPath = Path.Combine(pathDir, "squad.cmd");
        File.WriteAllText(shimPath, "@echo off");

        var environment = new FakeEnvironmentProvider(processPath: pathDir, userPath: null, machinePath: null);
        var runner = new FakeProcessRunner((fileName, args, _) =>
        {
            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["config", "get", "prefix"]))
            {
                return new CliInvocationResult(0, npmPrefix, string.Empty, false);
            }

            if (fileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) &&
                args.Length == 4 &&
                args[0] == "/d" &&
                args[1] == "/s" &&
                args[2] == "/c" &&
                args[3].Contains(CliCmdEscaping.CmdQuoteToken(shimPath), StringComparison.Ordinal))
            {
                return new CliInvocationResult(0, "0.9.5", string.Empty, false);
            }

            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["view", "@bradygaster/squad-cli", "version"]))
            {
                return new CliInvocationResult(0, "0.9.9", string.Empty, false);
            }

            throw new AssertionException($"Unexpected invocation: {fileName} {string.Join(" ", args)}");
        });

        var service = new SquadCliResolutionService(runner, environment);
        var resolved = await service.ResolveWithModeAsync(CliSelectionMode.GlobalNpm, workspace.RootPath);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.DetectedSource, Is.EqualTo(CliInstallSource.NativeOther));
            Assert.That(resolved.RuntimeType, Is.EqualTo(CliRuntimeType.NpmShim));
            Assert.That(resolved.ExecutablePath, Is.EqualTo(shimPath));
            Assert.That(resolved.UpdateInstruction.UpdateCommand, Is.Null);
            Assert.That(resolved.UpdateInstruction.DisplayMessage, Is.EqualTo("Squad CLI was installed outside npm. Check your installation source for updates."));
            Assert.That(resolved.UpdateInstruction.DisplayMessage, Does.Not.Contain("npm install"));
            Assert.That(resolved.UpdateInstruction.CanAutoUpdate, Is.False);
        });
    }

    [Test]
    public async Task ResolveWithModeAsync_NativeSystem_WithSuccessfulPrefixProbeAndPathMismatchClassifiesAsNativeOther()
    {
        using var workspace = new TestWorkspace();
        var npmPrefix = workspace.GetPath("npm-prefix");
        var pathDir = workspace.GetPath("path-shim");
        Directory.CreateDirectory(npmPrefix);
        Directory.CreateDirectory(pathDir);
        var shimPath = Path.Combine(pathDir, "squad.cmd");
        File.WriteAllText(shimPath, "@echo off");

        var environment = new FakeEnvironmentProvider(processPath: pathDir, userPath: null, machinePath: null);
        var runner = new FakeProcessRunner((fileName, args, _) =>
        {
            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["config", "get", "prefix"]))
            {
                return new CliInvocationResult(0, npmPrefix, string.Empty, false);
            }

            if (fileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) &&
                args.Length == 4 &&
                args[0] == "/d" &&
                args[1] == "/s" &&
                args[2] == "/c" &&
                args[3].Contains(CliCmdEscaping.CmdQuoteToken(shimPath), StringComparison.Ordinal))
            {
                return new CliInvocationResult(0, "0.9.5", string.Empty, false);
            }

            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["view", "@bradygaster/squad-cli", "version"]))
            {
                return new CliInvocationResult(0, "0.9.9", string.Empty, false);
            }

            throw new AssertionException($"Unexpected invocation: {fileName} {string.Join(" ", args)}");
        });

        var service = new SquadCliResolutionService(runner, environment);
        var resolved = await service.ResolveWithModeAsync(CliSelectionMode.NativeSystem, workspace.RootPath);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.DetectedSource, Is.EqualTo(CliInstallSource.NativeOther));
            Assert.That(resolved.UpdateInstruction.CanAutoUpdate, Is.False);
        });
    }

    [Test]
    public async Task ResolveWithModeAsync_GlobalNpm_WithPrefixTimeoutClassifiesAsNativeOther()
    {
        using var workspace = new TestWorkspace();
        var pathDir = workspace.GetPath("shim");
        Directory.CreateDirectory(pathDir);
        var shimPath = Path.Combine(pathDir, "squad.cmd");
        File.WriteAllText(shimPath, "@echo off");

        var environment = new FakeEnvironmentProvider(processPath: pathDir, userPath: null, machinePath: null);
        var runner = new FakeProcessRunner((fileName, args, _) =>
        {
            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["config", "get", "prefix"]))
            {
                return new CliInvocationResult(-1, string.Empty, string.Empty, true);
            }

            if (fileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) &&
                args.Length == 4 &&
                args[0] == "/d" &&
                args[1] == "/s" &&
                args[2] == "/c" &&
                args[3].Contains(CliCmdEscaping.CmdQuoteToken(shimPath), StringComparison.Ordinal))
            {
                return new CliInvocationResult(0, "0.9.5", string.Empty, false);
            }

            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["view", "@bradygaster/squad-cli", "version"]))
            {
                return new CliInvocationResult(0, "0.9.9", string.Empty, false);
            }

            throw new AssertionException($"Unexpected invocation: {fileName} {string.Join(" ", args)}");
        });

        var service = new SquadCliResolutionService(runner, environment);
        var resolved = await service.ResolveWithModeAsync(CliSelectionMode.GlobalNpm, workspace.RootPath);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.DetectedSource, Is.EqualTo(CliInstallSource.NativeOther));
            Assert.That(resolved.RuntimeType, Is.EqualTo(CliRuntimeType.NpmShim));
            Assert.That(resolved.ExecutablePath, Is.EqualTo(shimPath));
            Assert.That(resolved.UpdateInstruction.DisplayMessage, Is.EqualTo("Squad CLI was installed outside npm. Check your installation source for updates."));
            Assert.That(resolved.UpdateInstruction.CanAutoUpdate, Is.False);
        });
    }

    [Test]
    public async Task ResolveWithModeAsync_NativeSystem_IgnoresNodeModulesPathEntries()
    {
        using var workspace = new TestWorkspace();
        var nodeModulesBin = workspace.GetPath("node_modules", ".bin");
        Directory.CreateDirectory(nodeModulesBin);
        File.WriteAllText(Path.Combine(nodeModulesBin, "squad.cmd"), "@echo off");

        var environment = new FakeEnvironmentProvider(processPath: nodeModulesBin, userPath: null, machinePath: null);
        var runner = new FakeProcessRunner((fileName, args, _) =>
        {
            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["config", "get", "prefix"]))
            {
                return new CliInvocationResult(1, string.Empty, "missing", false);
            }

            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["view", "@bradygaster/squad-cli", "version"]))
            {
                return new CliInvocationResult(0, "1.0.0", string.Empty, false);
            }

            throw new AssertionException($"Unexpected invocation: {fileName} {string.Join(" ", args)}");
        });

        var service = new SquadCliResolutionService(runner, environment);
        var resolved = await service.ResolveWithModeAsync(CliSelectionMode.NativeSystem, workspace.RootPath);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.IsAvailable, Is.False);
            Assert.That(resolved.DetectedSource, Is.EqualTo(CliInstallSource.NativeOther));
            Assert.That(resolved.UpdateInstruction.CanAutoUpdate, Is.False);
        });
    }

    [Test]
    public async Task ResolveWithModeAsync_WorkspaceLocal_UnavailableProvidesInstallGuidance()
    {
        using var workspace = new TestWorkspace();
        var environment = new FakeEnvironmentProvider(processPath: null, userPath: null, machinePath: null);
        var runner = new FakeProcessRunner((fileName, args, _) =>
        {
            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["view", "@bradygaster/squad-cli", "version"]))
            {
                return new CliInvocationResult(0, "0.9.9", string.Empty, false);
            }

            throw new AssertionException($"Unexpected invocation: {fileName} {string.Join(" ", args)}");
        });

        var service = new SquadCliResolutionService(runner, environment);
        var resolved = await service.ResolveWithModeAsync(CliSelectionMode.WorkspaceLocal, workspace.RootPath);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.IsAvailable, Is.False);
            Assert.That(resolved.DetectedSource, Is.EqualTo(CliInstallSource.WorkspaceNpm));
            Assert.That(resolved.UpdateInstruction.DisplayMessage, Is.EqualTo("Run 'npm install --save-dev @bradygaster/squad-cli' to install Squad CLI in this workspace."));
            Assert.That(resolved.UpdateInstruction.CanAutoUpdate, Is.False);
        });
    }

    [Test]
    public async Task ResolveWithModeAsync_WorkspaceLocal_UsesNodeNpmCliScriptForTargetProbeOnWindows()
    {
        using var workspace = new TestWorkspace();
        var nodeDir = workspace.GetPath("node tools");
        var npmCliPath = Path.Combine(nodeDir, "node_modules", "npm", "bin", "npm-cli.js");
        Directory.CreateDirectory(Path.GetDirectoryName(npmCliPath)!);
        var nodeExePath = Path.Combine(nodeDir, "node.exe");
        File.WriteAllText(nodeExePath, "node");
        File.WriteAllText(npmCliPath, "console.log('npm');");

        var runner = new FakeProcessRunner((fileName, args, _) =>
        {
            if (fileName.Equals(nodeExePath, StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual([npmCliPath, "view", "@bradygaster/squad-cli", "version"]))
            {
                return new CliInvocationResult(0, "0.12.0", string.Empty, false);
            }

            throw new AssertionException($"Unexpected invocation: {fileName} {string.Join(" ", args)}");
        });
        var environment = new FakeEnvironmentProvider(
            processPath: nodeDir,
            userPath: null,
            machinePath: null);

        var service = new SquadCliResolutionService(runner, environment);
        var resolved = await service.ResolveWithModeAsync(CliSelectionMode.WorkspaceLocal, workspace.RootPath);

        Assert.That(resolved.UpdateInstruction.TargetVersion, Is.EqualTo("0.12.0"));
    }

    [Test]
    public async Task ResolveWithModeAsync_WorkspaceLocal_OlderInstalledVersionIsNotSupportedAndUsesSaveDevUpdate()
    {
        using var workspace = new TestWorkspace();
        var nodeDir = workspace.GetPath("node");
        var nodePath = Path.Combine(nodeDir, "node.exe");
        Directory.CreateDirectory(nodeDir);
        File.WriteAllText(nodePath, "node");

        var packageRoot = workspace.GetPath("node_modules", "@bradygaster", "squad-cli");
        Directory.CreateDirectory(Path.Combine(packageRoot, "dist"));
        File.WriteAllText(
            Path.Combine(packageRoot, "package.json"),
            """
            {
              "bin": {
                "squad": "dist/cli-entry.js"
              }
            }
            """);
        File.WriteAllText(Path.Combine(packageRoot, "dist", "cli-entry.js"), "console.log('squad');");

        var environment = new FakeEnvironmentProvider(processPath: nodeDir, userPath: null, machinePath: null);
        var runner = new FakeProcessRunner((fileName, args, _) =>
        {
            if (fileName.Equals(nodePath, StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual([Path.Combine(packageRoot, "dist", "cli-entry.js"), "--version"]))
            {
                return new CliInvocationResult(0, "0.9.4", string.Empty, false);
            }

            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["view", "@bradygaster/squad-cli", "version"]))
            {
                return new CliInvocationResult(0, "0.9.5", string.Empty, false);
            }

            throw new AssertionException($"Unexpected invocation: {fileName} {string.Join(" ", args)}");
        });

        var service = new SquadCliResolutionService(runner, environment);
        var resolved = await service.ResolveWithModeAsync(CliSelectionMode.WorkspaceLocal, workspace.RootPath);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.DetectedSource, Is.EqualTo(CliInstallSource.WorkspaceNpm));
            Assert.That(resolved.RuntimeType, Is.EqualTo(CliRuntimeType.NodeScript));
            Assert.That(resolved.AgentCmdSupport, Is.EqualTo(CliCapabilityStatus.NotSupported));
            Assert.That(resolved.UpdateInstruction.TargetVersion, Is.EqualTo("0.9.5"));
            Assert.That(resolved.UpdateInstruction.UpdateCommand, Is.EqualTo("npm install --save-dev @bradygaster/squad-cli@0.9.5"));
            Assert.That(resolved.UpdateInstruction.CanAutoUpdate, Is.True);
            Assert.That(runner.Calls.Any(call =>
                call.FileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                call.Args.SequenceEqual(["view", "@bradygaster/squad-cli", "version"])), Is.True);
            Assert.That(runner.Calls.Any(call =>
                call.FileName.Equals(nodePath, StringComparison.OrdinalIgnoreCase) &&
                call.Args.SequenceEqual([Path.Combine(packageRoot, "dist", "cli-entry.js"), "update-check", "--json"])),
                Is.False);
        });
    }

    [Test]
    public async Task ResolveWithModeAsync_NativeSystem_UnparseableVersionSetsCapabilityUnknown()
    {
        using var workspace = new TestWorkspace();
        var nativeDir = workspace.GetPath("native");
        Directory.CreateDirectory(nativeDir);
        var squadExe = Path.Combine(nativeDir, "squad.exe");
        File.WriteAllText(squadExe, "native");

        var environment = new FakeEnvironmentProvider(processPath: nativeDir, userPath: null, machinePath: null);
        var runner = new FakeProcessRunner((fileName, args, _) =>
        {
            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["config", "get", "prefix"]))
            {
                return new CliInvocationResult(1, string.Empty, "missing", false);
            }

            if (fileName.Equals(squadExe, StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["--version"]))
            {
                return new CliInvocationResult(0, "version: unknown", string.Empty, false);
            }

            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["view", "@bradygaster/squad-cli", "version"]))
            {
                return new CliInvocationResult(0, "1.0.0", string.Empty, false);
            }

            throw new AssertionException($"Unexpected invocation: {fileName} {string.Join(" ", args)}");
        });

        var service = new SquadCliResolutionService(runner, environment);
        var resolved = await service.ResolveWithModeAsync(CliSelectionMode.NativeSystem, workspace.RootPath);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Version, Is.Null);
            Assert.That(resolved.AgentCmdSupport, Is.EqualTo(CliCapabilityStatus.Unknown));
            Assert.That(resolved.UpdateInstruction.CanAutoUpdate, Is.False);
        });
    }

    [Test]
    public async Task ProbeVersionAsync_NpmShim_UsesCanonicalCmdQuoteTokenForExecutablePath()
    {
        var shimPath = @"C:\tools\shim folder\squad&shim.cmd";
        var expectedInvocation = $"{CliCmdEscaping.CmdQuoteToken(shimPath)} --version";
        var runner = new FakeProcessRunner((fileName, args, _) =>
        {
            if (fileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["/d", "/s", "/c", expectedInvocation]))
            {
                return new CliInvocationResult(0, "0.9.9", string.Empty, false);
            }

            if (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase) &&
                args.SequenceEqual(["view", "@bradygaster/squad-cli", "version"]))
            {
                return new CliInvocationResult(0, "1.0.0", string.Empty, false);
            }

            throw new AssertionException($"Unexpected invocation: {fileName} {string.Join(" ", args)}");
        });

        var service = new SquadCliResolutionService(
            runner,
            new FakeEnvironmentProvider(processPath: null, userPath: null, machinePath: null));
        var candidate = new ResolvedCliCandidate(
            SelectionMode: CliSelectionMode.Auto,
            DetectedSource: CliInstallSource.GlobalNpm,
            RuntimeType: CliRuntimeType.NpmShim,
            ExecutablePath: shimPath,
            EntryScriptPath: null,
            Version: null,
            IsAvailable: true,
            AgentCmdSupport: CliCapabilityStatus.Unknown,
            ProbeError: null,
            UpdateInstruction: new CliUpdateInstruction(null, false, null, null));

        var probed = await service.ProbeVersionAsync(candidate);

        Assert.That(probed.Version, Is.EqualTo("0.9.9"));
    }

    private sealed class FakeEnvironmentProvider : IEnvironmentProvider
    {
        private readonly string? _processPath;
        private readonly string? _userPath;
        private readonly string? _machinePath;

        public FakeEnvironmentProvider(string? processPath, string? userPath, string? machinePath)
        {
            _processPath = processPath;
            _userPath = userPath;
            _machinePath = machinePath;
        }

        public string? GetEnvironmentVariable(string name, EnvironmentVariableTarget target)
        {
            if (!name.Equals("PATH", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return target switch
            {
                EnvironmentVariableTarget.Process => _processPath,
                EnvironmentVariableTarget.User => _userPath,
                EnvironmentVariableTarget.Machine => _machinePath,
                _ => null
            };
        }

        public bool FileExists(string path)
        {
            return File.Exists(path);
        }
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Func<string, string[], TimeSpan, CliInvocationResult> _handler;

        public FakeProcessRunner(Func<string, string[], TimeSpan, CliInvocationResult> handler)
        {
            _handler = handler;
        }

        public List<Invocation> Calls { get; } = [];

        public Task<CliInvocationResult> RunAsync(string fileName, string[] args, TimeSpan timeout)
        {
            Calls.Add(new Invocation(fileName, args, timeout));
            return Task.FromResult(_handler(fileName, args, timeout));
        }
    }

    private sealed class StubSelectionStore(
        CliSelectionMode mode,
        string? customPath = null) : ISquadCliSelectionStore
    {
        public CliSelectionMode GetSelectionMode(string workspaceDir) => mode;

        public string? GetCustomPath(string workspaceDir) => customPath;

        public Task SaveSelectionAsync(string workspaceDir, CliSelectionMode selectedMode, string? selectedCustomPath) =>
            Task.CompletedTask;
    }

    private sealed record Invocation(string FileName, string[] Args, TimeSpan Timeout);
}
