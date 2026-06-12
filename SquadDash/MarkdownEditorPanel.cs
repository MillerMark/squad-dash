using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// Reusable WPF panel that combines a markdown-aware <see cref="RichTextBox"/> editor
/// with a <see cref="MarkdownEditorToolbar"/>.
/// <para>
/// Wires the standard markdown keyboard shortcuts (Ctrl+B, Ctrl+I, Enter for list
/// continuation, backtick-embedding), uses WPF built-in undo/redo, and surfaces a
/// debounced <see cref="PreviewUpdateRequested"/> event suitable for triggering
/// preview rebuilds.
/// </para>
/// </summary>
internal sealed class MarkdownEditorPanel : DockPanel
{
    // ── Public surface ────────────────────────────────────────────────────────

    /// <summary>The underlying RichTextBox; exposed for callers that need direct access (e.g. PTT, hover highlights).</summary>
    public RichTextBox EditorBox { get; }

    /// <summary>The formatting toolbar; exposed for callers that need to add buttons or change visibility.</summary>
    public MarkdownEditorToolbar Toolbar { get; }

    /// <summary>Returns the editor content as a plain-text string with '\n' line endings.</summary>
    public string GetText() => EditorBox.GetPlainText();

    /// <summary>Replaces the editor content without triggering <see cref="TextChanged"/>.</summary>
    public void SetText(string? text)
    {
        _settingText = true;
        try { EditorBox.SetPlainText(text); }
        finally { _settingText = false; }
    }

    /// <summary>Fired synchronously on every text change (not debounced).</summary>
    public event EventHandler<string>? TextChanged;

    /// <summary>
    /// Fired after a short debounce (~350 ms) — suitable for triggering preview rebuilds
    /// without updating on every keystroke.
    /// </summary>
    public event EventHandler<string>? PreviewUpdateRequested;

    // ── Private state ─────────────────────────────────────────────────────────

    private bool _settingText;
    private bool _suppressNextTextInput;
    private DispatcherTimer? _debounce;
    private readonly CamelCaseRichTextBoxAttachment _camelCaseNav = new();

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="showImageButton">Show the image-insert button in the toolbar.</param>
    /// <param name="showHrButton">Show the horizontal-rule button in the toolbar.</param>
    internal MarkdownEditorPanel(bool showImageButton = false, bool showHrButton = false)
    {
        LastChildFill = true;

        EditorBox = new RichTextBox
        {
            AcceptsReturn = true,
            AcceptsTab    = true,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontFamily    = new System.Windows.Media.FontFamily("Consolas, Courier New"),
            IsUndoEnabled = true,
            UndoLimit     = 200,
        };
        EditorBox.SetResourceReference(RichTextBox.FontSizeProperty,    "FontSizeBody");
        EditorBox.SetResourceReference(RichTextBox.ForegroundProperty,  "LabelText");
        EditorBox.SetResourceReference(RichTextBox.BackgroundProperty,  "RosterPanelSurface");
        EditorBox.SetResourceReference(RichTextBox.BorderBrushProperty, "SubtleBorder");

        EditorBox.TextChanged      += OnEditorTextChanged;
        EditorBox.PreviewKeyDown   += OnEditorPreviewKeyDown;
        EditorBox.PreviewTextInput += OnEditorPreviewTextInput;

        Toolbar = new MarkdownEditorToolbar
        {
            TargetRichTextBox = EditorBox,
            ShowImageButton   = showImageButton,
            ShowHrButton      = showHrButton,
            Margin            = new Thickness(0, 0, 0, 2),
        };

        DockPanel.SetDock(Toolbar, Dock.Top);
        Children.Add(Toolbar);
        Children.Add(EditorBox);
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnEditorTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_settingText) return;
        var text = EditorBox.GetPlainText();
        TextChanged?.Invoke(this, text);
        ScheduleDebounce();
    }

    private void ScheduleDebounce()
    {
        _debounce?.Stop();
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _debounce.Tick += (_, _) =>
        {
            _debounce?.Stop();
            _debounce = null;
            PreviewUpdateRequested?.Invoke(this, EditorBox.GetPlainText());
        };
        _debounce.Start();
    }

    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_camelCaseNav.HandlePreviewKeyDown(e, EditorBox)) { e.Handled = true; return; }

        var modifiers = Keyboard.Modifiers;

        if (e.Key == Key.Enter && modifiers == ModifierKeys.None)
        {
            if (MarkdownEditorCommands.ContinueListOnEnter(EditorBox))
            {
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.B && modifiers == ModifierKeys.Control)
        {
            MarkdownEditorCommands.ApplyBold(EditorBox);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.I && modifiers == ModifierKeys.Control)
        {
            MarkdownEditorCommands.ApplyItalic(EditorBox);
            e.Handled = true;
            return;
        }

        // Backtick with selection: inline code or fence
        if (e.Key == Key.OemTilde && modifiers == ModifierKeys.None && !EditorBox.Selection.IsEmpty)
        {
            if (MarkdownEditorCommands.ApplyInlineCodeOrFence(EditorBox))
            {
                _suppressNextTextInput = true;
                e.Handled = true;
                return;
            }
        }
    }

    private void OnEditorPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_suppressNextTextInput)
        {
            _suppressNextTextInput = false;
            e.Handled = true;
        }
    }
}
