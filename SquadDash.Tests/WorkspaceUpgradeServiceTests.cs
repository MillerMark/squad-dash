namespace SquadDash.Tests;

[TestFixture]
internal sealed class WorkspaceUpgradeServiceTests
{
    [Test]
    public void VersionPolicy_PendingTargetWinsOverOlderActiveCli()
    {
        var version = WorkspaceUpgradeVersionPolicy.ResolveDisplayVersion(
            isPending: true,
            recordedPendingVersion: "0.12.0",
            activeCliVersion: "0.10.0",
            latestAvailableVersion: null);

        Assert.That(version, Is.EqualTo("0.12.0"));
    }

    [Test]
    public void VersionPolicy_NewerAvailableVersionSupersedesStalePendingTarget()
    {
        var version = WorkspaceUpgradeVersionPolicy.ResolveDisplayVersion(
            isPending: true,
            recordedPendingVersion: "0.10.0",
            activeCliVersion: "0.10.0",
            latestAvailableVersion: "0.12.0");

        Assert.That(version, Is.EqualTo("0.12.0"));
    }

    [Test]
    public void VersionPolicy_SimulationUsesLatestAvailableVersion()
    {
        var version = WorkspaceUpgradeVersionPolicy.ResolveSimulationVersion(
            latestAvailableVersion: "0.12.0",
            activeCliVersion: "0.10.0");

        Assert.That(version, Is.EqualTo("0.12.0"));
    }

    [TestCase("0.12.0", "0.12.0", true)]
    [TestCase("v0.12.0", "0.12.0", true)]
    [TestCase("0.12.0", "0.10.0", false)]
    public void VersionPolicy_RequiresActiveCliToMatchWorkspaceTarget(
        string target,
        string active,
        bool expected)
    {
        Assert.That(
            WorkspaceUpgradeVersionPolicy.MatchesActiveCandidate(target, active),
            Is.EqualTo(expected));
    }

