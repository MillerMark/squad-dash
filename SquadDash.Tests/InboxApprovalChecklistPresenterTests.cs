using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class InboxApprovalChecklistPresenterTests
{
    [Test, Apartment(ApartmentState.STA)]
    public void Attach_ReplacesPersistedGlyphWithLiveCheckboxAndReportsChanges()
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
        bool? changedValue = null;

        var attached = InboxApprovalChecklistPresenter.Attach(
            document,
            [item],
            (changed, value) => { changedItem = changed; changedValue = value; },
            fontSize: 19);
        var checkbox = FindCheckBox(document.Blocks);
        checkbox!.IsChecked = true;
        var expectedFontSize = 19 * InboxApprovalChecklistPresenter.ChecklistFontScale;

        Assert.Multiple(() =>
        {
            Assert.That(attached, Is.EqualTo(1));
            Assert.That(checkbox.FontSize, Is.EqualTo(expectedFontSize));
            Assert.That(checkbox.Content, Is.TypeOf<Grid>());
            Assert.That(changedItem, Is.EqualTo(item));
            Assert.That(changedValue, Is.True);
        });

        var content = (Grid)checkbox.Content;
        var border = content.Children.OfType<Border>().Single();
        var label = content.Children.OfType<TextBlock>().Single();
        var list = document.Blocks.OfType<List>()
            .Single(candidate => candidate.MarkerStyle == TextMarkerStyle.None);
        var orderedLists = document.Blocks.OfType<List>()
            .Where(candidate => candidate.MarkerStyle == TextMarkerStyle.Decimal)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(label.Text, Is.EqualTo(item.Text));
            Assert.That(label.TextWrapping, Is.EqualTo(TextWrapping.Wrap));
            Assert.That(label.FontSize, Is.EqualTo(expectedFontSize));
            Assert.That(border.Width, Is.EqualTo(Math.Round(expectedFontSize * 1.2)));
            Assert.That(border.Height, Is.EqualTo(Math.Round(expectedFontSize * 1.2)));
            Assert.That(border.BorderThickness, Is.EqualTo(new Thickness(1.5)));
            Assert.That(border.CornerRadius, Is.EqualTo(new CornerRadius(2)));
            Assert.That(((Grid)border.Child).Children.OfType<Viewbox>().First().Visibility,
                Is.EqualTo(Visibility.Visible));
            Assert.That(
                checkbox.Margin.Top,
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
        var checkbox = FindCheckBox(document.Blocks)!;
        var expectedFontSize = 14 * InboxApprovalChecklistPresenter.ChecklistFontScale;

        checkbox.Measure(new Size(180, double.PositiveInfinity));
        var label = ((Grid)checkbox.Content).Children.OfType<TextBlock>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(checkbox.DesiredSize.Width, Is.LessThanOrEqualTo(180));
            Assert.That(label.FontSize, Is.EqualTo(expectedFontSize));
            Assert.That(label.DesiredSize.Height, Is.GreaterThan(expectedFontSize * 2));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Attach_PreviouslyConfirmedItem_ShowsQuestionUntilReconfirmed()
    {
        const string question = "Does the corrected result still appear?";
        var document = MarkdownFlowDocumentBuilder.BuildInbox($"- ? {question}", 14);
        var item = new PlanHumanReviewChecklistItem(
            "gate-1", "corrected-result", "corrected-result", question,
            "abcdef123456", false, true, "abcdef123456", ReviewTarget: "target-2");

        InboxApprovalChecklistPresenter.Attach(document, [item], (_, _) => { }, fontSize: 14);
        var checkbox = FindCheckBox(document.Blocks)!;
        var border = ((Grid)checkbox.Content).Children.OfType<Border>().Single();
        var icons = ((Grid)border.Child).Children.OfType<Viewbox>().ToArray();
        var tooltipText = (TextBlock)((ToolTip)checkbox.ToolTip).Content;

        Assert.Multiple(() =>
        {
            Assert.That(icons[0].Visibility, Is.EqualTo(Visibility.Hidden));
            Assert.That(icons[1].Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(tooltipText.Text, Is.EqualTo(PlanHumanReviewChecklist.ReconfirmationToolTip));
        });

        checkbox.IsChecked = true;

        Assert.Multiple(() =>
        {
            Assert.That(icons[0].Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(icons[1].Visibility, Is.EqualTo(Visibility.Hidden));
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

    private static CheckBox? FindCheckBox(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph)
            {
                var checkbox = paragraph.Inlines.OfType<InlineUIContainer>()
                    .Select(container => container.Child)
                    .OfType<CheckBox>()
                    .FirstOrDefault();
                if (checkbox is not null) return checkbox;
            }
            else if (block is List list)
            {
                foreach (var item in list.ListItems)
                {
                    var checkbox = FindCheckBox(item.Blocks);
                    if (checkbox is not null) return checkbox;
                }
            }
        }
        return null;
    }
}
