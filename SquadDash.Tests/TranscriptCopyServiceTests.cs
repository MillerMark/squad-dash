using System.Threading;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SquadDash.Tests;

/// <summary>
/// Tests for <see cref="TranscriptCopyService"/> and its companion type
/// <see cref="QuickReplyCopyData"/>.
///
/// WPF document objects (<see cref="Paragraph"/>, <see cref="Run"/>, etc.) carry
/// thread affinity and must be constructed on an STA thread. All tests that create
/// WPF elements are decorated with <c>[Apartment(ApartmentState.STA)]</c>.
/// Tests that touch only pure-C# value types (e.g. <see cref="QuickReplyCopyData"/>)
/// do not require STA.
/// </summary>
[TestFixture]
internal sealed class TranscriptCopyServiceTests {

    // ── QuickReplyCopyData.GetCopyText ────────────────────────────────────────

    [Test]
    public void QuickReplyCopyData_GetCopyText_EmptyList_ReturnsEmpty() {
        var data = new QuickReplyCopyData([], CaptionText: null);
        Assert.That(data.GetCopyText(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void QuickReplyCopyData_GetCopyText_AllWhitespaceLabels_ReturnsEmpty() {
        var data = new QuickReplyCopyData(["  ", "\t", ""], CaptionText: null);
        Assert.That(data.GetCopyText(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void QuickReplyCopyData_GetCopyText_SingleLabel_ReturnsBracketed() {
        var data = new QuickReplyCopyData(["Yes"], CaptionText: null);
        Assert.That(data.GetCopyText(), Is.EqualTo("[Yes]"));
    }

    [Test]
    public void QuickReplyCopyData_GetCopyText_MultipleLabels_ReturnsPipeSeparated() {
        var data = new QuickReplyCopyData(["Yes", "No", "Maybe"], CaptionText: null);
        Assert.That(data.GetCopyText(), Is.EqualTo("[Yes | No | Maybe]"));
    }

    [Test]
    public void QuickReplyCopyData_GetCopyText_FiltersWhitespaceFromMixedLabels() {
        var data = new QuickReplyCopyData(["Yes", "  ", "No"], CaptionText: null);
        Assert.That(data.GetCopyText(), Is.EqualTo("[Yes | No]"));
    }

    [Test]
    public void QuickReplyCopyData_GetCopyText_CaptionTextIsIgnoredInOutput() {
        // CaptionText is stored but GetCopyText only uses OptionLabels — verify no bleed-through.
        var data = new QuickReplyCopyData(["Option A"], CaptionText: "What next?");
        Assert.That(data.GetCopyText(), Is.EqualTo("[Option A]"));
    }

    // ── ExtractInlineText ─────────────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void ExtractInlineText_EmptyParagraph_ReturnsEmptyString() {
        var para = new Paragraph();
        Assert.That(TranscriptCopyService.ExtractInlineText(para.Inlines), Is.EqualTo(string.Empty));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ExtractInlineText_SingleRun_ReturnsRunText() {
        var para = new Paragraph(new Run("hello world"));
        Assert.That(TranscriptCopyService.ExtractInlineText(para.Inlines), Is.EqualTo("hello world"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ExtractInlineText_LineBreak_InsertsEnvironmentNewline() {
        var para = new Paragraph();
        para.Inlines.Add(new Run("line one"));
        para.Inlines.Add(new LineBreak());
        para.Inlines.Add(new Run("line two"));

        var text = TranscriptCopyService.ExtractInlineText(para.Inlines);

        Assert.That(text, Is.EqualTo("line one" + Environment.NewLine + "line two"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ExtractInlineText_NestedSpan_FlattensToPlainText() {
        var span = new Span();
        span.Inlines.Add(new Run("inner text"));
        var para = new Paragraph(span);

        Assert.That(TranscriptCopyService.ExtractInlineText(para.Inlines), Is.EqualTo("inner text"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ExtractInlineText_DeeplyNestedSpan_FlattensThroughAllLevels() {
        var innerSpan = new Span();
        innerSpan.Inlines.Add(new Run("deep"));
        var outerSpan = new Span();
        outerSpan.Inlines.Add(innerSpan);
        var para = new Paragraph(outerSpan);

        Assert.That(TranscriptCopyService.ExtractInlineText(para.Inlines), Is.EqualTo("deep"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ExtractInlineText_MixedRunsSpansAndLineBreaks_CombinesAll() {
        var para = new Paragraph();
        para.Inlines.Add(new Run("A"));
        var span = new Span();
        span.Inlines.Add(new Run("B"));
        para.Inlines.Add(span);
        para.Inlines.Add(new LineBreak());
        para.Inlines.Add(new Run("C"));

        var text = TranscriptCopyService.ExtractInlineText(para.Inlines);

        Assert.That(text, Is.EqualTo("AB" + Environment.NewLine + "C"));
    }

    // ── BuildSelectionText — selection gating ─────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void BuildSelectionText_EmptySelection_ReturnsNull() {
        // A freshly constructed RichTextBox has an empty selection (caret only).
        var rtb = new RichTextBox();
        Assert.That(TranscriptCopyService.BuildSelectionText(rtb), Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void BuildSelectionText_WhitespaceOnlyParagraph_ReturnsNull() {
        // Text that reduces to empty after TrimEnd must return null, not "".
        var rtb = MakeRichTextBox(new Paragraph(new Run("   ")));
        Assert.That(TranscriptCopyService.BuildSelectionText(rtb), Is.Null);
    }

    // ── BuildSelectionText — Paragraph blocks ─────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void BuildSelectionText_SingleParagraph_ReturnsText() {
        var rtb = MakeRichTextBox(new Paragraph(new Run("hello world")));
        Assert.That(TranscriptCopyService.BuildSelectionText(rtb), Is.EqualTo("hello world"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void BuildSelectionText_MultipleParagraphs_ContainsBothTexts() {
        var rtb = MakeRichTextBox(
            new Paragraph(new Run("First")),
            new Paragraph(new Run("Second")));

        var result = TranscriptCopyService.BuildSelectionText(rtb);

        Assert.Multiple(() => {
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("First"));
            Assert.That(result, Does.Contain("Second"));
        });
    }

    // ── BuildSelectionText — BlockUIContainer / ICopyable ─────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void BuildSelectionText_ICopyableTag_DelegatesText() {
        var buc = new BlockUIContainer(new TextBlock());
        buc.Tag = new StubCopyable("thinking block content");
        var rtb = MakeRichTextBox(buc);

        Assert.That(TranscriptCopyService.BuildSelectionText(rtb), Is.EqualTo("thinking block content"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void BuildSelectionText_ICopyableReturnsEmptyString_YieldsNull() {
        // TryAppendBlockUIContainer returns true (appends newline) but no text; TrimEnd → null.
        var buc = new BlockUIContainer(new TextBlock());
        buc.Tag = new StubCopyable(string.Empty);
        var rtb = MakeRichTextBox(buc);

        Assert.That(TranscriptCopyService.BuildSelectionText(rtb), Is.Null);
    }

    // ── BuildSelectionText — BlockUIContainer / StackPanel markdown table ──────

    [Test, Apartment(ApartmentState.STA)]
    public void BuildSelectionText_StackPanelWithStringTag_ReturnsTableText() {
        var sp  = new StackPanel { Tag = "| col1 | col2 |" };
        var buc = new BlockUIContainer(sp);
        var rtb = MakeRichTextBox(buc);

        Assert.That(TranscriptCopyService.BuildSelectionText(rtb), Is.EqualTo("| col1 | col2 |"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void BuildSelectionText_StackPanelWithEmptyStringTag_SkipsBlock() {
        // tableText.Length == 0 → falls through to "return false" → block omitted.
        var sp  = new StackPanel { Tag = "" };
        var buc = new BlockUIContainer(sp);
        var rtb = MakeRichTextBox(buc);

        Assert.That(TranscriptCopyService.BuildSelectionText(rtb), Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void BuildSelectionText_StackPanelWithNonStringTag_SkipsBlock() {
        // Tag is not a string → pattern match fails → block omitted.
        var sp  = new StackPanel { Tag = 42 };
        var buc = new BlockUIContainer(sp);
        var rtb = MakeRichTextBox(buc);

        Assert.That(TranscriptCopyService.BuildSelectionText(rtb), Is.Null);
    }

    // ── BuildSelectionText — BlockUIContainer / codeblock TextBox ─────────────

    [Test, Apartment(ApartmentState.STA)]
    public void BuildSelectionText_CodeBlockTextBox_ReturnsCodeText() {
        var tb  = new TextBox { Tag = "codeblock", Text = "int x = 42;" };
        var buc = new BlockUIContainer(tb);
        var rtb = MakeRichTextBox(buc);

        Assert.That(TranscriptCopyService.BuildSelectionText(rtb), Is.EqualTo("int x = 42;"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void BuildSelectionText_CodeBlockTextBox_EmptyText_YieldsNull() {
        // Code block with no text: sb.Append("") + AppendLine → TrimEnd → null.
        var tb  = new TextBox { Tag = "codeblock", Text = "" };
        var buc = new BlockUIContainer(tb);
        var rtb = MakeRichTextBox(buc);

        Assert.That(TranscriptCopyService.BuildSelectionText(rtb), Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void BuildSelectionText_TextBoxWithoutCodeblockTag_SkipsBlock() {
        // Tag != "codeblock" → falls through to "return false" → block omitted.
        var tb  = new TextBox { Tag = "richtext", Text = "hidden" };
        var buc = new BlockUIContainer(tb);
        var rtb = MakeRichTextBox(buc);

        Assert.That(TranscriptCopyService.BuildSelectionText(rtb), Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void BuildSelectionText_BlockUIContainerWithUnrecognizedChild_SkipsBlock() {
        // Child is a plain Button (not StackPanel, not TextBox with codeblock tag, no ICopyable Tag)
        // → TryAppendBlockUIContainer returns false → block omitted entirely.
        var buc = new BlockUIContainer(new Button());
        var rtb = MakeRichTextBox(buc);

        Assert.That(TranscriptCopyService.BuildSelectionText(rtb), Is.Null);
    }

    // ── BuildSelectionText — Section (recursive) ──────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void BuildSelectionText_SectionWithParagraph_RecursesIntoSection() {
        var section = new Section();
        section.Blocks.Add(new Paragraph(new Run("inside section")));
        var rtb = MakeRichTextBox(section);

        Assert.That(TranscriptCopyService.BuildSelectionText(rtb), Is.EqualTo("inside section"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void BuildSelectionText_NestedSections_RecursesThroughAllLevels() {
        var inner = new Section();
        inner.Blocks.Add(new Paragraph(new Run("deep paragraph")));
        var outer = new Section();
        outer.Blocks.Add(inner);
        var rtb = MakeRichTextBox(outer);

        Assert.That(TranscriptCopyService.BuildSelectionText(rtb), Is.EqualTo("deep paragraph"));
    }

    // ── BuildSelectionText — mixed heterogeneous block sequences ──────────────

    [Test, Apartment(ApartmentState.STA)]
    public void BuildSelectionText_ParagraphThenCodeBlock_ContainsBothTexts() {
        var tb  = new TextBox { Tag = "codeblock", Text = "console.log('hi')" };
        var buc = new BlockUIContainer(tb);
        var rtb = MakeRichTextBox(
            new Paragraph(new Run("Here is some code:")),
            buc);

        var result = TranscriptCopyService.BuildSelectionText(rtb);

        Assert.Multiple(() => {
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("Here is some code:"));
            Assert.That(result, Does.Contain("console.log('hi')"));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void BuildSelectionText_ParagraphThenSectionThenICopyable_ContainsAllTexts() {
        var section = new Section();
        section.Blocks.Add(new Paragraph(new Run("section content")));

        var buc = new BlockUIContainer(new TextBlock());
        buc.Tag = new StubCopyable("[quick reply]");

        var rtb = MakeRichTextBox(
            new Paragraph(new Run("intro")),
            section,
            buc);

        var result = TranscriptCopyService.BuildSelectionText(rtb);

        Assert.Multiple(() => {
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("intro"));
            Assert.That(result, Does.Contain("section content"));
            Assert.That(result, Does.Contain("[quick reply]"));
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="RichTextBox"/> whose document contains exactly
    /// <paramref name="blocks"/> (the default empty paragraph is removed) and
    /// whose selection spans the entire document.
    /// </summary>
    private static RichTextBox MakeRichTextBox(params Block[] blocks) {
        var rtb = new RichTextBox();
        rtb.Document.Blocks.Clear();
        foreach (var block in blocks)
            rtb.Document.Blocks.Add(block);

        // Select the full document so BuildSelectionText sees every block.
        rtb.Selection.Select(rtb.Document.ContentStart, rtb.Document.ContentEnd);
        return rtb;
    }

    private sealed class StubCopyable(string text) : ICopyable {
        public string GetCopyText() => text;
    }
}
