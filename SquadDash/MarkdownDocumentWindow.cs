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
    private static readonly TimeSpan EditorUpdateDebounce = TimeSpan.FromMilliseconds(350);
    private const int EditorTextChangedSlowTraceMs = 50;
    private const int EditorFlushSlowTraceMs = 100;

    public static void RefreshAllOpenWindows() {
        foreach (var window in _openWindows)
            window.RefreshTheme();
    }

    /// <summary>
    /// True while any open doc window has at least one active AI revision lock.
    /// Used by MainWindow to defer hot-reload restarts until all revisions complete.
    /// </summary>
    public static bool AnyRevisionInFlight =>
        _openWindows.Any(w => w._documents.Any(d => d.HasLockedRanges));

    /// <summary>
    /// Fired on the UI thread whenever an AI revision completes (lock released).
    /// MainWindow subscribes to trigger a deferred restart if one is pending.
    /// </summary>
    public static event Action? RevisionCompleted;

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
    private MarkdownEditorToolbar? _mdToolbar;
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
    private readonly DispatcherTimer _editorUpdateTimer;
    private readonly HashSet<MarkdownDocumentTabState> _pendingEditorUpdates = [];

    // ── Editor voice / PTT ─────────────────────────────────────────────────
    private ISpeechRecognitionService? _editorVoiceService;
    private PushToTalkWindow?         _editorPttWindow;
    private bool                      _editorVoiceStopOnCtrlRelease;
    private int                       _editorVoiceCaretIndex;
    private int                       _editorVoiceSelectionLength; // consumed on first insert; replaces selection
    private readonly CtrlDoubleTapGestureTracker _editorPttGesture =
        new(maxTapHoldMs: 250, doubleTapGapMs: 350);

    // ── Note title voice / PTT ─────────────────────────────────────────────
    private TextBox?                   _noteTitleBox;
    private ISpeechRecognitionService? _titleVoiceService;
    private PushToTalkWindow?          _titlePttWindow;
    private bool                       _titleVoiceStopOnCtrlRelease;
    private int                        _titleVoiceCaretIndex;
    private int                        _titleVoiceSelectionLength;

    private MarkdownDocumentCaptureContext? _captureContext;

    // ── Editor find-in-source bar state ────────────────────────────────────────
    private Border? _editorFindBar;
    private TextBox? _editorFindTextBox;
    private TextBlock? _editorFindMatchCount;
    private Canvas? _editorFindOverlay;
    private DispatcherTimer? _editorFindDebounceTimer;
    private List<int> _editorFindMatches = [];
    private int _editorFindCurrentIndex = -1;

    private MarkdownDocumentWindow(string title, IReadOnlyList<MarkdownDocumentSpec> documents,
        NoteEditContext? noteContext = null, LoopEditContext? loopEditContext = null) {
        if (documents.Count == 0)
            throw new ArgumentException("At least one markdown document is required.", nameof(documents));

        _baseTitle = title;
        _documents = documents
            .Select(spec => MarkdownDocumentTabState.Load(spec.TabTitle, spec.FilePath))
            .ToList();
        _editorUpdateTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = EditorUpdateDebounce
        };
        _editorUpdateTimer.Tick += (_, _) => FlushPendingEditorUpdates();

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
        _showSourceButton.SetResourceReference(TextElement.FontSizeProperty, "FontSizeNormal");
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

        // ── Note title row (only in Edit Note mode) ──────────────────────────
        if (noteContext is not null) {
            Title = $"Edit Note – {noteContext.InitialTitle}";
            var currentNoteTitle = noteContext.InitialTitle;

            var noteTitleRow = new DockPanel {
                Margin       = new Thickness(12, 0, 12, 8),
                LastChildFill = true,
            };
            DockPanel.SetDock(noteTitleRow, Dock.Top);

            var noteLabel = new TextBlock {
                Text              = "Note:",
                FontSize = (double)Application.Current.Resources["FontSizeBody"],
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 8, 0),
            };
            noteLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
            DockPanel.SetDock(noteLabel, Dock.Left);
            noteTitleRow.Children.Add(noteLabel);

            var noteTitleBox = new TextBox {
                Text            = noteContext.InitialTitle,
                Padding         = new Thickness(6, 4, 6, 4),
                BorderThickness = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            noteTitleBox.SetResourceReference(TextBox.FontSizeProperty,   "FontSizeBody");
            noteTitleBox.SetResourceReference(TextBox.BackgroundProperty, "InputSurface");
            noteTitleBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
            noteTitleBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");
            _noteTitleBox = noteTitleBox;
            noteTitleRow.Children.Add(noteTitleBox);

            void CommitNoteTitle() {
                var newTitle = noteTitleBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(newTitle)) {
                    noteTitleBox.Text = currentNoteTitle;
                    return;
                }
                currentNoteTitle = newTitle;
                noteContext.OnTitleCommit(newTitle);
                Title = $"Edit Note – {newTitle}";
            }

            noteTitleBox.LostFocus += (_, _) => CommitNoteTitle();
            noteTitleBox.KeyDown   += (_, e) => {
                if (e.Key == Key.Enter)  { CommitNoteTitle(); e.Handled = true; }
                if (e.Key == Key.Escape) { noteTitleBox.Text = currentNoteTitle; e.Handled = true; }
                if (e.Key == Key.Tab && _showSource) {
                    CommitNoteTitle();
                    _activeDocument?.EditorTextBox.Focus();
                    e.Handled = true;
                }
            };

            _rootPanel.Children.Add(noteTitleRow);
        }

        // ── Loop description row (only in loop edit mode) ─────────────────────
        if (loopEditContext is not null) {
            var displayName = loopEditContext.InitialDescription;
            var dashIdx     = displayName.IndexOf(" - ", StringComparison.Ordinal);
            var titlePart   = dashIdx > 0 ? displayName[..dashIdx].Trim() : displayName;
            Title = $"Edit Loop – {titlePart}";

            var currentLoopDescription = loopEditContext.InitialDescription;

            var loopDescRow = new DockPanel {
                Margin        = new Thickness(12, 0, 12, 8),
                LastChildFill = true,
            };
            DockPanel.SetDock(loopDescRow, Dock.Top);

            var loopDescLabel = new TextBlock {
                Text              = "Description:",
                FontSize = (double)Application.Current.Resources["FontSizeBody"],
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 8, 0),
            };
            loopDescLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
            DockPanel.SetDock(loopDescLabel, Dock.Left);
            loopDescRow.Children.Add(loopDescLabel);

            var loopDescHint = new TextBlock {
                Text              = "e.g. 'My Loop - tooltip hint shown on hover'",
                FontSize = (double)Application.Current.Resources["FontSizeSmall"],
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(12, 0, 8, 0),
                FontStyle         = FontStyles.Italic,
            };
            loopDescHint.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            DockPanel.SetDock(loopDescHint, Dock.Right);
            loopDescRow.Children.Add(loopDescHint);

            var loopDescBox = new TextBox {
                Text                     = loopEditContext.InitialDescription,
                FontSize = (double)Application.Current.Resources["FontSizeBody"],
                Padding                  = new Thickness(6, 4, 6, 4),
                BorderThickness          = new Thickness(1),
                Height                   = 28,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            loopDescBox.SetResourceReference(TextBox.BackgroundProperty, "InputSurface");
            loopDescBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
            loopDescBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");
            loopDescRow.Children.Add(loopDescBox);

            void CommitLoopDescription() {
                var newDesc = loopDescBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(newDesc) || newDesc == currentLoopDescription)
                    return;
                currentLoopDescription = newDesc;
                loopEditContext.OnDescriptionCommit(newDesc);
                var di     = newDesc.IndexOf(" - ", StringComparison.Ordinal);
                var tp     = di > 0 ? newDesc[..di].Trim() : newDesc;
                Title = $"Edit Loop – {tp}";
            }

            loopDescBox.LostFocus += (_, _) => CommitLoopDescription();
            loopDescBox.KeyDown   += (_, e) => {
                if (e.Key == Key.Enter)  { CommitLoopDescription(); e.Handled = true; }
                if (e.Key == Key.Escape) { loopDescBox.Text = currentLoopDescription; e.Handled = true; }
                // Tab from description → checkbox (default), so no override needed here.
            };

            _rootPanel.Children.Add(loopDescRow);

            // ── Include front matter checkbox ──────────────────────────────────
            var frontMatterCheckBox = new CheckBox {
                Content   = "Include front matter",
                FontSize = (double)Application.Current.Resources["FontSizeSmall"],
                IsChecked = false,
                Margin    = new Thickness(99, 0, 12, 8),
                VerticalAlignment = VerticalAlignment.Center,
            };
            frontMatterCheckBox.SetResourceReference(CheckBox.ForegroundProperty, "LabelText");

            var frontMatterRow = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(frontMatterRow, Dock.Top);
            frontMatterRow.Children.Add(frontMatterCheckBox);

            frontMatterCheckBox.Checked += (_, _) => {
                if (_activeDocument is null) return;
                var wasDirty = _activeDocument.IsDirty;
                var combined = _activeDocument.FrontMatter + _activeDocument.WorkingText;
                _activeDocument.WorkingText = combined;
                _activeDocument.FrontMatter = string.Empty;
                _activeDocument.EditorTextBox.SetPlainText(combined);
                _activeDocument.IsDirty = wasDirty;
                UpdateChrome();
            };

            frontMatterCheckBox.Unchecked += (_, _) => {
                if (_activeDocument is null) return;
                var wasDirty    = _activeDocument.IsDirty;
                var currentText = _activeDocument.EditorTextBox.GetPlainText();
                var stripped    = MarkdownDocumentTabState.StripFrontMatter(currentText, out var fm);
                _activeDocument.FrontMatter = fm;
                _activeDocument.WorkingText = stripped;
                _activeDocument.EditorTextBox.SetPlainText(stripped);
                _activeDocument.IsDirty = wasDirty;
                UpdateChrome();
            };

            // Tab from the checkbox skips the markdown toolbar buttons and lands in the editor.
            frontMatterCheckBox.KeyDown += (_, e) => {
                if (e.Key == Key.Tab && _showSource) {
                    _activeDocument?.EditorTextBox.Focus();
                    e.Handled = true;
                }
            };

            _rootPanel.Children.Add(frontMatterRow);
        }

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
        _tabControl.SetResourceReference(Control.StyleProperty, "ThemedTabControlStyle");
        _tabControl.SelectionChanged += TabControl_SelectionChanged;
        Grid.SetColumn(_tabControl, 0);
        _contentGrid.Children.Add(_tabControl);

        foreach (var document in _documents) {
            var tabItem = new TabItem {
                Content = document.PreviewHost,
                Tag = document
            };
            tabItem.SetResourceReference(Control.StyleProperty, "ThemedTabItemStyle");
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

        _mdToolbar = new MarkdownEditorToolbar {
            ShowImageButton = false,
            ShowHrButton    = true,
        };

        foreach (var document in _documents) {
            document.EditorTextBox.SelectionChanged += EditorTextBox_SelectionChanged;
            document.EditorTextBox.PreviewKeyDown   += EditorTextBox_PreviewKeyDown;
            document.EditorTextBox.PreviewTextInput += EditorTextBox_PreviewTextInput;
            document.EditorTextBox.ContextMenuOpening += EditorTextBox_ContextMenuOpening;
            DataObject.AddPastingHandler(document.EditorTextBox, EditorTextBox_Pasting);
        }

        var sourceColumnPanel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_mdToolbar, System.Windows.Controls.Dock.Top);
        sourceColumnPanel.Children.Add(_mdToolbar);
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
        Closed += (_, _) => {
            _ = StopEditorVoiceAsync();
            _ = StopTitleVoiceAsync();
        };

        PreviewKeyDown += MarkdownDocumentWindow_PreviewKeyDown;
        PreviewKeyUp   += MarkdownDocumentWindow_PreviewKeyUp;
        PreviewMouseDown += MarkdownDocumentWindow_PreviewMouseDown;

        ActivateDocument(_documents[0], preserveCurrentState: false);
        UpdatePreviewHostVisibility();
        UpdateSourcePaneVisibility();
        UpdateChrome();
    }

    private bool _autoSave;
    private bool _suppressEditorNextTextInput;

    // ── Shift+F3 case-cycle state (editor RichTextBox) ───────────────────────────
    private RichTextBox?  _editorCycleRtb;      // which RichTextBox is currently cycling
    private string?       _editorCycleOriginal; // original selected text before first cycle press
    private List<string>? _editorCycleVariants; // [TitleCase, SentenceCase, UPPER, PascalCase]
    private int           _editorCycleIndex;    // index of the variant currently shown
    private int           _editorCycleSelStart; // selection start when cycle began

    public static void Show(Window? owner, string title, string filePath, bool showSource = false,
        MarkdownDocumentCaptureContext? captureContext = null, bool autoSave = false,
        NoteEditContext? noteContext = null, LoopEditContext? loopEditContext = null) {
        // If a window already has this file open, bring it to the front instead of opening a duplicate.
        var existing = _openWindows.FirstOrDefault(w =>
            w._documents.Any(d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase)));
        if (existing is not null) {
            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        Show(owner, title, [new MarkdownDocumentSpec(Path.GetFileNameWithoutExtension(filePath), filePath)], showSource, captureContext, autoSave, noteContext, loopEditContext);
    }

    public static void Show(Window? owner, string title, IReadOnlyList<MarkdownDocumentSpec> documents, bool showSource = false,
        MarkdownDocumentCaptureContext? captureContext = null, bool autoSave = false,
        NoteEditContext? noteContext = null, LoopEditContext? loopEditContext = null) {
        var window = new MarkdownDocumentWindow(title, documents, noteContext, loopEditContext);
        window._captureContext = captureContext;
        window._autoSave       = autoSave;
        if (owner is not null)
            window.Owner = owner;

        // In auto-save mode (notes), manual Save and Back navigation are never useful.
        if (autoSave) {
            window._backButton!.Visibility = Visibility.Collapsed;
            window._saveButton.Visibility  = Visibility.Collapsed;
        }

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
        if (_isSwitchingDocument || sender is not RichTextBox { Tag: MarkdownDocumentTabState document } editorTextBox)
            return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        document.WorkingText = editorTextBox.GetPlainText();
        document.IsDirty = !string.Equals(document.WorkingText, document.SavedText, StringComparison.Ordinal);
        QueueEditorUpdate(document);
        UpdateChrome();
        sw.Stop();
        if (sw.ElapsedMilliseconds >= EditorTextChangedSlowTraceMs)
            SquadDashTrace.Write(
                "MarkdownDocumentWindow",
                $"EditorTextChanged elapsedMs={sw.ElapsedMilliseconds} textLen={document.WorkingText.Length} dirty={document.IsDirty} pending={_pendingEditorUpdates.Count}");
    }

    private void QueueEditorUpdate(MarkdownDocumentTabState document) {
        _pendingEditorUpdates.Add(document);
        _editorUpdateTimer.Stop();
        _editorUpdateTimer.Start();
    }

    private void FlushPendingEditorUpdates() {
        _editorUpdateTimer.Stop();
        if (_pendingEditorUpdates.Count == 0)
            return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var documents = _pendingEditorUpdates.ToArray();
        _pendingEditorUpdates.Clear();
        foreach (var document in documents) {
            RenderPreview(document, preserveScroll: true);
            if (_autoSave && document.IsDirty)
                AutoSaveDocument(document);
        }

        UpdateChrome();
        sw.Stop();
        if (sw.ElapsedMilliseconds >= EditorFlushSlowTraceMs)
            SquadDashTrace.Write(
                "MarkdownDocumentWindow",
                $"EditorFlush elapsedMs={sw.ElapsedMilliseconds} documents={documents.Length} autoSave={_autoSave}");
    }

    private void FlushPendingEditorUpdatesNow() {
        if (_pendingEditorUpdates.Count == 0)
            return;

        FlushPendingEditorUpdates();
    }

    private void EditorTextBox_SelectionChanged(object sender, System.Windows.RoutedEventArgs e) {
        if (sender is not RichTextBox { Tag: MarkdownDocumentTabState doc } || !ReferenceEquals(doc, _activeDocument))
            return;
    }

    private void EditorTextBox_ContextMenuOpening(object sender, ContextMenuEventArgs e) {
        if (sender is not RichTextBox tb || tb.ContextMenu is null) return;

        // Remove any previously-injected items so they don't stack
        for (int i = tb.ContextMenu.Items.Count - 1; i >= 0; i--) {
            if (tb.ContextMenu.Items[i] is MenuItem { Tag: "AddToChat" or "AddToNotes" } ||
                tb.ContextMenu.Items[i] is Separator { Tag: "AddToNotesSep" })
                tb.ContextMenu.Items.RemoveAt(i);
        }

        // Cut/Copy/Paste enabled state
        foreach (var obj in tb.ContextMenu.Items) {
            if (obj is not MenuItem mi) continue;
            if (mi.Command == ApplicationCommands.Cut   || mi.Command == ApplicationCommands.Copy)
                mi.IsEnabled = tb.GetSelectionLength() > 0;
            if (mi.Command == ApplicationCommands.Paste)
                mi.IsEnabled = Clipboard.ContainsText();
            if (mi.Tag is "SmoothDictation")
                mi.IsEnabled = tb.GetSelectionLength() > 0;
        }

        // Inject "Add to chat" and "Add to Notes" at the top when there is a selection
        if (tb.GetSelectionLength() > 0) {
            int insertIdx = 0;

            if (_captureContext?.AddToChatCallback is { } chatCallback) {
                var chatItem = new MenuItem { Header = "Add to chat", Tag = "AddToChat" };
                chatItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                chatItem.Click += (_, _) => {
                    var text = tb.GetSelectedText();
                    if (!string.IsNullOrWhiteSpace(text))
                        chatCallback(text);
                };
                tb.ContextMenu.Items.Insert(insertIdx++, chatItem);
            }

            if (_captureContext?.AddToNotesCallback is { } callback) {
                var noteItem = new MenuItem { Header = "Add to Notes", Tag = "AddToNotes" };
                noteItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                noteItem.Click += (_, _) => {
                    var text = tb.GetSelectedText();
                    if (!string.IsNullOrWhiteSpace(text))
                        callback(text);
                };
                tb.ContextMenu.Items.Insert(insertIdx++, noteItem);
            }

            if (insertIdx > 0) {
                var topSep = new Separator { Tag = "AddToNotesSep" };
                topSep.SetResourceReference(Separator.StyleProperty, "ThemedMenuSeparatorStyle");
                tb.ContextMenu.Items.Insert(insertIdx, topSep);
            }
        }

        // Remove any previously-injected "Revise with AI" items
        for (int i = tb.ContextMenu.Items.Count - 1; i >= 0; i--) {
            if (tb.ContextMenu.Items[i] is MenuItem { Tag: "ReviseWithAi" } ||
                tb.ContextMenu.Items[i] is Separator { Tag: "ReviseWithAiSep" })
                tb.ContextMenu.Items.RemoveAt(i);
        }

        // Add "Revise with AI" if callback is set and there's a selection
        if (_captureContext?.ReviseWithAiCallback is { } reviseCallback && tb.GetSelectionLength() > 0) {
            var sep2 = new Separator { Tag = "ReviseWithAiSep" };
            sep2.SetResourceReference(Separator.StyleProperty, "ThemedMenuSeparatorStyle");

            var reviseItem = new MenuItem { Header = "✏ _Revise with AI", Tag = "ReviseWithAi", InputGestureText = "Ctrl+Shift+A" };
            reviseItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
            reviseItem.Click += (_, _) => TriggerReviseWithAi(tb, reviseCallback);

            tb.ContextMenu.Items.Add(sep2);
            tb.ContextMenu.Items.Add(reviseItem);
        }
    }

    private void TriggerReviseWithAi(
        RichTextBox tb,
        Func<string, string, string, string, CancellationToken, Task<string>> reviseCallback)
    {
        if (tb.GetSelectionLength() == 0) return;
        var doc          = tb.Tag as MarkdownDocumentTabState;
        var selectedText = tb.GetSelectedText();
        var fullText     = tb.GetPlainText();
        var selStart     = tb.GetSelectionStart();
        var selLen       = tb.GetSelectionLength();
        var docPath      = doc?.FilePath ?? "";

        // Capture live TextPointer anchors via plain-text offsets — they track document edits automatically
        var startPointer = tb.GetTextPointerAt(selStart);
        var endPointer   = tb.GetTextPointerAt(selStart + selLen);

        var priorFocus = Keyboard.FocusedElement as IInputElement;

        // Capture adorner, indicator, and revision lock for lifecycle management
        RevisionHighlightAdorner? adorner = null;
        RevisionPendingIndicator? indicator = null;
        EditorRevisionLock? revLock = null;

        var popup = new DocRevisePopup(
            selectedText,
            fullText,
            docPath,
            reviseCallback,
            onRevised: revised => Dispatcher.Invoke(() => {
                adorner?.Remove();
                indicator?.Detach();
                if (revLock is not null) doc?.RemoveRevisionLock(revLock);
                revLock = null;

                // Use live TextPointers to get current text after any edits; normalize to \n
                var currentSelectedText = new TextRange(startPointer, endPointer).Text.Replace("\r\n", "\n");
                
                // Check if the original selection is still intact
                if (currentSelectedText == selectedText) {
                    var replaceRange = new TextRange(startPointer, endPointer);
                    replaceRange.Text = revised;
                } else {
                    var win = new RevisionResultWindow(revised) { Owner = this };
                    win.Show();
                }
            }),
            onSubmitting: popupCenter => {
                priorFocus?.Focus();
                Keyboard.Focus(priorFocus);
                RevisionWorkingOverlay.ShowAt(popupCenter, this);
                // Lock the revision range so editing keys are swallowed in that area
                revLock = doc?.AddRevisionLock(startPointer, endPointer);
                adorner = RevisionHighlightAdorner.Attach(tb, startPointer, endPointer);
                indicator = RevisionPendingIndicator.Attach(tb, endPointer);
            },
            onRevisionComplete: () => Dispatcher.Invoke(() => {
                // Fallback unlock — fires on failure/cancel as well as success
                adorner?.Remove();
                indicator?.Detach();
                if (revLock is not null) doc?.RemoveRevisionLock(revLock);
                revLock = null;
                RevisionCompleted?.Invoke();
            }),
            startPtt: _captureContext?.StartPttCallback,
            stopPtt:  _captureContext?.StopPttCallback);

        PositionPopupNearCaret(popup, tb, selStart);
        popup.Owner = this;
        popup.Show();
    }

    private void TriggerDirectReviseWithAi(
        RichTextBox tb,
        Func<string, string, string, string, CancellationToken, Task<string>> reviseCallback,
        string instructions)
    {
        if (string.IsNullOrWhiteSpace(instructions)) return;
        var doc = tb.Tag as MarkdownDocumentTabState;
        MarkdownRevisionExecutor.DirectRevise(
            tb, doc?.FilePath ?? "", instructions, reviseCallback, this,
            doc, onCompleted: () => RevisionCompleted?.Invoke());
    }

    private void PositionPopupNearCaret(Window popup, RichTextBox richTextBox, int charIndex)
    {
        try
        {
            var rect        = richTextBox.GetRectFromOffset(Math.Max(0, charIndex));
            var screenBottom = richTextBox.PointToScreen(new Point(rect.Left, rect.Bottom));
            var screenTop    = richTextBox.PointToScreen(new Point(rect.Left, rect.Top));
            var dpi         = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            var workArea    = NativeMethods.GetWorkAreaForPhysicalPoint((int)screenBottom.X, (int)screenBottom.Y);

            var waLeft   = workArea.Left   / dpi.DpiScaleX;
            var waTop    = workArea.Top    / dpi.DpiScaleY;
            var waRight  = workArea.Right  / dpi.DpiScaleX;
            var waBottom = workArea.Bottom / dpi.DpiScaleY;

            var logBottom = new Point(screenBottom.X / dpi.DpiScaleX, screenBottom.Y / dpi.DpiScaleY);
            var logTop    = new Point(screenTop.X    / dpi.DpiScaleX, screenTop.Y    / dpi.DpiScaleY);

            const double PopupWidth  = 470;
            const double PopupHeight = 235;

            double left = logBottom.X - 10;
            double top  = logBottom.Y + 6;

            if (left + PopupWidth > waRight) left = waRight - PopupWidth - 10;
            if (left < waLeft)               left = waLeft  + 10;
            if (top + PopupHeight > waBottom) top = logTop.Y - PopupHeight - 6;
            if (top < waTop) top = waTop + 10;

            popup.Left = left;
            popup.Top  = top;
        }
        catch
        {
            var mp  = PointToScreen(Mouse.GetPosition(this));
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            popup.Left = mp.X / dpi.DpiScaleX - 10;
            popup.Top  = mp.Y / dpi.DpiScaleY - 10;
        }
    }

    private static ContextMenu BuildSourceEditorContextMenu(RichTextBox tb) {
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

        var sep = new Separator { Tag = "SmoothDictationSep" };
        sep.SetResourceReference(Separator.StyleProperty, "ThemedMenuSeparatorStyle");

        var smoothItem = new MenuItem {
            Header           = "✨ Smooth Dictation",
            InputGestureText = "Shift+Space",
            Tag              = "SmoothDictation",
        };
        smoothItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
        smoothItem.Click += (_, _) => SmoothDictationHelper.ApplyToRichTextBox(tb);

        menu.Items.Add(cutItem);
        menu.Items.Add(copyItem);
        menu.Items.Add(pasteItem);
        menu.Items.Add(sep);
        menu.Items.Add(smoothItem);

        return menu;
    }

    private void EditorTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
        if (sender is not RichTextBox tb) return;

        // Block mutating keys while the caret/selection overlaps an active AI revision range.
        // Exception: if the caret sits exactly at a boundary, redirect it just outside the lock
        // and let the key event proceed so text is inserted adjacent to (not inside) the lock.
        if (IsKeyMutating(e.Key)
            && tb.Tag is MarkdownDocumentTabState lockedDoc
            && lockedDoc.HasLockedRanges
            && IsCaretInLockedRange(tb, lockedDoc)) {
            if (!TryRedirectCaretOutsideLock(tb, lockedDoc)) {
                e.Handled = true;
                return;
            }
            // Caret redirected to boundary-adjacent position; let the key event proceed normally.
        }

        // ── Smooth Dictation: Shift+Space on selection ────────────────────────
        if (e.Key == System.Windows.Input.Key.Space
            && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0
            && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0
            && tb.GetSelectionLength() > 0) {
            e.Handled = SmoothDictationHelper.ApplyToRichTextBox(tb);
            return;
        }

        // ── Shift+F3: cycle case of selected text ─────────────────────────────
        if (e.Key == System.Windows.Input.Key.F3
            && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Shift
            && tb.GetSelectionLength() > 0) {
            var selStart     = tb.GetSelectionStart();
            var selectedText = tb.GetSelectedText();

            bool continuing = _editorCycleVariants is not null
                && ReferenceEquals(tb, _editorCycleRtb)
                && selStart == _editorCycleSelStart
                && selectedText == _editorCycleVariants[_editorCycleIndex];

            if (!continuing) {
                _editorCycleRtb      = tb;
                _editorCycleOriginal = selectedText;
                _editorCycleVariants = TextCaseHelper.ComputeVariants(selectedText);
                _editorCycleIndex    = TextCaseHelper.GetFirstVariantIndex(selectedText);
                _editorCycleSelStart = selStart;

                var firstVariant = _editorCycleVariants[_editorCycleIndex];
                tb.ReplaceSelection(firstVariant);
                tb.SelectRange(selStart, firstVariant.Length);
            }
            else {
                _editorCycleIndex = (_editorCycleIndex + 1) % _editorCycleVariants!.Count;
                var nextVariant = _editorCycleVariants[_editorCycleIndex];

                // Restore original (no undo), then apply next variant as a single undo entry.
                tb.IsUndoEnabled = false;
                tb.SelectRange(_editorCycleSelStart, selectedText.Length);
                tb.Selection.Text = _editorCycleOriginal!;
                tb.IsUndoEnabled  = true;
                tb.SelectRange(_editorCycleSelStart, _editorCycleOriginal!.Length);
                tb.ReplaceSelection(nextVariant);
                tb.SelectRange(_editorCycleSelStart, nextVariant.Length);
            }
            e.Handled = true;
            return;
        }

        // ── Selection embedding: backtick ─────────────────────────────────────────
        if (e.Key == System.Windows.Input.Key.OemTilde
            && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None
            && tb.GetSelectionLength() > 0) {
            if (MarkdownEditorCommands.ApplyInlineCodeOrFence(tb)) {
                _suppressEditorNextTextInput = true;
                e.Handled = true;
                return;
            }
        }

        // ── Selection embedding: double-quote ─────────────────────────────────────
        if (e.Key == System.Windows.Input.Key.OemQuotes
            && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Shift
            && tb.GetSelectionLength() > 0) {
            if (MarkdownEditorCommands.ApplyInlineQuote(tb)) {
                _suppressEditorNextTextInput = true;
                e.Handled = true;
                return;
            }
        }

        // ── List continuation: Enter at end of a bullet/numbered line ─────────
        if (e.Key == System.Windows.Input.Key.Return
            && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None) {
            if (MarkdownEditorCommands.ContinueListOnEnter(tb))
                e.Handled = true;
            return;
        }

        // ── Shift+Enter: duplicate current line ───────────────────────────────────
        if (e.Key == System.Windows.Input.Key.Return
            && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Shift) {
            MarkdownEditorCommands.DuplicateLine(tb);
            e.Handled = true;
            return;
        }

        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0) return;
        if (e.Key == System.Windows.Input.Key.Z
            && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0) {
            if (tb.CanRedo) tb.Redo();
            e.Handled = true;
        } else if (e.Key == System.Windows.Input.Key.B) {
            MarkdownEditorCommands.ApplyBold(tb);
            e.Handled = true;
        } else if (e.Key == System.Windows.Input.Key.I) {
            MarkdownEditorCommands.ApplyItalic(tb);
            e.Handled = true;
        } else if (e.Key == System.Windows.Input.Key.S && _activeDocument is not null) {
            SaveDocument(_activeDocument);
            e.Handled = true;
        } else if (e.Key == System.Windows.Input.Key.A
            && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0
            && _captureContext?.ReviseWithAiCallback is { } revCb
            && tb.GetSelectionLength() > 0) {
            TriggerReviseWithAi(tb, revCb);
            e.Handled = true;
        } else if (e.Key == System.Windows.Input.Key.C
            && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0
            && _captureContext?.ReviseWithAiCallback is { } cleanupCb
            && _captureContext?.CleanupPrompt is { Length: > 0 } cleanupPrompt
            && tb.GetSelectionLength() > 0) {
            TriggerDirectReviseWithAi(tb, cleanupCb, cleanupPrompt);
            e.Handled = true;
        }
    }

    private void EditorTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e) {
        if (_suppressEditorNextTextInput) {
            _suppressEditorNextTextInput = false;
            e.Handled = true;
            return;
        }
        if (sender is not RichTextBox tb) return;

        // Paren embedding (works on all keyboard layouts)
        if ((e.Text == "(" || e.Text == ")") && tb.GetSelectionLength() > 0
            && !tb.GetSelectedText().Contains('\n')) {
            if (MarkdownEditorCommands.ApplyInlineParens(tb, e.Text)) {
                e.Handled = true;
                return;
            }
        }

        if (tb.Tag is MarkdownDocumentTabState doc
            && doc.HasLockedRanges
            && IsCaretInLockedRange(tb, doc)
            && !TryRedirectCaretOutsideLock(tb, doc))
            e.Handled = true;
    }

    /// <summary>
    /// Blocks paste when the caret/selection overlaps a locked range. If the caret sits exactly
    /// at a lock boundary, redirects it just outside the lock so the paste lands adjacent to it.
    /// </summary>
    private void EditorTextBox_Pasting(object sender, DataObjectPastingEventArgs e) {
        if (sender is not RichTextBox tb) return;
        if (tb.Tag is not MarkdownDocumentTabState doc || !doc.HasLockedRanges) return;
        if (!IsCaretInLockedRange(tb, doc)) return;
        if (!TryRedirectCaretOutsideLock(tb, doc))
            e.CancelCommand();
    }

    /// <summary>Returns true for keys that directly mutate document content.</summary>
    private static bool IsKeyMutating(Key key) =>
        key == Key.Back || key == Key.Delete || key == Key.Return || key == Key.Space;

    /// <summary>
    /// Returns true when the current caret position or selection overlaps any active revision lock.
    /// The check uses an inclusive boundary so Delete/Backspace at the very edge of a locked range
    /// are also blocked.
    /// </summary>
    private static bool IsCaretInLockedRange(RichTextBox tb, MarkdownDocumentTabState state) {
        var selStart = tb.Selection.Start;
        var selEnd   = tb.Selection.End;
        foreach (var range in state.LockedRanges) {
            // Inclusive boundary: block if selection touches or overlaps [range.Start, range.End]
            if (selStart.CompareTo(range.End) <= 0 && selEnd.CompareTo(range.Start) >= 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// If the caret (collapsed selection) sits exactly at the start or end boundary of a locked
    /// range, moves the caret to the nearest insertion position just outside that boundary and
    /// returns <see langword="true"/> so the caller can allow the pending edit to proceed.
    /// Returns <see langword="false"/> when no redirect is needed or possible (e.g. a non-empty
    /// selection, or the document start prevents moving before the lock).
    /// </summary>
    private static bool TryRedirectCaretOutsideLock(RichTextBox tb, MarkdownDocumentTabState state) {
        if (!tb.Selection.IsEmpty) return false;
        var caret = tb.CaretPosition;
        foreach (var range in state.LockedRanges) {
            if (caret.CompareTo(range.End) == 0) {
                // Caret is at the end boundary — step just past the lock.
                var after = range.End.GetNextInsertionPosition(LogicalDirection.Forward);
                tb.CaretPosition = after ?? range.End;
                return true;
            }
            if (caret.CompareTo(range.Start) == 0) {
                // Caret is at the start boundary — step just before the lock.
                var before = range.Start.GetNextInsertionPosition(LogicalDirection.Backward);
                if (before is null) return false; // at absolute document start; nowhere to redirect
                tb.CaretPosition = before;
                return true;
            }
        }
        return false;
    }

    // ── Editor PTT (double-tap Ctrl voice) ────────────────────────────────

    private void MarkdownDocumentWindow_PreviewKeyDown(object sender, KeyEventArgs e) {
        // ── Ctrl+F: show/focus find bar (source only) ─────────────────────────
        if (_showSource && e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0) {
            ShowEditorFindBar();
            e.Handled = true;
            return;
        }

        // ── Escape: hide find bar (source only) ───────────────────────────────
        if (_showSource && e.Key == Key.Escape && _editorFindBar is not null) {
            HideEditorFindBar();
            e.Handled = true;
            return;
        }

        var editorTb      = _activeDocument?.EditorTextBox;
        bool focusInEditor = _showSource && editorTb is not null && editorTb.IsKeyboardFocusWithin;
        bool focusInTitle  = _noteTitleBox is not null && _noteTitleBox.IsKeyboardFocusWithin;
        if (!focusInEditor && !focusInTitle) return;

        var action = _editorPttGesture.HandleKeyDown(e.Key, e.IsRepeat, DateTime.UtcNow);
        if (action != CtrlDoubleTapGestureAction.Triggered) return;

        if (focusInTitle) {
            if (_titleVoiceService is null) {
                _titleVoiceStopOnCtrlRelease = true;
                _ = StartTitleVoiceAsync();
            } else {
                _ = StopTitleVoiceAsync();
            }
        } else {
            if (_editorVoiceService is null) {
                _editorVoiceStopOnCtrlRelease = true;
                _ = StartEditorVoiceAsync();
            } else {
                _ = StopEditorVoiceAsync();
            }
        }
        e.Handled = true;
    }

    private void MarkdownDocumentWindow_PreviewKeyUp(object sender, KeyEventArgs e) {
        if (!CtrlDoubleTapGestureTracker.IsCtrlKey(e.Key)) return;

        if (_titleVoiceStopOnCtrlRelease && (_titleVoiceService is not null || _titlePttWindow is not null)) {
            _ = StopTitleVoiceAsync();
            e.Handled = true;
            return;
        }

        if (_editorVoiceStopOnCtrlRelease && (_editorVoiceService is not null || _editorPttWindow is not null)) {
            _ = StopEditorVoiceAsync();
            e.Handled = true;
            return;
        }

        _editorPttGesture.HandleKeyUp(e.Key, DateTime.UtcNow);
    }

    private async Task StartEditorVoiceAsync() {
        var settings = new ApplicationSettingsStore().Load();
        string key, region;
        if (settings.SpeechProvider == SpeechProvider.OpenAI) {
            key    = settings.OpenAiSpeechApiKey ?? string.Empty;
            region = string.Empty;
        }
        else {
            key    = Environment.GetEnvironmentVariable("SQUAD_SPEECH_KEY", EnvironmentVariableTarget.User) ?? string.Empty;
            region = settings.SpeechRegion ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(key) ||
            (settings.SpeechProvider == SpeechProvider.Azure && string.IsNullOrWhiteSpace(region))) {
            _editorVoiceStopOnCtrlRelease = false;
            _editorPttGesture.Reset();
            return;
        }

        var editorTb = _activeDocument?.EditorTextBox;
        if (editorTb is null) return;

        _editorVoiceCaretIndex    = editorTb.GetSelectionStart();
        _editorVoiceSelectionLength = editorTb.GetSelectionLength();

        _editorVoiceService = settings.SpeechProvider == SpeechProvider.OpenAI
            ? new WhisperSpeechRecognitionService()
            : new AzureSpeechRecognitionService();

        _editorVoiceService.PhraseRecognized += (_, text) =>
            Dispatcher.BeginInvoke(() => AppendSpeechToEditor(text));

        _editorVoiceService.VolumeChanged += (_, level) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Render, () => {
                if (_editorPttWindow is not null)
                    _editorPttWindow.VolumeBar.Height = Math.Max(2, level * 36);
            });

        _editorVoiceService.RecognitionError += (_, _) =>
            Dispatcher.BeginInvoke(() => _ = StopEditorVoiceAsync());

        try {
            // Get caret position in physical screen pixels (PointToScreen returns physical coords).
            System.Windows.Point physicalPt;
            try {
                var caretRect = editorTb.GetRectFromOffset(_editorVoiceCaretIndex);
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

            await _editorVoiceService.StartAsync(key, region, language: settings.SpeechLanguage).ConfigureAwait(false);
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

    private async Task StartTitleVoiceAsync() {
        var settings = new ApplicationSettingsStore().Load();
        string key, region;
        if (settings.SpeechProvider == SpeechProvider.OpenAI) {
            key    = settings.OpenAiSpeechApiKey ?? string.Empty;
            region = string.Empty;
        } else {
            key    = Environment.GetEnvironmentVariable("SQUAD_SPEECH_KEY", EnvironmentVariableTarget.User) ?? string.Empty;
            region = settings.SpeechRegion ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(key) ||
            (settings.SpeechProvider == SpeechProvider.Azure && string.IsNullOrWhiteSpace(region))) {
            _titleVoiceStopOnCtrlRelease = false;
            _editorPttGesture.Reset();
            return;
        }

        var titleBox = _noteTitleBox;
        if (titleBox is null) return;

        _titleVoiceCaretIndex      = titleBox.SelectionStart;
        _titleVoiceSelectionLength = titleBox.SelectionLength;

        _titleVoiceService = settings.SpeechProvider == SpeechProvider.OpenAI
            ? new WhisperSpeechRecognitionService()
            : new AzureSpeechRecognitionService();

        _titleVoiceService.PhraseRecognized += (_, text) =>
            Dispatcher.BeginInvoke(() => AppendSpeechToTitle(text));

        _titleVoiceService.VolumeChanged += (_, level) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Render, () => {
                if (_titlePttWindow is not null)
                    _titlePttWindow.VolumeBar.Height = Math.Max(2, level * 36);
            });

        _titleVoiceService.RecognitionError += (_, _) =>
            Dispatcher.BeginInvoke(() => _ = StopTitleVoiceAsync());

        try {
            System.Windows.Point physicalPt;
            try {
                var caretRect = titleBox.GetRectFromCharacterIndex(titleBox.SelectionStart);
                physicalPt = titleBox.PointToScreen(new System.Windows.Point(caretRect.Left, caretRect.Bottom));
            } catch {
                physicalPt = titleBox.PointToScreen(new System.Windows.Point(0, titleBox.ActualHeight + 4));
            }

            var physWa          = NativeMethods.GetWorkAreaForPhysicalPoint((int)physicalPt.X, (int)physicalPt.Y);
            var logicalPt       = DpiHelper.PhysicalToLogical(titleBox, physicalPt);
            var logicalWaOrigin = DpiHelper.PhysicalToLogical(titleBox, new System.Windows.Point(physWa.Left, physWa.Top));
            var logicalWaCorner = DpiHelper.PhysicalToLogical(titleBox, new System.Windows.Point(physWa.Right, physWa.Bottom));
            var logicalWorkArea = new System.Windows.Rect(logicalWaOrigin, logicalWaCorner);

            _titlePttWindow = new PushToTalkWindow(this, showHint: false);
            _titlePttWindow.PositionUnderCaret(logicalPt, logicalWorkArea);
            _titlePttWindow.Show();
            titleBox.Focus();

            await _titleVoiceService.StartAsync(key, region, language: settings.SpeechLanguage).ConfigureAwait(false);
        } catch {
            await Dispatcher.InvokeAsync(() => {
                _titlePttWindow?.Close();
                _titlePttWindow = null;
            });
            _titleVoiceService?.Dispose();
            _titleVoiceService = null;
            _titleVoiceStopOnCtrlRelease = false;
            _editorPttGesture.Reset();
        }
    }

    private async Task StopTitleVoiceAsync() {
        await Dispatcher.InvokeAsync(() => {
            _titlePttWindow?.Close();
            _titlePttWindow = null;
        });

        var service = _titleVoiceService;
        _titleVoiceService = null;

        if (service is not null) {
            try { await service.StopAsync().ConfigureAwait(false); } catch { }
            service.Dispose();
        }

        _titleVoiceStopOnCtrlRelease = false;
        _editorPttGesture.Reset();
    }

    private void AppendSpeechToTitle(string text) {
        var titleBox = _noteTitleBox;
        if (titleBox is null) return;

        var current     = titleBox.Text;
        var caretIndex  = Math.Min(_titleVoiceCaretIndex, current.Length);
        var selLength   = _titleVoiceSelectionLength;
        _titleVoiceSelectionLength = 0;
        var selEndIndex = Math.Min(caretIndex + selLength, current.Length);
        var left        = current[..caretIndex];
        var right       = current[selEndIndex..];
        var prefix      = VoiceInsertionHeuristics.LeadingInsertionSpace(left, right);
        var processed   = VoiceInsertionHeuristics.Apply(left, text, right);
        var insert      = prefix + processed;
        var rules       = new ApplicationSettingsStore().Load().VoiceReplacementRules;
        var replaced    = rules.Count > 0
            ? prefix + VoiceInsertionHeuristics.ApplyReplacementRules(processed, rules)
            : insert;

        titleBox.SelectionStart  = caretIndex;
        titleBox.SelectionLength = selEndIndex - caretIndex;
        titleBox.SelectedText    = replaced;
        titleBox.SelectionStart  = caretIndex + replaced.Length;
        titleBox.SelectionLength = 0;
        _titleVoiceCaretIndex    = caretIndex + replaced.Length;
    }

    private void AppendSpeechToEditor(string text) {
        var editorTb = _activeDocument?.EditorTextBox;
        if (editorTb is null) return;

        var current    = editorTb.GetPlainText();
        var caretIndex = Math.Min(_editorVoiceCaretIndex, current.Length);
        // Replace selection on first insert; subsequent phrases append at caret.
        var selLength  = _editorVoiceSelectionLength;
        _editorVoiceSelectionLength = 0;
        var selEndIndex = Math.Min(caretIndex + selLength, current.Length);
        var left       = current[..caretIndex];
        var right      = current[selEndIndex..];
        var prefix     = VoiceInsertionHeuristics.LeadingInsertionSpace(left, right);
        var processed  = VoiceInsertionHeuristics.Apply(left, text, right);
        var insert     = prefix + processed;
        var rules      = new ApplicationSettingsStore().Load().VoiceReplacementRules;
        var replaced   = rules.Count > 0
            ? prefix + VoiceInsertionHeuristics.ApplyReplacementRules(processed, rules)
            : insert;

        // Step 1: insert conditioned (pre-replacement) text
        editorTb.SelectRange(caretIndex, selEndIndex - caretIndex);
        editorTb.ReplaceSelection(insert);
        // Collapse selection to caret at end of inserted text — SelectRange(x,0) both moves
        // the caret and collapses the selection; SetCaretOffset alone does not clear selection.
        editorTb.SelectRange(caretIndex + insert.Length, 0);
        _editorVoiceCaretIndex = caretIndex + insert.Length;

        // Step 2 (if rules changed the text): replace with post-replacement text — second undo entry
        if (!string.Equals(replaced, insert, StringComparison.Ordinal))
        {
            editorTb.SelectRange(caretIndex, insert.Length);
            editorTb.ReplaceSelection(replaced);
            editorTb.SelectRange(caretIndex + replaced.Length, 0);
            _editorVoiceCaretIndex = caretIndex + replaced.Length;
        }
    }


    private void MarkdownDocumentWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e) {
        if (_isClosingAfterPrompt)
            return;

        FlushPendingEditorUpdatesNow();
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
        if (_mdToolbar is not null) _mdToolbar.TargetRichTextBox = _activeDocument?.EditorTextBox;

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
            if (child is RichTextBox { Tag: MarkdownDocumentTabState doc } tb)
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
        document.WorkingText = document.EditorTextBox.GetPlainText();
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
            imagePath => Dispatcher.BeginInvoke(new Action(() => HandleShowImageMenu(imagePath))),
            lineHint  => Dispatcher.BeginInvoke(new Action(() => ScrollToSourceLine(lineHint))));
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
        var text         = doc.EditorTextBox.GetPlainText().Replace("\r\n", "\n");
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

        doc.EditorTextBox.SetPlainText(string.Join("\n", lines));
    }

    /// <summary>
    /// Removes the 📸 placeholder blockquote that immediately follows the image tag
    /// for <paramref name="imagePath"/> (used when replacing an existing image).
    /// </summary>
    private static void RemoveScreenshotPlaceholderAfterImage(MarkdownDocumentTabState doc, string imagePath) {
        var text         = doc.EditorTextBox.GetPlainText().Replace("\r\n", "\n");
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

        doc.EditorTextBox.SetPlainText(string.Join("\n", lines));
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

        var lines = textBox.GetPlainText().Split('\n');
        if (lineNum > lines.Length) return;

        int startPos = 0;
        for (int i = 0; i < lineNum - 1; i++)
            startPos += lines[i].Length + 1;
        var lineLength = lines[lineNum - 1].Length;

        HighlightSourceRange(textBox, startPos, lineLength);
    }

    private void ScrollToSourceLine(string lineHint) {
        if (_activeDocument is null || string.IsNullOrEmpty(lineHint)) return;
        if (!int.TryParse(lineHint, out var lineNum) || lineNum < 1) return;
        var textBox = _activeDocument.EditorTextBox;
        if (textBox.Visibility != Visibility.Visible) return;

        var lines = textBox.GetPlainText().Split('\n');
        if (lineNum > lines.Length) return;

        int startPos = 0;
        for (int i = 0; i < lineNum - 1; i++)
            startPos += lines[i].Length + 1;
        var lineLength = lines[lineNum - 1].Length;

        textBox.ScrollToOffset(startPos);
        // Defer highlight until after WPF has processed the scroll (layout settles at Render priority).
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render,
            () => HighlightSourceRange(textBox, startPos, lineLength));
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

    private void HighlightSourceRange(RichTextBox textBox, int start, int length) {
        ClearSourceHoverHighlight();
        if (length <= 0) return;

        var rect = textBox.GetRectFromOffset(start);
        if (rect == Rect.Empty) return;

        var overlayCanvas = EnsureSourceOverlayCanvas();
        var origin = textBox.TranslatePoint(new Point(0, 0), overlayCanvas);
        var charTopLeft = textBox.TranslatePoint(rect.TopLeft, overlayCanvas);

        // If the target line is scrolled outside the visible area of the source box, don't draw.
        double visibleTop = origin.Y;
        double visibleBottom = origin.Y + textBox.ActualHeight;
        if (charTopLeft.Y < visibleTop || charTopLeft.Y >= visibleBottom) return;

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
        bool bodyChanged        = !string.Equals(strippedNew,     doc.SavedText,   StringComparison.Ordinal);
        bool frontMatterChanged = !string.Equals(newFrontMatter,  doc.FrontMatter, StringComparison.Ordinal);
        // When "Include front matter" is checked, FrontMatter is merged into WorkingText
        // (FrontMatter == ""). Detect that the on-disk frontmatter now differs from what
        // was originally merged in by comparing the full reconstructed text to WorkingText.
        bool mergedFrontMatterChanged = string.IsNullOrEmpty(doc.FrontMatter)
                                        && !string.IsNullOrEmpty(newFrontMatter)
                                        && !string.Equals(newFrontMatter + strippedNew, doc.WorkingText, StringComparison.Ordinal);
        if (!bodyChanged && !frontMatterChanged && !mergedFrontMatterChanged)
            return;
        if (mergedFrontMatterChanged) {
            // Front matter is merged into the editor; rebuild the combined text.
            var newFullText = newFrontMatter + strippedNew;
            doc.FrontMatter = string.Empty;  // keep in merged state
            doc.SavedText   = strippedNew;
            doc.WorkingText = newFullText;
            doc.EditorTextBox.SetPlainText(newFullText);
        } else {
            doc.FrontMatter = newFrontMatter;
            doc.SavedText   = strippedNew;
            if (bodyChanged) {
                doc.WorkingText = strippedNew;
                doc.EditorTextBox.SetPlainText(strippedNew);
            }
            // Front-matter-only change with the editor showing body only: FrontMatter is
            // now up to date in memory; the next Ctrl+S will write it correctly.
        }
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

    // ── Find-in-source bar ────────────────────────────────────────────────────────

    private void ShowEditorFindBar() {
        if (_activeDocument is null) return;

        if (_editorFindBar is not null) {
            _editorFindTextBox?.Focus();
            _editorFindTextBox?.SelectAll();
            return;
        }

        // _sourceEditorHost is already a Grid — add overlay and bar directly.
        _editorFindOverlay = new Canvas {
            IsHitTestVisible = false,
            Background = Brushes.Transparent
        };
        _sourceEditorHost.Children.Add(_editorFindOverlay);

        var sv = FindVisualChildInMdw<ScrollViewer>(_activeDocument.EditorTextBox);
        if (sv is not null)
            sv.ScrollChanged += EditorFind_ScrollChanged;

        var findPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };

        _editorFindTextBox = new TextBox {
            Width = 150,
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 6, 0)
        };
        _editorFindTextBox.TextChanged += EditorFind_TextChanged;
        _editorFindTextBox.PreviewKeyDown += EditorFind_KeyDown;

        var prevBtn = new Button {
            Content = "▲",
            Width = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 2, 0)
        };
        prevBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        prevBtn.Click += (s, e) => EditorFind_NavigatePrevious();

        var nextBtn = new Button {
            Content = "▼",
            Width = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 6, 0)
        };
        nextBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        nextBtn.Click += (s, e) => EditorFind_NavigateNext();

        _editorFindMatchCount = new TextBlock {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            FontSize = (double)Application.Current.Resources["FontSizeSmall"]
        };
        _editorFindMatchCount.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

        var closeBtn = new Button {
            Content = "✕",
            Width = 24,
            Padding = new Thickness(0)
        };
        closeBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        closeBtn.Click += (s, e) => HideEditorFindBar();

        findPanel.Children.Add(_editorFindTextBox);
        findPanel.Children.Add(prevBtn);
        findPanel.Children.Add(nextBtn);
        findPanel.Children.Add(_editorFindMatchCount);
        findPanel.Children.Add(closeBtn);

        _editorFindBar = new Border {
            Child = findPanel,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 10, 10, 0)
        };
        _editorFindBar.SetResourceReference(Border.BackgroundProperty, "PopupSurface");
        _editorFindBar.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        _editorFindBar.BorderThickness = new Thickness(1);

        _sourceEditorHost.Children.Add(_editorFindBar);

        _editorFindTextBox.Focus();
    }

    private void HideEditorFindBar() {
        if (_editorFindBar is null) return;

        if (_activeDocument is not null) {
            var sv = FindVisualChildInMdw<ScrollViewer>(_activeDocument.EditorTextBox);
            if (sv is not null)
                sv.ScrollChanged -= EditorFind_ScrollChanged;
        }

        _sourceEditorHost.Children.Remove(_editorFindBar);
        if (_editorFindOverlay is not null)
            _sourceEditorHost.Children.Remove(_editorFindOverlay);

        _editorFindBar = null;
        _editorFindTextBox = null;
        _editorFindMatchCount = null;
        _editorFindOverlay = null;
        _editorFindMatches.Clear();
        _editorFindCurrentIndex = -1;

        _activeDocument?.EditorTextBox.Focus();
    }

    private void EditorFind_ScrollChanged(object sender, ScrollChangedEventArgs e) {
        EditorFind_RenderHighlights();
    }

    private void EditorFind_TextChanged(object sender, TextChangedEventArgs e) {
        _editorFindDebounceTimer?.Stop();
        _editorFindDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _editorFindDebounceTimer.Tick += (s, args) => {
            _editorFindDebounceTimer.Stop();
            EditorFind_UpdateMatches();
        };
        _editorFindDebounceTimer.Start();
    }

    private void EditorFind_KeyDown(object sender, KeyEventArgs e) {
        if (e.Key == Key.Escape) {
            HideEditorFindBar();
            e.Handled = true;
        } else if (e.Key == Key.Enter || e.Key == Key.F3) {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                EditorFind_NavigatePrevious();
            else
                EditorFind_NavigateNext();
            e.Handled = true;
        }
    }

    private void EditorFind_UpdateMatches() {
        if (_activeDocument is null || _editorFindTextBox is null || _editorFindOverlay is null) return;

        _editorFindMatches.Clear();
        _editorFindCurrentIndex = -1;
        _editorFindOverlay.Children.Clear();

        var searchText = _editorFindTextBox.Text;
        if (string.IsNullOrEmpty(searchText)) {
            if (_editorFindMatchCount is not null)
                _editorFindMatchCount.Text = string.Empty;
            return;
        }

        var text = _activeDocument.EditorTextBox.GetPlainText();
        var index = 0;
        while ((index = text.IndexOf(searchText, index, StringComparison.OrdinalIgnoreCase)) >= 0) {
            _editorFindMatches.Add(index);
            index += searchText.Length;
        }

        if (_editorFindMatches.Count > 0)
            _editorFindCurrentIndex = 0;

        EditorFind_RenderHighlights();
        EditorFind_UpdateMatchCountDisplay();

        if (_editorFindCurrentIndex >= 0)
            EditorFind_ScrollToCurrentMatch();
    }

    private void EditorFind_RenderHighlights() {
        if (_activeDocument is null || _editorFindOverlay is null || _editorFindTextBox is null) return;

        _editorFindOverlay.Children.Clear();

        var isDark = AgentStatusCard.IsDarkTheme;
        var matchBg   = isDark ? Color.FromArgb(200, 74, 62, 16)  : Color.FromArgb(200, 200, 224, 255);
        var currentBg = isDark ? Color.FromArgb(220, 200, 160, 0) : Color.FromArgb(220, 32, 96, 192);
        var searchLen = _editorFindTextBox.Text.Length;
        if (searchLen == 0) return;

        var tb = _activeDocument.EditorTextBox;

        for (int i = 0; i < _editorFindMatches.Count; i++) {
            var pos = _editorFindMatches[i];
            var startRect = tb.GetRectFromOffset(pos);
            if (startRect == Rect.Empty) continue;

            var endPos = Math.Min(pos + searchLen, tb.GetTextLength());
            var endRect = tb.GetRectFromOffset(endPos);
            double highlightWidth = (endRect != Rect.Empty && endRect.Left >= startRect.Left)
                ? Math.Max(2, endRect.Left - startRect.Left)
                : Math.Max(2, searchLen * (startRect.Width > 0 ? startRect.Width : 8));

            var canvasOrigin = tb.TranslatePoint(new Point(startRect.Left, startRect.Top), _editorFindOverlay);

            var highlight = new System.Windows.Shapes.Rectangle {
                Width = highlightWidth,
                Height = Math.Max(2, startRect.Height),
                Fill = new SolidColorBrush(i == _editorFindCurrentIndex ? currentBg : matchBg),
                Opacity = 0.55
            };

            Canvas.SetLeft(highlight, canvasOrigin.X);
            Canvas.SetTop(highlight, canvasOrigin.Y);
            _editorFindOverlay.Children.Add(highlight);
        }

        if (tb.GetTextLength() > 0 && _editorFindMatches.Count > 0) {
            var sv = FindVisualChildInMdw<ScrollViewer>(tb);
            var scrollBar = sv is not null
                ? FindVisualChildInMdw<System.Windows.Controls.Primitives.ScrollBar>(sv)
                : null;
            double trackHeight = scrollBar?.ActualHeight ?? tb.ActualHeight;

            foreach (var pos in _editorFindMatches) {
                var fraction = (double)pos / tb.GetTextLength();
                var tick = new System.Windows.Shapes.Rectangle {
                    Width = 4,
                    Height = 3,
                    Fill = new SolidColorBrush(matchBg)
                };
                Canvas.SetRight(tick, 0);
                Canvas.SetTop(tick, fraction * trackHeight);
                _editorFindOverlay.Children.Add(tick);
            }
        }
    }

    private void EditorFind_UpdateMatchCountDisplay() {
        if (_editorFindMatchCount is null) return;
        _editorFindMatchCount.Text = _editorFindMatches.Count == 0
            ? "No matches"
            : $"{_editorFindCurrentIndex + 1} / {_editorFindMatches.Count}";
    }

    private void EditorFind_NavigateNext() {
        if (_editorFindMatches.Count == 0) return;
        _editorFindCurrentIndex = (_editorFindCurrentIndex + 1) % _editorFindMatches.Count;
        EditorFind_RenderHighlights();
        EditorFind_UpdateMatchCountDisplay();
        EditorFind_ScrollToCurrentMatch();
    }

    private void EditorFind_NavigatePrevious() {
        if (_editorFindMatches.Count == 0) return;
        _editorFindCurrentIndex--;
        if (_editorFindCurrentIndex < 0)
            _editorFindCurrentIndex = _editorFindMatches.Count - 1;
        EditorFind_RenderHighlights();
        EditorFind_UpdateMatchCountDisplay();
        EditorFind_ScrollToCurrentMatch();
    }

    private void EditorFind_ScrollToCurrentMatch() {
        if (_activeDocument is null || _editorFindCurrentIndex < 0 || _editorFindCurrentIndex >= _editorFindMatches.Count) return;

        var tb = _activeDocument.EditorTextBox;
        var pos = _editorFindMatches[_editorFindCurrentIndex];

        tb.ScrollToOffset(pos);

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => {
            var sv = FindVisualChildInMdw<ScrollViewer>(tb);
            if (sv is not null && _editorFindTextBox is not null) {
                var matchRect = tb.GetRectFromOffset(pos);
                if (matchRect != Rect.Empty) {
                    const double margin = 24;
                    if (matchRect.Left < 0)
                        sv.ScrollToHorizontalOffset(Math.Max(0, sv.HorizontalOffset + matchRect.Left - margin));
                    else if (matchRect.Right > tb.ActualWidth)
                        sv.ScrollToHorizontalOffset(sv.HorizontalOffset + matchRect.Right - tb.ActualWidth + margin);
                }
            }

            EditorFind_RenderHighlights();
            _editorFindTextBox?.Focus();
        });
    }

    private static T? FindVisualChildInMdw<T>(DependencyObject parent) where T : DependencyObject {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var result = FindVisualChildInMdw<T>(child);
            if (result is not null) return result;
        }
        return null;
    }
}

