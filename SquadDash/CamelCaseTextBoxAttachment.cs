using System;
using System.Windows.Controls;
using System.Windows.Input;

namespace SquadDash;

/// <summary>
/// Attaches camelCase segment navigation (Alt+Left / Alt+Right, with optional Shift for selection
/// extension) to any <see cref="TextBox"/>.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
///   _camelCaseNav = new CamelCaseTextBoxAttachment();
///   PreviewKeyDown += (_, e) => { if (_camelCaseNav.HandlePreviewKeyDown(e, focused)) e.Handled = true; };
/// </code>
/// </remarks>
internal sealed class CamelCaseTextBoxAttachment {
    private int  _anchor    = 0;
    private bool _hasAnchor = false;

    /// <summary>
    /// Call from the host window's PreviewKeyDown.
    /// Returns <c>true</c> when the event was consumed — the caller should set <c>e.Handled = true</c>.
    /// </summary>
    internal bool HandlePreviewKeyDown(KeyEventArgs e, TextBox? textBox) {
        if (textBox is null) return false;

        var effectiveKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (effectiveKey != Key.Left && effectiveKey != Key.Right)
            return false;

        var isAlt = (Keyboard.Modifiers & ModifierKeys.Alt)     != 0
                 && (Keyboard.Modifiers & ModifierKeys.Control) == 0;
        if (!isAlt) return false;

        bool isRight = effectiveKey == Key.Right;
        bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        string text      = textBox.Text;
        int    selStart  = textBox.SelectionStart;
        int    selLength = textBox.SelectionLength;

        if (!isShift) {
            // Alt+Arrow: move caret, collapse any selection
            int from   = selLength > 0 ? (isRight ? selStart + selLength : selStart) : selStart;
            int newPos = isRight
                ? CamelCaseNavigator.MoveRight(text, from)
                : CamelCaseNavigator.MoveLeft(text, from);
            textBox.Select(newPos, 0);
            _hasAnchor = false;
        }
        else {
            // Shift+Alt+Arrow: extend/shrink selection
            if (!_hasAnchor) {
                // Anchor is the "stable" end of any existing selection
                _anchor = selLength > 0
                    ? (isRight ? selStart : selStart + selLength)
                    : selStart;
                _hasAnchor = true;
            }

            // Active end is the end that moves (opposite of anchor)
            int activeEnd = _anchor == selStart + selLength ? selStart : selStart + selLength;

            int newPos     = isRight
                ? CamelCaseNavigator.MoveRight(text, activeEnd)
                : CamelCaseNavigator.MoveLeft(text, activeEnd);
            int newSelStart  = Math.Min(_anchor, newPos);
            int newSelLength = Math.Abs(_anchor - newPos);
            textBox.Select(newSelStart, newSelLength);
        }

        return true;
    }
}
