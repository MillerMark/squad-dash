using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Tests for <see cref="CodeHealthStubRecord"/> JSON serialisation/deserialisation
/// and for <see cref="CodeHealthStubStateStore"/> load/save round-trips.
/// </summary>
[TestFixture]
internal sealed class CodeHealthStubTests {

    private string _stateDir = null!;

    [SetUp]
    public void SetUp() {
        _stateDir = Path.Combine(TestContext.CurrentContext.WorkDirectory,
            $"stub_state_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_stateDir);
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(_stateDir))
            Directory.Delete(_stateDir, recursive: true);
    }

    // ── StubRecord serialisation ──────────────────────────────────────────────

    [Test]
    public void StubRecord_RoundTripsAllFields() {
        var original = new CodeHealthStubRecord {
            TaskTitle       = "Run Linter",
            ThreadId        = "abc-123",
            AnchorIndex     = 7,
            StartedAt       = new DateTimeOffset(2026, 5, 24, 8, 16, 25, TimeSpan.Zero),
            DurationSeconds = 320.5,
        };

        var json       = JsonSerializer.Serialize(original);
        var roundTrip  = JsonSerializer.Deserialize<CodeHealthStubRecord>(json);

        Assert.That(roundTrip, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(roundTrip!.TaskTitle,       Is.EqualTo("Run Linter"));
            Assert.That(roundTrip.ThreadId,         Is.EqualTo("abc-123"));
            Assert.That(roundTrip.AnchorIndex,      Is.EqualTo(7));
            Assert.That(roundTrip.StartedAt,        Is.EqualTo(original.StartedAt));
            Assert.That(roundTrip.DurationSeconds,  Is.EqualTo(320.5));
        });
    }

    [Test]
    public void StubRecord_NullThreadId_RoundTrips() {
        var original = new CodeHealthStubRecord {
            TaskTitle       = "Scan Deps",
            ThreadId        = null,
            AnchorIndex     = 0,
            StartedAt       = DateTimeOffset.UtcNow,
            DurationSeconds = 60.0,
        };

        var json      = JsonSerializer.Serialize(original);
        var roundTrip = JsonSerializer.Deserialize<CodeHealthStubRecord>(json);

        Assert.That(roundTrip, Is.Not.Null);
        Assert.That(roundTrip!.ThreadId, Is.Null,
            "Null ThreadId must survive the JSON round-trip");
    }

    [Test]
    public void StubRecordList_RoundTrips() {
        var records = new List<CodeHealthStubRecord> {
            new() { TaskTitle = "Task A", AnchorIndex = 1, StartedAt = DateTimeOffset.UtcNow, DurationSeconds = 10 },
            new() { TaskTitle = "Task B", AnchorIndex = 2, StartedAt = DateTimeOffset.UtcNow, DurationSeconds = 20 },
        };

        var json      = JsonSerializer.Serialize(records);
        var roundTrip = JsonSerializer.Deserialize<List<CodeHealthStubRecord>>(json);

        Assert.That(roundTrip, Has.Count.EqualTo(2));
        Assert.That(roundTrip![0].TaskTitle, Is.EqualTo("Task A"));
        Assert.That(roundTrip![1].TaskTitle, Is.EqualTo("Task B"));
    }

    [Test]
    public void StubRecord_JsonPropertyNames_AreCorrect() {
        var record = new CodeHealthStubRecord {
            TaskTitle       = "T",
            ThreadId        = "x",
            AnchorIndex     = 3,
            StartedAt       = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            DurationSeconds = 1.5,
        };

        var json = JsonSerializer.Serialize(record);

        Assert.Multiple(() => {
            Assert.That(json, Does.Contain("\"taskTitle\""),       "taskTitle property name");
            Assert.That(json, Does.Contain("\"threadId\""),        "threadId property name");
            Assert.That(json, Does.Contain("\"anchorIndex\""),     "anchorIndex property name");
            Assert.That(json, Does.Contain("\"startedAt\""),       "startedAt property name");
            Assert.That(json, Does.Contain("\"durationSeconds\""), "durationSeconds property name");
        });
    }

    // ── CodeHealthStubStateStore ─────────────────────────────────────────────

    [Test]
    public void StubStateStore_Load_WhenFileAbsent_LeavesPropertyNull() {
        var store = new CodeHealthStubStateStore(_stateDir);
        store.Load();
        Assert.That(store.LastRenderedSidecarPath, Is.Null,
            "Should leave LastRenderedSidecarPath null when state file does not exist");
    }

    [Test]
    public void StubStateStore_SaveAndLoad_RoundTrips() {
        var store = new CodeHealthStubStateStore(_stateDir);
        store.LastRenderedSidecarPath = @"C:\some\path\20260524-081625.json";
        store.Save();

        var store2 = new CodeHealthStubStateStore(_stateDir);
        store2.Load();

        Assert.That(store2.LastRenderedSidecarPath,
            Is.EqualTo(@"C:\some\path\20260524-081625.json"),
            "LastRenderedSidecarPath must survive a save/load round-trip");
    }

    [Test]
    public void StubStateStore_Save_NullPath_RoundTrips() {
        var store = new CodeHealthStubStateStore(_stateDir);
        store.LastRenderedSidecarPath = null;
        store.Save();

        var store2 = new CodeHealthStubStateStore(_stateDir);
        store2.Load();

        Assert.That(store2.LastRenderedSidecarPath, Is.Null,
            "Null LastRenderedSidecarPath must survive a save/load round-trip");
    }

    [Test]
    public void StubStateStore_Save_IsIdempotent() {
        var store = new CodeHealthStubStateStore(_stateDir);
        store.LastRenderedSidecarPath = "foo.json";
        store.Save();
        store.Save(); // second save must not throw

        var store2 = new CodeHealthStubStateStore(_stateDir);
        store2.Load();
        Assert.That(store2.LastRenderedSidecarPath, Is.EqualTo("foo.json"));
    }
}

