using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Windows.Media.Imaging;
using SquadDash.Screenshots;

namespace SquadDash;

internal sealed record MarkdownDocumentSpec(string TabTitle, string FilePath);

internal sealed class MarkdownDocumentWindow : Window {
    private static readonly List<MarkdownDocumentWindow> _openWindows = [];

    public static void RefreshAllOpenWindows() {
        foreach (var window in _openWindows)
            window.RefreshTheme();
    }

    private void RefreshTheme() {
        foreach (var document in _documents)
            RenderPreview(document, preserveScroll: true);
    }

    private readonly string _baseTitle;
    private readonly List<MarkdownDocumentTabState> _documents;
    private readonly List<MarkdownDocumentTabState> _allTrackedDocuments = [];
    private readonly DockPanel _rootPanel;
    private readonly Button _saveButton;
    private readonly Button _showSourceButton;
    private readonly TextBlock _statusTextBlock;
    private readonly Grid _contentGrid;
    private readonly ContentControl _singlePreviewHost;
    private readonly TabControl _tabControl;
    private readonly GridSplitter _splitter;
    private readonly Border _sourceBorder;
    private readonly Grid _sourceEditorHost;
    private readonly Border _sourceToolbarBorder;
    private Button? _srcBoldButton;
    private Button? _srcItalicButton;
    private bool _showSource;
    private bool _isSwitchingDocument;
    private bool _isClosingAfterPrompt;
    private MarkdownDocumentTabState? _activeDocument;
    private Button? _backButton;
    private readonly Stack<string> _navigationHistory = new();
    private Border _reloadFlashBorder = null!;
    private Canvas? _sourceOverlayCanvas;
    private System.Windows.Shapes.Rectangle? _sourceHoverHighlight;
    private DispatcherTimer? _sourceHoverTimer;

    // ── Editor voice / PTT ─────────────────────────────────────────────────
    private SpeechRecognitionService? _editorVoiceService;
    private PushToTalkWindow?         _editorPttWindow;
    private bool                      _editorVoiceStopOnCtrlRelease;
    private int                       _editorVoiceCaretIndex;
    private readonly CtrlDoubleTapGestureTracker _editorPttGesture =
        new(maxTapHoldMs: 250, doubleTapGapMs: 350);

    private MarkdownDocumentCaptureContext? _captureContext;

