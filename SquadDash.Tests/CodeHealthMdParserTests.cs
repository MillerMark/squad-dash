using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Behavioral specs for <see cref="CodeHealthMdParser"/>.
/// Tests will compile once Arjun Sen's Phase 1 implementation lands.
/// </summary>
[TestFixture]
internal sealed class CodeHealthMdParserTests {

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string WriteTempFile(string content) {
        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"maintenance_{Guid.NewGuid():N}.md");
        File.WriteAllText(path, content);
        return path;
    }

    private static void DeleteTempFile(string path) {
        try { File.Delete(path); } catch { /* best-effort cleanup */ }
    }

    // ── File-not-found ────────────────────────────────────────────────────────

    [Test]
    public void Parse_FileNotFound_ReturnsNull() {
        var result = CodeHealthMdParser.Parse(@"C:\does\not\exist\code-health.md");
        Assert.That(result, Is.Null);
    }

    // ── configured: false / missing still returns config ─────────────────────

    [Test]
    public void Parse_ConfiguredKeyAbsentFromFrontmatter_ReturnsConfigWithConfiguredFalse() {
        var path = WriteTempFile(
            """
            ---
            idle_timeout: 10
            max_tasks_per_session: 3
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Configured, Is.False);
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Parse_ConfiguredFalse_ReturnsConfigWithConfiguredFalse() {
        var path = WriteTempFile(
            """
            ---
            configured: false
            idle_timeout: 10
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Configured, Is.False);
        }
        finally { DeleteTempFile(path); }
    }

    // ── Global frontmatter fields ─────────────────────────────────────────────

    [Test]
    public void Parse_GlobalFields_ParsedCorrectly() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            idle_timeout: 20
            max_tasks_per_session: 8
            safety: direct
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.Multiple(() => {
                Assert.That(config!.IdleTimeout,         Is.EqualTo(20));
                Assert.That(config.MaxTasksPerSession,   Is.EqualTo(8));
                Assert.That(config.Safety,               Is.EqualTo("direct"));
            });
        }
        finally { DeleteTempFile(path); }
    }

    // ── Defaults ──────────────────────────────────────────────────────────────

    [Test]
    public void Parse_MissingOptionalGlobalFields_UsesDefaults() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.Multiple(() => {
                Assert.That(config!.IdleTimeout,       Is.EqualTo(15),      "idle_timeout default");
                Assert.That(config.MaxTasksPerSession, Is.EqualTo(5),       "max_tasks_per_session default");
                Assert.That(config.Safety,             Is.EqualTo("branch"),"safety default");
            });
        }
        finally { DeleteTempFile(path); }
    }

    // ── Task block ────────────────────────────────────────────────────────────

    [Test]
    public void Parse_SingleTaskBlock_ParsedCorrectly() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: cleanup
                enabled: true
                frequency: daily
                safety: branch
                title: "Daily Cleanup"
                instructions: "Remove stale temp files and update the changelog."
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Has.Count.EqualTo(1));

            var task = config.Tasks[0];
            Assert.Multiple(() => {
                Assert.That(task.Id,           Is.EqualTo("cleanup"));
                Assert.That(task.Enabled,      Is.True);
                Assert.That(task.Frequency,    Is.EqualTo("daily"));
                Assert.That(task.Safety,       Is.EqualTo("branch"));
                Assert.That(task.Title,        Is.EqualTo("Daily Cleanup"));
                Assert.That(task.Instructions, Does.Contain("Remove stale temp files"));
            });
        }
        finally { DeleteTempFile(path); }
    }

    // ── Task with options block ────────────────────────────────────────────────

    [Test]
    public void Parse_TaskWithOptionsBlock_RadioOptionsParsedCorrectly() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: dependency-update
                enabled: true
                frequency: per-commit
                safety: branch
                title: "Update Dependencies"
                instructions: "Run dependency updates."
                options:
                  strategy:
                    type: radio
                    label: "Update strategy"
                    choices: [patch, minor, major]
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Has.Count.EqualTo(1));

            var task = config.Tasks[0];
            Assert.That(task.Options, Is.Not.Null);
            Assert.That(task.Options, Has.Count.EqualTo(1));

            var opt = task.Options![0];
            Assert.Multiple(() => {
                Assert.That(opt.Key,     Is.EqualTo("strategy"));
                Assert.That(opt.Type,    Is.EqualTo("radio"));
                Assert.That(opt.Label,   Is.EqualTo("Update strategy"));
                Assert.That(opt.Choices, Is.Not.Null);
                Assert.That(opt.Choices, Has.Count.EqualTo(3));
                Assert.That(opt.Choices!.Select(c => c.Value), Does.Contain("patch"));
                Assert.That((opt.Choices ?? []).Select(c => c.Value),  Does.Contain("minor"));
                Assert.That((opt.Choices ?? []).Select(c => c.Value),  Does.Contain("major"));
            });
        }
        finally { DeleteTempFile(path); }
    }

    // ── Safety floor: global report-only ─────────────────────────────────────

    [Test]
    public void Parse_GlobalReportOnly_PerTaskDirectBecomesReportOnly() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            safety: report-only
            tasks:
              - id: aggressive-task
                enabled: true
                frequency: always
                safety: direct
                title: "Should be downgraded"
                instructions: "Make sweeping changes."
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Has.Count.EqualTo(1));

            Assert.That(config.Tasks[0].Safety, Is.EqualTo("report-only"),
                "Global report-only floor must prevent per-task safety from escalating to direct");
        }
        finally { DeleteTempFile(path); }
    }

    // ── Safety floor: global branch ───────────────────────────────────────────

    [Test]
    public void Parse_GlobalBranch_PerTaskDirectDowngradedToBranch() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            safety: branch
            tasks:
              - id: risky-task
                enabled: true
                frequency: always
                safety: direct
                title: "Should be capped at branch"
                instructions: "Directly commit changes."
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Has.Count.EqualTo(1));

            Assert.That(config.Tasks[0].Safety, Is.EqualTo("branch"),
                "Global branch floor must prevent per-task safety from escalating to direct");
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Parse_GlobalBranch_PerTaskReportOnlyKeptAsIs() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            safety: branch
            tasks:
              - id: read-only-task
                enabled: true
                frequency: always
                safety: report-only
                title: "Read-only analysis"
                instructions: "Analyse the build output."
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Is.Not.Null);
            // report-only is already more restrictive than branch; must not be upgraded.
            Assert.That((config.Tasks ?? [])[0].Safety, Is.EqualTo("report-only"),
                "A per-task safety more restrictive than the global floor must not be weakened");
        }
        finally { DeleteTempFile(path); }
    }

    // ── type: radio options parsed as LoopOption entries ─────────────────────

    [Test]
    public void Parse_TaskTypeRadioOptions_ParsedAsLoopOptionEntries() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: code-review
                enabled: true
                frequency: always
                safety: report-only
                title: "Code Review Depth"
                instructions: "Review the latest diff."
                options:
                  depth:
                    type: radio
                    label: "Review depth"
                    choices: [quick, standard, thorough]
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Is.Not.Null);
            var task = (config.Tasks ?? []).Single(t => t.Id == "code-review");
            Assert.That(task.Options, Is.Not.Null);
            var depthOpt = task.Options!.Single(o => o.Key == "depth");
            Assert.Multiple(() => {
                Assert.That(depthOpt.Type,    Is.EqualTo("radio"));
                Assert.That(depthOpt.Choices, Is.Not.Null);
                Assert.That(depthOpt.Choices!.Count, Is.EqualTo(3));
            });
        }
        finally { DeleteTempFile(path); }
    }

    // ── Multi-line instructions (block scalar) ────────────────────────────────

    [Test]
    public void Parse_MultiLineInstructions_AllLinesJoined() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: multi-step
                enabled: true
                frequency: weekly
                safety: branch
                title: "Multi-step Task"
                instructions: |
                  Step one: fetch the latest data.
                  Step two: validate the schema.
                  Step three: publish the report.
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Is.Not.Null);
            var task = (config.Tasks ?? []).Single(t => t.Id == "multi-step");
            Assert.Multiple(() => {
                Assert.That(task.Instructions, Does.Contain("Step one"));
                Assert.That(task.Instructions, Does.Contain("Step two"));
                Assert.That(task.Instructions, Does.Contain("Step three"));
                Assert.That(task.Instructions, Does.Contain("\n"),
                    "Multi-line instructions must contain newline separators");
            });
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Parse_MultiLineInstructionsFollowedByOptions_OptionsParsedCorrectly() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: with-options
                enabled: true
                frequency: daily
                safety: branch
                title: "Task With Options"
                instructions: |
                  Do the first thing.
                  Then do the second thing.
                options:
                  mode:
                    type: radio
                    label: "Run mode"
                    choices: [fast, thorough]
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Is.Not.Null);
            var task = (config.Tasks ?? []).Single(t => t.Id == "with-options");
            Assert.Multiple(() => {
                Assert.That(task.Instructions, Does.Contain("Do the first thing"));
                Assert.That(task.Instructions, Does.Contain("Then do the second thing"));
                Assert.That(task.Options,      Is.Not.Null);
                Assert.That(task.Options,      Has.Count.EqualTo(1));
                Assert.That(task.Options![0].Key, Is.EqualTo("mode"));
            });
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Parse_MultiLineInstructionsRunsToClosingFrontmatter_ParsedCorrectly() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: final-task
                enabled: true
                frequency: daily
                safety: branch
                title: "Final Task"
                instructions: |
                  Line A.
                  Line B.
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Is.Not.Null);
            var task = (config.Tasks ?? []).Single(t => t.Id == "final-task");
            Assert.That(task.Instructions, Does.Contain("Line A"));
            Assert.That(task.Instructions, Does.Contain("Line B"));
        }
        finally { DeleteTempFile(path); }
    }

    // ── Choices YAML list format (S2) ─────────────────────────────────────────

    [Test]
    public void Choices_YAML_list_parsed_correctly() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: run-tests
                enabled: true
                frequency: daily
                safety: branch
                title: "Run Tests"
                instructions: "Run all tests."
                options:
                  if_failing:
                    type: radio
                    label: If failing tests are found
                    default: report
                    choices:
                      - value: fix
                        tooltip: Fix each failing test; commit fixes to the branch
                      - value: report
                        tooltip: Report failures only — do not change any code
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Is.Not.Null);
            var opt = (config.Tasks ?? [])[0].Options!.Single(o => o.Key == "if_failing");
            Assert.That(opt.Choices, Is.Not.Null);
            Assert.That(opt.Choices, Has.Count.EqualTo(2));
            Assert.Multiple(() => {
                Assert.That(opt.Choices![0].Value,   Is.EqualTo("fix"));
                Assert.That(opt.Choices[0].Tooltip,  Is.Not.Empty);
                Assert.That(opt.Choices[1].Value,    Is.EqualTo("report"));
                Assert.That(opt.Choices[1].Tooltip,  Is.Not.Empty);
            });
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Choices_YAML_list_multiple_items() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: dup
                enabled: true
                frequency: daily
                safety: branch
                title: "Dedup"
                instructions: "Find duplicates."
                options:
                  if_found:
                    type: radio
                    label: If duplication is found
                    default: report
                    choices:
                      - value: fix
                        tooltip: Refactor inline on the current branch
                      - value: branch
                        tooltip: Create a maintenance branch and refactor there
                      - value: report
                        tooltip: List each instance — do not change any code
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Is.Not.Null);
            var opt = (config.Tasks ?? [])[0].Options!.Single(o => o.Key == "if_found");
            Assert.That(opt.Choices, Has.Count.EqualTo(3));
            Assert.Multiple(() => {
                Assert.That(opt.Choices![0].Value, Is.EqualTo("fix"));
                Assert.That(opt.Choices[1].Value,  Is.EqualTo("branch"));
                Assert.That(opt.Choices[2].Value,  Is.EqualTo("report"));
            });
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Choices_missing_tooltip_defaults_empty() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: test-task
                enabled: true
                frequency: daily
                safety: branch
                title: "Test"
                instructions: "Do stuff."
                options:
                  mode:
                    type: radio
                    label: Mode
                    default: fast
                    choices:
                      - value: fast
                      - value: slow
                        tooltip: Run slow thorough scan
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Is.Not.Null);
            var opt = (config.Tasks ?? [])[0].Options!.Single(o => o.Key == "mode");
            Assert.That(opt.Choices, Has.Count.EqualTo(2));
            Assert.Multiple(() => {
                Assert.That(opt.Choices![0].Value,   Is.EqualTo("fast"));
                Assert.That(opt.Choices[0].Tooltip,  Is.EqualTo(""),   "missing tooltip should default to empty");
                Assert.That(opt.Choices[1].Tooltip,  Is.EqualTo("Run slow thorough scan"));
            });
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Choices_bracket_format_still_works() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: legacy
                enabled: true
                frequency: daily
                safety: branch
                title: "Legacy Task"
                instructions: "Do something."
                options:
                  action:
                    type: radio
                    label: Action
                    choices: [fix, report]
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Is.Not.Null);
            var opt = (config.Tasks ?? [])[0].Options!.Single(o => o.Key == "action");
            Assert.That(opt.Choices, Has.Count.EqualTo(2));
            Assert.Multiple(() => {
                Assert.That(opt.Choices![0].Value,   Is.EqualTo("fix"));
                Assert.That(opt.Choices[0].Tooltip,  Is.EqualTo(""), "bracket format should have empty tooltip");
                Assert.That(opt.Choices[1].Value,    Is.EqualTo("report"));
                Assert.That(opt.Choices[1].Tooltip,  Is.EqualTo(""), "bracket format should have empty tooltip");
            });
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Choices_YAML_list_followed_by_next_option_key_parses_both() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: multi-opt
                enabled: true
                frequency: daily
                safety: branch
                title: "Multi Option"
                instructions: "Test."
                options:
                  first:
                    type: radio
                    label: First option
                    choices:
                      - value: a
                        tooltip: Choice A
                      - value: b
                        tooltip: Choice B
                  second:
                    type: radio
                    label: Second option
                    choices: [x, y]
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Is.Not.Null);
            var opts = (config.Tasks ?? [])[0].Options!;
            Assert.That(opts, Has.Count.EqualTo(2));
            Assert.That(opts[0].Choices, Has.Count.EqualTo(2));
            Assert.That(opts[1].Choices, Has.Count.EqualTo(2));
            Assert.That(opts[0].Choices![1].Value, Is.EqualTo("b"));
            Assert.That(opts[1].Choices![0].Value, Is.EqualTo("x"));
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Choices_YAML_list_tooltips_contain_expected_text() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: smells
                enabled: false
                frequency: daily
                safety: branch
                title: "Code Smell Cleanup"
                instructions: "Scan for code smells."
                options:
                  if_found:
                    type: radio
                    label: If code smells are found
                    default: report
                    choices:
                      - value: fix
                        tooltip: Address smells inline on the current branch
                      - value: branch
                        tooltip: Create a maintenance branch and address smells there
                      - value: report
                        tooltip: List each smell — do not change any code
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Is.Not.Null);
            var opt = (config.Tasks ?? [])[0].Options!.Single(o => o.Key == "if_found");
            Assert.That(opt.Choices![0].Tooltip, Does.Contain("inline"));
            Assert.That(opt.Choices[1].Tooltip,  Does.Contain("maintenance branch"));
            Assert.That(opt.Choices[2].Tooltip,  Does.Contain("do not change"));
        }
        finally { DeleteTempFile(path); }
    }

    // ── Option group tooltip ────────────────────────────────────────────────────

    [Test]
    public void Parse_OptionTooltip_ParsedCorrectly() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: run-tests
                enabled: true
                frequency: daily
                safety: branch
                title: "Run Tests"
                instructions: "Run all tests."
                options:
                  if_failing:
                    type: radio
                    label: If failing tests are found
                    tooltip: "Fix failures or only report them"
                    value: report
                    choices:
                      - value: fix
                        tooltip: Fix each failing test
                      - value: report
                        tooltip: Report failures only
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Is.Not.Null);
            var opt = (config.Tasks ?? [])[0].Options!.Single(o => o.Key == "if_failing");
            Assert.That(opt.Tooltip, Is.EqualTo("Fix failures or only report them"));
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Parse_OptionHint_BackwardCompatibility() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: run-tests
                enabled: true
                frequency: daily
                safety: branch
                title: "Run Tests"
                instructions: "Run all tests."
                options:
                  if_failing:
                    type: radio
                    label: If failing tests are found
                    hint: "Legacy hint text"
                    value: report
                    choices:
                      - value: fix
                        tooltip: Fix each failing test
                      - value: report
                        tooltip: Report failures only
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Is.Not.Null);
            var opt = (config.Tasks ?? [])[0].Options!.Single(o => o.Key == "if_failing");
            Assert.That(opt.Tooltip, Is.EqualTo("Legacy hint text"),
                "hint: key should populate the Tooltip field for backward compatibility");
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Parse_OptionTooltip_AbsentByDefault() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: run-tests
                enabled: true
                frequency: daily
                safety: branch
                title: "Run Tests"
                instructions: "Run all tests."
                options:
                  if_failing:
                    type: radio
                    label: If failing tests are found
                    value: report
                    choices:
                      - value: fix
                        tooltip: Fix each failing test
                      - value: report
                        tooltip: Report failures only
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Is.Not.Null);
            var opt = (config.Tasks ?? [])[0].Options!.Single(o => o.Key == "if_failing");
            Assert.That(string.IsNullOrEmpty(opt.Tooltip), Is.True,
                "Tooltip should be null or empty when neither tooltip: nor hint: is present");
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Parse_OptionTooltip_TooltipOverridesHint() {
        // If both hint: and tooltip: appear, the last one wins (tooltip: follows hint: in this case)
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: run-tests
                enabled: true
                frequency: daily
                safety: branch
                title: "Run Tests"
                instructions: "Run all tests."
                options:
                  if_failing:
                    type: radio
                    label: If failing tests are found
                    hint: "Old hint"
                    tooltip: "New tooltip"
                    value: report
                    choices:
                      - value: fix
                        tooltip: Fix each failing test
                      - value: report
                        tooltip: Report failures only
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Is.Not.Null);
            var opt = (config.Tasks ?? [])[0].Options!.Single(o => o.Key == "if_failing");
            Assert.That(opt.Tooltip, Is.EqualTo("New tooltip"),
                "tooltip: key should overwrite the value set by hint:");
        }
        finally { DeleteTempFile(path); }
    }

    // ── UpdateOptionValue ──────────────────────────────────────────────────────

    [Test]
    public void UpdateOptionValue_WritesValueToFile() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: run-tests
                enabled: true
                frequency: daily
                safety: branch
                title: "Run Tests"
                instructions: "Run all tests."
                options:
                  if_failing:
                    type: radio
                    label: If failing tests are found
                    value: report
                    choices:
                      - value: fix
                        tooltip: Fix each failing test
                      - value: report
                        tooltip: Report failures only
            ---
            """);
        try {
            CodeHealthMdParser.UpdateOptionValue(path, "run-tests", "if_failing", "fix");
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Is.Not.Null);
            var opt = (config.Tasks ?? [])[0].Options!.Single(o => o.Key == "if_failing");
            Assert.That(opt.RawValue, Is.EqualTo("fix"));
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void UpdateOptionValue_TaskNotFound_DoesNothing() {
        const string original =
            """
            ---
            configured: true
            tasks:
              - id: run-tests
                enabled: true
                frequency: daily
                safety: branch
                title: "Run Tests"
                instructions: "Run all tests."
                options:
                  if_failing:
                    type: radio
                    label: If failing tests are found
                    value: report
                    choices:
                      - value: fix
                        tooltip: Fix each failing test
                      - value: report
                        tooltip: Report failures only
            ---
            """;
        var path = WriteTempFile(original);
        try {
            CodeHealthMdParser.UpdateOptionValue(path, "nonexistent-task", "if_failing", "fix");
            var content = File.ReadAllText(path);
            // The value: line should still be "report"
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Is.Not.Null);
            var opt = (config.Tasks ?? [])[0].Options!.Single(o => o.Key == "if_failing");
            Assert.That(opt.RawValue, Is.EqualTo("report"), "File should be unchanged when task not found");
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void UpdateOptionValue_OptionKeyNotFound_DoesNothing() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: run-tests
                enabled: true
                frequency: daily
                safety: branch
                title: "Run Tests"
                instructions: "Run all tests."
                options:
                  if_failing:
                    type: radio
                    label: If failing tests are found
                    value: report
                    choices:
                      - value: fix
                        tooltip: Fix each failing test
                      - value: report
                        tooltip: Report failures only
            ---
            """);
        try {
            CodeHealthMdParser.UpdateOptionValue(path, "run-tests", "nonexistent_key", "fix");
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Is.Not.Null);
            var opt = (config.Tasks ?? [])[0].Options!.Single(o => o.Key == "if_failing");
            Assert.That(opt.RawValue, Is.EqualTo("report"), "File should be unchanged when option key not found");
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void UpdateOptionValue_FileNotFound_DoesNothing() {
        Assert.DoesNotThrow(() =>
            CodeHealthMdParser.UpdateOptionValue(
                @"C:\does\not\exist\code-health.md", "run-tests", "if_failing", "fix"));
    }

    [Test]
    public void UpdateOptionValue_MultipleTasksMultipleOptions_CorrectOneUpdated() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: task-alpha
                enabled: true
                frequency: daily
                safety: branch
                title: "Alpha"
                instructions: "Do alpha things."
                options:
                  opt_one:
                    type: radio
                    label: Option one
                    value: a
                    choices:
                      - value: a
                        tooltip: Choice A
                      - value: b
                        tooltip: Choice B
                  opt_two:
                    type: radio
                    label: Option two
                    value: x
                    choices:
                      - value: x
                        tooltip: Choice X
                      - value: y
                        tooltip: Choice Y
              - id: task-beta
                enabled: true
                frequency: daily
                safety: branch
                title: "Beta"
                instructions: "Do beta things."
                options:
                  opt_one:
                    type: radio
                    label: Option one
                    value: a
                    choices:
                      - value: a
                        tooltip: Choice A
                      - value: b
                        tooltip: Choice B
                  opt_two:
                    type: radio
                    label: Option two
                    value: x
                    choices:
                      - value: x
                        tooltip: Choice X
                      - value: y
                        tooltip: Choice Y
            ---
            """);
        try {
            CodeHealthMdParser.UpdateOptionValue(path, "task-beta", "opt_two", "y");
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Tasks, Is.Not.Null);

            var alpha = (config.Tasks ?? []).Single(t => t.Id == "task-alpha");
            var beta  = (config.Tasks ?? []).Single(t => t.Id == "task-beta");

            Assert.Multiple(() => {
                Assert.That(alpha.Options!.Single(o => o.Key == "opt_one").RawValue, Is.EqualTo("a"), "alpha.opt_one unchanged");
                Assert.That(alpha.Options!.Single(o => o.Key == "opt_two").RawValue, Is.EqualTo("x"), "alpha.opt_two unchanged");
                Assert.That(beta.Options!.Single(o => o.Key == "opt_one").RawValue,  Is.EqualTo("a"), "beta.opt_one unchanged");
                Assert.That(beta.Options!.Single(o => o.Key == "opt_two").RawValue,  Is.EqualTo("y"), "beta.opt_two updated");
            });
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void UpdateOptionValue_ValueKeyAbsent_DoesNotThrow() {
        // If a task/option exists but has no "value:" sub-key, UpdateOptionValue should do nothing
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: run-tests
                enabled: true
                frequency: daily
                safety: branch
                title: "Run Tests"
                instructions: "Run all tests."
                options:
                  if_failing:
                    type: radio
                    label: If failing tests are found
                    choices:
                      - value: fix
                        tooltip: Fix each failing test
                      - value: report
                        tooltip: Report failures only
            ---
            """);
        try {
            Assert.DoesNotThrow(() =>
                CodeHealthMdParser.UpdateOptionValue(path, "run-tests", "if_failing", "fix"));
        }
        finally { DeleteTempFile(path); }
    }

    // ── Configured property (Bug 1 fix) ───────────────────────────────────────

    [Test]
    public void Parse_ConfiguredFalse_ReturnsConfigWithTasksVisible() {
        var path = WriteTempFile(
            """
            ---
            configured: false
            tasks:
              - id: task-one
                enabled: true
                frequency: daily
                safety: branch
                title: "Task One"
                instructions: "Do task one."
              - id: task-two
                enabled: false
                frequency: weekly
                safety: branch
                title: "Task Two"
                instructions: "Do task two."
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config,            Is.Not.Null,  "config must not be null when configured: false");
            Assert.That(config!.Configured, Is.False,    "Configured must be false");
            Assert.That(config.Tasks,       Is.Not.Null);
            Assert.That(config.Tasks!.Count, Is.EqualTo(2), "both tasks must be visible");
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Parse_ConfiguredTrue_ReturnsConfiguredTrue() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            tasks:
              - id: single-task
                enabled: true
                frequency: daily
                safety: branch
                title: "Single Task"
                instructions: "Run it."
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config,             Is.Not.Null);
            Assert.That(config!.Configured,  Is.True);
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Parse_ConfiguredMissing_DefaultsFalse() {
        var path = WriteTempFile(
            """
            ---
            idle_timeout: 20
            tasks:
              - id: orphan-task
                enabled: true
                frequency: daily
                safety: branch
                title: "Orphan Task"
                instructions: "This task has no configured key above it."
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config,             Is.Not.Null, "config must not be null when configured: key is absent");
            Assert.That(config!.Configured,  Is.False,   "Configured defaults to false when key is absent");
        }
        finally { DeleteTempFile(path); }
    }

    // ── EnabledOnIdle ─────────────────────────────────────────────────────────

    [Test]
    public void Parse_EnabledOnIdle_True() {
        var path = WriteTempFile(
            """
            ---
            idle_timeout: 15
            enabled_on_idle: true
            configured: false
            tasks:
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config,                  Is.Not.Null);
            Assert.That(config!.EnabledOnIdle,    Is.True, "EnabledOnIdle should be true");
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Parse_EnabledOnIdle_False() {
        var path = WriteTempFile(
            """
            ---
            idle_timeout: 15
            enabled_on_idle: false
            configured: true
            tasks:
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config,                  Is.Not.Null);
            Assert.That(config!.EnabledOnIdle,    Is.False, "EnabledOnIdle should be false");
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Parse_EnabledOnIdle_Missing_DefaultsFalse() {
        var path = WriteTempFile(
            """
            ---
            idle_timeout: 15
            configured: true
            tasks:
            ---
            """);
        try {
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config,                  Is.Not.Null);
            Assert.That(config!.EnabledOnIdle,    Is.False, "EnabledOnIdle must default to false when key absent");
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void UpdateEnabledOnIdle_WritesTrue() {
        var path = WriteTempFile(
            """
            ---
            idle_timeout: 15
            enabled_on_idle: false
            configured: false
            tasks:
            ---
            """);
        try {
            CodeHealthMdParser.UpdateEnabledOnIdle(path, true);
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config,                  Is.Not.Null);
            Assert.That(config!.EnabledOnIdle,    Is.True, "EnabledOnIdle should be true after UpdateEnabledOnIdle(true)");
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void UpdateEnabledOnIdle_InsertsWhenMissing() {
        var path = WriteTempFile(
            """
            ---
            idle_timeout: 15
            configured: false
            tasks:
            ---
            """);
        try {
            CodeHealthMdParser.UpdateEnabledOnIdle(path, true);
            var config = CodeHealthMdParser.Parse(path);
            Assert.That(config,                  Is.Not.Null);
            Assert.That(config!.EnabledOnIdle,    Is.True, "EnabledOnIdle should be inserted and set to true");
        }
        finally { DeleteTempFile(path); }
    }

    // ── Override Pattern: Three-file loading with precedence ──────────────────

    [Test]
    public void ParseAllSources_NoOverridesOrCustom_ReturnsSystemTasks() {
        var tempDir = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"workspace_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempDir, ".squad"));

        var systemPath = Path.Combine(tempDir, ".squad", "code-health.md");
        var systemContent =
            """
            ---
            configured: true
            tasks:
              - id: system-task
                enabled: true
                frequency: daily
                safety: branch
                title: "System Task"
                instructions: "This is from the system file."
            ---
            """;
        File.WriteAllText(systemPath, systemContent);

        try {
            var tasks = CodeHealthMdParser.ParseAllSources(tempDir);
            Assert.That(tasks, Has.Count.EqualTo(1));
            Assert.That(tasks[0].Id, Is.EqualTo("system-task"));
        }
        finally {
            try { Directory.Delete(tempDir, true); } catch { /* best-effort cleanup */ }
        }
    }

    [Test]
    public void ParseAllSources_UsesRemoteTeamRootFromSquadConfig() {
        var tempDir = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"workspace_{Guid.NewGuid():N}");
        var projectSquadDir = Path.Combine(tempDir, ".squad");
        var remoteSquadDir = Path.Combine(tempDir, "external-state", ".squad");
        Directory.CreateDirectory(projectSquadDir);
        Directory.CreateDirectory(remoteSquadDir);
        File.WriteAllText(
            Path.Combine(projectSquadDir, "config.json"),
            $$"""
              {
                "version": 1,
                "teamRoot": "{{Path.GetRelativePath(tempDir, remoteSquadDir).Replace('\\', '/')}}"
              }
              """);

        var systemPath = Path.Combine(remoteSquadDir, "code-health.md");
        var systemContent =
            """
            ---
            configured: true
            tasks:
              - id: remote-system-task
                enabled: true
                frequency: daily
                safety: branch
                title: "Remote System Task"
                instructions: "This is from the remote team root."
            ---
            """;
        File.WriteAllText(systemPath, systemContent);

        try {
            var tasks = CodeHealthMdParser.ParseAllSources(tempDir);
            Assert.That(tasks.Select(t => t.Id), Is.EqualTo(new[] { "remote-system-task" }));
        }
        finally {
            try { Directory.Delete(tempDir, true); } catch { /* best-effort cleanup */ }
        }
    }

    [Test]
    public void ParseAllSources_WithCustomTasks_ReturnsBoth() {
        var tempDir = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"workspace_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempDir, ".squad"));

        var systemPath = Path.Combine(tempDir, ".squad", "code-health.md");
        var systemContent =
            """
            ---
            configured: true
            tasks:
              - id: system-task
                enabled: true
                frequency: daily
                safety: branch
                title: "System Task"
                instructions: "From system."
            ---
            """;
        File.WriteAllText(systemPath, systemContent);

        var customPath = Path.Combine(tempDir, ".squad", "code-health-custom.md");
        var customContent =
            """
            ---
            configured: true
            tasks:
              - id: custom-task
                enabled: true
                frequency: weekly
                safety: report-only
                title: "Custom Task"
                instructions: "From custom."
            ---
            """;
        File.WriteAllText(customPath, customContent);

        try {
            var tasks = CodeHealthMdParser.ParseAllSources(tempDir);
            Assert.That(tasks, Has.Count.EqualTo(2));
            Assert.That(tasks.Any(t => t.Id == "system-task"), Is.True);
            Assert.That(tasks.Any(t => t.Id == "custom-task"), Is.True);
        }
        finally {
            try { Directory.Delete(tempDir, true); } catch { /* best-effort cleanup */ }
        }
    }

    [Test]
    public void ParseAllSources_WithOverrides_OverridesHavePrecedence() {
        var tempDir = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"workspace_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempDir, ".squad"));

        var systemPath = Path.Combine(tempDir, ".squad", "code-health.md");
        var systemContent =
            """
            ---
            configured: true
            tasks:
              - id: shared-task
                enabled: true
                frequency: daily
                safety: branch
                title: "Original Title"
                instructions: "Original instructions."
            ---
            """;
        File.WriteAllText(systemPath, systemContent);

        var overridesPath = Path.Combine(tempDir, ".squad", "code-health-overrides.md");
        var overridesContent =
            """
            ---
            configured: true
            tasks:
              - id: shared-task
                enabled: false
                frequency: weekly
                safety: report-only
                title: "Overridden Title"
                instructions: "Overridden instructions."
            ---
            """;
        File.WriteAllText(overridesPath, overridesContent);

        try {
            var tasks = CodeHealthMdParser.ParseAllSources(tempDir);
            Assert.That(tasks, Has.Count.EqualTo(1));
            Assert.That(tasks[0].Id, Is.EqualTo("shared-task"));
            Assert.That(tasks[0].Title, Is.EqualTo("Overridden Title"),
                "Override should take precedence over system version");
            Assert.That(tasks[0].Enabled, Is.False);
        }
        finally {
            try { Directory.Delete(tempDir, true); } catch { /* best-effort cleanup */ }
        }
    }

    [Test]
    public void IsTaskOverridden_TaskInOverridesFile_ReturnsTrue() {
        var tempDir = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"workspace_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempDir, ".squad"));

        var overridesPath = Path.Combine(tempDir, ".squad", "code-health-overrides.md");
        var overridesContent =
            """
            ---
            configured: true
            tasks:
              - id: overridden-task
                enabled: true
                frequency: daily
                safety: branch
                title: "Overridden"
                instructions: "Test."
            ---
            """;
        File.WriteAllText(overridesPath, overridesContent);

        try {
            Assert.That(CodeHealthMdParser.IsTaskOverridden("overridden-task", tempDir), Is.True);
            Assert.That(CodeHealthMdParser.IsTaskOverridden("nonexistent-task", tempDir), Is.False);
        }
        finally {
            try { Directory.Delete(tempDir, true); } catch { /* best-effort cleanup */ }
        }
    }

    [Test]
    public void SaveTaskOverride_CreatesOverridesFile() {
        var tempDir = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"workspace_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempDir, ".squad"));

        var task = new CodeHealthTask(
            Id: "edited-task",
            Enabled: true,
            Frequency: "weekly",
            Safety: "report-only",
            Title: "Edited Task",
            Instructions: "User edited this task.");

        try {
            CodeHealthMdParser.SaveTaskOverride(task, tempDir);

            var overridesPath = Path.Combine(tempDir, ".squad", "code-health-overrides.md");
            Assert.That(File.Exists(overridesPath), Is.True, "Overrides file should be created");

            var config = CodeHealthMdParser.Parse(overridesPath);
            Assert.That(config!.Tasks, Has.Count.EqualTo(1));
            Assert.That(config.Tasks![0].Id, Is.EqualTo("edited-task"));
        }
        finally {
            try { Directory.Delete(tempDir, true); } catch { /* best-effort cleanup */ }
        }
    }

    [Test]
    public void RevertTaskToDefault_RemovesTaskFromOverrides() {
        var tempDir = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"workspace_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempDir, ".squad"));

        var overridesPath = Path.Combine(tempDir, ".squad", "code-health-overrides.md");
        var overridesContent =
            """
            ---
            configured: true
            tasks:
              - id: task-to-revert
                enabled: true
                frequency: daily
                safety: branch
                title: "Task to Revert"
                instructions: "This will be reverted."
            ---
            """;
        File.WriteAllText(overridesPath, overridesContent);

        try {
            Assert.That(CodeHealthMdParser.IsTaskOverridden("task-to-revert", tempDir), Is.True);

            CodeHealthMdParser.RevertTaskToDefault("task-to-revert", tempDir);

            Assert.That(CodeHealthMdParser.IsTaskOverridden("task-to-revert", tempDir), Is.False);
            Assert.That(File.Exists(overridesPath), Is.False,
                "Overrides file should be deleted when it becomes empty");
        }
        finally {
            try { Directory.Delete(tempDir, true); } catch { /* best-effort cleanup */ }
        }
    }

    [Test]
    public void ShouldShowRevertOption_MatchesIsTaskOverridden() {
        var tempDir = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"workspace_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempDir, ".squad"));

        var overridesPath = Path.Combine(tempDir, ".squad", "code-health-overrides.md");
        var overridesContent =
            """
            ---
            configured: true
            tasks:
              - id: overridden
                enabled: true
                frequency: daily
                safety: branch
                title: "Overridden"
                instructions: "Test."
            ---
            """;
        File.WriteAllText(overridesPath, overridesContent);

        try {
            Assert.That(CodeHealthMdParser.ShouldShowRevertOption("overridden", tempDir),
                Is.EqualTo(CodeHealthMdParser.IsTaskOverridden("overridden", tempDir)));
        }
        finally {
            try { Directory.Delete(tempDir, true); } catch { /* best-effort cleanup */ }
        }
    }
}

