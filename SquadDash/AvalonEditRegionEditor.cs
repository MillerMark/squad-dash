using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    public string SelectedText => _editor.SelectedText;

    public int TextLength => _editor.Document.TextLength;

    public bool IsKeyboardFocusWithin => _editor.IsKeyboardFocusWithin;

    public double MaxHeight
    {
        get => _editor.MaxHeight;
        set => _editor.MaxHeight = value;
    }

    public void RemoveSelectedText()
    {
        var length = _editor.SelectionLength;
        if (length <= 0)
            return;
        _editor.Document.Remove(_editor.SelectionStart, length);
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
