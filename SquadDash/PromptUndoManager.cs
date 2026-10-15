using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SquadDash;

/// <summary>
/// One immutable prompt undo/redo history entry (PROMPTUNDO-20261015, §2). It is the full flat
/// prompt serialization (<see cref="SerializedText"/> — the lossless
/// <see cref="RichTextBoxExtensions.GetPlainText"/> / <see cref="EmbeddedCodeRegionBlock.ToFencedText"/>
/// round-trip spanning host text AND every embedded region's fenced span) plus the selection as two
/// ABSOLUTE integer offsets in that one flat coordinate space.
/// <para>
/// <see cref="AnchorOffset"/> is the fixed end (where the selection began) and
/// <see cref="CaretOffset"/> the moving end (where the caret currently is), stored separately so
/// direction survives a round-trip (§4): a forward selection has anchor &lt; caret, a backward
/// selection has caret &lt; anchor, and a collapsed caret has anchor == caret. The authoritative
/// range is always <c>[SelectionStart, SelectionEnd]</c> PLUS which end is the caret.
/// </para>
/// </summary>
public readonly record struct PromptSnapshot(string SerializedText, int CaretOffset, int AnchorOffset)
{
    /// Lower bound of the absolute selection range (inclusive start).
    public int SelectionStart => Math.Min(CaretOffset, AnchorOffset);

    /// Upper bound of the absolute selection range (exclusive end).
    public int SelectionEnd => Math.Max(CaretOffset, AnchorOffset);

    /// True when there is no selection (the caret sits at a single point).
    public bool IsCollapsed => CaretOffset == AnchorOffset;

    /// True when the moving caret is BEFORE the anchor (a backward selection).
    public bool IsBackward => CaretOffset < AnchorOffset;
}

/// Which surface an absolute flat offset resolves onto after a document rebuild.
internal enum PromptOffsetSurface
{
    /// The offset lives in host text (or at a region's element boundary) — a host
    /// <see cref="TextPointer"/> locates it.
    Host,

    /// The offset lands strictly inside an embedded region's content — the region plus a
    /// region-local AvalonEdit offset locate it.
    Region,
}

/// <summary>
/// Result of resolving one absolute flat offset (PROMPTUNDO-20261015, §3) against the rebuilt
/// prompt document: either a host <see cref="TextPointer"/> or a (region + region-local offset)
/// pair. <see cref="AbsoluteOffset"/> is carried through so a straddle restore can fall back to a
/// host-level pointer for an in-region endpoint without re-deriving it.
/// </summary>
internal readonly record struct ResolvedPromptOffset(
    PromptOffsetSurface Surface,
    TextPointer? HostPointer,
    EmbeddedCodeRegionBlock? Region,
    int RegionLocalOffset,
    int AbsoluteOffset);

/// <summary>
/// Single-authority prompt undo/redo engine (PROMPTUNDO-20261015). This step (002) implements the
/// snapshot CAPTURE and APPLY halves plus the cross-seam selection resolution that locates an
/// absolute flat offset on the correct surface (host text vs a specific embedded region's internal
/// AvalonEdit offset). Ctrl+Z/Ctrl+Y routing and native-undo suppression are Step 3
/// (PROMPTUNDO-20261015-003); coalescing and clear-on-send are Step 4 (PROMPTUNDO-20261015-004);
/// the deterministic round-trip suite is Step 5 (PROMPTUNDO-20261015-005). This class is the engine
/// those steps consume.
/// </summary>
public static class PromptUndoManager
{
    // ── Capture ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Produces a <see cref="PromptSnapshot"/> from the prompt's CURRENT state: the full flat
    /// serialization plus absolute caret/anchor offsets. When keyboard focus is inside an embedded
    /// region the live caret/anchor are region-local and are lifted into the absolute flat space by
    /// adding the region's content-base offset; otherwise the host <see cref="RichTextBox"/>
    /// selection is used directly. Selection direction is preserved (§4).
    /// </summary>
    public static PromptSnapshot CaptureSnapshot(RichTextBox prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var serialized = prompt.GetPlainText();
        var (caret, anchor) = CaptureAbsoluteSelection(prompt);
        return new PromptSnapshot(serialized, caret, anchor);
    }

