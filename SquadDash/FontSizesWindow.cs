using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;

namespace SquadDash;

/// <summary>
/// Developer window that displays all named font-size tokens, each rendered
/// at its own size. Right-click a label to copy its resource key name.
/// </summary>
internal sealed class FontSizesWindow : Window
{
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
        "FontSizeDisplay",
    ];

    internal FontSizesWindow(Window owner)
    {
        Title = "Font Size Explorer";
        Width = 320;
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

        // ── Scrollable content ────────────────────────────────────────────
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        root.Children.Add(scroll);

        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 12) };
        scroll.Content = stack;

        for (int i = 0; i < FontSizeKeys.Length; i++)
        {
            var key = FontSizeKeys[i];
            bool isLast = i == FontSizeKeys.Length - 1;

            var tb = new TextBlock
            {
                Text = key,
                Margin = new Thickness(12, 6, 12, isLast ? 6 : 0),
                Cursor = Cursors.Hand
            };
            tb.SetResourceReference(TextBlock.FontSizeProperty, key);
            tb.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

            var cm = new ContextMenu();
            var mi = new MenuItem { Header = "Copy name" };
            mi.Click += (_, _) => Clipboard.SetText(key);
            cm.Items.Add(mi);
            tb.ContextMenu = cm;

            stack.Children.Add(tb);
        }
    }
}
