namespace SquadDash;

/// <summary>
/// Pure navigation logic for camelCase word-boundary movement.
/// No WPF dependency — testable in isolation.
/// </summary>
internal static class CamelCaseNavigator {
    /// <summary>
    /// Returns the caret index after moving right one camelCase segment from <paramref name="caretIndex"/>.
    /// A "segment boundary" is a lowercase→uppercase transition within a word.
    /// If no such transition exists before the end of the current word, returns the index just after the word.
    /// </summary>
    internal static int MoveRight(string text, int caretIndex) {
        int pos = caretIndex;
        // Skip leading whitespace to land at the start of the next word
        while (pos < text.Length && char.IsWhiteSpace(text[pos]))
            pos++;
        // Scan forward for a lowercase→uppercase transition or end-of-word
        for (int i = pos + 1; i < text.Length; i++) {
            if (char.IsWhiteSpace(text[i]))
                return i;
            if (char.IsLower(text[i - 1]) && char.IsUpper(text[i]))
                return i;
        }
        return text.Length;
    }

    /// <summary>
    /// Returns the caret index after moving left one camelCase segment from <paramref name="caretIndex"/>.
    /// If no lowercase→uppercase transition exists before the start of the current word, returns the
    /// index at the start of the word.
    /// </summary>
    internal static int MoveLeft(string text, int caretIndex) {
        int pos = caretIndex;
        // Skip whitespace to the left to land at the end of the previous word
        while (pos > 0 && char.IsWhiteSpace(text[pos - 1]))
            pos--;
        // Scan backward for a lowercase→uppercase transition or start-of-word
        for (int i = pos - 1; i > 0; i--) {
            if (char.IsWhiteSpace(text[i - 1]))
                return i;
            if (char.IsLower(text[i - 1]) && char.IsUpper(text[i]))
                return i;
        }
        return 0;
    }
}
