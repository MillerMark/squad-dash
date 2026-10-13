using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SquadDash;

/// <summary>
/// Detects completed triple-backtick fences in a plain-text <see cref="RichTextBox"/> (e.g.
/// <c>PromptTextBox</c>) and converts them in place into collapsible
/// <see cref="EmbeddedCodeRegionBlock"/> regions, hosted via <see cref="BlockUIContainer"/>.
/// <para>
/// Operates on a segment model (alternating plain-text runs and existing/new region blocks)
/// rather than raw character offsets, so pre-existing regions are preserved by reference
/// (same control instance, same collapse state) across a conversion, and only the segment
/// containing the newly-completed fence is rebuilt.
/// </para>
/// </summary>
internal static class EmbeddedCodeRegionManager
{
    private abstract class Segment { }

    private sealed class TextSegment : Segment
    {
        public required string Text;
    }

    private sealed class RegionSegment : Segment
    {
        /// Non-null when preserving an already-converted region unchanged.
        public BlockUIContainer? ExistingContainer;
        /// Non-null (together with NewLanguage) when building a brand-new region.
        public string? NewContent;
        public string? NewLanguage;
        /// When true, the region's content box should receive keyboard focus (with the
        /// caret placed in its — necessarily empty — content) immediately after render.
        /// Used only by the instant-open-fence path (<see cref="TryConvertOpenFence"/>);
        /// the complete-fence path never sets this.
        public bool FocusContentAfterRender;
    }

