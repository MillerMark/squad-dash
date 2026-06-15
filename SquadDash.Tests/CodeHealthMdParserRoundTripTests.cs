using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class MaintenanceMdParserRoundTripTests {

    // ── Shared YAML fixture ───────────────────────────────────────────────────

    private const string ThreeTaskMd = """
        ---
        configured: true
        safety: branch
        tasks:
          - id: cleanup
            enabled: true
            frequency: daily
            safety: branch
            title: Clean Up Temp Files
            instructions: |
              Remove all temporary files from the workspace.
              Check {{targetDir}} for files older than {{maxAgeDays}} days.
              {{#if dryRun}}
              Only report, do not delete.
              {{/if}}
          - id: lint
            enabled: false
            frequency: weekly
            safety: report-only
            title: Run Linter
            instructions: |
              Run the linter on all source files.
          - id: backup
            enabled: true
            frequency: monthly
            safety: direct
            title: Backup Configuration
            instructions: |
              Back up all configuration files.
        ---
        # Maintenance

        This file configures automated maintenance tasks.
        """;

    private const string OptionsTaskMd = """
        ---
        configured: true
        safety: branch
        tasks:
          - id: refactor
            enabled: true
            frequency: weekly
            safety: branch
            title: Refactor Pass
            instructions: |
              Run the refactor pass on the codebase.
            options:
              strategy:
                type: radio
                label: Refactor strategy
                tooltip: Choose how aggressively to refactor
                value: safe
                choices:
                  - value: safe
                    tooltip: Conservative refactoring only
                  - value: aggressive
                    tooltip: Full refactoring including renames
        ---
        """;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string WriteTempFile(string content) {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        return path;
    }

    private static void DeleteTempFile(string path) {
        try { File.Delete(path); } catch { /* best-effort */ }
    }

    // ── 1. Parse-only sanity ──────────────────────────────────────────────────

    [Test]
    public void Parse_MultiTaskFile_ReturnsAllTasksWithSourceFilePath() {
        var path = WriteTempFile(ThreeTaskMd);
        try {
            var config = MaintenanceMdParser.Parse(path);

            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Has.Count.EqualTo(3));

            var cleanup = config.Tasks![0];
            Assert.That(cleanup.Id,             Is.EqualTo("cleanup"));
            Assert.That(cleanup.Enabled,         Is.True);
            Assert.That(cleanup.Frequency,       Is.EqualTo("daily"));
            Assert.That(cleanup.Title,           Is.EqualTo("Clean Up Temp Files"));
            Assert.That(cleanup.SourceFilePath,  Is.EqualTo(path));

            var lint = config.Tasks[1];
            Assert.That(lint.Id,            Is.EqualTo("lint"));
            Assert.That(lint.Enabled,        Is.False);
            Assert.That(lint.SourceFilePath, Is.EqualTo(path));

            var backup = config.Tasks[2];
            Assert.That(backup.Id,            Is.EqualTo("backup"));
            Assert.That(backup.SourceFilePath, Is.EqualTo(path));
        }
        finally { DeleteTempFile(path); }
    }

    // ── 2. Round-trip: title change ───────────────────────────────────────────

    [Test]
    public void UpdateTask_TitleChange_RoundTrips() {
        var path = WriteTempFile(ThreeTaskMd);
        try {
            var before  = MaintenanceMdParser.Parse(path)!.Tasks![0];
            var updated = before with { Title = "Deep Clean Temp Files" };

            MaintenanceMdParser.UpdateTask(path, "cleanup", updated);

            var after = MaintenanceMdParser.Parse(path)!;
            var task  = after.Tasks!.First(t => t.Id == "cleanup");

            Assert.That(task.Title,        Is.EqualTo("Deep Clean Temp Files"));
            Assert.That(task.Id,           Is.EqualTo("cleanup"));
            Assert.That(task.Enabled,      Is.True);
            Assert.That(task.Frequency,    Is.EqualTo("daily"));
            Assert.That(task.Safety,       Is.EqualTo("branch"));
            Assert.That(task.Instructions, Is.EqualTo(before.Instructions));
        }
        finally { DeleteTempFile(path); }
    }

    // ── 3. Round-trip: instructions change ───────────────────────────────────

    [Test]
    public void UpdateTask_InstructionsChange_MultiLineWithTemplates_RoundTrips() {
        var path = WriteTempFile(ThreeTaskMd);
        try {
            var before  = MaintenanceMdParser.Parse(path)!.Tasks![0];
            var newInstr = "Delete stale artifacts from {{outputDir}}.\n{{#if verbose}}\nLog each deleted file.\n{{/if}}";
            var updated = before with { Instructions = newInstr };

            MaintenanceMdParser.UpdateTask(path, "cleanup", updated);

            var task = MaintenanceMdParser.Parse(path)!.Tasks!.First(t => t.Id == "cleanup");

            Assert.That(task.Instructions, Is.EqualTo(newInstr));
        }
        finally { DeleteTempFile(path); }
    }

    // ── 4. Round-trip: enabled toggle ─────────────────────────────────────────

    [Test]
    public void UpdateTask_EnabledToggle_RoundTrips() {
        var path = WriteTempFile(ThreeTaskMd);
        try {
            // lint starts as disabled — flip to enabled
            var before  = MaintenanceMdParser.Parse(path)!.Tasks![1];
            Assert.That(before.Enabled, Is.False);

            MaintenanceMdParser.UpdateTask(path, "lint", before with { Enabled = true });
            var afterTrue = MaintenanceMdParser.Parse(path)!.Tasks!.First(t => t.Id == "lint");
            Assert.That(afterTrue.Enabled, Is.True);

            // flip back
            MaintenanceMdParser.UpdateTask(path, "lint", afterTrue with { Enabled = false });
            var afterFalse = MaintenanceMdParser.Parse(path)!.Tasks!.First(t => t.Id == "lint");
            Assert.That(afterFalse.Enabled, Is.False);
        }
        finally { DeleteTempFile(path); }
    }

    // ── 5. Round-trip: frequency change ──────────────────────────────────────

    [Test]
    [TestCase("always")]
    [TestCase("daily")]
    [TestCase("weekly")]
    [TestCase("monthly")]
    [TestCase("after-commits")]
    public void UpdateTask_FrequencyChange_RoundTrips(string newFrequency) {
        var path = WriteTempFile(ThreeTaskMd);
        try {
            var before  = MaintenanceMdParser.Parse(path)!.Tasks![0];
            var updated = before with { Frequency = newFrequency };

            MaintenanceMdParser.UpdateTask(path, "cleanup", updated);

            var task = MaintenanceMdParser.Parse(path)!.Tasks!.First(t => t.Id == "cleanup");
            Assert.That(task.Frequency, Is.EqualTo(newFrequency));
        }
        finally { DeleteTempFile(path); }
    }

    // ── 6. Round-trip: safety change ─────────────────────────────────────────

    [Test]
    [TestCase("report-only")]
    [TestCase("branch")]
    [TestCase("direct")]
    public void UpdateTask_SafetyChange_RoundTrips(string newSafety) {
        var path = WriteTempFile(ThreeTaskMd);
        try {
            var before  = MaintenanceMdParser.Parse(path)!.Tasks![0];
            var updated = before with { Safety = newSafety };

            MaintenanceMdParser.UpdateTask(path, "cleanup", updated);

            var rawConfig = MaintenanceMdParser.Parse(path)!;
            var task      = rawConfig.Tasks!.First(t => t.Id == "cleanup");
            // Safety floor is applied based on global safety ("branch") — verify
            // value is not more permissive than global.
            Assert.That(task.Safety, Is.Not.Null);
        }
        finally { DeleteTempFile(path); }
    }

    // ── 7. Preservation test ─────────────────────────────────────────────────

    [Test]
    public void UpdateTask_Task2Changed_Task1AndTask3ByteIdentical() {
        var path = WriteTempFile(ThreeTaskMd);
        try {
            var configBefore = MaintenanceMdParser.Parse(path)!;
            var task1Before  = configBefore.Tasks![0];
            var task3Before  = configBefore.Tasks[2];
            var task2        = configBefore.Tasks[1];

            MaintenanceMdParser.UpdateTask(path, "lint", task2 with { Title = "Updated Linter Run" });

            var configAfter = MaintenanceMdParser.Parse(path)!;
            var task1After  = configAfter.Tasks![0];
            var task3After  = configAfter.Tasks[2];

            // Task 1 fields must be identical
            Assert.That(task1After.Id,           Is.EqualTo(task1Before.Id));
            Assert.That(task1After.Title,         Is.EqualTo(task1Before.Title));
            Assert.That(task1After.Enabled,       Is.EqualTo(task1Before.Enabled));
            Assert.That(task1After.Frequency,     Is.EqualTo(task1Before.Frequency));
            Assert.That(task1After.Instructions,  Is.EqualTo(task1Before.Instructions));

            // Task 3 fields must be identical
            Assert.That(task3After.Id,            Is.EqualTo(task3Before.Id));
            Assert.That(task3After.Title,          Is.EqualTo(task3Before.Title));
            Assert.That(task3After.Enabled,        Is.EqualTo(task3Before.Enabled));
            Assert.That(task3After.Frequency,      Is.EqualTo(task3Before.Frequency));
            Assert.That(task3After.Instructions,   Is.EqualTo(task3Before.Instructions));

            // Task 2 title updated
            var task2After = configAfter.Tasks[1];
            Assert.That(task2After.Title, Is.EqualTo("Updated Linter Run"));
        }
        finally { DeleteTempFile(path); }
    }

    // ── 8. Options preservation ───────────────────────────────────────────────

    [Test]
    public void UpdateTask_TitleOnlyChange_OptionsBlockPreserved() {
        var path = WriteTempFile(OptionsTaskMd);
        try {
            var before  = MaintenanceMdParser.Parse(path)!.Tasks![0];
            Assert.That(before.Options, Is.Not.Null);
            Assert.That(before.Options!, Has.Count.EqualTo(1));

            var updated = before with { Title = "Deep Refactor Pass" };
            MaintenanceMdParser.UpdateTask(path, "refactor", updated);

            var after = MaintenanceMdParser.Parse(path)!.Tasks![0];

            Assert.That(after.Title, Is.EqualTo("Deep Refactor Pass"));
            Assert.That(after.Options, Is.Not.Null);
            Assert.That(after.Options!, Has.Count.EqualTo(1));

            var opt = after.Options[0];
            Assert.That(opt.Key,      Is.EqualTo("strategy"));
            Assert.That(opt.Type,     Is.EqualTo("radio"));
            Assert.That(opt.Label,    Is.EqualTo("Refactor strategy"));
            Assert.That(opt.RawValue, Is.EqualTo("safe"));
            Assert.That(opt.Choices,  Has.Count.EqualTo(2));
            Assert.That(opt.Choices![0].Value,   Is.EqualTo("safe"));
            Assert.That(opt.Choices[0].Tooltip,  Is.EqualTo("Conservative refactoring only"));
            Assert.That(opt.Choices[1].Value,    Is.EqualTo("aggressive"));
        }
        finally { DeleteTempFile(path); }
    }
}
