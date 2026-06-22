using System.Windows;
using System.Windows.Controls;

namespace SquadDash;

internal sealed class SquadDelegateWindow : Window {
    private readonly TextBox _squadNameBox;
    private readonly TextBox _descriptionBox;
    private readonly Button _delegateButton;

    public SquadDelegateWindow(string? initialSquadName = null) {
        Title = "Delegate to Squad";
        Width = 560;
        Height = 360;
        MinWidth = 460;
        MinHeight = 300;
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

        _delegateButton = new Button {
            Content = "Delegate",
            MinWidth = 92,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            IsEnabled = false
        };
        _delegateButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        _delegateButton.Click += (_, _) => {
            DialogResult = true;
            Close();
        };
        buttons.Children.Add(_delegateButton);

        var cancelButton = new Button {
            Content = "Cancel",
            MinWidth = 80,
            IsCancel = true
        };
        cancelButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        buttons.Children.Add(cancelButton);

        var form = new Grid();
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(form);

        var squadLabel = CreateLabel("Squad name");
        Grid.SetRow(squadLabel, 0);
        form.Children.Add(squadLabel);

        _squadNameBox = new TextBox {
            Text = initialSquadName ?? string.Empty,
            Margin = new Thickness(0, 4, 0, 12)
        };
        _squadNameBox.SetResourceReference(Control.StyleProperty, "ThemedTextBoxStyle");
        _squadNameBox.TextChanged += (_, _) => SyncDelegateButton();
        Grid.SetRow(_squadNameBox, 1);
        form.Children.Add(_squadNameBox);

        var descriptionLabel = CreateLabel("Description");
        Grid.SetRow(descriptionLabel, 2);
        form.Children.Add(descriptionLabel);

        _descriptionBox = new TextBox {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 4, 0, 0)
        };
        _descriptionBox.SetResourceReference(Control.StyleProperty, "ThemedTextBoxStyle");
        _descriptionBox.TextChanged += (_, _) => SyncDelegateButton();
        Grid.SetRow(_descriptionBox, 3);
        form.Children.Add(_descriptionBox);

        Loaded += (_, _) => {
            if (string.IsNullOrWhiteSpace(_squadNameBox.Text))
                _squadNameBox.Focus();
            else
                _descriptionBox.Focus();
            SyncDelegateButton();
        };
    }

    public string SquadName => _squadNameBox.Text.Trim();
    public string Description => _descriptionBox.Text.Trim();

    private static TextBlock CreateLabel(string text) {
        var label = new TextBlock {
            Text = text,
            FontWeight = FontWeights.SemiBold
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        return label;
    }

    private void SyncDelegateButton() {
        _delegateButton.IsEnabled =
            !string.IsNullOrWhiteSpace(_squadNameBox.Text) &&
            !string.IsNullOrWhiteSpace(_descriptionBox.Text);
    }
}
