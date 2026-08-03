using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash;

/// <summary>Best-effort human identity for durable approval audit records.</summary>
internal static class HumanApprovalIdentityResolver
{
    private static readonly ConcurrentDictionary<string, string> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    internal static async Task<string> ResolveAsync(
        string workspace,
        CancellationToken cancellationToken = default)
    {
        if (Cache.TryGetValue(workspace, out var cached)) return cached;

        var name = await TryRunAsync("git", workspace, cancellationToken,
            "config", "--get", "user.name").ConfigureAwait(false);
        var email = await TryRunAsync("git", workspace, cancellationToken,
            "config", "--get", "user.email").ConfigureAwait(false);
        var login = await TryRunAsync("gh", workspace, cancellationToken,
            "api", "user", "--jq", ".login").ConfigureAwait(false);

        var resolved = !string.IsNullOrWhiteSpace(login)
            ? !string.IsNullOrWhiteSpace(name)
                ? $"{name.Trim()} (@{login.Trim().TrimStart('@')})"
                : $"@{login.Trim().TrimStart('@')}"
            : !string.IsNullOrWhiteSpace(name)
                ? name.Trim()
                : !string.IsNullOrWhiteSpace(email)
                    ? email.Trim()
                    : Environment.UserName;
        Cache[workspace] = resolved;
        return resolved;
    }

    private static async Task<string?> TryRunAsync(
        string executable,
        string workspace,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var startInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = workspace,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo);
            if (process is null) return null;
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = (await outputTask.ConfigureAwait(false)).Trim();
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
