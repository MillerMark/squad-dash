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
/// integration decision, AVALONEDIT-20261014-001). Grammar selection and theme pushes share the
/// region highlighting engine without changing the native editing/selection behavior.
/// </summary>
public sealed class AvalonEditRegionEditor : IRegionCodeEditor, IDisposable
{
    private const double DefaultEditorMaxHeight = 130;
    private const double EditorHorizontalPadding = 8;
    private const double EditorVerticalPadding = 3;
    private const double FallbackLineHeightMultiplier = 1.35;
    private const double HeightSlack = 2;

    private readonly TextEditor _editor;
    private readonly DispatcherTimer _autoDetectTimer;
    private readonly RegionHighlightingEngine _highlighting;
    private RegionEditorPalette _palette;
    private string _fenceToken = RegionLanguagePicker.PlainTextToken;
    private bool _textChangedDuringUpdate;
    private bool _disposed;

    public AvalonEditRegionEditor(string initialText, RegionHighlightingEngine? highlighting = null)
    {
        _highlighting = highlighting ?? RegionHighlightingEngine.Default;
        _palette = new ThemeResourcePaletteSource(_highlighting).CreatePalette();
        _editor = new TextEditor
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(
                EditorHorizontalPadding,
                EditorVerticalPadding,
                EditorHorizontalPadding,
                EditorVerticalPadding),
            Margin = new Thickness(0),
            MaxHeight = DefaultEditorMaxHeight,
            WordWrap = false,
            ShowLineNumbers = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        AvalonEditClipboardGuard.Attach(_editor);
        // A prompt code region is a plain editor: no clickable hyperlinks, no folding.
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.Document.Text = initialText ?? string.Empty;
        _autoDetectTimer = new DispatcherTimer(DispatcherPriority.Background, _editor.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _autoDetectTimer.Tick += AutoDetectTimer_Tick;
        _editor.Unloaded += Editor_Unloaded;
        _editor.Loaded += Editor_Loaded;
        _editor.PreviewMouseWheel += Editor_PreviewMouseWheel;
        ApplyPalette(_palette);

        // Single-authority undo/redo (PROMPTUNDO-20261015, §5): suppress THIS region's own
        // per-editor AvalonEdit undo stack so it never accumulates or services a user Ctrl+Z/Ctrl+Y.
        // A size limit of 0 means no undoable steps are ever retained, so AvalonEdit's built-in
        // Undo/Redo commands become inert no-ops; the host-level PromptUndoManager owns the one
        // shared prompt timeline (the keystrokes are additionally intercepted and marked handled in
        // MainWindow's window preview-key router before they can reach AvalonEdit at all).
        _editor.Document.UndoStack.SizeLimit = 0;

        _editor.Document.UpdateStarted += Document_UpdateStarted;
        _editor.TextChanged += Editor_TextChanged;
        _editor.Document.UpdateFinished += Document_UpdateFinished;
        UpdateEditorHeight();
    }

    public event EventHandler? TextChanging;
    public event EventHandler? TextChanged;
    public event EventHandler? HighlightingChanged;
    public string? HighlightingName => _editor.SyntaxHighlighting?.Name;
    internal bool IsAutoDetectPending => _autoDetectTimer.IsEnabled;
    internal bool IsDisposed => _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _autoDetectTimer.Stop();
        _autoDetectTimer.Tick -= AutoDetectTimer_Tick;
        _editor.Unloaded -= Editor_Unloaded;
        _editor.Loaded -= Editor_Loaded;
        _editor.PreviewMouseWheel -= Editor_PreviewMouseWheel;
        _editor.Document.UpdateStarted -= Document_UpdateStarted;
        _editor.TextChanged -= Editor_TextChanged;
        _editor.Document.UpdateFinished -= Document_UpdateFinished;
        TextChanging = null;
        TextChanged = null;
        HighlightingChanged = null;
    }

