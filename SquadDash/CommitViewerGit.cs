using System.Diagnostics;
using System.IO;
using System.Text;

namespace SquadDash;

internal static class CommitViewerGit
{
    internal static async Task<string> ResolveRepositoryRootAsync(string workspacePath)
    {
        var output = await RunAsync(workspacePath, "rev-parse", "--show-toplevel");
        var repositoryRoot = output.Trim();
        if (string.IsNullOrWhiteSpace(repositoryRoot))
            throw new InvalidOperationException("Git did not return a repository root for this workspace.");

        return Path.GetFullPath(repositoryRoot);
    }

    internal static async Task<string> RunAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Git could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0) throw new InvalidOperationException(error.Trim());
        return output;
    }
}
