using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using VoiceHeuristics;

namespace SquadDash.Tests;

/// <summary>
/// Regression coverage for the Step 1 PromptTextBox TextBox → RichTextBox migration
/// (PROMPTEDITOR-20261012-001). Proves that <see cref="RichTextBoxExtensions"/> — the
/// API surface PromptTextBox now uses in place of plain TextBox members — round-trips
/// representative plain text exactly, and that the plain-text-only paste enforcement
/// decision logic never lets rich formats through regardless of clipboard content.
/// </summary>
[TestFixture]
internal sealed class PromptTextBoxRichTextBoxMigrationTests
{
    // ── Round-trip: GetPlainText/SetPlainText preserve exact text ──────────────

    [Test, Apartment(ApartmentState.STA)]
    public void RoundTrip_EmptyString_PreservesExactText()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText(string.Empty);
        Assert.That(rtb.GetPlainText(), Is.EqualTo(string.Empty));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void RoundTrip_SingleLine_PreservesExactText()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("Hello, world!");
        Assert.That(rtb.GetPlainText(), Is.EqualTo("Hello, world!"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void RoundTrip_MultiLineText_PreservesExactText()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("line1\nline2\nline3");
        Assert.That(rtb.GetPlainText(), Is.EqualTo("line1\nline2\nline3"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void RoundTrip_WindowsLineEndings_NormalizeToLf()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("line1\r\nline2\r\nline3");
        Assert.That(rtb.GetPlainText(), Is.EqualTo("line1\nline2\nline3"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void RoundTrip_TripleBacktickFence_PreservesExactText()
    {
        // Representative of the embedded code-region content Steps 2/3 will build on.
        var rtb = new RichTextBox();
        var text = "before\n```csharp\nvar x = 1;\n```\nafter";
        rtb.SetPlainText(text);
        Assert.That(rtb.GetPlainText(), Is.EqualTo(text));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void RoundTrip_SpecialCharactersAndEmoji_PreservesExactText()
    {
        var rtb = new RichTextBox();
        var text = "Special chars: <tag> & \"quotes\" 'apostrophe' \t tab — em-dash 🚀 emoji";
        rtb.SetPlainText(text);
        Assert.That(rtb.GetPlainText(), Is.EqualTo(text));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void RoundTrip_LeadingAndTrailingWhitespace_PreservesExactText()
    {
        var rtb = new RichTextBox();
        var text = "   leading and trailing spaces   ";
        rtb.SetPlainText(text);
        Assert.That(rtb.GetPlainText(), Is.EqualTo(text));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void RoundTrip_ReSetAfterInitialSet_OverwritesCompletely()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("first content");
        rtb.SetPlainText("second content");
        Assert.That(rtb.GetPlainText(), Is.EqualTo("second content"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void RoundTrip_NullText_TreatedAsEmptyString()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText(null);
        Assert.That(rtb.GetPlainText(), Is.EqualTo(string.Empty));
    }

    // ── AppendPlainText (used by fullscreen-peek / clipboard-paste key handlers) ──

    [Test, Apartment(ApartmentState.STA)]
    public void AppendPlainText_ToEmptyBox_SetsText()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText(string.Empty);
        rtb.AppendPlainText("appended");
        Assert.That(rtb.GetPlainText(), Is.EqualTo("appended"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void AppendPlainText_ToExistingText_AppendsAtEnd()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("existing");
        rtb.AppendPlainText(" more");
        Assert.That(rtb.GetPlainText(), Is.EqualTo("existing more"));
    }

    // ── Caret offset round-trip ─────────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void CaretOffset_SetThenGet_RoundTrips()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("hello world");
        rtb.SetCaretOffset(5);
        Assert.That(rtb.GetCaretOffset(), Is.EqualTo(5));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void CaretOffset_SetToEndOfText_RoundTrips()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("hello world");
        rtb.SetCaretOffset(rtb.GetTextLength());
        Assert.That(rtb.GetCaretOffset(), Is.EqualTo("hello world".Length));
    }

    // ── Selection round-trip (used by context menu, Smooth Dictation, case-cycle) ──

    [Test, Apartment(ApartmentState.STA)]
    public void SelectRange_ThenGetSelectedText_ReturnsExpectedSubstring()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("hello world");
        rtb.SelectRange(6, 5); // "world"
        Assert.That(rtb.GetSelectedText(), Is.EqualTo("world"));
        Assert.That(rtb.GetSelectionStart(), Is.EqualTo(6));
        Assert.That(rtb.GetSelectionLength(), Is.EqualTo(5));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ReplaceSelection_ReplacesOnlySelectedRange()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("hello world");
        rtb.SelectRange(6, 5); // "world"
        rtb.ReplaceSelection("there");
        Assert.That(rtb.GetPlainText(), Is.EqualTo("hello there"));
    }

    // ── GetLineCount (used by the "is multi-line prompt" check) ─────────────────

    [TestCase(false)]
    [TestCase(true)]
    [Apartment(ApartmentState.STA)]
    public void SelectRange_FiveLinesWithFreshLineBreaks_RoundTripsEveryOffset(bool useLineBreaks)
    {
        var rtb = CreateFiveLineDocument(useLineBreaks);
        const string expected = "abc\n\ndef\n\nghi";
        Assert.That(rtb.GetPlainText(), Is.EqualTo(expected));

        for (var offset = 0; offset <= expected.Length; offset++)
        {
            rtb.SelectRange(offset, 0);
            Assert.That(rtb.GetSelectionStart(), Is.EqualTo(offset), $"caret offset {offset}");
            if (offset < expected.Length)
            {
                rtb.SelectRange(offset, 1);
                Assert.That(rtb.GetSelectionStart(), Is.EqualTo(offset), $"selection offset {offset}");
                Assert.That(rtb.GetSelectionLength(), Is.EqualTo(1), $"selection length at {offset}");
                Assert.That(rtb.GetSelectedText(), Is.EqualTo(expected.Substring(offset, 1)));
            }
        }
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DictationInsertion_AfterFreshLineBreak_ReplacesMiddleCharacterAndRestoresUndoSelection()
    {
        var rtb = CreateFiveLineDocument(useLineBreaks: true);
        rtb.IsUndoEnabled = false;
        var history = new PromptUndoHistory(rtb);
        history.CaptureBaseline();
        rtb.SelectionChanged += (_, _) => history.RefreshSelection();
        rtb.TextChanged += (_, _) => history.PushSnapshot();
        var lastRun = (Run)((Paragraph)rtb.Document.Blocks.LastBlock).Inlines.LastInline;
        // Select via native pointers, as the mouse does, independently of the offset resolver.
        rtb.Selection.Select(lastRun.ContentStart.GetPositionAtOffset(1)!,
            lastRun.ContentStart.GetPositionAtOffset(2)!);
        Assert.That(rtb.GetSelectedText(), Is.EqualTo("h"));
        var start = rtb.GetSelectionStart();
        var plan = VoiceInsertionHeuristics.PlanDictationInsertion(
            rtb.GetPlainText(), start, rtb.GetSelectionLength(), "replace", NoRules);
        Assert.That(plan.CaretIndex, Is.EqualTo(11));
        Assert.That(plan.SelectionLength, Is.EqualTo(1));
        Assert.That(plan.RightContext, Is.EqualTo("i"));

        ApplyPlanToRichTextBox(rtb, plan);

        Assert.That(rtb.GetPlainText(), Is.EqualTo(plan.TextAfterInsert));
        Assert.That(rtb.GetPlainText(), Does.EndWith(plan.InsertedText + "i"));
        Assert.That(rtb.GetCaretOffset(), Is.EqualTo(plan.CaretIndexAfterInsert));
        Assert.That(history.Undo(), Is.True);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("abc\n\ndef\n\nghi"));
        Assert.That(rtb.GetSelectionStart(), Is.EqualTo(start));
        Assert.That(rtb.GetSelectedText(), Is.EqualTo("h"));
        Assert.That(rtb.GetCaretOffset(), Is.EqualTo(start + 1));
        Assert.That(history.Redo(), Is.True);
        Assert.That(rtb.GetPlainText(), Is.EqualTo(plan.TextAfterInsert));
        Assert.That(rtb.GetCaretOffset(), Is.EqualTo(plan.CaretIndexAfterInsert));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void RebaseIfCurrentTextDiffers_LoadedDraftCannotUndoToPreLoadEmptyState()
    {
        var rtb = new RichTextBox();
        var history = new PromptUndoHistory(rtb);
        history.CaptureBaseline();

        // Startup restores the persisted draft under undo-capture suppression, so the timeline is
        // still baselined against the previously empty box until the load path synchronizes it.
        var loadedDraft = string.Join('\n', Enumerable.Repeat("large persisted prompt", 800));
        rtb.SetPlainText(loadedDraft);
        Assert.That(history.RebaseIfCurrentTextDiffers(), Is.True);

        rtb.SetPlainText(loadedDraft + "\n");
        history.PushSnapshot();

        Assert.That(history.Undo(), Is.True);
        Assert.That(rtb.GetPlainText(), Is.EqualTo(loadedDraft));
        Assert.That(history.CanUndo, Is.False, "the stale empty pre-load state must not survive");
    }

    [Test, Apartment(ApartmentState.STA)]
    public void RebaseIfCurrentTextDiffers_MatchingTabPreservesRedoChain()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before");
        var history = new PromptUndoHistory(rtb);
        history.CaptureBaseline();
        rtb.SetPlainText("after");
        history.PushSnapshot();
        Assert.That(history.Undo(), Is.True);
        Assert.That(history.CanRedo, Is.True);

        // Loading the same tab text must not discard its preserved history.
        rtb.SetPlainText("before");
        Assert.That(history.RebaseIfCurrentTextDiffers(), Is.False);
        Assert.That(history.CanRedo, Is.True);
        Assert.That(history.Redo(), Is.True);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("after"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void SelectRange_LineBreakBeforeLaterParagraph_PreservesSingleCharacterSelection()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("abc\n\ndef\n\nghi");
        var blank = (Paragraph)rtb.Document.Blocks.FirstBlock.NextBlock!;
        rtb.Document.Blocks.Remove(blank);
        ((Paragraph)rtb.Document.Blocks.FirstBlock).Inlines.Add(new LineBreak());
        Assert.That(rtb.GetPlainText(), Is.EqualTo("abc\n\ndef\n\nghi"));
        rtb.SelectRange(11, 1);
        Assert.That(rtb.GetSelectionStart(), Is.EqualTo(11));
        Assert.That(rtb.GetSelectionLength(), Is.EqualTo(1));
        Assert.That(rtb.GetSelectedText(), Is.EqualTo("h"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void SelectRange_IgnoredInlineAndParagraphEdges_UseSerializedOffsets()
    {
        var rtb = new RichTextBox();
        var paragraph = new Paragraph(new Run("ab"));
        paragraph.Inlines.Add(new InlineUIContainer(new TextBlock { Text = "indicator" }));
        var lastRun = new Run("cd");
        paragraph.Inlines.Add(lastRun);
        rtb.Document.Blocks.Clear();
        rtb.Document.Blocks.Add(paragraph);

        Assert.That(rtb.GetPlainText(), Is.EqualTo("abcd"));
        Assert.That(rtb.GetCharOffset(paragraph.ElementStart), Is.Zero);
        Assert.That(rtb.GetCharOffset(paragraph.ElementEnd), Is.EqualTo(4));
        rtb.Selection.Select(lastRun.ContentStart, lastRun.ContentStart.GetPositionAtOffset(1)!);
        Assert.That(rtb.GetSelectionStart(), Is.EqualTo(2));
        Assert.That(rtb.GetSelectionLength(), Is.EqualTo(1));
        rtb.SelectRange(2, 1);
        Assert.That(rtb.GetSelectionStart(), Is.EqualTo(2));
        Assert.That(rtb.GetSelectionLength(), Is.EqualTo(1));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DictationInsertion_AboveEmbeddedRegionAfterLineBreaks_StaysAtHostCaret()
    {
        var rtb = new RichTextBox { IsUndoEnabled = false };
        rtb.SetPlainText("before\n```text\ncode\n```\nafter");
        EmbeddedCodeRegionManager.ConvertAllFences(rtb);
        try
        {
            var paragraph = (Paragraph)rtb.Document.Blocks.FirstBlock;
            paragraph.Inlines.Clear();
            paragraph.Inlines.Add(new Run("abc"));
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());
            var lastRun = new Run("ghi");
            paragraph.Inlines.Add(lastRun);
            rtb.CaretPosition = lastRun.ContentStart.GetPositionAtOffset(1)!;
            var original = rtb.GetPlainText();
            var plan = VoiceInsertionHeuristics.PlanDictationInsertion(
                original, rtb.GetCaretOffset(), 0, "replace", NoRules);

            rtb.SelectRange(plan.CaretIndex, 0);
            Assert.That(rtb.CaretPosition.Paragraph, Is.SameAs(paragraph));
            Assert.That(rtb.GetCaretOffset(), Is.EqualTo(plan.CaretIndex));
            ApplyPlanToRichTextBox(rtb, plan);

            Assert.That(rtb.GetPlainText(), Is.EqualTo(plan.TextAfterInsert));
            Assert.That(rtb.CaretPosition.Paragraph, Is.SameAs(paragraph));
            Assert.That(rtb.GetPlainText(), Does.EndWith("```text\ncode\n```\nafter"));
        }
        finally
        {
            rtb.DisposeEmbeddedRegions();
        }
    }

    private static RichTextBox CreateFiveLineDocument(bool useLineBreaks)
    {
        var rtb = new RichTextBox();
        rtb.Document.Blocks.Clear();
        if (useLineBreaks)
        {
            var paragraph = new Paragraph(new Run("abc"));
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new Run("def"));
            paragraph.Inlines.Add(new LineBreak());
            var editedBreak = new LineBreak();
            paragraph.Inlines.Add(editedBreak);
            var lastRun = new Run("ghi");
            paragraph.Inlines.Add(lastRun);
            rtb.Document.Blocks.Add(paragraph);
            paragraph.Inlines.Remove(editedBreak);
            paragraph.Inlines.InsertBefore(lastRun, new LineBreak());
        }
        else
        {
            // Truly empty Paragraphs have no Run to provide an offset landing point.
            foreach (var line in new[] { "abc", "", "def", "", "ghi" })
                rtb.Document.Blocks.Add(line.Length == 0 ? new Paragraph() : new Paragraph(new Run(line)));
        }
        return rtb;
    }

    [Test, Apartment(ApartmentState.STA)]
    public void GetLineCount_SingleLine_ReturnsOne()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("single line");
        Assert.That(rtb.GetLineCount(), Is.EqualTo(1));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void GetLineCount_ThreeLines_ReturnsThree()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("line1\nline2\nline3");
        Assert.That(rtb.GetLineCount(), Is.EqualTo(3));
    }

    // ── Plain-text-only paste enforcement (no rich-formatting creep) ────────────
    // Covers the decision logic behind DataObject.AddPastingHandler(PromptTextBox, ...),
    // extracted into RichTextBoxExtensions.ResolvePlainTextPasteFormat so it is testable
    // without a live OS clipboard. This is the same guarantee the precedent decision
    // ("Convert MarkdownDocumentWindow to RichTextBox", commit e35c9b5) established for
    // MarkdownDocumentTabState.EditorTextBox.

    [Test]
    public void ResolvePlainTextPasteFormat_AlreadyUnicodeText_PassesThrough()
    {
        var result = RichTextBoxExtensions.ResolvePlainTextPasteFormat(
            DataFormats.UnicodeText, clipboardContainsText: true);
        Assert.That(result, Is.EqualTo(DataFormats.UnicodeText));
    }

    [Test]
    public void ResolvePlainTextPasteFormat_AlreadyPlainText_PassesThrough()
    {
        var result = RichTextBoxExtensions.ResolvePlainTextPasteFormat(
            DataFormats.Text, clipboardContainsText: true);
        Assert.That(result, Is.EqualTo(DataFormats.Text));
    }

    [Test]
    public void ResolvePlainTextPasteFormat_RtfWithClipboardText_DowngradesToUnicodeText()
    {
        // Simulates pasting rich/formatted content (e.g. from Word, a styled web page,
        // or another RichTextBox) — must never let DataFormats.Rtf/Xaml through.
        var result = RichTextBoxExtensions.ResolvePlainTextPasteFormat(
            DataFormats.Rtf, clipboardContainsText: true);
        Assert.That(result, Is.EqualTo(DataFormats.UnicodeText));
    }

    [Test]
    public void ResolvePlainTextPasteFormat_XamlWithClipboardText_DowngradesToUnicodeText()
    {
        var result = RichTextBoxExtensions.ResolvePlainTextPasteFormat(
            DataFormats.Xaml, clipboardContainsText: true);
        Assert.That(result, Is.EqualTo(DataFormats.UnicodeText));
    }

    [Test]
    public void ResolvePlainTextPasteFormat_RichFormatWithNoClipboardText_CancelsPaste()
    {
        // No plain-text fallback is available on the clipboard — the paste must be
        // cancelled entirely rather than letting the rich format through.
        var result = RichTextBoxExtensions.ResolvePlainTextPasteFormat(
            DataFormats.Rtf, clipboardContainsText: false);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ResolvePlainTextPasteFormat_FileDropWithNoClipboardText_CancelsPaste()
    {
        var result = RichTextBoxExtensions.ResolvePlainTextPasteFormat(
            DataFormats.FileDrop, clipboardContainsText: false);
        Assert.That(result, Is.Null);
    }

    // ── Voice dictation insertion path (AppendSpeechToRichTextBox contract) ─────
    // PromptTextBox now routes push-to-talk dictation through the same
    // RichTextBoxExtensions surface used for DocSourceTextBox, so inserted speech text
    // must land as plain text only, with no rich formatting, at the expected offset.
    //
    // MainWindow.AppendSpeechToRichTextBox delegates ALL of its insertion planning —
    // the leading-space prefix, the quote-after-space suppression heuristic, and the
    // selection-replace / replacement-rules second pass — to
    // VoiceInsertionHeuristics.PlanDictationInsertion. These tests call that real
    // production method directly (not a reimplementation), then apply its plan to a
    // RichTextBox via the same RichTextBoxExtensions.SelectRange/ReplaceSelection calls
    // AppendSpeechToRichTextBox uses, to additionally confirm no rich formatting leaks in.

    private static readonly VoiceReplacementRule[] NoRules = Array.Empty<VoiceReplacementRule>();

    [Test, Apartment(ApartmentState.STA)]
    public void DictationInsertion_AtCaretOffset_InsertsPlainTextOnly()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("hello world");
        rtb.SetCaretOffset(5); // between "hello" and " world"

        var current = rtb.GetPlainText();
        var caretIndex = rtb.GetCaretOffset();

        // Real production planning method — not a reimplementation.
        var plan = VoiceInsertionHeuristics.PlanDictationInsertion(
            current, caretIndex, selectionLength: 0, dictatedText: "dictated", NoRules);

        ApplyPlanToRichTextBox(rtb, plan);

        Assert.That(rtb.GetPlainText(), Is.EqualTo("hello dictated world"));
        Assert.That(rtb.GetCaretOffset(), Is.EqualTo(plan.CaretIndexAfterInsert));

        // Confirm no rich formatting was introduced — every block is a plain Paragraph
        // containing only Run/LineBreak inlines (no InlineUIContainer, no styled Span).
        foreach (var block in rtb.Document.Blocks)
        {
            Assert.That(block, Is.TypeOf<Paragraph>());
            var para = (Paragraph)block;
            foreach (var inline in para.Inlines)
                Assert.That(inline, Is.TypeOf<Run>().Or.TypeOf<LineBreak>());
        }
    }

    [Test]
    public void PlanDictationInsertion_AfterWordChar_PrependsLeadingSpace()
    {
        var plan = VoiceInsertionHeuristics.PlanDictationInsertion(
            "hello", caretIndex: 5, selectionLength: 0, dictatedText: "world", NoRules);

        Assert.That(plan.InsertedText, Does.StartWith(" "));
        Assert.That(plan.TextAfterInsert, Is.EqualTo("hello world"));
    }

    [Test]
    public void PlanDictationInsertion_AfterQuoteFollowingSpace_SuppressesLeadingSpace()
    {
        // Caret sits right after `like "` — the quote-after-space heuristic in the real
        // production method must suppress the normally-inserted leading space, otherwise
        // dictation would land as `like " word` (stray space inside the quote).
        var current = "say like \"";
        var plan = VoiceInsertionHeuristics.PlanDictationInsertion(
            current, caretIndex: current.Length, selectionLength: 0, dictatedText: "hello", NoRules);

        Assert.That(plan.InsertedText, Does.Not.StartWith(" "));
        Assert.That(plan.TextAfterInsert, Is.EqualTo("say like \"hello"));
    }

    [Test]
    public void PlanDictationInsertion_QuoteNotPrecededBySpace_StillInsertsLeadingSpace()
    {
        // Quote-suppression only applies when the quote itself follows a space
        // (e.g. mid-word quotes like `so"metimes"` must not be affected).
        var current = "end\"";
        var plan = VoiceInsertionHeuristics.PlanDictationInsertion(
            current, caretIndex: current.Length, selectionLength: 0, dictatedText: "more", NoRules);

        Assert.That(plan.InsertedText, Does.StartWith(" "));
    }

    [Test]
    public void PlanDictationInsertion_WithActiveSelection_ReplacesSelectedRange()
    {
        // When PTT started with a selection active, the first dictation insert must
        // replace that selected range rather than inserting at the caret.
        var current = "hello brave world";
        var plan = VoiceInsertionHeuristics.PlanDictationInsertion(
            current, caretIndex: 6, selectionLength: 5, dictatedText: "new", NoRules);

        Assert.That(plan.LeftContext, Is.EqualTo("hello "));
        Assert.That(plan.RightContext, Is.EqualTo(" world"));
        Assert.That(plan.TextAfterInsert, Is.EqualTo("hello new world"));
    }

    [Test]
    public void PlanDictationInsertion_WithReplacementRules_AppliesSecondUndoStep()
    {
        var rules = new[] { new VoiceReplacementRule(@"\bgonna\b", "going to") };
        var plan = VoiceInsertionHeuristics.PlanDictationInsertion(
            "I'm ", caretIndex: 4, selectionLength: 0, dictatedText: "gonna go", rules);

        Assert.That(plan.HasReplacementStep, Is.True);
        Assert.That(plan.ReplacedText, Does.Contain("going to"));
        Assert.That(plan.TextAfterReplace, Is.EqualTo("I'm going to go"));
    }

    [Test]
    public void PlanDictationInsertion_NoReplacementRules_SkipsSecondUndoStep()
    {
        var plan = VoiceInsertionHeuristics.PlanDictationInsertion(
            "hi ", caretIndex: 3, selectionLength: 0, dictatedText: "there", NoRules);

        Assert.That(plan.HasReplacementStep, Is.False);
        Assert.That(plan.ReplacedText, Is.EqualTo(plan.InsertedText));
    }

    /// <summary>
    /// Applies a <see cref="DictationInsertionPlan"/> to a RichTextBox using the exact
    /// two-step SelectRange/ReplaceSelection sequence MainWindow.AppendSpeechToRichTextBox
    /// performs in production.
    /// </summary>
    private static void ApplyPlanToRichTextBox(RichTextBox rtb, DictationInsertionPlan plan)
    {
        rtb.SelectRange(plan.CaretIndex, plan.SelectionLength);
        rtb.ReplaceSelection(plan.InsertedText);
        rtb.SelectRange(plan.CaretIndexAfterInsert, 0);

        if (plan.HasReplacementStep)
        {
            rtb.SelectRange(plan.CaretIndex, plan.InsertedText.Length);
            rtb.ReplaceSelection(plan.ReplacedText);
            rtb.SelectRange(plan.CaretIndexAfterReplace, 0);
        }
    }
}
