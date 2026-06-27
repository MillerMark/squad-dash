using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;

namespace SquadDash;

/// <summary>
/// Developer window that displays all named font-size tokens, each rendered
/// at its own size. Click a key to see which repo files reference it.
/// Right-click a label to copy its resource key name.
/// </summary>
internal sealed class FontSizesWindow : Window
{
    private static readonly string RepoRoot = @"D:\Drive\Source\SquadDash-public";

    private static readonly string[] FontSizeKeys =
    [
        "FontSizeTiny",
        "FontSizeXSmall",
        "FontSizeSmall",
        "FontSizeBody",
        "FontSizeNormal",
        "FontSizeMedium",
        "FontSizeLarge",
        "FontSizeLargePlus",
        "FontSizeSubtitle",
        "FontSizeTitle",
        "FontSizeHeading",
        "FontSizeHero",
    ];

    private readonly ListBox _keyList;
    private readonly ListBox _fileList;
    private readonly TextBlock _refHeader;

    internal FontSizesWindow(Window owner)
    {
        Title = "Font Size Explorer";
        Width = 640;
        Height = 500;
        ShowInTaskbar = false;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var chrome = new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(4),
            CornerRadius = new CornerRadius(0),
            GlassFrameThickness = new Thickness(0)
        };
        WindowChrome.SetWindowChrome(this, chrome);
        SetResourceReference(BackgroundProperty, "AppSurface");

        var root = new DockPanel();
        Content = root;

        // ── Custom title bar ──────────────────────────────────────────────
        var titleBar = new DockPanel { Height = 30 };
        titleBar.SetResourceReference(BackgroundProperty, "PanelBorder");
        titleBar.MouseLeftButtonDown += (_, _) => DragMove();
        DockPanel.SetDock(titleBar, Dock.Top);
        root.Children.Add(titleBar);

        var closeBtn = new Button
        {
            Content = "×",
            Width = 36,
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        closeBtn.SetResourceReference(ForegroundProperty, "LabelText");
        closeBtn.Click += (_, _) => Close();
        DockPanel.SetDock(closeBtn, Dock.Right);
        titleBar.Children.Add(closeBtn);

        var titleText = new TextBlock
        {
            Text = "Font Size Explorer",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            FontSize = 13
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        titleBar.Children.Add(titleText);

        // ── Two-column body ───────────────────────────────────────────────
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(grid);

        // ── Left panel: key list ──────────────────────────────────────────
        _keyList = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            BorderThickness = new Thickness(0),
        };
        _keyList.SetResourceReference(BackgroundProperty, "AppSurface");
        ScrollViewer.SetHorizontalScrollBarVisibility(_keyList, ScrollBarVisibility.Disabled);
        Grid.SetColumn(_keyList, 0);
        grid.Children.Add(_keyList);

        foreach (var key in FontSizeKeys)
        {
            var tb = new TextBlock
            {
                Text = key,
                Cursor = Cursors.Hand,
                Margin = new Thickness(4, 3, 4, 3)
            };
            tb.SetResourceReference(TextBlock.FontSizeProperty, key);
            tb.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

            var cm = new ContextMenu();
            var mi = new MenuItem { Header = "Copy name" };
            mi.Click += (_, _) => Clipboard.SetText(key);
            cm.Items.Add(mi);

            var item = new ListBoxItem { Content = tb, ContextMenu = cm };
            _keyList.Items.Add(item);
        }

        // ── Splitter ──────────────────────────────────────────────────────
        var splitter = new GridSplitter
        {
            Width = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext
        };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);

        // ── Right panel: file references ──────────────────────────────────
        var rightPanel = new DockPanel();
        Grid.SetColumn(rightPanel, 2);
        grid.Children.Add(rightPanel);

        _refHeader = new TextBlock
        {
            Text = "Select a font size",
            Margin = new Thickness(8, 6, 8, 4),
            FontSize = 11
        };
        _refHeader.SetResourceReference(ForegroundProperty, "SubtleText");
        DockPanel.SetDock(_refHeader, Dock.Top);
        rightPanel.Children.Add(_refHeader);

        _fileList = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            BorderThickness = new Thickness(0),
        };
        _fileList.SetResourceReference(BackgroundProperty, "AppSurface");
        ScrollViewer.SetHorizontalScrollBarVisibility(_fileList, ScrollBarVisibility.Auto);
        rightPanel.Children.Add(_fileList);

        _keyList.SelectionChanged += OnKeySelected;
    }

    private void OnKeySelected(object sender, SelectionChangedEventArgs e)
    {
        _fileList.Items.Clear();

        if (_keyList.SelectedItem is not ListBoxItem { Content: TextBlock { Text: string key } })
            return;

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs", ".xaml", ".md" };
        var matches = Directory
            .EnumerateFiles(RepoRoot, "*.*", SearchOption.AllDirectories)
            .Where(p => extensions.Contains(Path.GetExtension(p)))
            .Where(p => !p.Contains(@"\bin\") && !p.Contains(@"\obj\") && !p.Contains(@"\node_modules\"))
            .Where(p => { try { return File.ReadAllText(p).Contains(key, StringComparison.Ordinal); } catch { return false; } })
            .Select(p => p.Substring(RepoRoot.Length).TrimStart('\\').Replace('\\', '/'))
            .OrderBy(r => r)
            .ToList();

        _refHeader.Text = matches.Count > 0
            ? $"References: {key}  ({matches.Count} files)"
            : $"References: {key}  (0 files)";

        if (matches.Count == 0)
        {
            var none = new TextBlock
            {
                Text = "(none found)",
                FontStyle = FontStyles.Italic,
                FontSize = 11
            };
            none.SetResourceReference(ForegroundProperty, "SubtleText");
            _fileList.Items.Add(none);
            return;
        }

        foreach (var rel in matches)
        {
            var fullPath = Path.Combine(RepoRoot, rel.Replace('/', '\\'));
            var fileTb = new TextBlock
            {
                Text = rel,
                FontSize = 11,
                Margin = new Thickness(4, 2, 4, 2),
                ToolTip = fullPath
            };
            fileTb.SetResourceReference(ForegroundProperty, "LabelText");

            var fileCm = new ContextMenu();
            var fileMi = new MenuItem { Header = "Copy path" };
            fileMi.Click += (_, _) => Clipboard.SetText(rel);
            fileCm.Items.Add(fileMi);

            _fileList.Items.Add(new ListBoxItem { Content = fileTb, ContextMenu = fileCm });
        }
    }
}
