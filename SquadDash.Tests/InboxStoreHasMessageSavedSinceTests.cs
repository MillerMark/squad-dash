using System;
using System.IO;
using System.Threading;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Tests for <see cref="InboxStore.HasMessageSavedSince"/>.
/// </summary>
[TestFixture]
internal sealed class InboxStoreHasMessageSavedSinceTests {

    private string   _squadFolder = null!;
    private InboxStore _store     = null!;

    [SetUp]
    public void SetUp() {
        _squadFolder = Path.Combine(TestContext.CurrentContext.WorkDirectory,
            $"inbox_since_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_squadFolder);
        _store = new InboxStore(_squadFolder);
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(_squadFolder))
            Directory.Delete(_squadFolder, recursive: true);
    }

    private InboxMessage MakeMessage(string id) =>
        new() {
            Id        = id,
            Subject   = $"Subject {id}",
            From      = "test",
            Timestamp = DateTimeOffset.Now,
            Body      = "body",
        };

    [Test]
    public void ReturnsFalse_WhenInboxFolderDoesNotExist() {
        var since  = DateTimeOffset.UtcNow.AddHours(-1);
        var result = _store.HasMessageSavedSince(since);
        Assert.That(result, Is.False,
            "Should return false when no inbox folder exists yet");
    }

    [Test]
    public void ReturnsFalse_WhenInboxIsEmpty() {
        _store.Save(MakeMessage("a"));
        _store.Delete("a");

        var result = _store.HasMessageSavedSince(DateTimeOffset.UtcNow.AddHours(-1));
        Assert.That(result, Is.False,
            "Should return false when inbox is empty after deletion");
    }

    [Test]
    public void ReturnsFalse_WhenAllMessagesAreOlderThanSince() {
        var before = DateTimeOffset.UtcNow.AddSeconds(-2);
        _store.Save(MakeMessage("old"));
        // 'since' is set to right now, after the message was written
        Thread.Sleep(10);
        var since = DateTimeOffset.UtcNow;

        var result = _store.HasMessageSavedSince(since);
        Assert.That(result, Is.False,
            "Should return false when the only message pre-dates 'since'");
    }

    [Test]
    public void ReturnsTrue_WhenAMessageWasSavedAfterSince() {
        var since = DateTimeOffset.UtcNow.AddSeconds(-2);
        _store.Save(MakeMessage("new"));

        var result = _store.HasMessageSavedSince(since);
        Assert.That(result, Is.True,
            "Should return true when a message was saved after 'since'");
    }

    [Test]
    public void ReturnsTrue_EvenWhenSomeMessagesAreOlderAndOneIsNewer() {
        // Write an old message, then advance time reference, then write a new one
        _store.Save(MakeMessage("first"));
        Thread.Sleep(20);
        var since = DateTimeOffset.UtcNow;
        Thread.Sleep(20);
        _store.Save(MakeMessage("second"));

        var result = _store.HasMessageSavedSince(since);
        Assert.That(result, Is.True,
            "Should return true when at least one message post-dates 'since'");
    }
}
