using System;
using System.IO;
using System.Collections.Generic;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class NotesStoreTests {
    private TestWorkspace _workspace = null!;
    private NotesStore _store = null!;

    [SetUp]
    public void SetUp() {
        _workspace = new TestWorkspace();
        _store = new NotesStore(_workspace.RootPath);
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    [Test]
    public void LoadAll_WhenFileNotPresent_ReturnsEmptyList() {
        var result = _store.LoadAll();
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void LoadAll_CorruptJson_ReturnsEmptyList() {
        File.WriteAllText(Path.Combine(_workspace.RootPath, "notes.json"), "{ not valid json [[[");
        var result = _store.LoadAll();
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void LoadAll_ValidJson_ReturnsDeserializedItems() {
        var id = Guid.NewGuid();
        var item = new NoteItem(id, "My Note", 1234567890L);
        _store.SaveAll([item]);

        var result = _store.LoadAll();

        Assert.Multiple(() => {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Id, Is.EqualTo(id));
            Assert.That(result[0].Title, Is.EqualTo("My Note"));
            Assert.That(result[0].CreatedAt, Is.EqualTo(1234567890L));
        });
    }

    [Test]
    public void SaveAll_ThenLoadAll_RoundTripsItems() {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var items = new List<NoteItem> {
            new(id1, "First Note", 1000L),
            new(id2, "Second Note", 2000L),
        };

        _store.SaveAll(items);
        var result = _store.LoadAll();

        Assert.Multiple(() => {
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].Id, Is.EqualTo(id1));
            Assert.That(result[1].Id, Is.EqualTo(id2));
        });
    }

    [Test]
    public void LoadContent_WhenFileNotPresent_ReturnsEmptyString() {
        var id = Guid.NewGuid();
        var result = _store.LoadContent(id);
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void LoadContent_WhenFileExists_ReturnsContent() {
        var id = Guid.NewGuid();
        _store.WriteContent(id, "# My Note\n\nContent here.");

        var result = _store.LoadContent(id);

        Assert.That(result, Is.EqualTo("# My Note\n\nContent here."));
    }

    [Test]
    public void WriteContent_CreatesFile() {
        var id = Guid.NewGuid();
        _store.WriteContent(id, "content");

        Assert.That(File.Exists(_store.GetNotePath(id)), Is.True);
    }

    [Test]
    public void DeleteContent_RemovesFile() {
        var id = Guid.NewGuid();
        _store.WriteContent(id, "content");
        _store.DeleteContent(id);

        Assert.That(File.Exists(_store.GetNotePath(id)), Is.False);
    }

    [Test]
    public void DeleteContent_WhenFileNotPresent_DoesNotThrow() {
        var id = Guid.NewGuid();
        Assert.DoesNotThrow(() => _store.DeleteContent(id));
    }

    [Test]
    public void DeriveTitle_PlainText_ReturnsTextIfUnder40Chars() {
        Assert.That(NotesStore.DeriveTitle("Short note title"), Is.EqualTo("Short note title"));
    }

    [Test]
    public void DeriveTitle_WithMarkdownHeading_StripsHashPrefix() {
        Assert.That(NotesStore.DeriveTitle("# My Heading"), Is.EqualTo("My Heading"));
    }

    [Test]
    public void DeriveTitle_LongFirstLine_TruncatesAtWordBoundary() {
        var text = "This is a very long first line that exceeds forty characters limit";
        var result = NotesStore.DeriveTitle(text);
        Assert.That(result.Length, Is.LessThanOrEqualTo(41)); // 40 + ellipsis char
        Assert.That(result, Does.EndWith("…"));
    }

    [Test]
    public void DeriveTitle_EmptyText_ReturnsNote() {
        Assert.That(NotesStore.DeriveTitle(""), Is.EqualTo("Note"));
        Assert.That(NotesStore.DeriveTitle("   "), Is.EqualTo("Note"));
    }

    [Test]
    public void DeriveTitle_MultilineText_UsesFirstLineOnly() {
        var result = NotesStore.DeriveTitle("First line\nSecond line");
        Assert.That(result, Is.EqualTo("First line"));
    }
}