    /// Captures the current selection as absolute (caretOffset, anchorOffset) in the flat space.
    private static (int caret, int anchor) CaptureAbsoluteSelection(RichTextBox prompt)
    {
        var active = EmbeddedCodeRegionBlock.ActiveRegion;
        if (active is not null && active.IsAttachedToHost() && active.HasContentKeyboardFocus)
        {
            var spanStart = TryGetRegionSpanStart(prompt, active);
            if (spanStart >= 0)
            {
                var contentBase = spanStart + EmbeddedFenceDetector.FencedContentOffset(active.Content, active.Language);
                var (anchorLocal, caretLocal) = active.GetContentSelectionLocal();
                return (contentBase + caretLocal, contentBase + anchorLocal);
            }
        }

        // Host selection. WPF's TextSelection does not expose an anchor/caret direction, so derive
        // it by comparing the live caret to the selection's start/end.
        var caretOffset = prompt.GetCaretOffset();
        var selStart = prompt.GetSelectionStart();
        var selEnd = selStart + prompt.GetSelectionLength();

        int anchorOffset;
        if (caretOffset == selEnd)
            anchorOffset = selStart;           // forward selection (or collapsed)
        else if (caretOffset == selStart)
            anchorOffset = selEnd;             // backward selection
        else
            anchorOffset = caretOffset;        // caret outside the selection span (collapse)

        return (caretOffset, anchorOffset);
    }

    // ── Apply ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Restores <paramref name="snapshot"/> into <paramref name="prompt"/>: rebuilds the document
    /// from the flat serialization via the existing render path (<see cref="RichTextBoxExtensions.SetPlainText"/>
    /// followed by <see cref="EmbeddedCodeRegionManager.ConvertAllFences"/>, re-materializing every
    /// fenced block as an interactive region), then restores the EXACT selection — same absolute
    /// span with the caret on the stored moving end (§4) — for host-text selections, a collapsed
    /// caret, a selection wholly inside one region, and selections that straddle the host↔region
    /// boundary in either orientation (§3).
    /// </summary>
    public static void ApplySnapshot(RichTextBox prompt, PromptSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        prompt.SetPlainText(snapshot.SerializedText);
        EmbeddedCodeRegionManager.ConvertAllFences(prompt);

        var anchor = ResolveAbsoluteOffset(prompt, snapshot.AnchorOffset);
        var caret = ResolveAbsoluteOffset(prompt, snapshot.CaretOffset);
        RestoreResolvedSelection(prompt, anchor, caret);
    }

    /// Restores the live selection from two already-resolved endpoints, honoring the surface each
    /// landed on and preserving direction (the caret is the <paramref name="caret"/> endpoint).
    private static void RestoreResolvedSelection(RichTextBox prompt, ResolvedPromptOffset anchor, ResolvedPromptOffset caret)
    {
        var bothInSameRegion =
            anchor.Surface == PromptOffsetSurface.Region &&
            caret.Surface == PromptOffsetSurface.Region &&
            ReferenceEquals(anchor.Region, caret.Region);

        if (bothInSameRegion)
        {
            // Entirely inside one region (including a collapsed caret): restore directly in that
            // region's AvalonEdit, with no host text selected.
            anchor.Region!.RestoreContentSelection(anchor.RegionLocalOffset, caret.RegionLocalOffset);
            return;
        }

        var bothInHost =
            anchor.Surface == PromptOffsetSurface.Host &&
            caret.Surface == PromptOffsetSurface.Host;

        if (bothInHost && anchor.HostPointer is not null && caret.HostPointer is not null)
        {
            // Host-text selection or collapsed host caret. TextSelection.Select(anchor, moving)
            // places the caret at the moving end, so forward/backward direction is preserved.
            prompt.Selection.Select(anchor.HostPointer, caret.HostPointer);
            prompt.CaretPosition = caret.HostPointer;
            prompt.Focus();
            return;
        }

        // Straddle: the anchor and caret resolved to DIFFERENT surfaces (one host, one in-region),
        // or to two different regions. WPF cannot render a single visible highlight that spans the
        // host RichTextBox and a nested AvalonEdit, so the authoritative selection remains the
        // absolute [min,max] range + direction (carried by the snapshot); here we reflect that range
        // as faithfully as the control composition allows — a single host-level selection from the
        // anchor's absolute offset to the caret's absolute offset, treating any crossed region as
        // the atomic block it serializes to, with the caret on the stored moving end. The straddle
        // case is never silently dropped.
        var anchorPointer = anchor.HostPointer ?? prompt.GetTextPointerAt(anchor.AbsoluteOffset);
        var caretPointer = caret.HostPointer ?? prompt.GetTextPointerAt(caret.AbsoluteOffset);
        prompt.Selection.Select(anchorPointer, caretPointer);
        prompt.CaretPosition = caretPointer;
        prompt.Focus();
    }

