using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;

namespace SquadDash;

internal sealed class BulletedTextBox : TextBox {
    private const string BulletPrefix = "• ";
    private bool _internalChange;

    public BulletedTextBox() {
        AcceptsReturn = true;
        TextWrapping = System.Windows.TextWrapping.Wrap;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        PreviewKeyDown += HandlePreviewKeyDown;
        PreviewTextInput += HandlePreviewTextInput;
    }

    public string[] GetBulletItems() {
        return Text
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.StartsWith(BulletPrefix, StringComparison.Ordinal)
                ? line[BulletPrefix.Length..].Trim()
                : line)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }

    public void SetBulletItems(IEnumerable<string>? items) {
        var normalized = items?
            .Select(item => item?.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => BulletPrefix + item)
            .ToArray()
            ?? Array.Empty<string>();

        _internalChange = true;
        Text = string.Join(Environment.NewLine, normalized);
        CaretIndex = Text.Length;
        _internalChange = false;
    }

    private void HandlePreviewTextInput(object sender, TextCompositionEventArgs e) {
        if (_internalChange || string.IsNullOrEmpty(e.Text))
            return;

        var lineIndex = GetLineIndexFromCharacterIndex(CaretIndex);
        var lineStart = GetCharacterIndexFromLineIndex(lineIndex);
        var lineText = GetLineText(lineIndex);
        if (!string.IsNullOrWhiteSpace(lineText) || lineText.StartsWith(BulletPrefix, StringComparison.Ordinal))
            return;

        _internalChange = true;
        Text = Text.Insert(lineStart, BulletPrefix);
        CaretIndex += BulletPrefix.Length;
        _internalChange = false;
    }

    private void HandlePreviewKeyDown(object sender, KeyEventArgs e) {
        if (_internalChange)
            return;

        if (e.Key == Key.Return) {
            InsertNewBulletLine();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back && TryHandleBulletBackspace())
            e.Handled = true;
    }

    private void InsertNewBulletLine() {
        var insertion = Environment.NewLine + BulletPrefix;
        var selectionStart = SelectionStart;
        var selectionLength = SelectionLength;

        _internalChange = true;
        if (selectionLength > 0)
            Text = Text.Remove(selectionStart, selectionLength);
        Text = Text.Insert(selectionStart, insertion);
        CaretIndex = selectionStart + insertion.Length;
        _internalChange = false;
    }

    private bool TryHandleBulletBackspace() {
        if (SelectionLength > 0)
            return false;

        var lineIndex = GetLineIndexFromCharacterIndex(CaretIndex);
        if (lineIndex <= 0)
            return false;

        var lineStart = GetCharacterIndexFromLineIndex(lineIndex);
        var lineText = GetLineText(lineIndex);
        if (!lineText.StartsWith(BulletPrefix, StringComparison.Ordinal))
            return false;

        var emptyBulletLine = lineText.TrimEnd('\r', '\n').Equals(BulletPrefix, StringComparison.Ordinal);
        var bulletEnd = lineStart + BulletPrefix.Length;
        if (!emptyBulletLine || CaretIndex != bulletEnd)
            return false;

        var newlineLength = Environment.NewLine.Length;
        var removeStart = lineStart - newlineLength;
        if (removeStart < 0)
            return false;

        _internalChange = true;
        Text = Text.Remove(removeStart, newlineLength + BulletPrefix.Length);
        CaretIndex = removeStart;
        _internalChange = false;
        return true;
    }
}
