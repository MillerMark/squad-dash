using System;
using System.IO;
using System.Threading.Tasks;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PreferencesWindowCliSectionTests
{
    // ── A. Mode-selection persistence ────────────────────────────────────────

    [Test]
    public async Task PersistCliSelection_NonCustomMode_SavesModeAndNullsCustomPath()
    {
        using var workspace = new TestWorkspace();
        var settingsPath  = workspace.GetPath("settings", "settings.json");
        var workspaceDir  = workspace.GetPath("repo");
        Directory.CreateDirectory(workspaceDir);
        var store = new SquadCliSelectionStore(new ApplicationSettingsStore(settingsPath));

        await CliPreferencesLogic.PersistCliSelectionAsync(store, workspaceDir, CliSelectionMode.GlobalNpm, null);

        Assert.Multiple(() =>
        {
            Assert.That(store.GetSelectionMode(workspaceDir), Is.EqualTo(CliSelectionMode.GlobalNpm));
            Assert.That(store.GetCustomPath(workspaceDir), Is.Null);
        });
    }

    [Test]
    public async Task PersistCliSelection_CustomPath_PersistsModeAndPath()
    {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var workspaceDir = workspace.GetPath("repo");
        Directory.CreateDirectory(workspaceDir);
        var store = new SquadCliSelectionStore(new ApplicationSettingsStore(settingsPath));

        await CliPreferencesLogic.PersistCliSelectionAsync(
            store, workspaceDir, CliSelectionMode.CustomPath, @"C:\tools\squad.cmd");

        Assert.Multiple(() =>
        {
            Assert.That(store.GetSelectionMode(workspaceDir), Is.EqualTo(CliSelectionMode.CustomPath));
            Assert.That(store.GetCustomPath(workspaceDir), Is.EqualTo(@"C:\tools\squad.cmd"));
        });
    }

    [Test]
    public async Task PersistCliSelection_NonCustomModeIgnoresCustomPathInput()
    {
        // A non-Custom mode should persist null even when a custom path string is supplied,
        // so switching away from Custom clears the path on disk.
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.GetPath("settings", "settings.json");
        var workspaceDir = workspace.GetPath("repo");
        Directory.CreateDirectory(workspaceDir);
        var store = new SquadCliSelectionStore(new ApplicationSettingsStore(settingsPath));

        await CliPreferencesLogic.PersistCliSelectionAsync(
            store, workspaceDir, CliSelectionMode.WorkspaceLocal, @"C:\stale\path.cmd");

        Assert.That(store.GetCustomPath(workspaceDir), Is.Null);
    }

    // ── B. Tri-state capability-indicator display ─────────────────────────────

    [Test]
    public void BuildCapabilityIndicator_Supported_ReturnsSuccessTextResource()
    {
        var (label, resourceKey) = CliPreferencesLogic.BuildCapabilityIndicator(CliCapabilityStatus.Supported);
        Assert.Multiple(() =>
        {
            Assert.That(label,       Is.EqualTo("Supported"));
            Assert.That(resourceKey, Is.EqualTo("SuccessText"));
        });
    }

    [Test]
    public void BuildCapabilityIndicator_NotSupported_ReturnsErrorTextResource()
    {
        var (label, resourceKey) = CliPreferencesLogic.BuildCapabilityIndicator(CliCapabilityStatus.NotSupported);
        Assert.Multiple(() =>
        {
            Assert.That(label,       Is.EqualTo("Not supported"));
            Assert.That(resourceKey, Is.EqualTo("ErrorText"));
        });
    }

    [Test]
    public void BuildCapabilityIndicator_Unknown_ReturnsBodyTextResource()
    {
        var (label, resourceKey) = CliPreferencesLogic.BuildCapabilityIndicator(CliCapabilityStatus.Unknown);
        Assert.Multiple(() =>
        {
            Assert.That(label,       Is.EqualTo("Unknown"));
            Assert.That(resourceKey, Is.EqualTo("BodyText"));
        });
    }

    // ── C. Update badge/menu gating via CanAutoUpdate ─────────────────────────

    [Test]
    public void UpdateInstruction_CanAutoUpdate_TrueWhenServiceProducesNewerTarget()
    {
        // Verify CanAutoUpdate=true is properly carried through the record.
        var instruction = new CliUpdateInstruction(
            UpdateCommand: "npm install -g @bradygaster/squad-cli@0.9.7",
            CanAutoUpdate: true,
            DisplayMessage: null,
            TargetVersion: "0.9.7");

        Assert.Multiple(() =>
        {
            Assert.That(instruction.CanAutoUpdate, Is.True);
            Assert.That(instruction.TargetVersion, Is.EqualTo("0.9.7"));
            Assert.That(instruction.UpdateCommand, Is.Not.Null.And.Contains("0.9.7"));
        });
    }

    [Test]
    public void UpdateInstruction_CanAutoUpdate_FalseWhenVersionsAreEqual()
    {
        var instruction = new CliUpdateInstruction(
            UpdateCommand: null,
            CanAutoUpdate: false,
            DisplayMessage: null,
            TargetVersion: "0.9.7");

        Assert.That(instruction.CanAutoUpdate, Is.False);
    }

    [Test]
    public void UpdateInstruction_CanAutoUpdate_FalseWhenNoTargetVersion()
    {
        var instruction = new CliUpdateInstruction(
            UpdateCommand: null,
            CanAutoUpdate: false,
            DisplayMessage: null,
            TargetVersion: null);

        Assert.Multiple(() =>
        {
            Assert.That(instruction.CanAutoUpdate, Is.False);
            Assert.That(instruction.TargetVersion, Is.Null);
        });
    }

    [Test]
    public async Task SquadCliResolutionService_CanAutoUpdateTrue_WhenGlobalNpmTargetIsNewer()
    {
        // npm install -g puts squad.cmd directly in the prefix dir on Windows.
        using var workspace = new TestWorkspace();
        var prefixDir   = workspace.GetPath("npm-prefix");
        Directory.CreateDirectory(prefixDir);
        File.WriteAllText(Path.Combine(prefixDir, "squad.cmd"), string.Empty);
        var packageRoot = Path.Combine(prefixDir, "node_modules", "@bradygaster", "squad-cli");
        var binSubDir   = Path.Combine(packageRoot, "bin");
        Directory.CreateDirectory(binSubDir);
        File.WriteAllText(Path.Combine(binSubDir, "squad.js"), string.Empty);
        File.WriteAllText(Path.Combine(packageRoot, "package.json"),
            """{"name":"@bradygaster/squad-cli","version":"0.9.6","bin":{"squad":"bin/squad.js"}}""");

        var runner = new StubProcessRunner((fileName, args, _) =>
        {
            if (args is ["config", "get", "prefix"])
                return new CliInvocationResult(0, prefixDir, string.Empty, false);
            // NpmShim probe: cmd.exe /d /s /c "squad.cmd" --version
            if (fileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase))
                return new CliInvocationResult(0, "0.9.6", string.Empty, false);
            if (args is ["view", "@bradygaster/squad-cli", "version"])
                return new CliInvocationResult(0, "0.9.7", string.Empty, false);
            return new CliInvocationResult(1, string.Empty, string.Empty, false);
        });
        // PATH contains the prefix dir so squad.cmd is discoverable
        var env     = new StubEnvironmentProvider(processPath: prefixDir);
        var service = new SquadCliResolutionService(runner, env);

        var candidate = await service.ResolveWithModeAsync(CliSelectionMode.GlobalNpm, workspace.GetPath("repo"));

        Assert.Multiple(() =>
        {
            Assert.That(candidate.UpdateInstruction.CanAutoUpdate, Is.True);
            Assert.That(candidate.UpdateInstruction.TargetVersion, Is.EqualTo("0.9.7"));
        });
    }

    [TestCase("0.9.7", "0.9.7", Description = "equal versions")]
    [TestCase("0.9.6", "0.9.7", Description = "installed is newer")]
    public async Task SquadCliResolutionService_CanAutoUpdateFalse_WhenInstalledNotBehindTarget(
        string availableVersion, string installedVersion)
    {
        using var workspace = new TestWorkspace();
        var prefixDir   = workspace.GetPath("npm-prefix");
        Directory.CreateDirectory(prefixDir);
        File.WriteAllText(Path.Combine(prefixDir, "squad.cmd"), string.Empty);
        var packageRoot = Path.Combine(prefixDir, "node_modules", "@bradygaster", "squad-cli");
        var binSubDir   = Path.Combine(packageRoot, "bin");
        Directory.CreateDirectory(binSubDir);
        File.WriteAllText(Path.Combine(binSubDir, "squad.js"), string.Empty);
        File.WriteAllText(Path.Combine(packageRoot, "package.json"),
            $"{{\"name\":\"@bradygaster/squad-cli\",\"version\":\"{installedVersion}\",\"bin\":{{\"squad\":\"bin/squad.js\"}}}}");

        var runner = new StubProcessRunner((fileName, args, _) =>
        {
            if (args is ["config", "get", "prefix"])
                return new CliInvocationResult(0, prefixDir, string.Empty, false);
            // NpmShim probe: cmd.exe /d /s /c "squad.cmd" --version
            if (fileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase))
                return new CliInvocationResult(0, installedVersion, string.Empty, false);
            if (args is ["view", "@bradygaster/squad-cli", "version"])
                return new CliInvocationResult(0, availableVersion, string.Empty, false);
            return new CliInvocationResult(1, string.Empty, string.Empty, false);
        });
        var env     = new StubEnvironmentProvider(processPath: prefixDir);
        var service = new SquadCliResolutionService(runner, env);

        var candidate = await service.ResolveWithModeAsync(CliSelectionMode.GlobalNpm, workspace.GetPath("repo"));

        Assert.That(candidate.UpdateInstruction.CanAutoUpdate, Is.False);
    }

    // ── D. Update-command passthrough ────────────────────────────────────────

    [Test]
    public async Task SquadCliAdapter_LastResolvedCandidate_SetAfterResolveSquadVersionAsync()
    {
        var fixedCandidate = new ResolvedCliCandidate(
            SelectionMode: CliSelectionMode.Auto,
            DetectedSource: CliInstallSource.GlobalNpm,
            RuntimeType: CliRuntimeType.NpmShim,
            ExecutablePath: "squad.cmd",
            EntryScriptPath: null,
            Version: "0.9.7",
            IsAvailable: true,
            AgentCmdSupport: CliCapabilityStatus.Supported,
            ProbeError: null,
            UpdateInstruction: new CliUpdateInstruction(null, false, null, "0.9.7"));

        using var workspace = new TestWorkspace();
        var adapter = new SquadCliAdapter(
            FakeWorkspacePaths.Create(workspace),
            new StubResolutionService(fixedCandidate));

        await adapter.ResolveSquadVersionAsync();

        Assert.That(adapter.LastResolvedCandidate, Is.Not.Null);
    }

    [Test]
    public async Task SquadCliAdapter_UpdateInstruction_PropagatesUpdateCommandFromResolvedCandidate()
    {
        const string expectedCommand = "npm install -g @bradygaster/squad-cli@0.9.9";
        var fixedCandidate = new ResolvedCliCandidate(
            SelectionMode: CliSelectionMode.Auto,
            DetectedSource: CliInstallSource.GlobalNpm,
            RuntimeType: CliRuntimeType.NpmShim,
            ExecutablePath: "squad.cmd",
            EntryScriptPath: null,
            Version: "0.9.6",
            IsAvailable: true,
            AgentCmdSupport: CliCapabilityStatus.Supported,
            ProbeError: null,
            UpdateInstruction: new CliUpdateInstruction(
                UpdateCommand: expectedCommand,
                CanAutoUpdate: true,
                DisplayMessage: null,
                TargetVersion: "0.9.9"));

        using var workspace = new TestWorkspace();
        var adapter = new SquadCliAdapter(
            FakeWorkspacePaths.Create(workspace),
            new StubResolutionService(fixedCandidate));

        await adapter.ResolveSquadVersionAsync();

        var instruction = adapter.LastResolvedCandidate?.UpdateInstruction;
        Assert.Multiple(() =>
        {
            Assert.That(instruction,                Is.Not.Null);
            Assert.That(instruction!.CanAutoUpdate, Is.True);
            Assert.That(instruction.TargetVersion,  Is.EqualTo("0.9.9"));
            Assert.That(instruction.UpdateCommand,  Is.EqualTo(expectedCommand));
        });
    }

    // ── Private stubs ────────────────────────────────────────────────────────

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Func<string, string[], TimeSpan, CliInvocationResult> _handler;

        public StubProcessRunner(Func<string, string[], TimeSpan, CliInvocationResult> handler)
            => _handler = handler;

        public Task<CliInvocationResult> RunAsync(string fileName, string[] args, TimeSpan timeout)
            => Task.FromResult(_handler(fileName, args, timeout));
    }

    private sealed class StubEnvironmentProvider : IEnvironmentProvider
    {
        private readonly string? _processPath;

        public StubEnvironmentProvider(string? processPath) => _processPath = processPath;

        public string? GetEnvironmentVariable(string name, EnvironmentVariableTarget target)
        {
            if (!name.Equals("PATH", StringComparison.OrdinalIgnoreCase))
                return null;
            return target == EnvironmentVariableTarget.Process ? _processPath : null;
        }

        public bool FileExists(string path) => File.Exists(path);
    }

    private sealed class StubResolutionService : ISquadCliResolutionService
    {
        private readonly ResolvedCliCandidate _candidate;

        public StubResolutionService(ResolvedCliCandidate candidate) => _candidate = candidate;

        public Task<ResolvedCliCandidate> ResolveAsync(string workspaceDir)
            => Task.FromResult(_candidate);

        public Task<ResolvedCliCandidate> ResolveWithModeAsync(CliSelectionMode mode, string workspaceDir)
            => Task.FromResult(_candidate);

        public Task<ResolvedCliCandidate> ProbeVersionAsync(ResolvedCliCandidate candidate)
            => Task.FromResult(candidate);
    }

    private static class FakeWorkspacePaths
    {
        public static IWorkspacePaths Create(TestWorkspace workspace)
            => new Impl(workspace.GetPath("app"));

        private sealed class Impl : IWorkspacePaths
        {
            public string ApplicationRoot { get; }
            public string SquadSdkDirectory => System.IO.Path.Combine(ApplicationRoot, "Squad.SDK");
            public string RunRootDirectory => System.IO.Path.Combine(ApplicationRoot, "Run");
            public string AgentImageAssetsDirectory => System.IO.Path.Combine(ApplicationRoot, "Assets", "Agents");
            public string RoleIconAssetsDirectory => System.IO.Path.Combine(ApplicationRoot, "Assets", "Roles");
            public string ScreenshotsDirectory => System.IO.Path.Combine(ApplicationRoot, "docs", "screenshots");
            public Impl(string root) => ApplicationRoot = root;
        }
    }
}
