using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class MarkdownHtmlBuilderPriorityTests {

    // ── Task 1 helpers ───────────────────────────────────────────────────

    private static string Build(string markdown, bool isDark = false)
        => MarkdownHtmlBuilder.Build(markdown, "Test", isDark: isDark);

    // ── Emoji → pdot span replacements ──────────────────────────────────

    [Test]
    public void Build_ReplacesRedCircle_WithPdotHighSpan() {
        var html = Build("🔴 critical task");
        Assert.That(html, Does.Contain("<span class=\"pdot pdot-high\"></span>"));
        Assert.That(html, Does.Not.Contain("🔴"));
    }

    [Test]
    public void Build_ReplacesYellowCircle_WithPdotMidSpan() {
        var html = Build("🟡 medium task");
        Assert.That(html, Does.Contain("<span class=\"pdot pdot-mid\"></span>"));
        Assert.That(html, Does.Not.Contain("🟡"));
    }

    [Test]
    public void Build_ReplacesGreenCircle_WithPdotLowSpan() {
        var html = Build("🟢 low task");
        Assert.That(html, Does.Contain("<span class=\"pdot pdot-low\"></span>"));
        Assert.That(html, Does.Not.Contain("🟢"));
    }

    // ── Legacy prio-* colour CSS is absent ───────────────────────────────

    [Test]
    public void Build_DoesNotEmit_LegacyPrioColorCss() {
        // prio-high / prio-mid / prio-low CSS rules used to carry color declarations;
        // those are now gone — only the class names on elements remain.
        var html = Build("## 🔴 High\n## 🟡 Mid\n## 🟢 Low");
        Assert.That(html, Does.Not.Contain(".prio-high {"));
        Assert.That(html, Does.Not.Contain(".prio-mid {"));
        Assert.That(html, Does.Not.Contain(".prio-low {"));
    }

    // ── Emoji replacement happens AFTER HTML escape (no tag corruption) ──

    [Test]
    public void Build_EmojiReplacement_HappensAfterHtmlEscape_NoTagCorruption() {
        var html = Build("A&B 🔴 task");
        // Ampersand should be escaped
        Assert.That(html, Does.Contain("A&amp;B"));
        // Priority dot should still appear
        Assert.That(html, Does.Contain("<span class=\"pdot pdot-high\"></span>"));
    }

    // ── All three emojis on one line ──────────────────────────────────────

    [Test]
    public void Build_AllThreeEmojisOnOneLine_AllReplaced() {
        var html = Build("🔴 high 🟡 mid 🟢 low");
        Assert.That(html, Does.Contain("<span class=\"pdot pdot-high\"></span>"));
        Assert.That(html, Does.Contain("<span class=\"pdot pdot-mid\"></span>"));
        Assert.That(html, Does.Contain("<span class=\"pdot pdot-low\"></span>"));
        Assert.That(html, Does.Not.Contain("🔴"));
        Assert.That(html, Does.Not.Contain("🟡"));
        Assert.That(html, Does.Not.Contain("🟢"));
    }

    // ── No priority emoji → no pdot spans ────────────────────────────────

    [Test]
    public void Build_NoPriorityEmoji_EmitsNoPdotSpans() {
        var html = Build("Just a plain task with no emoji.");
        // The CSS will always include the .pdot class definition, but the body
        // should contain no <span class="pdot ..."> elements.
        Assert.That(html, Does.Not.Contain("pdot pdot-"));
    }

    // ── .pdot CSS definition present in both light and dark modes ────────

    [Test]
    public void Build_LightMode_IncludesPdotCssDefinition() {
        var html = Build("sample", isDark: false);
        Assert.That(html, Does.Contain(".pdot {"));
    }

    [Test]
    public void Build_DarkMode_IncludesPdotCssDefinition() {
        var html = Build("sample", isDark: true);
        Assert.That(html, Does.Contain(".pdot {"));
    }
}
