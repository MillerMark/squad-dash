using System.Text;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ButtonCommandExecutionServiceTests {
    private TestWorkspace _workspace = null!;
    private string _stateDirectory = null!;
    private ButtonWorkspaceSnapshot _snapshot = null!;

    [SetUp]
    public void SetUp() {
        _workspace = new TestWorkspace();
        _stateDirectory = SquadDashPaths.WorkspaceStateDirectory(_workspace.RootPath);
        _snapshot = ButtonWorkspaceSnapshot.Capture(SessionWorkspace.Create(_workspace.RootPath), _stateDirectory);
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(_stateDirectory))
            Directory.Delete(_stateDirectory, recursive: true);
        _workspace.Dispose();
    }

    [Test]
    public void CaptureAndPrepare_ReadInvocationTimeWorkspaceAndBranchInsteadOfEditorState() {
        _workspace.CreateFile(@".git\HEAD", "ref: refs/heads/editor-time\n");
        var button = Button("'{workspaceRoot}' '{workspaceName}' '{branch}'");
        var editorBranch = WorkspaceIdentityPresentation.Resolve(_workspace.RootPath, branch: null).Branch;
        _workspace.CreateFile(@".git\HEAD", "ref: refs/heads/click-time\n");
        var captured = ButtonWorkspaceSnapshot.Capture(
            new SessionWorkspace(_workspace.RootPath + Path.DirectorySeparatorChar, null, null), _stateDirectory);
        var clickBranch = WorkspaceIdentityPresentation.Resolve(captured.FolderPath, branch: null).Branch;
        var invocation = ButtonCommandExecutionService.PrepareInvocation(button, captured, clickBranch);

        Assert.Multiple(() => {
            Assert.That(editorBranch, Is.EqualTo("editor-time"));
            Assert.That(invocation.Branch, Is.EqualTo("click-time"));
            Assert.That(invocation.Workspace.FolderPath, Is.EqualTo(_workspace.RootPath));
            Assert.That(invocation.CommandText, Is.EqualTo(
                $"'{_workspace.RootPath}' '{Path.GetFileName(_workspace.RootPath)}' 'click-time'"));
            Assert.That(button.Content.CommandText, Does.Contain("{branch}"));
            Assert.That(invocation.Workspace.WorkspaceStateDirectory, Is.EqualTo(_stateDirectory));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("HEAD")]
    [TestCase("(detached)")]
    [TestCase("(detached abc1234)")]
    public void PrepareInvocation_UnavailableBranch_BlocksEvenWithoutTokens(string? branch) {
        Assert.That(() => ButtonCommandExecutionService.PrepareInvocation(Button("echo never-run"), _snapshot, branch),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("No command was launched"));
        Assert.That(Directory.Exists(_stateDirectory), Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    [TestCase(".")]
    public void PrepareInvocation_DefaultDirectory_IsOpenedWorkspaceNotStateDirectory(string? directory) {
        var invocation = Prepare(Button("echo test", directory: directory));
        Assert.That(invocation.WorkingDirectory, Is.EqualTo(_workspace.RootPath));
    }

    [TestCase("relative")]
    [TestCase("absolute")]
    [TestCase("placeholder")]
    public void PrepareInvocation_ResolvesConfiguredDirectory(string kind) {
        var subdirectory = _workspace.GetPath("sub dir");
        Directory.CreateDirectory(subdirectory);
        var configured = kind switch {
            "relative" => "sub dir",
            "absolute" => subdirectory,
            _ => @"{workspaceRoot}\sub dir"
        };
        Assert.That(Prepare(Button("echo test", directory: configured)).WorkingDirectory, Is.EqualTo(subdirectory));
    }

    [Test]
    public void PrepareInvocation_MissingDirectory_ThrowsBeforeCreatingScripts() {
        Assert.That(() => Prepare(Button("echo never-run", directory: "missing")),
            Throws.TypeOf<DirectoryNotFoundException>());
        Assert.That(() => ButtonCommandExecutionService.PrepareInvocation(Button("echo never-run"),
            _snapshot with { FolderPath = _workspace.GetPath("absent-workspace") }, "main"),
            Throws.TypeOf<DirectoryNotFoundException>());
        Assert.That(Directory.Exists(_stateDirectory), Is.False);
    }

    [Test]
    public void BuildPowerShellInvocation_PreservesOneMultilineScriptAndUsesFixedUtf8Bootstrap() {
        const string command = "$value = 'single '' quote and \"double\"'\r\n" +
            "function Read-Value { $value }\r\nWrite-Output (Read-Value)\r\nWrite-Output 'caf\u00e9'";
        var invocation = Prepare(Button(command));
        var script = Path.Combine(_stateDirectory, "space ' & % path", "run.ps1");
        var info = ButtonCommandExecutionService.BuildStartInfo(invocation, script);
        var arguments = info.ArgumentList.ToArray();
        var bootstrap = Encoding.Unicode.GetString(Convert.FromBase64String(arguments[^1]));
        Assert.Multiple(() => {
            Assert.That(ButtonCommandExecutionService.BuildScriptContent(invocation), Is.EqualTo(command));
            Assert.That(info.FileName, Is.EqualTo("pwsh.exe"));
            Assert.That(arguments[..^1], Is.EqualTo(new[] {
                "-NoLogo", "-NoProfile", "-NonInteractive", "-OutputFormat", "Text", "-EncodedCommand" }));
            Assert.That(bootstrap, Is.EqualTo(
                "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)\n" +
                "& $env:SQUADDASH_BUTTON_SCRIPT\n" +
                "if (-not $?) { if ($LASTEXITCODE) { exit $LASTEXITCODE }; exit 1 }\n"));
            Assert.That(info.Environment["SQUADDASH_BUTTON_SCRIPT"], Is.EqualTo(script));
            Assert.That(info.Arguments, Is.Empty);
            Assert.That(string.Join(" ", arguments), Does.Not.Contain(command).And.Not.Contain(script));
        });
        AssertRedirectedUtf8(info, invocation.WorkingDirectory);
    }

    [Test]
    public void BuildRawCommandInvocation_PreservesExecutableQuotingAndMultilineBatchSemantics() {
        const string command = "\"C:\\Program Files\\Example\\tool.exe\" \"two words\"\r\n" +
            "set VALUE=first\r\necho %VALUE%\r\nexit /b 7";
        var invocation = Prepare(Button(command, ButtonCommandType.RawCommand));
        var script = Path.Combine(_stateDirectory, "spaces %USERPROFILE% & path", "run.cmd");
        var info = ButtonCommandExecutionService.BuildStartInfo(invocation, script);
        Assert.Multiple(() => {
            Assert.That(ButtonCommandExecutionService.BuildScriptContent(invocation), Is.EqualTo("@echo off\r\n" + command));
            Assert.That(info.FileName, Is.EqualTo(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")));
            Assert.That(info.Arguments, Is.EqualTo("/d /s /c \"chcp 65001 >nul & \"%SQUADDASH_BUTTON_SCRIPT%\"\""));
            Assert.That(info.ArgumentList, Is.Empty);
            Assert.That(info.Environment["SQUADDASH_BUTTON_SCRIPT"], Is.EqualTo(script));
            Assert.That(info.Arguments, Does.Not.Contain(command).And.Not.Contain(script));
        });
        AssertRedirectedUtf8(info, invocation.WorkingDirectory);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExecuteAsync_MissingOrDetachedHead_ReturnsOriginatingWorkspaceErrorWithoutLaunching(bool detached) {
        // An isolated .git marker prevents parent-repository discovery; validation exits before PATH probes or launch.
        Directory.CreateDirectory(_workspace.GetPath(".git"));
        if (detached) _workspace.CreateFile(@".git\HEAD", new string('a', 40));
        var button = Button("echo never-run", ButtonCommandType.RawCommand);
        var result = await new ButtonCommandExecutionService().ExecuteAsync(button, _snapshot);
        Assert.Multiple(() => {
            Assert.That(result.Button, Is.EqualTo(button));
            Assert.That(result.Workspace, Is.EqualTo(_snapshot));
            Assert.That(result.ExitCode, Is.Null);
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(result.Error, Does.Contain("No command was launched"));
            Assert.That(result.CleanupError, Is.Null);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(Directory.Exists(_stateDirectory), Is.False);
        });
    }

    [Test]
    public async Task ExecuteAsync_HeadDetachedAfterSnapshot_RefreshesBranchAndBlocksLaunch() {
        _workspace.CreateFile(@".git\HEAD", "ref: refs/heads/editor-time\n");
        var snapshot = ButtonWorkspaceSnapshot.Capture(SessionWorkspace.Create(_workspace.RootPath), _stateDirectory);
        Assert.That(WorkspaceIdentityPresentation.Resolve(snapshot.FolderPath, null).Branch, Is.EqualTo("editor-time"));
        _workspace.CreateFile(@".git\HEAD", new string('b', 40));

        var result = await new ButtonCommandExecutionService().ExecuteAsync(
            Button("echo never-run", ButtonCommandType.RawCommand), snapshot);

        Assert.That(result.Error, Does.Contain("detached HEAD"));
        Assert.That(result.ExitCode, Is.Null);
        Assert.That(Directory.Exists(_stateDirectory), Is.False);
    }

    [Test]
    public async Task ExecuteAsync_PrelaunchIoFailure_IsAnErrorNotSuccessAndDoesNotLaunch() {
        _workspace.CreateFile(@".git\HEAD", "ref: refs/heads/main\n");
        Directory.CreateDirectory(_stateDirectory);
        File.WriteAllText(Path.Combine(_stateDirectory, "button-runs"), "blocks script directory creation");
        var button = Button("echo never-run", ButtonCommandType.RawCommand);
        var result = await new ButtonCommandExecutionService().ExecuteAsync(button, _snapshot);
        Assert.Multiple(() => {
            Assert.That(result.ExitCode, Is.Null);
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(result.Workspace, Is.EqualTo(_snapshot));
            Assert.That(result.Succeeded, Is.False);
            Assert.That(File.ReadAllText(Path.Combine(_stateDirectory, "button-runs")),
                Is.EqualTo("blocks script directory creation"));
        });
    }

    [TestCase(0, null, true)]
    [TestCase(7, "Button command exited with code 7.", false)]
    [TestCase(null, "Could not start 'pwsh.exe'.", false)]
    [TestCase(0, "execution error", false)]
    public void ExecutionResult_RetainsStreamsExitAndErrorsSeparately(int? exit, string? error, bool succeeded) {
        var button = Button("echo never-run");
        // Record-contract coverage only: this does not claim to exercise the private process runner.
        var result = new ButtonExecutionResult(button, _snapshot, exit, "stdout\n", "stderr\n", error, "cleanup error");
        Assert.Multiple(() => {
            Assert.That(result.Button, Is.EqualTo(button));
            Assert.That(result.Workspace, Is.EqualTo(_snapshot));
            Assert.That(result.ExitCode, Is.EqualTo(exit));
            Assert.That(result.StandardOutput, Is.EqualTo("stdout\n"));
            Assert.That(result.StandardError, Is.EqualTo("stderr\n"));
            Assert.That(result.Error, Is.EqualTo(error));
            Assert.That(result.CleanupError, Is.EqualTo("cleanup error"));
            Assert.That(result.Succeeded, Is.EqualTo(succeeded));
        });
    }

    private ButtonInvocation Prepare(ButtonDefinition button) =>
        ButtonCommandExecutionService.PrepareInvocation(button, _snapshot, "main");

    private static ButtonDefinition Button(string command, ButtonCommandType type = ButtonCommandType.PowerShell,
        string? directory = null) =>
        new(Guid.NewGuid(), new ButtonContent("Caption", "tip", command, type, directory), 0, 1);

    private static void AssertRedirectedUtf8(System.Diagnostics.ProcessStartInfo info, string directory) {
        Assert.Multiple(() => {
            Assert.That(info.WorkingDirectory, Is.EqualTo(directory));
            Assert.That(info.UseShellExecute, Is.False);
            Assert.That(info.CreateNoWindow, Is.True);
            Assert.That(info.RedirectStandardInput, Is.True);
            Assert.That(info.RedirectStandardOutput, Is.True);
            Assert.That(info.RedirectStandardError, Is.True);
            Assert.That(info.StandardOutputEncoding, Is.EqualTo(Encoding.UTF8));
            Assert.That(info.StandardErrorEncoding, Is.EqualTo(Encoding.UTF8));
        });
    }
}
