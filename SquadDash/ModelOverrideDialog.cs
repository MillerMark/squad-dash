using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SquadDash;

internal sealed class ModelOverrideDialog : ChromedWindow {
    private readonly ModelProfileStore _profileStore;
    private readonly string _agentHandle;
    private readonly IReadOnlyList<ModelProfile> _profiles;
    private readonly string? _currentOverrideProfileId;
    private readonly string? _effectiveProfileAlias;

    // "Not set" sentinel — null means clear the override
    private RadioButton _notSetRadio = null!;
    // One entry per profile, parallel to _orderedProfiles
    private IReadOnlyList<(RadioButton Radio, string ProfileId)> _profileRadios = [];

    public ModelOverrideDialog(ModelProfileStore profileStore, string agentHandle, string? effectiveProfileAlias = null)
        : base(captionHeight: 36, resizeMode: ResizeMode.NoResize) {
        ArgumentNullException.ThrowIfNull(profileStore);
        if (string.IsNullOrWhiteSpace(agentHandle))
            throw new ArgumentException("Agent handle cannot be empty.", nameof(agentHandle));

        _profileStore = profileStore;
        _agentHandle = agentHandle.Trim();
        _effectiveProfileAlias = effectiveProfileAlias;

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

        // Issue 1: use FontSizeSubtitle (one step below FontSizeTitle) and enable wrapping
        var titleBlock = new TextBlock {
            Text = "Choose a profile override for this agent",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        titleBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSubtitle");
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
        handleLabel.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeNormal");
        handleRow.Children.Add(handleLabel);

        var handleValue = new TextBlock {
            Text = _agentHandle,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };
        handleValue.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        handleValue.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeNormal");
        handleRow.Children.Add(handleValue);

        var profileRow = new StackPanel {
            Margin = new Thickness(0, 0, 0, 14)
        };
        Grid.SetRow(profileRow, 2);
        root.Children.Add(profileRow);

        var profileLabel = new TextBlock {
            Text = "Override profile",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        profileLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        profileLabel.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeNormal");
        profileRow.Children.Add(profileLabel);

        _profiles = _profileStore.GetProfiles();
        _currentOverrideProfileId = FindCurrentOverrideProfileId();

        // Issue 3: replace ComboBox with radio buttons; first option is "Not set"
        BuildProfileRadioButtons(profileRow);

        var hintText = new TextBlock {
            Text = "Choose a profile to use for future prompts from this agent.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        };
        hintText.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        hintText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeNormal");
        profileRow.Children.Add(hintText);

        var buttonRow = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        Grid.SetRow(buttonRow, 3);
        root.Children.Add(buttonRow);

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
    }

    private void BuildProfileRadioButtons(StackPanel container) {
        var notSetLabel = string.IsNullOrWhiteSpace(_effectiveProfileAlias)
            ? "No override"
            : $"No override (currently using {_effectiveProfileAlias} from the settings)";
        _notSetRadio = new RadioButton {
            Content = notSetLabel,
            GroupName = "ProfileOverride",
            IsChecked = string.IsNullOrWhiteSpace(_currentOverrideProfileId),
            Margin = new Thickness(0, 0, 0, 4)
        };
        _notSetRadio.SetResourceReference(RadioButton.ForegroundProperty, "LabelText");
        _notSetRadio.SetResourceReference(RadioButton.FontSizeProperty, "FontSizeNormal");
        container.Children.Add(_notSetRadio);

        var ordered = _profiles
            .Select(p => (ProfileId: p.Id, Alias: p.Alias))
            .OrderBy(p => p.Alias, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var radios = new List<(RadioButton, string)>(ordered.Length);
        foreach (var (profileId, alias) in ordered) {
            var rb = new RadioButton {
                Content = alias,
                GroupName = "ProfileOverride",
                IsChecked = string.Equals(profileId, _currentOverrideProfileId, StringComparison.OrdinalIgnoreCase),
                Margin = new Thickness(0, 0, 0, 4)
            };
            rb.SetResourceReference(RadioButton.ForegroundProperty, "LabelText");
            rb.SetResourceReference(RadioButton.FontSizeProperty, "FontSizeNormal");
            container.Children.Add(rb);
            radios.Add((rb, profileId));
        }

        _profileRadios = radios;
    }

    private string? FindCurrentOverrideProfileId() {
        var overrides = _profileStore.GetAgentOverrides();
        foreach (var entry in overrides) {
            if (string.Equals(entry.Key, _agentHandle, StringComparison.OrdinalIgnoreCase))
                return entry.Value;
        }
        return null;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) {
        if (_notSetRadio.IsChecked == true) {
            _profileStore.ClearAgentOverride(_agentHandle);
            DialogResult = true;
            Close();
            return;
        }

        foreach (var (radio, profileId) in _profileRadios) {
            if (radio.IsChecked == true) {
                _profileStore.SaveAgentOverride(_agentHandle, profileId);
                DialogResult = true;
                Close();
                return;
            }
        }
    }
}
