using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash;

/// <summary>
/// Abstraction for running CLI commands during identity resolution.
/// Enables deterministic testing without real subprocess execution.
/// </summary>
internal interface IIdentityCommandRunner
{
    Task<string?> RunAsync(string executable, string workspace, CancellationToken cancellationToken, params string[] arguments);
}

/// <summary>Best-effort human identity for durable approval audit records.</summary>
internal static class HumanApprovalIdentityResolver
{
    private static readonly ConcurrentDictionary<string, string> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    internal static async Task<string> ResolveAsync(
        string workspace,
        CancellationToken cancellationToken = default) =>
        await ResolveAsync(workspace, DefaultCommandRunner.Instance, cancellationToken).ConfigureAwait(false);

    internal static async Task<string> ResolveAsync(
        string workspace,
        IIdentityCommandRunner runner,
        CancellationToken cancellationToken = default)
    {
        if (Cache.TryGetValue(workspace, out var cached)) return cached;

        var name = await runner.RunAsync("git", workspace, cancellationToken,
            "config", "--get", "user.name").ConfigureAwait(false);
        var email = await runner.RunAsync("git", workspace, cancellationToken,
            "config", "--get", "user.email").ConfigureAwait(false);
        var login = await runner.RunAsync("gh", workspace, cancellationToken,
            "api", "user", "--jq", ".login").ConfigureAwait(false);

        var resolved = FormatIdentity(name, email, login);
        Cache[workspace] = resolved;
        return resolved;
    }

    /// <summary>Formats the resolved identity from constituent parts. Pure logic, no I/O.</summary>
    internal static string FormatIdentity(string? name, string? email, string? login) =>
        !string.IsNullOrWhiteSpace(login)
            ? !string.IsNullOrWhiteSpace(name)
                ? $"{name.Trim()} (@{login.Trim().TrimStart('@')})"
                : $"@{login.Trim().TrimStart('@')}"
            : !string.IsNullOrWhiteSpace(name)
                ? name.Trim()
                : !string.IsNullOrWhiteSpace(email)
                    ? email.Trim()
                    : Environment.UserName;

    /// <summary>Clears the workspace identity cache (used in tests).</summary>
    internal static void ClearCache() => Cache.Clear();

    private sealed class DefaultCommandRunner : IIdentityCommandRunner
    {
        internal static readonly DefaultCommandRunner Instance = new();

        public async Task<string?> RunAsync(
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
                SquadDashTrace.Write("ApprovalIdentity", $"{executable} resolution failed: {ex.Message}");
                return null;
            }
        }
    }
}