    /// <summary>
    /// Attempts a single fence-to-region conversion. Returns <see langword="true"/> if a
    /// fence was found and converted (caller should loop until this returns
    /// <see langword="false"/> to catch multiple fences, e.g. from a multi-block paste).
    /// </summary>
    internal static bool TryConvertNextFence(RichTextBox rtb)
    {
        var segments = ExtractSegments(rtb);

        for (var s = 0; s < segments.Count; s++)
        {
            if (segments[s] is not TextSegment textSeg) continue;

            var lines = textSeg.Text.Split('\n');
            var fence = EmbeddedFenceDetector.FindFirstFence(lines);
            if (fence is null) continue;

            var span = fence.Value;
            var before = string.Join("\n", lines.Take(span.OpenLineIndex));
            var after = string.Join("\n", lines.Skip(span.CloseLineIndex + 1));
            var content = span.ExtractContent(lines);

            var replacement = new List<Segment>();
            if (before.Length > 0) replacement.Add(new TextSegment { Text = before });
            replacement.Add(new RegionSegment { NewContent = content, NewLanguage = span.Language });
            if (after.Length > 0) replacement.Add(new TextSegment { Text = after });

            segments.RemoveAt(s);
            segments.InsertRange(s, replacement);

            RenderSegments(rtb, segments);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to convert a bare, just-completed *opening* fence marker (``` or
    /// ```&lt;language&gt; on its own line, with no closing ``` yet anywhere after it) into an
    /// empty, expanded <see cref="EmbeddedCodeRegionBlock"/> whose content box immediately
    /// receives keyboard focus — so the user never sees raw "```" sitting in the prompt
    /// waiting for a close; they type straight into the region instead. Unlike
    /// <see cref="TryConvertNextFence"/> this does not require (or wait for) a closing fence.
    /// Returns <see langword="false"/> if no such bare opening marker is present.
    /// </summary>
    internal static bool TryConvertOpenFence(RichTextBox rtb)
    {
        var segments = ExtractSegments(rtb);

        for (var s = 0; s < segments.Count; s++)
        {
            if (segments[s] is not TextSegment textSeg) continue;

            var lines = textSeg.Text.Split('\n');
            var openFence = EmbeddedFenceDetector.FindOpenFenceWithNoClose(lines);
            if (openFence is null) continue;

            var span = openFence.Value;
            var before = string.Join("\n", lines.Take(span.LineIndex));
            var after = string.Join("\n", lines.Skip(span.LineIndex + 1));

            var replacement = new List<Segment>();
            if (before.Length > 0) replacement.Add(new TextSegment { Text = before });
            replacement.Add(new RegionSegment
            {
                NewContent = "",
                NewLanguage = span.Language,
                FocusContentAfterRender = true,
            });
            if (after.Length > 0) replacement.Add(new TextSegment { Text = after });

            segments.RemoveAt(s);
            segments.InsertRange(s, replacement);

            RenderSegments(rtb, segments);
            return true;
        }

        return false;
    }

    private static List<Segment> ExtractSegments(RichTextBox rtb)
    {
        var segments = new List<Segment>();
        StringBuilder? currentText = null;

        void FlushText()
        {
            if (currentText is not null)
            {
                segments.Add(new TextSegment { Text = currentText.ToString() });
                currentText = null;
            }
        }

        foreach (var block in rtb.Document.Blocks.ToList())
        {
            if (block is Paragraph para)
            {
                currentText ??= new StringBuilder();
                if (currentText.Length > 0) currentText.Append('\n');
                currentText.Append(RichTextBoxExtensions.GetParagraphText(para));
            }
            else if (block is BlockUIContainer { Child: EmbeddedCodeRegionBlock } buc)
            {
                FlushText();
                segments.Add(new RegionSegment { ExistingContainer = buc });
            }
            else
            {
                FlushText();
            }
        }
        FlushText();

        return segments;
    }

    private static void RenderSegments(RichTextBox rtb, List<Segment> segments)
    {
        // Capture the caret's plain-text offset before rebuilding. Conversion never changes
        // the overall plain-text content (GetPlainText() before/after is identical — a raw
        // fence becomes a region that serializes back to the exact same fenced text), so the
        // same offset maps back to the equivalent position after rebuild: e.g. right after
        // the newly-created region if that's where the user's caret actually was (immediately
        // after typing/pasting the closing fence), rather than always jumping to document end.
        var caretOffset = rtb.GetCaretOffset();

        var undoWasEnabled = rtb.IsUndoEnabled;
        if (undoWasEnabled)
            rtb.IsUndoEnabled = false;

        EmbeddedCodeRegionBlock? regionToFocus = null;

        try
        {
            rtb.Document.Blocks.Clear();
            foreach (var segment in segments)
            {
                switch (segment)
                {
                    case TextSegment textSeg:
                        foreach (var line in textSeg.Text.Split('\n'))
                            rtb.Document.Blocks.Add(new Paragraph(new Run(line)) { Margin = new Thickness(0) });
                        break;

                    case RegionSegment { ExistingContainer: { } existing }:
                        rtb.Document.Blocks.Add(existing);
                        break;

                    case RegionSegment regionSeg:
                        var region = EmbeddedCodeRegionBlock.Build(regionSeg.NewContent ?? "", regionSeg.NewLanguage ?? "");
                        rtb.Document.Blocks.Add(new BlockUIContainer(region) { Margin = new Thickness(0) });
                        if (regionSeg.FocusContentAfterRender)
                            regionToFocus = region;
                        break;
                }
            }

            rtb.CaretPosition = ResolveCaretPointerAfterRebuild(rtb, caretOffset);
        }
        finally
        {
            if (undoWasEnabled)
                rtb.IsUndoEnabled = true;
        }

        // Done after the undo-state finally block (and outside the try) so a focus-transfer
        // failure (e.g. no live visual tree in a headless test) can't leave IsUndoEnabled
        // incorrectly restored.
        regionToFocus?.FocusContent();
    }

    /// Resolves the <see cref="TextPointer"/> to assign to <c>RichTextBox.CaretPosition</c>
    /// for a given plain-text offset, nudging a pointer that lands exactly at a region's
    /// own ElementStart/ElementEnd boundary onto the neighboring Paragraph's
    /// ContentStart/ContentEnd instead. WPF does not reliably keep the caret at a
    /// BlockUIContainer's own element boundary when that container hosts a focusable child
    /// control (the region's content TextBox) — assigning there can cause the caret to
    /// normalize elsewhere (e.g. document start), which is what made the caret look like it
    /// landed on the wrong line around a region. This nudge is local to caret *assignment*
    /// here; it intentionally does not change <see cref="RichTextBoxExtensions.GetTextPointerAt"/>
    /// or <see cref="RichTextBoxExtensions.GetCharOffset"/>, which must stay symmetric with
    /// each other for offset math elsewhere (selection, etc.) to remain correct.
    private static TextPointer ResolveCaretPointerAfterRebuild(RichTextBox rtb, int caretOffset)
    {
        var pointer = rtb.GetTextPointerAt(caretOffset);
        var blocks = rtb.Document.Blocks.ToList();

        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is not BlockUIContainer { Child: EmbeddedCodeRegionBlock } region) continue;

            if (pointer.CompareTo(region.ElementStart) == 0 && i > 0 && blocks[i - 1] is Paragraph prev)
                return prev.ContentEnd;
            if (pointer.CompareTo(region.ElementEnd) == 0 && i + 1 < blocks.Count && blocks[i + 1] is Paragraph next)
                return next.ContentStart;
        }

        return pointer;
    }
}
