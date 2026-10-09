using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class MarkdownDetailStateStoreTests {
    private TestWorkspace _workspace = null!;
    private MarkdownDetailStateStore _store = null!;
    private ILiveTraceTarget? _previousTraceTarget;
    private readonly ConcurrentQueue<string> _trace = new();

    [SetUp]
    public void SetUp() {
        _workspace = new TestWorkspace();
        _store = new MarkdownDetailStateStore(_workspace.GetPath("preferences", "markdown-detail-state.json"));
        _trace.Clear();
        _previousTraceTarget = SquadDashTrace.TraceTarget;
        SquadDashTrace.TraceTarget = new CapturingTraceTarget(_trace);
    }

    [TearDown]
    public void TearDown() {
        SquadDashTrace.TraceTarget = _previousTraceTarget;
        _workspace.Dispose();
    }

    private string Key(string taskId = "task-1", string label = "Why", string body = "Body") =>
        MarkdownDetailIdentity.ForTaskDescription(_workspace.RootPath, "plan-1", taskId).GetSectionKey(label, body);

    [Test]
    public void ProductionConstructor_UsesSeparateApplicationGlobalFile() {
        Assert.That(new MarkdownDetailStateStore().FilePath,
            Is.EqualTo(Path.Combine(SquadDashPaths.AppData, "markdown-detail-state.json")));
    }

    [Test]
    public void FreshStore_DefaultsCollapsedWithoutCreatingStorageOrReportingFailure() {
        Assert.Multiple(() => {
            Assert.That(_store.Load(Key()), Is.EqualTo(new MarkdownDetailStateReadResult(false)));
            Assert.That(_store.Remove(Key()).Succeeded, Is.True);
            Assert.That(Directory.Exists(Path.GetDirectoryName(_store.FilePath)), Is.False);
            Assert.That(_trace, Is.Empty);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void BothStates_RoundTripThroughFreshStoreInstances(bool expanded) {
        var key = Key();
        Assert.That(_store.Save(key, expanded).Succeeded, Is.True);
        var fresh = new MarkdownDetailStateStore(_store.FilePath);
        Assert.That(fresh.Load(key), Is.EqualTo(new MarkdownDetailStateReadResult(expanded)));
        using var document = JsonDocument.Parse(File.ReadAllText(_store.FilePath));
        Assert.That(document.RootElement.GetProperty("Entries").GetProperty(key).GetBoolean(), Is.EqualTo(expanded));
    }

    [Test]
    public void TaskDescription_RestoresAcrossViewIndependentRebuildsThenPersistsCollapsed() {
        var visualizerKey = MarkdownDetailIdentity.ForTaskDescription(_workspace.RootPath, "plan-1", "task-1")
            .GetSectionKey(" Why ", "a\r\nb");
        Assert.That(_store.Save(visualizerKey, true).Succeeded, Is.True);
        var inspectorKey = MarkdownDetailIdentity.ForTaskDescription(_workspace.RootPath.ToUpperInvariant() + "\\",
            "plan-1", "task-1").GetSectionKey("Why", "a\nb");
        var recreated = new MarkdownDetailStateStore(_store.FilePath);
        Assert.Multiple(() => {
            Assert.That(inspectorKey, Is.EqualTo(visualizerKey));
            Assert.That(recreated.Load(inspectorKey), Is.EqualTo(new MarkdownDetailStateReadResult(true)));
            Assert.That(recreated.Load(Key("task-2", "Why", "a\nb")), Is.EqualTo(new MarkdownDetailStateReadResult(false)));
        });
        Assert.That(recreated.Save(inspectorKey, false).Succeeded, Is.True);
        Assert.That(new MarkdownDetailStateStore(_store.FilePath).Load(visualizerKey),
            Is.EqualTo(new MarkdownDetailStateReadResult(false)));
    }

    [Test]
    public void StaleInitializationAndReads_NeverOverwriteSavedStateOrUnrelatedEntries() {
        var stale = new MarkdownDetailStateStore(_store.FilePath);
        Assert.That(stale.Load(Key()).IsExpanded, Is.False);
        Assert.That(_store.Save(Key(), true).Succeeded, Is.True);
        var savedBytes = File.ReadAllBytes(_store.FilePath);
        Assert.Multiple(() => {
            Assert.That(stale.Load(Key()).IsExpanded, Is.True);
            Assert.That(stale.Load(Key("unseen")).IsExpanded, Is.False);
            Assert.That(File.ReadAllBytes(_store.FilePath), Is.EqualTo(savedBytes));
        });
        Assert.That(stale.Save(Key("other"), false).Succeeded, Is.True);
        Assert.That(new MarkdownDetailStateStore(_store.FilePath).Load(Key()).IsExpanded, Is.True);
    }

    [Test]
    public void EditingSection_ResolvesCollapsedWithoutDeletingPreviousPreference() {
        Assert.That(_store.Save(Key(), true).Succeeded, Is.True);
        Assert.Multiple(() => {
            Assert.That(_store.Load(Key(label: "Edited")).IsExpanded, Is.False);
            Assert.That(_store.Load(Key(body: "Edited")).IsExpanded, Is.False);
            Assert.That(_store.Load(Key()).IsExpanded, Is.True);
        });
    }

    [Test]
    public void DuplicateSections_HaveIndependentPersistedStates() {
        var identity = MarkdownDetailIdentity.ForTaskDescription(_workspace.RootPath, "plan", "task");
        var sections = new[] { new MarkdownDetailSection("A", "Body"), new MarkdownDetailSection("A", "Body") };
        var keys = identity.GetSectionKeys(sections);
        Assert.That(_store.Save(keys[0], true).Succeeded, Is.True);
        Assert.That(_store.Save(keys[1], false).Succeeded, Is.True);
        var rebuilt = identity.GetSectionKeys([new("Unrelated", "Text"), .. sections]);
        var fresh = new MarkdownDetailStateStore(_store.FilePath);
        Assert.Multiple(() => {
            Assert.That(fresh.Load(rebuilt[1]).IsExpanded, Is.True);
            Assert.That(fresh.Load(rebuilt[2]).IsExpanded, Is.False);
        });
    }

    [Test]
    public void ConcurrentInstances_MergeEveryEntryAndBothStateTransitions() {
        var writes = new ConcurrentQueue<MarkdownDetailStateWriteResult>();
        Parallel.For(0, 24, index => {
            var store = new MarkdownDetailStateStore(_store.FilePath);
            var key = Key($"task-{index}");
            writes.Enqueue(store.Save(key, true));
            writes.Enqueue(store.Save(key, index % 2 == 0));
        });
        Assert.That(writes.All(result => result.Succeeded), Is.True);
        var fresh = new MarkdownDetailStateStore(_store.FilePath);
        for (var index = 0; index < 24; index++)
            Assert.That(fresh.Load(Key($"task-{index}")),
                Is.EqualTo(new MarkdownDetailStateReadResult(index % 2 == 0)));
    }

    [Test]
    public void FileContainsOnlySchemaHashedKeysAndBooleans_NotMarkdownOrEntityData() {
        Assert.That(_store.Save(Key("private-task", "Sensitive label", "Secret Markdown body"), true).Succeeded, Is.True);
        var json = File.ReadAllText(_store.FilePath);
        using var document = JsonDocument.Parse(json);
        Assert.Multiple(() => {
            Assert.That(document.RootElement.EnumerateObject().Select(p => p.Name),
                Is.EquivalentTo(new[] { "SchemaVersion", "Entries" }));
            Assert.That(document.RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(json, Does.Not.Contain("private-task").And.Not.Contain("Sensitive label")
                .And.Not.Contain("Secret Markdown body").And.Not.Contain(_workspace.RootPath));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(_store.FilePath)!, "*.tmp"), Is.Empty);
        });
    }

    [Test]
    public void Cleanup_RemovesOnlyExplicitKeyAndPreservesRealPreferences() {
        Assert.That(_store.Save(Key("fixture"), true).Succeeded, Is.True);
        Assert.That(_store.Save(Key("real"), true).Succeeded, Is.True);
        Assert.That(_store.Remove(Key("fixture")).Succeeded, Is.True);
        var fresh = new MarkdownDetailStateStore(_store.FilePath);
        Assert.Multiple(() => {
            Assert.That(fresh.Load(Key("fixture")).IsExpanded, Is.False);
            Assert.That(fresh.Load(Key("real")).IsExpanded, Is.True);
        });
        using var document = JsonDocument.Parse(File.ReadAllText(_store.FilePath));
        Assert.That(document.RootElement.GetProperty("Entries").EnumerateObject().Select(p => p.Name),
            Is.EqualTo(new[] { Key("real") }));
    }

    [TestCase("{ broken")]
    [TestCase("null")]
    [TestCase("{}")]
    [TestCase("{\"SchemaVersion\":2,\"Entries\":{}}")]
    [TestCase("{\"SchemaVersion\":1,\"Entries\":null}")]
    [TestCase("{\"SchemaVersion\":1,\"SchemaVersion\":1,\"Entries\":{}}")]
    [TestCase("{\"SchemaVersion\":1,\"Entries\":{},\"FutureData\":42}")]
    public void CorruptOrUnsupportedDocument_IsReportedWithoutDestructiveRecovery(string json) {
        Directory.CreateDirectory(Path.GetDirectoryName(_store.FilePath)!);
        File.WriteAllText(_store.FilePath, json);
        AssertFailedReadAndMutationsPreserveBytes();
    }

    [TestCase("invalid-key")]
    [TestCase("non-boolean")]
    [TestCase("duplicate-key")]
    public void InvalidEntry_DoesNotDiscardRecoverableEntries(string scenario) {
        var key = Key();
        var invalidEntry = scenario switch {
            "invalid-key" => "\"Markdown is not a key\":true",
            "non-boolean" => $"\"{Key("other")}\":\"true\"",
            _ => $"\"{key}\":false"
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_store.FilePath)!);
        File.WriteAllText(_store.FilePath, "{\"SchemaVersion\":1,\"Entries\":{\"" + key + "\":true," + invalidEntry + "}}");
        AssertFailedReadAndMutationsPreserveBytes();
    }

    [TestCase("save")]
    [TestCase("remove")]
    public void FailedReplace_ReportsFailureAndPreservesPreviousFile(string operation) {
        Assert.That(_store.Save(Key(), true).Succeeded, Is.True);
        var before = File.ReadAllBytes(_store.FilePath);
        MarkdownDetailStateWriteResult result;
        using (var held = new FileStream(_store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            result = operation == "save" ? _store.Save(Key(), false) : _store.Remove(Key());
        Assert.Multiple(() => {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Is.InstanceOf<IOException>());
            Assert.That(File.ReadAllBytes(_store.FilePath), Is.EqualTo(before));
            Assert.That(_store.Load(Key()).IsExpanded, Is.True);
            Assert.That(_trace.Any(line => line.Contains("failed")), Is.True);
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(_store.FilePath)!, "*.tmp"), Is.Empty);
        });
        Assert.That(_store.Save(Key(), false).Succeeded, Is.True);
        Assert.That(new MarkdownDetailStateStore(_store.FilePath).Load(Key()).IsExpanded, Is.False);
    }

    [Test]
    public void UnreadableStorage_ReturnsReportedCollapsedFallbackAndDoesNotReset() {
        Assert.That(_store.Save(Key(), true).Succeeded, Is.True);
        var before = File.ReadAllBytes(_store.FilePath);
        using (var held = new FileStream(_store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None)) {
            Assert.Multiple(() => {
                Assert.That(_store.Load(Key()).Succeeded, Is.False);
                Assert.That(_store.Load(Key()).IsExpanded, Is.False);
                Assert.That(_store.Save(Key(), false).Succeeded, Is.False);
                Assert.That(_store.Remove(Key()).Succeeded, Is.False);
            });
        }
        Assert.Multiple(() => {
            Assert.That(File.ReadAllBytes(_store.FilePath), Is.EqualTo(before));
            Assert.That(_trace.Count, Is.EqualTo(4));
            Assert.That(new MarkdownDetailStateStore(_store.FilePath).Load(Key()).IsExpanded, Is.True);
        });
    }

    [Test]
    public void FailedFirstWrite_IsReportedWithoutCreatingSuccessState() {
        Directory.CreateDirectory(_store.FilePath);
        var result = _store.Save(Key(), true);
        Assert.Multiple(() => {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Is.Not.Null);
            Assert.That(_store.Load(Key()).Succeeded, Is.False);
            Assert.That(_store.Load(Key()).IsExpanded, Is.False);
            Assert.That(Directory.Exists(_store.FilePath), Is.True);
            Assert.That(_trace.Any(line => line.Contains("Save failed")), Is.True);
        });
    }

    [Test]
    public async Task NamedMutex_AnotherProcessBlocksMutationAndTimeoutDoesNotEraseState() {
        Assert.That(_store.Save(Key(), true).Succeeded, Is.True);
        var before = File.ReadAllBytes(_store.FilePath);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_store.FilePath.ToUpperInvariant())));
        var mutexName = $@"Local\SquadDash.MarkdownDetailState.{hash}";
        var start = new ProcessStartInfo {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"$m = [Threading.Mutex]::new($false, '{mutexName}'); " +
            "$null = $m.WaitOne(); try { [Console]::WriteLine('ready'); $null = [Console]::ReadLine() } " +
            "finally { $m.ReleaseMutex(); $m.Dispose() }");
        using var child = Process.Start(start)!;
        try {
            Assert.That(await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)), Is.EqualTo("ready"));
            var read = _store.Load(Key());
            var write = _store.Save(Key(), false);
            Assert.Multiple(() => {
                Assert.That(read.Succeeded, Is.False);
                Assert.That(read.IsExpanded, Is.False);
                Assert.That(read.Error, Is.TypeOf<TimeoutException>());
                Assert.That(write.Succeeded, Is.False);
                Assert.That(write.Error, Is.TypeOf<TimeoutException>());
                Assert.That(File.ReadAllBytes(_store.FilePath), Is.EqualTo(before));
                Assert.That(_trace.Count, Is.EqualTo(2));
            });
        }
        finally {
            if (!child.HasExited) {
                await child.StandardInput.WriteLineAsync("release");
                if (!child.WaitForExit(5000))
                    child.Kill();
            }
        }
        Assert.That(new MarkdownDetailStateStore(_store.FilePath).Load(Key()).IsExpanded, Is.True);
    }

    [Test]
    public void InvalidKeys_CannotPersistBodiesOrPerformBroadCleanup() {
        Assert.Multiple(() => {
            Assert.That(() => _store.Save("body text", true), Throws.ArgumentException);
            Assert.That(() => _store.Load(""), Throws.ArgumentException);
            Assert.That(() => _store.Remove("md-detail:v1:*"), Throws.ArgumentException);
            Assert.That(File.Exists(_store.FilePath), Is.False);
        });
    }

    private void AssertFailedReadAndMutationsPreserveBytes() {
        var before = File.ReadAllBytes(_store.FilePath);
        var loaded = _store.Load(Key());
        var saved = _store.Save(Key(), true);
        var removed = _store.Remove(Key());
        Assert.Multiple(() => {
            Assert.That(loaded.Succeeded, Is.False);
            Assert.That(loaded.IsExpanded, Is.False);
            Assert.That(loaded.Error, Is.Not.Null);
            Assert.That(saved.Succeeded, Is.False);
            Assert.That(removed.Succeeded, Is.False);
            Assert.That(File.ReadAllBytes(_store.FilePath), Is.EqualTo(before));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(_store.FilePath)!, "*.tmp"), Is.Empty);
            Assert.That(_trace.Count, Is.EqualTo(3));
        });
    }

    private sealed class CapturingTraceTarget(ConcurrentQueue<string> messages) : ILiveTraceTarget {
        public void AddEntry(TraceCategory category, string detail) => messages.Enqueue(detail);
    }
}
