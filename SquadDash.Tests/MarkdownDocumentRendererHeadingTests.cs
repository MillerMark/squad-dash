using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using SquadDash;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class MarkdownDocumentRendererHeadingTests {

    // ── Helpers ───────────────────────────────────────────────────────────

    private static MarkdownDocumentRenderer MakeRenderer(
        string? gitHubUrl = "https://github.com/owner/repo",
        Func<string, string?>? resolvePlanDisplayName = null) =>
        new(
            getFontSize:               () => 14.0,
            getWorkspaceGitHubUrl:     () => gitHubUrl,
            onLinkClicked:             _ => { },
            onException:               (_, _) => { },
            resolveContinuationThread: _ => null,
            onQuickReplyButtonClick:   (_, _) => { },
            appendResponseSegment:     (_, _, _) => { },
            scrollToEndIfAtBottom:     _ => { },
            getCoordinatorThread:      () => null!,
            resolvePlanDisplayName:    resolvePlanDisplayName);

    private static IEnumerable<Inline> FlattenInlines(IEnumerable<Inline> inlines) {
        foreach (var inline in inlines) {
            yield return inline;
            if (inline is Span span)
                foreach (var child in FlattenInlines(span.Inlines))
                    yield return child;
        }
    }

    private static List<Block> BuildHeadingBlocks(
        string markdownLine,
        string? gitHubUrl = "https://github.com/owner/repo",
        Func<string, string?>? resolvePlanDisplayName = null) {
        EnsureApplicationResources();
        var renderer = MakeRenderer(gitHubUrl, resolvePlanDisplayName);
        var thread   = new TranscriptThreadState("t1", TranscriptThreadKind.Coordinator, "Test", DateTimeOffset.Now);
        var section  = new Section();
        var turn     = new TranscriptTurnView(thread, "prompt", DateTimeOffset.Now, section, []);
        var entry    = new TranscriptResponseEntry(turn, 1, section, allowQuickReplies: false);
        return renderer.BuildResponseBlocks(entry, markdownLine, allowQuickReplies: false).ToList();
    }

    [Test]
    public void BarePlanOrTaskId_RendersPlanNameAsLinkText()
    {
        var blocks = BuildHeadingBlocks(
            "Found candidate for MODELPROF-20260810-007.",
            resolvePlanDisplayName: reference =>
                reference == "MODELPROF-20260810-007" ? "Model Profiles" : null);

        var allInlines = blocks.OfType<Paragraph>()
            .SelectMany(paragraph => FlattenInlines(paragraph.Inlines))
            .ToList();
        var link = allInlines.OfType<Hyperlink>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(link.Inlines.OfType<Run>().Single().Text, Is.EqualTo("Model Profiles"));
            Assert.That(link.Tag, Is.EqualTo("app://open-plan:MODELPROF-20260810-007"));
            Assert.That(allInlines.OfType<Run>().Any(run =>
                run.Text.Contains("MODELPROF-20260810-007", StringComparison.Ordinal)), Is.False);
        });
    }

    private static void EnsureApplicationResources() {
        var app = Application.Current ?? new Application();
        app.Resources["FontSizeNormal"] = 14.0;
    }

    private static TextBox FindCodeTextBox(Block block) {
        var container = (BlockUIContainer)block;
        var stack = (StackPanel)container.Child;
        return stack.Children.OfType<TextBox>().Single();
    }

    // ── Commit hash in heading ────────────────────────────────────────────

    [Test]
    public void Heading_WithBacktickCommitHash_RendersHashAsHyperlink() {
        // ### ✅ `LoopController` hardened — committed `7932ea8`
        var blocks = BuildHeadingBlocks("### ✅ `LoopController` hardened — committed `7932ea8`");

        var paragraph = blocks.OfType<Paragraph>().Single();
        var allInlines = FlattenInlines(paragraph.Inlines).ToList();

        Assert.That(allInlines.OfType<Hyperlink>().Any(h => {
            var run = h.Inlines.OfType<Run>().FirstOrDefault();
            return run?.Text == "7932ea8";
        }), Is.True, "Commit hash inside backticks in a heading should render as a Hyperlink");
    }

    [Test]
    public void Heading_WithBareCommitHash_RendersHashAsHyperlink() {
        var blocks = BuildHeadingBlocks("## Merged abc1234f into main");

        var paragraph = blocks.OfType<Paragraph>().Single();
        var allInlines = FlattenInlines(paragraph.Inlines).ToList();

        Assert.That(allInlines.OfType<Hyperlink>().Any(h => {
            var run = h.Inlines.OfType<Run>().FirstOrDefault();
            return run?.Text == "abc1234f";
        }), Is.True, "Bare commit hash in a heading should render as a Hyperlink");
    }

    [Test]
    public void Heading_WithNoGitHubUrl_DoesNotRenderHashAsHyperlink() {
        // Without a GitHub URL, bare hashes should NOT become links
        var blocks = BuildHeadingBlocks("## Merged abc1234f into main", gitHubUrl: null);

        var paragraph = blocks.OfType<Paragraph>().Single();
        var allInlines = FlattenInlines(paragraph.Inlines).ToList();

        Assert.That(allInlines.OfType<Hyperlink>(), Is.Empty,
            "No Hyperlink should be created when GitHub URL is not configured");
    }

    // ── Heading still bold ────────────────────────────────────────────────

    [Test]
    public void Heading_PlainText_IsRenderedBold() {
        var blocks = BuildHeadingBlocks("### Summary");

        var paragraph = blocks.OfType<Paragraph>().Single();
        // After fix, inlines are wrapped in a Bold span
        var bold = FlattenInlines(paragraph.Inlines).OfType<Bold>().FirstOrDefault()
                   ?? paragraph.Inlines.OfType<Bold>().FirstOrDefault();

        Assert.That(bold, Is.Not.Null, "Heading text should be wrapped in a Bold inline");
    }

    // ── Heading font size ─────────────────────────────────────────────────

    [Test]
    public void Heading_Level1_HasLargerFontThanLevel3() {
        var h1 = BuildHeadingBlocks("# Big title").OfType<Paragraph>().Single();
        var h3 = BuildHeadingBlocks("### Small title").OfType<Paragraph>().Single();

        Assert.That(h1.FontSize, Is.GreaterThan(h3.FontSize));
    }

    [Test]
    public void CodeFence_LongOuterFence_IgnoresShorterInnerFence() {
        var blocks = BuildHeadingBlocks("""
            Intro

            ````markdown
            # Artifact prompt

            ```json
            { "ok": true }
            ```

            Still inside the artifact.
            ````

            Outro
            """);

        var codeBlocks = blocks.OfType<BlockUIContainer>().ToList();
        var codeText = FindCodeTextBox(codeBlocks.Single()).Text;

        Assert.Multiple(() => {
            Assert.That(codeText, Does.Contain("```json"));
            Assert.That(codeText, Does.Contain("{ \"ok\": true }"));
            Assert.That(codeText, Does.Contain("Still inside the artifact."));
            Assert.That(blocks.OfType<Paragraph>().Select(p => p.Tag), Does.Contain("Intro"));
            Assert.That(blocks.OfType<Paragraph>().Select(p => p.Tag), Does.Contain("Outro"));
        });
    }
}
