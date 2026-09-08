using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SquadDash;

/// <summary>
/// Replaces the durable Markdown checkbox lines in an approval Inbox message with live WPF
/// controls. The Markdown remains useful in previews and persisted history; only the open Inbox
/// window receives interactive controls.
/// </summary>
internal static class InboxApprovalChecklistPresenter
{
    internal static int Attach(
        FlowDocument document,
        IReadOnlyList<PlanHumanReviewChecklistItem> items,
        Action<PlanHumanReviewChecklistItem, bool> onSelectionChanged)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(onSelectionChanged);

        var remaining = items.ToList();
        var attached = 0;
        foreach (var paragraph in EnumerateParagraphs(document.Blocks).ToArray())
        {
            var text = ReadInlineText(paragraph.Inlines).Trim();
            var item = remaining.FirstOrDefault(candidate =>
                string.Equals(text, $"☐ {candidate.Text}", StringComparison.Ordinal) ||
                string.Equals(text, $"☑ {candidate.Text}", StringComparison.Ordinal));
            if (item is null)
                continue;

            var checkBox = new CheckBox
            {
                Content = item.Text,
                IsChecked = item.IsChecked,
                Tag = item,
                Margin = new Thickness(0, 2, 0, 2),
                VerticalContentAlignment = VerticalAlignment.Top,
            };
            checkBox.SetResourceReference(Control.ForegroundProperty, "ImportantText");
            checkBox.SetResourceReference(Control.FontSizeProperty, "FontSizeBody");
            checkBox.Checked += (_, _) => onSelectionChanged(item, true);
            checkBox.Unchecked += (_, _) => onSelectionChanged(item, false);

            paragraph.Inlines.Clear();
            paragraph.Inlines.Add(new InlineUIContainer(checkBox)
            {
                BaselineAlignment = BaselineAlignment.Center,
            });
            remaining.Remove(item);
            attached++;
        }

        return attached;
    }

    private static string ReadInlineText(InlineCollection inlines) =>
        string.Concat(inlines.Cast<Inline>().Select(inline => inline switch
        {
            Run run => run.Text,
            LineBreak => "\n",
            Span span => ReadInlineText(span.Inlines),
            _ => string.Empty,
        }));

    private static IEnumerable<Paragraph> EnumerateParagraphs(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph)
                yield return paragraph;
            else if (block is List list)
            {
                foreach (var item in list.ListItems)
                foreach (var nested in EnumerateParagraphs(item.Blocks))
                    yield return nested;
            }
            else if (block is Section section)
            {
                foreach (var nested in EnumerateParagraphs(section.Blocks))
                    yield return nested;
            }
        }
    }
}
