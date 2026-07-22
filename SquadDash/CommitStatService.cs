namespace SquadDash;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

/// <summary>
/// Delegate that executes a single <c>git</c> command in the workspace directory and returns stdout.
/// Injected at construction so unit tests can supply a fake without spawning processes.
/// </summary>
internal delegate Task<string> GitCommandRunner(string gitArguments, CancellationToken cancellationToken);

/// <summary>
/// Production and test implementation of <see cref="ICommitStatService"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Cache: <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed on SHA (case-insensitive).
///   Once stored, a result is immutable — no invalidation. <c>IsFound = false</c> SHAs are also
///   cached so they are never retried.</item>
/// <item>Fetch: uncached SHAs are batched into groups of <see cref="BatchSize"/> and dispatched
///   with bounded parallelism of <see cref="MaxParallelism"/> concurrent git processes.</item>
/// </list>
/// </remarks>
internal sealed class CommitStatService : ICommitStatService
{
    internal const int BatchSize      = 50;
    internal const int MaxParallelism = 8;

    private readonly GitCommandRunner _git;
    private readonly string?          _diskCachePath;
    private readonly ConcurrentDictionary<string, CommitStatResult> _cache
        = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented        = false,
    };

    /// <param name="workspaceFolderPath">
    ///   Absolute path to the workspace repo root.  Passed to git as its working directory.
    ///   Scoped at construction — no per-call override.
    /// </param>
    public CommitStatService(string workspaceFolderPath)
        : this(MakeRealRunner(workspaceFolderPath), GetDiskCachePath(workspaceFolderPath)) { }

    private static string? GetDiskCachePath(string workspaceFolderPath)
    {
        var gitDir = Path.Combine(workspaceFolderPath, ".git");
        return Directory.Exists(gitDir) ? Path.Combine(gitDir, "squad-commitstats-cache.json") : null;
    }

    /// <summary>Internal constructor for unit tests that supply a fake git runner.</summary>
    internal CommitStatService(GitCommandRunner gitRunner, string? diskCachePath = null)
    {
        _git           = gitRunner ?? throw new ArgumentNullException(nameof(gitRunner));
        _diskCachePath = diskCachePath;
        if (diskCachePath is not null)
            LoadFromDisk(diskCachePath);
    }

    // ── ICommitStatService ────────────────────────────────────────────────────

    public CommitStatResult? TryGetCached(string sha)
        => _cache.TryGetValue(sha, out var r) ? r : null;

    public async Task<IReadOnlyList<CommitStatResult>> GetStatsAsync(
        IEnumerable<CommitStatRequest>              requests,
        IProgress<IReadOnlyList<CommitStatResult>>? progress          = null,
        CancellationToken                           cancellationToken = default)
    {
        var all      = requests.ToList();
        var uncached = all.Where(r => !_cache.ContainsKey(r.Sha)).ToList();

        if (uncached.Count > 0)
            await FetchUncachedAsync(uncached, progress, cancellationToken).ConfigureAwait(false);

        // Any SHA that git could not resolve at all → cache as IsFound = false.
        foreach (var req in uncached.Where(r => !_cache.ContainsKey(r.Sha)))
        {
            var notFound = new CommitStatResult(
                req.Sha, req.FeatureGroupId, req.TurnDate,
                FilesChanged: 0, Insertions: 0, Deletions: 0, IsFound: false,
                TurnStartedAt: req.TurnStartedAt);
            _cache.TryAdd(req.Sha, notFound);
        }

        var results = new List<CommitStatResult>(all.Count);
        foreach (var req in all)
        {
            if (_cache.TryGetValue(req.Sha, out var r))
                results.Add(r);
        }
        return results;
    }

    // ── Disk cache ────────────────────────────────────────────────────────────

    private void LoadFromDisk(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var json    = File.ReadAllText(path);
            var entries = JsonSerializer.Deserialize<List<CommitStatResult>>(json, s_jsonOptions);
            if (entries is null) return;
            foreach (var entry in entries)
                _cache.TryAdd(entry.Sha, entry);
            SquadDashTrace.Write("CommitStatCache", $"Loaded {entries.Count} cached stat entries from disk");
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("CommitStatCache", $"LoadFromDisk error: {ex.Message}");
        }
    }

    private async Task SaveToDiskAsync()
    {
        if (_diskCachePath is null) return;
        try
        {
            var entries = _cache.Values.ToList();
            var tmp     = _diskCachePath + ".tmp";
            await using (var fs = File.Create(tmp))
                await JsonSerializer.SerializeAsync(fs, entries, s_jsonOptions).ConfigureAwait(false);
            File.Move(tmp, _diskCachePath, overwrite: true);
            SquadDashTrace.Write("CommitStatCache", $"Saved {entries.Count} stat entries to disk");
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("CommitStatCache", $"SaveToDisk error: {ex.Message}");
        }
    }

    // ── Batch orchestration ───────────────────────────────────────────────────

    private async Task FetchUncachedAsync(
        List<CommitStatRequest>                     uncached,
        IProgress<IReadOnlyList<CommitStatResult>>? progress,
        CancellationToken                           cancellationToken)
    {
        var batches = uncached
            .Select((req, i) => (req, i))
            .GroupBy(x => x.i / BatchSize, x => x.req)
            .ToList();

        using var sem = new SemaphoreSlim(MaxParallelism);

        var tasks = batches.Select(batch => Task.Run(async () =>
        {
            await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var batchList    = batch.ToList();
                var batchResults = await FetchBatchAsync(batchList, cancellationToken).ConfigureAwait(false);
                foreach (var r in batchResults)
                    _cache.TryAdd(r.Sha, r);
                progress?.Report(batchResults);
            }
            finally
            {
                sem.Release();
            }
        }, cancellationToken)).ToList();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        _ = SaveToDiskAsync(); // fire-and-forget; best-effort
    }

    // ── Single-batch git call ─────────────────────────────────────────────────

    private async Task<List<CommitStatResult>> FetchBatchAsync(
        List<CommitStatRequest> batch,
        CancellationToken       cancellationToken)
    {
        var shaList = string.Join(" ", batch.Select(r => r.Sha));
        var args    = $"log --no-walk --format=\"STAT:%H %aI %s\" --numstat {shaList}";

        string stdout;
        try
        {
            stdout = await _git(args, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // git unavailable or failed — SHAs will be marked IsFound=false by the caller.
            return [];
        }

        return ParseBatchOutput(stdout, batch);
    }

    // ── Output parsing (internal for unit tests) ──────────────────────────────

    /// <summary>
    /// Parses the output of
    /// <c>git log --no-walk --format="STAT:%H %aI" --numstat sha1 sha2 ...</c>.
    /// </summary>
    /// <remarks>
    /// Expected output shape per commit:
    /// <code>
    /// STAT:abc123fullsha 2024-01-15T10:30:00+05:30
    ///
    /// 3	2	src/foo.cs
    /// 1	0	docs/bar.md
    ///
    /// STAT:def456fullsha 2024-01-16T08:00:00+00:00
    /// ...
    /// </code>
    /// Binary files produce <c>-\t-\tfilename</c> lines; those are counted as a file changed
    /// but contribute zero insertions/deletions.
    /// </remarks>
    internal static List<CommitStatResult> ParseBatchOutput(
        string                  output,
        List<CommitStatRequest> batch)
    {
        var requestBySha = batch.ToDictionary(r => r.Sha, r => r, StringComparer.OrdinalIgnoreCase);
        var results      = new List<CommitStatResult>(batch.Count);

        CommitStatRequest? current           = null;
        int                files             = 0;
        int                insertions        = 0;
        int                deletions         = 0;
        DateTimeOffset?    currentCommitTime = null;
        string?            currentMessage    = null;

        void Flush()
        {
            if (current is null) return;
            results.Add(new CommitStatResult(
                current.Sha, current.FeatureGroupId, current.TurnDate,
                files, insertions, deletions, IsFound: true,
                TurnStartedAt: current.TurnStartedAt,
                CommitTime:    currentCommitTime,
                Message:       currentMessage));
            current           = null;
            files             = 0;
            insertions        = 0;
            deletions         = 0;
            currentCommitTime = null;
            currentMessage    = null;
        }

        foreach (var line in output.AsSpan().EnumerateLines())
        {
            var s = line.ToString();
            if (s.StartsWith("STAT:", StringComparison.Ordinal))
            {
                Flush();
                var rest       = s[5..];
                var firstSpace = rest.IndexOf(' ');
                string gitSha;
                if (firstSpace > 0)
                {
                    gitSha = rest[..firstSpace];
                    var remaining   = rest[(firstSpace + 1)..];
                    var secondSpace = remaining.IndexOf(' ');
                    if (secondSpace > 0)
                    {
                        var tsStr   = remaining[..secondSpace];
                        var subject = remaining[(secondSpace + 1)..].Trim();
                        if (DateTimeOffset.TryParse(tsStr, out var ts))
                            currentCommitTime = ts;
                        if (!string.IsNullOrEmpty(subject))
                            currentMessage = subject;
                    }
                    else
                    {
                        var tsStr = remaining.Trim();
                        if (DateTimeOffset.TryParse(tsStr, out var ts))
                            currentCommitTime = ts;
                    }
                }
                else
                {
                    gitSha = rest;
                }
                current    = MatchRequest(gitSha, requestBySha);
                files      = 0;
                insertions = 0;
                deletions  = 0;
            }
            else if (current is not null && s.Length > 0 && (char.IsAsciiDigit(s[0]) || s[0] == '-'))
            {
                // numstat line: additions \t deletions \t filename
                // binary files use '-' instead of a number.
                var parts = s.Split('\t', 3);
                if (parts.Length >= 2)
                {
                    files++;
                    if (int.TryParse(parts[0], out var ins)) insertions += ins;
                    if (int.TryParse(parts[1], out var del)) deletions  += del;
                }
            }
        }
        Flush();

        return results;
    }

    /// <summary>
    /// Matches the full SHA returned by git back to the original request, which may have used a
    /// short SHA.  Full-SHA exact match is tried first; then prefix matching for short SHAs.
    /// </summary>
    private static CommitStatRequest? MatchRequest(
        string                               gitSha, // always the full SHA from git
        Dictionary<string, CommitStatRequest> byRequestedSha)
    {
        if (byRequestedSha.TryGetValue(gitSha, out var exact))
            return exact;

        // The caller may have used a short SHA (e.g., "abc1234") — the full SHA starts with it.
        foreach (var (queriedSha, req) in byRequestedSha)
        {
            if (gitSha.StartsWith(queriedSha, StringComparison.OrdinalIgnoreCase))
                return req;
        }

        return null;
    }

    // ── Real git runner factory ───────────────────────────────────────────────

    private static GitCommandRunner MakeRealRunner(string workspaceFolderPath)
    {
        return async (args, ct) =>
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory       = workspaceFolderPath,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start git process.");

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            return await stdoutTask.ConfigureAwait(false);
        };
    }
}
