using System;

namespace SquadDash;

internal enum AttachmentTextRenderMode
{
    PlainText,
    WholeCode,
    MixedMarkdown,
}

internal sealed record AttachmentTextClassification(
    AttachmentTextRenderMode Mode,
    string Content,
    string? Grammar,
    string? LanguageLabel);

internal static class AttachmentTextClassifier
{
    internal static AttachmentTextClassification Classify(string text, RegionHighlightingEngine? engine = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!IsWithinHighlightBudget(text))
            return PlainFallback(text);

        var resolver = engine ?? RegionHighlightingEngine.Default;
        var normalized = NormalizeLineEndings(text);
        var lines = normalized.Split('\n');
        var firstFence = EmbeddedFenceDetector.FindFirstFence(lines);
        if (firstFence is not null)
        {
            var fence = firstFence.Value;
            if (IsWholeBodyFence(lines, fence))
                return ClassifyWholeFence(lines, fence, resolver);

            return new AttachmentTextClassification(AttachmentTextRenderMode.MixedMarkdown, text, null, null);
        }

        var detected = resolver.Resolve(string.Empty, text);
        return detected is null
            ? PlainFallback(text)
            : new AttachmentTextClassification(AttachmentTextRenderMode.WholeCode, text, detected, detected);
    }

    private static AttachmentTextClassification ClassifyWholeFence(
        string[] lines,
        EmbeddedFenceDetector.FenceSpan fence,
        RegionHighlightingEngine engine)
    {
        var extracted = fence.ExtractContent(lines);
        var token = fence.Language;
        return RegionLanguagePicker.GetMode(token) switch
        {
            RegionLanguageMode.AutoDetect => AutoDetectWholeFence(extracted, engine),
            RegionLanguageMode.PlainText => new AttachmentTextClassification(
                AttachmentTextRenderMode.WholeCode,
                extracted,
                null,
                "Plain text"),
            _ => ExplicitWholeFence(extracted, token, engine),
        };
    }

    private static AttachmentTextClassification AutoDetectWholeFence(string extracted, RegionHighlightingEngine engine)
    {
        var grammar = engine.Resolve(string.Empty, extracted);
        return new AttachmentTextClassification(
            AttachmentTextRenderMode.WholeCode,
            extracted,
            grammar,
            grammar ?? "Plain text");
    }

    private static AttachmentTextClassification ExplicitWholeFence(
        string extracted,
        string token,
        RegionHighlightingEngine engine)
    {
        var grammar = engine.ResolveFenceToken(token);
        return new AttachmentTextClassification(
            AttachmentTextRenderMode.WholeCode,
            extracted,
            grammar,
            grammar ?? token);
    }

    private static bool IsWholeBodyFence(string[] lines, EmbeddedFenceDetector.FenceSpan fence)
    {
        if (fence.OpenLineIndex != 0)
            return false;

        var lastLineIndex = lines.Length - 1;
        if (fence.CloseLineIndex == lastLineIndex)
            return true;

        return lines.Length >= 2 &&
               lines[lastLineIndex].Length == 0 &&
               fence.CloseLineIndex == lastLineIndex - 1;
    }

    private static bool IsWithinHighlightBudget(string text)
    {
        if (text.Length > TranscriptCodeBlockPreview.MaximumCharacters)
            return false;

        var lines = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n' || (text[i] == '\r' && (i + 1 == text.Length || text[i + 1] != '\n')))
            {
                if (++lines > TranscriptCodeBlockPreview.MaximumLines)
                    return false;
            }
        }

        return true;
    }

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n");

    private static AttachmentTextClassification PlainFallback(string text) =>
        new(AttachmentTextRenderMode.PlainText, text, null, null);
}
