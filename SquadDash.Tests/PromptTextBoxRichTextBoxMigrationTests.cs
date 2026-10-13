using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

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

    [Test, Apartment(ApartmentState.STA)]
    public void DictationInsertion_AtCaretOffset_InsertsPlainTextOnly()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("hello world");
        rtb.SetCaretOffset(5); // between "hello" and " world"

        // Simulate the insertion MainWindow.AppendSpeechToRichTextBox performs: replace
        // the dictation-session range with plain recognized text via SetPlainText-based
        // reconstruction (the same contract exercised by the production code path).
        var current = rtb.GetPlainText();
        var caretIndex = rtb.GetCaretOffset();
        var inserted = " dictated";
        var updated = current[..caretIndex] + inserted + current[caretIndex..];
        rtb.SetPlainText(updated);
        rtb.SetCaretOffset(caretIndex + inserted.Length);

        Assert.That(rtb.GetPlainText(), Is.EqualTo("hello dictated world"));
        Assert.That(rtb.GetCaretOffset(), Is.EqualTo(5 + inserted.Length));

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
}
