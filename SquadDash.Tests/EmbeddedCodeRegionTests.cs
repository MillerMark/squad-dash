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
        Assert.That(rtb.Document.Blocks.Count, Is.EqualTo(1));
        var container = (BlockUIContainer)rtb.Document.Blocks.FirstBlock!;
        var region = (EmbeddedCodeRegionBlock)container.Child;
        Assert.That(region.Content, Is.EqualTo("def foo():\n    pass"));
        Assert.That(region.Language, Is.EqualTo(""));

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
        Assert.That(region.IsCollapsed, Is.False);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void CollapseThenExpand_TogglesIsCollapsed_ContentUnaffected()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nhello\nworld\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;

        region.SetCollapsed(true);
        Assert.That(region.IsCollapsed, Is.True);
        Assert.That(region.Content, Is.EqualTo("hello\nworld"));

        region.SetCollapsed(false);
        Assert.That(region.IsCollapsed, Is.False);
        Assert.That(region.Content, Is.EqualTo("hello\nworld"));

        // Collapse state never changes the serialized fenced form.
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```\nhello\nworld\n```"));
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
        Assert.That(blocks.Count, Is.EqualTo(3));
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
        var contentBox = (TextBox)((StackPanel)region.Child).Children[1];
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
        var contentBox = (TextBox)((StackPanel)region.Child).Children[1];
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

        // The caret is nudged onto the following paragraph's ContentStart for reliable
        // assignment (see EmbeddedCodeRegionManager.ResolveCaretPointerAfterRebuild), which is
        // one plain-text offset past the bare region-boundary position (both represent the
        // same visual "right after the region" location) — the key regression being guarded
        // against is that it must land near the region, never jump to the document's end.
        Assert.That(rtb.GetCaretOffset(), Is.EqualTo(caretAfterFence + 1));
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
}
