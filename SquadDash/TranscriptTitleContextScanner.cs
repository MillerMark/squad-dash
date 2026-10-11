using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SquadDash;

internal static class TranscriptTitleContextScanner
{
    internal static (string? Intent, DateTimeOffset? Timestamp) Scan(
        TranscriptThreadState thread,
        IEnumerable<ToolTranscriptEntry> toolEntries,
        ScrollViewer? scrollViewer)
    {
        (string? Text, DateTimeOffset? Timestamp) visible = default, above = default;
        var bestY = double.MinValue;
        var bestAboveBottom = double.MinValue;

        void Consider(string? text, DateTimeOffset timestamp, double top = 0, double bottom = 0)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (scrollViewer is null)
            {
                if (!visible.Timestamp.HasValue || timestamp > visible.Timestamp.Value)
                    visible = (text, timestamp);
            }
            else if (top < scrollViewer.ViewportHeight && bottom > 0)
            {
                if (top > bestY || (top == bestY && timestamp > visible.Timestamp))
                {
                    bestY = top;
                    visible = (text, timestamp);
                }
            }
            else if (bottom <= 0 && (bottom > bestAboveBottom ||
                     (bottom == bestAboveBottom && timestamp > above.Timestamp)))
            {
                bestAboveBottom = bottom;
                above = (text, timestamp);
            }
        }

        foreach (var toolEntry in toolEntries)
        {
            if (toolEntry.Turn.OwnerThread != thread || toolEntry.Descriptor.ToolName != "report_intent")
                continue;

            var text = toolEntry.Descriptor.DisplayText ?? toolEntry.Descriptor.Intent;
            if (scrollViewer is null)
            {
                Consider(text, toolEntry.StartedAt);
                continue;
            }

            // Collapsed tooling groups still represent their intents at the group header.
            var target = toolEntry.Expander.IsVisible ? toolEntry.Expander
                : toolEntry.ThinkingBlock.Expander.IsVisible ? toolEntry.ThinkingBlock.Expander : null;
            if (target is null || !scrollViewer.IsAncestorOf(target))
                continue;

            var position = target.TranslatePoint(new Point(), scrollViewer);
            Consider(text, toolEntry.StartedAt, position.Y, position.Y + target.ActualHeight);
        }

        foreach (var prompt in thread.PromptParagraphs)
        {
            if (string.IsNullOrWhiteSpace(prompt.Title))
                continue;

            if (scrollViewer is null)
            {
                Consider(prompt.Title, prompt.Timestamp);
                continue;
            }

            var paragraph = prompt.Paragraph;
            if (!paragraph.ContentStart.HasValidLayout || !paragraph.ContentEnd.HasValidLayout ||
                scrollViewer.Content is not FrameworkElement renderScope)
                continue;

            var start = paragraph.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            var end = paragraph.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            if (start.IsEmpty || end.IsEmpty)
                continue;

            // TextPointer rectangles are relative to the document's rendering surface.
            var top = renderScope.TranslatePoint(start.TopLeft, scrollViewer).Y;
            var bottom = renderScope.TranslatePoint(end.BottomLeft, scrollViewer).Y;
            Consider(prompt.Title, prompt.Timestamp, top, bottom);
        }

        return visible.Text is not null ? visible : above;
    }
}
