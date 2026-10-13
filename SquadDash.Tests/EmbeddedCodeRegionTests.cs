using System.Threading;
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
}