    // ── Cross-seam resolution ───────────────────────────────────────────────────

    /// <summary>
    /// Resolves an absolute flat offset (PROMPTUNDO-20261015, §3) against the current prompt
    /// document to the surface it lands on: a host <see cref="TextPointer"/>, or a specific embedded
    /// region plus a region-local AvalonEdit offset when the absolute offset falls inside that
    /// region's serialized CONTENT. An offset inside a region's fence markers (the opening
    /// <c>{fence}{language}</c> / separating newline / closing fence) is NOT content — it resolves to
    /// the host boundary immediately before/after the region, consistent with
    /// <see cref="RichTextBoxExtensions.GetTextPointerAt"/>.
    /// <para>
    /// Content-base math, verified against <see cref="EmbeddedFenceDetector.BuildFencedText"/>
    /// (<c>{fence}{language}\n{content}\n{fence}</c>): content-local offset L maps to absolute
    /// <c>spanStart + FencedContentOffset(content,language) + L</c>, where
    /// <see cref="EmbeddedFenceDetector.FencedContentOffset"/> = fenceLength + language.Length + 1.
    /// </para>
    /// </summary>
    internal static ResolvedPromptOffset ResolveAbsoluteOffset(RichTextBox prompt, int absoluteOffset)
    {
        foreach (var (region, spanStart) in EnumerateRegionSpans(prompt))
        {
            var contentBase = spanStart + EmbeddedFenceDetector.FencedContentOffset(region.Content, region.Language);
            var contentLength = NormalizeContent(region.Content).Length;

            if (absoluteOffset >= contentBase && absoluteOffset <= contentBase + contentLength)
            {
                return new ResolvedPromptOffset(
                    PromptOffsetSurface.Region,
                    HostPointer: null,
                    Region: region,
                    RegionLocalOffset: absoluteOffset - contentBase,
                    AbsoluteOffset: absoluteOffset);
            }
        }

        return new ResolvedPromptOffset(
            PromptOffsetSurface.Host,
            HostPointer: prompt.GetTextPointerAt(absoluteOffset),
            Region: null,
            RegionLocalOffset: 0,
            AbsoluteOffset: absoluteOffset);
    }

    /// Enumerates each embedded region together with the ABSOLUTE flat offset at which its fenced
    /// span begins, using the same block-walk coordinate space as
    /// <see cref="RichTextBoxExtensions.GetCharOffset"/> (which returns a region's fenced-span start
    /// for its container's ElementStart pointer).
    private static IEnumerable<(EmbeddedCodeRegionBlock region, int spanStart)> EnumerateRegionSpans(RichTextBox prompt)
    {
        foreach (var block in prompt.Document.Blocks)
        {
            if (block is BlockUIContainer { Child: EmbeddedCodeRegionBlock region } container)
                yield return (region, prompt.GetCharOffset(container.ElementStart));
        }
    }

    /// Absolute fenced-span start of a specific region, or -1 if it is not in the document.
    private static int TryGetRegionSpanStart(RichTextBox prompt, EmbeddedCodeRegionBlock target)
    {
        foreach (var (region, spanStart) in EnumerateRegionSpans(prompt))
        {
            if (ReferenceEquals(region, target))
                return spanStart;
        }
        return -1;
    }

