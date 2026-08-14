using System.Diagnostics;
using System.Text;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class SquadCliAdapterTests {
    [Test]
    public void CreatePowerShellCommandStartInfo_EncodesExecutableScript() {
        const string appRoot = @"D:\Drive\Source\SquadDash-public";
        var action = new WorkspaceIssueAction(
            "Update Squad CLI",
            WorkspaceIssueActionKind.LaunchPowerShellCommand,
            "npm install @bradygaster/squad-cli@0.12.0");

        var startInfo = SquadCliAdapter.CreatePowerShellCommandStartInfo(appRoot, action);
        var script = DecodeScript(startInfo.Arguments);

        Assert.Multiple(() => {
            Assert.That(startInfo.FileName, Is.EqualTo("powershell.exe"));
            Assert.That(startInfo.Arguments, Does.StartWith("-NoExit -EncodedCommand "));
            Assert.That(startInfo.WorkingDirectory, Is.EqualTo(appRoot));
            Assert.That(startInfo.UseShellExecute, Is.True);
            Assert.That(script, Does.Contain($"Set-Location -LiteralPath '{appRoot}'"));
            Assert.That(script, Does.Contain("npm install @bradygaster/squad-cli@0.12.0"));
            Assert.That(script, Does.Contain("Update Squad CLI completed."));
        });
    }

    [Test]
    public void CreatePowerShellCommandStartInfo_EscapesApostropheInWorkspacePath() {
        const string appRoot = @"D:\Developer's Files\SquadDash";
        var action = new WorkspaceIssueAction(
            "Update Squad CLI",
            WorkspaceIssueActionKind.LaunchPowerShellCommand,
            "npm install @bradygaster/squad-cli@0.12.0");

        var startInfo = SquadCliAdapter.CreatePowerShellCommandStartInfo(appRoot, action);
        var script = DecodeScript(startInfo.Arguments);

        Assert.That(script, Does.Contain("Set-Location -LiteralPath 'D:\\Developer''s Files\\SquadDash'"));
    }

    [Test]
    public void CreatePowerShellCommandStartInfo_RunsCommandInsteadOfPrintingIt() {
        var appRoot = TestContext.CurrentContext.WorkDirectory;
        var action = new WorkspaceIssueAction(
            "Update Squad CLI",
            WorkspaceIssueActionKind.LaunchPowerShellCommand,
            "cmd.exe /c exit 0");
        var startInfo = SquadCliAdapter.CreatePowerShellCommandStartInfo(appRoot, action);
        startInfo.Arguments = startInfo.Arguments.Replace("-NoExit ", string.Empty, StringComparison.Ordinal);
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        using var process = Process.Start(startInfo);
        Assert.That(process, Is.Not.Null);
        var standardOutput = process!.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        Assert.That(process.WaitForExit(10_000), Is.True, standardError);

        Assert.Multiple(() => {
            Assert.That(process.ExitCode, Is.EqualTo(0), standardError);
            Assert.That(standardOutput, Does.Contain("Update Squad CLI completed."));
            Assert.That(standardOutput, Does.Not.Contain("$machinePath ="));
        });
    }

    private static string DecodeScript(string arguments) {
        const string marker = "-EncodedCommand ";
        var markerIndex = arguments.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(markerIndex, Is.GreaterThanOrEqualTo(0));
        var encodedScript = arguments[(markerIndex + marker.Length)..];
        return Encoding.Unicode.GetString(Convert.FromBase64String(encodedScript));
    }
}
