using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
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
    internal const double ChecklistTopSpacingScale = 0.50;

    internal static int Attach(
        FlowDocument document,
        IReadOnlyList<PlanHumanReviewChecklistItem> items,
        Action<PlanHumanReviewChecklistItem, PlanHumanReviewItemResponse> onResponseChanged,
        double fontSize,
        bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(onResponseChanged);

        var remaining = items.ToList();
        var checklistLists = new HashSet<List>();
        var checklistParagraphs = new HashSet<Paragraph>();
        var attached = 0;
        foreach (var (paragraph, parentList) in MarkdownVisibleContent.Documents(document, visibleOnly: false)
                     .SelectMany(entry => EnumerateParagraphs(entry.Document.Blocks)).ToArray())
        {
            var text = MarkdownFlowDocumentBuilder.TryGetListItemSourceText(paragraph, out var sourceText)
                ? sourceText.Trim()
                : ReadInlineText(paragraph.Inlines).Trim();
            var item = remaining.FirstOrDefault(candidate =>
                string.Equals(text, $"☐ {candidate.Text}", StringComparison.Ordinal) ||
                string.Equals(text, $"☑ {candidate.Text}", StringComparison.Ordinal) ||
                string.Equals(text, $"? {candidate.Text}", StringComparison.Ordinal));
            if (item is null)
                continue;

            var control = BuildChecklistItemControl(
                item,
                response => onResponseChanged(item, response),
                fontSize,
                isEnabled);

            paragraph.Inlines.Clear();
            paragraph.Inlines.Add(new InlineUIContainer(control)
            {
                BaselineAlignment = BaselineAlignment.Center,
            });
            if (parentList is not null)
                checklistLists.Add(parentList);
            checklistParagraphs.Add(paragraph);
            remaining.Remove(item);
            attached++;
        }

        // Markdown renders checklist state glyphs as a bulleted list. Once the glyph becomes a
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

    private static FrameworkElement BuildChecklistItemControl(
        PlanHumanReviewChecklistItem item,
        Action<PlanHumanReviewItemResponse> onResponseChanged,
        double fontSize,
        bool isEnabled)
    {
        var checklistFontSize = fontSize * ChecklistFontScale;
        var response = PlanHumanReviewItemResponse.FromItem(item).Normalized();
        var persistedResponse = response;
        var suppressNoteUpdate = false;

        var root = new StackPanel
        {
            Margin = new Thickness(
                0,
                2 + (checklistFontSize * ChecklistTopSpacingScale),
                0,
                4),
            Opacity = isEnabled ? 1 : 0.5,
        };

        var itemText = new TextBlock
        {
            Text = item.Text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = checklistFontSize,
            Margin = new Thickness(8, 0, 0, 3),
        };
        itemText.SetResourceReference(TextBlock.ForegroundProperty, "ControlText");
        AutomationProperties.SetName(itemText, "Approval question");
        root.Children.Add(itemText);

        var choices = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 0, 0, 2),
        };
        root.Children.Add(choices);

        var yesChoice = BuildChoice(
            "Yes",
            checklistFontSize,
            Application.Current?.TryFindResource("PlanApprovalResolvedCheck") as Viewbox ?? new Viewbox(),
            isEnabled);
        var noChoice = BuildChoice(
            "No",
            checklistFontSize,
            Application.Current?.TryFindResource("TaskFailed") as Viewbox ?? new Viewbox(),
            isEnabled);
        choices.Children.Add(yesChoice.CheckBox);
        choices.Children.Add(noChoice.CheckBox);

        var noteRow = new Grid { Margin = new Thickness(8, 1, 0, 0) };
        noteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        noteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var noteLabel = new TextBlock
        {
            Text = "Notes",
            FontSize = checklistFontSize - 1,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 6, 0),
        };
        noteLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        noteRow.Children.Add(noteLabel);

        var noteBox = new TextBox
        {
            Text = response.Note ?? string.Empty,
            MinHeight = 24,
            MaxHeight = 84,
            AcceptsReturn = false,
            Focusable = true,
            IsReadOnly = !isEnabled,
            IsEnabled = isEnabled,
            IsTabStop = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = checklistFontSize - 1,
            Padding = new Thickness(6, 3, 6, 3),
            BorderThickness = new Thickness(1),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = ToolTipHelper.MakeThemedToolTip("Optional note. Double-tap Ctrl while focused to dictate."),
        };
        TranscriptApprovalCardBuilder.ConfigureNoteEditor(noteBox);
        noteBox.SetResourceReference(TextBox.BackgroundProperty, "ApprovalFieldSurface");
        noteBox.SetResourceReference(TextBox.ForegroundProperty, "ImportantText");
        noteBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        AutomationProperties.SetName(noteBox, $"Notes for {item.Text}");
        Grid.SetColumn(noteBox, 1);
        noteRow.Children.Add(noteBox);
        root.Children.Add(noteRow);

        void UpdateSemanticText()
        {
            var normalizedNote = PlanHumanReviewItemResponse.NormalizeNote(noteBox.Text);
            var semantic = $"[{PlanHumanReviewAnswer.Display(response.IsApproved)}] {item.Text}";
            if (!string.IsNullOrWhiteSpace(normalizedNote))
                semantic += Environment.NewLine + "Notes: " + normalizedNote;
            MarkdownVisibleContent.SetSemanticText(root, semantic);
        }

        void RefreshChoiceVisuals()
        {
            yesChoice.CheckBox.IsChecked = response.IsApproved == true;
            noChoice.CheckBox.IsChecked = response.IsApproved == false;
            yesChoice.Icon.Visibility = response.IsApproved == true ? Visibility.Visible : Visibility.Hidden;
            noChoice.Icon.Visibility = response.IsApproved == false ? Visibility.Visible : Visibility.Hidden;
            yesChoice.Border.Opacity = response.IsApproved is null or true ? 1 : 0.55;
            noChoice.Border.Opacity = response.IsApproved is null or false ? 1 : 0.55;
            UpdateSemanticText();
        }

        void SetAnswer(bool? value, bool notify)
        {
            response = response with
            {
                IsApproved = response.IsApproved == value ? null : value,
                Note = PlanHumanReviewItemResponse.NormalizeNote(noteBox.Text),
            };

            RefreshChoiceVisuals();
            if (!notify)
                return;

            persistedResponse = response.Normalized();
            onResponseChanged(persistedResponse);
        }

        yesChoice.CheckBox.Click += (_, _) => SetAnswer(true, notify: true);
        noChoice.CheckBox.Click += (_, _) => SetAnswer(false, notify: true);
        noteBox.TextChanged += (_, _) =>
        {
            if (suppressNoteUpdate) return;
            response = response with { Note = noteBox.Text };
            UpdateSemanticText();
        };
        noteBox.LostFocus += (_, _) =>
        {
            var normalized = PlanHumanReviewItemResponse.NormalizeNote(noteBox.Text);
            if (string.Equals(normalized, persistedResponse.Note, StringComparison.Ordinal))
                return;

            suppressNoteUpdate = true;
            noteBox.Text = normalized ?? string.Empty;
            suppressNoteUpdate = false;
            response = response with { Note = normalized };
            persistedResponse = response.Normalized();
            UpdateSemanticText();
            onResponseChanged(persistedResponse);
        };

        RefreshChoiceVisuals();
        return root;
    }

    private static (CheckBox CheckBox, Border Border, Viewbox Icon) BuildChoice(
        string label,
        double fontSize,
        Viewbox icon,
        bool isEnabled)
    {
        var boxSize = Math.Round(fontSize * 1.2);
        icon.Visibility = Visibility.Hidden;
        icon.Margin = new Thickness(label == "No" ? 3 : 2);

        var iconHost = new Border
        {
            Width = boxSize,
            Height = boxSize,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(2),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
            Child = icon,
        };
        iconHost.SetResourceReference(Border.BorderBrushProperty, "CheckBoxBorder");

        var text = new TextBlock
        {
            Text = label,
            FontSize = fontSize - 1,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "ControlText");

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0),
        };
        content.Children.Add(iconHost);
        content.Children.Add(text);

        var checkBox = new CheckBox
        {
            Content = content,
            IsEnabled = isEnabled,
            Cursor = isEnabled ? Cursors.Hand : Cursors.Arrow,
            Template = BuildContentOnlyTemplate(),
            Margin = new Thickness(0, 0, 16, 0),
            ToolTip = ToolTipHelper.MakeThemedToolTip(label == "Yes"
                ? "Mark this item approved."
                : "Mark this item unapproved."),
        };
        AutomationProperties.SetName(checkBox, label);
        return (checkBox, iconHost, icon);
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
