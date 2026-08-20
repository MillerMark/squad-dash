using System;
using System.Linq;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class SquadCliCommandsTests {
    [Test]
    public void InstallLocalCli_HasCmdExeFileName() {
        Assert.That(SquadCliCommands.InstallLocalCli.FileName, Is.EqualTo("cmd.exe"));
    }

    [Test]
    public void InstallLocalCli_ArgumentsContainNpmInstall() {
        Assert.That(SquadCliCommands.InstallLocalCli.Arguments, Does.Contain("npm install"));
    }

    [Test]
    public void InstallLocalCli_HasNonEmptyDisplayName() {
        Assert.That(SquadCliCommands.InstallLocalCli.DisplayName, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Init_HasNodeFileName() {
        Assert.That(SquadCliCommands.Init.FileName, Is.EqualTo("node"));
    }

    [Test]
    public void Init_ArgumentsUseLocalCliEntryAndInit() {
        Assert.That(
            SquadCliCommands.Init.Arguments,
            Does.Contain(SquadCliCommands.LocalCliEntryPath)
                .And.Contain("init")
                .And.Not.Contain("npx"));
    }

    [Test]
    public void Init_HasNonEmptyDisplayName() {
        Assert.That(SquadCliCommands.Init.DisplayName, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Doctor_HasCmdExeFileName() {
        Assert.That(SquadCliCommands.Doctor.FileName, Is.EqualTo("cmd.exe"));
    }

    [Test]
    public void Doctor_ArgumentsContainNpxAndDoctor() {
        Assert.That(SquadCliCommands.Doctor.Arguments, Does.Contain("npx").And.Contain("doctor"));
    }

    [Test]
    public void Doctor_HasNonEmptyDisplayName() {
        Assert.That(SquadCliCommands.Doctor.DisplayName, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void AllCommands_HaveDistinctArguments() {
        var arguments = new[] {
            SquadCliCommands.InstallLocalCli.Arguments,
            SquadCliCommands.Init.Arguments,
            SquadCliCommands.Doctor.Arguments
        };

        Assert.That(arguments, Is.Unique);
    }

    [Test]
    public void BuildCommandStartInfo_NpmShim_UsesCmdExeAndCanonicalEscapedInvocation() {
        var candidate = CreateCandidate(
            CliRuntimeType.NpmShim,
            executablePath: @"C:\tools\squad shim.cmd");
        var commandArguments = new[] { "watch", "with spaces", "double\"quote", "a&b|c^d", string.Empty };

        var startInfo = SquadCliCommands.BuildCommandStartInfo(candidate, @"C:\workspace", commandArguments);
        var expectedInvocation = string.Join(
            " ",
            new[] { candidate.ExecutablePath }.Concat(commandArguments).Select(CliCmdEscaping.CmdQuoteToken));

        Assert.Multiple(() => {
            Assert.That(startInfo.FileName, Is.EqualTo("cmd.exe"));
            Assert.That(startInfo.WorkingDirectory, Is.EqualTo(@"C:\workspace"));
            Assert.That(startInfo.ArgumentList, Is.EqualTo(new[] { "/d", "/s", "/c", expectedInvocation }));
        });
    }

    [Test]
    public void BuildCommandStartInfo_NodeScript_UsesArgumentListWithEntryScriptFirst() {
        var candidate = CreateCandidate(
            CliRuntimeType.NodeScript,
            executablePath: @"C:\node\node.exe",
            entryScriptPath: @"D:\workspace\node_modules\@bradygaster\squad-cli\dist\cli-entry.js");

        var startInfo = SquadCliCommands.BuildCommandStartInfo(
            candidate,
            @"D:\workspace",
            "watch",
            "--health");

        Assert.Multiple(() => {
            Assert.That(startInfo.FileName, Is.EqualTo(candidate.ExecutablePath));
            Assert.That(startInfo.WorkingDirectory, Is.EqualTo(@"D:\workspace"));
            Assert.That(startInfo.ArgumentList, Is.EqualTo(new[] { candidate.EntryScriptPath!, "watch", "--health" }));
        });
    }

    [Test]
    public void BuildCommandStartInfo_NativeExecutable_UsesArgumentListTokens() {
        var candidate = CreateCandidate(
            CliRuntimeType.NativeExecutable,
            executablePath: @"C:\tools\squad.exe");

        var startInfo = SquadCliCommands.BuildCommandStartInfo(
            candidate,
            @"D:\workspace",
            "doctor",
            "--json");

        Assert.Multiple(() => {
            Assert.That(startInfo.FileName, Is.EqualTo(candidate.ExecutablePath));
            Assert.That(startInfo.WorkingDirectory, Is.EqualTo(@"D:\workspace"));
            Assert.That(startInfo.ArgumentList, Is.EqualTo(new[] { "doctor", "--json" }));
        });
    }

    [Test]
    [TestCase("simple")]
    [TestCase("with spaces")]
    [TestCase("double\"quote")]
    [TestCase("a&b|c^d")]
    [TestCase("")]
    public void BuildCommandStartInfo_NpmShim_CmdEscapingMatchesCanonicalHelper(string token) {
        var candidate = CreateCandidate(
            CliRuntimeType.NpmShim,
            executablePath: @"C:\tools\squad.cmd");

        var startInfo = SquadCliCommands.BuildCommandStartInfo(
            candidate,
            @"C:\workspace",
            "watch",
            token);

        var expectedInvocation = string.Join(
            " ",
            new[] { candidate.ExecutablePath, "watch", token }.Select(CliCmdEscaping.CmdQuoteToken));

        Assert.That(startInfo.ArgumentList[3], Is.EqualTo(expectedInvocation));
    }

    private static ResolvedCliCandidate CreateCandidate(
        CliRuntimeType runtimeType,
        string executablePath,
        string? entryScriptPath = null) {
        return new ResolvedCliCandidate(
            SelectionMode: CliSelectionMode.Auto,
            DetectedSource: CliInstallSource.Unknown,
            RuntimeType: runtimeType,
            ExecutablePath: executablePath,
            EntryScriptPath: entryScriptPath,
            Version: null,
            IsAvailable: true,
            AgentCmdSupport: CliCapabilityStatus.Unknown,
            ProbeError: null,
            UpdateInstruction: new CliUpdateInstruction(null, false, null, null));
    }
}