    private MarkdownDocumentWindow(string title, IReadOnlyList<MarkdownDocumentSpec> documents) {
        if (documents.Count == 0)
            throw new ArgumentException("At least one markdown document is required.", nameof(documents));

        _baseTitle = title;
        _documents = documents
            .Select(spec => MarkdownDocumentTabState.Load(spec.TabTitle, spec.FilePath))
            .ToList();

        Title = title;
        Width = 1120;
        Height = 820;
        MinWidth = 760;
        MinHeight = 560;
        this.SetResourceReference(BackgroundProperty, "AppSurface");

        _rootPanel = new DockPanel();
        Content = _rootPanel;

        var toolBar = new DockPanel {
            Margin = new Thickness(12, 12, 12, 8),
            LastChildFill = true
        };
        DockPanel.SetDock(toolBar, Dock.Top);
        _rootPanel.Children.Add(toolBar);

        var actionPanel = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(actionPanel, Dock.Right);
        toolBar.Children.Add(actionPanel);

        _backButton = new Button {
            Content = "← Back",
            MinWidth = 80,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = false
        };
        _backButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        _backButton.Click += BackButton_Click;
        actionPanel.Children.Add(_backButton);

        _showSourceButton = new Button {
            Content = "Show Source",
            MinWidth = 108,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _showSourceButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        _showSourceButton.Click += ShowSourceButton_Click;
        actionPanel.Children.Add(_showSourceButton);

        _saveButton = new Button {
            Content = "Save",
            Width = 88,
            Height = 30,
            IsEnabled = false
        };
        _saveButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        _saveButton.Click += SaveButton_Click;
        actionPanel.Children.Add(_saveButton);

        _statusTextBlock = new TextBlock {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        _statusTextBlock.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        toolBar.Children.Add(_statusTextBlock);

        _contentGrid = new Grid {
            Margin = new Thickness(12, 0, 12, 12)
        };
        _contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        _contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        _rootPanel.Children.Add(_contentGrid);

        _singlePreviewHost = new ContentControl();
        Grid.SetColumn(_singlePreviewHost, 0);
        _contentGrid.Children.Add(_singlePreviewHost);

        _reloadFlashBorder = new Border {
            BorderThickness = new Thickness(0),
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetColumn(_reloadFlashBorder, 0);
        _contentGrid.Children.Add(_reloadFlashBorder);

        _tabControl = new TabControl {
            Visibility = _documents.Count > 1 ? Visibility.Visible : Visibility.Collapsed
        };
        _tabControl.SelectionChanged += TabControl_SelectionChanged;
        Grid.SetColumn(_tabControl, 0);
        _contentGrid.Children.Add(_tabControl);

        foreach (var document in _documents) {
            var tabItem = new TabItem {
                Content = document.PreviewHost,
                Tag = document
            };
            document.TabItem = tabItem;
            _tabControl.Items.Add(tabItem);
        }

        _splitter = new GridSplitter {
            Width = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };
        _splitter.SetResourceReference(BackgroundProperty, "PanelBorder");
        Grid.SetColumn(_splitter, 1);
        _contentGrid.Children.Add(_splitter);

        _sourceEditorHost = new Grid();
        foreach (var document in _documents) {
            document.EditorTextBox.Tag = document;
            document.EditorTextBox.ContextMenu = BuildSourceEditorContextMenu(document.EditorTextBox);
            document.EditorTextBox.TextChanged += EditorTextBox_TextChanged;
            _sourceEditorHost.Children.Add(document.EditorTextBox);
        }

        _sourceBorder = new Border {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8),
            Child = _sourceEditorHost,
        };
        _sourceBorder.SetResourceReference(Border.BackgroundProperty, "InputSurface");
        _sourceBorder.SetResourceReference(Border.BorderBrushProperty, "InputBorder");

        _srcBoldButton   = MakeToolbarButton("B",  "Bold (Ctrl+B)",        bold:   true, enabled: false);
        _srcItalicButton = MakeToolbarButton("I",  "Italic (Ctrl+I)",      italic: true, enabled: false);
        var srcLinkBtn   = MakeToolbarButton("Link", "Insert link",         enabled: true);
        var srcTableBtn  = MakeToolbarButton("Table", "Insert table",       enabled: true);
        var srcCodeBtn   = MakeToolbarButton("`code`", "Insert inline code", enabled: true);
        var srcBlockBtn  = MakeToolbarButton("{ }", "Insert code block",    enabled: true);
        var srcHrBtn     = MakeToolbarButton("—",   "Insert horizontal rule (---)", enabled: true);

        foreach (var document in _documents) {
            document.EditorTextBox.SelectionChanged += EditorTextBox_SelectionChanged;
            document.EditorTextBox.PreviewKeyDown   += EditorTextBox_PreviewKeyDown;
            document.EditorTextBox.ContextMenuOpening += EditorTextBox_ContextMenuOpening;
        }

        var tbStack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(0, 0, 0, 6) };
        foreach (var btn in new[] { (Button)_srcBoldButton, _srcItalicButton, srcLinkBtn, srcTableBtn, srcCodeBtn, srcBlockBtn, srcHrBtn })
            tbStack.Children.Add(btn);

        var sourceColumnPanel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(tbStack, System.Windows.Controls.Dock.Top);
        sourceColumnPanel.Children.Add(tbStack);
        sourceColumnPanel.Children.Add(_sourceBorder);

        _sourceToolbarBorder = new Border { Child = sourceColumnPanel, Visibility = Visibility.Collapsed };
        Grid.SetColumn(_sourceToolbarBorder, 2);
        _contentGrid.Children.Add(_sourceToolbarBorder);

        Closing += MarkdownDocumentWindow_Closing;

        foreach (var document in _documents)
            SetupWebBrowser(document.WebBrowser);

        _allTrackedDocuments.AddRange(_documents);
        foreach (var document in _documents)
            SetupFileWatcher(document);

        Closed += (_, _) => DisposeAllFileWatchers();
        Closed += (_, _) => _ = StopEditorVoiceAsync();

        PreviewKeyDown += MarkdownDocumentWindow_PreviewKeyDown;
        PreviewKeyUp   += MarkdownDocumentWindow_PreviewKeyUp;
        PreviewMouseDown += MarkdownDocumentWindow_PreviewMouseDown;

        ActivateDocument(_documents[0], preserveCurrentState: false);
        UpdatePreviewHostVisibility();
        UpdateSourcePaneVisibility();
        UpdateChrome();
    }

    private bool _autoSave;

    public static void Show(Window? owner, string title, string filePath, bool showSource = false,
        MarkdownDocumentCaptureContext? captureContext = null, bool autoSave = false) {
        // If a window already has this file open, bring it to the front instead of opening a duplicate.
        var existing = _openWindows.FirstOrDefault(w =>
            w._documents.Any(d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase)));
        if (existing is not null) {
            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        Show(owner, title, [new MarkdownDocumentSpec(Path.GetFileNameWithoutExtension(filePath), filePath)], showSource, captureContext, autoSave);
    }

    public static void Show(Window? owner, string title, IReadOnlyList<MarkdownDocumentSpec> documents, bool showSource = false,
        MarkdownDocumentCaptureContext? captureContext = null, bool autoSave = false) {
        var window = new MarkdownDocumentWindow(title, documents);
        window._captureContext = captureContext;
        window._autoSave       = autoSave;
        if (owner is not null)
            window.Owner = owner;

        if (showSource) {
            window._showSource = true;
            window.UpdateSourcePaneVisibility();
            window.UpdateEditorFromActiveDocument();
        }

        _openWindows.Add(window);
        window.Closed += (_, _) => _openWindows.Remove(window);
        window.Show();
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (_isSwitchingDocument || _tabControl.SelectedItem is not TabItem { Tag: MarkdownDocumentTabState document })
            return;

        ActivateDocument(document, preserveCurrentState: true);
    }

    private void ShowSourceButton_Click(object sender, RoutedEventArgs e) {
        _showSource = !_showSource;
        UpdateSourcePaneVisibility();
        UpdateEditorFromActiveDocument();
        if (_showSource && _activeDocument is not null)
            TryInjectHoverScript(_activeDocument.WebBrowser);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e) {
        if (_activeDocument is null)
            return;

        SaveDocument(_activeDocument);
    }

    private void EditorTextBox_TextChanged(object sender, TextChangedEventArgs e) {
        if (_isSwitchingDocument || sender is not TextBox { Tag: MarkdownDocumentTabState document } editorTextBox)
            return;

        document.WorkingText = editorTextBox.Text;
        document.IsDirty = !string.Equals(document.WorkingText, document.SavedText, StringComparison.Ordinal);
        RenderPreview(document, preserveScroll: true);
        if (_autoSave && document.IsDirty)
            AutoSaveDocument(document);
        UpdateChrome();
    }

    private void EditorTextBox_SelectionChanged(object sender, System.Windows.RoutedEventArgs e) {
        if (sender is not TextBox { Tag: MarkdownDocumentTabState doc } tb || !ReferenceEquals(doc, _activeDocument))
            return;
        var hasSelection = tb.SelectionLength > 0;
        if (_srcBoldButton   is not null) _srcBoldButton.IsEnabled   = hasSelection;
        if (_srcItalicButton is not null) _srcItalicButton.IsEnabled = hasSelection;
    }

    private void EditorTextBox_ContextMenuOpening(object sender, ContextMenuEventArgs e) {
        if (sender is not TextBox tb || tb.ContextMenu is null) return;

        // Remove any previously-injected "Add to Notes" items so they don't stack
        for (int i = tb.ContextMenu.Items.Count - 1; i >= 0; i--) {
            if (tb.ContextMenu.Items[i] is MenuItem { Tag: "AddToNotes" } ||
                tb.ContextMenu.Items[i] is Separator { Tag: "AddToNotesSep" })
                tb.ContextMenu.Items.RemoveAt(i);
        }

        // Cut/Copy/Paste enabled state
        foreach (var obj in tb.ContextMenu.Items) {
            if (obj is not MenuItem mi) continue;
            if (mi.Command == ApplicationCommands.Cut   || mi.Command == ApplicationCommands.Copy)
                mi.IsEnabled = tb.SelectionLength > 0;
            if (mi.Command == ApplicationCommands.Paste)
                mi.IsEnabled = Clipboard.ContainsText();
        }

        // Add "Add to Notes" if callback is set and there's a selection
        if (_captureContext?.AddToNotesCallback is { } callback && tb.SelectionLength > 0) {
            var sep = new Separator { Tag = "AddToNotesSep" };
            sep.SetResourceReference(Separator.StyleProperty, "ThemedMenuSeparatorStyle");

            var noteItem = new MenuItem { Header = "Add to Notes", Tag = "AddToNotes" };
            noteItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
            noteItem.Click += (_, _) => {
                var text = tb.SelectedText;
                if (!string.IsNullOrWhiteSpace(text))
                    callback(text);
            };

            tb.ContextMenu.Items.Add(sep);
            tb.ContextMenu.Items.Add(noteItem);
        }

        // Remove any previously-injected "Revise with AI" items
        for (int i = tb.ContextMenu.Items.Count - 1; i >= 0; i--) {
            if (tb.ContextMenu.Items[i] is MenuItem { Tag: "ReviseWithAi" } ||
                tb.ContextMenu.Items[i] is Separator { Tag: "ReviseWithAiSep" })
                tb.ContextMenu.Items.RemoveAt(i);
        }

        // Add "Revise with AI" if callback is set and there's a selection
        if (_captureContext?.ReviseWithAiCallback is { } reviseCallback && tb.SelectionLength > 0) {
            var sep2 = new Separator { Tag = "ReviseWithAiSep" };
            sep2.SetResourceReference(Separator.StyleProperty, "ThemedMenuSeparatorStyle");

            var reviseItem = new MenuItem { Header = "✏ _Revise with AI", Tag = "ReviseWithAi" };
            reviseItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
            reviseItem.Click += (_, _) => {
                if (tb.SelectionLength == 0) return;
                var doc          = tb.Tag as MarkdownDocumentTabState;
                var selectedText = tb.SelectedText;
                var fullText     = tb.Text;
                var selStart     = tb.SelectionStart;
                var selLen       = tb.SelectionLength;
                var cwd          = doc is not null ? System.IO.Path.GetDirectoryName(doc.FilePath) ?? "" : "";
                var docPath      = doc?.FilePath ?? "";

                var popup = new DocRevisePopup(
                    selectedText,
                    fullText,
                    docPath,
                    reviseCallback);

                var mousePos  = Mouse.GetPosition(this);
                var screenPos = PointToScreen(mousePos);
                popup.Left  = Math.Min(screenPos.X - 10, SystemParameters.PrimaryScreenWidth  - 360);
                popup.Top   = Math.Max(screenPos.Y - 160, 0);
                popup.Owner = this;

                if (popup.ShowDialog() == true && popup.RevisedText is { } revised) {
                    tb.SelectionStart  = selStart;
                    tb.SelectionLength = selLen;
                    tb.SelectedText    = revised;
                }
            };

            tb.ContextMenu.Items.Add(sep2);
            tb.ContextMenu.Items.Add(reviseItem);
        }
    }

    private static ContextMenu BuildSourceEditorContextMenu(TextBox tb) {
        var menu = new ContextMenu();
        menu.SetResourceReference(ContextMenu.StyleProperty, "ThemedContextMenuStyle");

        var cutItem = new MenuItem {
            Header = "Cu_t",
            Command = ApplicationCommands.Cut,
            CommandTarget = tb,
        };
        cutItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");

        var copyItem = new MenuItem {
            Header = "_Copy",
            Command = ApplicationCommands.Copy,
            CommandTarget = tb,
        };
        copyItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");

        var pasteItem = new MenuItem {
            Header = "_Paste",
            Command = ApplicationCommands.Paste,
            CommandTarget = tb,
        };
        pasteItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");

        menu.Items.Add(cutItem);
        menu.Items.Add(copyItem);
        menu.Items.Add(pasteItem);

        return menu;
    }

    private void EditorTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
        if (sender is not TextBox tb) return;
        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0) return;
        if (e.Key == System.Windows.Input.Key.B) {
            MarkdownEditorCommands.ApplyBold(tb);
            e.Handled = true;
        } else if (e.Key == System.Windows.Input.Key.I) {
            MarkdownEditorCommands.ApplyItalic(tb);
            e.Handled = true;
        } else if (e.Key == System.Windows.Input.Key.S && _activeDocument is not null) {
            SaveDocument(_activeDocument);
            e.Handled = true;
        }
    }

    // ── Editor PTT (double-tap Ctrl voice) ────────────────────────────────

    private void MarkdownDocumentWindow_PreviewKeyDown(object sender, KeyEventArgs e) {
        if (!_showSource) return;
        var editorTb = _activeDocument?.EditorTextBox;
        if (editorTb is null || !editorTb.IsKeyboardFocusWithin) return;

        var action = _editorPttGesture.HandleKeyDown(e.Key, e.IsRepeat, DateTime.UtcNow);
        if (action != CtrlDoubleTapGestureAction.Triggered) return;

        if (_editorVoiceService is null) {
            _editorVoiceStopOnCtrlRelease = true;
            _ = StartEditorVoiceAsync();
        } else {
            _ = StopEditorVoiceAsync();
        }
        e.Handled = true;
    }

    private void MarkdownDocumentWindow_PreviewKeyUp(object sender, KeyEventArgs e) {
        if (!CtrlDoubleTapGestureTracker.IsCtrlKey(e.Key)) return;

        if (_editorVoiceStopOnCtrlRelease && (_editorVoiceService is not null || _editorPttWindow is not null)) {
            _ = StopEditorVoiceAsync();
            e.Handled = true;
            return;
        }

        _editorPttGesture.HandleKeyUp(e.Key, DateTime.UtcNow);
    }

    private async Task StartEditorVoiceAsync() {
        var key    = Environment.GetEnvironmentVariable("SQUAD_SPEECH_KEY", EnvironmentVariableTarget.User);
        var region = new ApplicationSettingsStore().Load().SpeechRegion;

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region)) {
            _editorVoiceStopOnCtrlRelease = false;
            _editorPttGesture.Reset();
            return;
        }

        var editorTb = _activeDocument?.EditorTextBox;
        if (editorTb is null) return;

        _editorVoiceCaretIndex = editorTb.SelectionStart;

        _editorVoiceService = new SpeechRecognitionService();

        _editorVoiceService.PhraseRecognized += (_, text) =>
            Dispatcher.BeginInvoke(() => AppendSpeechToEditor(text));

        _editorVoiceService.VolumeChanged += (_, level) =>
            Dispatcher.BeginInvoke(() => {
                if (_editorPttWindow is not null)
                    _editorPttWindow.VolumeBar.Height = Math.Max(2, level * 36);
            });

        _editorVoiceService.RecognitionError += (_, _) =>
            Dispatcher.BeginInvoke(() => _ = StopEditorVoiceAsync());

        try {
            // Get caret position in physical screen pixels (PointToScreen returns physical coords).
            System.Windows.Point physicalPt;
            try {
                var caretRect = editorTb.GetRectFromCharacterIndex(_editorVoiceCaretIndex);
                physicalPt = editorTb.PointToScreen(new System.Windows.Point(caretRect.Left, caretRect.Bottom));
            } catch {
                physicalPt = editorTb.PointToScreen(new System.Windows.Point(0, editorTb.ActualHeight + 4));
            }

            // Get the work area for the monitor containing the caret (physical pixels).
            var physWa = NativeMethods.GetWorkAreaForPhysicalPoint((int)physicalPt.X, (int)physicalPt.Y);

            // Convert to WPF logical DIPs before passing to PositionUnderCaret.
            var logicalPt       = DpiHelper.PhysicalToLogical(editorTb, physicalPt);
            var logicalWaOrigin = DpiHelper.PhysicalToLogical(editorTb, new System.Windows.Point(physWa.Left, physWa.Top));
            var logicalWaCorner = DpiHelper.PhysicalToLogical(editorTb, new System.Windows.Point(physWa.Right, physWa.Bottom));
            var logicalWorkArea = new System.Windows.Rect(logicalWaOrigin, logicalWaCorner);

            _editorPttWindow = new PushToTalkWindow(this, showHint: false);
            _editorPttWindow.PositionUnderCaret(logicalPt, logicalWorkArea);
            _editorPttWindow.Show();
            editorTb.Focus();

            await _editorVoiceService.StartAsync(key, region).ConfigureAwait(false);
        }
        catch {
            await Dispatcher.InvokeAsync(() => {
                _editorPttWindow?.Close();
                _editorPttWindow = null;
            });
            _editorVoiceService?.Dispose();
            _editorVoiceService = null;
            _editorVoiceStopOnCtrlRelease = false;
            _editorPttGesture.Reset();
        }
    }

    private async Task StopEditorVoiceAsync() {
        await Dispatcher.InvokeAsync(() => {
            _editorPttWindow?.Close();
            _editorPttWindow = null;
        });

        var service = _editorVoiceService;
        _editorVoiceService = null;

        if (service is not null) {
            try { await service.StopAsync().ConfigureAwait(false); } catch { }
            service.Dispose();
        }

        _editorVoiceStopOnCtrlRelease = false;
        _editorPttGesture.Reset();
    }

    private void AppendSpeechToEditor(string text) {
        var editorTb = _activeDocument?.EditorTextBox;
        if (editorTb is null) return;

        var current   = editorTb.Text;
        var caretIndex = Math.Min(_editorVoiceCaretIndex, current.Length);
        var left       = current[..caretIndex];
        var right      = current[caretIndex..];
        var prefix     = caretIndex > 0 && current[caretIndex - 1] != ' ' ? " " : string.Empty;
        var processed  = VoiceInsertionHeuristics.Apply(left, text, right);
        var insert     = prefix + processed;
        editorTb.Text       = left + insert + right;
        editorTb.CaretIndex = caretIndex + insert.Length;
        _editorVoiceCaretIndex = caretIndex + insert.Length;
    }


    private Button MakeToolbarButton(string label, string tooltip, bool bold = false, bool italic = false, bool enabled = true) {
        var btn = new Button {
            Content = label,
            Width   = 28,
            Height  = 24,
            Margin  = new System.Windows.Thickness(0, 0, 3, 0),
            ToolTip = tooltip,
            IsEnabled = enabled,
            FontWeight = bold   ? System.Windows.FontWeights.Bold   : System.Windows.FontWeights.Normal,
            FontStyle  = italic ? System.Windows.FontStyles.Italic  : System.Windows.FontStyles.Normal,
        };
        btn.SetResourceReference(StyleProperty, "ThemedButtonStyle");
        btn.Click += (_, _) => OnToolbarButtonClick(label);
        return btn;
    }

    private void OnToolbarButtonClick(string label) {
        var tb = _activeDocument?.EditorTextBox;
        if (tb is null) return;
        switch (label) {
            case "B":       MarkdownEditorCommands.ApplyBold(tb);            break;
            case "I":       MarkdownEditorCommands.ApplyItalic(tb);          break;
            case "Link":    MarkdownEditorCommands.InsertLink(tb);           break;
            case "Table":   MarkdownEditorCommands.InsertTable(tb);          break;
            case "`code`":  MarkdownEditorCommands.InsertInlineCode(tb);     break;
            case "{ }":     MarkdownEditorCommands.InsertCodeBlock(tb);      break;
            case "—":       MarkdownEditorCommands.InsertHorizontalRule(tb); break;
        }
        tb.Focus();
    }

    private void MarkdownDocumentWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e) {
        if (_isClosingAfterPrompt)
            return;

        var dirtyDocuments = _documents.Where(document => document.IsDirty).ToArray();
        if (dirtyDocuments.Length == 0)
            return;

        var message = dirtyDocuments.Length == 1
            ? $"Save changes to {dirtyDocuments[0].FileName} before closing?"
            : $"Save changes to {dirtyDocuments.Length} markdown files before closing?";
        var result = MessageBox.Show(
            this,
            message,
            "Unsaved Markdown Changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel) {
            e.Cancel = true;
            return;
        }

        if (result == MessageBoxResult.Yes) {
            foreach (var document in dirtyDocuments)
                SaveDocument(document);
        }

        _isClosingAfterPrompt = true;
    }

    private void ActivateDocument(MarkdownDocumentTabState document, bool preserveCurrentState) {
        ClearSourceHoverHighlight();
        _activeDocument = document;

        _isSwitchingDocument = true;
        try {
            if (_documents.Count > 1) {
                var selectedDocument = (_tabControl.SelectedItem as TabItem)?.Tag as MarkdownDocumentTabState;
                if (!ReferenceEquals(selectedDocument, document))
                    _tabControl.SelectedItem = document.TabItem;
            }

            if (_documents.Count == 1)
                _singlePreviewHost.Content = document.PreviewHost;

            RenderPreview(document);
            UpdateEditorFromActiveDocument();
            UpdateChrome();
        }
        finally {
            _isSwitchingDocument = false;
        }
    }

    private void UpdatePreviewHostVisibility() {
        _singlePreviewHost.Visibility = _documents.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
        _tabControl.Visibility = _documents.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateSourcePaneVisibility() {
        var sourceVisible = _showSource;
        _contentGrid.ColumnDefinitions[1].Width = sourceVisible ? new GridLength(6) : new GridLength(0);
        _contentGrid.ColumnDefinitions[2].Width = sourceVisible ? new GridLength(0.95, GridUnitType.Star) : new GridLength(0);
        _splitter.Visibility = sourceVisible ? Visibility.Visible : Visibility.Collapsed;
        _sourceToolbarBorder.Visibility = sourceVisible ? Visibility.Visible : Visibility.Collapsed;
        _showSourceButton.Content = sourceVisible ? "Hide Source" : "Show Source";
    }

    private void UpdateEditorFromActiveDocument() {
        if (_activeDocument is null || !_showSource)
            return;

        foreach (UIElement child in _sourceEditorHost.Children) {
            if (child is TextBox { Tag: MarkdownDocumentTabState doc } tb)
                tb.Visibility = ReferenceEquals(doc, _activeDocument) ? Visibility.Visible : Visibility.Collapsed;
        }

        Dispatcher.BeginInvoke(new Action(() => {
            if (!_showSource || _activeDocument is null)
                return;

            _activeDocument.EditorTextBox.Focus();
            Keyboard.Focus(_activeDocument.EditorTextBox);
        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void SaveDocument(MarkdownDocumentTabState document) {
        document.WorkingText = document.EditorTextBox.Text;
        File.WriteAllText(document.FilePath, document.FrontMatter + document.WorkingText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        document.SavedText = document.WorkingText;
        document.IsDirty = false;
        RenderPreview(document, preserveScroll: true);
        UpdateChrome($"Saved {document.FileName} at {DateTime.Now:t}");
    }

    private void AutoSaveDocument(MarkdownDocumentTabState document) {
        try {
            File.WriteAllText(document.FilePath, document.FrontMatter + document.WorkingText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            document.SavedText = document.WorkingText;
            document.IsDirty   = false;
        }
        catch (Exception ex) {
            SquadDashTrace.Write("MarkdownDocumentWindow", $"AutoSave failed: {ex.Message}");
        }
    }

    private void RenderPreview(MarkdownDocumentTabState document, bool preserveScroll = false) {
        document.PendingScrollFraction = preserveScroll
            ? CaptureWebBrowserScroll(document.WebBrowser)
            : null;

        document.FallbackViewer.Document = MarkdownFlowDocumentBuilder.Build(document.WorkingText);

        try {
            var html = MarkdownHtmlBuilder.Build(document.WorkingText, document.FileName, document.FilePath,
                isDark: AgentStatusCard.IsDarkTheme);
            document.WebBrowser.Visibility = Visibility.Visible;
            document.FallbackViewer.Visibility = Visibility.Collapsed;
            document.WebBrowser.NavigateToString(html);
        }
        catch {
            document.WebBrowser.Visibility = Visibility.Collapsed;
            document.FallbackViewer.Visibility = Visibility.Visible;
        }
    }

    private void SetupWebBrowser(WebBrowser browser) {
        browser.ObjectForScripting = new MarkdownDocumentScriptingBridge(
            HandleLinkNavigation,
            lineHint  => Dispatcher.BeginInvoke(new Action(() => HighlightSourceFromHover(lineHint))),
            imagePath => Dispatcher.BeginInvoke(new Action(() => HandleShowScreenshotMenu(imagePath))),
            imagePath => Dispatcher.BeginInvoke(new Action(() => HandleShowImageMenu(imagePath))));
        browser.Navigating += WebBrowser_Navigating;
        browser.LoadCompleted += WebBrowser_LoadCompleted;
    }

    private void HandleLinkNavigation(string href) {
        Dispatcher.Invoke(() => {
            if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(href) { UseShellExecute = true });
                return;
            }

            if (Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.IsFile) {
                var localPath = uri.LocalPath;
                if (localPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && File.Exists(localPath))
                    NavigateTo(localPath);
            }
        });
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => NavigateBack();

    // ── Screenshot / image context menus ───────────────────────────────────

    private void HandleShowScreenshotMenu(string imagePath) {
        var menu = new ContextMenu();

        var pasteItem = new MenuItem { Header = "Paste screenshot from clipboard" };
        pasteItem.IsEnabled = Clipboard.ContainsImage();
        pasteItem.Click += (_, _) => PasteScreenshotFromClipboard(imagePath);
        menu.Items.Add(pasteItem);

        var captureItem = new MenuItem { Header = "Capture image" };
        captureItem.Click += (_, _) => _ = CaptureImageAsync(imagePath);
        menu.Items.Add(captureItem);

        menu.PlacementTarget = _activeDocument?.WebBrowser;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void HandleShowImageMenu(string imagePath) {
        var menu = new ContextMenu();

        var pasteItem = new MenuItem { Header = "Replace with clipboard image" };
        pasteItem.IsEnabled = Clipboard.ContainsImage();
        pasteItem.Click += (_, _) => ReplaceImageFromClipboard(imagePath);
        menu.Items.Add(pasteItem);

        var captureItem = new MenuItem { Header = "Replace with captured image" };
        captureItem.Click += (_, _) => _ = CaptureImageAsync(imagePath);
        menu.Items.Add(captureItem);

        menu.PlacementTarget = _activeDocument?.WebBrowser;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void PasteScreenshotFromClipboard(string imagePath) {
        var doc = _activeDocument;
        if (doc is null) return;

        if (!Clipboard.ContainsImage()) {
            MessageBox.Show("No image found on clipboard.", "Screenshot",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(imagePath)) {
            MessageBox.Show(
                "Could not determine the image file path for this placeholder.\n\n" +
                "Make sure the markdown has an ![alt](path/to/image.png) line immediately before the 📸 blockquote.",
                "Screenshot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(Path.GetExtension(imagePath))) {
            MessageBox.Show(
                $"The image path \"{imagePath}\" has no file extension. Expected a path like images/screenshot.png.",
                "Screenshot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var docDir        = Path.GetDirectoryName(doc.FilePath)!;
        var fullImagePath = Path.Combine(docDir, imagePath.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(fullImagePath)!);

        var clipImg = Clipboard.GetImage()!;
        var editor  = new ClipboardImageEditorWindow(this, clipImg);
        editor.ShowDialog();
        if (editor.Result is not { } image) return;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.OpenWrite(fullImagePath);
        encoder.Save(stream);

        RemoveScreenshotPlaceholder(doc, imagePath);
        SaveDocument(doc);
    }

    private void ReplaceImageFromClipboard(string imagePath) {
        var doc = _activeDocument;
        if (doc is null) return;

        if (!Clipboard.ContainsImage()) {
            MessageBox.Show("No image found on clipboard.", "Replace Screenshot",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(imagePath) || string.IsNullOrEmpty(Path.GetExtension(imagePath))) {
            MessageBox.Show($"Cannot determine image file path from \"{imagePath}\".",
                "Replace Screenshot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var docDir        = Path.GetDirectoryName(doc.FilePath)!;
        var fullImagePath = Path.Combine(docDir, imagePath.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(fullImagePath)!);

        var clipImg = Clipboard.GetImage()!;
        var editor  = new ClipboardImageEditorWindow(this, clipImg);
        editor.ShowDialog();
        if (editor.Result is not { } image) return;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.OpenWrite(fullImagePath)) {
            stream.SetLength(0);
            encoder.Save(stream);
        }

        // Remove the 📸 placeholder if present immediately after the image line.
        RemoveScreenshotPlaceholderAfterImage(doc, imagePath);
        SaveDocument(doc);
    }

    /// <summary>
    /// Returns the alt text from the first <c>![alt text](imagePath)</c> found in
    /// <paramref name="docText"/>, or an empty string when no match exists.
    /// </summary>
    private static string ExtractImageAltText(string docText, string imagePath) {
        var normalizedPath = imagePath.Replace('\\', '/');
        var escapedPath    = System.Text.RegularExpressions.Regex.Escape(normalizedPath);
        var m = System.Text.RegularExpressions.Regex.Match(
            docText, $@"!\[([^\]]*)\]\({escapedPath}\)");
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    /// <summary>
    /// Removes the 📸 placeholder blockquote that immediately precedes or follows the
    /// image tag for <paramref name="imagePath"/> in the document.
    /// </summary>
    private static void RemoveScreenshotPlaceholder(MarkdownDocumentTabState doc, string imagePath) {
        var text         = doc.EditorTextBox.Text.Replace("\r\n", "\n");
        var lines        = text.Split('\n').ToList();
        var fwdSlashPath = imagePath.Replace('\\', '/');

        // Search from the bottom so removing a line doesn't skew earlier indices.
        for (var i = lines.Count - 1; i >= 0; i--) {
            if ((lines[i].Contains("📸") || lines[i].Contains("Screenshot needed")) &&
                i > 0 && lines[i - 1].Replace('\\', '/').Contains(fwdSlashPath)) {
                lines.RemoveAt(i);
                // Drop any blank line that now occupies the same position.
                if (i < lines.Count && string.IsNullOrWhiteSpace(lines[i]))
                    lines.RemoveAt(i);
                break;
            }
        }

        doc.EditorTextBox.Text = string.Join("\n", lines);
    }

    /// <summary>
    /// Removes the 📸 placeholder blockquote that immediately follows the image tag
    /// for <paramref name="imagePath"/> (used when replacing an existing image).
    /// </summary>
    private static void RemoveScreenshotPlaceholderAfterImage(MarkdownDocumentTabState doc, string imagePath) {
        var text         = doc.EditorTextBox.Text.Replace("\r\n", "\n");
        var lines        = text.Split('\n').ToList();
        var fwdSlashPath = imagePath.Replace('\\', '/');

        for (var i = 0; i < lines.Count - 1; i++) {
            if (!lines[i].Replace('\\', '/').Contains(fwdSlashPath))
                continue;

            var nextI = i + 1;
            if (nextI < lines.Count &&
                (lines[nextI].Contains("📸") || lines[nextI].Contains("Screenshot needed"))) {
                lines.RemoveAt(nextI);
                if (nextI < lines.Count && string.IsNullOrWhiteSpace(lines[nextI]))
                    lines.RemoveAt(nextI);
                break;
            }
        }

        doc.EditorTextBox.Text = string.Join("\n", lines);
    }

    /// <summary>
    /// Opens <see cref="ScreenshotOverlayWindow"/> targeting <paramref name="imagePath"/>
    /// (resolved relative to the active document's directory).  If a matching
    /// <see cref="ScreenshotDefinition"/> exists in <see cref="MarkdownDocumentCaptureContext.ScreenshotsDirectory"/>
    /// and declares a fixture or replay action, those are applied before the overlay opens
    /// and restored in a <c>finally</c> block regardless of capture outcome.
    /// </summary>
    private async Task CaptureImageAsync(string imagePath) {
        var doc = _activeDocument;
        if (doc is null) return;

        if (string.IsNullOrWhiteSpace(imagePath) || string.IsNullOrEmpty(Path.GetExtension(imagePath))) {
            MessageBox.Show(
                $"Cannot determine the image file path from \"{imagePath}\".\n\n" +
                "Make sure the markdown placeholder has a valid image path like images/screenshot.png.",
                "Capture Image", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var docDir        = Path.GetDirectoryName(doc.FilePath)!;
        var fullImagePath = Path.Combine(docDir, imagePath.Replace('/', '\\'));
        var imageDir      = Path.GetDirectoryName(fullImagePath)!;

        // Try to find a matching screenshot definition by filename stem.
        ScreenshotDefinition? definition = null;
        if (_captureContext?.ScreenshotsDirectory is { } screenshotsDir) {
            try {
                var registry = await ScreenshotDefinitionRegistry.LoadAsync(screenshotsDir).ConfigureAwait(true);
                var stem     = Path.GetFileNameWithoutExtension(imagePath);
                definition   = registry.TryGet(stem);
            }
            catch { /* registry unavailable — proceed without fixture */ }
        }

        var fixtureApplied = false;
        IReplayableUiAction? replayAction = null;

        try {
            // Apply fixture if the definition specifies one.
            if (definition is not null && _captureContext?.FixtureRegistry is { } fixtureReg) {
                var fixture = ScreenshotFixture.Empty;

                if (!string.IsNullOrWhiteSpace(definition.FixturePath)) {
                    var fixturePath = Path.IsPathRooted(definition.FixturePath)
                        ? definition.FixturePath
                        : Path.Combine(docDir, definition.FixturePath);

                    if (File.Exists(fixturePath)) {
                        var json    = await File.ReadAllTextAsync(fixturePath).ConfigureAwait(true);
                        var opts    = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        fixture     = JsonSerializer.Deserialize<ScreenshotFixture>(json, opts)
                                      ?? ScreenshotFixture.Empty;
                    }
                }

                if (!string.IsNullOrWhiteSpace(definition.FixturePath)
                    || !string.IsNullOrWhiteSpace(definition.ReplayActionId)) {
                    await fixtureReg.ApplyAllAsync(fixture, CancellationToken.None).ConfigureAwait(true);
                    fixtureApplied = true;
                }
            }

            // Execute replay action if available.
            if (definition is not null
                && !string.IsNullOrWhiteSpace(definition.ReplayActionId)
                && _captureContext?.ActionRegistry is { } actionReg
                && actionReg.TryGet(definition.ReplayActionId, out replayAction)
                && replayAction is not null
                && replayAction.IsSideEffectFree) {
                await replayAction.ExecuteAsync(CancellationToken.None).ConfigureAwait(true);

                // Poll up to 10 s for the action to report ready.
                const int MaxWaitMs      = 10_000;
                const int PollIntervalMs = 100;
                var elapsed = 0;
                while (elapsed < MaxWaitMs && !await replayAction.IsReadyAsync().ConfigureAwait(true)) {
                    await Task.Delay(PollIntervalMs).ConfigureAwait(true);
                    elapsed += PollIntervalMs;
                }
            }

            // Open the capture overlay.
            Directory.CreateDirectory(imageDir);

            var targetWindow = Owner ?? Application.Current.MainWindow;
            var themeName    = _captureContext?.ThemeName    ?? "Light";
            var speechRegion = _captureContext?.SpeechRegion ?? string.Empty;

            var tcs     = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var initialDesc = ExtractImageAltText(doc.WorkingText, imagePath);
            var overlay = new ScreenshotOverlayWindow(targetWindow, imageDir, themeName, speechRegion, initialDesc);
            overlay.ScreenshotSaved  += (_, e) => tcs.TrySetResult(e.PngPath);
            overlay.ScreenshotFailed += (_, _) => tcs.TrySetResult(null);
            overlay.Closed           += (_, _) => tcs.TrySetResult(null); // cancelled
            overlay.Show();

            var savedPath = await tcs.Task.ConfigureAwait(true);
            if (savedPath is null) return; // user cancelled or capture failed

            // Move the provisional PNG to the intended image path.
            File.Move(savedPath, fullImagePath, overwrite: true);

            // Remove the 📸 placeholder and save the document.
            RemoveScreenshotPlaceholder(doc, imagePath);
            SaveDocument(doc);
        }
        finally {
            // Undo any replay action.
            if (replayAction is not null) {
                try { await replayAction.UndoAsync().ConfigureAwait(true); } catch { }
            }

            // Always restore fixture state, even on cancellation or error.
            if (fixtureApplied && _captureContext?.FixtureRegistry is { } f) {
                try { await f.RestoreAllAsync(CancellationToken.None).ConfigureAwait(true); } catch { }
            }
        }
    }

    private void MarkdownDocumentWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton == MouseButton.XButton1) {
            NavigateBack();
            e.Handled = true;
        }
    }

    private void WebBrowser_Navigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e) {
        if (e.Uri == null || e.Uri.Scheme is "about" or "res")
            return;

        e.Cancel = true;
    }

    private void NavigateTo(string filePath) {
        if (_documents.Count != 1)
            return;

        if (_activeDocument != null)
            _navigationHistory.Push(_activeDocument.FilePath);

        var newDoc = MarkdownDocumentTabState.Load(Path.GetFileNameWithoutExtension(filePath), filePath);
        SetupWebBrowser(newDoc.WebBrowser);
        newDoc.EditorTextBox.Tag = newDoc;
        newDoc.EditorTextBox.TextChanged += EditorTextBox_TextChanged;
        _sourceEditorHost.Children.Add(newDoc.EditorTextBox);

        _allTrackedDocuments.Add(newDoc);
        SetupFileWatcher(newDoc);
        ApplyNavDocument(newDoc);
    }

    private void NavigateBack() {
        if (_navigationHistory.Count == 0)
            return;

        var filePath = _navigationHistory.Pop();
        var existing = _documents.FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

        if (existing != null) {
            ApplyNavDocument(existing);
            return;
        }

        var prevDoc = MarkdownDocumentTabState.Load(Path.GetFileNameWithoutExtension(filePath), filePath);
        SetupWebBrowser(prevDoc.WebBrowser);
        prevDoc.EditorTextBox.Tag = prevDoc;
        prevDoc.EditorTextBox.TextChanged += EditorTextBox_TextChanged;
        _sourceEditorHost.Children.Add(prevDoc.EditorTextBox);
        _allTrackedDocuments.Add(prevDoc);
        SetupFileWatcher(prevDoc);
        ApplyNavDocument(prevDoc);
    }

    private void ApplyNavDocument(MarkdownDocumentTabState document) {
        ClearSourceHoverHighlight();
        _activeDocument = document;
        _isSwitchingDocument = true;
        try {
            _singlePreviewHost.Content = document.PreviewHost;
            RenderPreview(document);
            UpdateEditorFromActiveDocument();
            if (_backButton != null)
                _backButton.IsEnabled = _navigationHistory.Count > 0;
            UpdateChrome();
        }
        finally {
            _isSwitchingDocument = false;
        }
    }

    private void WebBrowser_LoadCompleted(object sender, System.Windows.Navigation.NavigationEventArgs e) {
        if (sender is not WebBrowser browser || browser.Tag is not MarkdownDocumentTabState doc)
            return;
        if (doc.PendingScrollFraction is double fraction && fraction >= 0.001) {
            doc.PendingScrollFraction = null;
            RestoreWebBrowserScroll(browser, fraction);
        }
        if (_showSource)
            TryInjectHoverScript(browser);
    }

    private void TryInjectHoverScript(WebBrowser browser) {
        try {
            browser.InvokeScript("eval", new object[] { MarkdownDocumentScripts.HoverInjectionScript });
        }
        catch { }
    }

    private void HighlightSourceFromHover(string lineHint) {
        if (_activeDocument is null || string.IsNullOrEmpty(lineHint)) return;
        if (!int.TryParse(lineHint, out var lineNum) || lineNum < 1) return;
        var textBox = _activeDocument.EditorTextBox;
        if (textBox.Visibility != Visibility.Visible) return;

        var lines = textBox.Text.Split('\n');
        if (lineNum > lines.Length) return;

        int startPos = 0;
        for (int i = 0; i < lineNum - 1; i++)
            startPos += lines[i].Length + 1;
        var lineLength = lines[lineNum - 1].Length;

        HighlightSourceRange(textBox, startPos, lineLength);
    }

    private Canvas EnsureSourceOverlayCanvas() {
        if (_sourceOverlayCanvas is not null) return _sourceOverlayCanvas;
        _sourceOverlayCanvas = new Canvas {
            IsHitTestVisible = false,
            Background = Brushes.Transparent
        };
        _sourceEditorHost.Children.Add(_sourceOverlayCanvas);
        return _sourceOverlayCanvas;
    }

    private void ClearSourceHoverHighlight() {
        _sourceHoverTimer?.Stop();
        if (_sourceHoverHighlight is not null) {
            (_sourceHoverHighlight.Parent as Canvas)?.Children.Remove(_sourceHoverHighlight);
            _sourceHoverHighlight = null;
        }
    }

    private void HighlightSourceRange(TextBox textBox, int start, int length) {
        ClearSourceHoverHighlight();
        if (length <= 0) return;

        var rect = textBox.GetRectFromCharacterIndex(start);
        if (rect == Rect.Empty) return;

        var overlayCanvas = EnsureSourceOverlayCanvas();
        var origin = textBox.TranslatePoint(new Point(0, 0), overlayCanvas);
        var charTopLeft = textBox.TranslatePoint(rect.TopLeft, overlayCanvas);

        var isDark = AgentStatusCard.IsDarkTheme;
        var highlightColor = isDark
            ? Color.FromArgb(60, 255, 220, 80)
            : Color.FromArgb(50, 100, 180, 255);

        double highlightWidth = Math.Max(textBox.ActualWidth - (charTopLeft.X - origin.X), 0);

        _sourceHoverHighlight = new System.Windows.Shapes.Rectangle {
            Width = highlightWidth,
            Height = Math.Max(rect.Height, 14),
            Fill = new SolidColorBrush(highlightColor),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(_sourceHoverHighlight, charTopLeft.X);
        Canvas.SetTop(_sourceHoverHighlight, charTopLeft.Y);
        overlayCanvas.Children.Add(_sourceHoverHighlight);

        _sourceHoverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sourceHoverTimer.Tick += (s, e) => {
            _sourceHoverTimer?.Stop();
            ClearSourceHoverHighlight();
        };
        _sourceHoverTimer.Start();
    }

    private static void RestoreWebBrowserScroll(WebBrowser browser, double fraction) {
        try {
            var f = fraction.ToString("G17", CultureInfo.InvariantCulture);
            browser.InvokeScript("eval", new object[] {
                $"(function(){{var h=Math.max(0,(document.documentElement.scrollHeight||document.body.scrollHeight||0)-(document.documentElement.clientHeight||document.body.clientHeight||0));var t=Math.round(h*{f});document.documentElement.scrollTop=t;document.body.scrollTop=t;}})();"
            });
        }
        catch { }
    }

    private static double CaptureWebBrowserScroll(WebBrowser browser) {
        try {
            if (browser.Document == null)
                return 0.0;
            var scrollTopObj = browser.InvokeScript("eval",
                new object[] { "document.documentElement.scrollTop || document.body.scrollTop || 0" });
            var scrollableHeightObj = browser.InvokeScript("eval",
                new object[] { "Math.max(0,(document.documentElement.scrollHeight||document.body.scrollHeight||0)-(document.documentElement.clientHeight||document.body.clientHeight||0))" });
            var scrollTop = ToDouble(scrollTopObj);
            var scrollableHeight = ToDouble(scrollableHeightObj);
            return scrollableHeight > 0 ? Math.Clamp(scrollTop / scrollableHeight, 0.0, 1.0) : 0.0;
        }
        catch {
            return 0.0;
        }
    }

    private static double ToDouble(object? val) =>
        val switch {
            int i => (double)i,
            double d => d,
            string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) => r,
            _ => 0.0
        };

    private void SetupFileWatcher(MarkdownDocumentTabState doc) {
        var dir = Path.GetDirectoryName(doc.FilePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return;
        try {
            var watcher = new FileSystemWatcher(dir, Path.GetFileName(doc.FilePath)) {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            watcher.Changed += (_, _) => {
                if (doc.IsReloadPending)
                    return;
                doc.IsReloadPending = true;
                Dispatcher.BeginInvoke(new Action(() => ReloadDocumentFromDisk(doc)),
                    System.Windows.Threading.DispatcherPriority.Background);
            };
            doc.FileWatcher = watcher;
        }
        catch { }
    }

    private void ReloadDocumentFromDisk(MarkdownDocumentTabState doc) {
        doc.IsReloadPending = false;
        if (doc.IsDirty || !File.Exists(doc.FilePath))
            return;
        string newText;
        try {
            newText = File.ReadAllText(doc.FilePath);
        }
        catch {
            return;
        }
        var strippedNew = MarkdownDocumentTabState.StripFrontMatter(newText, out var newFrontMatter);
        if (string.Equals(strippedNew, doc.SavedText, StringComparison.Ordinal))
            return;
        doc.FrontMatter = newFrontMatter;
        doc.SavedText = strippedNew;
        doc.WorkingText = strippedNew;
        doc.EditorTextBox.Text = strippedNew;
        RenderPreview(doc, preserveScroll: true);
        UpdateChrome();
        if (doc == _activeDocument)
            FlashReloadBorder();
    }

    private void FlashReloadBorder() {
        _reloadFlashBorder.BorderThickness = new Thickness(2);
        var brush = new SolidColorBrush(Color.FromArgb(200, 255, 140, 0));
        _reloadFlashBorder.BorderBrush = brush;
        var anim = new ColorAnimation {
            From = Color.FromArgb(200, 255, 140, 0),
            To = Color.FromArgb(0, 255, 140, 0),
            Duration = new Duration(TimeSpan.FromSeconds(1.2)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    private void DisposeAllFileWatchers() {
        foreach (var doc in _allTrackedDocuments) {
            if (doc.FileWatcher is { } watcher) {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                doc.FileWatcher = null;
            }
        }
    }

    private void UpdateChrome(string? transientStatus = null) {
        foreach (var document in _documents) {
            if (document.TabItem is not null)
                document.TabItem.Header = document.IsDirty ? $"{document.TabTitle}*" : document.TabTitle;
        }

        var activeFile = _activeDocument?.FilePath ?? string.Empty;
        _statusTextBlock.Text = transientStatus ?? activeFile;
        _saveButton.IsEnabled = _activeDocument?.IsDirty == true;
        Title = _documents.Count == 1 && _documents[0].IsDirty
            ? _baseTitle + " *"
            : _baseTitle;
    }
}

internal sealed class MarkdownDocumentTabState {
    private MarkdownDocumentTabState(string tabTitle, string filePath, string text) {
        TabTitle = tabTitle;
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        var stripped = StripFrontMatter(text, out var frontMatter);
        FrontMatter  = frontMatter;
        SavedText    = stripped;
        WorkingText  = stripped;

        WebBrowser = new WebBrowser();
        WebBrowser.Tag = this;
        FallbackViewer = new FlowDocumentScrollViewer {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        FallbackViewer.SetResourceReference(Control.BackgroundProperty, "TranscriptSurface");
        EditorTextBox = new TextBox {
            Text = stripped,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas, Segoe UI Emoji"),
            FontSize = 14,
            BorderThickness = new Thickness(0),
            Visibility = Visibility.Collapsed
        };
        EditorTextBox.SetResourceReference(TextBox.BackgroundProperty, "InputSurface");
        EditorTextBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");

        PreviewHost = new Grid();
        PreviewHost.Children.Add(WebBrowser);
        PreviewHost.Children.Add(FallbackViewer);
    }

    public string TabTitle { get; }
    public string FilePath { get; }
    public string FileName { get; }
    public string FrontMatter { get; set; } = string.Empty;
    public string SavedText { get; set; }
    public string WorkingText { get; set; }
    public bool IsDirty { get; set; }
    public WebBrowser WebBrowser { get; }
    public FlowDocumentScrollViewer FallbackViewer { get; }
    public TextBox EditorTextBox { get; }
    public Grid PreviewHost { get; }
    public TabItem? TabItem { get; set; }
    internal double? PendingScrollFraction { get; set; }
    internal bool IsReloadPending { get; set; }
    internal FileSystemWatcher? FileWatcher { get; set; }

    public static MarkdownDocumentTabState Load(string tabTitle, string filePath) {
        var text = File.Exists(filePath) ? File.ReadAllText(filePath) : string.Empty;
        return new MarkdownDocumentTabState(tabTitle, filePath, text);
    }

    // Detects and strips a Jekyll/just-the-docs YAML frontmatter block (--- ... ---) from
    // the start of the text. The stripped block is returned via frontMatter; the remainder
    // is the return value. If no frontmatter is found, frontMatter is empty and the original
    // text is returned unchanged.
    private static readonly Regex s_frontMatterRegex = new(
        @"^---[ \t]*\r?\n[\s\S]*?\r?\n---[ \t]*\r?\n?",
        RegexOptions.Compiled);

    public static string StripFrontMatter(string rawText, out string frontMatter) {
        frontMatter = string.Empty;
        if (string.IsNullOrEmpty(rawText)) return rawText;
        var m = s_frontMatterRegex.Match(rawText);
        if (!m.Success) return rawText;
        frontMatter = m.Value;
        return rawText[m.Length..];
    }
}

internal static class MarkdownFlowDocumentBuilder {
    private static readonly Brush DefaultForegroundBrush    = new SolidColorBrush(Color.FromRgb(0x32, 0x2A, 0x23));
    private static readonly Brush DefaultQuoteFillBrush     = new SolidColorBrush(Color.FromRgb(0xF6, 0xF1, 0xE8));
    private static readonly Brush DefaultQuoteBorderBrush   = new SolidColorBrush(Color.FromRgb(0xD5, 0xCA, 0xBA));
    private static readonly Brush DefaultCodeFillBrush      = new SolidColorBrush(Color.FromRgb(0xFA, 0xF6, 0xF0));
    private static readonly Brush DefaultCodeBorderBrush    = new SolidColorBrush(Color.FromRgb(0xE2, 0xD7, 0xC8));
    private static readonly Brush DefaultTableBorderBrush   = new SolidColorBrush(Color.FromArgb(0x38, 0x40, 0x40, 0x40));
    private static readonly Brush DefaultTableHeaderBrush   = new SolidColorBrush(Color.FromArgb(0x18, 0x40, 0x40, 0x40));

    private static Brush Res(string key, Brush fallback) =>
        Application.Current?.Resources[key] as Brush ?? fallback;

    public static FlowDocument Build(string markdown) {
        var foreground   = Res("LabelText",          DefaultForegroundBrush);
        var quoteFill    = Res("QuoteSurface",        DefaultQuoteFillBrush);
        var quoteBorder  = Res("QuoteBorder",         DefaultQuoteBorderBrush);
        var codeFill     = Res("CodeSurface",         DefaultCodeFillBrush);
        var codeBorder   = Res("InputBorder",         DefaultCodeBorderBrush);
        var tableRule    = Res("TableRule",           DefaultTableBorderBrush);
        var tableHeader  = Res("TableHeaderSurface",  DefaultTableHeaderBrush);

        var document = new FlowDocument {
            FontFamily = new FontFamily("Segoe UI, Segoe UI Emoji"),
            FontSize = 14,
            Foreground = foreground,
            PagePadding = new Thickness(18)
        };

        var lines = Normalize(markdown).Split('\n');

        for (var index = 0; index < lines.Length; index++) {
            var line = lines[index];
            var trimmed = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmed)) {
                document.Blocks.Add(new Paragraph());
                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal)) {
                var codeLines = new List<string>();
                index++;
                while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal)) {
                    codeLines.Add(lines[index]);
                    index++;
                }

                document.Blocks.Add(BuildCodeBlock(string.Join(Environment.NewLine, codeLines), codeFill, codeBorder));
                continue;
            }

            if (TryReadTable(lines, ref index, out var tableRows)) {
                document.Blocks.Add(BuildTable(tableRows, tableRule, tableHeader));
                continue;
            }

            if (trimmed.StartsWith("#", StringComparison.Ordinal)) {
                document.Blocks.Add(BuildHeading(trimmed));
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal)) {
                document.Blocks.Add(BuildQuote(trimmed[2..].Trim(), quoteFill, quoteBorder, codeFill));
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal)) {
                var listItems = new List<string> { trimmed[2..].Trim() };
                while (index + 1 < lines.Length) {
                    var next = lines[index + 1].Trim();
                    if (!next.StartsWith("- ", StringComparison.Ordinal) &&
                        !next.StartsWith("* ", StringComparison.Ordinal)) {
                        break;
                    }

                    listItems.Add(next[2..].Trim());
                    index++;
                }

                document.Blocks.Add(BuildList(listItems, codeFill));
                continue;
            }

            if (IsHorizontalRule(trimmed)) {
                document.Blocks.Add(new BlockUIContainer(new Border {
                    Height = 1,
                    Margin = new Thickness(0, 6, 0, 12),
                    Background = tableRule
                }));
                continue;
            }

            document.Blocks.Add(BuildParagraph(trimmed, codeFill));
        }

        return document;
    }

    private static string Normalize(string markdown) {
        return (markdown ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
    }

    private static Paragraph BuildHeading(string line) {
        var level = line.TakeWhile(character => character == '#').Count();
        var text = line[level..].Trim();
        var size = level switch {
            1 => 24d,
            2 => 20d,
            3 => 17d,
            _ => 15d
        };

        var paragraph = new Paragraph {
            Margin = new Thickness(0, level == 1 ? 4 : 10, 0, 6)
        };
        paragraph.Inlines.Add(new Run(text) {
            FontSize = size,
            FontWeight = FontWeights.SemiBold
        });
        return paragraph;
    }

    private static Paragraph BuildParagraph(string text, Brush codeFill) {
        var paragraph = new Paragraph {
            Margin = new Thickness(0, 0, 0, 10)
        };
        AddInlineText(paragraph.Inlines, text, codeFill);
        return paragraph;
    }

    private static BlockUIContainer BuildQuote(string text, Brush quoteFill, Brush quoteBorder, Brush codeFill) {
        var paragraph = new Paragraph {
            Margin = new Thickness(0)
        };
        AddInlineText(paragraph.Inlines, text, codeFill);

        return new BlockUIContainer(new Border {
            Background = quoteFill,
            BorderBrush = quoteBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 2, 0, 10),
            Child = new RichTextBox {
                Document = new FlowDocument(paragraph),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                IsDocumentEnabled = true
            }
        });
    }

    private static List BuildList(IEnumerable<string> items, Brush codeFill) {
        var list = new List {
            Margin = new Thickness(16, 0, 0, 10),
            MarkerStyle = TextMarkerStyle.Disc
        };

        foreach (var item in items) {
            var paragraph = new Paragraph {
                Margin = new Thickness(0, 0, 0, 4)
            };
            AddInlineText(paragraph.Inlines, item, codeFill);
            list.ListItems.Add(new ListItem(paragraph));
        }

        return list;
    }

    private static BlockUIContainer BuildCodeBlock(string code, Brush codeFill, Brush codeBorder) {
        return new BlockUIContainer(new Border {
            Background = codeFill,
            BorderBrush = codeBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 2, 0, 10),
            Child = new TextBox {
                Text = code,
                IsReadOnly = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontFamily = new FontFamily("Consolas"),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }
        });
    }

    private static bool TryReadTable(string[] lines, ref int index, out List<string[]> rows) {
        rows = new List<string[]>();

        if (!IsTableRow(lines[index]))
            return false;

        if (index + 1 >= lines.Length || !IsTableSeparator(lines[index + 1]))
            return false;

        rows.Add(ParseTableRow(lines[index]));
        index++;

        while (index + 1 < lines.Length && IsTableRow(lines[index + 1])) {
            rows.Add(ParseTableRow(lines[index + 1]));
            index++;
        }

        return rows.Count > 0;
    }

    private static Table BuildTable(IReadOnlyList<string[]> rows, Brush tableRule, Brush tableHeader) {
        var table = new Table {
            CellSpacing = 0,
            Margin = new Thickness(0, 2, 0, 12)
        };

        var columnCount = rows.Max(row => row.Length);
        for (var index = 0; index < columnCount; index++)
            table.Columns.Add(new TableColumn());

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++) {
            var row = new TableRow();
            group.Rows.Add(row);

            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++) {
                var text = columnIndex < rows[rowIndex].Length ? rows[rowIndex][columnIndex] : string.Empty;
                var paragraph = new Paragraph {
                    Margin = new Thickness(0)
                };
                var codeFill = Res("CodeSurface", DefaultCodeFillBrush);
                AddInlineText(paragraph.Inlines, text, codeFill);

                row.Cells.Add(new TableCell(paragraph) {
                    BorderBrush = tableRule,
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(8, 5, 8, 5),
                    Background = rowIndex == 0 ? tableHeader : Brushes.Transparent
                });
            }
        }

        return table;
    }

    private static bool IsTableRow(string line) {
        var trimmed = line.Trim();
        return trimmed.StartsWith("|", StringComparison.Ordinal) &&
               trimmed.EndsWith("|", StringComparison.Ordinal) &&
               trimmed.Count(character => character == '|') >= 2;
    }

    private static bool IsTableSeparator(string line) {
        if (!IsTableRow(line))
            return false;

        var cells = ParseTableRow(line);
        return cells.All(cell => cell.Length > 0 && cell.All(character => character is '-' or ':' or ' '));
    }

    private static string[] ParseTableRow(string line) {
        return line
            .Trim()
            .Trim('|')
            .Split('|')
            .Select(cell => cell.Trim())
            .ToArray();
    }

    private static bool IsHorizontalRule(string line) {
        return line.Length >= 3 && line.All(character => character is '-' or '_' or '*');
    }

    // Colored-circle emoji that WPF cannot render from font glyphs — replaced with drawn ellipses.
    private static readonly Dictionary<string, Color> CircleEmojiColors = new() {
        { "🔴", Color.FromRgb(0xE5, 0x39, 0x35) },
        { "🟠", Color.FromRgb(0xF4, 0x51, 0x1E) },
        { "🟡", Color.FromRgb(0xFF, 0xB3, 0x00) },
        { "🟢", Color.FromRgb(0x43, 0xA0, 0x47) },
        { "🔵", Color.FromRgb(0x1E, 0x88, 0xE5) },
        { "🟣", Color.FromRgb(0x8E, 0x24, 0xAA) },
        { "⚫", Color.FromRgb(0x21, 0x21, 0x21) },
        { "⚪", Color.FromRgb(0xDD, 0xDD, 0xDD) },
        { "🟤", Color.FromRgb(0x6D, 0x4C, 0x41) },
    };

    private static void AddInlineText(InlineCollection inlines, string text, Brush codeFill) {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var segments = normalized.Split('`');

        for (var index = 0; index < segments.Length; index++) {
            if (segments[index].Length == 0)
                continue;

            if (index % 2 == 1) {
                // Inside backtick code span — emit as-is in monospace.
                var run = new Run(segments[index]) {
                    FontFamily = new FontFamily("Consolas"),
                    Background = codeFill,
                };
                inlines.Add(run);
                continue;
            }

            // Outside code span — split on colored-circle emoji and draw them as Ellipse.
            AddTextWithCircleEmoji(inlines, segments[index]);
        }
    }

    private static void AddTextWithCircleEmoji(InlineCollection inlines, string text) {
        // Walk through the string, splitting out any known circle emoji.
        var remaining = text;
        while (remaining.Length > 0) {
            // Find the earliest emoji occurrence.
            var earliestIdx = -1;
            var earliestEmoji = string.Empty;
            foreach (var emoji in CircleEmojiColors.Keys) {
                var idx = remaining.IndexOf(emoji, StringComparison.Ordinal);
                if (idx >= 0 && (earliestIdx < 0 || idx < earliestIdx)) {
                    earliestIdx  = idx;
                    earliestEmoji = emoji;
                }
            }

            if (earliestIdx < 0) {
                // No more emoji — emit the rest as a plain Run.
                inlines.Add(new Run(remaining));
                break;
            }

            // Emit text before the emoji.
            if (earliestIdx > 0)
                inlines.Add(new Run(remaining[..earliestIdx]));

            // Emit the emoji as a drawn circle.
            var color  = CircleEmojiColors[earliestEmoji];
            var brush  = new SolidColorBrush(color);
            var ellipse = new System.Windows.Shapes.Ellipse {
                Width   = 11,
                Height  = 11,
                Fill    = brush,
                Margin  = new Thickness(0, 0, 2, -1),
            };
            inlines.Add(new InlineUIContainer(ellipse));

            remaining = remaining[(earliestIdx + earliestEmoji.Length)..];
        }
    }
}

[System.Runtime.InteropServices.ComVisible(true)]
public sealed class MarkdownDocumentScriptingBridge {
    private readonly Action<string>  _navigate;
    private readonly Action<string>? _hoverElement;
    private readonly Action<string>? _showScreenshotMenu;
    private readonly Action<string>? _showImageMenu;

    public MarkdownDocumentScriptingBridge(
        Action<string>  navigate,
        Action<string>? hoverElement       = null,
        Action<string>? showScreenshotMenu = null,
        Action<string>? showImageMenu      = null) {
        _navigate           = navigate;
        _hoverElement       = hoverElement;
        _showScreenshotMenu = showScreenshotMenu;
        _showImageMenu      = showImageMenu;
    }

    public void Navigate(string href)            => _navigate(href);
    public void HoverElement(string lineHint)    => _hoverElement?.Invoke(lineHint);
    public void ShowScreenshotMenu(string path)  => _showScreenshotMenu?.Invoke(path);
    public void ShowImageMenu(string path)       => _showImageMenu?.Invoke(path);
}

internal static class MarkdownDocumentScripts {
    /// <summary>
    /// Injects mouseover listeners on [data-source-line] elements so the host
    /// can call window.external.HoverElement(lineHint) to highlight source lines.
    /// </summary>
    internal static readonly string HoverInjectionScript = @"
(function() {
    if (window.__hoverListenersAttached) return;
    window.__hoverListenersAttached = true;
    var elements = document.querySelectorAll('[data-source-line]');
    for (var i = 0; i < elements.length; i++) {
        (function(el) {
            el.addEventListener('mouseover', function(ev) {
                ev.stopPropagation();
                var lineHint = el.getAttribute('data-source-line');
                if (lineHint) {
                    try { window.external.HoverElement(lineHint); } catch(ex) {}
                }
            });
        })(elements[i]);
    }
})();
";
}

/// <summary>
/// Optional services passed to <see cref="MarkdownDocumentWindow.Show"/> to enable
/// the "Capture image" context-menu action on screenshot placeholders.
/// </summary>
/// <param name="FixtureRegistry">
///   Applies and restores fixture state before and after interactive capture.
///   When <c>null</c>, fixture loading is skipped.
/// </param>
/// <param name="ActionRegistry">
///   Registry of <see cref="IReplayableUiAction"/> instances used to replay UI state
///   before capture.  When <c>null</c>, replay actions are skipped.
/// </param>
/// <param name="ScreenshotsDirectory">
///   Full path to the <c>docs/screenshots</c> directory; used to load
///   <see cref="ScreenshotDefinitionRegistry"/> on demand.
///   When <c>null</c>, definition lookup is skipped.
/// </param>
/// <param name="ThemeName">Current UI theme name passed to <see cref="ScreenshotOverlayWindow"/>.</param>
/// <param name="SpeechRegion">Azure speech region for overlay voice dictation, or empty string.</param>
internal sealed record MarkdownDocumentCaptureContext(
    Screenshots.FixtureLoaderRegistry?  FixtureRegistry,
    Screenshots.UiActionReplayRegistry? ActionRegistry,
    string?                             ScreenshotsDirectory,
    string                              ThemeName,
    string                              SpeechRegion) {

    /// <summary>
    /// Optional callback invoked when the user chooses "Add to Notes" from the source
    /// editor context menu. Receives the selected markdown text.
    /// </summary>
    public Action<string>? AddToNotesCallback { get; init; }

    /// <summary>
    /// Optional callback invoked when the user chooses "Revise with AI" from the source
    /// editor context menu. Parameters: instructions, selectedText, fullDocumentText, workingDirectory.
    /// Returns the revised text.
    /// </summary>
    public Func<string, string, string, string, CancellationToken, Task<string>>? ReviseWithAiCallback { get; init; }
}