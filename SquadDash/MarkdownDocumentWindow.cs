using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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

        foreach (var document in _documents) {
            document.EditorTextBox.SelectionChanged += EditorTextBox_SelectionChanged;
            document.EditorTextBox.PreviewKeyDown   += EditorTextBox_PreviewKeyDown;
        }

        var tbStack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(0, 0, 0, 6) };
        foreach (var btn in new[] { (Button)_srcBoldButton, _srcItalicButton, srcLinkBtn, srcTableBtn, srcCodeBtn, srcBlockBtn })
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

        PreviewMouseDown += MarkdownDocumentWindow_PreviewMouseDown;

        ActivateDocument(_documents[0], preserveCurrentState: false);
        UpdatePreviewHostVisibility();
        UpdateSourcePaneVisibility();
        UpdateChrome();
    }

    public static void Show(Window? owner, string title, string filePath) {
        Show(owner, title, [new MarkdownDocumentSpec(Path.GetFileNameWithoutExtension(filePath), filePath)]);
    }

    public static void Show(Window? owner, string title, IReadOnlyList<MarkdownDocumentSpec> documents) {
        var window = new MarkdownDocumentWindow(title, documents);
        if (owner is not null)
            window.Owner = owner;

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
        RenderPreview(document);
        UpdateChrome();
    }

    private void EditorTextBox_SelectionChanged(object sender, System.Windows.RoutedEventArgs e) {
        if (sender is not TextBox { Tag: MarkdownDocumentTabState doc } tb || !ReferenceEquals(doc, _activeDocument))
            return;
        var hasSelection = tb.SelectionLength > 0;
        if (_srcBoldButton   is not null) _srcBoldButton.IsEnabled   = hasSelection;
        if (_srcItalicButton is not null) _srcItalicButton.IsEnabled = hasSelection;
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
        }
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
            case "B":       MarkdownEditorCommands.ApplyBold(tb);        break;
            case "I":       MarkdownEditorCommands.ApplyItalic(tb);      break;
            case "Link":    MarkdownEditorCommands.InsertLink(tb);       break;
            case "Table":   MarkdownEditorCommands.InsertTable(tb);      break;
            case "`code`":  MarkdownEditorCommands.InsertInlineCode(tb); break;
            case "{ }":     MarkdownEditorCommands.InsertCodeBlock(tb);  break;
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
        File.WriteAllText(document.FilePath, document.WorkingText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        document.SavedText = document.WorkingText;
        document.IsDirty = false;
        RenderPreview(document);
        UpdateChrome($"Saved {document.FileName} at {DateTime.Now:t}");
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
            lineHint => Dispatcher.BeginInvoke(new Action(() => HighlightSourceFromHover(lineHint))));
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
        if (string.Equals(newText, doc.SavedText, StringComparison.Ordinal))
            return;
        doc.SavedText = newText;
        doc.WorkingText = newText;
        doc.EditorTextBox.Text = newText;
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
        SavedText = text;
        WorkingText = text;

        WebBrowser = new WebBrowser();
        WebBrowser.Tag = this;
        FallbackViewer = new FlowDocumentScrollViewer {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        FallbackViewer.SetResourceReference(Control.BackgroundProperty, "TranscriptSurface");
        EditorTextBox = new TextBox {
            Text = text,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
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
}

internal static class MarkdownHtmlBuilder {
    private static readonly Regex InlineCodeRegex = new("`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex ImageRegex = new(@"!\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex LinkRegex = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex BoldRegex = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex ItalicRegex = new(@"(?<!\*)\*(?!\s)(.+?)(?<!\s)\*(?!\*)", RegexOptions.Compiled);

    public static string Build(string markdown, string title, string? filePath = null, bool isDark = false) {
        var body = BuildBody(markdown ?? string.Empty);
        var baseTag = string.Empty;
        if (!string.IsNullOrEmpty(filePath)) {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) {
                var normalized = dir.Replace('\\', '/').TrimEnd('/') + '/';
                baseTag = $"\n<base href=\"file:///{normalized}\" />";
            }
        }

        var bg          = isDark ? "#1e1c17"                  : "#fffdf9";
        var fg          = isDark ? "#d8c8b0"                  : "#31281f";
        var line        = isDark ? "rgba(216,200,176,0.14)"   : "rgba(49,40,31,0.14)";
        var lineStrong  = isDark ? "rgba(216,200,176,0.28)"   : "rgba(49,40,31,0.22)";
        var quote       = isDark ? "#27221a"                  : "#f7f2e9";
        var code        = isDark ? "#252018"                  : "#f6f1e9";
        var link        = isDark ? "#6890c8"                  : "#2d5ea8";
        var headingColor = isDark ? "#e5d5c0" : "#2a211a";
        var thHeaderBg  = isDark ? "rgba(216,200,176,0.07)"   : "rgba(49,40,31,0.05)";

        return $$"""
<!DOCTYPE html>
<html>
<head>
<meta http-equiv="X-UA-Compatible" content="IE=edge" />{{baseTag}}
<meta charset="utf-8">
<title>{{EscapeHtml(title)}}</title>
<style>
  body {
    margin: 0;
    padding: 26px;
    background: {{bg}};
    color: {{fg}};
    font: 15px/1.55 "Segoe UI", sans-serif;
  }
  h1,h2,h3,h4 { margin: 1.2em 0 0.45em; color: {{headingColor}}; }
  h1:first-child,h2:first-child,h3:first-child,h4:first-child { margin-top: 0; }
  h1 { font-size: 1.75rem; }
  h2 { font-size: 1.4rem; }
  h3 { font-size: 1.15rem; }
  p { margin: 0 0 0.9em; }
  blockquote {
    margin: 0 0 1em;
    padding: 0.8em 1em;
    background: {{quote}};
    border-left: 3px solid {{lineStrong}};
    border-radius: 10px;
  }
  pre {
    margin: 0 0 1em;
    padding: 0.9em 1em;
    background: {{code}};
    border: 1px solid {{line}};
    border-radius: 10px;
    overflow: auto;
    font: 13px/1.45 Consolas, monospace;
  }
  code {
    background: {{code}};
    border-radius: 4px;
    padding: 0.08em 0.32em;
    font: 0.95em Consolas, monospace;
  }
  ul {
    margin: 0 0 1em 1.2em;
    padding: 0;
  }
  li { margin: 0.18em 0; }
  hr {
    border: none;
    border-top: 1px solid {{line}};
    margin: 1em 0 1.15em;
  }
  table {
    width: auto;
    border-collapse: collapse;
    margin: 0 0 1em;
  }
  th, td {
    border: 1px solid {{line}};
    padding: 0.45em 0.6em;
    vertical-align: top;
    text-align: left;
    white-space: nowrap;
  }
  th {
    background: {{thHeaderBg}};
    font-weight: 600;
  }
  a { color: {{link}}; text-decoration: none; }
  a:hover { text-decoration: underline; }
  a[href] { cursor: pointer; }
  /* Ensure transparent-corner images never show a black host-window background. */
  html { background: {{bg}}; }
  img { background-color: {{(isDark ? "#000000" : bg)}}; }
  /* Themed scrollbar — webkit, Firefox, IE */
  html, body {
    scrollbar-width: thin;
    scrollbar-color: {{(isDark ? "#555 #2a2a2a" : "#aaa #f0f0f0")}};
    {{(isDark
        ? "scrollbar-base-color:#555;scrollbar-face-color:#555;scrollbar-track-color:#2a2a2a;scrollbar-arrow-color:#777;scrollbar-highlight-color:#2a2a2a;scrollbar-shadow-color:#333;"
        : "scrollbar-base-color:#aaa;scrollbar-face-color:#aaa;scrollbar-track-color:#f0f0f0;scrollbar-arrow-color:#888;scrollbar-highlight-color:#f0f0f0;scrollbar-shadow-color:#ccc;"
    )}}
  }
  pre {
    scrollbar-width: thin;
    scrollbar-color: {{(isDark ? "#555 #2a2a2a" : "#aaa #f0f0f0")}};
  }
  ::-webkit-scrollbar {
    width: 11px;
    height: 11px;
  }
  ::-webkit-scrollbar-track {
    background: {{(isDark ? "#2a2a2a" : "#f0f0f0")}};
    border-radius: 6px;
  }
  ::-webkit-scrollbar-thumb {
    background: {{(isDark ? "#555" : "#aaa")}};
    border-radius: 6px;
  }
  ::-webkit-scrollbar-thumb:hover {
    background: {{(isDark ? "#777" : "#888")}};
  }
  ::-webkit-scrollbar-thumb:active {
    background: {{(isDark ? "#999" : "#666")}};
  }
</style>
</head>
<body>
{{body}}
<script>
// Suppress IE script error dialogs — errors in our injected scripts are handled gracefully.
window.onerror = function() { return true; };
document.addEventListener('click', function(e) {
  var a = e.target;
  while (a && a.tagName !== 'A') a = a.parentElement;
  if (a && a.href) {
    e.preventDefault();
    e.stopPropagation();
    try { window.external.Navigate(a.href); } catch(ex) {}
  }
});
</script>
<script>
(function() {
  // ── 📸 placeholder blockquotes: right-click to paste screenshot ──────────
  var bqList = document.querySelectorAll('blockquote');
  for (var i = 0; i < bqList.length; i++) { (function(bq) {
    var text = bq.textContent || '';
    if (text.indexOf('\uD83D\uDCF8') === -1 && text.indexOf('Screenshot needed') === -1) return;
    var imgSrc = '';
    var prev = bq.previousElementSibling;
    if (prev) {
      var img = prev.tagName === 'IMG' ? prev : prev.querySelector('img');
      if (img) imgSrc = img.getAttribute('src') || '';
    }
    bq.style.cursor = 'context-menu';
    bq.oncontextmenu = function(e) {
      if (e && e.preventDefault) e.preventDefault();
      if (e) e.cancelBubble = true;
      var src = imgSrc;
      window.setTimeout(function() {
        try { window.external.ShowScreenshotMenu(src); } catch(ex) {}
      }, 0);
      return false;
    };
  })(bqList[i]); }

  // ── Existing images: right-click to replace from clipboard ───────────────
  var imgBg = '{{(isDark ? "#000000" : bg)}}';
  var imgList = document.querySelectorAll('img');
  for (var j = 0; j < imgList.length; j++) { (function(img) {
    img.style.backgroundColor = imgBg;
    img.style.cursor = 'context-menu';
    img.oncontextmenu = function(e) {
      if (e && e.preventDefault) e.preventDefault();
      if (e) e.cancelBubble = true;
      var src = img.getAttribute('src') || '';
      window.setTimeout(function() {
        try { window.external.ShowImageMenu(src); } catch(ex) {}
      }, 0);
      return false;
    };
  })(imgList[j]); }
})();
</script>
</body>
</html>
""";
    }

    private static string BuildBody(string markdown) {
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var builder = new StringBuilder();

        for (var index = 0; index < lines.Length; index++) {
            var lineNum = index + 1; // 1-based line numbers
            var trimmed = lines[index].Trim();
            if (trimmed.Length == 0)
                continue;

            if (trimmed.StartsWith("```", StringComparison.Ordinal)) {
                var code = new StringBuilder();
                var startLine = lineNum;
                index++;
                while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal)) {
                    if (code.Length > 0)
                        code.Append('\n');
                    code.Append(lines[index]);
                    index++;
                }

                builder.Append($"<pre data-source-line=\"{startLine}\"><code>")
                    .Append(EscapeHtml(code.ToString()))
                    .AppendLine("</code></pre>");
                continue;
            }

            if (TryReadTable(lines, ref index, out var rows)) {
                builder.AppendLine(BuildTable(rows, lineNum));
                continue;
            }

            if (IsHorizontalRule(trimmed)) {
                builder.AppendLine($"<hr data-source-line=\"{lineNum}\" />");
                continue;
            }

            if (trimmed.StartsWith("#", StringComparison.Ordinal)) {
                var level = Math.Clamp(trimmed.TakeWhile(character => character == '#').Count(), 1, 4);
                builder.Append('<').Append('h').Append(level).Append($" data-source-line=\"{lineNum}\">")
                    .Append(RenderInline(trimmed[level..].Trim()))
                    .Append("</h").Append(level).AppendLine(">");
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal)) {
                builder.Append($"<blockquote data-source-line=\"{lineNum}\"><p>")
                    .Append(RenderInline(trimmed[2..].Trim()))
                    .AppendLine("</p></blockquote>");
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal)) {
                builder.AppendLine($"<ul data-source-line=\"{lineNum}\">");
                while (index < lines.Length) {
                    var listLine = lines[index].Trim();
                    if (!listLine.StartsWith("- ", StringComparison.Ordinal) &&
                        !listLine.StartsWith("* ", StringComparison.Ordinal)) {
                        index--;
                        break;
                    }

                    var itemLineNum = index + 1; // 1-based line number for this specific item
                    builder.Append($"<li data-source-line=\"{itemLineNum}\">")
                        .Append(RenderInline(listLine[2..].Trim()))
                        .AppendLine("</li>");
                    index++;
                }
                builder.AppendLine("</ul>");
                continue;
            }

            if (IsOrderedListItem(trimmed, out var olText)) {
                builder.AppendLine($"<ol data-source-line=\"{lineNum}\">");
                while (index < lines.Length) {
                    var listLine = lines[index].Trim();
                    if (!IsOrderedListItem(listLine, out var itemText)) {
                        index--;
                        break;
                    }
                    var itemLineNum = index + 1; // 1-based line number for this specific item
                    builder.Append($"<li data-source-line=\"{itemLineNum}\">")
                        .Append(RenderInline(itemText))
                        .AppendLine("</li>");
                    index++;
                }
                builder.AppendLine("</ol>");
                continue;
            }

            // Standalone image line: ![alt](src)
            var imgMatch = ImageRegex.Match(trimmed);
            if (imgMatch.Success && imgMatch.Index == 0 && imgMatch.Length == trimmed.Length) {
                var alt = EscapeHtml(imgMatch.Groups[1].Value);
                var src = EscapeHtml(imgMatch.Groups[2].Value);
                builder.AppendLine($"<p data-source-line=\"{lineNum}\"><img src=\"{src}\" alt=\"{alt}\" style=\"max-width:100%;\" /></p>");
                continue;
            }

            var paragraphLines = new List<string> { trimmed };
            while (index + 1 < lines.Length) {
                var next = lines[index + 1].Trim();
                if (next.Length == 0 ||
                    next.StartsWith("#", StringComparison.Ordinal) ||
                    next.StartsWith("> ", StringComparison.Ordinal) ||
                    next.StartsWith("- ", StringComparison.Ordinal) ||
                    next.StartsWith("* ", StringComparison.Ordinal) ||
                    next.StartsWith("```", StringComparison.Ordinal) ||
                    IsHorizontalRule(next) ||
                    (IsTableRow(next) && index + 2 < lines.Length && IsTableSeparator(lines[index + 2]))) {
                    break;
                }

                paragraphLines.Add(next);
                index++;
            }

            builder.Append($"<p data-source-line=\"{lineNum}\">")
                .Append(RenderInline(string.Join(" ", paragraphLines)))
                .AppendLine("</p>");
        }

        return builder.ToString();
    }

    private static string RenderInline(string text) {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var escaped = EscapeHtml(text);
        var codePlaceholders = new Dictionary<string, string>();
        var placeholderIndex = 0;

        escaped = InlineCodeRegex.Replace(escaped, match => {
            var key = $"@@CODE{placeholderIndex++}@@";
            codePlaceholders[key] = $"<code>{match.Groups[1].Value}</code>";
            return key;
        });

        // Images before links so the ![alt](src) pattern isn't consumed by LinkRegex.
        escaped = ImageRegex.Replace(escaped, "<img src=\"$2\" alt=\"$1\" style=\"max-width:100%;\" />");
        escaped = LinkRegex.Replace(escaped, "<a href=\"$2\">$1</a>");
        escaped = BoldRegex.Replace(escaped, "<strong>$1</strong>");
        escaped = ItalicRegex.Replace(escaped, "<em>$1</em>");

        foreach (var pair in codePlaceholders)
            escaped = escaped.Replace(pair.Key, pair.Value, StringComparison.Ordinal);

        return escaped;
    }

    private static string BuildTable(IReadOnlyList<string[]> rows, int lineNum) {
        var builder = new StringBuilder();
        builder.AppendLine($"<table data-source-line=\"{lineNum}\">");

        if (rows.Count > 0) {
            // Header row is at lineNum; the separator row (lineNum+1) is not emitted.
            builder.AppendLine($"<thead><tr data-source-line=\"{lineNum}\">");
            foreach (var cell in rows[0])
                builder.Append("<th>").Append(RenderInline(cell)).AppendLine("</th>");
            builder.AppendLine("</tr></thead>");
        }

        if (rows.Count > 1) {
            builder.AppendLine("<tbody>");
            for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++) {
                // rows[0]=header@lineNum, separator@lineNum+1, rows[1]@lineNum+2, rows[k]@lineNum+1+k
                var rowLineNum = lineNum + 1 + rowIndex;
                builder.AppendLine($"<tr data-source-line=\"{rowLineNum}\">");
                foreach (var cell in rows[rowIndex])
                    builder.Append("<td>").Append(RenderInline(cell)).AppendLine("</td>");
                builder.AppendLine("</tr>");
            }
            builder.AppendLine("</tbody>");
        }

        builder.AppendLine("</table>");
        return builder.ToString();
    }

    private static bool TryReadTable(string[] lines, ref int index, out List<string[]> rows) {
        rows = [];
        if (!IsTableRow(lines[index]) || index + 1 >= lines.Length || !IsTableSeparator(lines[index + 1]))
            return false;

        rows.Add(ParseTableRow(lines[index]));
        index++;

        while (index + 1 < lines.Length && IsTableRow(lines[index + 1])) {
            rows.Add(ParseTableRow(lines[index + 1]));
            index++;
        }

        return rows.Count > 0;
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

        return ParseTableRow(line)
            .All(cell => cell.Length > 0 && cell.All(character => character is '-' or ':' or ' '));
    }

    private static string[] ParseTableRow(string line) {
        return line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
    }

    private static bool IsHorizontalRule(string line) {
        return line.Length >= 3 && line.All(character => character is '-' or '_' or '*');
    }

    private static bool IsOrderedListItem(string line, out string text) {
        var dotIdx = line.IndexOf(". ", StringComparison.Ordinal);
        if (dotIdx > 0 && line[..dotIdx].All(char.IsDigit)) {
            text = line[(dotIdx + 2)..].Trim();
            return true;
        }
        text = string.Empty;
        return false;
    }

    private static string EscapeHtml(string text) {
        return text
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
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
            FontFamily = new FontFamily("Segoe UI"),
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

    private static void AddInlineText(InlineCollection inlines, string text, Brush codeFill) {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var segments = normalized.Split('`');

        for (var index = 0; index < segments.Length; index++) {
            if (segments[index].Length == 0)
                continue;

            var run = new Run(segments[index]);
            if (index % 2 == 1) {
                run.FontFamily = new FontFamily("Consolas");
                run.Background = codeFill;
            }

            inlines.Add(run);
        }
    }
}

[System.Runtime.InteropServices.ComVisible(true)]
public sealed class MarkdownDocumentScriptingBridge {
    private readonly Action<string> _navigate;
    private readonly Action<string>? _hoverElement;

    public MarkdownDocumentScriptingBridge(Action<string> navigate, Action<string>? hoverElement = null) {
        _navigate = navigate;
        _hoverElement = hoverElement;
    }

    public void Navigate(string href) => _navigate(href);

    public void HoverElement(string lineHint) => _hoverElement?.Invoke(lineHint);
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
