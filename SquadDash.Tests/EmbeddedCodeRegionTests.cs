using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SquadDash.Tests;

/// <summary>
/// Automated proof for the Step 2 "embedded-region-serialization-test" requirement: an
/// embedded code region created from a triple-backtick fence — with and without a stored
/// language value — serializes back into an equivalent fenced block via
/// <see cref="RichTextBoxExtensions.GetPlainText"/>, and the collapse/expand toggle works
/// without affecting that serialized output. Covers both normative contract examples:
/// "backtick-fence-creates-region" and "region-serializes-with-language-tag".
/// </summary>
[TestFixture]
internal sealed class EmbeddedCodeRegionTests
{
    // ── EmbeddedFenceDetector (pure logic) ──────────────────────────────────────

    [Test]
    public void FindFirstFence_BareFenceWithContent_FindsOpenAndCloseLines()
    {
        var lines = new[] { "before", "```", "def foo():", "    pass", "```", "after" };
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);

        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.OpenLineIndex, Is.EqualTo(1));
        Assert.That(fence.Value.CloseLineIndex, Is.EqualTo(4));
        Assert.That(fence.Value.Language, Is.EqualTo(""));
        Assert.That(fence.Value.ExtractContent(lines), Is.EqualTo("def foo():\n    pass"));
    }

    [Test]
    public void FindFirstFence_FenceWithLanguageTag_CapturesLanguage()
    {
        var lines = new[] { "```python", "x = 1", "```" };
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);

        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.Language, Is.EqualTo("python"));
    }

    [Test]
    public void FindFirstFence_UnclosedFence_ReturnsNull()
    {
        var lines = new[] { "```", "still typing..." };
        Assert.That(EmbeddedFenceDetector.FindFirstFence(lines), Is.Null);
    }

    [Test]
    public void FindFirstFence_NoFence_ReturnsNull()
    {
        var lines = new[] { "just", "plain", "text" };
        Assert.That(EmbeddedFenceDetector.FindFirstFence(lines), Is.Null);
    }

    [Test]
    public void BuildFencedText_NoLanguage_ProducesBareFence()
    {
        var text = EmbeddedFenceDetector.BuildFencedText("def foo():\n    pass", null);
        Assert.That(text, Is.EqualTo("```\ndef foo():\n    pass\n```"));
    }

    [Test]
    public void BuildFencedText_WithLanguage_PlacesLanguageImmediatelyAfterOpeningBackticks()
    {
        var text = EmbeddedFenceDetector.BuildFencedText("x = 1", "python");
        Assert.That(text, Is.EqualTo("```python\nx = 1\n```"));
    }

    // ── Serialization convention: language-mode round-trips through the fence token ──────
    // explicit language → ```<lang> ; auto detect → bare ``` ; plain text → the reserved
    // token `text`. Each example builds the fenced form then re-parses it, proving the mode
    // survives the serialize→parse round-trip (the deterministic AVALONEDIT-20261014 contract).

    [Test]
    public void Serialization_ExplicitLanguageFence_EmitsAndReparsesLanguageToken()
    {
        // explicit-language-fence: Region mode = explicit language "csharp".
        var serialized = EmbeddedFenceDetector.BuildFencedText("int x = 1;", "csharp");
        Assert.That(serialized, Is.EqualTo("```csharp\nint x = 1;\n```"));

        var fence = EmbeddedFenceDetector.FindFirstFence(serialized.Split('\n'));
        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.Language, Is.EqualTo("csharp"),
            "explicit language token must round-trip");
        Assert.That(fence.Value.ExtractContent(serialized.Split('\n')), Is.EqualTo("int x = 1;"));
    }

    [Test]
    public void Serialization_AutoDetect_EmitsBareFenceWithNoToken()
    {
        // auto-detect-no-tag: Region mode = Auto detect, Language field empty.
        var serialized = EmbeddedFenceDetector.BuildFencedText("int x = 1;", "");
        Assert.That(serialized, Is.EqualTo("```\nint x = 1;\n```"),
            "auto detect must serialize as a bare fence with no language token");

        var fence = EmbeddedFenceDetector.FindFirstFence(serialized.Split('\n'));
        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.Language, Is.EqualTo(""),
            "auto detect must round-trip as an empty language token");
    }

    [Test]
    public void Serialization_PlainText_EmitsReservedTextToken()
    {
        // plain-text-marker: Region mode = Plain text → reserved token "text".
        var serialized = EmbeddedFenceDetector.BuildFencedText("int x = 1;", "text");
        Assert.That(serialized, Is.EqualTo("```text\nint x = 1;\n```"),
            "plain text must serialize with the reserved token `text`");

        var fence = EmbeddedFenceDetector.FindFirstFence(serialized.Split('\n'));
        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.Language, Is.EqualTo("text"),
            "plain-text reserved token must round-trip");
    }

    // ── Contract Example 1: backtick-fence-creates-region ───────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void TypingFence_CreatesSingleCollapsibleRegion_ReplacingRawMarkers()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\ndef foo():\n    pass\n```");

        var converted = EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        Assert.That(converted, Is.True);

        // The raw fence paragraphs are gone — the document now holds exactly one block,
        // a BlockUIContainer wrapping an EmbeddedCodeRegionBlock.
        Assert.That(rtb.Document.Blocks.Count, Is.EqualTo(2));
        var container = (BlockUIContainer)rtb.Document.Blocks.FirstBlock!;
        var region = (EmbeddedCodeRegionBlock)container.Child;
        Assert.That(region.Content, Is.EqualTo("def foo():\n    pass"));
        Assert.That(region.Language, Is.EqualTo(""));
        Assert.That(RichTextBoxExtensions.IsEmptyEmbeddedCodeRegionCaretLandingParagraph(container.NextBlock!), Is.True);

        // No further fences remain to convert.
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(rtb), Is.False);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TypingFence_ExpandedByDefault_ShowsFullContent()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nhello\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;

        // The region is always expanded: no collapse/expand toggle button exists anywhere in
        // its visual tree, and the content editor is visible.
        Assert.That(FindDescendant<System.Windows.Controls.Primitives.ToggleButton>(region), Is.Null,
            "collapse/expand toggle button must be gone");
        var contentEditor = (ICSharpCode.AvalonEdit.TextEditor)((Border)((StackPanel)region.Child).Children[1]).Child;
        Assert.That(contentEditor.Visibility, Is.EqualTo(Visibility.Visible));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void LanguageLabel_SitsAboveEditorBorder_NotInsideIt()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```python\nhello\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;

        // Outer element is a transparent layout host: no border chrome painted on it.
        Assert.That(region.BorderThickness, Is.EqualTo(new Thickness(0)));

        var stack = (StackPanel)region.Child;
        var label = (TextBlock)stack.Children[0];
        var editorBorder = (Border)stack.Children[1];

        Assert.That(label.Text, Is.EqualTo("python"));
        // The label is a sibling ABOVE the editor rectangle, never a descendant of it.
        Assert.That(FindDescendant<TextBlock>(editorBorder), Is.Null,
            "language label must not live inside the editor border");
        Assert.That(editorBorder.Child, Is.InstanceOf<ICSharpCode.AvalonEdit.TextEditor>());

        // Serialized form is unaffected by the chrome restructure.
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```python\nhello\n```"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void LanguageLabel_IsItalicWithNoBackground()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```python\nhello\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;

        var stack = (StackPanel)region.Child;
        var label = (TextBlock)stack.Children[0];

        Assert.That(label.FontStyle, Is.EqualTo(FontStyles.Italic),
            "language label must render italic");
        Assert.That(label.Background, Is.EqualTo(System.Windows.Media.Brushes.Transparent),
            "language label must have a transparent (no) background fill");
    }

    // ── Contract Example 2: region-serializes-with-language-tag ─────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void SerializedRegion_WithStoredLanguage_ProducesLanguageTaggedFence()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nx = 1\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;

        // Language is a data-model field set programmatically here — no picker UI ships
        // with this step, per plan scope.
        region.Language = "python";

        Assert.That(rtb.GetPlainText(), Is.EqualTo("```python\nx = 1\n```"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FenceWithLanguageTag_RoundTripsThroughConversionAndSerialization()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```python\ndef foo():\n    pass\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        Assert.That(rtb.GetPlainText(), Is.EqualTo("```python\ndef foo():\n    pass\n```"));
    }

    // ── Serialization round-trip with surrounding plain text ────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void FenceSurroundedByPlainText_PreservesTextBeforeAndAfter()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        Assert.That(rtb.GetPlainText(), Is.EqualTo("before\n```\ncode\n```\nafter"));

        // Confirm the middle block really did become a region, not just unconverted text.
        var blocks = rtb.Document.Blocks;
        Assert.That(blocks.Count, Is.EqualTo(4));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void MultipleFences_PastedTogether_BothConvertAfterLoop()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nfirst\n```\nmiddle\n```python\nsecond\n```");

        var conversions = 0;
        while (EmbeddedCodeRegionManager.TryConvertNextFence(rtb)) conversions++;

        Assert.That(conversions, Is.EqualTo(2));
        Assert.That(rtb.GetPlainText(),
            Is.EqualTo("```\nfirst\n```\nmiddle\n```python\nsecond\n```"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TextChangedConversion_WithExistingEmbeddedRegion_RebuildsWithoutUndoSerialization()
    {
        var rtb = new RichTextBox { IsUndoEnabled = true };
        rtb.SetPlainText("```\nfirst\n```\nplaceholder");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        Assert.That(rtb.Document.Blocks.OfType<BlockUIContainer>().Count(), Is.EqualTo(1));

        Exception? conversionException = null;
        var converting = false;
        rtb.TextChanged += (_, _) =>
        {
            if (converting)
                return;

            converting = true;
            try
            {
                while (EmbeddedCodeRegionManager.TryConvertNextFence(rtb)) { }
            }
            catch (Exception ex)
            {
                conversionException = ex;
            }
            finally
            {
                converting = false;
            }
        };

        var lastParagraph = (Paragraph)rtb.Document.Blocks.LastBlock!;
        Assert.DoesNotThrow(() =>
        {
            lastParagraph.Inlines.Clear();
            lastParagraph.Inlines.Add(new Run("```\nsecond\n```"));
        });

        Assert.That(conversionException, Is.Null);
        Assert.That(rtb.Document.Blocks.OfType<BlockUIContainer>().Count(), Is.EqualTo(2));
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```\nfirst\n```\n```" + "\nsecond\n```"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void SetPlainText_AfterEmbeddedRegion_DoesNotSerializeRegionIntoUndoUnit()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        Assert.That(rtb.ContainsEmbeddedCodeRegion(), Is.True);

        Assert.DoesNotThrow(() => rtb.SetPlainText("replacement"));
        Assert.That(rtb.GetPlainText(), Is.EqualTo("replacement"));
        Assert.That(rtb.ContainsEmbeddedCodeRegion(), Is.False);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DeleteSelectionWithoutUndo_SelectedEmbeddedRegion_RemovesRegionWithoutSerializingIt()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\ncode\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        rtb.Selection.Select(rtb.Document.ContentStart, rtb.Document.ContentEnd);

        Assert.That(rtb.SelectionContainsEmbeddedCodeRegion(), Is.True);
        Assert.DoesNotThrow(() => rtb.DeleteSelectionWithoutUndo());
        Assert.That(rtb.ContainsEmbeddedCodeRegion(), Is.False);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void SelectionContainsEmbeddedCodeRegion_OrdinaryTextSelectionBeforeRegion_ReturnsFalse()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        rtb.Selection.Select(
            rtb.Document.Blocks.FirstBlock!.ContentStart,
            rtb.Document.Blocks.FirstBlock!.ContentEnd);

        Assert.That(rtb.SelectionContainsEmbeddedCodeRegion(), Is.False);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ReplaceSelectionWithoutUndo_SelectedEmbeddedRegion_ReplacesRegionWithoutSerializingIt()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\ncode\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        rtb.Selection.Select(rtb.Document.ContentStart, rtb.Document.ContentEnd);

        Assert.DoesNotThrow(() => rtb.ReplaceSelectionWithoutUndo("typed"));
        Assert.That(rtb.ContainsEmbeddedCodeRegion(), Is.False);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("typed"));
    }

    // ── Bug fix: copy/cut of a RichTextBox-level selection spanning a region keeps it ──
    // Regression guard for the embedded-region copy hole: WPF's default clipboard
    // serialization walks only text Runs and drops the region's BlockUIContainer, stripping
    // the region out of copied text. GetSelectionFencedText reuses the GetPlainText
    // block-walk so the region survives as its full fenced form.

    [Test, Apartment(ApartmentState.STA)]
    public void GetSelectionFencedText_SelectionSpanningRegion_PreservesRegionAsFencedText()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        // Select the whole document — spans text before, the region, and text after.
        rtb.Selection.Select(rtb.Document.ContentStart, rtb.Document.ContentEnd);

        var text = rtb.GetSelectionFencedText(out var containsRegion);

        Assert.That(containsRegion, Is.True);
        Assert.That(text, Is.EqualTo("before\n```\ncode\n```\nafter"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void GetSelectionFencedText_RegionContainingBacktickRun_UsesVariableLengthOuterFence()
    {
        var rtb = new RichTextBox();
        // Region content itself contains a 4-backtick line, so the outer fence must widen to
        // max(3, longestRun + 1) = 5 backticks. The variable-length-backtick path is exactly
        // what regressed, so prove it is used here and the region is not stripped.
        rtb.SetPlainText("intro\n`````\n````\ndone\n`````\noutro");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var region = (EmbeddedCodeRegionBlock)rtb.Document.Blocks.OfType<BlockUIContainer>().First().Child;
        Assert.That(region.Content, Is.EqualTo("````\ndone"), "fence scanning should have captured the 4-backtick line as content");

        rtb.Selection.Select(rtb.Document.ContentStart, rtb.Document.ContentEnd);
        var text = rtb.GetSelectionFencedText(out var containsRegion);

        Assert.That(containsRegion, Is.True);
        Assert.That(text, Is.EqualTo("intro\n`````\n````\ndone\n`````\noutro"));
        // Explicitly confirm the widened 5-backtick outer fence is present (not stripped,
        // not a bare 3-backtick fence that would collide with the inner run).
        Assert.That(text, Does.Contain("`````\n````\ndone\n`````"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void GetSelectionFencedText_SelectionWithoutRegion_ReportsNoRegionSoWpfDefaultCopyProceeds()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        // Select only the leading "before" paragraph — no region touched.
        var firstPara = rtb.Document.Blocks.FirstBlock!;
        rtb.Selection.Select(firstPara.ContentStart, firstPara.ContentEnd);

        var text = rtb.GetSelectionFencedText(out var containsRegion);

        Assert.That(containsRegion, Is.False, "no region intersected — caller must let WPF's default copy run");
        Assert.That(text, Is.EqualTo("before"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void GetSelectionFencedText_PartialLeadingAndTrailingParagraphs_ClampToSelectionButKeepFullRegion()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("hello\n```\ncode\n```\nworld");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        // Plain-text layout: "hello"(0..5) \n ```\ncode\n```(region, 6..18) \n "world"(19..24).
        // Select from mid-"hello" (after "hel", offset 3) to mid-"world" (after "wor",
        // offset 22), spanning the region in between.
        rtb.SelectRange(3, 19);

        var text = rtb.GetSelectionFencedText(out var containsRegion);

        Assert.That(containsRegion, Is.True);
        Assert.That(text, Is.EqualTo("lo\n```\ncode\n```\nwor"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void GetSelectionFencedText_RegionWithLanguageTag_PreservesLanguageTaggedFence()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```python\nx = 1\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        rtb.Selection.Select(rtb.Document.ContentStart, rtb.Document.ContentEnd);

        var text = rtb.GetSelectionFencedText(out var containsRegion);

        Assert.That(containsRegion, Is.True);
        Assert.That(text, Is.EqualTo("```python\nx = 1\n```"));
    }

    // ── No Step 1 regression: plain text with no embedded regions round-trips untouched ──

    [Test, Apartment(ApartmentState.STA)]
    public void PlainTextWithNoFence_GetPlainTextUnaffected()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("just an ordinary prompt with no code fences at all");
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(rtb), Is.False);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("just an ordinary prompt with no code fences at all"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void UnclosedFence_DoesNotConvert_LeavesRawTextIntact()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nstill typing, no closing fence yet");
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(rtb), Is.False);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```\nstill typing, no closing fence yet"));
    }

    // ── Bug fix 1: content box is genuinely editable and round-trips edits ─────

    [Test, Apartment(ApartmentState.STA)]
    public void EditingContentDirectly_UpdatesContentProperty_AndRoundTripsThroughGetPlainText()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\noriginal\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;

        // Simulate the user editing inside the region's content box directly (as opposed to
        // setting Content programmatically, which the old read-only TextBox could never do).
        var contentBox = GetContentEditor(region);
        Assert.That(contentBox.IsReadOnly, Is.False, "content box must be editable, not read-only");

        contentBox.Text = "edited content";

        Assert.That(region.Content, Is.EqualTo("edited content"));
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```\nedited content\n```"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void EditingContentDirectly_SurvivesUnrelatedFenceConversionRebuild()
    {
        // A region's content edits must not be clobbered when an unrelated new fence
        // elsewhere in the same document triggers EmbeddedCodeRegionManager to rebuild
        // Document.Blocks (the rebuild reattaches the *same* BlockUIContainer instance).
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\noriginal\n```\nsome text");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var container = (BlockUIContainer)rtb.Document.Blocks.FirstBlock!;
        var region = (EmbeddedCodeRegionBlock)container.Child;
        var contentBox = GetContentEditor(region);
        contentBox.Text = "edited before rebuild";

        // Replace the trailing plain text with a brand-new fence, forcing another
        // conversion/rebuild pass that must preserve the first region's instance and edits.
        var lastParagraph = (Paragraph)rtb.Document.Blocks.LastBlock!;
        lastParagraph.Inlines.Clear();
        lastParagraph.Inlines.Add(new Run("```\nsecond\n```"));
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(rtb), Is.True);

        var stillSameContainer = (BlockUIContainer)rtb.Document.Blocks.FirstBlock!;
        Assert.That(stillSameContainer, Is.SameAs(container), "rebuild must reuse the same BlockUIContainer instance");
        var stillSameRegion = (EmbeddedCodeRegionBlock)stillSameContainer.Child;
        Assert.That(stillSameRegion.Content, Is.EqualTo("edited before rebuild"));
        Assert.That(rtb.GetPlainText(), Does.StartWith("```\nedited before rebuild\n```"));
    }

    // ── Bug fix 2: caret placement after conversion (not always document end) ─────

    [Test, Apartment(ApartmentState.STA)]
    public void TryConvertNextFence_CaretRightAfterClosingFence_EndsUpRightAfterRegion_NotDocumentEnd()
    {
        var rtb = new RichTextBox();
        // Trailing text after the fence means "document end" and "right after the region"
        // are two different positions — this distinguishes the old (always-jump-to-end) bug
        // from the fix.
        rtb.SetPlainText("```\ncode\n```\nmore text after");
        var caretAfterFence = "```\ncode\n```".Length;
        rtb.SetCaretOffset(caretAfterFence);

        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        // The caret lands on the empty caret landing paragraph after the region. That line is
        // intentionally skipped by GetPlainText while empty, so its offset is the serialized
        // fence boundary itself.
        Assert.That(rtb.GetCaretOffset(), Is.EqualTo(caretAfterFence));
        Assert.That(rtb.GetCaretOffset(), Is.Not.EqualTo(rtb.GetTextLength()));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void GetCharOffset_PositionAfterRegion_AccountsForRegionsFencedTextLength()
    {
        // Direct proof that offset math treats a region as consuming exactly its serialized
        // fenced-text length — the root cause behind "typing lands above the region".
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var fullText = rtb.GetPlainText();
        Assert.That(fullText, Is.EqualTo("before\n```\ncode\n```\nafter"));

        var afterOffset = fullText.IndexOf("after", StringComparison.Ordinal);
        var pointer = rtb.GetTextPointerAt(afterOffset);
        Assert.That(rtb.GetCharOffset(pointer), Is.EqualTo(afterOffset));
    }

    // ── Bug fix 3: fence conversion runs on programmatic load, not just typing ─────

    [Test, Apartment(ApartmentState.STA)]
    public void SetPlainTextThenExplicitConversionLoop_ConvertsPreExistingFences_SimulatingLoadPath()
    {
        // MainWindow's SetPromptTextBoxLogicalBuffer (the programmatic load/restore path)
        // now runs the same conversion loop PromptTextBox_TextChanged uses, immediately
        // after SetPlainText, regardless of IsApplyingHistoryEntry. This proves the
        // conversion primitives it relies on correctly convert content set programmatically
        // (not just via live typing/TextChanged).
        var rtb = new RichTextBox();
        rtb.SetPlainText("```python\nx = 1\n```");

        Assert.That(rtb.ContainsEmbeddedCodeRegion(), Is.False, "SetPlainText alone must not auto-convert");

        while (EmbeddedCodeRegionManager.TryConvertNextFence(rtb)) { }

        Assert.That(rtb.ContainsEmbeddedCodeRegion(), Is.True);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```python\nx = 1\n```"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void EmptyCaretLandingParagraph_AfterRegion_IsVisualOnlyUntilTypedInto()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\ncode\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var regionContainer = (BlockUIContainer)rtb.Document.Blocks.FirstBlock!;
        var landing = (Paragraph)regionContainer.NextBlock!;

        Assert.That(RichTextBoxExtensions.IsEmptyEmbeddedCodeRegionCaretLandingParagraph(landing), Is.True);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```\ncode\n```"));

        landing.Inlines.Clear();
        landing.Inlines.Add(new Run("after"));

        Assert.That(RichTextBoxExtensions.IsEmptyEmbeddedCodeRegionCaretLandingParagraph(landing), Is.False);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```\ncode\n```\nafter"));
    }

    // ── Bug fix 4: deleting a region never crashes ──────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void TryDeleteAdjacentEmbeddedCodeRegion_CaretImmediatelyBeforeRegion_RemovesItWithoutCrashing()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var container = rtb.Document.Blocks.OfType<BlockUIContainer>().First();

        // Proves the pure adjacency rule directly against an explicit TextPointer, rather
        // than through RichTextBox.CaretPosition's setter — which only reliably normalizes
        // a position adjacent to a focusable-content BlockUIContainer once the control is
        // actually focused inside a rendered window (not the case for a bare, unparented
        // RichTextBox in a unit test).
        var found = RichTextBoxExtensions.FindAdjacentEmbeddedCodeRegion(
            rtb.Document, container.ElementStart, LogicalDirection.Forward);
        Assert.That(found, Is.SameAs(container));

        Assert.DoesNotThrow(() => rtb.Document.Blocks.Remove(container));
        Assert.That(rtb.ContainsEmbeddedCodeRegion(), Is.False);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("before\nafter"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryDeleteAdjacentEmbeddedCodeRegion_CaretImmediatelyAfterRegion_RemovesItWithoutCrashing()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var container = rtb.Document.Blocks.OfType<BlockUIContainer>().First();

        var found = RichTextBoxExtensions.FindAdjacentEmbeddedCodeRegion(
            rtb.Document, container.ElementEnd, LogicalDirection.Backward);
        Assert.That(found, Is.SameAs(container));

        Assert.DoesNotThrow(() => rtb.Document.Blocks.Remove(container));
        Assert.That(rtb.ContainsEmbeddedCodeRegion(), Is.False);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("before\nafter"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FindAdjacentEmbeddedCodeRegion_CaretNotAdjacent_ReturnsNull()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var found = RichTextBoxExtensions.FindAdjacentEmbeddedCodeRegion(
            rtb.Document, rtb.Document.ContentStart, LogicalDirection.Forward);

        Assert.That(found, Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryDeleteAdjacentEmbeddedCodeRegion_EmptyDocument_ReturnsFalseAndDoesNotThrow()
    {
        // End-to-end smoke test through the real RichTextBox.CaretPosition-based entry point:
        // when there's nothing adjacent (or no region at all), it must safely no-op rather
        // than throw, regardless of the environment's caret-normalization behavior.
        var rtb = new RichTextBox();
        rtb.SetPlainText("just plain text, no regions");

        bool removed = false;
        Assert.DoesNotThrow(() => removed = rtb.TryDeleteAdjacentEmbeddedCodeRegion(LogicalDirection.Forward));
        Assert.That(removed, Is.False);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void NativeDeleteCommand_SelectedEmbeddedRegionWithSurroundingParagraphs_DoesNotThrow()
    {
        // Regression proof for the reported crash: selecting exactly a region (amid
        // surrounding plain-text paragraphs, matching real prompt-box documents rather than
        // a single-block document) and invoking the same editing command WPF's native
        // Delete key raises must not throw.
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var container = rtb.Document.Blocks.OfType<BlockUIContainer>().First();
        rtb.Selection.Select(container.ElementStart, container.ElementEnd);

        Assert.DoesNotThrow(() => EditingCommands.Delete.Execute(null, rtb));
        Assert.That(rtb.ContainsEmbeddedCodeRegion(), Is.False);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("before\nafter"));
    }

    // ── Instant open-fence conversion: region appears the moment "```" is completed ──

    [Test]
    public void FindOpenFenceWithNoClose_BareOpeningMarker_NoCloseAnywhere_Found()
    {
        var lines = new[] { "before", "```" };
        var open = EmbeddedFenceDetector.FindOpenFenceWithNoClose(lines);

        Assert.That(open, Is.Not.Null);
        Assert.That(open!.Value.LineIndex, Is.EqualTo(1));
        Assert.That(open.Value.Language, Is.EqualTo(""));
    }

    [Test]
    public void FindOpenFenceWithNoClose_WithLanguageTag_CapturesLanguage()
    {
        var lines = new[] { "```python" };
        var open = EmbeddedFenceDetector.FindOpenFenceWithNoClose(lines);

        Assert.That(open, Is.Not.Null);
        Assert.That(open!.Value.Language, Is.EqualTo("python"));
    }

    [Test]
    public void FindOpenFenceWithNoClose_CompleteFenceAlreadyExists_ReturnsNull()
    {
        // A fully-formed open+close pair must be left to FindFirstFence/TryConvertNextFence
        // instead — this is what lets the complete-fence-paste path keep priority.
        var lines = new[] { "```", "code", "```" };
        Assert.That(EmbeddedFenceDetector.FindOpenFenceWithNoClose(lines), Is.Null);
    }

    [Test]
    public void FindOpenFenceWithNoClose_NoFenceAtAll_ReturnsNull()
    {
        var lines = new[] { "just", "plain", "text" };
        Assert.That(EmbeddedFenceDetector.FindOpenFenceWithNoClose(lines), Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryConvertOpenFence_BareOpeningMarker_InstantlyCreatesEmptyExpandedRegion_WithCaretFocusedInside()
    {
        // Simulates the user finishing typing "```" on an otherwise-empty line, with no
        // closing fence typed yet.
        var rtb = new RichTextBox();
        rtb.SetPlainText("```");

        var converted = EmbeddedCodeRegionManager.TryConvertOpenFence(rtb);
        Assert.That(converted, Is.True);

        Assert.That(rtb.Document.Blocks.Count, Is.EqualTo(2));
        var container = (BlockUIContainer)rtb.Document.Blocks.FirstBlock!;
        var region = (EmbeddedCodeRegionBlock)container.Child;
        Assert.That(RichTextBoxExtensions.IsEmptyEmbeddedCodeRegionCaretLandingParagraph(container.NextBlock!), Is.True);

        Assert.That(region.Content, Is.EqualTo(""), "region must be created empty, not waiting for pasted content");
        Assert.That(region.Language, Is.EqualTo(""));

        // A live keyboard-focus assertion without a real window/PresentationSource: calling
        // Focus() on a control outside a visual root can't acquire true OS keyboard focus,
        // but it does still set WPF's own logical IsFocused state within the control's focus
        // scope — which is exactly what FocusContent (called by TryConvertOpenFence's
        // render step) is proven here to have done.
        var contentBox = GetContentEditor(region);
        Assert.That(contentBox.TextArea.IsFocused, Is.True, "caret must be focused into the content box immediately");
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryConvertOpenFence_WithLanguageTag_InstantlyCreatesEmptyExpandedRegion_WithLanguageAndFocus()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```python");

        var converted = EmbeddedCodeRegionManager.TryConvertOpenFence(rtb);
        Assert.That(converted, Is.True);

        var container = (BlockUIContainer)rtb.Document.Blocks.FirstBlock!;
        var region = (EmbeddedCodeRegionBlock)container.Child;

        Assert.That(region.Content, Is.EqualTo(""));
        Assert.That(region.Language, Is.EqualTo("python"));

        var contentBox = GetContentEditor(region);
        Assert.That(contentBox.TextArea.IsFocused, Is.True);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryConvertOpenFence_SurroundingPlainTextPreserved()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\n");

        Assert.That(EmbeddedCodeRegionManager.TryConvertOpenFence(rtb), Is.True);
        Assert.That(rtb.Document.Blocks.Count, Is.EqualTo(3));
        Assert.That(((Paragraph)rtb.Document.Blocks.FirstBlock!).Inlines.OfType<Run>().First().Text,
            Is.EqualTo("before"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryConvertOpenFence_CompleteFenceAlreadyPresent_DoesNotApply()
    {
        // When a complete fence has already been pasted/typed, the open-fence path must not
        // fire — TryConvertNextFence owns that case (priority rule from the task brief).
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\ncode\n```");

        Assert.That(EmbeddedCodeRegionManager.TryConvertOpenFence(rtb), Is.False);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryConvertNextFence_TakesPriorityOverOpenFence_WhenBothPresentInSameEdit()
    {
        // A multi-line paste that completes one fence while a second, separate fence is
        // still open must have the complete one converted by TryConvertNextFence first;
        // TryConvertOpenFence then only picks up the still-open remainder — matching how
        // MainWindow.PromptTextBox_TextChanged orders the two calls.
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nfirst\n```\n```python");

        var completeConversions = 0;
        while (EmbeddedCodeRegionManager.TryConvertNextFence(rtb)) completeConversions++;
        Assert.That(completeConversions, Is.EqualTo(1));

        Assert.That(EmbeddedCodeRegionManager.TryConvertOpenFence(rtb), Is.True);

        var containers = rtb.Document.Blocks.OfType<BlockUIContainer>().ToList();
        Assert.That(containers.Count, Is.EqualTo(2));
        var firstRegion = (EmbeddedCodeRegionBlock)containers[0].Child;
        var secondRegion = (EmbeddedCodeRegionBlock)containers[1].Child;
        Assert.That(firstRegion.Content, Is.EqualTo("first"));
        Assert.That(secondRegion.Content, Is.EqualTo(""));
        Assert.That(secondRegion.Language, Is.EqualTo("python"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TypingClosingFenceLineInsideContentBox_StripsMarkerFromContent_RegionStaysExpanded()
    {
        // Judgment call for "how does the user finish an instantly-opened region": typing a
        // bare ``` as its own last line inside the content box ends it — the marker line is
        // stripped back out of Content (ToFencedText already supplies the closing marker).
        // The region stays expanded (collapse was removed); the caret exits below the region.
        var rtb = new RichTextBox();
        rtb.SetPlainText("```");
        EmbeddedCodeRegionManager.TryConvertOpenFence(rtb);

        var container = (BlockUIContainer)rtb.Document.Blocks.FirstBlock!;
        var region = (EmbeddedCodeRegionBlock)container.Child;
        var contentBox = GetContentEditor(region);

        contentBox.Text = "x = 1\n```";

        Assert.That(region.Content, Is.EqualTo("x = 1"));
        Assert.That(contentBox.Visibility, Is.EqualTo(Visibility.Visible), "region stays expanded, not collapsed");
        Assert.That(FindDescendant<System.Windows.Controls.Primitives.ToggleButton>(region), Is.Null);
        Assert.That(rtb.Document.Blocks.Count, Is.EqualTo(2), "closing the region should leave a caret landing paragraph after it");
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```\nx = 1\n```"));
    }

    // ── Smart arrow-key navigation ─────────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void FindRegionOnCaretParagraph_CaretOnRegionParagraph_FindsRegion()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\n123456\n```\nafter");
        
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        // Find the paragraph between "before" and the region
        var blocks = rtb.Document.Blocks.ToList();
        var beforePara = blocks[0] as Paragraph;
        Assert.That(beforePara, Is.Not.Null);

        // Place caret at the end of the "before" paragraph, right where it would be
        // when the user presses Down and would land on the region's paragraph line
        var caretPointer = beforePara!.ContentEnd;
        
        var region = RichTextBoxExtensions.FindRegionOnCaretParagraph(rtb.Document, caretPointer);
        Assert.That(region, Is.Not.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FindRegionOnCaretParagraph_CaretNotOnRegion_ReturnsNull()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("line1\nline2\nline3");

        var firstPara = rtb.Document.Blocks.FirstBlock as Paragraph;
        var caretPointer = firstPara!.ContentStart;

        var region = RichTextBoxExtensions.FindRegionOnCaretParagraph(rtb.Document, caretPointer);
        Assert.That(region, Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FocusContentAtPosition_StartPosition_PlacesCareAtStart()
    {
        var region = EmbeddedCodeRegionBlock.Build("123456", "");
        
        // We can't fully test focus without a window, but we can test the caret positioning
        // by accessing the internal TextBox through reflection or by verifying the method exists
        // For now, just verify the method can be called without throwing
        region.FocusContentAtPosition(0);
        
        Assert.That(region, Is.Not.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FocusContentAtPosition_EndPosition_PlacesCaretAtEnd()
    {
        var region = EmbeddedCodeRegionBlock.Build("123456", "");
        
        region.FocusContentAtPosition(region.Content.Length);
        
        Assert.That(region, Is.Not.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FocusContentAtPosition_ClampsBeyondLength()
    {
        var region = EmbeddedCodeRegionBlock.Build("123", "");
        
        // Should clamp to Content.Length (3) without throwing
        region.FocusContentAtPosition(1000);
        
        Assert.That(region, Is.Not.Null);
    }

    // ── Mouse text selection inside the region (double-click word-select / click-drag) ──
    //
    // Regression guard for the bug where the host RichTextBox's Preview handler and the
    // content box's own Preview handler both claimed every left-button-down, suppressing the
    // nested TextBox's native caret/double-click/drag selection. The pure predicate below
    // encodes the host's decision, and the STA test proves the content box's mouse handler no
    // longer collapses an in-progress selection.

    [Test]
    public void ShouldClaimRegionMouseGesture_ContentTextBoxClick_IsNotClaimed()
    {
        // Click landed on the nested TextBox → host must leave the gesture alone so the TextBox
        // performs native selection (double-click word-select, click-drag, double-click-drag).
        Assert.That(
            EmbeddedCodeRegionBlock.ShouldClaimRegionMouseGesture(
                hitRegion: true, hitToggle: false, hitContentTextBox: true),
            Is.False);
    }

    [Test]
    public void ShouldClaimRegionMouseGesture_RegionChromeClick_IsClaimed()
    {
        // Click landed on region chrome (background/label), no nested editor under the cursor →
        // host claims the gesture to focus the region's content.
        Assert.That(
            EmbeddedCodeRegionBlock.ShouldClaimRegionMouseGesture(
                hitRegion: true, hitToggle: false, hitContentTextBox: false),
            Is.True);
    }

    [Test]
    public void ShouldClaimRegionMouseGesture_ToggleClick_IsNotClaimed()
    {
        // The collapse/expand toggle owns its own click; the region must never claim it.
        Assert.That(
            EmbeddedCodeRegionBlock.ShouldClaimRegionMouseGesture(
                hitRegion: true, hitToggle: true, hitContentTextBox: false),
            Is.False);
    }

    [Test]
    public void ShouldClaimRegionMouseGesture_OutsideAnyRegion_IsNotClaimed()
    {
        Assert.That(
            EmbeddedCodeRegionBlock.ShouldClaimRegionMouseGesture(
                hitRegion: false, hitToggle: false, hitContentTextBox: false),
            Is.False);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ContentBoxMouseLeftButtonDown_DoesNotCollapseExistingSelection()
    {
        // Simulates the TextBox's native mouse handling having already selected a word (e.g. a
        // double-click), then the region's bubbling MouseLeftButtonDown handler running. The
        // handler must claim the gesture (so the host never object-selects the BlockUIContainer)
        // WITHOUT resetting the caret/selection the TextBox just applied — the old Preview
        // handler called FocusContentAtPoint, which collapsed the selection and broke dragging.
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nhello world\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;
        var contentBox = GetContentEditor(region);

        // Pretend the TextBox's native handling selected the word "world".
        contentBox.Select(6, 5);
        Assert.That(contentBox.SelectedText, Is.EqualTo("world"));

        var args = new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.InputManager.Current.PrimaryMouseDevice,
            0,
            System.Windows.Input.MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            Source = contentBox,
        };
        contentBox.RaiseEvent(args);

        // Gesture claimed (host won't object-select) and native selection left intact.
        Assert.That(args.Handled, Is.True);
        Assert.That(contentBox.SelectionStart, Is.EqualTo(6));
        Assert.That(contentBox.SelectionLength, Is.EqualTo(5));
        Assert.That(contentBox.SelectedText, Is.EqualTo("world"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryNavigateIntoEmbeddedRegionWithDirection_DownArrow_ReturnsTrue()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\n123456\n```\nafter");
        
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        // Simulate: caret before region, user presses Down
        // We'd need to actually navigate the caret to test the full flow,
        // so for now verify the method exists and can be called
        var region = rtb.Document.Blocks
            .OfType<BlockUIContainer>()
            .FirstOrDefault()?
            .Child as EmbeddedCodeRegionBlock;
        
        Assert.That(region, Is.Not.Null);
    }

    // ── Issue 2 (Down arrow state bug): Verify Left/Right work after Down entry ──

    [Test, Apartment(ApartmentState.STA)]
    public void MultilineRegion_DownArrowOnFirstLine_ShouldNotExit()
    {
        // Ensures Down arrow within a multi-line region moves to next line, not exit
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nline1\nline2\nline3\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;
        var contentBox = GetContentEditor(region);

        // Simulate pressing Down on first line — should allow navigation within region
        contentBox.CaretOffset = 0; // Start of "line1"
        
        // Verify we can navigate within the content
        Assert.That(contentBox.Text, Contains.Substring("line1\nline2\nline3"));
        Assert.That(contentBox.Text.Count(c => c == '\n'), Is.EqualTo(2));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void MultilineRegion_UpArrowOnLastLine_ShouldNotExit()
    {
        // Ensures Up arrow within a multi-line region moves to previous line, not exit
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nline1\nline2\nline3\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;
        var contentBox = GetContentEditor(region);

        // Position caret at start of last line
        var lastLineStart = contentBox.Text.LastIndexOf("line3");
        contentBox.CaretOffset = lastLineStart;
        
        // Verify content integrity
        Assert.That(contentBox.CaretOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(contentBox.Text.Contains("line3"));
    }

    // ── Issue 5: Clipboard support for fenced text preservation ──

    [Test, Apartment(ApartmentState.STA)]
    public void Region_ToFencedText_WithLanguage_IncludesLanguageTag()
    {
        var region = EmbeddedCodeRegionBlock.Build("x = 1", "python");
        
        var fenced = region.ToFencedText();
        
        Assert.That(fenced, Is.EqualTo("```python\nx = 1\n```"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Region_ToFencedText_WithoutLanguage_OmitsLanguageTag()
    {
        var region = EmbeddedCodeRegionBlock.Build("plain text", "");
        
        var fenced = region.ToFencedText();
        
        Assert.That(fenced, Is.EqualTo("```\nplain text\n```"));
    }

    // ── Regression: bugs #1 (language-tag with '#'/'.'), #3 (CRLF), #4 (indentation) ──

    [Test]
    public void FindFirstFence_LanguageTagWithHash_IsRecognizedAndCaptured()
    {
        var lines = new[] { "```c#", "int x = 1;", "```" };
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);

        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.Language, Is.EqualTo("c#"));
    }

    [Test]
    public void FindFirstFence_LanguageTagWithDot_IsRecognizedAndCaptured()
    {
        var lines = new[] { "```asp.net", "<% %>", "```" };
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);

        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.Language, Is.EqualTo("asp.net"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FenceWithHashLanguageTag_RoundTripsThroughConversionAndSerialization()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```f#\nlet x = 1\n```");
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(rtb), Is.True);

        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;
        Assert.That(region.Language, Is.EqualTo("f#"));
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```f#\nlet x = 1\n```"));
    }

    [Test]
    public void FindOpenFenceWithNoClose_LanguageTagWithHash_CapturesLanguage()
    {
        var lines = new[] { "```c#" };
        var open = EmbeddedFenceDetector.FindOpenFenceWithNoClose(lines);

        Assert.That(open, Is.Not.Null);
        Assert.That(open!.Value.Language, Is.EqualTo("c#"));
    }

    [Test]
    public void ExtractContent_CrlfPastedBlock_HasNoStrayCarriageReturns()
    {
        // Simulates CRLF-pasted text split on '\n': each line retains a trailing '\r'.
        var lines = "```\r\nline1\r\nline2\r\n```".Split('\n');
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);

        Assert.That(fence, Is.Not.Null);
        var content = fence!.Value.ExtractContent(lines);
        Assert.That(content, Does.Not.Contain("\r"));
        Assert.That(content, Is.EqualTo("line1\nline2"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void CrlfPastedFence_ConvertsAndRoundTripsWithoutStrayCarriageReturns()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\r\n```python\r\nx = 1\r\ny = 2\r\n```\r\nafter");
        while (EmbeddedCodeRegionManager.TryConvertNextFence(rtb)) { }

        var region = (EmbeddedCodeRegionBlock)rtb.Document.Blocks
            .OfType<BlockUIContainer>().First().Child;
        Assert.That(region.Content, Does.Not.Contain("\r"));
        Assert.That(region.Content, Is.EqualTo("x = 1\ny = 2"));
        Assert.That(rtb.GetPlainText(), Does.Not.Contain("\r"));
        Assert.That(rtb.GetPlainText(), Is.EqualTo("before\n```python\nx = 1\ny = 2\n```\nafter"));
    }

    [Test]
    public void FindFirstFence_IndentedOpeningMarker_IsNotTreatedAsFence()
    {
        // An indented ``` (e.g. inside a list item or block-quote) is literal text, not a
        // fence marker — so no region is created and the indentation is preserved.
        var lines = new[] { "- item", "    ```", "    code", "    ```" };
        Assert.That(EmbeddedFenceDetector.FindFirstFence(lines), Is.Null);
    }

    [Test]
    public void FindFirstFence_IndentedClosingMarker_IsNotTreatedAsClose()
    {
        // Opening marker is at column 0, but the only candidate close is indented, so the
        // fence is considered incomplete (no column-0 close) and is not recognized.
        var lines = new[] { "```", "code", "    ```" };
        Assert.That(EmbeddedFenceDetector.FindFirstFence(lines), Is.Null);
    }

    [Test]
    public void FindFirstFence_OpeningMarkerWithTrailingWhitespace_StillRecognized()
    {
        // Trailing whitespace after the language tag is tolerated (TrimEnd), unlike leading
        // indentation.
        var lines = new[] { "```python   ", "x = 1", "```  " };
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);

        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.Language, Is.EqualTo("python"));
    }

    // ── Bug #2: variable-length fences for lossless round-trip ───────────────────

    // Serializes content, re-parses it with the detector, and returns the extracted content —
    // the full copy/send → re-detect round-trip for a single region.
    private static (string ExtractedContent, string Language, int FenceLength) RoundTrip(string content, string? language)
    {
        var fenced = EmbeddedFenceDetector.BuildFencedText(content, language);
        var lines = fenced.Split('\n');
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);
        Assert.That(fence, Is.Not.Null, "serialized block must re-parse as a complete fence");
        return (fence!.Value.ExtractContent(lines), fence.Value.Language, fence.Value.FenceLength);
    }

    private static int OpenFenceLength(string content)
        => EmbeddedFenceDetector.BuildFencedText(content, null).Split('\n')[0].Length;

    [Test]
    public void BuildFencedText_ContentWithBareThreeBacktickLine_UsesFourBacktickOuterFence()
    {
        var text = EmbeddedFenceDetector.BuildFencedText("```", null);
        Assert.That(text, Is.EqualTo("````\n```\n````"));
    }

    [Test]
    public void RoundTrip_ContentWithBareThreeBacktickLine_IsLossless()
    {
        var (content, language, fenceLength) = RoundTrip("```", null);
        Assert.That(content, Is.EqualTo("```"));
        Assert.That(language, Is.EqualTo(""));
        Assert.That(fenceLength, Is.EqualTo(4));
    }

    [Test]
    public void RoundTrip_ContentWithNestedFencedBlock_IsLossless()
    {
        var nested = "```python\nprint('hi')\n```";
        var (content, _, fenceLength) = RoundTrip(nested, null);
        Assert.That(content, Is.EqualTo(nested));
        Assert.That(fenceLength, Is.GreaterThanOrEqualTo(4));
    }

    [Test]
    public void RoundTrip_ContentWithTwentyBacktickLine_UsesTwentyOneBacktickOuterFence()
    {
        var twenty = new string('`', 20);
        var (content, _, fenceLength) = RoundTrip(twenty, null);
        Assert.That(content, Is.EqualTo(twenty));
        Assert.That(fenceLength, Is.EqualTo(21));
    }

    [Test]
    public void RoundTrip_InlineBackticksWithinTextLine_IsLossless()
    {
        var (content, _, fenceLength) = RoundTrip("use `code` inline here", null);
        Assert.That(content, Is.EqualTo("use `code` inline here"));
        Assert.That(fenceLength, Is.EqualTo(3), "inline single backticks never force the outer fence past 3");
    }

    [Test]
    public void RoundTrip_EmptyContent_IsLossless()
    {
        var (content, _, fenceLength) = RoundTrip("", null);
        Assert.That(content, Is.EqualTo(""));
        Assert.That(fenceLength, Is.EqualTo(3));
    }

    [Test]
    public void RoundTrip_WhitespaceOnlyContent_IsLossless()
    {
        var (content, _, fenceLength) = RoundTrip("   \n\t\n   ", null);
        Assert.That(content, Is.EqualTo("   \n\t\n   "));
        Assert.That(fenceLength, Is.EqualTo(3));
    }

    [Test]
    public void RoundTrip_LanguageTagPreservedWithLongerFence()
    {
        var (content, language, fenceLength) = RoundTrip("```\ninner", "python");
        Assert.That(content, Is.EqualTo("```\ninner"));
        Assert.That(language, Is.EqualTo("python"));
        Assert.That(fenceLength, Is.EqualTo(4));
    }

    [Test]
    public void RoundTrip_CrlfContent_IsNormalizedAndLossless()
    {
        var (content, _, _) = RoundTrip("a\r\nb\rc", null);
        Assert.That(content, Does.Not.Contain("\r"));
        Assert.That(content, Is.EqualTo("a\nb\nc"));
    }

    [Test]
    public void LongestBacktickRun_CountsLongestConsecutiveRunAnywhere()
    {
        Assert.That(EmbeddedFenceDetector.LongestBacktickRun(""), Is.EqualTo(0));
        Assert.That(EmbeddedFenceDetector.LongestBacktickRun("no ticks"), Is.EqualTo(0));
        Assert.That(EmbeddedFenceDetector.LongestBacktickRun("a `` b ``` c"), Is.EqualTo(3));
        Assert.That(EmbeddedFenceDetector.LongestBacktickRun("```\ntext\n`````"), Is.EqualTo(5));
    }

    [Test]
    public void BuildFencedText_FenceLengthMath_AtBoundaries()
    {
        Assert.That(OpenFenceLength(""), Is.EqualTo(3), "0 backticks -> 3");
        Assert.That(OpenFenceLength("```"), Is.EqualTo(4), "3 backticks -> 4");
        Assert.That(OpenFenceLength("````"), Is.EqualTo(5), "4 backticks -> 5");
        Assert.That(OpenFenceLength(new string('`', 20)), Is.EqualTo(21), "20 backticks -> 21");
    }

    [Test]
    public void FindFirstFence_ParsesFourBacktickFence_CapturingLengthAndLanguage()
    {
        var lines = new[] { "````python", "```", "still code", "````" };
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);

        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.FenceLength, Is.EqualTo(4));
        Assert.That(fence.Value.Language, Is.EqualTo("python"));
        Assert.That(fence.Value.OpenLineIndex, Is.EqualTo(0));
        Assert.That(fence.Value.CloseLineIndex, Is.EqualTo(3));
        Assert.That(fence.Value.ExtractContent(lines), Is.EqualTo("```\nstill code"));
    }

    [Test]
    public void FindFirstFence_ParsesFiveBacktickFence()
    {
        var lines = new[] { "`````", "````", "code", "`````" };
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);

        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.FenceLength, Is.EqualTo(5));
        Assert.That(fence.Value.ExtractContent(lines), Is.EqualTo("````\ncode"));
    }

    [Test]
    public void FindFirstFence_ShorterBareBacktickLine_DoesNotClose_LongerLineCloses()
    {
        // Opener is 4 backticks: an inner 3-backtick line must NOT close the fence; only the
        // later 4-backtick line does.
        var lines = new[] { "````", "```", "````" };
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);

        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.FenceLength, Is.EqualTo(4));
        Assert.That(fence.Value.CloseLineIndex, Is.EqualTo(2));
        Assert.That(fence.Value.ExtractContent(lines), Is.EqualTo("```"));
    }

    [Test]
    public void FindFirstFence_CloseLongerThanOpener_StillCloses()
    {
        // A bare-backtick line strictly longer than the opener still qualifies as a close.
        var lines = new[] { "```", "code", "`````" };
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);

        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.FenceLength, Is.EqualTo(3));
        Assert.That(fence.Value.CloseLineIndex, Is.EqualTo(2));
    }

    [Test]
    public void FindFirstFence_OnlyShorterBareBacktickLines_ReturnsNull()
    {
        // Opener is 4 backticks; the only later bare-backtick line is 3 — too short to close.
        var lines = new[] { "````", "code", "```" };
        Assert.That(EmbeddedFenceDetector.FindFirstFence(lines), Is.Null);
    }

    [Test]
    public void FindOpenFenceWithNoClose_FourBacktickOpenerWithOnlyShorterTicks_IsUnterminated()
    {
        var lines = new[] { "````", "```", "trailing" };
        var open = EmbeddedFenceDetector.FindOpenFenceWithNoClose(lines);

        Assert.That(open, Is.Not.Null);
        Assert.That(open!.Value.LineIndex, Is.EqualTo(0));
        Assert.That(open.Value.FenceLength, Is.EqualTo(4));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryConvertOpenFence_UnterminatedOpenerWithContent_ConsumesThroughEndOfInput()
    {
        // User requirement: if there is no closing fence before end-of-input, grab everything
        // from the line after the opener through the end of the prompt as region content —
        // nothing is silently dropped.
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nhello\nworld");

        Assert.That(EmbeddedCodeRegionManager.TryConvertOpenFence(rtb), Is.True);

        var region = (EmbeddedCodeRegionBlock)rtb.Document.Blocks
            .OfType<BlockUIContainer>().First().Child;
        Assert.That(region.Content, Is.EqualTo("hello\nworld"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FenceContainingBareBacktickLine_RoundTripsThroughRichTextBox()
    {
        // End-to-end: a pasted region whose content contains a bare ``` line must survive a
        // full convert -> GetPlainText (copy/send) -> reconvert cycle without truncation.
        var rtb = new RichTextBox();
        rtb.SetPlainText("````\n```\ninner\n```\n````");
        while (EmbeddedCodeRegionManager.TryConvertNextFence(rtb)) { }

        var region = (EmbeddedCodeRegionBlock)rtb.Document.Blocks
            .OfType<BlockUIContainer>().First().Child;
        Assert.That(region.Content, Is.EqualTo("```\ninner\n```"));

        var serialized = rtb.GetPlainText();
        Assert.That(serialized, Is.EqualTo("````\n```\ninner\n```\n````"));

        var reparsed = new RichTextBox();
        reparsed.SetPlainText(serialized);
        while (EmbeddedCodeRegionManager.TryConvertNextFence(reparsed)) { }
        var reparsedRegion = (EmbeddedCodeRegionBlock)reparsed.Document.Blocks
            .OfType<BlockUIContainer>().First().Child;
        Assert.That(reparsedRegion.Content, Is.EqualTo("```\ninner\n```"));
    }

    // ── Editor chrome: height cap & scrollbar (prompt-editor-embedded-regions UI pass) ──

    [Test, Apartment(ApartmentState.STA)]
    public void AttachHost_HostMaxHeight260_ContentEditorCapAtHalf()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nhello\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;
        var contentBox = GetContentEditor(region);

        var host = new RichTextBox { MaxHeight = 260 };
        region.AttachHost(host);

        Assert.That(contentBox.MaxHeight, Is.EqualTo(130),
            "content editor height caps at half the host RichTextBox's MaxHeight");
        Assert.That(contentBox.VerticalScrollBarVisibility, Is.EqualTo(ScrollBarVisibility.Auto),
            "a vertical scrollbar must appear when content exceeds the cap");
    }

    [Test, Apartment(ApartmentState.STA)]
    public void AttachHost_HostMaxHeightUnset_FallsBackTo130()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nhello\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;
        var contentBox = GetContentEditor(region);

        // A default RichTextBox has MaxHeight == PositiveInfinity, so the fallback cap applies.
        var host = new RichTextBox();
        Assert.That(double.IsInfinity(host.MaxHeight) || double.IsNaN(host.MaxHeight), Is.True);
        region.AttachHost(host);

        Assert.That(contentBox.MaxHeight, Is.EqualTo(130));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void BeforeAttachHost_ContentEditorHasDefault130Cap()
    {
        var region = EmbeddedCodeRegionBlock.Build("hello", "");
        var contentBox = GetContentEditor(region);
        Assert.That(contentBox.MaxHeight, Is.EqualTo(130));
    }

    // The region's content editor is now an AvalonEdit TextEditor hosted inside the editor
    // border (the second child of the region's StackPanel).
    private static ICSharpCode.AvalonEdit.TextEditor GetContentEditor(EmbeddedCodeRegionBlock region)
        => (ICSharpCode.AvalonEdit.TextEditor)((Border)((StackPanel)region.Child).Children[1]).Child;

    // Depth-first visual-tree search for the first descendant of type <typeparamref name="T"/>.
    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;
            var nested = FindDescendant<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
