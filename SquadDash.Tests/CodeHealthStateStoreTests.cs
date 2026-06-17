using System;
using System.IO;
using System.Text.Json;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Behavioral specs for <see cref="CodeHealthStateStore"/>.
/// Tests will compile once Arjun Sen's Phase 1 implementation lands.
/// </summary>
[TestFixture]
internal sealed class CodeHealthStateStoreTests {

    private TestWorkspace _workspace = null!;
    private CodeHealthStateStore _store = null!;

    [SetUp]
    public void SetUp() {
        _workspace = new TestWorkspace();
        _store = CreateStore();
    }

    private CodeHealthStateStore CreateStore(ITimeProvider? clock = null,
        Func<string, string, Task<int>>? commitCounter = null) =>
        new CodeHealthStateStore(_workspace.RootPath, clock, commitCounter);

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    // ── IsEligible — unknown task ─────────────────────────────────────────────

    [Test]
    public void IsEligible_UnknownTask_ReturnsTrue() {
        // A task that has never run should always be eligible.
        var eligible = _store.IsEligibleAsync("never-run-task", "daily", commitSha: "abc123").GetAwaiter().GetResult();
        Assert.That(eligible, Is.True, "A task with no prior run record must be eligible");
    }

    // ── IsEligible — frequency: daily ─────────────────────────────────────────

    [Test]
    public void IsEligible_DailyFrequency_NotEligibleAfterRunToday() {
        _store.RecordRun("daily-task", commitSha: "sha1");
        var eligible = _store.IsEligibleAsync("daily-task", "daily", commitSha: "sha2").GetAwaiter().GetResult();
        Assert.That(eligible, Is.False, "daily task must not be eligible again on the same calendar day");
    }

    [Test]
    public void IsEligible_DailyFrequency_EligibleAfterRunYesterday() {
        // Seed the state file with a last-run timestamp from yesterday.
        WriteStateWithLastRunAt("daily-task", lastRunAt: DateTime.UtcNow.AddDays(-1), lastSha: "sha1");
        _store.Reload();

        var eligible = _store.IsEligibleAsync("daily-task", "daily", commitSha: "sha2").GetAwaiter().GetResult();
        Assert.That(eligible, Is.True, "daily task must be eligible the day after it last ran");
    }

    // ── IsEligible — frequency: per-commit ────────────────────────────────────

    [Test]
    public void IsEligible_PerCommitFrequency_NotEligibleForSameCommitSha() {
        const string sha = "deadbeef";
        _store.RecordRun("per-commit-task", commitSha: sha);
        var eligible = _store.IsEligibleAsync("per-commit-task", "per-commit", commitSha: sha).GetAwaiter().GetResult();
        Assert.That(eligible, Is.False, "per-commit task must not re-run for the same commit SHA");
    }

    [Test]
    public void IsEligible_PerCommitFrequency_EligibleForNewCommitSha() {
        _store.RecordRun("per-commit-task", commitSha: "old-sha");
        var eligible = _store.IsEligibleAsync("per-commit-task", "per-commit", commitSha: "new-sha").GetAwaiter().GetResult();
        Assert.That(eligible, Is.True, "per-commit task must be eligible when the commit SHA changes");
    }

    // ── IsEligible — per-commit git fallback (null SHA) ──────────────────────

    [Test]
    public void IsEligible_PerCommitFrequency_NullCommitSha_FallsBackToDaily_NotEligibleAfterRunToday() {
        // git unavailable → commitSha is null → must behave like daily (same day = not eligible).
        _store.RecordRun("per-commit-task", commitSha: null);
        var eligible = _store.IsEligibleAsync("per-commit-task", "per-commit", commitSha: null).GetAwaiter().GetResult();
        Assert.That(eligible, Is.False,
            "per-commit task with null SHA must fall back to daily — not eligible again on the same day");
    }

