using System;
using System.IO;
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
}
