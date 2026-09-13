using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using SquadDash;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanRecoveryGitQueryTests
{
    private const string Workspace = @"C:\Source\Repo";
    private const string OldCommit = "ddc9136669";
    private const string Head = "f8f3dd89b9";
    private static readonly TimeSpan HarnessLimit = TimeSpan.FromSeconds(5);

    [Test]
    public async Task TryReadHeadAsync_UsesBoundedArgumentSafeQueryAndTrimsOutput()
    {
        string? capturedWorkspace = null;
        IReadOnlyList<string>? capturedArguments = null;
        var tokenCanBeCanceled = false;
        var query = new PlanRecoveryGitQuery((workspace, arguments, token) =>
        {
            capturedWorkspace = workspace;
            capturedArguments = arguments;
            tokenCanBeCanceled = token.CanBeCanceled;
            return Task.FromResult(Success(Head + "\r\n"));
        });

        var result = await query.TryReadHeadAsync(Workspace).WaitAsync(HarnessLimit);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Head));
            Assert.That(capturedWorkspace, Is.EqualTo(Workspace));
            Assert.That(capturedArguments, Is.EqualTo(new[] { "rev-parse", "HEAD" }));
            Assert.That(tokenCanBeCanceled, Is.True);
            Assert.That(PlanRecoveryGitQuery.DefaultTimeoutMilliseconds, Is.EqualTo(2000));
        });
    }

    [TestCase(false, 0, Head)]
    [TestCase(true, 128, Head)]
    [TestCase(true, 0, " \r\n")]
    public async Task TryReadHeadAsync_IncompleteFailedOrEmptyOutputReturnsNull(
        bool completed, int exitCode, string output)
    {
        var query = new PlanRecoveryGitQuery((_, _, _) => Task.FromResult(
            new PlanRecoveryGitQuery.CommandResult(completed, exitCode, output)));

        Assert.That(await query.TryReadHeadAsync(Workspace).WaitAsync(HarnessLimit), Is.Null);
    }

    [TestCase("")]
    [TestCase(" \r\n\t")]
    public async Task TryReadHeadAsync_SuccessfulButBlankOutputReturnsNullAndLogs(string output)
    {
        var logs = new ConcurrentQueue<string>();
        var query = new PlanRecoveryGitQuery(
            (_, _, _) => Task.FromResult(Success(output)), log: logs.Enqueue);

        var head = await query.TryReadHeadAsync(Workspace).WaitAsync(HarnessLimit);

        Assert.Multiple(() =>
        {
            Assert.That(head, Is.Null);
            Assert.That(logs.Any(message => message.Contains("HEAD", StringComparison.OrdinalIgnoreCase)),
                Is.True, "A successful Git exit without a commit must still be diagnosable.");
        });
    }

    [Test]
    public async Task IsAncestorAsync_UsesSeparateArgumentsAndCancelableToken()
    {
        IReadOnlyList<string>? capturedArguments = null;
        var tokenCanBeCanceled = false;
        var query = new PlanRecoveryGitQuery((_, arguments, token) =>
        {
            capturedArguments = arguments;
            tokenCanBeCanceled = token.CanBeCanceled;
            return Task.FromResult(Success());
        });

        var result = await query.IsAncestorAsync(Workspace, OldCommit, Head).WaitAsync(HarnessLimit);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(tokenCanBeCanceled, Is.True);
            Assert.That(capturedArguments, Is.EqualTo(new[]
            {
                "merge-base", "--is-ancestor", OldCommit, Head,
            }));
        });
    }

    [TestCase(false, 0)]
    [TestCase(true, 1)]
    [TestCase(true, 128)]
    public async Task IsAncestorAsync_IncompleteOrFailedQueryReturnsFalse(bool completed, int exitCode)
    {
        var query = new PlanRecoveryGitQuery((_, _, _) => Task.FromResult(
            new PlanRecoveryGitQuery.CommandResult(completed, exitCode, string.Empty)));

        Assert.That(await query.IsAncestorAsync(Workspace, OldCommit, Head).WaitAsync(HarnessLimit), Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public async Task ReadSnapshotAsync_MissingWorkspaceNeverInvokesGit(string? workspace)
    {
        var calls = 0;
        var query = new PlanRecoveryGitQuery((_, _, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(Success(Head));
        });

        var snapshot = await query.ReadSnapshotAsync(workspace, [OldCommit]).WaitAsync(HarnessLimit);

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.Zero);
            Assert.That(snapshot.Head, Is.Null);
            Assert.That(snapshot.IsAncestor(OldCommit, Head), Is.False);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Timeout_NoncooperatingAsyncRunnerReturnsSafeFallbackAndLogs(bool ancestry)
    {
        var pending = new TaskCompletionSource<PlanRecoveryGitQuery.CommandResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var logs = new ConcurrentQueue<string>();
        var query = new PlanRecoveryGitQuery(
            (_, _, _) => pending.Task,
            TimeSpan.FromMilliseconds(100),
            logs.Enqueue);

        try
        {
            if (ancestry)
                Assert.That(await query.IsAncestorAsync(Workspace, OldCommit, Head).WaitAsync(HarnessLimit), Is.False);
            else
                Assert.That(await query.TryReadHeadAsync(Workspace).WaitAsync(HarnessLimit), Is.Null);
            Assert.That(logs, Is.Not.Empty, "Timeout must be diagnosable, not silently swallowed.");
        }
        finally
        {
            pending.TrySetResult(Success(Head));
        }
    }

    [Test]
    public async Task Timeout_SynchronouslyBlockingRunnerCannotBlockApiOrAcceptLateHead()
    {
        using var release = new ManualResetEventSlim();
        var runnerFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logs = new ConcurrentQueue<string>();
        var query = new PlanRecoveryGitQuery((_, _, _) =>
        {
            try
            {
                release.Wait(HarnessLimit);
                return Task.FromResult(Success(Head));
            }
            finally
            {
                runnerFinished.TrySetResult();
            }
        }, TimeSpan.FromMilliseconds(100), logs.Enqueue);

        try
        {
            var snapshot = await query.ReadSnapshotAsync(Workspace, [OldCommit]).WaitAsync(HarnessLimit);
            Assert.Multiple(() =>
            {
                Assert.That(release.IsSet, Is.False);
                Assert.That(snapshot.Head, Is.Null);
                Assert.That(snapshot.Ancestors, Is.Empty);
                Assert.That(snapshot.IsAncestor(OldCommit, Head), Is.False);
                Assert.That(logs, Is.Not.Empty);
            });
        }
        finally
        {
            release.Set();
            await runnerFinished.Task.WaitAsync(HarnessLimit);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Cancellation_InFlightRunnerIsCanceledAndReturnsSafeFallback(bool ancestry)
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logs = new ConcurrentQueue<string>();
        var query = new PlanRecoveryGitQuery(async (_, _, token) =>
        {
            entered.TrySetResult(token);
            await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
            return Success(Head);
        }, TimeSpan.FromSeconds(30), logs.Enqueue);

        Task operation = ancestry
            ? query.IsAncestorAsync(Workspace, OldCommit, Head, cancellation.Token)
            : query.TryReadHeadAsync(Workspace, cancellation.Token);
        try
        {
            var runnerToken = await entered.Task.WaitAsync(HarnessLimit);
            cancellation.Cancel();
            await operation.WaitAsync(HarnessLimit);
            Assert.Multiple(() =>
            {
                Assert.That(runnerToken.IsCancellationRequested, Is.True);
                Assert.That(logs, Is.Not.Empty);
            });
            if (ancestry)
                Assert.That(await (Task<bool>)operation, Is.False);
            else
                Assert.That(await (Task<string?>)operation, Is.Null);
        }
        finally
        {
            cancellation.Cancel();
        }
    }

    [Test]
    public async Task ReadSnapshotAsync_PreCanceledRequestCannotAcceptStaleEvidence()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var query = new PlanRecoveryGitQuery((_, _, _) => Task.FromResult(Success(Head)));

        var snapshot = await query.ReadSnapshotAsync(Workspace, [OldCommit], cancellation.Token)
            .WaitAsync(HarnessLimit);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Head, Is.Null);
            Assert.That(snapshot.Ancestors, Is.Empty);
            Assert.That(snapshot.IsAncestor(OldCommit, Head), Is.False);
        });
    }

    private static IEnumerable<Exception> ExpectedFailures()
    {
        yield return new Win32Exception("git unavailable");
        yield return new IOException("pipe closed");
        yield return new InvalidOperationException("process exited");
        yield return new OperationCanceledException("query canceled");
    }

    [TestCaseSource(nameof(ExpectedFailures))]
    public async Task ExpectedGitFault_ReturnsNullOrFalseAndLogs(Exception failure)
    {
        var logs = new ConcurrentQueue<string>();
        var query = new PlanRecoveryGitQuery(
            (_, _, _) => Task.FromException<PlanRecoveryGitQuery.CommandResult>(failure),
            log: logs.Enqueue);

        var head = await query.TryReadHeadAsync(Workspace).WaitAsync(HarnessLimit);
        var ancestor = await query.IsAncestorAsync(Workspace, OldCommit, Head).WaitAsync(HarnessLimit);

        Assert.Multiple(() =>
        {
            Assert.That(head, Is.Null);
            Assert.That(ancestor, Is.False);
            Assert.That(logs.Count, Is.GreaterThanOrEqualTo(2));
        });
    }

    [Test]
    public async Task SynchronousExpectedRunnerFault_IsCaughtOffContext()
    {
        var query = new PlanRecoveryGitQuery((_, _, _) => throw new IOException("pipe failed"));

        Assert.That(await query.TryReadHeadAsync(Workspace).WaitAsync(HarnessLimit), Is.Null);
    }

    [Test]
    public void ProgrammerFault_IsNotMisreportedAsGitFailure()
    {
        var query = new PlanRecoveryGitQuery((_, _, _) => throw new ArgumentException("runner bug"));

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await query.TryReadHeadAsync(Workspace).WaitAsync(HarnessLimit));
    }

    [Test]
    public async Task ReadSnapshotAsync_QueriesEachCommitAgainstCapturedHeadAndThenIsPure()
    {
        var calls = new ConcurrentQueue<string[]>();
        var query = new PlanRecoveryGitQuery((_, arguments, _) =>
        {
            calls.Enqueue(arguments.ToArray());
            return Task.FromResult(arguments[0] == "rev-parse"
                ? Success(Head)
                : new PlanRecoveryGitQuery.CommandResult(true, arguments[2] == OldCommit ? 0 : 1, ""));
        });

        var snapshot = await query.ReadSnapshotAsync(Workspace, [OldCommit, "unrelated"]).WaitAsync(HarnessLimit);
        var countAfterRead = calls.Count;

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Workspace, Is.EqualTo(Workspace));
            Assert.That(snapshot.Head, Is.EqualTo(Head));
            Assert.That(snapshot.Ancestors, Is.EquivalentTo(new[] { OldCommit }));
            Assert.That(snapshot.IsAncestor(OldCommit, Head), Is.True);
            Assert.That(snapshot.IsAncestor("unrelated", Head), Is.False);
            Assert.That(snapshot.IsAncestor(OldCommit, "another-head"), Is.False);
            Assert.That(snapshot.IsAncestor(Head, OldCommit), Is.False);
            Assert.That(calls.Count, Is.EqualTo(countAfterRead), "Snapshot lookup must never start Git.");
            Assert.That(calls.Where(args => args[0] == "merge-base").Select(args => args[3]),
                Is.All.EqualTo(Head));
        });
    }

    [Test]
    public async Task ReadSnapshotAsync_FailedHeadDoesNotProbeOrReusePreviousSuccessfulSnapshot()
    {
        var failHead = false;
        var probes = 0;
        var query = new PlanRecoveryGitQuery((_, args, _) =>
        {
            if (args[0] == "rev-parse")
                return Task.FromResult(failHead ? PlanRecoveryGitQuery.CommandResult.Failed : Success(Head));
            Interlocked.Increment(ref probes);
            return Task.FromResult(Success());
        });
        var previous = await query.ReadSnapshotAsync(Workspace, [OldCommit]).WaitAsync(HarnessLimit);
        failHead = true;
        var failed = await query.ReadSnapshotAsync(Workspace, [OldCommit]).WaitAsync(HarnessLimit);

        Assert.Multiple(() =>
        {
            Assert.That(previous.IsAncestor(OldCommit, Head), Is.True);
            Assert.That(failed.Head, Is.Null);
            Assert.That(failed.IsAncestor(OldCommit, Head), Is.False);
            Assert.That(probes, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ReadSnapshotAsync_AncestryTimeoutDoesNotAuthorizeUnverifiedCommit()
    {
        var pending = new TaskCompletionSource<PlanRecoveryGitQuery.CommandResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var query = new PlanRecoveryGitQuery((_, args, _) =>
            args[0] == "rev-parse" ? Task.FromResult(Success(Head)) : pending.Task,
            TimeSpan.FromMilliseconds(100));
        try
        {
            var snapshot = await query.ReadSnapshotAsync(Workspace, [OldCommit]).WaitAsync(HarnessLimit);
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.Ancestors, Is.Empty);
                Assert.That(snapshot.IsAncestor(OldCommit, Head), Is.False);
            });
        }
        finally
        {
            pending.TrySetResult(Success());
        }
    }

    [Test]
    public async Task Startup_NonPumpingContextReturnsPromptlyRunsDelegateOffContextAndCompletesBounded()
    {
        var context = new NonPumpingSynchronizationContext();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource<SynchronizationContext?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var returned = new TaskCompletionSource<Task<PlanRecoveryGitSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var query = new PlanRecoveryGitQuery(async (_, args, _) =>
        {
            entered.TrySetResult(SynchronizationContext.Current);
            // A delegate invoked on the startup context would deadlock at this await.
            await release.Task;
            return Success(args[0] == "rev-parse" ? Head : "");
        }, TimeSpan.FromSeconds(30));
        var startup = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                returned.TrySetResult(query.ReadSnapshotAsync(Workspace, [OldCommit]));
            }
            catch (Exception exception)
            {
                returned.TrySetException(exception);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
            }
        }) { IsBackground = true };

        startup.Start();
        try
        {
            var operation = await returned.Task.WaitAsync(HarnessLimit);
            var runnerContext = await entered.Task.WaitAsync(HarnessLimit);
            Assert.Multiple(() =>
            {
                Assert.That(operation.IsCompleted, Is.False, "The API must return before Git is released.");
                Assert.That(runnerContext, Is.Null, "The injected delegate must not run on the startup context.");
            });
            release.TrySetResult();
            var snapshot = await operation.WaitAsync(HarnessLimit);
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.Head, Is.EqualTo(Head));
                Assert.That(snapshot.IsAncestor(OldCommit, Head), Is.True);
                Assert.That(context.PostCount, Is.Zero, "No continuation may depend on dispatcher pumping.");
            });
        }
        finally
        {
            release.TrySetResult();
            Assert.That(startup.Join(HarnessLimit), Is.True, "Startup entry must not remain blocked.");
        }
    }

    private static PlanRecoveryGitQuery.CommandResult Success(string output = "") => new(true, 0, output);

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;
        internal int PostCount => Volatile.Read(ref _postCount);
        public override void Post(SendOrPostCallback callback, object? state) =>
            Interlocked.Increment(ref _postCount);
    }
}
