using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class LoopMdParserTests {

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string WriteTempFile(string content) {
        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"loop_{Guid.NewGuid():N}.md");
        File.WriteAllText(path, content);
        return path;
    }

    private static void DeleteTempFile(string path) {
        try { File.Delete(path); } catch { /* best-effort cleanup */ }
    }

    // ── File-not-found ────────────────────────────────────────────────────────

    [Test]
    public void Parse_FileNotFound_ReturnsNull() {
        var result = LoopMdParser.Parse(@"C:\does\not\exist\loop.md");
        Assert.That(result, Is.Null);
    }

    // ── Missing / false configured ────────────────────────────────────────────

    [Test]
    public void Parse_MissingConfiguredKey_ReturnsNull() {
        var path = WriteTempFile(
            """
            ---
            interval: 5
            timeout: 2
            ---
            Do something.
            """);
        try {
            Assert.That(LoopMdParser.Parse(path), Is.Null);
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Parse_ConfiguredFalse_ReturnsNull() {
        var path = WriteTempFile(
            """
            ---
            configured: false
            interval: 5
            ---
            Do something.
            """);
        try {
            Assert.That(LoopMdParser.Parse(path), Is.Null);
        }
        finally { DeleteTempFile(path); }
    }

    // ── Valid frontmatter + body ──────────────────────────────────────────────

    [Test]
    public void Parse_ValidFrontmatterAndBody_ReturnsCorrectConfig() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            interval: 15
            timeout: 7
            description: "My loop description"
            ---
            Run the tests and report results.
            """);
        try {
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.IntervalMinutes, Is.EqualTo(15));
            Assert.That(config.TimeoutMinutes,   Is.EqualTo(7));
            Assert.That(config.Description,      Is.EqualTo("My loop description"));
            Assert.That(config.Instructions,     Is.EqualTo("Run the tests and report results."));
        }
        finally { DeleteTempFile(path); }
    }

    // ── Default values for optional fields ────────────────────────────────────

    [Test]
    public void Parse_MissingOptionalFields_UsesDefaults() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            ---
            """);
        try {
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.IntervalMinutes, Is.EqualTo(10));
            Assert.That(config.TimeoutMinutes,   Is.EqualTo(5));
            Assert.That(config.Description,      Is.EqualTo(""));
            Assert.That(config.Instructions,     Is.EqualTo(""));
        }
        finally { DeleteTempFile(path); }
    }

    // ── CRLF line endings ─────────────────────────────────────────────────────

    [Test]
    public void Parse_CrlfLineEndings_ParsesCorrectly() {
        var content =
            "---\r\n" +
            "configured: true\r\n" +
            "interval: 20\r\n" +
            "---\r\n" +
            "Instructions body.\r\n";
        var path = WriteTempFile(content);
        try {
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.IntervalMinutes, Is.EqualTo(20));
            Assert.That(config.Instructions,     Is.EqualTo("Instructions body."));
        }
        finally { DeleteTempFile(path); }
    }

    // ── Body extraction ───────────────────────────────────────────────────────

    [Test]
    public void Parse_MultiLineBody_ExtractedAndTrimmed() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            ---

            Line one.
            Line two.
            Line three.
            """);
        try {
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            // Leading/trailing blank lines are trimmed; internal content preserved.
            Assert.That(config!.Instructions, Does.StartWith("Line one."));
            Assert.That(config.Instructions, Does.Contain("Line two."));
            Assert.That(config.Instructions, Does.EndWith("Line three."));
        }
        finally { DeleteTempFile(path); }
    }

    // ── Quoted / unquoted description ─────────────────────────────────────────

    [Test]
    public void Parse_UnquotedDescription_ParsesCorrectly() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            description: Plain description
            ---
            """);
        try {
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Description, Is.EqualTo("Plain description"));
        }
        finally { DeleteTempFile(path); }
    }

    // ── No opening --- ────────────────────────────────────────────────────────

    [Test]
    public void Parse_NoFrontmatterDelimiter_ReturnsNull() {
        var path = WriteTempFile("configured: true\ninterval: 5\n");
        try {
            Assert.That(LoopMdParser.Parse(path), Is.Null);
        }
        finally { DeleteTempFile(path); }
    }

    // ── commands: frontmatter field ───────────────────────────────────────────

    [Test]
    public void Parse_CommandsList_ParsedCorrectly() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            commands: [stop_loop, start_loop]
            ---
            Do work.
            """);
        try {
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Commands, Is.Not.Null);
            Assert.That(config.Commands, Has.Count.EqualTo(2));
            Assert.That(config.Commands, Does.Contain("stop_loop"));
            Assert.That(config.Commands, Does.Contain("start_loop"));
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Parse_SingleCommand_ParsedCorrectly() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            commands: [stop_loop]
            ---
            Do work.
            """);
        try {
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Commands, Has.Count.EqualTo(1));
            Assert.That(config.Commands![0], Is.EqualTo("stop_loop"));
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Parse_NoCommandsField_ReturnsEmptyList() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            ---
            Do work.
            """);
        try {
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Commands, Is.Not.Null);
            Assert.That(config.Commands, Is.Empty);
        }
        finally { DeleteTempFile(path); }
    }

    // ── ScanForLoopFiles ──────────────────────────────────────────────────────

    [Test]
    public void ScanForLoopFiles_DirectoryDoesNotExist_ReturnsEmpty() {
        var result = LoopMdParser.ScanForLoopFiles(@"C:\this\path\does\not\exist");
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ScanForLoopFiles_EmptyDirectory_ReturnsEmpty() {
        var dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"loopscan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try {
            var result = LoopMdParser.ScanForLoopFiles(dir);
            Assert.That(result, Is.Empty);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void ScanForLoopFiles_DescriptionWithHyphen_SplitsDisplayNameAndTooltip() {
        var dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"loopscan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var loopPath = Path.Combine(dir, "loop.md");
        File.WriteAllText(loopPath,
            """
            ---
            configured: true
            description: "Daily Build - runs tests and reports"
            ---
            Do the thing.
            """);
        try {
            var result = LoopMdParser.ScanForLoopFiles(dir);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].DisplayName, Is.EqualTo("Daily Build"));
            Assert.That(result[0].TooltipText, Is.EqualTo("runs tests and reports"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void ScanForLoopFiles_DescriptionWithEnDash_SplitsCorrectly() {
        var dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"loopscan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var loopPath = Path.Combine(dir, "loop.md");
        // Use an actual en-dash character (U+2013) in the description string.
        File.WriteAllText(loopPath,
            "---\nconfigured: true\ndescription: \"Fast Loop \u2013 lightweight check\"\n---\nBody.\n");
        try {
            var result = LoopMdParser.ScanForLoopFiles(dir);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].DisplayName, Is.EqualTo("Fast Loop"));
            Assert.That(result[0].TooltipText, Is.EqualTo("lightweight check"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void ScanForLoopFiles_DescriptionNoDash_TooltipIsEmpty() {
        var dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"loopscan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var loopPath = Path.Combine(dir, "loop.md");
        File.WriteAllText(loopPath,
            """
            ---
            configured: true
            description: "Simple Loop"
            ---
            Body.
            """);
        try {
            var result = LoopMdParser.ScanForLoopFiles(dir);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].DisplayName, Is.EqualTo("Simple Loop"));
            Assert.That(result[0].TooltipText, Is.EqualTo(""));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void ScanForLoopFiles_NoDescription_FallsBackToFilename() {
        var dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"loopscan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        // Use a filename without a hyphen so the fallback name isn't split.
        var loopPath = Path.Combine(dir, "loopweekly.md");
        File.WriteAllText(loopPath,
            """
            ---
            configured: true
            ---
            Body.
            """);
        try {
            var result = LoopMdParser.ScanForLoopFiles(dir);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].DisplayName, Is.EqualTo("loopweekly"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void ScanForLoopFiles_LoopMdSortsFirst() {
        var dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"loopscan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "loop-zzz.md"),
            "---\nconfigured: true\ndescription: ZZZ\n---\n");
        File.WriteAllText(Path.Combine(dir, "loop.md"),
            "---\nconfigured: true\ndescription: Default\n---\n");
        File.WriteAllText(Path.Combine(dir, "loop-aaa.md"),
            "---\nconfigured: true\ndescription: AAA\n---\n");
        try {
            var result = LoopMdParser.ScanForLoopFiles(dir);
            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(Path.GetFileName(result[0].FilePath), Is.EqualTo("loop.md").IgnoreCase);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void ScanForLoopFiles_NonDefaultFilesSortedAlphabetically() {
        var dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"loopscan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "loop-charlie.md"),
            "---\nconfigured: true\ndescription: Charlie\n---\n");
        File.WriteAllText(Path.Combine(dir, "loop-alpha.md"),
            "---\nconfigured: true\ndescription: Alpha\n---\n");
        File.WriteAllText(Path.Combine(dir, "loop-bravo.md"),
            "---\nconfigured: true\ndescription: Bravo\n---\n");
        try {
            var result = LoopMdParser.ScanForLoopFiles(dir);
            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(Path.GetFileName(result[0].FilePath), Is.EqualTo("loop-alpha.md").IgnoreCase);
            Assert.That(Path.GetFileName(result[1].FilePath), Is.EqualTo("loop-bravo.md").IgnoreCase);
            Assert.That(Path.GetFileName(result[2].FilePath), Is.EqualTo("loop-charlie.md").IgnoreCase);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── options: block parsing ────────────────────────────────────────────────

    [Test]
    public void Parse_OptionsBlock_IntervalAndTimeoutDerivedFromOptions() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            options:
              interval:
                value: 3
                type: int
                label: "Interval (min)"
              timeout:
                value: 30
                type: int
                label: "Timeout (min)"
            description: "Test loop"
            ---
            Do work.
            """);
        try {
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.IntervalMinutes, Is.EqualTo(3));
            Assert.That(config.TimeoutMinutes,   Is.EqualTo(30));
            Assert.That(config.Options, Is.Not.Null);
            Assert.That(config.Options, Has.Count.EqualTo(2));
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Parse_OptionsBlock_AllFieldsParsedCorrectly() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            options:
              commit_after_task:
                value: ask
                type: enum
                choices: [always, never, ask]
                label: "Commit after task"
                hint: "When to commit"
              build_verify:
                value: true
                type: bool
                label: "Verify build"
                hint: "Run build check"
            ---
            Body text.
            """);
        try {
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Options, Has.Count.EqualTo(2));

            var commitOpt = config.Options!.First(o => o.Key == "commit_after_task");
            Assert.That(commitOpt.RawValue, Is.EqualTo("ask"));
            Assert.That(commitOpt.Type,     Is.EqualTo("enum"));
            Assert.That(commitOpt.Label,    Is.EqualTo("Commit after task"));
            Assert.That(commitOpt.Hint,     Is.EqualTo("When to commit"));
            Assert.That(commitOpt.Choices,  Is.Not.Null);
            Assert.That(commitOpt.Choices,  Has.Count.EqualTo(3));
            Assert.That(commitOpt.Choices,  Does.Contain("always"));
            Assert.That(commitOpt.Choices,  Does.Contain("never"));
            Assert.That(commitOpt.Choices,  Does.Contain("ask"));

            var buildOpt = config.Options.First(o => o.Key == "build_verify");
            Assert.That(buildOpt.RawValue, Is.EqualTo("true"));
            Assert.That(buildOpt.Type,     Is.EqualTo("bool"));
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Parse_OptionsBlock_FlatIntervalStillWorksForBackwardsCompat() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            interval: 7
            timeout: 2
            ---
            Body.
            """);
        try {
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.IntervalMinutes, Is.EqualTo(7));
            Assert.That(config.TimeoutMinutes,   Is.EqualTo(2));
            Assert.That(config.Options, Is.Null);
        }
        finally { DeleteTempFile(path); }
    }

    // ── UpdateOptionValue ─────────────────────────────────────────────────────

    [Test]
    public void UpdateOptionValue_ExistingKey_UpdatesValueInFile() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            options:
              interval:
                value: 5
                type: int
              timeout:
                value: 60
                type: int
            ---
            Body.
            """);
        try {
            LoopMdParser.UpdateOptionValue(path, "interval", "15");
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.IntervalMinutes, Is.EqualTo(15));
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void UpdateOptionValue_SecondKey_UpdatesCorrectOption() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            options:
              interval:
                value: 5
                type: int
              timeout:
                value: 60
                type: int
            ---
            Body.
            """);
        try {
            LoopMdParser.UpdateOptionValue(path, "timeout", "90");
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.TimeoutMinutes, Is.EqualTo(90));
            Assert.That(config.IntervalMinutes, Is.EqualTo(5), "other option unchanged");
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void UpdateOptionValue_NonExistentKey_DoesNothing() {
        var content = "---\nconfigured: true\noptions:\n  interval:\n    value: 5\n    type: int\n---\nBody.";
        var path = WriteTempFile(content);
        try {
            LoopMdParser.UpdateOptionValue(path, "nonexistent", "999");
            Assert.That(File.ReadAllText(path), Does.Contain("value: 5"), "file should be unchanged");
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void UpdateOptionValue_FileNotFound_DoesNotThrow() {
        Assert.DoesNotThrow(() =>
            LoopMdParser.UpdateOptionValue(@"C:\does\not\exist\loop.md", "interval", "5"));
    }


    [Test]
    public void StripFrontmatter_NormalContent_RemovesFrontmatter() {
        var content =
            "---\nconfigured: true\ninterval: 5\n---\nThis is the body.";
        var result = LoopMdParser.StripFrontmatter(content);
        Assert.That(result, Is.EqualTo("This is the body."));
    }

    [Test]
    public void StripFrontmatter_NoFrontmatter_ReturnsOriginal() {
        var content = "Just a plain body without frontmatter.";
        var result = LoopMdParser.StripFrontmatter(content);
        Assert.That(result, Is.EqualTo(content));
    }

    [Test]
    public void StripFrontmatter_BlankLinesAfterClosingDelimiter_AreTrimmed() {
        var content = "---\nconfigured: true\n---\n\n\nActual body here.";
        var result = LoopMdParser.StripFrontmatter(content);
        Assert.That(result, Is.EqualTo("Actual body here."));
    }

    [Test]
    public void StripFrontmatter_MultiLineBody_PreservesInternalContent() {
        var content = "---\nconfigured: true\n---\nLine one.\nLine two.\nLine three.";
        var result = LoopMdParser.StripFrontmatter(content);
        Assert.That(result, Is.EqualTo("Line one.\nLine two.\nLine three."));
    }

    // ── Options: YAML insertion order ─────────────────────────────────────────

    [Test]
    public void Parse_Options_PreservesYamlInsertionOrder() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            options:
              alpha:
                value: 1
                type: int
              beta:
                value: 2
                type: int
              gamma:
                value: 3
                type: int
            ---
            Body.
            """);
        try {
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Options, Has.Count.EqualTo(3));
            Assert.That(config.Options![0].Key, Is.EqualTo("alpha"));
            Assert.That(config.Options[1].Key,  Is.EqualTo("beta"));
            Assert.That(config.Options[2].Key,  Is.EqualTo("gamma"));
        }
        finally { DeleteTempFile(path); }
    }

    // ── Options: group type ───────────────────────────────────────────────────

    [Test]
    public void Parse_Options_GroupType_ParsedCorrectly() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            options:
              after_task_header:
                type: group
                label: "After Task Completes:"
              build_verify:
                value: true
                type: bool
                label: "Verify build"
            ---
            Body.
            """);
        try {
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Options, Has.Count.EqualTo(2));

            var groupOpt = config.Options![0];
            Assert.That(groupOpt.Key,      Is.EqualTo("after_task_header"));
            Assert.That(groupOpt.Type,     Is.EqualTo("group"));
            Assert.That(groupOpt.Label,    Is.EqualTo("After Task Completes:"));
            Assert.That(groupOpt.RawValue, Is.EqualTo(""));
            Assert.That(groupOpt.Hint,     Is.Null);
            Assert.That(groupOpt.Choices,  Is.Null);

            var boolOpt = config.Options[1];
            Assert.That(boolOpt.Key,  Is.EqualTo("build_verify"));
            Assert.That(boolOpt.Type, Is.EqualTo("bool"));
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Parse_Options_GroupFollowedByChildren_OrderPreserved() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            options:
              after_task_header:
                type: group
                label: "After Task Completes:"
              build_verify:
                value: true
                type: bool
                label: "Verify build"
              test_after_task:
                value: true
                type: bool
                label: "Write tests"
              commit_after_task:
                value: ask
                type: enum
                choices: [always, never, ask]
                label: "Commit"
              interval:
                value: 1
                type: int
                label: "Interval (min)"
              timeout:
                value: 60
                type: int
                label: "Timeout (min)"
              max_iterations:
                value: 0
                type: int
                label: "Max iterations"
            ---
            Body.
            """);
        try {
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Options, Has.Count.EqualTo(7));

            Assert.That(config.Options![0].Key, Is.EqualTo("after_task_header"), "index 0");
            Assert.That(config.Options[0].Type, Is.EqualTo("group"));
            Assert.That(config.Options[1].Key,  Is.EqualTo("build_verify"),      "index 1");
            Assert.That(config.Options[2].Key,  Is.EqualTo("test_after_task"),   "index 2");
            Assert.That(config.Options[3].Key,  Is.EqualTo("commit_after_task"), "index 3");
            Assert.That(config.Options[4].Key,  Is.EqualTo("interval"),          "index 4");
            Assert.That(config.Options[5].Key,  Is.EqualTo("timeout"),           "index 5");
            Assert.That(config.Options[6].Key,  Is.EqualTo("max_iterations"),    "index 6");
        }
        finally { DeleteTempFile(path); }
    }

    // ── UpdateOptionValue: bool and enum ──────────────────────────────────────

    [Test]
    public void UpdateOptionValue_Bool_UpdatesCorrectly() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            options:
              build_verify:
                value: true
                type: bool
                label: "Verify build"
            ---
            Body.
            """);
        try {
            LoopMdParser.UpdateOptionValue(path, "build_verify", "false");
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            var opt = config!.Options!.First(o => o.Key == "build_verify");
            Assert.That(opt.RawValue, Is.EqualTo("false"));
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void UpdateOptionValue_Enum_UpdatesCorrectly() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            options:
              commit_after_task:
                value: ask
                type: enum
                choices: [always, never, ask]
                label: "Commit"
            ---
            Body.
            """);
        try {
            LoopMdParser.UpdateOptionValue(path, "commit_after_task", "always");
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            var opt = config!.Options!.First(o => o.Key == "commit_after_task");
            Assert.That(opt.RawValue, Is.EqualTo("always"));
        }
        finally { DeleteTempFile(path); }
    }

    // ── Variable injection ────────────────────────────────────────────────────

    [Test]
    public void VariableInjection_OptionValues_AreSubstituted() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            options:
              build_verify:
                value: true
                type: bool
              commit_after_task:
                value: ask
                type: enum
                choices: [always, never, ask]
            ---
            Body.
            """);
        try {
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);

            string text = "build_verify = {{build_verify}}, commit = {{commit_after_task}}";
            foreach (var opt in config!.Options!)
                text = text.Replace($"{{{{{opt.Key}}}}}", opt.RawValue, StringComparison.Ordinal);

            Assert.That(text, Is.EqualTo("build_verify = true, commit = ask"));
        }
        finally { DeleteTempFile(path); }
    }

    private static LoopOption Opt(string key, string value, string type = "string") =>
        new(key, value, type, null, null, null);

    [Test]
    public void VariableInjection_GroupOption_ReplacesPlaceholderWithEmpty() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            options:
              after_task_header:
                type: group
                label: "After Task Completes:"
            ---
            Body.
            """);
        try {
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);

            string text = "header={{after_task_header}}end";
            foreach (var opt in config!.Options!)
                text = text.Replace($"{{{{{opt.Key}}}}}", opt.RawValue, StringComparison.Ordinal);

            Assert.That(text, Is.EqualTo("header=end"),
                "group option RawValue is empty string, so placeholder is replaced with nothing");
        }
        finally { DeleteTempFile(path); }
    }

    // ── PreprocessConditionals: mid-line tag handling ─────────────────────────

    [Test]
    public void PreprocessConditionals_MidLineIf_ConditionTrue_EmitsPrefixAndBody() {
        var text =
            "4. {{#if key == \"value\"}}\n" +
            "   Body line.\n" +
            "   {{/if}}";
        var options = new[] { Opt("key", "value") };

        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Is.EqualTo("4. Body line."));
    }

    [Test]
    public void PreprocessConditionals_MidLineIf_ConditionFalse_OmitsPrefixAndBody() {
        var text =
            "4. {{#if key == \"value\"}}\n" +
            "   Body line.\n" +
            "   {{/if}}";
        var options = new[] { Opt("key", "other") };

        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Is.EqualTo(""));
    }

    [Test]
    public void PreprocessConditionals_MidLineUnless_ConditionNotMet_EmitsPrefixAndBody() {
        // unless + key != expected → condition not met → block is included
        var text =
            "4. {{#unless key == \"value\"}}\n" +
            "   Unless body.\n" +
            "   {{/unless}}";
        var options = new[] { Opt("key", "other") };

        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Is.EqualTo("4. Unless body."));
    }

    [Test]
    public void PreprocessConditionals_MidLineUnless_ConditionMet_OmitsPrefixAndBody() {
        // unless + key == expected → condition met → block is excluded
        var text =
            "4. {{#unless key == \"value\"}}\n" +
            "   Unless body.\n" +
            "   {{/unless}}";
        var options = new[] { Opt("key", "value") };

        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Is.EqualTo(""));
    }

    [Test]
    public void PreprocessConditionals_NumberedListThreeBranches_AlwaysValue_EmitsOnlyAlwaysBranch() {
        var text =
            "4. {{#if commit_after_task == \"always\"}}\n" +
            "   Commit automatically.\n" +
            "   {{/if}}\n" +
            "   {{#if commit_after_task == \"ask\"}}\n" +
            "   Ask first.\n" +
            "   {{/if}}\n" +
            "   {{#if commit_after_task == \"never\"}}\n" +
            "   Never commit.\n" +
            "   {{/if}}";
        var options = new[] { Opt("commit_after_task", "always", "enum") };

        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Does.StartWith("4. "),                 "numbered prefix emitted for matched branch");
        Assert.That(result, Does.Contain("Commit automatically."), "always branch body included");
        Assert.That(result, Does.Not.Contain("Ask first."),        "ask branch excluded");
        Assert.That(result, Does.Not.Contain("Never commit."),     "never branch excluded");
    }

    [Test]
    public void PreprocessConditionals_NumberedListThreeBranches_AskValue_EmitsOnlyAskBranch() {
        var text =
            "4. {{#if commit_after_task == \"always\"}}\n" +
            "   Commit automatically.\n" +
            "   {{/if}}\n" +
            "   {{#if commit_after_task == \"ask\"}}\n" +
            "   Ask first.\n" +
            "   {{/if}}\n" +
            "   {{#if commit_after_task == \"never\"}}\n" +
            "   Never commit.\n" +
            "   {{/if}}";
        var options = new[] { Opt("commit_after_task", "ask", "enum") };

        var result = LoopMdParser.PreprocessConditionals(text, options);

        // The "4. " prefix is on the false "always" branch — it must not be emitted
        Assert.That(result, Does.Not.StartWith("4. "),                  "prefix of false branch not emitted");
        Assert.That(result, Does.Not.Contain("Commit automatically."),  "always branch excluded");
        Assert.That(result, Does.Contain("Ask first."),                 "ask branch body included");
        Assert.That(result, Does.Not.Contain("Never commit."),          "never branch excluded");
    }

    [Test]
    public void PreprocessConditionals_NumberedListThreeBranches_NeverValue_EmitsOnlyNeverBranch() {
        var text =
            "4. {{#if commit_after_task == \"always\"}}\n" +
            "   Commit automatically.\n" +
            "   {{/if}}\n" +
            "   {{#if commit_after_task == \"ask\"}}\n" +
            "   Ask first.\n" +
            "   {{/if}}\n" +
            "   {{#if commit_after_task == \"never\"}}\n" +
            "   Never commit.\n" +
            "   {{/if}}";
        var options = new[] { Opt("commit_after_task", "never", "enum") };

        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Does.Not.Contain("Commit automatically."), "always branch excluded");
        Assert.That(result, Does.Not.Contain("Ask first."),            "ask branch excluded");
        Assert.That(result, Does.Contain("Never commit."),             "never branch body included");
    }

    [Test]
    public void PreprocessConditionals_WhitespaceOnlyPrefix_ConditionTrue_PrefixNotEmittedBodyIncluded() {
        var text =
            "   {{#if key == \"val\"}}\n" +
            "   Body line.\n" +
            "   {{/if}}";
        var options = new[] { Opt("key", "val") };

        var result = LoopMdParser.PreprocessConditionals(text, options);

        // Whitespace-only prefix must never be emitted (same as whole-line tag behaviour)
        Assert.That(result, Is.EqualTo("   Body line."));
    }

    [Test]
    public void PreprocessConditionals_WhitespaceOnlyPrefix_ConditionFalse_BodyExcluded() {
        var text =
            "   {{#if key == \"val\"}}\n" +
            "   Body line.\n" +
            "   {{/if}}";
        var options = new[] { Opt("key", "other") };

        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Is.EqualTo(""));
    }

    [Test]
    public void PreprocessConditionals_MixedWholeLineAndMidLineTags_BothProcessedCorrectly() {
        var text =
            "Intro line.\n" +
            "{{#if show_intro == \"yes\"}}\n" +
            "Intro details.\n" +
            "{{/if}}\n" +
            "Step 1. {{#if do_step == \"true\"}}\n" +
            "   Do the step.\n" +
            "   {{/if}}\n" +
            "Footer.";
        var options = new[] {
            Opt("show_intro", "yes"),
            Opt("do_step",    "true"),
        };

        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Does.Contain("Intro line."),    "static line before blocks preserved");
        Assert.That(result, Does.Contain("Intro details."), "whole-line tag block body included");
        Assert.That(result, Does.Contain("Step 1. "),       "mid-line prefix emitted for true branch");
        Assert.That(result, Does.Contain("Do the step."),   "mid-line tag block body included");
        Assert.That(result, Does.Contain("Footer."),        "static line after blocks preserved");
    }

    // ── PreprocessConditionals: pending-prefix (fix: prepend to first content line) ─

    [Test]
    public void PreprocessConditionals_InlineIf_Included_PrependsPrefixToFirstContentLine() {
        var text =
            "4. {{#if x == \"a\"}}\n" +
            "   Content here\n" +
            "   {{/if}}";
        var options = new[] { Opt("x", "a") };

        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Is.EqualTo("4. Content here"));
    }

    [Test]
    public void PreprocessConditionals_InlineIf_Excluded_EmitsNothing() {
        var text =
            "4. {{#if x == \"a\"}}\n" +
            "   Content here\n" +
            "   {{/if}}";
        var options = new[] { Opt("x", "b") };

        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Is.EqualTo(""));
    }

    [Test]
    public void PreprocessConditionals_NumberedListMultiBranch_FirstBranchSelected_PrependsPrefixToContent() {
        // Only the first {{#if}} has a real prefix ("4. "); the others have whitespace-only prefixes.
        var text =
            "4. {{#if x == \"a\"}}\n" +
            "   Branch A\n" +
            "   {{/if}}\n" +
            "   {{#if x == \"b\"}}\n" +
            "   Branch B\n" +
            "   {{/if}}";
        var options = new[] { Opt("x", "a") };

        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Does.Contain("4. Branch A"), "prefix prepended to first content line");
        Assert.That(result, Does.Not.Contain("Branch B"), "second branch excluded");
    }

    [Test]
    public void PreprocessConditionals_NumberedListMultiBranch_SecondBranchSelected_NoPrefixOnSecondBranch() {
        var text =
            "4. {{#if x == \"a\"}}\n" +
            "   Branch A\n" +
            "   {{/if}}\n" +
            "   {{#if x == \"b\"}}\n" +
            "   Branch B\n" +
            "   {{/if}}";
        var options = new[] { Opt("x", "b") };

        var result = LoopMdParser.PreprocessConditionals(text, options);

        Assert.That(result, Does.Not.Contain("4. "),     "prefix of false branch not emitted");
        Assert.That(result, Does.Not.Contain("Branch A"), "first branch excluded");
        Assert.That(result, Does.Contain("Branch B"),     "second branch body included");
    }

    // ── Safety field ──────────────────────────────────────────────────────────

    [Test]
    public void Parse_SafetyDirect_ParsedCorrectly() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            safety: direct
            ---
            Do work.
            """);
        try {
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Safety, Is.EqualTo("direct"));
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void Parse_SafetyAbsent_DefaultsToisBranch() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            ---
            Do work.
            """);
        try {
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Safety, Is.EqualTo("branch"));
        }
        finally { DeleteTempFile(path); }
    }

    // ── UpdateDescription round-trip ─────────────────────────────────────────

    [Test]
    public void UpdateDescription_ExistingLine_RoundTrip() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            description: Old description
            ---
            Body.
            """);
        try {
            LoopMdParser.UpdateDescription(path, "New description");
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Description, Is.EqualTo("New description"));
        }
        finally { DeleteTempFile(path); }
    }

    // ── UpdateSafety round-trip ───────────────────────────────────────────────

    [Test]
    public void UpdateSafety_ExistingLine_RoundTrip() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            safety: branch
            ---
            Body.
            """);
        try {
            LoopMdParser.UpdateSafety(path, "direct");
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Safety, Is.EqualTo("direct"));
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void UpdateSafety_AbsentLine_InsertsAndRoundTrips() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            ---
            Body.
            """);
        try {
            LoopMdParser.UpdateSafety(path, "report-only");
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Safety, Is.EqualTo("report-only"));
        }
        finally { DeleteTempFile(path); }
    }

    // ── UpdateInstructions round-trip ─────────────────────────────────────────

    [Test]
    public void UpdateInstructions_ReplacesBodyAndFrontmatterPreserved() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            description: My loop
            ---
            Old instructions.
            """);
        try {
            LoopMdParser.UpdateInstructions(path, "Brand new instructions.");
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Instructions, Is.EqualTo("Brand new instructions."));
            Assert.That(config.Description,   Is.EqualTo("My loop"), "frontmatter must be unchanged");
        }
        finally { DeleteTempFile(path); }
    }

    // ── UpdateOptions round-trip ──────────────────────────────────────────────

    [Test]
    public void UpdateOptions_RoundTrip_OptionsRestoredFromEditorYaml() {
        // Start with a loop file that has an options block in loop format
        var path = WriteTempFile(
            """
            ---
            configured: true
            description: "Test loop"
            options:
              build_verify:
                value: true
                type: bool
                label: "Verify build"
                hint: "Run a build check after each task"
              commit_after_task:
                value: ask
                type: enum
                choices: [always, never, ask]
                label: "Commit after task"
            ---
            Body.
            """);
        try {
            // Simulate what the editor does: parse → convert to CodeHealthOption → serialize
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Options, Has.Count.EqualTo(2));

            // Convert LoopOption → editor YAML (mirrors LoopPanelEditLoopMdMenuItem_Click)
            var sb = new System.Text.StringBuilder();
            foreach (var opt in config.Options!) {
                sb.AppendLine($"{opt.Key}:");
                if (!string.IsNullOrEmpty(opt.Type))   sb.AppendLine($"  type: {opt.Type}");
                if (!string.IsNullOrEmpty(opt.Label))  sb.AppendLine($"  label: {opt.Label}");
                if (!string.IsNullOrEmpty(opt.Hint))   sb.AppendLine($"  tooltip: {opt.Hint}");
                if (!string.IsNullOrEmpty(opt.RawValue)) sb.AppendLine($"  value: {opt.RawValue}");
                if (opt.Choices is { Count: > 0 }) {
                    sb.AppendLine("  choices:");
                    foreach (var c in opt.Choices)
                        sb.AppendLine($"    - value: {c}");
                }
            }
            var editorYaml = sb.ToString().TrimEnd();

            // Save via UpdateOptions (as OnSave does)
            LoopMdParser.UpdateOptions(path, editorYaml);

            // Re-parse and verify options survived the round-trip
            var config2 = LoopMdParser.Parse(path);
            Assert.That(config2, Is.Not.Null);
            Assert.That(config2!.Options, Has.Count.EqualTo(2));

            var bv = config2.Options![0];
            Assert.That(bv.Key,      Is.EqualTo("build_verify"));
            Assert.That(bv.Type,     Is.EqualTo("bool"));
            Assert.That(bv.Label,    Is.EqualTo("Verify build"));
            Assert.That(bv.Hint,     Is.EqualTo("Run a build check after each task"));
            Assert.That(bv.RawValue, Is.EqualTo("true"));

            var ca = config2.Options[1];
            Assert.That(ca.Key,      Is.EqualTo("commit_after_task"));
            Assert.That(ca.Type,     Is.EqualTo("enum"));
            Assert.That(ca.RawValue, Is.EqualTo("ask"));
            Assert.That(ca.Choices,  Is.Not.Null);
            Assert.That(ca.Choices,  Has.Count.EqualTo(3));
            Assert.That(ca.Choices,  Does.Contain("always"));
            Assert.That(ca.Choices,  Does.Contain("never"));
            Assert.That(ca.Choices,  Does.Contain("ask"));

            // Other frontmatter must be unchanged
            Assert.That(config2.Description, Is.EqualTo("Test loop"));
            Assert.That(config2.Instructions, Is.EqualTo("Body."));
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void UpdateOptions_NullYaml_RemovesOptionsBlock() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            options:
              interval:
                value: 5
                type: int
            ---
            Body.
            """);
        try {
            LoopMdParser.UpdateOptions(path, null);
            var raw = File.ReadAllText(path);
            Assert.That(raw, Does.Not.Contain("options:"));
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Options, Is.Null.Or.Empty);
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void UpdateOptions_NoExistingOptionsBlock_InsertsNewBlock() {
        var path = WriteTempFile(
            """
            ---
            configured: true
            description: "Simple"
            ---
            Body.
            """);
        try {
            var editorYaml = "my_opt:\n  type: bool\n  value: true";
            LoopMdParser.UpdateOptions(path, editorYaml);
            var config = LoopMdParser.Parse(path);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Options, Is.Not.Null);
            Assert.That(config.Options, Has.Count.EqualTo(1));
            Assert.That(config.Options![0].Key,  Is.EqualTo("my_opt"));
            Assert.That(config.Options[0].Type,  Is.EqualTo("bool"));
            Assert.That(config.Options[0].RawValue, Is.EqualTo("true"));
            Assert.That(config.Description, Is.EqualTo("Simple"), "other frontmatter unchanged");
        }
        finally { DeleteTempFile(path); }
    }

    [Test]
    public void UpdateOptions_FileNotFound_DoesNotThrow() {
        Assert.DoesNotThrow(() =>
            LoopMdParser.UpdateOptions(@"C:\does\not\exist\loop.md", "foo:\n  value: bar"));
    }
}
