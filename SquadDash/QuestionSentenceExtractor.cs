namespace SquadDash;

/// <summary>
/// Pure string logic for locating question sentences within plain text.
/// Used by <c>MainWindow</c> to drive the Ctrl+Alt+PageUp/Down question-highlight feature.
/// </summary>
internal static class QuestionSentenceExtractor
{
    /// <summary>
    /// Finds all sentences containing a <c>?</c> in <paramref name="text"/> and returns
    /// their character-index ranges as <c>(Start, End)</c> pairs, where
    /// <c>Start</c> is the first non-whitespace character of the sentence and
    /// <c>End</c> is the index of the last <c>?</c> in that sentence.
    /// </summary>
    /// <remarks>
    /// Sentence-start detection: walking backward from each <c>?</c>, the first
    /// occurrence of <c>.</c> <c>!</c> <c>?</c> <c>\n</c> or <c>\r</c> (or the
    /// beginning of the string) marks the boundary; any leading whitespace after the
    /// boundary (including at position 0) is then skipped.
    ///
    /// Merging: a new range is merged into the preceding one when its computed
    /// sentence start falls at or before <c>prevEnd + 1</c>.  This covers both
    /// immediately adjacent <c>??</c> and cases where there is literally no character
    /// gap between the previous <c>?</c> and the next sentence start.
    /// </remarks>
    internal static IReadOnlyList<(int Start, int End)> ExtractQuestionSentenceRanges(string text)
    {
        var result = new List<(int Start, int End)>();
        if (string.IsNullOrEmpty(text))
            return result;

        for (int qIdx = 0; qIdx < text.Length; qIdx++)
        {
            if (text[qIdx] != '?')
                continue;

            int sentenceStart = 0;
            for (int i = qIdx - 1; i >= 0; i--)
            {
                char c = text[i];
                if (c == '.' || c == '!' || c == '?' || c == '\n' || c == '\r')
                {
                    sentenceStart = i + 1;
                    break;
                }
            }

            // Skip any whitespace after the boundary, including leading whitespace
            // at the very start of the string.
            while (sentenceStart < qIdx && char.IsWhiteSpace(text[sentenceStart]))
                sentenceStart++;

            // Merge with the previous range when the new sentence start falls within
            // or immediately adjacent to it (handles "??", "A?B?", etc.).
            if (result.Count > 0 && sentenceStart <= result[^1].End + 1)
                result[^1] = (result[^1].Start, qIdx);
            else
                result.Add((sentenceStart, qIdx));
        }

        return result;
    }
}
