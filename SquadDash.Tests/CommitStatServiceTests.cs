using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class CommitStatServiceTests
{
    // ── ParseBatchOutput ──────────────────────────────────────────────────────

    [Test]
    public void ParseBatchOutput_SingleCommit_ReturnsCorrectStats()
    {
        var batch = new List<CommitStatRequest> {
            new("abc123", "Feature-A", new DateOnly(2026, 6, 1))
        };
        var output = """
            STAT:abc123fullshaxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

            3	2	src/foo.cs
            1	0	docs/bar.md

            """;

        var results = CommitStatService.ParseBatchOutput(output, batch);

        Assert.That(results, Has.Count.EqualTo(1));
        var r = results[0];
        Assert.That(r.Sha,           Is.EqualTo("abc123"));
        Assert.That(r.FilesChanged,  Is.EqualTo(2));
        Assert.That(r.Insertions,    Is.EqualTo(4));
        Assert.That(r.Deletions,     Is.EqualTo(2));
        Assert.That(r.IsFound,       Is.True);
        Assert.That(r.FeatureGroupId, Is.EqualTo("Feature-A"));
        Assert.That(r.TurnDate,      Is.EqualTo(new DateOnly(2026, 6, 1)));
    }

    [Test]
    public void ParseBatchOutput_MultipleCommits_ReturnsAllResults()
    {
        var batch = new List<CommitStatRequest> {
            new("sha1aaa", "GroupA", new DateOnly(2026, 6, 1)),
            new("sha2bbb", "GroupB", new DateOnly(2026, 6, 2)),
        };
        var output = """
            STAT:sha1aaafullxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

            5	1	src/a.cs

            STAT:sha2bbbfullxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

            2	3	src/b.cs
            1	0	src/c.cs

            """;

        var results = CommitStatService.ParseBatchOutput(output, batch);

        Assert.That(results, Has.Count.EqualTo(2));

        var a = results.Single(r => r.Sha == "sha1aaa");
        Assert.That(a.FilesChanged, Is.EqualTo(1));
        Assert.That(a.Insertions,   Is.EqualTo(5));
        Assert.That(a.Deletions,    Is.EqualTo(1));

        var b = results.Single(r => r.Sha == "sha2bbb");
        Assert.That(b.FilesChanged, Is.EqualTo(2));
        Assert.That(b.Insertions,   Is.EqualTo(3));
        Assert.That(b.Deletions,    Is.EqualTo(3));
    }

    [Test]
    public void ParseBatchOutput_NullFeatureGroup_PreservedInResult()
    {
        var batch = new List<CommitStatRequest> {
            new("abc123", null, new DateOnly(2026, 6, 3))
        };
        var output = "STAT:abc123fullxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\n\n1\t1\tsrc/x.cs\n";

        var results = CommitStatService.ParseBatchOutput(output, batch);

        Assert.That(results[0].FeatureGroupId, Is.Null);
    }

    [Test]
    public void ParseBatchOutput_BinaryFile_CountedAsFileChangedNoInsDel()
    {
        var batch = new List<CommitStatRequest> {
            new("abc123", null, new DateOnly(2026, 6, 1))
        };
        // Binary files have '-' for insertions and deletions in numstat output.
        var output = "STAT:abc123xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\n\n-\t-\tassets/image.png\n";

        var results = CommitStatService.ParseBatchOutput(output, batch);

        Assert.That(results[0].FilesChanged, Is.EqualTo(1));
        Assert.That(results[0].Insertions,   Is.EqualTo(0));
        Assert.That(results[0].Deletions,    Is.EqualTo(0));
    }

    [Test]
    public void ParseBatchOutput_EmptyOutput_ReturnsEmpty()
    {
        var batch = new List<CommitStatRequest> {
            new("unknown1", "G", new DateOnly(2026, 1, 1))
        };

        var results = CommitStatService.ParseBatchOutput(string.Empty, batch);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void ParseBatchOutput_ShaShorterThanGitOutput_MatchesByPrefix()
    {
        // The caller used a 7-char short SHA; git returns the full 40-char SHA.
        var shortSha = "abc1234";
        var fullSha  = "abc1234def5678901234567890123456789012345";
        var batch = new List<CommitStatRequest> {
            new(shortSha, "Feature", new DateOnly(2026, 6, 5))
        };
        var output = $"STAT:{fullSha}\n\n2\t1\tsrc/x.cs\n";

        var results = CommitStatService.ParseBatchOutput(output, batch);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Sha,          Is.EqualTo(shortSha));
        Assert.That(results[0].FilesChanged,  Is.EqualTo(1));
    }

    [Test]
    public void ParseBatchOutput_ZeroStatCommit_IsFoundTrue()
    {
        // A valid merge commit may have no file changes.
        var batch = new List<CommitStatRequest> {
            new("abc123", "G", new DateOnly(2026, 6, 1))
        };
        var output = "STAT:abc123xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\n\n";

        var results = CommitStatService.ParseBatchOutput(output, batch);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].FilesChanged, Is.EqualTo(0));
        Assert.That(results[0].IsFound,      Is.True);
    }

    // ── GetStatsAsync (cache + IsFound=false) ─────────────────────────────────

    [Test]
    public async Task GetStatsAsync_EmptyInput_ReturnsEmpty()
    {
        var svc = new CommitStatService((_,_) => Task.FromResult(string.Empty));
        var result = await svc.GetStatsAsync([]);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetStatsAsync_CachedSha_DoesNotCallGit()
    {
        var gitCallCount = 0;
        var sha  = "aabbcc";
        var date = new DateOnly(2026, 7, 1);
        var req  = new CommitStatRequest(sha, "G", date);

        var gitOutput = $"STAT:{sha}xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\n\n1\t0\tsrc/x.cs\n";
        Task<string> FakeGit(string _, CancellationToken __)
        {
            gitCallCount++;
            return Task.FromResult(gitOutput);
        }

        var svc = new CommitStatService(FakeGit);

        // First call — populates cache.
        await svc.GetStatsAsync([req]);
        Assert.That(gitCallCount, Is.EqualTo(1));

        // Second call — should be served entirely from cache.
        await svc.GetStatsAsync([req]);
        Assert.That(gitCallCount, Is.EqualTo(1), "git must not be called again for cached SHA");
    }

    [Test]
    public async Task GetStatsAsync_GitReturnsNoOutput_ShaMarkedNotFound()
    {
        var req = new CommitStatRequest("deadbeef", "G", new DateOnly(2026, 7, 1));
        var svc = new CommitStatService((_, _) => Task.FromResult(string.Empty));

        var results = await svc.GetStatsAsync([req]);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].IsFound, Is.False);
    }

    [Test]
    public async Task GetStatsAsync_IsFoundFalseCached_NotRetried()
    {
        var gitCallCount = 0;
        var req = new CommitStatRequest("deadbeef", "G", new DateOnly(2026, 7, 1));
        Task<string> FakeGit(string _, CancellationToken __)
        {
            gitCallCount++;
            return Task.FromResult(string.Empty); // no output → IsFound=false
        }

        var svc = new CommitStatService(FakeGit);

        await svc.GetStatsAsync([req]); // IsFound=false cached
        await svc.GetStatsAsync([req]); // must not re-query git

        Assert.That(gitCallCount, Is.EqualTo(1), "IsFound=false SHA must be cached and not retried");
    }

    [Test]
    public async Task GetStatsAsync_GitRunnerThrows_ShaMarkedNotFound()
    {
        var req = new CommitStatRequest("badf00d", "G", new DateOnly(2026, 7, 1));
        var svc = new CommitStatService((_, _) => Task.FromException<string>(new Exception("git error")));

        var results = await svc.GetStatsAsync([req]);

        Assert.That(results[0].IsFound, Is.False);
    }

    [Test]
    public async Task GetStatsAsync_ProgressCalledPerBatch()
    {
        var progressBatches = new List<IReadOnlyList<CommitStatResult>>();
        var progress        = new Progress<IReadOnlyList<CommitStatResult>>(b => progressBatches.Add(b));

        // Two SHAs that resolve → each will end up in one batch (BatchSize=50, only 2 here).
        var requests = new[] {
            new CommitStatRequest("aaa111", "G", new DateOnly(2026, 7, 1)),
            new CommitStatRequest("bbb222", "G", new DateOnly(2026, 7, 2)),
        };

        Task<string> FakeGit(string args, CancellationToken _)
        {
            var out_ = new System.Text.StringBuilder();
            if (args.Contains("aaa111"))
                out_.AppendLine($"STAT:aaa111xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\n\n1\t0\tsrc/a.cs");
            if (args.Contains("bbb222"))
                out_.AppendLine($"STAT:bbb222xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\n\n2\t1\tsrc/b.cs");
            return Task.FromResult(out_.ToString());
        }

        var svc     = new CommitStatService(FakeGit);
        var results = await svc.GetStatsAsync(requests, progress);

        // Allow the Progress<T> continuation to fire on the thread pool.
        await Task.Delay(50);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(progressBatches, Has.Count.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task TryGetCached_BeforeFetch_ReturnsNull()
    {
        var svc = new CommitStatService((_, _) => Task.FromResult(string.Empty));
        Assert.That(svc.TryGetCached("anything"), Is.Null);
    }

    [Test]
    public async Task TryGetCached_AfterFetch_ReturnsCachedResult()
    {
        var sha  = "abc999";
        var req  = new CommitStatRequest(sha, "G", new DateOnly(2026, 7, 1));
        var svc  = new CommitStatService((_, _) =>
            Task.FromResult($"STAT:{sha}xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\n\n3\t1\tsrc/x.cs\n"));

        await svc.GetStatsAsync([req]);

        var cached = svc.TryGetCached(sha);
        Assert.That(cached,              Is.Not.Null);
        Assert.That(cached!.FilesChanged, Is.EqualTo(1));
        Assert.That(cached.IsFound,       Is.True);
    }

    // ── Batch partitioning ────────────────────────────────────────────────────

    [Test]
    public async Task GetStatsAsync_MoreThanBatchSize_SpawnsMultipleBatches()
    {
        var batchCount    = 0;
        var totalRequests = CommitStatService.BatchSize + 5; // crosses the batch boundary

        var requests = Enumerable.Range(0, totalRequests)
            .Select(i => new CommitStatRequest($"sha{i:D4}", null, new DateOnly(2026, 1, 1)))
            .ToList();

        Task<string> FakeGit(string args, CancellationToken _)
        {
            Interlocked.Increment(ref batchCount);
            return Task.FromResult(string.Empty);
        }

        var svc = new CommitStatService(FakeGit);
        await svc.GetStatsAsync(requests);

        Assert.That(batchCount, Is.EqualTo(2), "55 SHAs should produce 2 batches of 50/5");
    }
}
