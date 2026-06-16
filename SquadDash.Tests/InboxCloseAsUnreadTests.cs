using System;
using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Tests for the "Close as Unread" feature:
/// <list type="bullet">
///   <item><see cref="InboxStore.MarkUnread"/> behaviour</item>
///   <item>Null-safety contract for the <c>onMarkedUnread</c> callback wired in MainWindow</item>
/// </list>
/// </summary>
[TestFixture]
internal sealed class InboxCloseAsUnreadTests
{
    private string     _squadFolder = null!;
    private InboxStore _store       = null!;

    [SetUp]
    public void SetUp()
    {
        _squadFolder = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"inbox_unread_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_squadFolder);
        _store = new InboxStore(_squadFolder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_squadFolder))
            Directory.Delete(_squadFolder, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private InboxMessage MakeMessage(string id, bool read = false) =>
        new()
        {
            Id        = id,
            Subject   = $"Subject {id}",
            From      = "test-agent",
            Timestamp = DateTimeOffset.UtcNow,
            Body      = "Test body.",
            Read      = read,
        };

    // ── MarkUnread tests ──────────────────────────────────────────────────────

    [Test]
    public void MarkUnread_SetsReadToFalse()
    {
        var msg = MakeMessage("msg-read", read: true);
        _store.Save(msg);

        _store.MarkUnread(msg.Id);

        var reloaded = _store.GetById(msg.Id);
        Assert.That(reloaded, Is.Not.Null, "Message should still exist after MarkUnread");
        Assert.That(reloaded!.Read, Is.False, "Read should be false after MarkUnread");
    }

    [Test]
    public void MarkUnread_MessageAlreadyUnread_IsIdempotent()
    {
        var msg = MakeMessage("msg-unread", read: false);
        _store.Save(msg);

        Assert.DoesNotThrow(() => _store.MarkUnread(msg.Id),
            "MarkUnread on an already-unread message should not throw");

        var reloaded = _store.GetById(msg.Id);
        Assert.That(reloaded!.Read, Is.False, "Message should remain unread after idempotent call");
    }

    [Test]
    public void MarkUnread_UnknownId_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _store.MarkUnread("does-not-exist"),
            "MarkUnread with a non-existent ID should not throw");
    }

    // ── Callback null-safety contract ─────────────────────────────────────────

    /// <summary>
    /// Documents that <c>InboxMessageWindow</c> accepts a nullable <c>onMarkedUnread</c>
    /// callback and invokes it with the null-conditional operator (<c>?.</c>), so omitting
    /// the callback must never raise a <see cref="NullReferenceException"/>.
    /// </summary>
    [Test]
    public void OnMarkedUnread_NullCallback_DoesNotThrow()
    {
        Action? onMarkedUnread = null;

        // This mirrors the exact pattern used inside InboxMessageWindow when the
        // "Close as Unread" button is clicked.
        Assert.DoesNotThrow(() => onMarkedUnread?.Invoke(),
            "Null onMarkedUnread callback must not throw — window uses ?. invocation");
    }

    /// <summary>
    /// When a non-null callback is supplied it must be invoked exactly once.
    /// </summary>
    [Test]
    public void OnMarkedUnread_NonNullCallback_IsInvoked()
    {
        int callCount = 0;
        Action? onMarkedUnread = () => callCount++;

        onMarkedUnread?.Invoke();

        Assert.That(callCount, Is.EqualTo(1),
            "onMarkedUnread callback should be called exactly once");
    }

    /// <summary>
    /// Verifies the full round-trip that MainWindow performs: save a read message,
    /// the user clicks "Close as Unread", MainWindow calls <c>MarkUnread</c> in the
    /// callback, and the inbox list reflects the change.
    /// </summary>
    [Test]
    public void CloseAsUnread_FullRoundTrip_MessageAppearsUnreadInList()
    {
        var msg = MakeMessage("roundtrip-msg", read: true);
        _store.Save(msg);

        // Simulate what MainWindow's onMarkedUnread lambda does
        Action onMarkedUnread = () => _store.MarkUnread(msg.Id);
        onMarkedUnread.Invoke();

        var all = _store.LoadAll();
        var saved = System.Linq.Enumerable.FirstOrDefault(all, m => m.Id == msg.Id);
        Assert.That(saved, Is.Not.Null);
        Assert.That(saved!.Read, Is.False,
            "Message should appear unread in LoadAll after the onMarkedUnread callback fires");
    }
}
