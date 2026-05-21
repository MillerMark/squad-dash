using System;
using System.IO;
using System.Text.Json;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Behavioral specs for <see cref="MaintenanceStateStore"/>.
/// Tests will compile once Arjun Sen's Phase 1 implementation lands.
/// </summary>
[TestFixture]
internal sealed class MaintenanceStateStoreTests {

    private TestWorkspace _workspace = null!;
    private MaintenanceStateStore _store = null!;

    [SetUp]
    public void SetUp() {
        _workspace = new TestWorkspace();
        _store = new MaintenanceStateStore(_workspace.RootPath);
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    // ── IsEligible — unknown task ─────────────────────────────────────────────

    [Test]
    public void IsEligible_UnknownTask_ReturnsTrue() {
        // A task that has never run should always be eligible.
        var eligible = _store.IsEligible("never-run-task", "daily", commitSha: "abc123");
        Assert.That(eligible, Is.True, "A task with no prior run record must be eligible");
    }

    // ── IsEligible — frequency: daily ─────────────────────────────────────────

    [Test]
    public void IsEligible_DailyFrequency_NotEligibleAfterRunToday() {
        _store.RecordRun("daily-task", commitSha: "sha1");
        var eligible = _store.IsEligible("daily-task", "daily", commitSha: "sha2");
        Assert.That(eligible, Is.False, "daily task must not be eligible again on the same calendar day");
    }

    [Test]
    public void IsEligible_DailyFrequency_EligibleAfterRunYesterday() {
        // Seed the state file with a last-run timestamp from yesterday.
        WriteStateWithLastRunAt("daily-task", lastRunAt: DateTime.UtcNow.AddDays(-1), lastSha: "sha1");
        _store.Reload();

        var eligible = _store.IsEligible("daily-task", "daily", commitSha: "sha2");
        Assert.That(eligible, Is.True, "daily task must be eligible the day after it last ran");
    }

    // ── IsEligible — frequency: per-commit ────────────────────────────────────

    [Test]
    public void IsEligible_PerCommitFrequency_NotEligibleForSameCommitSha() {
        const string sha = "deadbeef";
        _store.RecordRun("per-commit-task", commitSha: sha);
        var eligible = _store.IsEligible("per-commit-task", "per-commit", commitSha: sha);
        Assert.That(eligible, Is.False, "per-commit task must not re-run for the same commit SHA");
    }

    [Test]
    public void IsEligible_PerCommitFrequency_EligibleForNewCommitSha() {
        _store.RecordRun("per-commit-task", commitSha: "old-sha");
        var eligible = _store.IsEligible("per-commit-task", "per-commit", commitSha: "new-sha");
        Assert.That(eligible, Is.True, "per-commit task must be eligible when the commit SHA changes");
    }

    // ── IsEligible — per-commit git fallback (null SHA) ──────────────────────

    [Test]
    public void IsEligible_PerCommitFrequency_NullCommitSha_FallsBackToDaily_NotEligibleAfterRunToday() {
        // git unavailable → commitSha is null → must behave like daily (same day = not eligible).
        _store.RecordRun("per-commit-task", commitSha: null);
        var eligible = _store.IsEligible("per-commit-task", "per-commit", commitSha: null);
        Assert.That(eligible, Is.False,
            "per-commit task with null SHA must fall back to daily — not eligible again on the same day");
    }

    [Test]
    public void IsEligible_PerCommitFrequency_NullCommitSha_FallsBackToDaily_EligibleAfterRunYesterday() {
        // git unavailable → commitSha is null → daily fallback means eligible after a day gap.
        WriteStateWithLastRunAt("per-commit-task", lastRunAt: DateTime.UtcNow.AddDays(-1), lastSha: "sha1");
        _store.Reload();

        var eligible = _store.IsEligible("per-commit-task", "per-commit", commitSha: null);
        Assert.That(eligible, Is.True,
            "per-commit task with null SHA must fall back to daily — eligible after a full-day gap");
    }

    [Test]
    public void IsEligible_PerCommitFrequency_NullCommitSha_WritesTraceEntry() {
        // Capture live trace entries so we can assert the fallback message was emitted.
        var captured = new System.Collections.Generic.List<(TraceCategory Category, string Message)>();
        var target = new CapturingTraceTarget(captured);
        SquadDashTrace.TraceTarget = target;

        try {
            _store.IsEligible("trace-task", "per-commit", commitSha: null);
        }
        finally {
            SquadDashTrace.TraceTarget = null;
        }

        Assert.That(captured, Has.Count.GreaterThan(0),
            "A trace entry must be written when per-commit falls back due to null commit SHA");
        Assert.That(captured[0].Category, Is.EqualTo(TraceCategory.General));
        Assert.That(captured[0].Message, Does.Contain("per-commit git fallback"),
            "Trace message must identify this as a per-commit git fallback");
    }

    // ── IsEligible — frequency: always ────────────────────────────────────────

    [Test]
    public void IsEligible_AlwaysFrequency_EligibleEvenAfterRecentRun() {
        _store.RecordRun("always-task", commitSha: "sha1");
        var eligible = _store.IsEligible("always-task", "always", commitSha: "sha1");
        Assert.That(eligible, Is.True, "always task must be eligible regardless of prior run history");
    }

    // ── IsEligible — frequency: weekly ───────────────────────────────────────

    [Test]
    public void IsEligible_Weekly_NeverRun_ReturnsTrue() {
        var eligible = _store.IsEligible("weekly-never-run", "weekly", commitSha: "sha1");
        Assert.That(eligible, Is.True, "A weekly task with no prior run record must be eligible");
    }

    [Test]
    public void IsEligible_Weekly_LastRunAtNull_ReturnsTrue() {
        WriteStateWithNullLastRunAt("weekly-task", lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligible("weekly-task", "weekly", commitSha: "sha2");
        Assert.That(eligible, Is.True, "A weekly task with null LastRunAt must be eligible");
    }

    [Test]
    public void IsEligible_Weekly_Run8DaysAgo_ReturnsTrue() {
        WriteStateWithLastRunAt("weekly-task", lastRunAt: DateTime.UtcNow.AddDays(-8), lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligible("weekly-task", "weekly", commitSha: "sha2");
        Assert.That(eligible, Is.True, "A weekly task run 8 days ago must be eligible");
    }

    [Test]
    public void IsEligible_Weekly_RunJustOver7DaysAgo_ReturnsTrue() {
        WriteStateWithLastRunAt("weekly-task", lastRunAt: DateTime.UtcNow.AddDays(-7).AddSeconds(-1), lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligible("weekly-task", "weekly", commitSha: "sha2");
        Assert.That(eligible, Is.True, "A weekly task run just over 7 days ago must be eligible (strictly less than now minus 7 days)");
    }

    [Test]
    public void IsEligible_Weekly_Run6DaysAgo_ReturnsFalse() {
        WriteStateWithLastRunAt("weekly-task", lastRunAt: DateTime.UtcNow.AddDays(-6), lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligible("weekly-task", "weekly", commitSha: "sha2");
        Assert.That(eligible, Is.False, "A weekly task run only 6 days ago must not be eligible");
    }

    [Test]
    public void IsEligible_Weekly_RunToday_ReturnsFalse() {
        WriteStateWithLastRunAt("weekly-task", lastRunAt: DateTime.UtcNow, lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligible("weekly-task", "weekly", commitSha: "sha2");
        Assert.That(eligible, Is.False, "A weekly task run today must not be eligible");
    }

    // ── IsEligible — frequency: monthly ──────────────────────────────────────

    [Test]
    public void IsEligible_Monthly_NeverRun_ReturnsTrue() {
        var eligible = _store.IsEligible("monthly-never-run", "monthly", commitSha: "sha1");
        Assert.That(eligible, Is.True, "A monthly task with no prior run record must be eligible");
    }

    [Test]
    public void IsEligible_Monthly_LastRunAtNull_ReturnsTrue() {
        WriteStateWithNullLastRunAt("monthly-task", lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligible("monthly-task", "monthly", commitSha: "sha2");
        Assert.That(eligible, Is.True, "A monthly task with null LastRunAt must be eligible");
    }

    [Test]
    public void IsEligible_Monthly_RunPriorCalendarMonth_ReturnsTrue() {
        var now = DateTime.UtcNow;
        var priorMonth = now.Month == 1
            ? new DateTime(now.Year - 1, 12, 1, 0, 0, 0, DateTimeKind.Utc)
            : new DateTime(now.Year, now.Month - 1, 1, 0, 0, 0, DateTimeKind.Utc);
        WriteStateWithLastRunAt("monthly-task", lastRunAt: priorMonth, lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligible("monthly-task", "monthly", commitSha: "sha2");
        Assert.That(eligible, Is.True, "A monthly task run in a prior calendar month must be eligible");
    }

    [Test]
    public void IsEligible_Monthly_RunPriorYear_ReturnsTrue() {
        WriteStateWithLastRunAt("monthly-task", lastRunAt: DateTime.UtcNow.AddYears(-1), lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligible("monthly-task", "monthly", commitSha: "sha2");
        Assert.That(eligible, Is.True, "A monthly task run in a prior year must be eligible");
    }

    [Test]
    public void IsEligible_Monthly_RunThisCalendarMonth_ReturnsFalse() {
        WriteStateWithLastRunAt("monthly-task", lastRunAt: DateTime.UtcNow.AddDays(-3), lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligible("monthly-task", "monthly", commitSha: "sha2");
        Assert.That(eligible, Is.False, "A monthly task run 3 days ago (same month) must not be eligible");
    }

    [Test]
    public void IsEligible_Monthly_RunFirstOfThisMonth_ReturnsFalse() {
        var now = DateTime.UtcNow;
        var firstOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        WriteStateWithLastRunAt("monthly-task", lastRunAt: firstOfMonth, lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligible("monthly-task", "monthly", commitSha: "sha2");
        Assert.That(eligible, Is.False, "A monthly task run on the first of this month must not be eligible");
    }

    // ── RecordRun persists to disk ────────────────────────────────────────────

    [Test]
    public void RecordRun_PersistsStateToDisk() {
        _store.RecordRun("persist-task", commitSha: "sha-persist");

        // Verify a state file was written somewhere inside _workspace.RootPath.
        var files = Directory.GetFiles(_workspace.RootPath, "*", SearchOption.AllDirectories);
        Assert.That(files, Is.Not.Empty, "RecordRun must write at least one file to the state directory");
    }

    // ── State survives reload ─────────────────────────────────────────────────

    [Test]
    public void RecordRun_StateSurvivesReload() {
        _store.RecordRun("reload-task", commitSha: "sha-reload");

        // Create a fresh store over the same directory and reload.
        var freshStore = new MaintenanceStateStore(_workspace.RootPath);
        freshStore.Reload();

        var eligible = freshStore.IsEligible("reload-task", "daily", commitSha: "sha-other");
        Assert.That(eligible, Is.False,
            "After reload, a task run today must still be ineligible (state must survive across instances)");
    }

    // ── Atomic write ─────────────────────────────────────────────────────────

    [Test]
    public void RecordRun_ProducesCompleteStateFile() {
        _store.RecordRun("atomic-task", commitSha: "sha-atomic");

        // Ensure the final state file is valid JSON (proves the write was completed atomically
        // and did not leave a half-written or zero-byte file).
        var files = Directory.GetFiles(_workspace.RootPath, "*.json", SearchOption.AllDirectories);
        Assert.That(files, Is.Not.Empty, "A .json state file must exist after RecordRun");

        foreach (var file in files) {
            var text = File.ReadAllText(file);
            Assert.DoesNotThrow(
                () => JsonDocument.Parse(text),
                $"State file '{Path.GetFileName(file)}' must be valid JSON (no partial writes)");
        }
    }

    // ── Corrupt state file ────────────────────────────────────────────────────

    [Test]
    public void Load_CorruptStateFile_GracefulDegradation_ReturnsEmptyState() {
        // Write garbage directly to the expected state file.
        var stateFile = Path.Combine(_workspace.RootPath, "maintenance-state.json");
        File.WriteAllText(stateFile, "{ this is not valid json !!!");

        var store = new MaintenanceStateStore(_workspace.RootPath);
        store.Reload();

        // All tasks should appear as never-run (no crash, empty state).
        var eligible = store.IsEligible("any-task", "daily", commitSha: "sha");
        Assert.That(eligible, Is.True,
            "A corrupt state file must not crash the store; all tasks should be eligible (empty state)");
    }

    // ── Missing state file ────────────────────────────────────────────────────

    [Test]
    public void Load_MissingStateFile_GracefulDegradation_ReturnsEmptyState() {
        // Point to a directory that contains no state file at all.
        var emptyDir = Path.Combine(_workspace.RootPath, "empty");
        Directory.CreateDirectory(emptyDir);

        var store = new MaintenanceStateStore(emptyDir);
        store.Reload();

        var eligible = store.IsEligible("any-task", "per-commit", commitSha: "sha");
        Assert.That(eligible, Is.True,
            "A missing state file must not crash the store; all tasks should be eligible");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class CapturingTraceTarget(
        System.Collections.Generic.List<(TraceCategory Category, string Message)> sink)
        : ILiveTraceTarget {
        public void AddEntry(TraceCategory category, string detail) =>
            sink.Add((category, detail));
    }

    /// <summary>
    /// Writes a state JSON file where the task exists but <c>lastRunAt</c> is empty (parses as null).
    /// </summary>
    private void WriteStateWithNullLastRunAt(string taskId, string lastSha) {
        var stateFile = Path.Combine(_workspace.RootPath, "maintenance-state.json");
        var json = $$"""
            {
              "tasks": {
                "{{taskId}}": {
                  "lastRunAt": "",
                  "lastCommitSha": "{{lastSha}}"
                }
              }
            }
            """;
        File.WriteAllText(stateFile, json);
    }

    /// <summary>
    /// Writes a minimal state JSON file into <see cref="_workspace.RootPath"/> so tests can
    /// exercise date-dependent eligibility without waiting a real day.
    /// </summary>
    private void WriteStateWithLastRunAt(string taskId, DateTime lastRunAt, string lastSha) {
        var stateFile = Path.Combine(_workspace.RootPath, "maintenance-state.json");
        var json = $$"""
            {
              "tasks": {
                "{{taskId}}": {
                  "lastRunAt": "{{lastRunAt:O}}",
                  "lastCommitSha": "{{lastSha}}"
                }
              }
            }
            """;
        File.WriteAllText(stateFile, json);
    }
}
