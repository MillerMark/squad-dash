using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SquadDash;

internal sealed class WorkspaceInstallDiagnosticsWindow : ChromedWindow {
    private readonly string _diagnosticsText;

    public WorkspaceInstallDiagnosticsWindow(string diagnosticsText)
        : base(captionHeight: CloseButtonHeight) {
        _diagnosticsText = string.IsNullOrWhiteSpace(diagnosticsText)
            ? "(no diagnostics available)"
            : diagnosticsText.TrimEnd();

        Title = "Squad Install Diagnostics";
        Width = 720;
        Height = 520;
        MinWidth = 520;
        MinHeight = 340;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var contentArea = ApplyOuterBorder("AppSurface", Title);

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentArea.Child = root;

        var textBorder = new Border {
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
        };
        textBorder.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        textBorder.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");

        var textBox = new TextBox {
            Text = _diagnosticsText,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Consolas")
        };
        textBox.SetResourceReference(Control.BackgroundProperty, "CardSurface");
        textBox.SetResourceReference(Control.ForegroundProperty, "LabelText");
        textBox.SetResourceReference(Control.FontSizeProperty, "FontSizeSmall");
        textBorder.Child = textBox;

        Grid.SetRow(textBorder, 0);
        root.Children.Add(textBorder);

        var buttons = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var copyButton = new Button {
            Content = "Copy",
            MinWidth = 84,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0)
        };
        copyButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        copyButton.Click += (_, _) => {
            Clipboard.SetText(_diagnosticsText);
            copyButton.Content = "Copied";
        };
        buttons.Children.Add(copyButton);

        var closeButton = new Button {
            Content = "Close",
            MinWidth = 84,
            Height = 30,
            IsCancel = true
        };
        closeButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        closeButton.Click += (_, _) => Close();
        buttons.Children.Add(closeButton);

        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);

        KeyDown += (_, e) => {
            if (e.Key == Key.Escape)
                Close();
        };
    }
}
