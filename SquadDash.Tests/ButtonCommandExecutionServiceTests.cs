using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
[NonParallelizable]
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

    [Test]
    public void BuildUrlInvocation_UsesShellExecuteAndDefaultBrowserLaunchPath() {
        var invocation = Prepare(Button("https://example.com/path?q=1", ButtonCommandType.Url));

        var info = ButtonCommandExecutionService.BuildStartInfo(invocation, scriptPath: string.Empty);

        Assert.Multiple(() => {
            Assert.That(info.FileName, Is.EqualTo("https://example.com/path?q=1"));
            Assert.That(info.UseShellExecute, Is.True);
            Assert.That(info.WorkingDirectory, Is.EqualTo(invocation.WorkingDirectory));
            Assert.That(info.RedirectStandardOutput, Is.False);
            Assert.That(info.RedirectStandardError, Is.False);
            Assert.That(info.RedirectStandardInput, Is.False);
        });
    }

    [TestCase("relative/path")]
    [TestCase("ftp://example.com/download")]
    [TestCase("file:///C:/Windows")]
    [TestCase("https://")]
    public void BuildUrlInvocation_InvalidUrl_ThrowsBeforeLaunch(string url) {
        var invocation = Prepare(Button(url, ButtonCommandType.Url));
        Assert.That(() => ButtonCommandExecutionService.BuildStartInfo(invocation, scriptPath: string.Empty),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void BuildElevatedStartInfo_PowerShell_UsesRunAsVerbAndRedirectsToLogFile() {
        const string command = "restart-service audiosrv";
        var invocation = Prepare(Button(command));
        var script = Path.Combine(_stateDirectory, "run.ps1");
        var logPath = Path.Combine(_stateDirectory, "run.ps1.out.log");

        var info = ButtonCommandExecutionService.BuildElevatedStartInfo(invocation, script, logPath);

        Assert.Multiple(() => {
            Assert.That(info.FileName, Is.EqualTo(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")));
            Assert.That(info.Verb, Is.EqualTo("runas"));
            Assert.That(info.UseShellExecute, Is.True);
            Assert.That(info.RedirectStandardOutput, Is.False);
            Assert.That(info.RedirectStandardError, Is.False);
            Assert.That(info.RedirectStandardInput, Is.False);
            Assert.That(info.WorkingDirectory, Is.EqualTo(invocation.WorkingDirectory));
            Assert.That(info.Arguments, Does.StartWith("/d /s /c \"pwsh.exe "));
            Assert.That(info.Arguments, Does.Contain("-EncodedCommand "));
            Assert.That(info.Arguments, Does.Contain($"> \"{logPath}\" 2>&1\""));
            Assert.That(info.Arguments, Does.Not.Contain(command));
            var encodedStart = info.Arguments.IndexOf("-EncodedCommand ", StringComparison.Ordinal) + "-EncodedCommand ".Length;
            var encodedEnd = info.Arguments.IndexOf(" > \"", StringComparison.Ordinal);
            var bootstrap = Encoding.Unicode.GetString(Convert.FromBase64String(info.Arguments[encodedStart..encodedEnd]));
            Assert.That(bootstrap, Does.Contain($"& '{script}'"));
        });
    }

    [Test]
    public void BuildElevatedStartInfo_RawCommand_UsesRunAsVerbAndRedirectsToLogFile() {
        var invocation = Prepare(Button("echo raw-stdout", ButtonCommandType.RawCommand));
        var script = Path.Combine(_stateDirectory, "run.cmd");
        var logPath = Path.Combine(_stateDirectory, "run.cmd.out.log");

        var info = ButtonCommandExecutionService.BuildElevatedStartInfo(invocation, script, logPath);

        Assert.Multiple(() => {
            Assert.That(info.Verb, Is.EqualTo("runas"));
            Assert.That(info.UseShellExecute, Is.True);
            Assert.That(info.Arguments, Is.EqualTo(
                $"/d /s /c \"chcp 65001 >nul & \"{script}\" > \"{logPath}\" 2>&1\""));
        });
    }

    [Test]
    public void BuildElevatedStartInfo_Url_ThrowsBecauseElevationIsNotSupported() {
        var invocation = Prepare(Button("https://example.com", ButtonCommandType.Url));
        Assert.That(() => ButtonCommandExecutionService.BuildElevatedStartInfo(invocation, "script", "log"),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public async Task ExecuteAsync_UrlValidationFailure_ReturnsErrorWithoutScriptsOrCapturedStreams() {
        _workspace.CreateFile(@".git\HEAD", "ref: refs/heads/main\n");

        var result = await new ButtonCommandExecutionService().ExecuteAsync(
            Button("ftp://example.com/download", ButtonCommandType.Url), _snapshot);

        Assert.Multiple(() => {
            Assert.That(result.ExitCode, Is.Null);
            Assert.That(result.Error, Does.Contain("http or https"));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(Directory.Exists(Path.Combine(_stateDirectory, "button-runs")), Is.False);
        });
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

    [TestCase(0)]
    [TestCase(7)]
    public async Task ExecuteAsync_PowerShellMultiline_CapturesBothFullStreamsAndMapsExitCode(int exitCode) {
        Assert.That(WorkspaceIssueFactory.IsPowerShellAvailable(), Is.True,
            "The production PowerShell availability probe must succeed; this integration case must not be skipped.");
        _workspace.CreateFile(@".git\HEAD", "ref: refs/heads/test-authored-command\n");
        const int lineCount = 512;
        var stdoutLine = "stdout-" + new string('o', 256);
        var stderrLine = "stderr-" + new string('e', 256);
        // Each stream exceeds pipe capacity. Sequential draining would block before either tail is reached.
        var button = Button(
            $"$stdout = '{stdoutLine}'\r\n" +
            $"$stderr = '{stderrLine}'\r\n" +
            $"for ($i = 0; $i -lt {lineCount}; $i++) {{\r\n" +
            "    Write-Output $stdout\r\n" +
            "    [Console]::Error.WriteLine($stderr)\r\n" +
            "}\r\n" +
            "Write-Output 'stdout-tail'\r\n" +
            "[Console]::Error.WriteLine('stderr-tail')\r\n" +
            $"exit {exitCode}");

        var lines = new ConcurrentQueue<string>();
        var result = await new ButtonCommandExecutionService().ExecuteAsync(button, _snapshot, new InlineProgress(lines.Enqueue));

        AssertExecutedResult(result, button, exitCode,
            string.Concat(Enumerable.Repeat(stdoutLine + Environment.NewLine, lineCount)) + "stdout-tail" + Environment.NewLine,
            string.Concat(Enumerable.Repeat(stderrLine + Environment.NewLine, lineCount)) + "stderr-tail" + Environment.NewLine);
        Assert.That(lines, Is.EquivalentTo(Enumerable.Repeat(stdoutLine, lineCount)
            .Concat(Enumerable.Repeat(stderrLine, lineCount)).Concat(new[] { "stdout-tail", "stderr-tail" })));
    }

    [TestCase(0)]
    [TestCase(9)]
    public async Task ExecuteAsync_RawCommand_CapturesDistinctStreamsAndMapsExitCode(int exitCode) {
        _workspace.CreateFile(@".git\HEAD", "ref: refs/heads/test-authored-command\n");
        var button = Button(
            "echo raw-stdout\r\n" +
            "1>&2 echo raw-stderr\r\n" +
            $"exit /b {exitCode}", ButtonCommandType.RawCommand);

        var result = await new ButtonCommandExecutionService().ExecuteAsync(button, _snapshot);

        AssertExecutedResult(result, button, exitCode,
            "raw-stdout" + Environment.NewLine, "raw-stderr" + Environment.NewLine);
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(4096)]
    public async Task CaptureOutputAsync_ReportsLinesAndPreservesExactNewlinesAndUnterminatedTail(int chunkSize) {
        const string text = "first\r\n\r\nsecond\nthird\rfourth caf\u00e9\nlast";
        var lines = new List<string>();
        using var reader = new ChunkedReader(text, chunkSize);

        var captured = await ButtonCommandExecutionService.CaptureOutputAsync(reader, new InlineProgress(lines.Add));

        Assert.Multiple(() => {
            Assert.That(captured, Is.EqualTo(text));
            Assert.That(lines, Is.EqualTo(new[] { "first", "", "second", "third", "fourth caf\u00e9", "last" }));
        });
    }

    [Test]
    public async Task TailLogFileAsync_StreamsAppendedContentAndFlushesUnterminatedTail() {
        var logPath = _workspace.GetPath("elevated.out.log");
        using (var writer = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) {
            var firstChunk = Encoding.UTF8.GetBytes("first-line\r\n");
            writer.Write(firstChunk, 0, firstChunk.Length);
            writer.Flush();
        }
        var lines = new ConcurrentQueue<string>();
        using var stopPolling = new CancellationTokenSource();
        var tail = ButtonCommandExecutionService.TailLogFileAsync(logPath, new InlineProgress(lines.Enqueue), stopPolling.Token);

        // Give the first poll a chance to observe the already-written content, then append more.
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        using (var writer = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) {
            var secondChunk = Encoding.UTF8.GetBytes("second-line\r\nunterminated-tail");
            writer.Write(secondChunk, 0, secondChunk.Length);
            writer.Flush();
        }
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        stopPolling.Cancel();
        var captured = await tail.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() => {
            Assert.That(captured, Is.EqualTo("first-line\r\nsecond-line\r\nunterminated-tail"));
            Assert.That(lines, Is.EquivalentTo(new[] { "first-line", "second-line", "unterminated-tail" }));
        });
    }

    [Test]
    public async Task CaptureOutputAsync_StoppedPendingRead_PreservesCapturedTextAndFlushesTail() {
        using var reader = new HeldOpenReader("first\r\ntail");
        using var stopReading = new CancellationTokenSource();
        var lines = new List<string>();
        var capture = ButtonCommandExecutionService.CaptureOutputAsync(reader, new InlineProgress(lines.Add), stopReading.Token);
        await reader.ReadPending.Task.WaitAsync(TimeSpan.FromSeconds(3));

        stopReading.Cancel();
        var captured = await capture.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Multiple(() => {
            Assert.That(captured, Is.EqualTo("first\r\ntail"));
            Assert.That(lines, Is.EqualTo(new[] { "first", "tail" }));
            Assert.That(reader.PendingRead.Task.IsCompleted, Is.False,
                "Capture must not depend on the underlying pipe supporting cancellation or reaching EOF.");
        });
        reader.PendingRead.SetResult(0);
    }

    [Test]
    public async Task ExecuteAsync_StreamsBothPipesBeforeExitAndStillReturnsExactBuffers() {
        Assert.That(WorkspaceIssueFactory.IsPowerShellAvailable(), Is.True);
        _workspace.CreateFile(@".git\HEAD", "ref: refs/heads/test-authored-command\n");
        var lines = new ConcurrentQueue<string>();
        var progress = new InlineProgress(line => {
            lines.Enqueue(line);
            if (line is "stdout-ready" or "stderr-ready")
                File.WriteAllText(_workspace.GetPath(line), "received");
        });
        // The command cannot produce its tails or succeed until both live notifications arrive.
        // A deadline bounds a regressed test without introducing a production execution timeout.
        var button = Button(
            "[Console]::Out.Write(\"stdout-ready`r`n\")\n" +
            "[Console]::Error.Write(\"stderr-ready`n\")\n" +
            "$deadline = [DateTime]::UtcNow.AddSeconds(10)\n" +
            "while (!(Test-Path stdout-ready) -or !(Test-Path stderr-ready)) {\n" +
            "    if ([DateTime]::UtcNow -gt $deadline) { exit 88 }\n" +
            "    Start-Sleep -Milliseconds 20\n" +
            "}\n" +
            "[Console]::Out.Write('stdout-tail')\n" +
            "[Console]::Error.Write('stderr-tail')\n" +
            "exit 7");

        var result = await new ButtonCommandExecutionService().ExecuteAsync(button, _snapshot, progress);

        AssertExecutedResult(result, button, 7, "stdout-ready\r\nstdout-tail", "stderr-ready\nstderr-tail");
        Assert.That(lines, Is.EquivalentTo(new[] { "stdout-ready", "stderr-ready", "stdout-tail", "stderr-tail" }));
    }

    [Test]
    public async Task ExecuteAsync_InheritedPipes_FinalizesAfterLauncherExitWithoutStoppingDescendant() {
        Assert.That(WorkspaceIssueFactory.IsPowerShellAvailable(), Is.True);
        _workspace.CreateFile(@".git\HEAD", "ref: refs/heads/test-authored-command\n");
        var readyPath = _workspace.GetPath("descendant-ready").Replace("'", "''");
        var childCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(
            $"[IO.File]::WriteAllText('{readyPath}', 'ready'); Start-Sleep -Seconds 60"));
        var launcherReady = new TaskCompletionSource<Process>(TaskCreationOptions.RunContinuationsAsynchronously);
        var descendantReady = new TaskCompletionSource<Process>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lines = new ConcurrentQueue<string>();
        var progress = new InlineProgress(line => {
            lines.Enqueue(line);
            if (line.StartsWith("launcher:", StringComparison.Ordinal))
                launcherReady.SetResult(Process.GetProcessById(int.Parse(line["launcher:".Length..])));
            if (line.StartsWith("descendant:", StringComparison.Ordinal))
                descendantReady.SetResult(Process.GetProcessById(int.Parse(line["descendant:".Length..])));
        });
        // Process.Start without redirection inherits BOTH captured pipe handles. The descendant
        // signals readiness but does not close those handles until explicitly cleaned up by this test.
        var button = Button(
            "[Console]::Out.WriteLine(\"launcher:$PID\")\n" +
            "$info = [Diagnostics.ProcessStartInfo]::new([Environment]::ProcessPath)\n" +
            "$info.UseShellExecute = $false\n" +
            "$info.CreateNoWindow = $true\n" +
            "$info.WorkingDirectory = [IO.Path]::GetTempPath()\n" +
            $"$info.Arguments = '-NoProfile -NonInteractive -EncodedCommand {childCommand}'\n" +
            "$child = [Diagnostics.Process]::Start($info)\n" +
            "[Console]::Out.WriteLine(\"descendant:$($child.Id)\")\n" +
            "$deadline = [DateTime]::UtcNow.AddSeconds(15)\n" +
            "while (!(Test-Path descendant-ready) -or !(Test-Path allow-launcher-exit)) {\n" +
            "    if ([DateTime]::UtcNow -gt $deadline) { exit 88 }\n" +
            "    Start-Sleep -Milliseconds 20\n" +
            "}\n" +
            "[Console]::Out.Write('stdout-tail')\n" +
            "[Console]::Error.Write('stderr-tail')\n" +
            "exit 7");
        var execution = new ButtonCommandExecutionService().ExecuteAsync(button, _snapshot, progress);
        Process? launcher = null;
        Process? descendant = null;
        try {
            launcher = await launcherReady.Task.WaitAsync(TimeSpan.FromSeconds(15));
            descendant = await descendantReady.Task.WaitAsync(TimeSpan.FromSeconds(15));
            // Acquire the launcher's handle before allowing it to exit, independently of pipe EOF.
            _ = launcher.SafeHandle;
            _ = descendant.SafeHandle;
            File.WriteAllText(_workspace.GetPath("allow-launcher-exit"), "exit");
            await launcher.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));

            var result = await execution.WaitAsync(TimeSpan.FromSeconds(3));

            AssertExecutedResult(result, button, 7,
                $"launcher:{launcher.Id}{Environment.NewLine}descendant:{descendant.Id}{Environment.NewLine}stdout-tail",
                "stderr-tail");
            Assert.Multiple(() => {
                Assert.That(descendant.HasExited, Is.False, "Finalization must not kill the inherited-handle owner.");
                Assert.That(lines, Is.EquivalentTo(new[] {
                    $"launcher:{launcher.Id}", $"descendant:{descendant.Id}", "stdout-tail", "stderr-tail" }));
            });
        }
        finally {
            // These handles belong only to this test; never discover or terminate unrelated processes.
            launcher ??= launcherReady.Task.IsCompletedSuccessfully ? launcherReady.Task.Result : null;
            descendant ??= descendantReady.Task.IsCompletedSuccessfully ? descendantReady.Task.Result : null;
            foreach (var ownedProcess in new[] { launcher, descendant }) {
                if (ownedProcess is null) continue;
                using (ownedProcess) {
                    if (!ownedProcess.HasExited)
                        ownedProcess.Kill();
                    await ownedProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
            await execution.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Test]
    public async Task ExecuteAsync_InvalidTestOwnedPowerShellImage_ReturnsStartErrorWithoutExitCode() {
        Assert.That(WorkspaceIssueFactory.IsPowerShellAvailable(), Is.True);
        _workspace.CreateFile(@".git\HEAD", "ref: refs/heads/test-authored-command\n");
        _workspace.CreateFile("pwsh.exe", "Test-owned invalid executable image; contains no executable code.");
        var button = Button("Write-Output 'must-not-run'");
        var originalDirectory = Environment.CurrentDirectory;
        ButtonExecutionResult result;
        try {
            // Windows searches the host's current directory before PATH. Only this nonparallel
            // test process changes directory; no installed executable or environment variable is modified.
            Environment.CurrentDirectory = _workspace.RootPath;
            Assert.That(WorkspaceIssueFactory.IsPowerShellAvailable(), Is.True);
            result = await new ButtonCommandExecutionService().ExecuteAsync(button, _snapshot);
        }
        finally {
            Environment.CurrentDirectory = originalDirectory;
        }

        Assert.Multiple(() => {
            Assert.That(result.Button, Is.EqualTo(button));
            Assert.That(result.Workspace, Is.EqualTo(_snapshot));
            Assert.That(result.ExitCode, Is.Null);
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(result.Error, Does.Contain("pwsh.exe").And.Contain(_workspace.RootPath));
            Assert.That(result.Error, Does.Not.Contain("not available on PATH"));
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.CleanupError, Is.Null);
            Assert.That(Directory.Exists(Path.Combine(_stateDirectory, "button-runs")), Is.True,
                "Script creation must have completed before the process-start failure.");
            Assert.That(Directory.GetFiles(Path.Combine(_stateDirectory, "button-runs")), Is.Empty);
        });
    }

    private void AssertExecutedResult(ButtonExecutionResult result, ButtonDefinition button, int exitCode,
        string stdout, string stderr) {
        Assert.Multiple(() => {
            Assert.That(result.Button, Is.EqualTo(button));
            Assert.That(result.Workspace, Is.EqualTo(_snapshot));
            Assert.That(result.ExitCode, Is.EqualTo(exitCode));
            Assert.That(result.StandardOutput, Is.EqualTo(stdout));
            Assert.That(result.StandardError, Is.EqualTo(stderr));
            Assert.That(result.Error, Is.EqualTo(exitCode == 0 ? null : $"Button command exited with code {exitCode}."));
            Assert.That(result.Succeeded, Is.EqualTo(exitCode == 0));
            Assert.That(result.CleanupError, Is.Null);
            Assert.That(Directory.GetFiles(Path.Combine(_stateDirectory, "button-runs")), Is.Empty);
        });
    }

    private ButtonInvocation Prepare(ButtonDefinition button) =>
        ButtonCommandExecutionService.PrepareInvocation(button, _snapshot, "main");

    private sealed class InlineProgress(Action<string> report) : IProgress<string> {
        public void Report(string value) => report(value);
    }

    private sealed class ChunkedReader(string text, int chunkSize) : StringReader(text) {
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(chunkSize, buffer.Length)], cancellationToken);
    }

    private sealed class HeldOpenReader(string text) : TextReader {
        private bool _returnedText;
        public TaskCompletionSource ReadPending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<int> PendingRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default) {
            if (!_returnedText) {
                text.AsMemory().CopyTo(buffer);
                _returnedText = true;
                return ValueTask.FromResult(text.Length);
            }
            ReadPending.SetResult();
            return new ValueTask<int>(PendingRead.Task);
        }
    }

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