    [Test]
    public void IsEligible_PerCommitFrequency_NullCommitSha_FallsBackToDaily_EligibleAfterRunYesterday() {
        // git unavailable → commitSha is null → daily fallback means eligible after a day gap.
        WriteStateWithLastRunAt("per-commit-task", lastRunAt: DateTime.UtcNow.AddDays(-1), lastSha: "sha1");
        _store.Reload();

        var eligible = _store.IsEligibleAsync("per-commit-task", "per-commit", commitSha: null).GetAwaiter().GetResult();
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
            _store.IsEligibleAsync("trace-task", "per-commit", commitSha: null).GetAwaiter().GetResult();
        }
        finally {
            SquadDashTrace.TraceTarget = null;
        }

        Assert.That(captured, Has.Count.GreaterThan(0),
            "A trace entry must be written when per-commit falls back due to null commit SHA");
        Assert.That(captured[0].Category, Is.EqualTo(TraceCategory.General));
        Assert.That(captured[0].Message, Does.Contain("after-commits git fallback"),
            "Trace message must identify this as an after-commits git fallback");
    }

    // ── IsEligible — frequency: after-commits ────────────────────────────────

    [Test]
    public void IsEligible_AfterCommitsFrequency_NotEligibleForSameCommitSha() {
        const string sha = "deadbeef";
        _store.RecordRun("after-commits-task", commitSha: sha);
        var eligible = _store.IsEligibleAsync("after-commits-task", "after-commits", commitSha: sha).GetAwaiter().GetResult();
        Assert.That(eligible, Is.False, "after-commits task must not re-run for the same commit SHA");
    }

    [Test]
    public void IsEligible_AfterCommitsFrequency_EligibleForNewCommitSha() {
        _store.RecordRun("after-commits-task", commitSha: "old-sha");
        var eligible = _store.IsEligibleAsync("after-commits-task", "after-commits", commitSha: "new-sha").GetAwaiter().GetResult();
        Assert.That(eligible, Is.True, "after-commits task must be eligible when the commit SHA changes");
    }

    [Test]
    public void IsEligible_AfterCommitsFrequency_NullCommitSha_FallsBackToDaily_NotEligibleAfterRunToday() {
        _store.RecordRun("after-commits-task", commitSha: null);
        var eligible = _store.IsEligibleAsync("after-commits-task", "after-commits", commitSha: null).GetAwaiter().GetResult();
        Assert.That(eligible, Is.False,
            "after-commits task with null SHA must fall back to daily — not eligible again on the same day");
    }

    [Test]
    public void IsEligible_AfterCommitsFrequency_NullCommitSha_FallsBackToDaily_EligibleAfterRunYesterday() {
        WriteStateWithLastRunAt("after-commits-task", lastRunAt: DateTime.UtcNow.AddDays(-1), lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligibleAsync("after-commits-task", "after-commits", commitSha: null).GetAwaiter().GetResult();
        Assert.That(eligible, Is.True,
            "after-commits task with null SHA must fall back to daily — eligible after a full-day gap");
    }

    [Test]
    public void IsEligible_AfterCommitsFrequency_NullCommitSha_WritesTraceEntry() {
        var captured = new System.Collections.Generic.List<(TraceCategory Category, string Message)>();
        var target = new CapturingTraceTarget(captured);
        SquadDashTrace.TraceTarget = target;

        try {
            _store.IsEligibleAsync("trace-task", "after-commits", commitSha: null).GetAwaiter().GetResult();
        }
        finally {
            SquadDashTrace.TraceTarget = null;
        }

        Assert.That(captured, Has.Count.GreaterThan(0),
            "A trace entry must be written when after-commits falls back due to null commit SHA");
        Assert.That(captured[0].Category, Is.EqualTo(TraceCategory.General));
        Assert.That(captured[0].Message, Does.Contain("after-commits git fallback"),
            "Trace message must identify this as an after-commits git fallback");
    }

    // ── IsEligible — frequency: always ────────────────────────────────────────

    [Test]
    public void IsEligible_AlwaysFrequency_EligibleEvenAfterRecentRun() {
        _store.RecordRun("always-task", commitSha: "sha1");
        var eligible = _store.IsEligibleAsync("always-task", "always", commitSha: "sha1").GetAwaiter().GetResult();
        Assert.That(eligible, Is.True, "always task must be eligible regardless of prior run history");
    }

    // ── IsEligible — frequency: weekly ───────────────────────────────────────

    [Test]
    public void IsEligible_Weekly_NeverRun_ReturnsTrue() {
        var eligible = _store.IsEligibleAsync("weekly-never-run", "weekly", commitSha: "sha1").GetAwaiter().GetResult();
        Assert.That(eligible, Is.True, "A weekly task with no prior run record must be eligible");
    }

    [Test]
    public void IsEligible_Weekly_LastRunAtNull_ReturnsTrue() {
        WriteStateWithNullLastRunAt("weekly-task", lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligibleAsync("weekly-task", "weekly", commitSha: "sha2").GetAwaiter().GetResult();
        Assert.That(eligible, Is.True, "A weekly task with null LastRunAt must be eligible");
    }

    [Test]
    public void IsEligible_Weekly_RunInPriorCalendarWeek_ReturnsTrue() {
        var today = DateTime.UtcNow.Date;
        var thisMonday = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
        // Last Sunday is definitively in the prior calendar week.
        WriteStateWithLastRunAt("weekly-task", lastRunAt: thisMonday.AddDays(-1), lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligibleAsync("weekly-task", "weekly", commitSha: "sha2").GetAwaiter().GetResult();
        Assert.That(eligible, Is.True, "A weekly task run in the prior calendar week (last Sunday) must be eligible");
    }

    [Test]
    public void IsEligible_Weekly_RunThisMonday_ReturnsFalse() {
        var today = DateTime.UtcNow.Date;
        var thisMonday = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
        WriteStateWithLastRunAt("weekly-task", lastRunAt: thisMonday, lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligibleAsync("weekly-task", "weekly", commitSha: "sha2").GetAwaiter().GetResult();
        Assert.That(eligible, Is.False, "A weekly task run this Monday must not be eligible again this week");
    }

    [Test]
    public void IsEligible_Weekly_RunMidweekThisWeek_ReturnsFalse() {
        // Wednesday 2025-01-08 is used as "today"; last run is also Wednesday of the same week.
        var wednesday = new DateTime(2025, 1, 8, 0, 0, 0, DateTimeKind.Utc);
        _store = CreateStore(new FakeTimeProvider(wednesday));
        WriteStateWithLastRunAt("weekly-task", lastRunAt: wednesday, lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligibleAsync("weekly-task", "weekly", commitSha: "sha2").GetAwaiter().GetResult();
        Assert.That(eligible, Is.False, "A weekly task run this Wednesday must not be eligible again this week");
    }

    [Test]
    public void IsEligible_Weekly_RunTodayAtStartOfWeek_ReturnsFalse() {
        // Monday 2025-01-06 is used as "today".
        var monday = new DateTime(2025, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        _store = CreateStore(new FakeTimeProvider(monday));
        WriteStateWithLastRunAt("weekly-task", lastRunAt: monday, lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligibleAsync("weekly-task", "weekly", commitSha: "sha2").GetAwaiter().GetResult();
        Assert.That(eligible, Is.False, "A weekly task run today when today is Monday must not be eligible");
    }

    [Test]
    public void IsEligible_Weekly_RunLastWeekFriday_ReturnsTrue() {
        var today = DateTime.UtcNow.Date;
        var thisMonday = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
        // thisMonday - 3 days = last week's Friday.
        WriteStateWithLastRunAt("weekly-task", lastRunAt: thisMonday.AddDays(-3), lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligibleAsync("weekly-task", "weekly", commitSha: "sha2").GetAwaiter().GetResult();
        Assert.That(eligible, Is.True, "A weekly task run last week's Friday must be eligible (prior calendar week)");
    }

    // ── IsEligible — frequency: monthly ──────────────────────────────────────

    [Test]
    public void IsEligible_Monthly_NeverRun_ReturnsTrue() {
        var eligible = _store.IsEligibleAsync("monthly-never-run", "monthly", commitSha: "sha1").GetAwaiter().GetResult();
        Assert.That(eligible, Is.True, "A monthly task with no prior run record must be eligible");
    }

    [Test]
    public void IsEligible_Monthly_LastRunAtNull_ReturnsTrue() {
        WriteStateWithNullLastRunAt("monthly-task", lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligibleAsync("monthly-task", "monthly", commitSha: "sha2").GetAwaiter().GetResult();
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
        var eligible = _store.IsEligibleAsync("monthly-task", "monthly", commitSha: "sha2").GetAwaiter().GetResult();
        Assert.That(eligible, Is.True, "A monthly task run in a prior calendar month must be eligible");
    }

    [Test]
    public void IsEligible_Monthly_RunPriorYear_ReturnsTrue() {
        WriteStateWithLastRunAt("monthly-task", lastRunAt: DateTime.UtcNow.AddYears(-1), lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligibleAsync("monthly-task", "monthly", commitSha: "sha2").GetAwaiter().GetResult();
        Assert.That(eligible, Is.True, "A monthly task run in a prior year must be eligible");
    }

    [Test]
    public void IsEligible_Monthly_RunThisCalendarMonth_ReturnsFalse() {
        // Use a fixed "today" mid-month so AddDays(-3) stays in the same calendar month.
        var today = new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        _store = CreateStore(new FakeTimeProvider(today));
        WriteStateWithLastRunAt("monthly-task", lastRunAt: today.AddDays(-3), lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligibleAsync("monthly-task", "monthly", commitSha: "sha2").GetAwaiter().GetResult();
        Assert.That(eligible, Is.False, "A monthly task run 3 days ago (same month) must not be eligible");
    }

    [Test]
    public void IsEligible_Monthly_RunFirstOfThisMonth_ReturnsFalse() {
        var now = DateTime.UtcNow;
        var firstOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        WriteStateWithLastRunAt("monthly-task", lastRunAt: firstOfMonth, lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligibleAsync("monthly-task", "monthly", commitSha: "sha2").GetAwaiter().GetResult();
        Assert.That(eligible, Is.False, "A monthly task run on the first of this month must not be eligible");
    }

    // ── IsEligible — unknown/default frequency ───────────────────────────────

    [Test]
    public void IsEligible_UnknownFrequency_FallsBackToDaily_NotEligibleAfterRunToday() {
        _store.RecordRun("unk-task", commitSha: "sha1");
        var eligible = _store.IsEligibleAsync("unk-task", "unknown-frequency-xyz", commitSha: "sha1").GetAwaiter().GetResult();
        Assert.That(eligible, Is.False,
            "An unrecognized frequency must fall back to daily — task must not be eligible on the same day");
    }

    [Test]
    public void IsEligible_UnknownFrequency_FallsBackToDaily_EligibleAfterRunYesterday() {
        WriteStateWithLastRunAt("unk-task", lastRunAt: DateTime.UtcNow.AddDays(-1), lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligibleAsync("unk-task", "unknown-frequency-xyz", commitSha: "sha1").GetAwaiter().GetResult();
        Assert.That(eligible, Is.True,
            "An unrecognized frequency must fall back to daily — task must be eligible after a full-day gap");
    }

    // ── IsEligible — monthly year-boundary ───────────────────────────────────

    [Test]
    public void IsEligible_Monthly_YearBoundary_RanDecemberNowNewYear_ReturnsTrue() {
        var now = DateTime.UtcNow;
        // Explicitly anchor to December of the prior year regardless of current month.
        var lastDecember = new DateTime(now.Year - 1, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        WriteStateWithLastRunAt("monthly-task", lastRunAt: lastDecember, lastSha: "sha1");
        _store.Reload();
        var eligible = _store.IsEligibleAsync("monthly-task", "monthly", commitSha: "sha2").GetAwaiter().GetResult();
        Assert.That(eligible, Is.True,
            "A monthly task run in December of last year must be eligible this year (year-boundary check)");
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
        var freshStore = new CodeHealthStateStore(_workspace.RootPath);
        freshStore.Reload();

        var eligible = freshStore.IsEligibleAsync("reload-task", "daily", commitSha: "sha-other").GetAwaiter().GetResult();
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
        var stateFile = Path.Combine(_workspace.RootPath, "code-health-state.json");
        File.WriteAllText(stateFile, "{ this is not valid json !!!");

        var store = new CodeHealthStateStore(_workspace.RootPath);
        store.Reload();

        // All tasks should appear as never-run (no crash, empty state).
        var eligible = store.IsEligibleAsync("any-task", "daily", commitSha: "sha").GetAwaiter().GetResult();
        Assert.That(eligible, Is.True,
            "A corrupt state file must not crash the store; all tasks should be eligible (empty state)");
    }

    // ── Missing state file ────────────────────────────────────────────────────

    [Test]
    public void Load_MissingStateFile_GracefulDegradation_ReturnsEmptyState() {
        // Point to a directory that contains no state file at all.
        var emptyDir = Path.Combine(_workspace.RootPath, "empty");
        Directory.CreateDirectory(emptyDir);

        var store = new CodeHealthStateStore(emptyDir);
        store.Reload();

        var eligible = store.IsEligibleAsync("any-task", "per-commit", commitSha: "sha").GetAwaiter().GetResult();
        Assert.That(eligible, Is.True,
            "A missing state file must not crash the store; all tasks should be eligible");
    }

    // ── IsEligible — frequency: every-N-commits ──────────────────────────────

    [Test]
    public void IsEligible_EveryNCommits_NeverRun_ReturnsTrue() {
        // A task that has never run has no baseline — always eligible.
        var store = CreateStore(commitCounter: (_, _) => Task.FromResult(3));
        var eligible = store.IsEligibleAsync("every-n-task", "every-5-commits",
            commitSha: "new-sha", workspacePath: _workspace.RootPath).GetAwaiter().GetResult();
        Assert.That(eligible, Is.True, "A task with no prior run must be eligible regardless of commit count");
    }

    [Test]
    public void IsEligible_EveryNCommits_NoBaselineSha_ReturnsTrue() {
        // Task exists in state but LastCommitSha is null → no baseline → eligible.
        WriteStateWithNullLastRunAt("every-n-task", lastSha: "");
        var store = CreateStore(commitCounter: (_, _) => Task.FromResult(0));
        store.Reload();
        var eligible = store.IsEligibleAsync("every-n-task", "every-5-commits",
            commitSha: "new-sha", workspacePath: _workspace.RootPath).GetAwaiter().GetResult();
        Assert.That(eligible, Is.True, "A task with no recorded baseline SHA must be eligible");
    }

    [Test]
    public void IsEligible_EveryNCommits_BelowThreshold_ReturnsFalse() {
        // 4 new commits, threshold is 5 → not eligible.
        WriteStateWithLastRunAt("every-n-task", lastRunAt: DateTime.UtcNow.AddDays(-1), lastSha: "base-sha");
        var store = CreateStore(commitCounter: (_, _) => Task.FromResult(4));
        store.Reload();
        var eligible = store.IsEligibleAsync("every-n-task", "every-5-commits",
            commitSha: "new-sha", workspacePath: _workspace.RootPath).GetAwaiter().GetResult();
        Assert.That(eligible, Is.False, "4 new commits against a threshold of 5 must not be eligible");
    }

    [Test]
    public void IsEligible_EveryNCommits_AtThreshold_ReturnsTrue() {
        // Exactly 5 new commits, threshold is 5 → eligible.
        WriteStateWithLastRunAt("every-n-task", lastRunAt: DateTime.UtcNow.AddDays(-1), lastSha: "base-sha");
        var store = CreateStore(commitCounter: (_, _) => Task.FromResult(5));
        store.Reload();
        var eligible = store.IsEligibleAsync("every-n-task", "every-5-commits",
            commitSha: "new-sha", workspacePath: _workspace.RootPath).GetAwaiter().GetResult();
        Assert.That(eligible, Is.True, "Exactly 5 new commits against a threshold of 5 must be eligible");
    }

    [Test]
    public void IsEligible_EveryNCommits_AboveThreshold_ReturnsTrue() {
        // 9 new commits, threshold is 5 → eligible.
        WriteStateWithLastRunAt("every-n-task", lastRunAt: DateTime.UtcNow.AddDays(-1), lastSha: "base-sha");
        var store = CreateStore(commitCounter: (_, _) => Task.FromResult(9));
        store.Reload();
        var eligible = store.IsEligibleAsync("every-n-task", "every-5-commits",
            commitSha: "new-sha", workspacePath: _workspace.RootPath).GetAwaiter().GetResult();
        Assert.That(eligible, Is.True, "9 new commits against a threshold of 5 must be eligible");
    }

    [Test]
    public void IsEligible_EveryNCommits_SameHeadSha_ReturnsFalse() {
        // HEAD hasn't moved since the last run — not eligible regardless of threshold.
        const string sha = "same-sha";
        WriteStateWithLastRunAt("every-n-task", lastRunAt: DateTime.UtcNow.AddDays(-1), lastSha: sha);
        var store = CreateStore(commitCounter: (_, _) => Task.FromResult(99));
        store.Reload();
        var eligible = store.IsEligibleAsync("every-n-task", "every-5-commits",
            commitSha: sha, workspacePath: _workspace.RootPath).GetAwaiter().GetResult();
        Assert.That(eligible, Is.False, "If HEAD SHA matches the last-run SHA, task must not be eligible");
    }

    [Test]
    public void IsEligible_EveryNCommits_NullWorkspacePath_FallsBackToDaily_NotEligibleAfterRunToday() {
        // workspacePath is null → cannot count commits → fall back to daily.
        _store.RecordRun("every-n-task", commitSha: "base-sha");
        var eligible = _store.IsEligibleAsync("every-n-task", "every-5-commits",
            commitSha: "new-sha", workspacePath: null).GetAwaiter().GetResult();
        Assert.That(eligible, Is.False,
            "Null workspacePath must fall back to daily — not eligible again on the same day");
    }

    [Test]
    public void IsEligible_EveryNCommits_NullCommitSha_FallsBackToDaily_NotEligibleAfterRunToday() {
        // commitSha is null (git unavailable) → fall back to daily.
        _store.RecordRun("every-n-task", commitSha: "base-sha");
        var eligible = _store.IsEligibleAsync("every-n-task", "every-5-commits",
            commitSha: null, workspacePath: _workspace.RootPath).GetAwaiter().GetResult();
        Assert.That(eligible, Is.False,
            "Null commitSha must fall back to daily — not eligible again on the same day");
    }

    // ── GetSafetyOverride — empty string treated as null ─────────────────────

    [Test]
    public void GetSafetyOverride_EmptyStringInJson_ReturnsNull() {
        // Regression: persisted safetyOverride: "" must be treated as "no override"
        // so it doesn't shadow the task's declared safety via null-coalescing.
        var stateFile = Path.Combine(_workspace.RootPath, "code-health-state.json");
        File.WriteAllText(stateFile, """
            {
              "tasks": {
                "commit-review": {
                  "lastRunAt": "2026-06-17T18:00:00Z",
                  "lastCommitSha": "abc123",
                  "safetyOverride": ""
                }
              }
            }
            """);
        var store = new CodeHealthStateStore(_workspace.RootPath);
        store.Reload();

        var result = store.GetSafetyOverride("commit-review");
        Assert.That(result, Is.Null,
            "An empty safetyOverride string must be treated as no override (return null), " +
            "otherwise it defeats the task's declared safety level via null-coalescing.");
    }

    [Test]
    public void GetSafetyOverride_ActualOverride_ReturnsValue() {
        var store = new CodeHealthStateStore(_workspace.RootPath);
        store.SetSafetyOverride("some-task", "report-only");

        var result = store.GetSafetyOverride("some-task");
        Assert.That(result, Is.EqualTo("report-only"),
            "A non-empty safetyOverride must be returned as-is.");
    }

    [Test]
    public void GetSafetyOverride_NullOverride_ReturnsNull() {
        var store = new CodeHealthStateStore(_workspace.RootPath);
        store.SetSafetyOverride("some-task", null);

        var result = store.GetSafetyOverride("some-task");
        Assert.That(result, Is.Null,
            "A null safetyOverride must be returned as null.");
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
        var stateFile = Path.Combine(_workspace.RootPath, "code-health-state.json");
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
        var stateFile = Path.Combine(_workspace.RootPath, "code-health-state.json");
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

