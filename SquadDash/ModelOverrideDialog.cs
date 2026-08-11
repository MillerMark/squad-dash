using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SquadDash;

internal sealed class ModelOverrideDialog : ChromedWindow {
    private readonly ModelProfileStore _profileStore;
    private readonly string _agentHandle;
    private readonly ComboBox _profileComboBox;
    private readonly Button _clearButton;
    private readonly IReadOnlyList<ModelProfile> _profiles;
    private readonly string? _currentOverrideProfileId;

    public ModelOverrideDialog(ModelProfileStore profileStore, string agentHandle)
        : base(captionHeight: 36, resizeMode: ResizeMode.NoResize) {
        ArgumentNullException.ThrowIfNull(profileStore);
        if (string.IsNullOrWhiteSpace(agentHandle))
            throw new ArgumentException("Agent handle cannot be empty.", nameof(agentHandle));

        _profileStore = profileStore;
        _agentHandle = agentHandle.Trim();

        Title = "Model override";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        MinWidth = 400;
        MaxWidth = 560;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid {
            Margin = new Thickness(18)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var outerBorder = ApplyOuterBorder();
        outerBorder.Child = root;

        var titleBlock = new TextBlock {
            Text = "Choose a profile override for this agent",
            FontSize = (double)Application.Current.Resources["FontSizeTitle"],
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        root.Children.Add(titleBlock);

        var handleRow = new StackPanel {
            Margin = new Thickness(0, 0, 0, 14)
        };
        Grid.SetRow(handleRow, 1);
        root.Children.Add(handleRow);

        var handleLabel = new TextBlock {
            Text = "Agent handle",
            FontWeight = FontWeights.SemiBold
        };
        handleLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        handleRow.Children.Add(handleLabel);

        var handleValue = new TextBlock {
            Text = _agentHandle,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };
        handleValue.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        handleRow.Children.Add(handleValue);

        var profileRow = new StackPanel {
            Margin = new Thickness(0, 0, 0, 14)
        };
        Grid.SetRow(profileRow, 2);
        root.Children.Add(profileRow);

        var profileLabel = new TextBlock {
            Text = "Override profile",
            FontWeight = FontWeights.SemiBold
        };
        profileLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        profileRow.Children.Add(profileLabel);

        _profiles = _profileStore.GetProfiles();
        _currentOverrideProfileId = FindCurrentOverrideProfileId();

        _profileComboBox = new ComboBox {
            Margin = new Thickness(0, 6, 0, 0),
            MinWidth = 320,
            DisplayMemberPath = nameof(ProfileSelectionItem.DisplayText),
            SelectedValuePath = nameof(ProfileSelectionItem.ProfileId),
            ItemsSource = BuildSelectionItems()
        };
        _profileComboBox.SelectionChanged += ProfileComboBox_SelectionChanged;
        _profileComboBox.SetResourceReference(Control.StyleProperty, "ThemedComboBoxStyle");
        profileRow.Children.Add(_profileComboBox);

        var hintText = new TextBlock {
            Text = "Choose a profile to use for future prompts from this agent.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        };
        hintText.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        profileRow.Children.Add(hintText);

        var buttonRow = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        Grid.SetRow(buttonRow, 3);
        root.Children.Add(buttonRow);

        _clearButton = new Button {
            Content = "Clear override",
            Width = 120,
            Height = 32,
            Margin = new Thickness(0, 0, 10, 0),
            IsEnabled = !string.IsNullOrWhiteSpace(_currentOverrideProfileId)
        };
        _clearButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        _clearButton.Click += ClearButton_Click;
        buttonRow.Children.Add(_clearButton);

        var cancelButton = new Button {
            Content = "Cancel",
            Width = 96,
            Height = 32,
            Margin = new Thickness(0, 0, 10, 0),
            IsCancel = true
        };
        cancelButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        buttonRow.Children.Add(cancelButton);

        var okButton = new Button {
            Content = "OK",
            Width = 96,
            Height = 32,
            IsDefault = true
        };
        okButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        okButton.Click += OkButton_Click;
        buttonRow.Children.Add(okButton);

        PreviewKeyDown += (_, e) => {
            if (e.Key != System.Windows.Input.Key.Escape)
                return;

            DialogResult = false;
            Close();
        };

        if (!string.IsNullOrWhiteSpace(_currentOverrideProfileId)) {
            _profileComboBox.SelectedValue = _currentOverrideProfileId;
        }
        else if (_profiles.Count > 0) {
            _profileComboBox.SelectedIndex = 0;
        }
    }

    private IReadOnlyList<ProfileSelectionItem> BuildSelectionItems() {
        return _profiles
            .Select(profile => new ProfileSelectionItem(profile.Id, profile.Alias))
            .OrderBy(item => item.DisplayText, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string? FindCurrentOverrideProfileId() {
        var overrides = _profileStore.GetAgentOverrides();
        foreach (var entry in overrides) {
            if (string.Equals(entry.Key, _agentHandle, StringComparison.OrdinalIgnoreCase))
                return entry.Value;
        }
        return null;
    }

    private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        _clearButton.IsEnabled = _profileComboBox.SelectedItem is not null;
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e) {
        _profileStore.ClearAgentOverride(_agentHandle);
        DialogResult = true;
        Close();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) {
        if (_profileComboBox.SelectedValue is not string profileId || string.IsNullOrWhiteSpace(profileId))
            return;

        _profileStore.SaveAgentOverride(_agentHandle, profileId);
        DialogResult = true;
        Close();
    }

    private sealed record ProfileSelectionItem(string ProfileId, string DisplayText);
}
