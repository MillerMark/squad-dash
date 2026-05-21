using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Integration tests for <see cref="MaintenanceReportWriter"/>.
/// </summary>
[TestFixture]
internal sealed class MaintenanceReportWriterTests {

    private TestWorkspace _workspace = null!;
    private MaintenanceReportWriter _writer = null!;

    [SetUp]
    public void SetUp() {
        _workspace = new TestWorkspace();
        _writer = new MaintenanceReportWriter(_workspace.RootPath);
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MaintenanceReport MakeReport(
        DateTimeOffset? startedAt = null,
        IReadOnlyList<MaintenanceTaskResult>? taskResults = null,
        string? summary = null) {
        var start = startedAt ?? DateTimeOffset.UtcNow;
        var results = taskResults ?? [];
        return new MaintenanceReport {
            RanTaskIds     = results.Select(t => t.Id).ToList(),
            SkippedTaskIds = [],
            TaskResults    = results,
            StartedAt      = start,
            CompletedAt    = start.AddMinutes(5),
            Summary        = summary,
        };
    }

    // ── File naming ───────────────────────────────────────────────────────────

    [Test]
    public void WriteReport_CreatesCorrectlyNamedFile() {
        var startedAt = new DateTimeOffset(2024, 3, 15, 10, 30, 45, TimeSpan.Zero);
        var report    = MakeReport(startedAt: startedAt);

        var filePath = _writer.WriteReport(report);
        var fileName = Path.GetFileName(filePath);

        Assert.That(File.Exists(filePath), Is.True,
            "WriteReport must create the report file on disk");
        Assert.That(Regex.IsMatch(fileName, @"^\d{8}-\d{6}\.md$"), Is.True,
            $"Filename must match yyyyMMdd-HHmmss.md pattern; got: {fileName}");

        var reportsDir = Path.Combine(_workspace.RootPath, ".squad", "maintenance-reports");
        Assert.That(filePath, Does.StartWith(reportsDir),
            "Report must be written inside .squad/maintenance-reports/");
    }

    // ── Content sections ──────────────────────────────────────────────────────

    [Test]
    public void WriteReport_ContentContainsExpectedSections() {
        var taskResults = new List<MaintenanceTaskResult> {
            new("lint-task", "Run Linter", MaintenanceTaskOutcome.Completed,
                TimeSpan.FromSeconds(12),
                FilesChanged: ["src/foo.cs", "src/bar.cs"]),
            new("fmt-task",  "Format Code", MaintenanceTaskOutcome.Skipped,
                TimeSpan.Zero),
        };
        var report = MakeReport(taskResults: taskResults, summary: "All clean.");

        _writer.WriteReport(report);

        var paths = _writer.GetReportPaths();
        Assert.That(paths, Has.Count.EqualTo(1));

        var content = File.ReadAllText(paths[0]);
        Assert.Multiple(() => {
            Assert.That(content, Does.Contain("Run Linter"),   "Content must include task title");
            Assert.That(content, Does.Contain("lint-task"),    "Content must include task id");
            Assert.That(content, Does.Contain("src/foo.cs"),   "Content must list changed files");
            Assert.That(content, Does.Contain("All clean."),   "Content must include summary text");
            Assert.That(content, Does.Contain("## Tasks Run"), "Content must have a Tasks Run section");
            Assert.That(content, Does.Contain("## Files Changed"), "Content must have a Files Changed section");
            Assert.That(content, Does.Contain("## Summary"),   "Content must have a Summary section");
        });
    }

    [Test]
    public void WriteReport_DurationFormattedCorrectly() {
        // 1 hour 25 minutes should format as e.g. "1h 25m"
        var start  = new DateTimeOffset(2024, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var report = new MaintenanceReport {
            RanTaskIds     = [],
            SkippedTaskIds = [],
            TaskResults    = [],
            StartedAt      = start,
            CompletedAt    = start.AddHours(1).AddMinutes(25),
        };

        _writer.WriteReport(report);
        var paths   = _writer.GetReportPaths();
        var content = File.ReadAllText(paths[0]);

        Assert.That(content, Does.Contain("1h 25m"),
            "Duration of 1h 25m must be formatted as '1h 25m' in the report");
    }

    // ── Pruning ───────────────────────────────────────────────────────────────

    [Test]
    public void WriteReport_PrunesOldFilesWhenOver30() {
        var reportsDir = Path.Combine(_workspace.RootPath, ".squad", "maintenance-reports");
        Directory.CreateDirectory(reportsDir);

        // Pre-populate with exactly 30 files with old timestamps (all before any current-date file)
        for (int i = 0; i < 30; i++) {
            var oldName = $"20200101-{i:D6}.md";
            File.WriteAllText(Path.Combine(reportsDir, oldName), $"# Old report {i}");
        }

        // Write a new report with current time — its filename will sort after the old ones
        var report = MakeReport(startedAt: DateTimeOffset.UtcNow);
        _writer.WriteReport(report);

        var remaining = Directory.GetFiles(reportsDir, "*.md");
        Assert.That(remaining.Length, Is.EqualTo(30),
            "Auto-prune must keep exactly 30 files; oldest must be removed when limit is exceeded");

        // The oldest file (20200101-000000.md) must have been deleted
        Assert.That(remaining.Any(p => Path.GetFileName(p) == "20200101-000000.md"), Is.False,
            "The single oldest report must have been pruned");
    }

    [Test]
    public void WriteReport_DoesNotPruneWhenUnder30() {
        var reportsDir = Path.Combine(_workspace.RootPath, ".squad", "maintenance-reports");
        Directory.CreateDirectory(reportsDir);

        // Pre-populate with only 5 old files
        for (int i = 0; i < 5; i++) {
            var oldName = $"20200101-{i:D6}.md";
            File.WriteAllText(Path.Combine(reportsDir, oldName), $"# Old report {i}");
        }

        var report = MakeReport(startedAt: DateTimeOffset.UtcNow);
        _writer.WriteReport(report);

        var remaining = Directory.GetFiles(reportsDir, "*.md");
        Assert.That(remaining.Length, Is.EqualTo(6),
            "No pruning must occur when total count is below 30");
    }

    // ── GetReportPaths ────────────────────────────────────────────────────────

    [Test]
    public void GetReportPaths_ReturnsNewestFirst() {
        var reportsDir = Path.Combine(_workspace.RootPath, ".squad", "maintenance-reports");
        Directory.CreateDirectory(reportsDir);

        File.WriteAllText(Path.Combine(reportsDir, "20240101-120000.md"), "oldest");
        File.WriteAllText(Path.Combine(reportsDir, "20240601-080000.md"), "middle");
        File.WriteAllText(Path.Combine(reportsDir, "20240901-093000.md"), "newest");

        var paths = _writer.GetReportPaths();

        Assert.That(paths, Has.Count.EqualTo(3));
        Assert.That(Path.GetFileName(paths[0]), Is.EqualTo("20240901-093000.md"),
            "Most recent report must be first");
        Assert.That(Path.GetFileName(paths[2]), Is.EqualTo("20240101-120000.md"),
            "Oldest report must be last");
    }

    [Test]
    public void GetReportPaths_WhenDirectoryAbsent_ReturnsEmpty() {
        // No reports directory has been created — GetReportPaths must not throw
        var paths = _writer.GetReportPaths();
        Assert.That(paths, Is.Empty,
            "GetReportPaths must return an empty list when the reports directory does not exist");
    }

    // TODO: WriteReport_CallsPushNotification — MaintenanceReportWriter does not currently
    // accept or invoke a push notification service. Sending "maintenance_completed" push
    // notifications is the responsibility of the caller (e.g. MaintenanceRunner wired to
    // PushNotificationService.NotifyEventAsync). Add tests here once that wiring is added to
    // this class, or cover it in MaintenanceRunnerTests with a fake IPushNotificationProvider.
}