    public void SetLanguage(string fenceToken)
    {
        if (_disposed)
            return;

        _fenceToken = fenceToken ?? string.Empty;
        _autoDetectTimer.Stop();
        RefreshHighlighting();
    }

    private void RefreshHighlighting()
    {
        if (_disposed)
            return;

        var grammar = _highlighting.Resolve(_fenceToken, Text);
        if (grammar == HighlightingName)
            return;
        _editor.SyntaxHighlighting = grammar is null ? null : _highlighting.CreateDefinition(grammar, _palette);
        UpdateEditorHeight();
        _editor.TextArea.TextView.Redraw();
        HighlightingChanged?.Invoke(this, EventArgs.Empty);
    }

    public Control Control => _editor;

    public string Text
    {
        get => _editor.Document.Text;
        set
        {
            if (_disposed)
                return;
            _editor.Document.Text = value ?? string.Empty;
            UpdateEditorHeight();
        }
    }

    public int CaretOffset
    {
        get => _editor.TextArea.Caret.Offset;
        set
        {
            if (_disposed)
                return;
            _editor.TextArea.Caret.Offset = Math.Clamp(value, 0, _editor.Document.TextLength);
        }
    }

    public int SelectionStart => _editor.SelectionStart;

    public int SelectionLength
    {
        get => _editor.SelectionLength;
        set
        {
            if (_disposed)
                return;
            if (value <= 0)
                _editor.TextArea.ClearSelection();
            else
                _editor.Select(_editor.SelectionStart, value);
        }
    }

    public (int anchorOffset, int caretOffset) GetSelectionOffsets()
    {
        var selection = _editor.TextArea.Selection;
        if (selection.IsEmpty)
            return (CaretOffset, CaretOffset);

        // Whole-word mouse selection leaves the caret at the click, not necessarily an endpoint.
        // AvalonEdit's selection retains the full, directed range independently of that caret.
        return (_editor.Document.GetOffset(selection.StartPosition.Location),
            _editor.Document.GetOffset(selection.EndPosition.Location));
    }

    public void RestoreSelection(int anchorOffset, int caretOffset)
    {
        if (_disposed)
            return;

        var length = _editor.Document.TextLength;
        anchorOffset = Math.Clamp(anchorOffset, 0, length);
        caretOffset = Math.Clamp(caretOffset, 0, length);

        if (anchorOffset == caretOffset)
            _editor.TextArea.ClearSelection();
        else
            _editor.TextArea.Selection =
                ICSharpCode.AvalonEdit.Editing.Selection.Create(_editor.TextArea, anchorOffset, caretOffset);

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
        set
        {
            if (!_disposed)
            {
                _editor.MaxHeight = value;
                UpdateEditorHeight();
            }
        }
    }

    public double EditorFontSize
    {
        // Setting TextEditor.FontSize re-measures the TextArea/document and repaints the view
        // automatically — no explicit redraw needed. The Consolas family set in the constructor
        // is preserved; only the point size tracks the host prompt.
        get => _editor.FontSize;
        set
        {
            if (!_disposed)
            {
                _editor.FontSize = value;
                UpdateEditorHeight();
            }
        }
    }

    public void RemoveSelectedText()
    {
        if (_disposed)
            return;

        var length = _editor.SelectionLength;
        if (length <= 0)
            return;
        _editor.Document.Remove(_editor.SelectionStart, length);
    }

    public void DeleteText(int offset, int length)
    {
        if (_disposed)
            return;

        if (length <= 0)
            return;

        var textLength = _editor.Document.TextLength;
        offset = Math.Clamp(offset, 0, textLength);
        length = Math.Min(length, textLength - offset);
        if (length <= 0)
            return;

        _editor.Document.Remove(offset, length);
        _editor.TextArea.ClearSelection();
        _editor.TextArea.Caret.Offset = offset;
    }