[System.Runtime.InteropServices.ComVisible(true)]
public sealed class MarkdownDocumentScriptingBridge {
    private readonly Action<string>  _navigate;
    private readonly Action<string>? _hoverElement;
    private readonly Action<string>? _showScreenshotMenu;
    private readonly Action<string>? _showImageMenu;
    private readonly Action<string>? _clickElement;

    public MarkdownDocumentScriptingBridge(
        Action<string>  navigate,
        Action<string>? hoverElement       = null,
        Action<string>? showScreenshotMenu = null,
        Action<string>? showImageMenu      = null,
        Action<string>? clickElement       = null) {
        _navigate           = navigate;
        _hoverElement       = hoverElement;
        _showScreenshotMenu = showScreenshotMenu;
        _showImageMenu      = showImageMenu;
        _clickElement       = clickElement;
    }

    public void Navigate(string href)            => _navigate(href);
    public void HoverElement(string lineHint)    => _hoverElement?.Invoke(lineHint);
    public void ShowScreenshotMenu(string path)  => _showScreenshotMenu?.Invoke(path);
    public void ShowImageMenu(string path)       => _showImageMenu?.Invoke(path);
    public void ClickElement(string lineHint)    => _clickElement?.Invoke(lineHint);
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
            el.addEventListener('click', function(ev) {
                ev.stopPropagation();
                var lineHint = el.getAttribute('data-source-line');
                if (lineHint) {
                    try { window.external.ClickElement(lineHint); } catch(ex) {}
                }
            });
        })(elements[i]);
    }
})();
";
}

