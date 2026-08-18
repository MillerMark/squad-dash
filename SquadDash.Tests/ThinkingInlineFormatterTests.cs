using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class ThinkingInlineFormatterTests
{
    [Test]
    public void AppendTo_PreservesLineBreaksAndKeepsProseItalic()
    {
        var textBlock = new TextBlock();

        ThinkingInlineFormatter.AppendTo(textBlock.Inlines, "First paragraph.\n\n- next item");

        var inlines = textBlock.Inlines.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(inlines.OfType<LineBreak>().Count(), Is.EqualTo(2));
            Assert.That(inlines.OfType<Run>().Select(run => run.Text),
                Is.EqualTo(new[] { "First paragraph.", "- next item" }));
            Assert.That(inlines.OfType<Run>().All(run => run.FontStyle == FontStyles.Italic), Is.True);
        });
    }

    [Test]
    public void AppendTo_StylesExplicitAndCamelCaseReferencesAsInlineCode()
    {
        var textBlock = new TextBlock();

        ThinkingInlineFormatter.AppendTo(
            textBlock.Inlines,
            "Use `human-observation`, afterTaskIds, and ProofRequirement; leave TASKS_JSON alone.");

        var runs = textBlock.Inlines.OfType<Run>().ToArray();
        var codeRuns = runs.Where(run => run.FontFamily.Source.Contains("Consolas", StringComparison.Ordinal)).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(codeRuns.Select(run => run.Text),
                Is.EqualTo(new[] { "human-observation", "afterTaskIds", "ProofRequirement" }));
            Assert.That(codeRuns.All(run => run.FontStyle == FontStyles.Normal), Is.True);
            Assert.That(runs.Single(run => run.Text.Contains("TASKS_JSON", StringComparison.Ordinal)).FontStyle,
                Is.EqualTo(FontStyles.Italic));
        });
    }
}
