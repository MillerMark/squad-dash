using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;

namespace SquadDash;

/// <summary>
/// Concrete <see cref="IRegionCodeEditor"/> backed by ICSharpCode AvalonEdit's
/// <see cref="TextEditor"/>. Replaces the plain Consolas <see cref="TextBox"/> that previously
/// hosted embedded code-region content, mapping each TextBox API the region relied on onto
/// AvalonEdit's <c>Document</c>/<c>TextArea</c>/<c>Caret</c> surface (per the AvalonEdit
/// integration decision, AVALONEDIT-20261014-001). No syntax highlighting is configured here —
/// that is a later task; this editor paints plain surface/text colors only.
/// </summary>
public sealed class AvalonEditRegionEditor : IRegionCodeEditor
{
    private readonly TextEditor _editor;

    public AvalonEditRegionEditor(string initialText)
    {
        _editor = new TextEditor
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8),
            Margin = new Thickness(0),
            MaxHeight = 130,
            WordWrap = false,
            ShowLineNumbers = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        // A prompt code region is a plain editor: no clickable hyperlinks, no folding.
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.Document.Text = initialText ?? string.Empty;

        // AvalonEdit raises TextChanged on the TextEditor; forward it verbatim through the seam.
        _editor.TextChanged += (s, e) => TextChanged?.Invoke(this, e);
    }

    public event EventHandler? TextChanged;

    public Control Control => _editor;

    public string Text
    {
        get => _editor.Document.Text;
        set => _editor.Document.Text = value ?? string.Empty;
    }

    public int CaretOffset
    {
        get => _editor.TextArea.Caret.Offset;
        set => _editor.TextArea.Caret.Offset = Math.Clamp(value, 0, _editor.Document.TextLength);
    }

    public int SelectionStart => _editor.SelectionStart;

    public int SelectionLength
    {
        get => _editor.SelectionLength;
        set
        {
            if (value <= 0)
                _editor.TextArea.ClearSelection();
            else
                _editor.Select(_editor.SelectionStart, value);
        }
    }

    public void RestoreSelection(int anchorOffset, int caretOffset)
    {
        var length = _editor.Document.TextLength;
        anchorOffset = Math.Clamp(anchorOffset, 0, length);
        caretOffset = Math.Clamp(caretOffset, 0, length);

        var min = Math.Min(anchorOffset, caretOffset);
        var max = Math.Max(anchorOffset, caretOffset);

        if (min == max)
            _editor.TextArea.ClearSelection();
        else
            _editor.TextArea.Selection =
                ICSharpCode.AvalonEdit.Editing.Selection.Create(_editor.TextArea, min, max);

        // Place the caret on the moving end so a forward and a backward selection each restore with
        // the active caret on the correct side (AvalonEdit renders the highlight from the selection,
        // the blink position from the caret).
        _editor.TextArea.Caret.Offset = caretOffset;
    }

    public string SelectedText => _editor.SelectedText;

    public int TextLength => _editor.Document.TextLength;

    public bool IsKeyboardFocusWithin => _editor.IsKeyboardFocusWithin;

    public bool IsTextInputSurfaceFocused => _editor.TextArea.IsKeyboardFocused;

    public double MaxHeight
    {
        get => _editor.MaxHeight;
        set => _editor.MaxHeight = value;
    }

    public double EditorFontSize
    {
        // Setting TextEditor.FontSize re-measures the TextArea/document and repaints the view
        // automatically — no explicit redraw needed. The Consolas family set in the constructor
        // is preserved; only the point size tracks the host prompt.
        get => _editor.FontSize;
        set => _editor.FontSize = value;
    }

    public void RemoveSelectedText()
    {
        var length = _editor.SelectionLength;
        if (length <= 0)
            return;
        _editor.Document.Remove(_editor.SelectionStart, length);
    }

    public void InsertText(string text)
    {
        text ??= string.Empty;
        var selLength = _editor.SelectionLength;
        var offset = selLength > 0 ? _editor.SelectionStart : _editor.TextArea.Caret.Offset;
        offset = Math.Clamp(offset, 0, _editor.Document.TextLength);
        if (selLength > 0)
            _editor.Document.Replace(offset, selLength, text);
        else
            _editor.Document.Insert(offset, text);
        _editor.TextArea.ClearSelection();
        _editor.TextArea.Caret.Offset = Math.Clamp(offset + text.Length, 0, _editor.Document.TextLength);
    }

    public void FocusEditor()
    {
        // FocusManager.SetFocusedElement sets logical/keyboard focus within the control's focus
        // scope even before the element has gone through a layout pass against a real
        // PresentationSource (e.g. right after the region is created, before it has rendered) —
        // plain Focus()/Keyboard.Focus() require IsVisible to already be true. The TextArea is
        // AvalonEdit's focusable text-input surface, so it is the focus target.
        var textArea = _editor.TextArea;
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(_editor), textArea);
        textArea.Focus();
        Keyboard.Focus(textArea);

        // If real OS keyboard focus did not land on the TextArea synchronously (the control may
        // not yet be connected to a PresentationSource / visible), retry once after layout at
        // Input priority. Without real keyboard focus on the TextArea, WPF routes KeyDown/
        // TextInput (e.g. Space and printable keys) to the host RichTextBox instead of the
        // region, so the character leaks outside the region. The retry establishes genuine
        // keyboard ownership once the editor has rendered.
        if (!textArea.IsKeyboardFocused)
        {
            textArea.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (textArea.IsKeyboardFocused)
                    return;
                FocusManager.SetFocusedElement(FocusManager.GetFocusScope(_editor), textArea);
                textArea.Focus();
                Keyboard.Focus(textArea);
            }));
        }
    }

    public int GetOffsetFromPoint(Point pointInControl)
    {
        if (_editor.Document.TextLength == 0)
            return 0;

        var position = _editor.GetPositionFromPoint(pointInControl);
        if (position is null)
            return _editor.Document.TextLength;

        return Math.Clamp(_editor.Document.GetOffset(position.Value.Location), 0, _editor.Document.TextLength);
    }

    public void ApplyPalette(RegionEditorPalette palette)
    {
        // Stage 1: plain editor colors only. Surface/text are pushed imperatively because
        // AvalonEdit does not honor DynamicResource. Highlight colors are carried on the palette
        // for the later syntax-highlighting task and intentionally ignored here.
        _editor.Background = palette.Surface;
        _editor.Foreground = palette.Text;
    }
}
