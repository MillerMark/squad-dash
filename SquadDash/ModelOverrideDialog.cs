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
    private readonly IReadOnlyList<ModelProfile> _profiles;
    private readonly string? _currentOverrideProfileId;
    private readonly string? _effectiveProfileAlias;
    private readonly string? _agentDisplayName;
    private readonly ImageSource? _agentImageSource;

    // "Not set" sentinel — null means clear the override
    private RadioButton _notSetRadio = null!;
    // One entry per profile, parallel to _orderedProfiles
    private IReadOnlyList<(RadioButton Radio, string ProfileId)> _profileRadios = [];

    public ModelOverrideDialog(ModelProfileStore profileStore, string agentHandle, string? effectiveProfileAlias = null,
        string? agentDisplayName = null, ImageSource? agentImageSource = null)
        : base(captionHeight: 36, resizeMode: ResizeMode.NoResize) {
        ArgumentNullException.ThrowIfNull(profileStore);
        if (string.IsNullOrWhiteSpace(agentHandle))
            throw new ArgumentException("Agent handle cannot be empty.", nameof(agentHandle));

        _profileStore = profileStore;
        _agentHandle = agentHandle.Trim();
        _effectiveProfileAlias = effectiveProfileAlias;
        _agentDisplayName = agentDisplayName;
        _agentImageSource = agentImageSource;

        Title = "AI model override";
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
            Text = "Agent Model Override",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        titleBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeLargePlus");
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        Grid.SetRow(titleBlock, 0);
        root.Children.Add(titleBlock);

        var identityPanel = new StackPanel {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 14)
        };
        Grid.SetRow(identityPanel, 1);
        root.Children.Add(identityPanel);

        if (_agentImageSource is not null) {
            var avatarBorder = new Border {
                Width = 58,
                Height = 58,
                CornerRadius = new CornerRadius(29),
                ClipToBounds = true
            };
            var img = new System.Windows.Controls.Image {
                Source = _agentImageSource,
                Stretch = Stretch.UniformToFill
            };
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.HighQuality);
            avatarBorder.Child = img;
            identityPanel.Children.Add(avatarBorder);
        } else {
            var displayName = _agentDisplayName ?? _agentHandle;
            var initial = displayName.Length > 0 ? displayName[0].ToString() : "?";
            var placeholderBorder = new Border {
                Width = 58,
                Height = 58,
                CornerRadius = new CornerRadius(29),
                ClipToBounds = true
            };
            placeholderBorder.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            var initialText = new TextBlock {
                Text = initial,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            initialText.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
            placeholderBorder.Child = initialText;
            identityPanel.Children.Add(placeholderBorder);
        }

        var nameStack = new StackPanel {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        identityPanel.Children.Add(nameStack);

        var nameBlock = new TextBlock {
            Text = _agentDisplayName ?? _agentHandle,
            FontWeight = FontWeights.SemiBold
        };
        nameBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeNormal");
        nameBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        nameStack.Children.Add(nameBlock);

        var profileRow = new StackPanel {
            Margin = new Thickness(0, 0, 0, 14)
        };
        Grid.SetRow(profileRow, 2);
        root.Children.Add(profileRow);

        _profiles = _profileStore.GetProfiles();
        _currentOverrideProfileId = FindCurrentOverrideProfileId();

        var subheading = new TextBlock {
            Text = "Future prompts to this agent will use:",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        subheading.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeNormal");
        subheading.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        profileRow.Children.Add(subheading);

        BuildProfileRadioButtons(profileRow);

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
            : $"No override (currently {_effectiveProfileAlias} from settings)";
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
            .Where(p => p.IsEnabled)
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
