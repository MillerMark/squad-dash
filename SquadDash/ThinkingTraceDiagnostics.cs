using System;
using System.Text;

namespace SquadDash;

internal static class ThinkingTraceDiagnostics
{
    private const int EdgePreviewChars = 36;

    internal static string BuildBridgeReceivedMessage(SquadSdkEvent evt)
    {
        var text = ExtractThinkingText(evt);
        return "THINKING_TRACE bridge-received " +
               $"type={evt.Type ?? "(none)"} requestId={evt.RequestId ?? "(none)"} " +
               $"sessionId={evt.SessionId ?? "(none)"} parentToolCallId={evt.ParentToolCallId ?? "(none)"} " +
               DescribeText("text", text);
    }

    internal static string BuildUiDropMessage(
        string target,
        string eventType,
        string? rawText,
        string normalizedText,
        string? currentText)
    {
        return "THINKING_TRACE ui-drop " +
               $"target={target} event={eventType} reason=whitespace-only " +
               DescribeText("raw", rawText) + " " +
               DescribeText("normalized", normalizedText) + " " +
               DescribeText("current", currentText);
    }

    internal static string BuildUiSkipMessage(
        string target,
        string reason,
        string? text,
        string? currentText)
    {
        return "THINKING_TRACE ui-skip " +
               $"target={target} reason={reason} " +
               DescribeText("chunk", text) + " " +
               DescribeText("current", currentText);
    }

    internal static string BuildUiAppendMessage(
        string target,
        string currentText,
        string chunk,
        string reason)
    {
        return "THINKING_TRACE ui-append-boundary " +
               $"target={target} reason={reason} " +
               DescribeJoin(currentText, chunk) + " " +
               DescribeText("chunk", chunk);
    }

    internal static string BuildUiReconcileMessage(
        string target,
        string currentText,
        string incomingText,
        string mergedText,
        string? tailToAppend,
        string action)
    {
        return "THINKING_TRACE ui-reconcile " +
               $"target={target} action={action} " +
               DescribeText("current", currentText) + " " +
               DescribeText("incoming", incomingText) + " " +
               DescribeText("tail", tailToAppend) + " " +
               DescribeText("merged", mergedText);
    }

    internal static string BuildFinalReasoningMessage(
        string target,
        string currentText,
        string finalText)
    {
        return "THINKING_TRACE ui-final-reasoning " +
               $"target={target} " +
               DescribeJoin(currentText, finalText) + " " +
               DescribeText("final", finalText);
    }

    internal static bool ShouldTraceAppendBoundary(string currentText, string chunk)
    {
        if (string.IsNullOrEmpty(currentText) || string.IsNullOrEmpty(chunk))
            return false;

        if (char.IsWhiteSpace(currentText[^1]) || char.IsWhiteSpace(chunk[0]))
            return true;

        return LooksLikeMissingSentenceSeparator(currentText, chunk);
    }

    internal static string DescribeText(string label, string? text)
    {
        if (text is null)
            return $"{label}=<null>";

        return $"{label}Len={text.Length} " +
               $"{label}WhitespaceOnly={IsWhitespaceOnly(text)} " +
               $"{label}LeadingWs={CountLeadingWhitespace(text)} " +
               $"{label}TrailingWs={CountTrailingWhitespace(text)} " +
               $"{label}Head=\"{EscapeForTrace(Head(text))}\" " +
               $"{label}Tail=\"{EscapeForTrace(Tail(text))}\"";
    }

    internal static string DescribeJoin(string currentText, string chunk)
    {
        var currentTail = Tail(currentText);
        var chunkHead = Head(chunk);
        var left = currentText.Length == 0 ? "<empty>" : EscapeForTrace(currentText[^1].ToString());
        var right = chunk.Length == 0 ? "<empty>" : EscapeForTrace(chunk[0].ToString());
        return $"joinLeft=\"{left}\" joinRight=\"{right}\" " +
               $"missingSentenceSeparator={LooksLikeMissingSentenceSeparator(currentText, chunk)} " +
               $"currentTail=\"{EscapeForTrace(currentTail)}\" chunkHead=\"{EscapeForTrace(chunkHead)}\"";
    }

    internal static string? ExtractThinkingText(SquadSdkEvent evt) =>
        evt.Text ?? evt.ReasoningText;

    private static bool LooksLikeMissingSentenceSeparator(string currentText, string chunk)
    {
        if (string.IsNullOrEmpty(currentText) || string.IsNullOrEmpty(chunk))
            return false;
        if (char.IsWhiteSpace(currentText[^1]) || char.IsWhiteSpace(chunk[0]))
            return false;

        var punctuation = FindSentenceBoundaryPunctuation(currentText);
        if (punctuation is null)
            return false;

        var firstLetter = FindFirstBoundaryLetter(chunk);
        return firstLetter is not null && char.IsUpper(firstLetter.Value);
    }

    private static char? FindSentenceBoundaryPunctuation(string text)
    {
        for (var index = text.Length - 1; index >= 0; index--)
        {
            var ch = text[index];
            if (ch is '"' or '\'' or ')' or ']' or '}' or '`')
                continue;
            return ch is '.' or '!' or '?' or ':' ? ch : null;
        }

        return null;
    }

    private static char? FindFirstBoundaryLetter(string text)
    {
        foreach (var ch in text)
        {
            if (ch is '"' or '\'' or '(' or '[' or '{' or '`' or '*' or '_' or '~')
                continue;
            return char.IsLetter(ch) ? ch : null;
        }

        return null;
    }

    private static bool IsWhitespaceOnly(string text) =>
        text.Length > 0 && string.IsNullOrWhiteSpace(text);

    private static int CountLeadingWhitespace(string text)
    {
        var count = 0;
        while (count < text.Length && char.IsWhiteSpace(text[count]))
            count++;
        return count;
    }

    private static int CountTrailingWhitespace(string text)
    {
        var count = 0;
        for (var index = text.Length - 1; index >= 0 && char.IsWhiteSpace(text[index]); index--)
            count++;
        return count;
    }

    private static string Head(string text) =>
        text.Length <= EdgePreviewChars ? text : text[..EdgePreviewChars];

    private static string Tail(string text) =>
        text.Length <= EdgePreviewChars ? text : text[^EdgePreviewChars..];

    private static string EscapeForTrace(string text)
    {
        if (text.Length == 0)
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(ch switch
            {
                ' '  => "[sp]",
                '\t' => "\\t",
                '\r' => "\\r",
                '\n' => "\\n",
                '\f' => "\\f",
                '\v' => "\\v",
                '\\' => "\\\\",
                '"'  => "\\\"",
                _ when char.IsControl(ch) => "\\u" + ((int)ch).ToString("x4"),
                _ => ch
            });
        }

        return builder.ToString();
    }
}
