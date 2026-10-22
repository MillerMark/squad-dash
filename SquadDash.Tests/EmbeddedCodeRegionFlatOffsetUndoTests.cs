using System.Linq;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Documents;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Step 5 (PROMPTUNDO-20261015-005) regression proof for the flat-offset ↔ caret/anchor/selection
/// round-trip of the single-authority prompt undo/redo engine (PROMPTUNDO-20261015, §3–§4).
/// <para>
/// Each test drives the REAL production surfaces — <see cref="PromptUndoManager.CaptureSnapshot"/>,
/// <see cref="PromptUndoManager.ApplySnapshot"/>, <see cref="PromptUndoManager.ResolveAbsoluteOffset"/>
/// and <see cref="EmbeddedFenceDetector.FencedContentOffset"/> — against a document that mixes host
/// text with one embedded region, and asserts that an absolute <c>{anchorOffset, caretOffset}</c>
/// selection survives a capture→apply round-trip with the correct absolute span AND direction (which
/// endpoint is the active caret) for all six contract cases:
/// forward host, backward host, collapsed caret, both-endpoints-in-region, straddle-forward
/// (host anchor + in-region caret) and straddle-backward (in-region anchor + host caret).
/// </para>
/// <para>
/// DISCREPANCY WITH THE NORMATIVE CONTRACT NUMBERS (verified, intentional): the Step 2 fixture
/// describes the region of document <c>Hello\n```python\nx=1\n```\nWorld</c> as fenced span [6,23)
/// with content base 16 — i.e. raw-string indices where the first backtick is at string index 6 and
/// 'x' at 16. The PRODUCTION flat coordinate space that <see cref="PromptUndoManager"/> actually
/// captures and resolves in is <see cref="RichTextBoxExtensions.GetCharOffset"/>, which does NOT
/// count the paragraph-separator '\n' that precedes the region as a position before the region: it
/// reports the region's fenced-span start as absolute 5 and the content base as 15 (verified at
/// runtime via ResolveAbsoluteOffset — offsets 15,16,17 resolve to region content-local 0,1,2).
/// Capture (<c>CaptureAbsoluteSelection</c>) and resolve (<c>ResolveAbsoluteOffset</c>) use that
/// SAME space, so the round-trip is internally exact; the only difference from the normative example
/// is a uniform −1 on the region's absolute base. The meaningful region position — the content-LOCAL
/// offset — matches the contract exactly (straddle-forward caret = content-local 2, straddle-backward
/// anchor = content-local 1); per the task these tests assert the REAL production values (caret 17,
/// anchor 16) rather than the raw-string-index values (18, 17), and host caret = 26 matches exactly.
/// </para>
/// <para>
/// The region/straddle cases cannot be captured through live keyboard focus headlessly
/// (<c>HasContentKeyboardFocus</c> needs a real input surface), so their absolute offsets are
/// derived from the SAME production math the capture path uses
/// (<c>spanStart + FencedContentOffset(content, language) + local</c>) rather than hardcoded magic
/// numbers, and the restore is asserted through <see cref="PromptUndoManager.ResolveAbsoluteOffset"/>
/// (the production inverse mapping) plus the region's own read-back selection.
/// </para>
/// </summary>
[TestFixture]
internal sealed class EmbeddedCodeRegionFlatOffsetUndoTests
{
    // Shared document: host text "Hello", one python region whose content is "x=1", host text "World".
    //
    //  Flat serialization (GetPlainText): "Hello\n```python\nx=1\n```\nWorld".
    //
    //  Production flat offset space (RichTextBoxExtensions.GetCharOffset, the space the undo engine
    //  captures/resolves in — verified at runtime):
    //    host "Hello"   → absolute 0..5
    //    region fenced-span start (spanStart) = 5
    //    region content "x=1" → absolute 15,16,17 (content-local 0,1,2); content end at 18
    //    host "World"   → absolute 19..29   ('W'=19 … 'r'=26 … 'd'=28, end 29)
    private const string MixedDocument = "Hello\n```python\nx=1\n```\nWorld";

    // Production-measured values (NOT the raw-string-index 6/16 from the normative example — see the
    // class-level DISCREPANCY note). spanStart 5, content base 15.
    private const int ExpectedSpanStart = 5;
    private const int ExpectedContentBase = 15;

    [SetUp]
    public void ResetActiveRegion()
    {
        // ActiveRegion is a static set by any prior RestoreContentSelection; clear it so the host
        // capture path in CaptureSnapshot is not influenced by a stale region from another test.
        EmbeddedCodeRegionBlock.ClearActiveRegion();
    }

    // ── Shared harness ──────────────────────────────────────────────────────────

