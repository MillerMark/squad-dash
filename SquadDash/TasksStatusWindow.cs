using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Shell;

namespace SquadDash;

internal sealed class TasksStatusWindow : Window {
    private readonly TextBox _contentTextBox;

    public TasksStatusWindow() {
        Title = "Live Tasks";
        Width = 560;
        Height = 420;
        MinWidth = 420;
        MinHeight = 260;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight         = 36,
            ResizeBorderThickness = new Thickness(4),
            GlassFrameThickness   = new Thickness(0),
            UseAeroCaptionButtons = false,
        });

        SourceInitialized += (_, _) =>
            NativeMethods.DisableRoundedCorners(new WindowInteropHelper(this).Handle);

        var outerBorder = new Border { BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(4) };
        outerBorder.SetResourceReference(Border.BackgroundProperty, "AppSurface");
        outerBorder.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");

        var root = new Grid {
            Margin = new Thickness(12)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outerBorder.Child = root;
        Content = outerBorder;

        var header = new DockPanel {
            LastChildFill = false,
            Background    = Brushes.Transparent,
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var closeButton = new Button {
            Content = "Close",
            MinWidth = 76,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        closeButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(closeButton, true);
        closeButton.Click += (_, _) => Close();
        DockPanel.SetDock(closeButton, Dock.Right);
        header.Children.Add(closeButton);

        var copyButton = new Button {
            Content = "Copy",
            MinWidth = 76,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        copyButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(copyButton, true);
        copyButton.Click += (_, _) => {
            var text = _contentTextBox?.Text;
            if (!string.IsNullOrEmpty(text))
                Clipboard.SetText(text);
        };
        DockPanel.SetDock(copyButton, Dock.Right);
        header.Children.Add(copyButton);

        var titleBlock = new TextBlock {
            Text = "Live Tasks",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        header.Children.Add(titleBlock);

        var hintBlock = new TextBlock {
            Text = "Use /dropTasks to hide this window.",
            Margin = new Thickness(0, 8, 0, 10),
            TextWrapping = TextWrapping.Wrap
        };
        hintBlock.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        Grid.SetRow(hintBlock, 1);
        root.Children.Add(hintBlock);

        var contentBorder = new Border {
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12)
        };
        contentBorder.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        contentBorder.SetResourceReference(Border.BorderBrushProperty, "LineColor");
        Grid.SetRow(contentBorder, 2);
        root.Children.Add(contentBorder);

        _contentTextBox = new TextBox {
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13
        };
        _contentTextBox.SetResourceReference(TextBox.BackgroundProperty, "CardSurface");
        _contentTextBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");
        contentBorder.Child = _contentTextBox;
    }

    public void UpdateContent(string content) {
        _contentTextBox.Text = content ?? string.Empty;
        _contentTextBox.ScrollToHome();
    }
}
