using System.Threading;
using System.Windows.Controls;
using System.Windows.Documents;
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

        Assert.Multiple(() =>
        {
            Assert.That(attached, Is.EqualTo(1));
            Assert.That(checkbox.Content, Is.EqualTo(item.Text));
            Assert.That(checkbox.FontSize, Is.EqualTo(19));
            Assert.That(changedItem, Is.EqualTo(item));
            Assert.That(changedValue, Is.True);
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