    /// Builds the mixed host+region document through the real render path and returns the host
    /// RichTextBox plus the materialized region and the production-derived absolute content base.
    private static (RichTextBox rtb, EmbeddedCodeRegionBlock region, int contentBase) BuildMixedDocument()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText(MixedDocument);
        EmbeddedCodeRegionManager.ConvertAllFences(rtb);

        // Serialization is lossless: the flat offsets asserted below are the offsets this exact
        // string defines — not invented numbers.
        Assert.That(rtb.GetPlainText(), Is.EqualTo(MixedDocument),
            "mixed host+region document must round-trip losslessly through GetPlainText/ToFencedText");

        var (region, container) = SingleRegion(rtb);

        // Derive the region's absolute content base the SAME way the production capture path does.
        var spanStart = rtb.GetCharOffset(container.ElementStart);
        var contentBase = spanStart + EmbeddedFenceDetector.FencedContentOffset(region.Content, region.Language);

        Assert.That(spanStart, Is.EqualTo(ExpectedSpanStart),
            "region fenced-span start must be the production GetCharOffset value 5");
        Assert.That(contentBase, Is.EqualTo(ExpectedContentBase),
            "region content base must be the production value 15 (spanStart 5 + fenced-content offset 10)");

        return (rtb, region, contentBase);
    }

    private static (EmbeddedCodeRegionBlock region, BlockUIContainer container) SingleRegion(RichTextBox rtb)
    {
        var container = rtb.Document.Blocks
            .OfType<BlockUIContainer>()
            .Single(c => c.Child is EmbeddedCodeRegionBlock);
        return ((EmbeddedCodeRegionBlock)container.Child, container);
    }

    // ── Case 1: forward host selection (both endpoints in host text, anchor < caret) ────

    [Test, Apartment(ApartmentState.STA)]
    public void FlatOffset_ForwardHostSelection_RoundTripsSpanAndDirection()
    {
        var (rtb, _, _) = BuildMixedDocument();

        // "Hello" occupies absolute [0,5]; select it forward.
        const int anchor = 0;
        const int caret = 5;
        var snapshot = new PromptSnapshot(MixedDocument, CaretOffset: caret, AnchorOffset: anchor);

        Assert.That(snapshot.IsCollapsed, Is.False);
        Assert.That(snapshot.IsBackward, Is.False, "anchor < caret is a FORWARD selection");
        Assert.That(snapshot.SelectionStart, Is.EqualTo(0));
        Assert.That(snapshot.SelectionEnd, Is.EqualTo(5));

        PromptUndoManager.ApplySnapshot(rtb, snapshot);

        // Both endpoints resolve onto the host surface.
        Assert.That(PromptUndoManager.ResolveAbsoluteOffset(rtb, anchor).Surface, Is.EqualTo(PromptOffsetSurface.Host));
        Assert.That(PromptUndoManager.ResolveAbsoluteOffset(rtb, caret).Surface, Is.EqualTo(PromptOffsetSurface.Host));

        // Direction guarantee (§4): the active caret restores on the moving (higher) end.
        Assert.That(rtb.GetCaretOffset(), Is.EqualTo(caret), "forward selection restores the caret on the higher end");

        Assert.That(rtb.Selection.Text, Is.EqualTo("Hello"));
        Assert.That(PromptUndoManager.CaptureSnapshot(rtb), Is.EqualTo(snapshot));
    }

    // ── Case 2: backward host selection (both endpoints in host text, anchor > caret) ───

    [Test, Apartment(ApartmentState.STA)]
    public void FlatOffset_BackwardHostSelection_RoundTripsSpanAndDirection()
    {
        var (rtb, _, _) = BuildMixedDocument();

        // Same "Hello" span, selected BACKWARD: anchor on the higher end, caret on the lower end.
        const int anchor = 5;
        const int caret = 0;
        var snapshot = new PromptSnapshot(MixedDocument, CaretOffset: caret, AnchorOffset: anchor);

        Assert.That(snapshot.IsCollapsed, Is.False);
        Assert.That(snapshot.IsBackward, Is.True, "caret < anchor is a BACKWARD selection");
        Assert.That(snapshot.SelectionStart, Is.EqualTo(0));
        Assert.That(snapshot.SelectionEnd, Is.EqualTo(5));

        PromptUndoManager.ApplySnapshot(rtb, snapshot);

        Assert.That(PromptUndoManager.ResolveAbsoluteOffset(rtb, anchor).Surface, Is.EqualTo(PromptOffsetSurface.Host));
        Assert.That(PromptUndoManager.ResolveAbsoluteOffset(rtb, caret).Surface, Is.EqualTo(PromptOffsetSurface.Host));

        // Direction guarantee (§4): unlike the forward case the caret restores on the LOWER (moving)
        // end, proving the active caret end is preserved rather than canonicalized to a forward span.
        Assert.That(rtb.GetCaretOffset(), Is.EqualTo(caret), "backward selection restores the caret on the lower end");

        Assert.That(rtb.Selection.Text, Is.EqualTo("Hello"));
        Assert.That(PromptUndoManager.CaptureSnapshot(rtb), Is.EqualTo(snapshot));
    }

    // ── Case 3: collapsed caret (anchor == caret) ───────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void FlatOffset_CollapsedHostCaret_RoundTripsWithNoSelection()
    {
        var (rtb, _, _) = BuildMixedDocument();

        const int point = 2; // inside "Hello", between the two 'l's
        var snapshot = new PromptSnapshot(MixedDocument, CaretOffset: point, AnchorOffset: point);

        Assert.That(snapshot.IsCollapsed, Is.True);
        Assert.That(snapshot.IsBackward, Is.False);
        Assert.That(snapshot.SelectionStart, Is.EqualTo(snapshot.SelectionEnd));

        PromptUndoManager.ApplySnapshot(rtb, snapshot);

        Assert.That(PromptUndoManager.ResolveAbsoluteOffset(rtb, point).Surface, Is.EqualTo(PromptOffsetSurface.Host));
        Assert.That(rtb.GetSelectionLength(), Is.EqualTo(0), "a collapsed caret restores with no selection");
        Assert.That(rtb.GetCaretOffset(), Is.EqualTo(point));
    }

    // ── Case 4: selection with both endpoints inside one region ─────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void FlatOffset_SelectionWhollyInsideRegion_RoundTripsLocalSpanAndDirection()
    {
        var (rtb, _, contentBase) = BuildMixedDocument();

        // Select the whole region content "x=1" FORWARD: content-local [0,3] → absolute [15,18].
        var anchor = contentBase + 0; // 15
        var caret = contentBase + 3;  // 18
        var snapshot = new PromptSnapshot(MixedDocument, CaretOffset: caret, AnchorOffset: anchor);

        Assert.That(snapshot.IsBackward, Is.False);

        PromptUndoManager.ApplySnapshot(rtb, snapshot);

        // Both endpoints resolve into the SAME rebuilt region, at the expected region-local offsets.
        var (regionAfter, _) = SingleRegion(rtb);
        var anchorResolved = PromptUndoManager.ResolveAbsoluteOffset(rtb, anchor);
        var caretResolved = PromptUndoManager.ResolveAbsoluteOffset(rtb, caret);

        Assert.That(anchorResolved.Surface, Is.EqualTo(PromptOffsetSurface.Region));
        Assert.That(caretResolved.Surface, Is.EqualTo(PromptOffsetSurface.Region));
        Assert.That(anchorResolved.Region, Is.SameAs(regionAfter));
        Assert.That(caretResolved.Region, Is.SameAs(regionAfter));
        Assert.That(anchorResolved.RegionLocalOffset, Is.EqualTo(0));
        Assert.That(caretResolved.RegionLocalOffset, Is.EqualTo(3));

        // The region editor physically restored the selection: read it back and lift to absolute,
        // proving the local span AND direction survive (anchor local 0, caret local 3 → forward).
        var (anchorLocal, caretLocal) = regionAfter.GetContentSelectionLocal();
        Assert.That(contentBase + anchorLocal, Is.EqualTo(anchor));
        Assert.That(contentBase + caretLocal, Is.EqualTo(caret));
        Assert.That(caretLocal, Is.GreaterThan(anchorLocal), "forward in-region selection keeps caret on the higher local end");
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FlatOffset_BackwardSelectionWhollyInsideRegion_PreservesInverseDirection()
    {
        var (rtb, _, contentBase) = BuildMixedDocument();

        // Same region content span, selected BACKWARD: anchor local 3, caret local 0.
        var anchor = contentBase + 3; // 18
        var caret = contentBase + 0;  // 15
        var snapshot = new PromptSnapshot(MixedDocument, CaretOffset: caret, AnchorOffset: anchor);

        Assert.That(snapshot.IsBackward, Is.True);

        PromptUndoManager.ApplySnapshot(rtb, snapshot);

        var (regionAfter, _) = SingleRegion(rtb);
        var (anchorLocal, caretLocal) = regionAfter.GetContentSelectionLocal();
        Assert.That(contentBase + anchorLocal, Is.EqualTo(anchor));
        Assert.That(contentBase + caretLocal, Is.EqualTo(caret));
        Assert.That(caretLocal, Is.LessThan(anchorLocal), "backward in-region selection keeps caret on the lower local end");
    }

    // ── Case 5: straddle-forward — host anchor + in-region caret ────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void FlatOffset_StraddleForward_HostAnchorInRegionCaret_RoundTrips()
    {
        var (rtb, _, contentBase) = BuildMixedDocument();

        // Normative contract "straddle-forward-roundtrip", asserted at the REAL production offsets:
        //   anchor = 2  (host text, inside "Hello")
        //   caret  = content-local 2 inside the region  →  contentBase 15 + 2 = 17
        //   (the normative example quotes raw-string caret 18; production's GetCharOffset space
        //    yields 17 — see the class-level DISCREPANCY note. Content-local 2 matches exactly.)
        const int anchor = 2;
        var caret = contentBase + 2; // 17

        Assert.That(caret, Is.EqualTo(17), "in-region caret absolute offset in the production flat space is 17 (content-local 2)");

        var snapshot = new PromptSnapshot(MixedDocument, CaretOffset: caret, AnchorOffset: anchor);

        Assert.That(snapshot.IsBackward, Is.False, "anchor(2) < caret(17): FORWARD straddle");
        Assert.That(snapshot.SelectionStart, Is.EqualTo(2));
        Assert.That(snapshot.SelectionEnd, Is.EqualTo(17));

        Assert.DoesNotThrow(() => PromptUndoManager.ApplySnapshot(rtb, snapshot),
            "a straddling selection must never throw on restore");

        // The restore locates the two endpoints on DIFFERENT surfaces: anchor outside the region
        // (host), active caret inside the region's content at local offset 2.
        var (regionAfter, _) = SingleRegion(rtb);
        var anchorResolved = PromptUndoManager.ResolveAbsoluteOffset(rtb, anchor);
        var caretResolved = PromptUndoManager.ResolveAbsoluteOffset(rtb, caret);

        Assert.That(anchorResolved.Surface, Is.EqualTo(PromptOffsetSurface.Host),
            "straddle-forward anchor stays in host text");
        Assert.That(caretResolved.Surface, Is.EqualTo(PromptOffsetSurface.Region),
            "straddle-forward active caret lands inside the region");
        Assert.That(caretResolved.Region, Is.SameAs(regionAfter));
        Assert.That(caretResolved.RegionLocalOffset, Is.EqualTo(2),
            "in-region caret maps back to content-local offset 2 (matches the normative example)");
        Assert.That(anchorResolved.AbsoluteOffset, Is.EqualTo(2));
        Assert.That(caretResolved.AbsoluteOffset, Is.EqualTo(17));
    }

    // ── Case 6: straddle-backward — in-region anchor + host caret ───────────────

    [Test, Apartment(ApartmentState.STA)]
    public void FlatOffset_StraddleBackward_InRegionAnchorHostCaret_RoundTrips()
    {
        var (rtb, _, contentBase) = BuildMixedDocument();

        // Normative contract "straddle-backward-roundtrip", asserted at the REAL production offsets:
        //   anchor = content-local 1 inside the region  →  contentBase 15 + 1 = 16
        //   caret  = 26 (host text, inside "World", after the region) — matches the example exactly
        //   (the normative example quotes raw-string anchor 17; production yields 16, content-local 1.)
        var anchor = contentBase + 1; // 16
        const int caret = 26;

        Assert.That(anchor, Is.EqualTo(16), "in-region anchor absolute offset in the production flat space is 16 (content-local 1)");

        var snapshot = new PromptSnapshot(MixedDocument, CaretOffset: caret, AnchorOffset: anchor);

        Assert.That(snapshot.SelectionStart, Is.EqualTo(16));
        Assert.That(snapshot.SelectionEnd, Is.EqualTo(26));

        Assert.DoesNotThrow(() => PromptUndoManager.ApplySnapshot(rtb, snapshot),
            "the inverse straddle orientation must never throw on restore");

        // Inverse orientation of case 5: anchor inside the region (content-local 1), active caret in
        // host text AFTER the region.
        var (regionAfter, _) = SingleRegion(rtb);
        var anchorResolved = PromptUndoManager.ResolveAbsoluteOffset(rtb, anchor);
        var caretResolved = PromptUndoManager.ResolveAbsoluteOffset(rtb, caret);

        Assert.That(anchorResolved.Surface, Is.EqualTo(PromptOffsetSurface.Region),
            "straddle-backward anchor stays inside the region");
        Assert.That(anchorResolved.Region, Is.SameAs(regionAfter));
        Assert.That(anchorResolved.RegionLocalOffset, Is.EqualTo(1),
            "in-region anchor maps back to content-local offset 1 (matches the normative example)");
        Assert.That(caretResolved.Surface, Is.EqualTo(PromptOffsetSurface.Host),
            "straddle-backward active caret lands in host text after the region");
        Assert.That(anchorResolved.AbsoluteOffset, Is.EqualTo(16));
        Assert.That(caretResolved.AbsoluteOffset, Is.EqualTo(26));
    }
}
