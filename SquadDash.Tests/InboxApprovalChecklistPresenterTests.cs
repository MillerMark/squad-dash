using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class InboxApprovalChecklistPresenterTests
{
    [Test, Apartment(ApartmentState.STA)]
    public void Attach_ReplacesPersistedGlyphWithLiveYesNoControlsNotesAndReportsChanges()
    {
        var document = MarkdownFlowDocumentBuilder.BuildInbox(
            """
            **Manual test instructions**
            1. **Set up:** Build the prepared package.
            2. **Open and perform:** Open the sample and invoke the feature.
            3. **Observe:** The new result appears.

            - ☐ Does the new result appear?

            4. **Clean up:** Remove the temporary state.
            """, 14);
        var item = new PlanHumanReviewChecklistItem(
            "gate-1", "new-result", "new-result", "Does the new result appear?",
            PlanHumanReviewChecklist.UncommittedCandidate, false, false, null);
        PlanHumanReviewChecklistItem? changedItem = null;
        PlanHumanReviewItemResponse? changedResponse = null;

        var attached = InboxApprovalChecklistPresenter.Attach(
            document,
            [item],
            (changed, response) => { changedItem = changed; changedResponse = response; },
            fontSize: 19);
        var root = FindChecklistRoot(document.Blocks)!;
        var boxes = FindCheckBoxes(root).ToArray();
        var choices = root.Children.OfType<StackPanel>()
            .Single(panel => FindCheckBoxes(panel).Count() == 2);
        var noteRow = root.Children.OfType<Grid>().Single();
        var noteBox = noteRow.Children.OfType<TextBox>().Single();
        var notePlaceholder = noteRow.Children.OfType<TextBlock>()
            .Single(text => text.Text == "(notes)");
        boxes[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var expectedFontSize = 19 * InboxApprovalChecklistPresenter.ChecklistFontScale;

        Assert.Multiple(() =>
        {
            Assert.That(attached, Is.EqualTo(1));
            Assert.That(changedItem, Is.EqualTo(item));
            Assert.That(changedResponse?.IsApproved, Is.True);
            Assert.That(root.Children.OfType<TextBlock>().Single().FontSize, Is.EqualTo(expectedFontSize));
            Assert.That(boxes, Has.Length.EqualTo(2));
            Assert.That(boxes[0].IsChecked, Is.True);
            Assert.That(boxes[1].IsChecked, Is.False);
            Assert.That(noteBox, Is.Not.Null);
            Assert.That(choices.Margin.Top, Is.EqualTo(6));
            Assert.That(noteRow.Margin.Top, Is.EqualTo(4));
            Assert.That(noteBox.SnapsToDevicePixels, Is.True);
            Assert.That(noteBox.UseLayoutRounding, Is.True);
            Assert.That(TextOptions.GetTextFormattingMode(noteBox), Is.EqualTo(TextFormattingMode.Display));
            Assert.That(((TextBlock)((ToolTip)noteBox.ToolTip).Content).Text,
                Is.EqualTo(TranscriptApprovalCardBuilder.ReviewItemNoteToolTipText));
            Assert.That(notePlaceholder.Visibility, Is.EqualTo(Visibility.Visible));
        });
        noteBox.Text = "Needs follow-up";
        Assert.That(notePlaceholder.Visibility, Is.EqualTo(Visibility.Collapsed));

        var yesContent = (StackPanel)boxes[0].Content;
        var yesBorder = yesContent.Children.OfType<Border>().Single();
        var yesIcon = FindChoiceIcon(boxes[0]);
        var noContent = (StackPanel)boxes[1].Content;
        var noBorder = noContent.Children.OfType<Border>().Single();
        var noIcon = FindChoiceIcon(boxes[1]);
        var list = document.Blocks.OfType<List>()
            .Single(candidate => candidate.MarkerStyle == TextMarkerStyle.None);
        var orderedLists = document.Blocks.OfType<List>()
            .Where(candidate => candidate.MarkerStyle == TextMarkerStyle.Decimal)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(root.Children.OfType<TextBlock>().Single().Text, Is.EqualTo(item.Text));
            Assert.That(root.Children.OfType<TextBlock>().Single().TextWrapping, Is.EqualTo(TextWrapping.Wrap));
            Assert.That(yesBorder.Width, Is.EqualTo(Math.Round(expectedFontSize * 1.2)));
            Assert.That(yesBorder.Height, Is.EqualTo(Math.Round(expectedFontSize * 1.2)));
            Assert.That(yesBorder.BorderThickness, Is.EqualTo(new Thickness(1.5)));
            Assert.That(yesBorder.CornerRadius, Is.EqualTo(new CornerRadius(2)));
            Assert.That(yesIcon.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(noIcon.Margin,
                Is.EqualTo(TranscriptApprovalCardBuilder.GetReviewChoiceIconMargin("No", noBorder.Width)));
            Assert.That(noIcon.Margin.Left, Is.LessThan(2));
            Assert.That(root.Margin.Top,
                Is.EqualTo(2 + (expectedFontSize * InboxApprovalChecklistPresenter.ChecklistTopSpacingScale)));
            Assert.That(list.MarkerStyle, Is.EqualTo(TextMarkerStyle.None));
            Assert.That(orderedLists, Has.Length.EqualTo(2));
            Assert.That(orderedLists[0].StartIndex, Is.EqualTo(1));
            Assert.That(orderedLists[1].StartIndex, Is.EqualTo(4));
            Assert.That(
                ((Paragraph)orderedLists[0].ListItems.First().Blocks.FirstBlock).Margin.Top,
                Is.EqualTo(1 + (14 * MarkdownFlowDocumentBuilder.OrderedListItemTopSpacingScale)));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Attach_LongChecklistTextWrapsWithinTheAvailableWidth()
    {
        const string question =
            "Does the intentionally long review question wrap inside a narrow Inbox message instead of forcing it wider?";
        var document = MarkdownFlowDocumentBuilder.BuildInbox($"- ☐ {question}", 14);
        var item = new PlanHumanReviewChecklistItem(
            "gate-1", "wrap", "wrap", question,
            PlanHumanReviewChecklist.UncommittedCandidate, false, false, null);

        InboxApprovalChecklistPresenter.Attach(document, [item], (_, _) => { }, fontSize: 14);
        var root = FindChecklistRoot(document.Blocks)!;
        var expectedFontSize = 14 * InboxApprovalChecklistPresenter.ChecklistFontScale;

        root.Measure(new Size(180, double.PositiveInfinity));
        var label = root.Children.OfType<TextBlock>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(root.DesiredSize.Width, Is.LessThanOrEqualTo(180));
            Assert.That(label.FontSize, Is.EqualTo(expectedFontSize));
            Assert.That(label.DesiredSize.Height, Is.GreaterThan(expectedFontSize * 2));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Attach_PreviouslyConfirmedItem_StartsUnansweredUntilReconfirmed()
    {
        var application = Application.Current ?? new Application();
        var approvalBrush = Brushes.LimeGreen;
        application.Resources["PlanApprovalResolved"] = approvalBrush;
        const string question = "Does the corrected result still appear?";
        var document = MarkdownFlowDocumentBuilder.BuildInbox($"- ? {question}", 14);
        var item = new PlanHumanReviewChecklistItem(
            "gate-1", "corrected-result", "corrected-result", question,
            "abcdef123456", false, true, "abcdef123456", ReviewTarget: "target-2");

        InboxApprovalChecklistPresenter.Attach(document, [item], (_, _) => { }, fontSize: 14);
        var boxes = FindCheckBoxes(FindChecklistRoot(document.Blocks)!).ToArray();
        var yesMarker = FindChoiceMarker(boxes[0]);
        var yesIcon = FindChoiceIcon(boxes[0]);

        Assert.Multiple(() =>
        {
            Assert.That(boxes[0].IsChecked, Is.True);
            Assert.That(boxes[1].IsChecked, Is.False);
            Assert.That(yesMarker.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(yesMarker.Foreground, Is.SameAs(approvalBrush));
            Assert.That(yesIcon.Visibility, Is.EqualTo(Visibility.Hidden));
        });

        boxes[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        Assert.Multiple(() =>
        {
            Assert.That(boxes[0].IsChecked, Is.True);
            Assert.That(boxes[1].IsChecked, Is.False);
            Assert.That(yesMarker.Visibility, Is.EqualTo(Visibility.Hidden));
            Assert.That(yesIcon.Visibility, Is.EqualTo(Visibility.Visible));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Attach_PreviouslyRejectedItem_ShowsQuestionMarkInNoChoiceUntilReconfirmed()
    {
        var application = Application.Current ?? new Application();
        var failureBrush = Brushes.IndianRed;
        application.Resources["TaskFailedFill"] = failureBrush;
        const string question = "Does the result still fail?";
        var document = MarkdownFlowDocumentBuilder.BuildInbox($"- ? {question}", 14);
        var item = new PlanHumanReviewChecklistItem(
            "gate-1", "corrected-result", "corrected-result", question,
            "abcdef123456", false, false, "abcdef123456",
            ReviewTarget: "target-2",
            PreviousAnswer: PlanHumanReviewAnswer.No);

        InboxApprovalChecklistPresenter.Attach(document, [item], (_, _) => { }, fontSize: 14);
        var boxes = FindCheckBoxes(FindChecklistRoot(document.Blocks)!).ToArray();
        var noMarker = FindChoiceMarker(boxes[1]);
        var noIcon = FindChoiceIcon(boxes[1]);

        Assert.Multiple(() =>
        {
            Assert.That(boxes[0].IsChecked, Is.False);
            Assert.That(boxes[1].IsChecked, Is.True);
            Assert.That(noMarker.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(noMarker.Foreground, Is.SameAs(failureBrush));
            Assert.That(noIcon.Visibility, Is.EqualTo(Visibility.Hidden));
        });

        boxes[1].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        Assert.Multiple(() =>
        {
            Assert.That(boxes[0].IsChecked, Is.False);
            Assert.That(boxes[1].IsChecked, Is.True);
            Assert.That(noMarker.Visibility, Is.EqualTo(Visibility.Hidden));
            Assert.That(noIcon.Visibility, Is.EqualTo(Visibility.Visible));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Attach_MixedMarkdownListKeepsOrdinaryBulletButNotCheckboxBullet()
    {
        const string question = "Does the result appear?";
        var document = MarkdownFlowDocumentBuilder.BuildInbox(
            $"- Supporting instruction\n- ☐ {question}", 14);
        var item = new PlanHumanReviewChecklistItem(
            "gate-1", "mixed-list", "mixed-list", question,
            PlanHumanReviewChecklist.UncommittedCandidate, false, false, null);

        InboxApprovalChecklistPresenter.Attach(document, [item], (_, _) => { }, fontSize: 14);

        var list = document.Blocks.OfType<List>().Single();
        var ordinaryParagraph = (Paragraph)list.ListItems.First().Blocks.FirstBlock;
        var checkboxParagraph = (Paragraph)list.ListItems.Last().Blocks.FirstBlock;
        Assert.Multiple(() =>
        {
            Assert.That(list.MarkerStyle, Is.EqualTo(TextMarkerStyle.None));
            Assert.That(new TextRange(ordinaryParagraph.ContentStart, ordinaryParagraph.ContentEnd).Text.TrimStart(),
                Does.StartWith("• Supporting instruction"));
            Assert.That(checkboxParagraph.Inlines.FirstInline, Is.TypeOf<InlineUIContainer>());
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Attach_FormulaWithMarkdownAsterisksStillBecomesLiveChecklist()
    {
        const string question = "Can you author a*x^2 + b*x + c without entering any GUID?";
        var document = MarkdownFlowDocumentBuilder.BuildInbox($"- ☐ {question}", 14);
        var item = new PlanHumanReviewChecklistItem(
            "gate-1", "formula", "formula", question,
            PlanHumanReviewChecklist.UncommittedCandidate, false, false, null);

        var attached = InboxApprovalChecklistPresenter.Attach(
            document, [item], (_, _) => { }, fontSize: 14);

        var root = FindChecklistRoot(document.Blocks)!;
        var list = document.Blocks.OfType<List>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(attached, Is.EqualTo(1));
            Assert.That(root.Children.OfType<TextBlock>().Single().Text, Is.EqualTo(question));
            Assert.That(list.MarkerStyle, Is.EqualTo(TextMarkerStyle.None));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Attach_NoCanBeSelectedAndSelectedAgainToClear()
    {
        const string question = "Does the result fail?";
        var document = MarkdownFlowDocumentBuilder.BuildInbox($"- ☐ {question}", 14);
        var item = new PlanHumanReviewChecklistItem(
            "gate-1", "failure", "failure", question,
            PlanHumanReviewChecklist.UncommittedCandidate, false, false, null);
        PlanHumanReviewItemResponse? response = null;

        InboxApprovalChecklistPresenter.Attach(document, [item], (_, changed) => response = changed, fontSize: 14);
        var boxes = FindCheckBoxes(FindChecklistRoot(document.Blocks)!).ToArray();

        boxes[1].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Multiple(() =>
        {
            Assert.That(response?.IsApproved, Is.False);
            Assert.That(boxes[0].IsChecked, Is.False);
            Assert.That(boxes[1].IsChecked, Is.True);
        });

        boxes[1].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Multiple(() =>
        {
            Assert.That(response?.IsApproved, Is.Null);
            Assert.That(boxes[0].IsChecked, Is.False);
            Assert.That(boxes[1].IsChecked, Is.False);
        });
    }

    private static StackPanel? FindChecklistRoot(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph)
            {
                var root = paragraph.Inlines.OfType<InlineUIContainer>()
                    .Select(container => container.Child)
                    .OfType<StackPanel>()
                    .FirstOrDefault(panel => FindCheckBoxes(panel).Count() == 2);
                if (root is not null) return root;
            }
            else if (block is List list)
            {
                foreach (var item in list.ListItems)
                {
                    var root = FindChecklistRoot(item.Blocks);
                    if (root is not null) return root;
                }
            }
        }
        return null;
    }

    private static IEnumerable<CheckBox> FindCheckBoxes(DependencyObject root)
    {
        if (root is CheckBox checkBox)
            yield return checkBox;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        foreach (var nested in FindCheckBoxes(child))
            yield return nested;
    }

    private static Viewbox FindChoiceIcon(CheckBox choice)
    {
        var content = (StackPanel)choice.Content;
        var border = content.Children.OfType<Border>().Single();
        var slot = (Grid)border.Child;
        return slot.Children.OfType<Viewbox>().Single();
    }

    private static TextBlock FindChoiceMarker(CheckBox choice)
    {
        var content = (StackPanel)choice.Content;
        var border = content.Children.OfType<Border>().Single();
        var slot = (Grid)border.Child;
        return slot.Children.OfType<TextBlock>().Single();
    }
}
