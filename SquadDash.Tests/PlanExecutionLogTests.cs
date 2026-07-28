using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanExecutionLogTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp() => _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Test]
    public void Append_CreatesFile()
    {
        var log = new PlanExecutionLog(_tempDir);
        log.Append(MakeEntry("plan_started"));
        Assert.That(File.Exists(log.LogPath), Is.True);
    }

    [Test]
    public void Append_MultipleEntries_AllPresent()
    {
        var log = new PlanExecutionLog(_tempDir);
        log.Append(MakeEntry("plan_started"));
        log.Append(MakeEntry("round_started", round: 1));
        log.Append(MakeEntry("round_completed", round: 1));

        var entries = log.Load();
        Assert.That(entries.Count, Is.EqualTo(3));
        Assert.That(entries.Select(e => e.Kind), Is.EqualTo(new[] { "plan_started", "round_started", "round_completed" }));
    }

    [Test]
    public void Load_NonExistentFile_ReturnsEmpty()
    {
        var log = new PlanExecutionLog(_tempDir);
        var entries = log.Load();
        Assert.That(entries, Is.Empty);
    }

    [Test]
    public void Load_EmptyFile_ReturnsEmpty()
    {
        var log = new PlanExecutionLog(_tempDir);
        File.WriteAllText(log.LogPath, "");
        var entries = log.Load();
        Assert.That(entries, Is.Empty);
    }

    [Test]
    public void Load_MalformedLine_SkipsIt()
    {
        var log = new PlanExecutionLog(_tempDir);
        log.Append(MakeEntry("plan_started"));
        File.AppendAllText(log.LogPath, "NOT VALID JSON" + Environment.NewLine);
        log.Append(MakeEntry("plan_stopped"));

        var entries = log.Load();
        Assert.That(entries.Count, Is.EqualTo(2));
        Assert.That(entries[0].Kind, Is.EqualTo("plan_started"));
        Assert.That(entries[1].Kind, Is.EqualTo("plan_stopped"));
    }

    [Test]
    public void Append_RoundTrip_PreservesFields()
    {
        var log = new PlanExecutionLog(_tempDir);
        var ts = DateTimeOffset.UtcNow.ToString("O");
        var entry = new PlanExecutionLogEntry(
            Kind: "round_completed",
            Timestamp: ts,
            PlanId: "plan-abc",
            Revision: "rev-1",
            Round: 3,
            TaskId: "task-x",
            TaskTitle: "Fix the bug",
            Message: null,
            Outcome: "completed");
        log.Append(entry);

        var loaded = log.Load();
        Assert.That(loaded.Count, Is.EqualTo(1));
        var e = loaded[0];
        Assert.That(e.Kind,      Is.EqualTo("round_completed"));
        Assert.That(e.Timestamp, Is.EqualTo(ts));
        Assert.That(e.PlanId,    Is.EqualTo("plan-abc"));
        Assert.That(e.Revision,  Is.EqualTo("rev-1"));
        Assert.That(e.Round,     Is.EqualTo(3));
        Assert.That(e.TaskId,    Is.EqualTo("task-x"));
        Assert.That(e.TaskTitle, Is.EqualTo("Fix the bug"));
        Assert.That(e.Outcome,   Is.EqualTo("completed"));
    }

    [Test]
    public void Load_TrimsToMaxEntries()
    {
        var log = new PlanExecutionLog(_tempDir);
        int total = PlanExecutionLog.MaxEntries + 50;
        for (int i = 0; i < total; i++)
            log.Append(MakeEntry("round_started", round: i));

        var entries = log.Load();
        Assert.That(entries.Count, Is.EqualTo(PlanExecutionLog.MaxEntries));
        // Verify oldest were trimmed — first entry should be round 50
        Assert.That(entries[0].Round, Is.EqualTo(50));
    }

    [Test]
    public void TrimFile_CalledOnLoad_WhenOverLimit()
    {
        var log = new PlanExecutionLog(_tempDir);
        int total = PlanExecutionLog.MaxEntries + 10;
        for (int i = 0; i < total; i++)
            log.Append(MakeEntry("round_started", round: i));

        // First load trims the file
        log.Load();

        // Read the raw file and count lines
        var lines = File.ReadAllLines(log.LogPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        Assert.That(lines.Length, Is.EqualTo(PlanExecutionLog.MaxEntries));
    }

    private static PlanExecutionLogEntry MakeEntry(string kind, int? round = null) =>
        new PlanExecutionLogEntry(
            Kind: kind,
            Timestamp: DateTimeOffset.UtcNow.ToString("O"),
            PlanId: "test-plan",
            Revision: null,
            Round: round,
            TaskId: null,
            TaskTitle: null,
            Message: null,
            Outcome: null);
}
