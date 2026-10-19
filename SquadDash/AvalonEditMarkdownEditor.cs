using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Search;

namespace SquadDash;

internal sealed class AvalonEditMarkdownEditor : DockPanel, IDisposable
{
    private static readonly TimeSpan PreviewDebounceInterval = TimeSpan.FromMilliseconds(350);

    private readonly RegionHighlightingEngine _highlighting = RegionHighlightingEngine.Default;
    private readonly ThemeResourcePaletteSource _paletteSource;
    private readonly TextBox _commandBridge;
    private readonly DispatcherTimer _previewDebounce;

    private readonly MarkdownEditorToolbar _toolbar;
    private readonly RoutedEventHandler _toolbarClickHandler;
    private readonly Button? _boldButton;
    private readonly Button? _italicButton;
    private readonly Button? _bulletButton;
    private readonly Button? _numberedButton;

    private bool _isDisposed;
    private bool _suppressNotifications;
    private bool _textChangedDuringUpdate;
    private bool _hasToolbarSelectionSnapshot;
    private int _toolbarSelectionStart;
    private int _toolbarSelectionLength;

    public AvalonEditMarkdownEditor()
    {
        LastChildFill = true;
        _paletteSource = new ThemeResourcePaletteSource(_highlighting);
        _commandBridge = new TextBox();

        _toolbar = new MarkdownEditorToolbar
        {
            ShowImageButton = false,
            ShowHrButton = true,
            Margin = new Thickness(0, 0, 0, 2),
        };
        _toolbarClickHandler = Toolbar_ButtonClick;
        _toolbar.AddHandler(ButtonBase.ClickEvent, _toolbarClickHandler, true);
        _toolbar.PreviewMouseDown += Toolbar_PreviewMouseDown;

        _boldButton = _toolbar.FindName("BoldButton") as Button;
        _italicButton = _toolbar.FindName("ItalicButton") as Button;
        _bulletButton = _toolbar.FindName("BulletListButton") as Button;
        _numberedButton = _toolbar.FindName("NumberedListButton") as Button;

        Editor = new TextEditor
        {
            WordWrap = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            ShowLineNumbers = false,
            FontFamily = SystemFonts.MessageFontFamily,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Editor.SetResourceReference(TextEditor.FontSizeProperty, "FontSizeBody");
        Editor.SetResourceReference(TextEditor.BorderBrushProperty, "SubtleBorder");
        Editor.Document.UndoStack.SizeLimit = 200;
        Editor.TextArea.PreviewKeyDown += Editor_PreviewKeyDown;
        Editor.TextArea.PreviewTextInput += Editor_PreviewTextInput;
        Editor.TextChanged += Editor_TextChanged;
        Editor.Document.UpdateFinished += Document_UpdateFinished;
        Editor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
        Editor.TextArea.SelectionChanged += TextArea_SelectionChanged;
        SearchPanel.Install(Editor.TextArea);
        Editor.TextArea.ContextMenu = BuildContextMenu();
        DataObject.AddPastingHandler(Editor, Editor_Pasting);

        DockPanel.SetDock(_toolbar, Dock.Top);
        Children.Add(_toolbar);
        var trialNote = new TextBlock
        {
            Text = "AvalonEdit trial: AI revision, dictation, case cycling, custom word navigation and preview hover highlights are not available.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        trialNote.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        trialNote.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        DockPanel.SetDock(trialNote, Dock.Top);
        Children.Add(trialNote);
        Children.Add(Editor);

        _previewDebounce = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = PreviewDebounceInterval,
        };
        _previewDebounce.Tick += PreviewDebounce_Tick;

        var markdownPalette = _paletteSource.CreatePalette();
        Editor.SyntaxHighlighting = _highlighting.CreateDefinition("Markdown", markdownPalette);
        ApplyPalette(markdownPalette);
        UpdateToolbarSelectionGates();
    }

    public TextEditor Editor { get; }

    public event EventHandler<string>? TextChanged;
    public event EventHandler<string>? PreviewUpdateRequested;

    public string GetText() => NormalizeLf(Editor.Document.Text);

    public void SetText(string? text)
    {
        ThrowIfDisposed();
        _previewDebounce.Stop();
        _suppressNotifications = true;
        try
        {
            var normalized = NormalizeLf(text);
            Editor.Document.Text = normalized;
            Editor.Document.UndoStack.ClearAll();
            Editor.Select(0, 0);
        }
        finally
        {
            _suppressNotifications = false;
        }

        UpdateToolbarSelectionGates();
    }

    public void RefreshTheme() => ApplyPalette(_paletteSource.CreatePalette());

    public void ApplyPalette(RegionEditorPalette palette)
    {
        ThrowIfDisposed();

        var importantText = Application.Current?.TryFindResource("ImportantText") as Brush;
        var textBrush = importantText ?? palette.Text;
        var effectivePalette = new RegionEditorPalette(palette.Surface, textBrush, palette.HighlightColors);

        Editor.Background = effectivePalette.Surface;
        Editor.Foreground = effectivePalette.Text;

        if (Editor.SyntaxHighlighting is RegionHighlightingDefinition definition)
        {
            definition.ApplyPalette(effectivePalette);
        }
        else
        {
            Editor.SyntaxHighlighting = _highlighting.CreateDefinition("Markdown", effectivePalette);
        }

        Editor.TextArea.TextView.Redraw();
    }

    public void FocusEditor()
    {
        ThrowIfDisposed();
        Editor.Focus();
        Editor.TextArea.Focus();
        Keyboard.Focus(Editor.TextArea);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _previewDebounce.Stop();
        _previewDebounce.Tick -= PreviewDebounce_Tick;
        _toolbar.RemoveHandler(ButtonBase.ClickEvent, _toolbarClickHandler);
        _toolbar.PreviewMouseDown -= Toolbar_PreviewMouseDown;
        Editor.TextChanged -= Editor_TextChanged;
        Editor.Document.UpdateFinished -= Document_UpdateFinished;
        DataObject.RemovePastingHandler(Editor, Editor_Pasting);
        Editor.TextArea.PreviewKeyDown -= Editor_PreviewKeyDown;
        Editor.TextArea.PreviewTextInput -= Editor_PreviewTextInput;
        Editor.TextArea.Caret.PositionChanged -= Caret_PositionChanged;
        Editor.TextArea.SelectionChanged -= TextArea_SelectionChanged;
        TextChanged = null;
        PreviewUpdateRequested = null;
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        _textChangedDuringUpdate = true;
    }

    private void Document_UpdateFinished(object? sender, EventArgs e)
    {
        if (!_textChangedDuringUpdate)
            return;
        _textChangedDuringUpdate = false;
        if (_suppressNotifications || _isDisposed)
            return;

        var text = GetText();
        TextChanged?.Invoke(this, text);
        _previewDebounce.Stop();
        _previewDebounce.Start();
        UpdateToolbarSelectionGates();
    }

    private void PreviewDebounce_Tick(object? sender, EventArgs e)
    {
        _previewDebounce.Stop();
        if (_isDisposed)
            return;
        PreviewUpdateRequested?.Invoke(this, GetText());
    }

    private void Caret_PositionChanged(object? sender, EventArgs e) => UpdateToolbarSelectionGates();

    private void TextArea_SelectionChanged(object? sender, EventArgs e)
    {
        _hasToolbarSelectionSnapshot = false;
        UpdateToolbarSelectionGates();
    }

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;

        if (e.Key == Key.Return && modifiers == ModifierKeys.None)
        {
            if (!ApplyBridgeMutation(MarkdownEditorCommands.ContinueListOnEnter))
                Editor.SelectedText = "\n";
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Return && modifiers == ModifierKeys.Shift)
        {
            ApplyBridgeMutation(MarkdownEditorCommands.DuplicateLine);
            e.Handled = true;
            return;
        }

        if ((modifiers & ModifierKeys.Control) == 0)
            return;

        if (e.Key == Key.Z && (modifiers & ModifierKeys.Shift) != 0)
        {
            Editor.Redo();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Y)
        {
            Editor.Redo();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Z)
        {
            Editor.Undo();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.B)
        {
            ApplyBridgeMutation(MarkdownEditorCommands.ApplyBold);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.I)
        {
            ApplyBridgeMutation(MarkdownEditorCommands.ApplyItalic);
            e.Handled = true;
        }
    }

