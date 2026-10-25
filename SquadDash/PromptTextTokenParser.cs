using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;

namespace SquadDash;

internal enum PromptTextTokenKind {
    HeadingMarker,
    BoldContent,
    HexColor,
    InlineCodeContent,
    QuotedContent,
}

internal readonly record struct PromptTextToken(
    int Start,
    int Length,
    PromptTextTokenKind Kind,
    Color Color = default);

internal static class PromptTextTokenParser {
    internal static IReadOnlyList<PromptTextToken> Parse(string text) {
        if (string.IsNullOrEmpty(text))
            return [];

        var tokens = new List<PromptTextToken>();
        var nonWhitespacePrefix = BuildNonWhitespacePrefix(text);
        var openStarBold = new List<int>();
        var openUnderscoreBold = new List<int>();

        var inInlineCode = false;
        var inlineCodeDelimiterLength = 0;
        var inlineCodeStart = 0;
        var openQuote = -1;
        var closingQuote = '\0';

        var i = 0;
        while (i < text.Length) {
            // Hard line breaks bound incomplete prose; visual wrapping does not.
            if (text[i] is '\r' or '\n') {
                openQuote = -1;
                closingQuote = '\0';
            }

            if (!inInlineCode && text[i] is '"' or '\u201C' or '\u201D' && !IsEscaped(text, i)) {
                if (openQuote >= 0 && text[i] == closingQuote) {
                    if (HasNonWhitespaceBetween(nonWhitespacePrefix, openQuote + 1, i))
                        tokens.Add(new PromptTextToken(openQuote + 1, i - openQuote - 1, PromptTextTokenKind.QuotedContent));
                    openQuote = -1;
                    closingQuote = '\0';
                }
                else if (openQuote < 0 && text[i] is '"' or '\u201C') {
                    openQuote = i;
                    closingQuote = text[i] == '"' ? '"' : '\u201D';
                }
            }

            if (!inInlineCode &&
                IsAtLineStart(text, i) &&
                TryReadHeadingMarker(text, i, out var headingStart, out var headingLength)) {
                tokens.Add(new PromptTextToken(headingStart, headingLength, PromptTextTokenKind.HeadingMarker));
                i = headingStart + headingLength;
                continue;
            }

            if (text[i] == '#' &&
                TryReadHexColorToken(text, i, out var hexToken)) {
                tokens.Add(hexToken);
                i += hexToken.Length;
                continue;
            }

            if (!inInlineCode &&
                TryReadBoldDelimiter(text, i, out var delimiterChar) &&
                !IsEscaped(text, i)) {
                var stack = delimiterChar == '*'
                    ? openStarBold
                    : openUnderscoreBold;
                var canOpen = CanOpenBold(text, i, delimiterChar);
                var canClose = CanCloseBold(text, i, delimiterChar);

                if (canClose && stack.Count > 0) {
                    var open = stack[^1];
                    if (HasNonWhitespaceBetween(nonWhitespacePrefix, open + 2, i)) {
                        tokens.Add(new PromptTextToken(open + 2, i - open - 2, PromptTextTokenKind.BoldContent));
                        stack.RemoveAt(stack.Count - 1);
                        i += 2;
                        continue;
                    }
                }

                if (canOpen)
                    stack.Add(i);

                i += 2;
                continue;
            }

            if (text[i] == '`' && !IsEscaped(text, i)) {
                var tickCount = CountRun(text, i, '`');
                if (!inInlineCode) {
                    inInlineCode = true;
                    inlineCodeDelimiterLength = tickCount;
                    inlineCodeStart = i + tickCount;
                    i += tickCount;
                    continue;
                }

                if (tickCount == inlineCodeDelimiterLength) {
                    if (i > inlineCodeStart)
                        tokens.Add(new PromptTextToken(inlineCodeStart, i - inlineCodeStart, PromptTextTokenKind.InlineCodeContent));
                    inInlineCode = false;
                    inlineCodeDelimiterLength = 0;
                }
                i += tickCount;
                continue;
            }

            i++;
        }

        if (tokens.Count <= 1)
            return tokens;

        tokens.Sort(static (a, b) => {
            var cmp = a.Start.CompareTo(b.Start);
            if (cmp != 0)
                return cmp;

            cmp = a.Length.CompareTo(b.Length);
            if (cmp != 0)
                return cmp;

            return a.Kind.CompareTo(b.Kind);
        });
        return tokens;
    }