    private static string NormalizeContent(string? content)
        => (content ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
}

/// <summary>
/// The single-authority undo/redo HISTORY (PROMPTUNDO-20261015, Step 3). It owns the one linear
/// snapshot timeline for the entire prompt <see cref="RichTextBox"/> — spanning host text AND every
/// embedded region — and is the ONLY component that drives <see cref="PromptUndoManager"/>'s
/// capture/apply engine in response to a user Ctrl+Z/Ctrl+Y. It holds a list of
/// <see cref="PromptSnapshot"/> entries plus an index into that list marking the live document
/// state; <see cref="PushSnapshot"/> appends a new committed state (truncating any redo tail),
/// <see cref="Undo"/>/<see cref="Redo"/> walk the index and re-apply the engine.
/// <para>
/// Routing (which keys reach this class) and native/per-region undo suppression are wired in
/// <c>MainWindow</c>. Rapid-edit coalescing and clear-on-send are Step 4
/// (PROMPTUNDO-20261015-004); this class pushes one entry per observed edit boundary and exposes
/// <see cref="Clear"/> for Step 4 to call on send.
/// </para>
/// </summary>
public sealed class PromptUndoHistory
{
    private readonly RichTextBox _prompt;
    private readonly List<PromptSnapshot> _entries = new();

    // Index of the snapshot representing the CURRENT live document state. -1 only before the first
    // baseline is captured.
    private int _index = -1;

    // Re-entrancy guard: true while ApplySnapshot is rebuilding the document. Any TextChanged /
    // region-edit notification that fires during a restore must NOT be recorded as a new user edit.
    private bool _isApplying;

    public PromptUndoHistory(RichTextBox prompt)
        => _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));

    /// True while <see cref="Undo"/>/<see cref="Redo"/> are re-applying a snapshot; edit hooks must
    /// consult this and skip <see cref="PushSnapshot"/> so a restore never pollutes the timeline.
    public bool IsApplying => _isApplying;

    public bool CanUndo => _index > 0;

    public bool CanRedo => _index >= 0 && _index < _entries.Count - 1;

    /// Number of entries currently in the timeline (diagnostics/tests).
    public int Count => _entries.Count;

    /// Current index into the timeline (diagnostics/tests).
    public int Index => _index;

    /// <summary>
    /// Seeds the timeline with the prompt's current state as the one baseline entry, discarding any
    /// prior history. Call once when the prompt is first wired, and (Step 4) on clear-on-send.
    /// </summary>
    public void CaptureBaseline()
    {
        _entries.Clear();
        _entries.Add(PromptUndoManager.CaptureSnapshot(_prompt));
        _index = 0;
    }

    /// <summary>
    /// Records the prompt's CURRENT state as the newest committed entry. If the serialized text is
    /// unchanged from the live entry (e.g. a pure caret/selection move), the existing entry's
    /// selection is refreshed in place instead of growing the timeline. Any redo tail ahead of the
    /// current index is discarded (a fresh edit forks the future). No-ops while a restore is in
    /// flight.
    /// </summary>
    public void PushSnapshot()
    {
        if (_isApplying)
            return;

        if (_index < 0)
        {
            CaptureBaseline();
            return;
        }

        var snapshot = PromptUndoManager.CaptureSnapshot(_prompt);

        if (string.Equals(_entries[_index].SerializedText, snapshot.SerializedText, StringComparison.Ordinal))
        {
            // Text identical — only the caret/anchor may have moved. Keep the single entry and
            // refresh its selection so a later undo restores the latest caret, without adding a
            // redundant history step.
            _entries[_index] = snapshot;
            return;
        }

        if (_index < _entries.Count - 1)
            _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);

        _entries.Add(snapshot);
        _index = _entries.Count - 1;
    }

    /// Moves one step back in the timeline and restores that snapshot. Returns false (no-op) at the
    /// oldest entry.
    public bool Undo()
    {
        if (!CanUndo)
            return false;

        _index--;
        Apply(_entries[_index]);
        return true;
    }

    /// Moves one step forward in the timeline and restores that snapshot. Returns false (no-op) at
    /// the newest entry.
    public bool Redo()
    {
        if (!CanRedo)
            return false;

        _index++;
        Apply(_entries[_index]);
        return true;
    }

    /// Clears the timeline and re-seeds it with the current prompt state (Step 4 calls this on send
    /// so a sent prompt starts a fresh empty history).
    public void Clear() => CaptureBaseline();

    private void Apply(PromptSnapshot snapshot)
    {
        _isApplying = true;
        try
        {
            PromptUndoManager.ApplySnapshot(_prompt, snapshot);
        }
        finally
        {
            _isApplying = false;
        }
    }
}