    public void InsertText(string text)
    {
        if (_disposed)
            return;

        using var update = _editor.Document.RunUpdate();
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
        if (_disposed)
            return;

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
                if (_disposed || !_editor.IsLoaded)
                    return;
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
        if (_disposed)
            return 0;

        if (_editor.Document.TextLength == 0)
            return 0;

        var position = _editor.GetPositionFromPoint(pointInControl);
        if (position is null)
            return _editor.Document.TextLength;

        return Math.Clamp(_editor.Document.GetOffset(position.Value.Location), 0, _editor.Document.TextLength);
    }

    public void ApplyPalette(RegionEditorPalette palette)
    {
        if (_disposed)
            return;

        _palette = palette;
        _editor.Background = palette.Surface;
        _editor.Foreground = palette.Text;
        if (_editor.SyntaxHighlighting is RegionHighlightingDefinition definition)
            definition.ApplyPalette(palette);
        _editor.TextArea.TextView.Redraw();
    }

    private void AutoDetectTimer_Tick(object? sender, EventArgs e)
    {
        _autoDetectTimer.Stop();
        if (!_disposed && RegionGrammarResolver.ShouldAutoDetect(_fenceToken))
            RefreshHighlighting();
    }

    private void Editor_Unloaded(object sender, RoutedEventArgs e) => _autoDetectTimer.Stop();

    internal static bool ShouldBubbleMouseWheel(
        int delta, double verticalOffset, double extentHeight, double viewportHeight)
    {
        const double edgeTolerance = 0.5;
        if (delta == 0)
            return false;
        if (extentHeight <= viewportHeight)
            return true;

        return delta < 0
            ? verticalOffset >= extentHeight - viewportHeight - edgeTolerance
            : verticalOffset <= edgeTolerance;
    }

    private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!ShouldBubbleMouseWheel(
                e.Delta, _editor.VerticalOffset, _editor.ExtentHeight, _editor.ViewportHeight))
            return;

        // Start outside AvalonEdit's visual subtree so its ScrollViewer cannot swallow the wheel.
        var parent = VisualTreeHelper.GetParent(_editor);
        while (parent is not null && parent is not UIElement)
            parent = VisualTreeHelper.GetParent(parent);

        e.Handled = true;
        var args = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender,
        };
        (parent as UIElement)?.RaiseEvent(args);
    }

    private void Editor_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateEditorHeight();
        RefreshHighlighting();
    }

    private void Document_UpdateStarted(object? sender, EventArgs e)
    {
        if (!_disposed)
            TextChanging?.Invoke(this, e);
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
            _textChangedDuringUpdate = true;
    }

    private void Document_UpdateFinished(object? sender, EventArgs e)
    {
        if (_disposed || !_textChangedDuringUpdate)
            return;

        _textChangedDuringUpdate = false;
        UpdateEditorHeight();
        _autoDetectTimer.Stop();
        if (_editor.IsLoaded && RegionGrammarResolver.ShouldAutoDetect(_fenceToken))
            _autoDetectTimer.Start();
        TextChanged?.Invoke(this, e);
    }

    private void UpdateEditorHeight()
    {
        if (_disposed)
            return;

        var lineHeight = _editor.TextArea.TextView.DefaultLineHeight;
        if (double.IsNaN(lineHeight) || double.IsInfinity(lineHeight) || lineHeight <= 0)
            lineHeight = _editor.FontSize * FallbackLineHeightMultiplier;

        var lineCount = Math.Max(1, _editor.Document.LineCount);
        var desired = Math.Ceiling(
            lineCount * lineHeight +
            _editor.Padding.Top +
            _editor.Padding.Bottom +
            _editor.BorderThickness.Top +
            _editor.BorderThickness.Bottom +
            HeightSlack);

        var maxHeight = _editor.MaxHeight;
        if (!double.IsNaN(maxHeight) && !double.IsInfinity(maxHeight) && maxHeight > 0)
            desired = Math.Min(desired, maxHeight);

        _editor.Height = desired;
    }
}