    private void Editor_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (Editor.SelectionLength == 0)
            return;

        if (e.Text is "`" or "'")
        {
            e.Handled = ApplyBridgeMutation(MarkdownEditorCommands.ApplyInlineCodeOrFence);
            return;
        }
        if (e.Text == "\"")
        {
            e.Handled = ApplyBridgeMutation(MarkdownEditorCommands.ApplyInlineQuote);
            return;
        }
        if ((e.Text == "(" || e.Text == ")") &&
            !Editor.SelectedText.Contains('\n'))
        {
            if (ApplyBridgeMutation(box => MarkdownEditorCommands.ApplyInlineParens(box, e.Text)))
                e.Handled = true;
        }
    }

    private void Editor_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is string text)
        {
            e.DataObject = new DataObject(DataFormats.UnicodeText, NormalizeLf(text));
            e.FormatToApply = DataFormats.UnicodeText;
        }
    }

    private void Toolbar_ButtonClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;

        var button = FindAncestor<Button>(source);
        if (button is null)
            return;

        if (!TryRunToolbarCommand(button))
            return;

        e.Handled = true;
        _hasToolbarSelectionSnapshot = false;
        FocusEditor();
    }

    private bool TryRunToolbarCommand(Button button)
    {
        var name = button.Name ?? string.Empty;
        if (name == "BoldButton")
        {
            ApplyBridgeMutation(MarkdownEditorCommands.ApplyBold, useToolbarSnapshot: true);
            return true;
        }
        if (name == "ItalicButton")
        {
            ApplyBridgeMutation(MarkdownEditorCommands.ApplyItalic, useToolbarSnapshot: true);
            return true;
        }
        if (name == "BulletListButton")
        {
            ApplyBridgeMutation(MarkdownEditorCommands.ApplyBulletList, useToolbarSnapshot: true);
            return true;
        }
        if (name == "NumberedListButton")
        {
            ApplyBridgeMutation(MarkdownEditorCommands.ApplyNumberedList, useToolbarSnapshot: true);
            return true;
        }
        if (name == "HrButton")
        {
            ApplyBridgeMutation(MarkdownEditorCommands.InsertHorizontalRule, useToolbarSnapshot: true);
            return true;
        }

        var tooltip = button.ToolTip?.ToString() ?? string.Empty;
        if (tooltip.Contains("Insert link", StringComparison.OrdinalIgnoreCase))
        {
            ApplyBridgeMutation(MarkdownEditorCommands.InsertLink, useToolbarSnapshot: true);
            return true;
        }
        if (tooltip.Contains("Insert table", StringComparison.OrdinalIgnoreCase))
        {
            ApplyBridgeMutation(MarkdownEditorCommands.InsertTable, useToolbarSnapshot: true);
            return true;
        }
        if (tooltip.Contains("Smart code formatting", StringComparison.OrdinalIgnoreCase))
        {
            if (!ApplyBridgeMutation(MarkdownEditorCommands.ApplyInlineCodeOrFence, useToolbarSnapshot: true))
                ApplyBridgeMutation(MarkdownEditorCommands.InsertInlineCode, useToolbarSnapshot: true);
            return true;
        }

        return false;
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        menu.SetResourceReference(ContextMenu.StyleProperty, "ThemedContextMenuStyle");

        MenuItem CreateItem(string header, ICommand command)
        {
            var item = new MenuItem
            {
                Header = header,
                Command = command,
                CommandTarget = Editor.TextArea,
            };
            item.SetResourceReference(StyleProperty, "ThemedMenuItemStyle");
            return item;
        }

        menu.Items.Add(CreateItem("_Undo", ApplicationCommands.Undo));
        menu.Items.Add(CreateItem("_Redo", ApplicationCommands.Redo));

        var separator = new Separator();
        separator.SetResourceReference(StyleProperty, "ThemedMenuSeparatorStyle");
        menu.Items.Add(separator);

        menu.Items.Add(CreateItem("Cu_t", ApplicationCommands.Cut));
        menu.Items.Add(CreateItem("_Copy", ApplicationCommands.Copy));
        menu.Items.Add(CreateItem("_Paste", ApplicationCommands.Paste));
        return menu;
    }

    private bool ApplyBridgeMutation(Action<TextBox> action, bool useToolbarSnapshot = false)
    {
        ThrowIfDisposed();
        SyncBridgeFromEditor(useToolbarSnapshot);
        var beforeText = Editor.Document.Text;
        var beforeSelectionStart = _commandBridge.SelectionStart;
        var beforeSelectionLength = _commandBridge.SelectionLength;

        action(_commandBridge);
        return ApplyBridgeResult(beforeText, beforeSelectionStart, beforeSelectionLength);
    }

    private bool ApplyBridgeMutation(Func<TextBox, bool> action, bool useToolbarSnapshot = false)
    {
        ThrowIfDisposed();
        SyncBridgeFromEditor(useToolbarSnapshot);
        var beforeText = Editor.Document.Text;
        var beforeSelectionStart = _commandBridge.SelectionStart;
        var beforeSelectionLength = _commandBridge.SelectionLength;
        if (!action(_commandBridge))
            return false;
        return ApplyBridgeResult(beforeText, beforeSelectionStart, beforeSelectionLength);
    }

    private bool ApplyBridgeResult(string beforeText, int beforeSelectionStart, int beforeSelectionLength)
    {
        var afterText = NormalizeLf(_commandBridge.Text);
        var afterSelectionStart = Clamp(_commandBridge.SelectionStart, 0, afterText.Length);
        var afterSelectionLength = Clamp(_commandBridge.SelectionLength, 0, afterText.Length - afterSelectionStart);

        if (beforeText == afterText &&
            beforeSelectionStart == afterSelectionStart &&
            beforeSelectionLength == afterSelectionLength)
        {
            return false;
        }

        var (offset, removeLength, insertText) = ComputeDiff(beforeText, afterText);
        using (Editor.Document.RunUpdate())
        {
            Editor.Document.Replace(offset, removeLength, insertText);
        }

        Editor.Select(afterSelectionStart, afterSelectionLength);
        if (afterSelectionLength == 0)
            Editor.CaretOffset = afterSelectionStart;
        return true;
    }

    private void SyncBridgeFromEditor(bool useToolbarSnapshot)
    {
        // Reuse the existing formatting rules only on command, then patch their result into
        // Avalon's document so normal typing and undo never depend on a mirrored editor.
        _commandBridge.Text = GetText();
        var selectionStart = Editor.SelectionStart;
        var selectionLength = Editor.SelectionLength;
        if (useToolbarSnapshot && _hasToolbarSelectionSnapshot)
        {
            selectionStart = _toolbarSelectionStart;
            selectionLength = _toolbarSelectionLength;
        }
        var rawText = Editor.Document.Text;
        var selectionEnd = Math.Clamp(selectionStart + selectionLength, 0, rawText.Length);
        selectionStart = NormalizeLf(rawText[..Math.Clamp(selectionStart, 0, rawText.Length)]).Length;
        selectionLength = NormalizeLf(rawText[..selectionEnd]).Length - selectionStart;
        _commandBridge.SelectionStart = selectionStart;
        _commandBridge.SelectionLength = selectionLength;
        if (selectionLength == 0)
            _commandBridge.CaretIndex = NormalizeLf(rawText[..Editor.CaretOffset]).Length;
    }

    private void Toolbar_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _toolbarSelectionStart = Editor.SelectionStart;
        _toolbarSelectionLength = Editor.SelectionLength;
        _hasToolbarSelectionSnapshot = _toolbarSelectionLength > 0;
    }

    private void UpdateToolbarSelectionGates()
    {
        var hasSelection = Editor.SelectionLength > 0;
        if (_boldButton is not null)
            _boldButton.IsEnabled = hasSelection;
        if (_italicButton is not null)
            _italicButton.IsEnabled = hasSelection;
        if (_bulletButton is not null)
            _bulletButton.IsEnabled = hasSelection;
        if (_numberedButton is not null)
            _numberedButton.IsEnabled = hasSelection;
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(AvalonEditMarkdownEditor));
    }

    private static string NormalizeLf(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");

    private static (int Offset, int RemoveLength, string InsertText) ComputeDiff(string before, string after)
    {
        var prefix = 0;
        var maxPrefix = Math.Min(before.Length, after.Length);
        while (prefix < maxPrefix && before[prefix] == after[prefix])
            prefix++;

        var beforeSuffix = before.Length - 1;
        var afterSuffix = after.Length - 1;
        while (beforeSuffix >= prefix &&
               afterSuffix >= prefix &&
               before[beforeSuffix] == after[afterSuffix])
        {
            beforeSuffix--;
            afterSuffix--;
        }

        var removeLength = Math.Max(0, beforeSuffix - prefix + 1);
        var insertText = afterSuffix >= prefix
            ? after.Substring(prefix, afterSuffix - prefix + 1)
            : string.Empty;

        return (prefix, removeLength, insertText);
    }

    private static int Clamp(int value, int minInclusive, int maxInclusive)
    {
        return Math.Clamp(value, minInclusive, maxInclusive);
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        var current = start;
        while (current is not null)
        {
            if (current is T typed)
                return typed;
            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }
        return null;
    }
}
