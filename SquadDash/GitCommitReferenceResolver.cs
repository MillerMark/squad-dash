using System.Diagnostics;

namespace SquadDash;

/// <summary>
/// Resolves transcript hash-shaped tokens against the active repository before the markdown
/// renderer presents them as commit links. Results are cached because a restored transcript can
/// contain the same abbreviated SHA in several places.
/// </summary>
internal sealed class GitCommitReferenceResolver
{
    private static readonly TimeSpan NegativeCacheLifetime = TimeSpan.FromSeconds(30);
    private const int ResolveTimeoutMilliseconds = 500;

    private readonly Func<string?> _getWorkspacePath;
    private readonly Func<string, string, bool> _probe;
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private string? _cachedWorkspacePath;

    internal GitCommitReferenceResolver(
        Func<string?> getWorkspacePath,
        Func<string, string, bool>? probe = null)
    {
        _getWorkspacePath = getWorkspacePath;
        _probe = probe ?? TryResolve;
    }

    internal bool ResolvesToCommit(string candidate)
    {
        var workspacePath = _getWorkspacePath()?.Trim();
        if (string.IsNullOrWhiteSpace(workspacePath) || string.IsNullOrWhiteSpace(candidate))
            return false;

        lock (_gate)
        {
            if (!string.Equals(_cachedWorkspacePath, workspacePath, StringComparison.OrdinalIgnoreCase))
            {
                _cachedWorkspacePath = workspacePath;
                _cache.Clear();
            }

            if (_cache.TryGetValue(candidate, out var cached) &&
                (cached.Resolves || DateTime.UtcNow - cached.CheckedAtUtc < NegativeCacheLifetime))
            {
                return cached.Resolves;
            }
        }

        var resolves = _probe(workspacePath, candidate);
        lock (_gate)
            _cache[candidate] = new CacheEntry(resolves, DateTime.UtcNow);
        return resolves;
    }

    private static bool TryResolve(string workspacePath, string candidate)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("git")
                {
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    WorkingDirectory = workspacePath,
                },
            };
            process.StartInfo.ArgumentList.Add("rev-parse");
            process.StartInfo.ArgumentList.Add("--verify");
            process.StartInfo.ArgumentList.Add("--quiet");
            process.StartInfo.ArgumentList.Add($"{candidate}^{{commit}}");
            process.StartInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";

            if (!process.Start())
                return false;
            if (!process.WaitForExit(ResolveTimeoutMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); }
                catch { }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private sealed record CacheEntry(bool Resolves, DateTime CheckedAtUtc);
}
