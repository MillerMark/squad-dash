using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SquadDash;

internal sealed class ToolIconGalleryWindow : Window {
    private static readonly (string ToolName, string ResourceKey, string Description)[] Icons = [
        ("grep",          "ToolIcon_grep",          "Search — grep, glob"),
        ("view",          "ToolIcon_view",           "Read file — view"),
        ("edit",          "ToolIcon_edit",           "Edit file — edit"),
        ("web_fetch",     "ToolIcon_web_fetch",      "HTTP — web_fetch"),
        ("create",        "ToolIcon_create",         "New file — create"),
        ("task",          "ToolIcon_task",           "Sub-agent — task"),
        ("skill",         "ToolIcon_skill",          "Skill call — skill"),
        ("store_memory",  "ToolIcon_store_memory",   "Persist fact — store_memory"),
        ("report_intent", "ToolIcon_report_intent",  "Intent marker — report_intent"),
        ("sql",           "ToolIcon_sql",            "Database — sql"),
        ("powershell",    "ToolIcon_powershell",     "Shell — powershell"),
        ("(default)",     "ToolIcon_default",        "Fallback for unmapped tools"),
    ];

    private ToolIconGalleryWindow() {
        Title = "Tool Icon Gallery";
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Width = 480;
        MaxHeight = 700;
        this.SetResourceReference(BackgroundProperty, "AppSurface");

        var root = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        Content = root;

        // Title bar
        var titleBlock = new TextBlock {
            Text = "Tool Icons",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(20, 18, 20, 12)
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        DockPanel.SetDock(titleBlock, Dock.Top);
        root.Children.Add(titleBlock);

        // Close button
        var closeButton = new Button {
            Content = "Close",
            Width = 80,
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(20, 8, 20, 0)
        };
        closeButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        closeButton.Click += (_, _) => Close();
        DockPanel.SetDock(closeButton, Dock.Bottom);
        root.Children.Add(closeButton);

        // Scroll area
        var scroll = new ScrollViewer {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        root.Children.Add(scroll);

        var grid = new Grid { Margin = new Thickness(20, 0, 20, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        scroll.Content = grid;

        AddHeaderRow(grid, 0);

        for (var i = 0; i < Icons.Length; i++)
            AddIconRow(grid, Icons[i].ToolName, Icons[i].ResourceKey, Icons[i].Description, i + 1);
    }

    private void AddHeaderRow(Grid grid, int row) {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var headerBorder = new Border {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(0, 0, 0, 4)
        };
        headerBorder.SetResourceReference(Border.BorderBrushProperty, "InputBorder");
        Grid.SetColumnSpan(headerBorder, 3);
        Grid.SetRow(headerBorder, row);

        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

        var headerLabels = new[] { ("Icon", 40), ("Tool Name", 110), ("Description", 200) };
        foreach (var (label, _) in headerLabels) {
            var tb = new TextBlock {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Margin = new Thickness(0, 0, 12, 0)
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
            headerPanel.Children.Add(tb);
        }

        headerBorder.Child = headerPanel;
        grid.Children.Add(headerBorder);
    }

    private void AddIconRow(Grid grid, string toolName, string resourceKey, string description, int row) {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Icon cell
        var source = TryFindResource(resourceKey) as ImageSource;
        var img = new Image {
            Width = 20,
            Height = 20,
            Source = source,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 5),
            Visibility = source is not null ? Visibility.Visible : Visibility.Collapsed
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        Grid.SetColumn(img, 0);
        Grid.SetRow(img, row);
        grid.Children.Add(img);

        // Tool name cell
        var nameBlock = new TextBlock {
            Text = toolName,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 5, 8, 5)
        };
        nameBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        Grid.SetColumn(nameBlock, 1);
        Grid.SetRow(nameBlock, row);
        grid.Children.Add(nameBlock);

        // Description cell
        var descBlock = new TextBlock {
            Text = description,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 5)
        };
        descBlock.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        Grid.SetColumn(descBlock, 2);
        Grid.SetRow(descBlock, row);
        grid.Children.Add(descBlock);
    }

    public static void Show(Window owner) {
        var window = new ToolIconGalleryWindow { Owner = owner };
        window.Show();
    }
}
