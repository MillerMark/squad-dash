using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace SquadDash;

internal sealed class SystemProcessRunner : IProcessRunner
{
    public async Task<CliInvocationResult> RunAsync(string fileName, string[] args, TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = BuildStartInfo(fileName, args),
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                SquadDashTrace.Write("CliResolution", $"Failed to start process: {fileName}");
                return new CliInvocationResult(-1, string.Empty, $"Failed to start process: {fileName}", false);
            }
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("CliResolution", $"Process start failed for {fileName}: {ex.Message}");
            return new CliInvocationResult(-1, string.Empty, ex.Message, false);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = new System.Threading.CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            var timedOutStdout = await ReadSafelyAsync(stdoutTask).ConfigureAwait(false);
            var timedOutStderr = await ReadSafelyAsync(stderrTask).ConfigureAwait(false);
            return new CliInvocationResult(-1, timedOutStdout, timedOutStderr, true);
        }

        var stdout = await ReadSafelyAsync(stdoutTask).ConfigureAwait(false);
        var stderr = await ReadSafelyAsync(stderrTask).ConfigureAwait(false);
        return new CliInvocationResult(process.ExitCode, stdout, stderr, false);
    }

    private static ProcessStartInfo BuildStartInfo(string fileName, string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        return startInfo;
    }

    private static async Task<string?> ReadSafelyAsync(Task<string> readTask)
    {
        try
        {
            return await readTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("CliResolution", $"Process output read failed: {ex.Message}");
            return string.Empty;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("CliResolution", $"Timed-out process kill failed: {ex.Message}");
        }
    }
}
