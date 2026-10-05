using System.Linq;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Documents;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ButtonCaptionPresentationTests
{
    [Test]
    public void ParseVisibleTextMatchesAndRender_UseSameBoldCaptionRuns()
    {
        const string caption = "**Deploy** app";

        var runs = ButtonCaptionPresentation.Parse(caption);
        var visible = ButtonCaptionPresentation.VisibleText(caption);
        var match = ButtonCaptionPresentation.Matches(caption, "  DEPLOY APP  ");
        var rendered = ButtonCaptionPresentation.Render(caption);

        Assert.Multiple(() =>
        {
            Assert.That(runs.Select(run => (run.Text, run.Bold)),
                Is.EqualTo(new[] { ("Deploy", true), (" app", false) }));
            Assert.That(visible, Is.EqualTo("Deploy app"));
            Assert.That(match, Is.True);
            Assert.That(rendered.Inlines.Count, Is.EqualTo(2));
            Assert.That(rendered.Inlines.FirstInline, Is.TypeOf<Bold>());
            Assert.That(((Run)((Bold)rendered.Inlines.FirstInline!).Inlines.FirstInline!).Text, Is.EqualTo("Deploy"));
            Assert.That(rendered.Inlines.LastInline, Is.TypeOf<Run>());
            Assert.That(((Run)rendered.Inlines.LastInline!).Text, Is.EqualTo(" app"));
        });
    }

    [Test]
    public void ParseAndVisibleText_TreatEscapedAndUnmatchedMarkdownAsLiteral()
    {
        const string caption = @"Escaped \*\*not bold\*\* and **open only";

        var runs = ButtonCaptionPresentation.Parse(caption);
        var visible = ButtonCaptionPresentation.VisibleText(caption);

        Assert.Multiple(() =>
        {
            Assert.That(runs.Count, Is.EqualTo(1));
            Assert.That(runs[0].Bold, Is.False);
            Assert.That(visible, Is.EqualTo("Escaped **not bold** and **open only"));
        });
    }

    [Test]
    public void Matches_FiltersOnlyVisibleCaptionText()
    {
        const string caption = "**Deploy** app";

        Assert.Multiple(() =>
        {
            Assert.That(ButtonCaptionPresentation.Matches(caption, "deploy"), Is.True);
            Assert.That(ButtonCaptionPresentation.Matches(caption, "tooltip-only"), Is.False);
            Assert.That(ButtonCaptionPresentation.Matches(caption, "git pull"), Is.False);
            Assert.That(ButtonCaptionPresentation.Matches(caption, "**"), Is.False);
            Assert.That(ButtonCaptionPresentation.Matches(caption, " \t"), Is.True);
        });
    }

    [Test]
    public void PanelSetFilter_UsesVisibleCaptionAndExcludesCommandAndTooltip()
    {
        var list = new StackPanel();
        var panel = new StackPanel();
        var filter = new TextBox();
        var controller = new ButtonsPanelController(list, panel, filter,
            () => { }, _ => { }, _ => { }, _ => Assert.Fail("Filtering must not execute a command."));
        var matching = new ButtonDefinition(Guid.NewGuid(),
            new ButtonContent("**Deploy** app", "tip", "echo never-run", ButtonCommandType.RawCommand), 0, 1);
        var other = new ButtonDefinition(Guid.NewGuid(),
            new ButtonContent("Other", "Deploy app", "Deploy app", ButtonCommandType.RawCommand), 1, 1);
        controller.Refresh(new[] { matching, other });

        controller.SetFilter("DEPLOY APP ");

        Assert.Multiple(() =>
        {
            Assert.That(controller.ViewModel.FilterText, Is.EqualTo("DEPLOY APP"));
            Assert.That(list.Children.OfType<Button>().Select(button => ((ButtonDefinition)button.Tag).Id),
                Is.EqualTo(new[] { matching.Id }));
        });
        controller.SetFilter("   ");
        Assert.That(list.Children.OfType<Button>().Count(), Is.EqualTo(2));
    }
}
