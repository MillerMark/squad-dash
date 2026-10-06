using System.Threading;
using System.Windows;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class TranscriptTitleFitPolicyTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void Resolve_UsesNominalWhenFullTextFits()
    {
        var decision = TranscriptTitleFitPolicy.Resolve(
            "agent - intent - 5s ago",
            "agent - intent",
            nominalFontSize: 18,
            availableWidth: 360,
            measure: (_, fontSize) => 12 * fontSize);

        Assert.That(decision, Is.EqualTo(new TranscriptTitleFitDecision(
            "agent - intent - 5s ago",
            18,
            TextTrimming.None)));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Resolve_KeepsFullTextAndEllipsisWhenOnlyBaseFits()
    {
        var decision = TranscriptTitleFitPolicy.Resolve(
            "agent - intent - 5s ago",
            "agent - intent",
            nominalFontSize: 18,
            availableWidth: 240,
            measure: (text, fontSize) =>
                text == "agent - intent - 5s ago"
                    ? 420 * (fontSize / 18.0)
                    : 230 * (fontSize / 18.0));

        Assert.That(decision, Is.EqualTo(new TranscriptTitleFitDecision(
            "agent - intent - 5s ago",
            18,
            TextTrimming.CharacterEllipsis)));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Resolve_FallsBackToSeventyPercentWithEllipsis()
    {
        var decision = TranscriptTitleFitPolicy.Resolve(
            "agent - intent - 5s ago",
            "agent - intent",
            nominalFontSize: 18,
            availableWidth: 120,
            measure: (_, fontSize) => 400 * (fontSize / 18.0));

        Assert.That(decision, Is.EqualTo(new TranscriptTitleFitDecision(
            "agent - intent - 5s ago",
            12.6,
            TextTrimming.CharacterEllipsis)));
    }

    [TestCase(180, 15.3)]
    [TestCase(150, 12.6)]
    public void Resolve_ShrinksFullTextBeforeUsingEllipsis(double availableWidth, double expectedFontSize)
    {
        var decision = TranscriptTitleFitPolicy.Resolve(
            "full title", "base", 18, availableWidth, (_, size) => 200 * size / 18);
        Assert.That(decision.FontSize, Is.EqualTo(expectedFontSize).Within(0.0001));
        Assert.That(decision.Trimming, Is.EqualTo(TextTrimming.None));
    }

    [Test]
    public void Resolve_BudgetsCandidateFontSpinnerWidthRatherThanPreviousSmallerSlot()
    {
        var decision = TranscriptTitleFitPolicy.Resolve(
            "full title", "base", 18, 219,
            (_, size) => 200 * size / 18 + ActivitySpinner.GetExpandedLayoutWidth(size));
        Assert.That(decision.FontSize, Is.EqualTo(15.3).Within(0.0001));
        Assert.That(decision.Trimming, Is.EqualTo(TextTrimming.None));
    }
}
