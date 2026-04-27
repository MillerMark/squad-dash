using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PromptHistoryStoreTests {
    // PromptHistoryStore writes to %LOCALAPPDATA%\SquadDash\prompt-history.json.
    // Each test uses unique sentinel prefixes to avoid cross-test contamination and
    // restores the original file content in a finally block.

    private static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SquadDash",
        "prompt-history.json");

    private IReadOnlyList<string>? _originalContent;

    [SetUp]
    public void SetUp() {
        _originalContent = File.Exists(HistoryPath)
            ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(HistoryPath)) ?? []
            : null;
    }

    [TearDown]
    public void TearDown() {
        try {
            if (_originalContent is null)
                File.Delete(HistoryPath);
            else
                new PromptHistoryStore().Save(_originalContent);
        }
        catch { /* best-effort restore */ }
    }

    [Test]
    public void SaveAndLoad_RoundTrip_ReturnsEntriesInOrder() {
        var store = new PromptHistoryStore();
        var entries = new[] { "first prompt", "second prompt", "third prompt" };

        store.Save(entries);
        var loaded = store.Load();

        Assert.That(loaded, Is.EqualTo(entries));
    }

    [Test]
    public void Save_MoreThan200Entries_KeepsOnlyLast200() {
        var store = new PromptHistoryStore();
        var entries = Enumerable.Range(1, 201).Select(i => $"prompt-{i:D3}").ToArray();

        store.Save(entries);
        var loaded = store.Load();

        Assert.Multiple(() => {
            Assert.That(loaded, Has.Count.EqualTo(200));
            Assert.That(loaded[0], Is.EqualTo("prompt-002")); // first was dropped
            Assert.That(loaded[^1], Is.EqualTo("prompt-201"));
        });
    }

    [Test]
    public void Save_Exactly200Entries_KeepsAll() {
        var store = new PromptHistoryStore();
        var entries = Enumerable.Range(1, 200).Select(i => $"prompt-{i:D3}").ToArray();

        store.Save(entries);
        var loaded = store.Load();

        Assert.That(loaded, Has.Count.EqualTo(200));
    }

    [Test]
    public void Load_WhenFileDoesNotExist_ReturnsEmptyList() {
        if (File.Exists(HistoryPath))
            File.Delete(HistoryPath);

        var store = new PromptHistoryStore();
        var loaded = store.Load();

        Assert.That(loaded, Is.Empty);
    }

    [Test]
    public void Load_WhenFileContainsMalformedJson_ReturnsEmptyList() {
        Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
        File.WriteAllText(HistoryPath, "this is not valid json {{ ]]]");

        var store = new PromptHistoryStore();
        var loaded = store.Load();

        Assert.That(loaded, Is.Empty);
    }

    [Test]
    public void Save_CalledTwice_SecondCallOverwritesFirst() {
        var store = new PromptHistoryStore();

        store.Save(["first-save-entry"]);
        store.Save(["second-save-entry"]);

        var loaded = store.Load();
        Assert.Multiple(() => {
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0], Is.EqualTo("second-save-entry"));
        });
    }

    [Test]
    public void Save_EmptyList_LoadReturnsEmpty() {
        var store = new PromptHistoryStore();
        store.Save(["seed-entry"]);
        store.Save([]);

        var loaded = store.Load();

        Assert.That(loaded, Is.Empty);
    }
}
