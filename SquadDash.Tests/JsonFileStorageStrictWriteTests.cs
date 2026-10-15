using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class JsonFileStorageStrictWriteTests {
    [Test]
    public void AtomicWrite_CreatesThenReplacesCompleteDocument() {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("document.json");
        JsonFileStorage.AtomicWritePreservingPrevious(path, new { Value = "first" });
        JsonFileStorage.AtomicWritePreservingPrevious(path, new { Value = "second\nline" });
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.That(document.RootElement.GetProperty("Value").GetString(), Is.EqualTo("second\nline"));
        Assert.That(Directory.GetFiles(workspace.RootPath, "*.tmp"), Is.Empty);
    }

    [Test]
    public void SerializationFailure_PreservesPreviousBytesAndRemovesPartialTemporaryFile() {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("document.json");
        JsonFileStorage.AtomicWritePreservingPrevious(path, new { Value = "keep me" });
        var before = File.ReadAllBytes(path);
        var cycle = new List<object>();
        cycle.Add(cycle);
        Assert.That(() => JsonFileStorage.AtomicWritePreservingPrevious(path, cycle), Throws.TypeOf<JsonException>());
        Assert.Multiple(() => {
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(Directory.GetFiles(workspace.RootPath, "*.tmp"), Is.Empty);
        });
    }

    // Regression: a transiently-locked destination (antivirus / Search indexer / Explorer / a
    // lingering read handle) makes the commit's File.Replace throw IOException ("Unable to remove
    // the file to be replaced"). The bounded retry-with-backoff must absorb that lock and still
    // commit the new document once the handle releases, with no partial state or leftover temp file.
    [Test]
    public void Commit_RetriesPastTransientlyLockedDestination_ThenSucceeds() {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("document.json");
        JsonFileStorage.AtomicWritePreservingPrevious(path, new { Value = "first" });

        // A read handle opened without FileShare.Delete blocks ReplaceFile from removing the
        // destination, reproducing the transient Windows sharing lock the retry loop defends against.
        var lockHandle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var writeReachedCommit = new ManualResetEventSlim();
        var write = Task.Run(() => {
            writeReachedCommit.Set();
            JsonFileStorage.AtomicWritePreservingPrevious(path, new { Value = "second" });
        });

        Assert.That(writeReachedCommit.Wait(TimeSpan.FromSeconds(5)), Is.True);
        // Hold the lock long enough to force several transient-lock retries, then release it so a
        // subsequent retry (not the first attempt) is the one that succeeds.
        Thread.Sleep(250);
        lockHandle.Dispose();

        Assert.That(write.Wait(TimeSpan.FromSeconds(10)), Is.True,
            "commit never completed after the transient lock released");
        Assert.DoesNotThrow(() => write.GetAwaiter().GetResult());

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Multiple(() => {
            Assert.That(document.RootElement.GetProperty("Value").GetString(), Is.EqualTo("second"));
            Assert.That(Directory.GetFiles(workspace.RootPath, "*.tmp"), Is.Empty);
        });
    }

    // Regression: a genuine FileNotFoundException (the source/temp file is really gone) is NOT a
    // transient sharing lock, so the commit must rethrow it immediately instead of burning the
    // retry budget and masking a real failure behind seconds of backoff.
    [Test]
    public void Commit_GenuineFileNotFound_RethrowsImmediatelyWithoutRetry() {
        using var workspace = new TestWorkspace();
        var missingTemporaryPath = workspace.GetPath("absent.tmp");
        var destinationPath = workspace.GetPath("document.json");

        var commit = typeof(JsonFileStorage).GetMethod(
            "CommitReplacingPrevious", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(commit, Is.Not.Null, "CommitReplacingPrevious should exist for retry-behavior coverage");

        var stopwatch = Stopwatch.StartNew();
        var error = Assert.Throws<TargetInvocationException>(
            () => commit!.Invoke(null, new object[] { missingTemporaryPath, destinationPath }));
        stopwatch.Stop();

        Assert.Multiple(() => {
            Assert.That(error!.InnerException, Is.TypeOf<FileNotFoundException>());
            // Twenty backed-off attempts would take multiple seconds; a sub-second failure proves
            // the genuine error short-circuited the retry loop on the first attempt.
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)),
                "a genuine missing-file error must not be retried");
        });
    }
}
