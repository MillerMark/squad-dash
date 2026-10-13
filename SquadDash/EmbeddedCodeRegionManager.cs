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
                    break;
            }
        }

        // Place the caret at the end of the document so typing continues naturally after
        // the just-created region.
        rtb.CaretPosition = rtb.Document.ContentEnd;
    }
}