/// <summary>
/// Enables the note-title editing row and "Edit Note" window title when
/// opening a MarkdownDocumentWindow for note editing.
/// </summary>
internal sealed record NoteEditContext(string InitialTitle, Action<string> OnTitleCommit);

/// <summary>
/// Enables the description editing row and "Edit Loop" window title when
/// opening a MarkdownDocumentWindow for loop editing.
/// </summary>
internal sealed record LoopEditContext(string InitialDescription, Action<string> OnDescriptionCommit);

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
    /// Optional callback invoked when the user chooses "Add to chat" from the source
    /// editor context menu. Receives the selected markdown text.
    /// </summary>
    public Action<string>? AddToChatCallback { get; init; }


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

    /// <summary>
    /// Optional callbacks for starting/stopping push-to-talk voice inside the Revise-with-AI popup.
    /// <see cref="StartPttCallback"/> receives the instruction TextBox; <see cref="StopPttCallback"/> ends the session.
    /// </summary>
    public Action<TextBox>? StartPttCallback { get; init; }
    public Action? StopPttCallback { get; init; }

    /// <summary>
    /// Prompt sent to the AI when the user presses Ctrl+Shift+C (Quick Cleanup) inside the editor.
    /// If null or empty, Quick Cleanup is disabled.
    /// </summary>
    public string? CleanupPrompt { get; init; }
}