    private static bool TryReadHeadingMarker(string text, int lineStart, out int markerStart, out int markerLength) {
        markerStart = -1;
        markerLength = 0;

        var i = lineStart;
        var leadingSpaces = 0;
        while (i < text.Length && text[i] == ' ' && leadingSpaces < 4) {
            i++;
            leadingSpaces++;
        }

        if (leadingSpaces > 3 || i >= text.Length || text[i] != '#' || IsEscaped(text, i))
            return false;

        var hashes = 0;
        while (i + hashes < text.Length && text[i + hashes] == '#')
            hashes++;

        if (hashes < 1 || hashes > 6)
            return false;

        var after = i + hashes;
        if (after < text.Length && !char.IsWhiteSpace(text[after]))
            return false;

        markerStart = i;
        markerLength = hashes;
        return true;
    }

    private static bool TryReadHexColorToken(string text, int start, out PromptTextToken token) {
        token = default;
        if (IsEscaped(text, start))
            return false;

        if (start > 0) {
            var before = text[start - 1];
            if (IsWordChar(before) || before == '#')
                return false;
        }

        if (!MarkdownDocumentRenderer.TryReadColorHex(text, start, out var end, out var color))
            return false;

        if (end < text.Length && IsInvalidHexSuffix(text[end]))
            return false;

        token = new PromptTextToken(start, end - start, PromptTextTokenKind.HexColor, color);
        return true;
    }

    private static bool TryReadBoldDelimiter(string text, int index, out char delimiterChar) {
        delimiterChar = default;
        if (index + 1 >= text.Length)
            return false;

        var c = text[index];
        if ((c == '*' || c == '_') && text[index + 1] == c) {
            delimiterChar = c;
            return true;
        }

        return false;
    }

    private static bool CanOpenBold(string text, int delimiterStart, char delimiterChar) {
        var afterIndex = delimiterStart + 2;
        if (afterIndex >= text.Length)
            return false;
        if (char.IsWhiteSpace(text[afterIndex]))
            return false;

        if (delimiterChar == '_') {
            var hasBefore = delimiterStart > 0;
            var beforeIsWord = hasBefore && IsWordChar(text[delimiterStart - 1]);
            var afterIsWord = IsWordChar(text[afterIndex]);
            if (beforeIsWord && afterIsWord)
                return false;
        }

        return true;
    }

    private static bool CanCloseBold(string text, int delimiterStart, char delimiterChar) {
        if (delimiterStart == 0)
            return false;
        if (char.IsWhiteSpace(text[delimiterStart - 1]))
            return false;

        if (delimiterChar == '_') {
            var hasAfter = delimiterStart + 2 < text.Length;
            var beforeIsWord = IsWordChar(text[delimiterStart - 1]);
            var afterIsWord = hasAfter && IsWordChar(text[delimiterStart + 2]);
            if (beforeIsWord && afterIsWord)
                return false;
        }

        return true;
    }

    private static bool IsInvalidHexSuffix(char c) {
        if (char.IsLetterOrDigit(c) || c is '_' or '#')
            return true;

        var category = char.GetUnicodeCategory(c);
        return category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
    }

    private static int[] BuildNonWhitespacePrefix(string text) {
        var prefix = new int[text.Length + 1];
        for (var i = 0; i < text.Length; i++)
            prefix[i + 1] = prefix[i] + (char.IsWhiteSpace(text[i]) ? 0 : 1);
        return prefix;
    }

    private static bool HasNonWhitespaceBetween(int[] nonWhitespacePrefix, int startInclusive, int endExclusive) =>
        startInclusive < endExclusive &&
        nonWhitespacePrefix[endExclusive] > nonWhitespacePrefix[startInclusive];

    private static bool IsAtLineStart(string text, int index) =>
        index == 0 || text[index - 1] is '\n' or '\r';

    private static int CountRun(string text, int start, char c) {
        var count = 0;
        while (start + count < text.Length && text[start + count] == c)
            count++;
        return count;
    }

    private static bool IsEscaped(string text, int index) {
        var slashCount = 0;
        for (var i = index - 1; i >= 0 && text[i] == '\\'; i--)
            slashCount++;
        return (slashCount & 1) == 1;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
