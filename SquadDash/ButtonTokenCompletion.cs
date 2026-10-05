using System;
using System.Linq;

namespace SquadDash;

internal sealed record ButtonTokenInsertion(string Text, int CaretIndex, int Start, int Length);

internal static class ButtonTokenCompletion {
    internal static bool TryGetPrefix(string text, int caret, out int start, out string prefix) {
        start = -1;
        prefix = string.Empty;
        if (caret < 0 || caret > text.Length) return false;
        for (var i = caret - 1; i >= 0; i--) {
            if (text[i] == '{') {
                start = i;
                prefix = text[i..caret];
                return true;
            }
            if (!char.IsLetterOrDigit(text[i]) && text[i] != '_') return false;
        }
        return false;
    }

    internal static ButtonTokenInsertion Complete(string text, int caret, string token) {
        if (!ButtonPlaceholders.Catalog.Any(item => item.Token == token))
            throw new ArgumentException("Choose a token from the placeholder catalog.", nameof(token));
        if (!TryGetPrefix(text, caret, out var start, out _))
            throw new InvalidOperationException("The command caret no longer follows a token prefix.");
        var end = caret < text.Length && text[caret] == '}' ? caret + 1 : caret;
        return new ButtonTokenInsertion(text[..start] + token + text[end..],
            start + token.Length, start, end - start);
    }
}
