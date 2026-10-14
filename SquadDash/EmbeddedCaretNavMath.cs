using System;
using System.Windows.Input;

namespace SquadDash;

/// <summary>
/// Pure, position-only arrow-key navigation decision for the inline code-region editor
/// (<see cref="EmbeddedCodeRegionBlock"/>'s content <see cref="System.Windows.Controls.TextBox"/>).
/// <para>
/// The decision depends ONLY on <c>(text, caretIndex, selectionLength, key)</c> — never on how the
/// caret entered the region, focus history, WPF's remembered "desired horizontal caret column"
/// (magnetic X), or any prior navigation. This statelessness is the whole point: the same key at the
/// same caret position must always resolve the same way.
/// </para>
/// <para>
/// The content box uses <c>TextWrapping = NoWrap</c>, so logical <c>\n</c> lines equal visual lines —
/// there is no word-wrap complication. "First line" means there is no <c>\n</c> in <c>text[0..caret)</c>;
/// "last line" means there is no <c>\n</c> in <c>text[caret..L)</c>.
/// </para>
/// </summary>
public static class EmbeddedCaretNavMath
{
    public enum Decision
    {
        /// Let the TextBox handle the key normally (do not set e.Handled).
        PassThrough,

        /// Exit the region toward the previous host text position (LogicalDirection.Backward).
        ExitBackward,

        /// Exit the region toward the next host text position (LogicalDirection.Forward).
        ExitForward,
    }

    /// <summary>
    /// Decide what an unmodified Left/Right/Up/Down key should do, given only the caret state.
    /// Any non-arrow key returns <see cref="Decision.PassThrough"/>. Modifier handling (Ctrl+Up/Down,
    /// Tab/Shift+Tab, Ctrl+C/X) is the caller's responsibility and is intentionally not modeled here.
    /// </summary>
    public static Decision Decide(string text, int caretIndex, int selectionLength, Key key)
    {
        text ??= string.Empty;
        var length = text.Length;

        // Clamp caret defensively; the decision must stay well-defined for any input.
        if (caretIndex < 0)
            caretIndex = 0;
        else if (caretIndex > length)
            caretIndex = length;

        // A live selection: let the TextBox collapse it normally. Simple and stateless.
        if (selectionLength > 0)
            return Decision.PassThrough;

        switch (key)
        {
            case Key.Left:
                return caretIndex == 0 ? Decision.ExitBackward : Decision.PassThrough;

            case Key.Right:
                return caretIndex == length ? Decision.ExitForward : Decision.PassThrough;

            case Key.Up:
                // First line (no newline before the caret) → always exit, regardless of column.
                return IsOnFirstLine(text, caretIndex) ? Decision.ExitBackward : Decision.PassThrough;

            case Key.Down:
                // Last line (no newline at or after the caret) → always exit, regardless of column.
                return IsOnLastLine(text, caretIndex) ? Decision.ExitForward : Decision.PassThrough;

            default:
                return Decision.PassThrough;
        }
    }

    private static bool IsOnFirstLine(string text, int caretIndex)
    {
        for (var i = 0; i < caretIndex; i++)
        {
            if (text[i] == '\n')
                return false;
        }

        return true;
    }

    private static bool IsOnLastLine(string text, int caretIndex)
    {
        for (var i = caretIndex; i < text.Length; i++)
        {
            if (text[i] == '\n')
                return false;
        }

        return true;
    }
}
