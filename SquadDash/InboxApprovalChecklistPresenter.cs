using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// Replaces the durable Markdown checkbox lines in an approval Inbox message with live WPF
/// controls. The Markdown remains useful in previews and persisted history; only the open Inbox
/// window receives interactive controls.
/// </summary>
internal static class InboxApprovalChecklistPresenter
{
    internal const double ChecklistFontScale = 1.10;

    internal static int Attach(
        FlowDocument document,
        IReadOnlyList<PlanHumanReviewChecklistItem> items,
        Action<PlanHumanReviewChecklistItem, bool> onSelectionChanged,
        double fontSize,
        bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(onSelectionChanged);

        var remaining = items.ToList();
        var checklistLists = new HashSet<List>();
        var checklistParagraphs = new HashSet<Paragraph>();
        var attached = 0;
        foreach (var (paragraph, parentList) in EnumerateParagraphs(document.Blocks).ToArray())
        {
            var text = ReadInlineText(paragraph.Inlines).Trim();
            var item = remaining.FirstOrDefault(candidate =>
                string.Equals(text, $"☐ {candidate.Text}", StringComparison.Ordinal) ||
                string.Equals(text, $"☑ {candidate.Text}", StringComparison.Ordinal));
            if (item is null)
                continue;

            var checklistFontSize = fontSize * ChecklistFontScale;
            var itemText = new TextBlock
            {
                Text = item.Text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = checklistFontSize,
            };
            itemText.SetResourceReference(TextBlock.ForegroundProperty, "ControlText");

            // Keep this visual in lockstep with the Review Required card: its box is 1.2 em,
            // has the same border geometry, and uses the same themed resolved-check glyph.
            var boxSize = Math.Round(checklistFontSize * 1.2);
            var checkIcon = Application.Current?.TryFindResource("PlanApprovalResolvedCheck") as Viewbox
                ?? new Viewbox();
            checkIcon.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Hidden;
            checkIcon.Margin = new Thickness(2);

            var checkBorder = new Border
            {
                Width = boxSize,
                Height = boxSize,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(2),
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8, 2, 6, 0),
                Child = checkIcon,
            };
            checkBorder.SetResourceReference(Border.BorderBrushProperty, "InputBorder");

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(checkBorder, 0);
            Grid.SetColumn(itemText, 1);
            content.Children.Add(checkBorder);
            content.Children.Add(itemText);

            var checkBox = new CheckBox
            {
                Content = content,
                IsChecked = item.IsChecked,
                Tag = item,
                Margin = new Thickness(0, 2, 0, 2),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top,
                FontSize = checklistFontSize,
                IsEnabled = isEnabled,
                Opacity = isEnabled ? 1 : 0.5,
                Cursor = isEnabled ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
                Template = BuildContentOnlyTemplate(),
                ToolTip = ToolTipHelper.MakeThemedToolTip(
                    "Check this after you personally verify that the statement is true."),
            };
            AutomationProperties.SetName(checkBox, item.Text);
            checkBox.Checked += (_, _) =>
            {
                checkIcon.Visibility = Visibility.Visible;
                onSelectionChanged(item, true);
            };
            checkBox.Unchecked += (_, _) =>
            {
                checkIcon.Visibility = Visibility.Hidden;
                onSelectionChanged(item, false);
            };

            paragraph.Inlines.Clear();
            paragraph.Inlines.Add(new InlineUIContainer(checkBox)
            {
                BaselineAlignment = BaselineAlignment.Center,
            });
            if (parentList is not null)
                checklistLists.Add(parentList);
            checklistParagraphs.Add(paragraph);
            remaining.Remove(item);
            attached++;
        }

        // Markdown renders "- ☐ ..." as a bulleted list. Once the checkbox glyph becomes a
        // real control the list marker is redundant visual noise, while the list margin retains
        // the intended indentation.
        foreach (var list in checklistLists)
        {
            // Preserve ordinary bullets if an AI-authored list happens to mix them with
            // checkpoint rows. WPF only exposes marker style at the whole-list level.
            foreach (var listItem in list.ListItems.Cast<ListItem>().ToArray())
            {
                var paragraphs = EnumerateParagraphs(listItem.Blocks, list)
                    .Select(entry => entry.Paragraph)
                    .ToArray();
                if (paragraphs.Any(checklistParagraphs.Contains))
                    continue;

                var firstParagraph = paragraphs.FirstOrDefault();
                if (firstParagraph?.Inlines.FirstInline is { } firstInline)
                    firstParagraph.Inlines.InsertBefore(firstInline, new Run("• "));
            }
            list.MarkerStyle = TextMarkerStyle.None;
        }

        return attached;
    }

    private static ControlTemplate BuildContentOnlyTemplate()
    {
#pragma warning disable CS0618 // FrameworkElementFactory is the supported code-only template API here.
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
#pragma warning restore CS0618
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Top);
        presenter.SetBinding(
            ContentPresenter.ContentProperty,
            new System.Windows.Data.Binding(nameof(ContentControl.Content))
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent,
            });
        return new ControlTemplate(typeof(CheckBox)) { VisualTree = presenter };
    }

    private static string ReadInlineText(InlineCollection inlines) =>
        string.Concat(inlines.Cast<Inline>().Select(inline => inline switch
        {
            Run run => run.Text,
            LineBreak => "\n",
            Span span => ReadInlineText(span.Inlines),
            _ => string.Empty,
        }));

    private static IEnumerable<(Paragraph Paragraph, List? ParentList)> EnumerateParagraphs(
        BlockCollection blocks,
        List? parentList = null)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph)
                yield return (paragraph, parentList);
            else if (block is List list)
            {
                foreach (var item in list.ListItems)
                foreach (var nested in EnumerateParagraphs(item.Blocks, list))
                    yield return nested;
            }
            else if (block is Section section)
            {
                foreach (var nested in EnumerateParagraphs(section.Blocks, parentList))
                    yield return nested;
            }
        }
    }
}
