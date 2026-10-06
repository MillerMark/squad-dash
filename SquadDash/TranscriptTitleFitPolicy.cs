using System;
using System.Windows;

namespace SquadDash;

internal readonly record struct TranscriptTitleFitDecision(
    string DisplayText,
    double FontSize,
    TextTrimming Trimming);

internal static class TranscriptTitleFitPolicy
{
    private const double MidScale = 0.85;
    private const double MinScale = 0.70;

    internal static TranscriptTitleFitDecision Resolve(
        string fullText,
        string baseText,
        double nominalFontSize,
        double availableWidth,
        Func<string, double, double> measure)
    {
        if (measure(fullText, nominalFontSize) <= availableWidth)
            return new TranscriptTitleFitDecision(fullText, nominalFontSize, TextTrimming.None);

        if (measure(fullText, nominalFontSize * MidScale) <= availableWidth)
            return new TranscriptTitleFitDecision(fullText, nominalFontSize * MidScale, TextTrimming.None);

        if (measure(fullText, nominalFontSize * MinScale) <= availableWidth)
            return new TranscriptTitleFitDecision(fullText, nominalFontSize * MinScale, TextTrimming.None);

        if (measure(baseText, nominalFontSize) <= availableWidth)
            return new TranscriptTitleFitDecision(fullText, nominalFontSize, TextTrimming.CharacterEllipsis);

        if (measure(baseText, nominalFontSize * MinScale) <= availableWidth)
            return new TranscriptTitleFitDecision(fullText, nominalFontSize * MinScale, TextTrimming.CharacterEllipsis);

        return new TranscriptTitleFitDecision(fullText, nominalFontSize * MinScale, TextTrimming.CharacterEllipsis);
    }
}
