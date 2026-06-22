using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SquadDash;

internal sealed class SquadCommandOutputWindow : Window {
    public SquadCommandOutputWindow(
        string title,
        string output,
        bool showDelegateButton = false) {
        Title = title;
        Width = 720;
        Height = 480;
        MinWidth = 520;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(16) };
        Content = root;

        var buttons = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        if (showDelegateButton) {
            var delegateButton = new Button {
                Content = "Delegate...",
                MinWidth = 96,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            delegateButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
            delegateButton.Click += (_, _) => {
                DelegateRequested = true;
                DialogResult = true;
            };
            buttons.Children.Add(delegateButton);
        }

        var closeButton = new Button {
            Content = "Close",
            MinWidth = 80,
            IsCancel = true
        };
        closeButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        closeButton.Click += (_, _) => Close();
        buttons.Children.Add(closeButton);

        var outputBox = new TextBox {
            Text = string.IsNullOrWhiteSpace(output) ? "(no output)" : output.TrimEnd(),
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12
        };
        outputBox.SetResourceReference(Control.BackgroundProperty, "PanelSurface");
        outputBox.SetResourceReference(Control.ForegroundProperty, "PrimaryText");
        root.Children.Add(outputBox);
    }

    public bool DelegateRequested { get; private set; }
}