    [Test]
    public async Task DryRunAsync_WhenCandidateUnavailable_FailsWithoutSpawningCommand()
    {
        var runner = new RecordingCommandRunner();
        var service = CreateService(runner, dryRunSupported: true, dryRunArguments: ["upgrade", "--preview"]);

        var result = await service.DryRunAsync(CreateUnavailableCandidate(), @"C:\workspace");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(runner.Invocations, Is.Empty);
        });
    }

    [Test]
    public async Task DryRunAsync_WhenDryRunFeatureDisabled_FailsWithoutSpawningCommand()
    {
        var runner = new RecordingCommandRunner();
        var service = CreateService(
            runner,
            dryRunSupported: false,
            dryRunArguments: [],
            dryRunUnsupportedMessage: SquadCliCommands.WorkspaceUpgradeDryRunUnsupportedMessage);

        var result = await service.DryRunAsync(CreateAvailableCandidate(), @"C:\workspace");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(SquadCliCommands.WorkspaceUpgradeDryRunUnsupportedMessage));
            Assert.That(runner.Invocations, Is.Empty);
        });
    }

    [Test]
    public async Task DryRunAsync_WhenCliPredatesSafeDryRun_FailsWithoutSpawningCommand()
    {
        var runner = new RecordingCommandRunner();
        var service = CreateService(
            runner,
            dryRunSupported: SquadCliCommands.WorkspaceUpgradeDryRunSupported,
            dryRunArguments: SquadCliCommands.WorkspaceUpgradeDryRunArguments,
            dryRunUnsupportedMessage: SquadCliCommands.WorkspaceUpgradeDryRunUnsupportedMessage);

        var result = await service.DryRunAsync(
            CreateAvailableCandidate() with { Version = "0.10.0" },
            @"C:\workspace");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(SquadCliCommands.WorkspaceUpgradeDryRunUnsupportedMessage));
            Assert.That(runner.Invocations, Is.Empty);
        });
    }

    [Test]
    public async Task DryRunAsync_WhenDryRunSupported_InvokesRunnerWithExpectedArguments()
    {
        var expectedArguments = SquadCliCommands.WorkspaceUpgradeDryRunArguments;
        var runner = new RecordingCommandRunner();
        var service = CreateService(
            runner,
            dryRunSupported: SquadCliCommands.WorkspaceUpgradeDryRunSupported,
            dryRunArguments: expectedArguments);

        var result = await service.DryRunAsync(CreateAvailableCandidate(), @"C:\workspace");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(runner.Invocations, Has.Count.EqualTo(1));
            Assert.That(runner.Invocations[0].DisplayName, Is.EqualTo("Dry Run Workspace Upgrade"));
            Assert.That(runner.Invocations[0].Arguments, Is.EqualTo(expectedArguments));
        });
    }

    [TestCase("0.10.0", false)]
    [TestCase("0.11.0", true)]
    [TestCase("0.12.0", true)]
    [TestCase("v0.12.0", true)]
    [TestCase("0.13.0-insider.1", true)]
    [TestCase(null, false)]
    [TestCase("unknown", false)]
    public void SupportsWorkspaceUpgradeDryRun_UsesSafeCliVersionBoundary(string? version, bool expected)
    {
        Assert.That(SquadCliCommands.SupportsWorkspaceUpgradeDryRun(version), Is.EqualTo(expected));
    }

    [Test]
    public async Task UpgradeAsync_WhenCandidateUnavailable_FailsWithoutSpawningCommand()
    {
        var runner = new RecordingCommandRunner();
        var service = CreateService(runner, dryRunSupported: true, dryRunArguments: ["upgrade", "--preview"]);

        var result = await service.UpgradeAsync(CreateUnavailableCandidate(), @"C:\workspace");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.IsPartial, Is.False);
            Assert.That(runner.Invocations, Is.Empty);
        });
    }

    [Test]
    public async Task UpgradeAsync_WhenCommandFailsAfterPartialApply_PreservesParsedPartialLists()
    {
        var runner = new RecordingCommandRunner(
            new SquadCommandResult(
                Success: false,
                ExitCode: 1,
                StandardOutput: """
                    create templates/session-init-reference.md
                    generated .squad/casting/registry.json
                    squad.agent.md has local customizations — would back up to squad.agent.md.local-backup
                    """,
                StandardError: "simulated stderr",
                Message: "Upgrade command failed after partially applying files."));
        var service = CreateService(runner, dryRunSupported: true, dryRunArguments: ["upgrade", "--preview"]);

        var result = await service.UpgradeAsync(CreateAvailableCandidate(), @"C:\workspace");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.IsPartial, Is.True);
            Assert.That(result.ErrorMessage, Is.EqualTo("Upgrade command failed after partially applying files."));
            Assert.That(result.SafeAdditionsOrRefreshes, Contains.Item("templates\\session-init-reference.md"));
            Assert.That(result.TrackedLocalChangesAtRisk, Contains.Item(".github\\agents\\squad.agent.md"));
            Assert.That(result.GeneratedFilesAtRisk, Contains.Item(".squad\\casting\\registry.json"));
            Assert.That(runner.Invocations, Has.Count.EqualTo(1));
            Assert.That(runner.Invocations[0].DisplayName, Is.EqualTo("Apply Workspace Upgrade"));
            Assert.That(runner.Invocations[0].Arguments, Is.EqualTo(SquadCliCommands.WorkspaceUpgradeApplyArguments));
        });
    }

    [Test]
    public void ParseDryRunOutput_ClassifiesRepresentativeLinesIntoAllThreeCategories()
    {
        var output = """
            Dry run — previewing upgrade from 0.11.0 → 0.12.0
            ⚠️ squad.agent.md has local customizations — would back up to squad.agent.md.local-backup
            Would overwrite 3 squad-owned file(s):
              create  templates/session-init-reference.md
              overwrite  templates/charter.md
            generated  .squad/casting/registry.json
            """;

        var result = WorkspaceUpgradeService.ParseDryRunOutput(output);

        Assert.Multiple(() =>
        {
            Assert.That(result.SafeAdditionsOrRefreshes, Is.EquivalentTo(new[]
            {
                "templates\\session-init-reference.md",
                "templates\\charter.md"
            }));
            Assert.That(result.TrackedLocalChangesAtRisk, Is.EquivalentTo(new[]
            {
                ".github\\agents\\squad.agent.md"
            }));
            Assert.That(result.GeneratedFilesAtRisk, Is.EquivalentTo(new[]
            {
                ".squad\\casting\\registry.json"
            }));
        });
    }

    [Test]
    public void ParseDryRunOutput_MalformedOrEmptyOutput_DoesNotThrowAndReturnsEmpty()
    {
        Assert.DoesNotThrow(() =>
        {
            var result = WorkspaceUpgradeService.ParseDryRunOutput(
                null,
                string.Empty,
                " \r\n\t",
                "This line is unrelated output with no parseable file markers.");

            Assert.Multiple(() =>
            {
                Assert.That(result.SafeAdditionsOrRefreshes, Is.Empty);
                Assert.That(result.TrackedLocalChangesAtRisk, Is.Empty);
                Assert.That(result.GeneratedFilesAtRisk, Is.Empty);
            });
        });
    }

    [Test]
    public void ParseDryRunOutput_CleanWorkspaceDryRun_HasEmptyRiskLists()
    {
        var output = """
            Dry run — previewing upgrade from 0.11.0 → 0.12.0
            squad.agent.md is unmodified — would refresh with latest template
            Would overwrite 2 squad-owned file(s):
              overwrite  templates/history.md
              create  templates/skill.md
            Run without --dry-run to apply.
            """;

        var result = WorkspaceUpgradeService.ParseDryRunOutput(output);

        Assert.Multiple(() =>
        {
            Assert.That(result.SafeAdditionsOrRefreshes, Contains.Item(".github\\agents\\squad.agent.md"));
            Assert.That(result.SafeAdditionsOrRefreshes, Contains.Item("templates\\history.md"));
            Assert.That(result.SafeAdditionsOrRefreshes, Contains.Item("templates\\skill.md"));
            Assert.That(result.TrackedLocalChangesAtRisk, Is.Empty);
            Assert.That(result.GeneratedFilesAtRisk, Is.Empty);
        });
    }

    private static WorkspaceUpgradeService CreateService(
        RecordingCommandRunner runner,
        bool dryRunSupported,
        IReadOnlyList<string> dryRunArguments,
        string dryRunUnsupportedMessage = "Workspace dry-run unsupported.")
    {
        return new WorkspaceUpgradeService(
            runner,
            new SquadInstallerService(),
            new StubCliResolutionService(CreateAvailableCandidate()),
            dryRunSupported,
            dryRunArguments,
            dryRunUnsupportedMessage);
    }

    private static ResolvedCliCandidate CreateAvailableCandidate() =>
        new(
            SelectionMode: CliSelectionMode.Auto,
            DetectedSource: CliInstallSource.WorkspaceNpm,
            RuntimeType: CliRuntimeType.NodeScript,
            ExecutablePath: "node",
            EntryScriptPath: @"C:\workspace\node_modules\@bradygaster\squad-cli\dist\cli-entry.js",
            Version: "0.13.0",
            IsAvailable: true,
            AgentCmdSupport: CliCapabilityStatus.Supported,
            ProbeError: null,
            UpdateInstruction: new CliUpdateInstruction(null, false, null, null));

    private static ResolvedCliCandidate CreateUnavailableCandidate() =>
        CreateAvailableCandidate() with
        {
            IsAvailable = false,
            ProbeError = "CLI missing"
        };

    private sealed class StubCliResolutionService : ISquadCliResolutionService
    {
        private readonly ResolvedCliCandidate _candidate;

        public StubCliResolutionService(ResolvedCliCandidate candidate) => _candidate = candidate;

        public Task<ResolvedCliCandidate> ResolveAsync(string workspaceDir) => Task.FromResult(_candidate);

        public Task<ResolvedCliCandidate> ResolveWithModeAsync(CliSelectionMode mode, string workspaceDir) => Task.FromResult(_candidate);

        public Task<ResolvedCliCandidate> ProbeVersionAsync(ResolvedCliCandidate candidate) => Task.FromResult(candidate);
    }

    private sealed record CommandInvocation(string DisplayName, IReadOnlyList<string> Arguments);

    private sealed class RecordingCommandRunner : ISquadCommandRunner
    {
        private readonly Queue<SquadCommandResult> _results = new();

        public RecordingCommandRunner(params SquadCommandResult[] results)
        {
            foreach (var result in results)
            {
                _results.Enqueue(result);
            }
        }

        public List<CommandInvocation> Invocations { get; } = new();

        public Task<SquadCommandResult> RunAsync(SquadCliCommandDefinition command, string activeDirectory)
        {
            Invocations.Add(new CommandInvocation(command.DisplayName, command.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
            return Task.FromResult(NextResult());
        }

        public Task<SquadCommandResult> RunAsync(
            ResolvedCliCandidate candidate,
            string activeDirectory,
            string displayName,
            IReadOnlyList<string> commandArguments)
        {
            Invocations.Add(new CommandInvocation(displayName, commandArguments.ToArray()));
            return Task.FromResult(NextResult());
        }

        private SquadCommandResult NextResult()
        {
            if (_results.Count > 0)
            {
                return _results.Dequeue();
            }

            return new SquadCommandResult(true, 0, string.Empty, string.Empty, "ok");
        }
    }
}
