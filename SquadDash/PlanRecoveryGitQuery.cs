using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace SquadDash;

internal sealed class PlanRecoveryGitQuery
{
    internal const int DefaultTimeoutMilliseconds = 2000;

    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<CommandResult>> _run;
    private readonly TimeSpan _timeout;
    private readonly Action<string> _log;

    internal PlanRecoveryGitQuery(
        Func<string, IReadOnlyList<string>, CancellationToken, Task<CommandResult>>? run = null,
        TimeSpan? timeout = null,
        Action<string>? log = null)
    {
        _run = run ?? RunAsync;
        _timeout = timeout ?? TimeSpan.FromMilliseconds(DefaultTimeoutMilliseconds);
        if (_timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        _log = log ?? (message => SquadDashTrace.Write("PlanRecovery", message));
    }

    internal async Task<string?> TryReadHeadAsync(string? workspace, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspace))
        {
            _log("Skipped recovery HEAD query: workspace unavailable.");
            return null;
        }

        var result = await QueryAsync(
            ct => _run(workspace, ["rev-parse", "HEAD"], ct), cancellationToken).ConfigureAwait(false);
        if (!result.Completed || result.ExitCode != 0)
            return null;

        var head = result.StandardOutput.Trim();
        if (!string.IsNullOrWhiteSpace(head)) return head;
        _log("Recovery HEAD query returned no commit; retaining prior presentation.");
        return null;
    }

    internal async Task<bool> IsAncestorAsync(
        string? workspace, string? ancestor, string? descendant, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspace) ||
            string.IsNullOrWhiteSpace(ancestor) ||
            string.IsNullOrWhiteSpace(descendant))
        {
            _log("Skipped recovery ancestry query: workspace or commit unavailable.");
            return false;
        }

        var result = await QueryAsync(
            ct => _run(workspace, ["merge-base", "--is-ancestor", ancestor, descendant], ct),
            cancellationToken).ConfigureAwait(false);
        return result.Completed && result.ExitCode == 0;
    }

    internal async Task<PlanRecoveryGitSnapshot> ReadSnapshotAsync(
        string? workspace, IEnumerable<string> commits, CancellationToken cancellationToken = default)
    {
        var head = await TryReadHeadAsync(workspace, cancellationToken).ConfigureAwait(false);
        var ancestors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (head is not null)
        {
            foreach (var commit in commits.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (string.Equals(commit, head, StringComparison.OrdinalIgnoreCase) ||
                    await IsAncestorAsync(workspace, commit, head, cancellationToken).ConfigureAwait(false))
                    ancestors.Add(commit);
            }
        }
        return new PlanRecoveryGitSnapshot(workspace, head, ancestors);
    }

    internal async Task<string> RunRequiredAsync(
        string workspace, string arguments, CancellationToken cancellationToken = default)
    {
        var startInfo = CreateStartInfo(workspace);
        startInfo.Arguments = arguments;
        var result = await QueryAsync(
            ct => RunProcessAsync(startInfo, ct), cancellationToken).ConfigureAwait(false);
        if (!result.Completed || result.ExitCode != 0)
            throw new InvalidOperationException($"Recovery Git query failed or timed out: {arguments}");
        return result.StandardOutput.Trim();
    }

    private async Task<CommandResult> QueryAsync(
        Func<CancellationToken, Task<CommandResult>> run, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        var token = timeout.Token;
        try
        {
            // Process.Start and injected runners cannot capture or block the dispatcher.
            var result = await Task.Run(async () =>
            {
                try
                {
                    return await run(token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException or Win32Exception or
                                           IOException or InvalidOperationException)
                {
                    _log($"Recovery Git query failed: {ex.Message}");
                    return CommandResult.Failed;
                }
            }, token).WaitAsync(token).ConfigureAwait(false);
            if (!result.Completed || result.ExitCode != 0)
                _log($"Recovery Git query unavailable (exit={result.ExitCode}); retaining prior presentation.");
            return result;
        }
        catch (OperationCanceledException ex)
        {
            _log($"Recovery Git query cancelled or timed out after at most {_timeout.TotalMilliseconds} ms: {ex.Message}");
            return CommandResult.Failed;
        }
    }

    private static ProcessStartInfo CreateStartInfo(string workspace)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        return startInfo;
    }

    private Task<CommandResult> RunAsync(
        string workspace, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(workspace);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        return RunProcessAsync(startInfo, cancellationToken);
    }

    private async Task<CommandResult> RunProcessAsync(
        ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start recovery Git process.");
        using var cancellation = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                _log($"Could not terminate cancelled recovery Git process: {ex.Message}");
            }
        });
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(cancellationToken)).ConfigureAwait(false);
        if (process.ExitCode != 0)
            _log($"Recovery Git exited {process.ExitCode}: {await stderr.ConfigureAwait(false)}");
        return new CommandResult(true, process.ExitCode, await stdout.ConfigureAwait(false));
    }

    internal sealed record CommandResult(bool Completed, int ExitCode, string StandardOutput)
    {
        internal static CommandResult Failed { get; } = new(false, -1, string.Empty);
    }
}

internal sealed record PlanRecoveryGitSnapshot(
    string? Workspace, string? Head, IReadOnlySet<string> Ancestors)
{
    internal static PlanRecoveryGitSnapshot SelectForPresentation(
        PlanRecoveryGitSnapshot? previous, PlanRecoveryGitSnapshot candidate) =>
        candidate.Head is null && previous?.Head is not null &&
        string.Equals(previous.Workspace, candidate.Workspace, StringComparison.OrdinalIgnoreCase)
            ? previous
            : candidate;

    internal bool IsAncestor(string ancestor, string descendant) =>
        Head is not null &&
        string.Equals(Head, descendant, StringComparison.OrdinalIgnoreCase) &&
        Ancestors.Contains(ancestor);
}

internal sealed class PlanRecoveryGitPresentationCache
{
    private string? _workspace;
    private int _version;
    private bool _refreshSurfaces;
    private PlanRecoveryGitSnapshot? _rendered;

    internal PlanRecoveryGitSnapshot? Current { get; private set; }

    internal int BeginRefresh(string? workspace, bool refreshSurfaces)
    {
        if (!string.Equals(_workspace, workspace, StringComparison.OrdinalIgnoreCase))
        {
            _workspace = workspace;
            Current = null;
            _rendered = null;
            _refreshSurfaces = false;
        }
        _refreshSurfaces |= refreshSurfaces;
        return ++_version;
    }

    internal bool TryApply(int version, PlanRecoveryGitSnapshot candidate, out bool refreshSurfaces)
    {
        refreshSurfaces = false;
        if (version != _version ||
            !string.Equals(_workspace, candidate.Workspace, StringComparison.OrdinalIgnoreCase))
            return false;

        Current = PlanRecoveryGitSnapshot.SelectForPresentation(Current, candidate);
        if (candidate.Head is not null && _refreshSurfaces)
        {
            refreshSurfaces = _rendered is null ||
                !string.Equals(_rendered.Head, Current.Head, StringComparison.OrdinalIgnoreCase) ||
                !_rendered.Ancestors.SetEquals(Current.Ancestors);
            if (!refreshSurfaces) _refreshSurfaces = false;
        }
        return true;
    }

    internal void MarkRendered()
    {
        _rendered = Current;
        _refreshSurfaces = false;
    }
}
