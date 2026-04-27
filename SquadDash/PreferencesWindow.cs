using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

namespace SquadDash;

internal sealed class PreferencesWindow : Window {
    private readonly ApplicationSettingsStore _settingsStore;
    private readonly Action<ApplicationSettingsSnapshot> _onSaved;
    private readonly TextBox _userNameBox;
    private readonly PasswordBox _apiKeyPasswordBox;
    private readonly TextBox _apiKeyRevealBox;
    private readonly TextBox _speechRegionBox;
    private readonly ComboBox? _startupIssueSimulationComboBox;
    private readonly ComboBox? _runtimeIssueSimulationComboBox;
    private readonly TextBlock _statusText;

    private PreferencesWindow(
        ApplicationSettingsStore settingsStore,
        ApplicationSettingsSnapshot currentSettings,
        Action<ApplicationSettingsSnapshot> onSaved,
        bool showDevOptions = false) {
        _settingsStore = settingsStore;
        _onSaved = onSaved;

        Title = "Preferences";
        Width = 460;
        Height = 560;
        MinWidth = 380;
        MinHeight = 500;
        ResizeMode = ResizeMode.CanResize;
        this.SetResourceReference(BackgroundProperty, "AppSurface");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        KeyDown += (_, e) => {
            if (e.Key == Key.Enter)
                SaveButton_Click(this, new RoutedEventArgs());
        };

        var root = new DockPanel { Margin = new Thickness(24, 24, 24, 16) };
        Content = root;

        // Bottom: status + Save button
        var buttonRow = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        root.Children.Add(buttonRow);

        var saveButton = new Button {
            Content = "Save",
            Width = 88,
            Height = 30
        };
        saveButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        DockPanel.SetDock(saveButton, Dock.Right);
        buttonRow.Children.Add(saveButton);
        saveButton.Click += SaveButton_Click;

        _statusText = new TextBlock {
            VerticalAlignment = VerticalAlignment.Center
        };
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        buttonRow.Children.Add(_statusText);

        // Form fields
        var form = new StackPanel();
        root.Children.Add(form);

        // User Name
        var userNameLabel = new TextBlock {
            Text = "User Name (appears in the Transcript, before user prompts)",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        };
        userNameLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        form.Children.Add(userNameLabel);

        _userNameBox = new TextBox {
            Text = string.IsNullOrWhiteSpace(currentSettings.UserName) ? "User" : currentSettings.UserName,
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30,
            Margin = new Thickness(0, 0, 0, 20)
        };
        form.Children.Add(_userNameBox);

        // Azure Speech API Key
        var apiKeyLabel = new TextBlock {
            Text = "Azure Speech API Key",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        };
        apiKeyLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        form.Children.Add(apiKeyLabel);

        var currentApiKey = Environment.GetEnvironmentVariable("SQUAD_SPEECH_KEY", EnvironmentVariableTarget.User) ?? string.Empty;

        var apiKeyHost = new Grid();
        _apiKeyPasswordBox = new PasswordBox {
            Password = currentApiKey,
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30
        };
        _apiKeyRevealBox = new TextBox {
            Text = currentApiKey,
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30,
            Visibility = Visibility.Collapsed
        };
        apiKeyHost.Children.Add(_apiKeyPasswordBox);
        apiKeyHost.Children.Add(_apiKeyRevealBox);
        form.Children.Add(apiKeyHost);

        // Reveal link
        var revealLink = new TextBlock {
            Margin = new Thickness(0, 6, 0, 0),
            Cursor = Cursors.Hand
        };
        var revealRun = new System.Windows.Documents.Run("(reveal key)");
        revealRun.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "ActionLinkText");
        revealLink.Inlines.Add(revealRun);
        revealLink.MouseLeftButtonDown += RevealLink_MouseDown;
        revealLink.MouseLeftButtonUp += RevealLink_MouseUp;
        form.Children.Add(revealLink);

        // Azure Speech Region
        var speechRegionLabel = new TextBlock {
            Text = "Azure Speech Region",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 20, 0, 5)
        };
        speechRegionLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        form.Children.Add(speechRegionLabel);

        _speechRegionBox = new TextBox {
            Text = currentSettings.SpeechRegion ?? string.Empty,
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30
        };
        form.Children.Add(_speechRegionBox);

        var regionHint = new TextBlock {
            Text = "e.g. eastus, westus2, westeurope",
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0)
        };
        regionHint.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        form.Children.Add(regionHint);

        if (showDevOptions)
        {
        form.Children.Add(new Separator {
            Margin = new Thickness(0, 22, 0, 18)
        });

        var devSimLabel = new TextBlock {
            Text = "Developer Issue Simulation",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 5)
        };
        devSimLabel.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        form.Children.Add(devSimLabel);

        var devSimHint = new TextBlock {
            Text = "Use this only for UI testing. Startup simulations affect the top issue panel. Runtime simulations make the next prompt fail through the friendly error path.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 12)
        };
        devSimHint.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        form.Children.Add(devSimHint);

        var startupSimLabel = new TextBlock {
            Text = "Startup Issue Preview",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        };
        startupSimLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        form.Children.Add(startupSimLabel);

        _startupIssueSimulationComboBox = new ComboBox {
            Height = 30,
            Margin = new Thickness(0, 0, 0, 14)
        };
        AddSimulationOption(_startupIssueSimulationComboBox, "None", DeveloperStartupIssueSimulation.None);
        AddSimulationOption(_startupIssueSimulationComboBox, "Missing Node.js tooling", DeveloperStartupIssueSimulation.MissingNodeTooling);
        AddSimulationOption(_startupIssueSimulationComboBox, "Squad not installed", DeveloperStartupIssueSimulation.SquadNotInstalled);
        AddSimulationOption(_startupIssueSimulationComboBox, "Partial Squad install", DeveloperStartupIssueSimulation.PartialSquadInstall);
        SelectSimulationOption(_startupIssueSimulationComboBox, currentSettings.StartupIssueSimulation);
        form.Children.Add(_startupIssueSimulationComboBox);

        var runtimeSimLabel = new TextBlock {
            Text = "Runtime Failure Simulation",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        };
        runtimeSimLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        form.Children.Add(runtimeSimLabel);

        _runtimeIssueSimulationComboBox = new ComboBox {
            Height = 30
        };
        AddSimulationOption(_runtimeIssueSimulationComboBox, "None", DeveloperRuntimeIssueSimulation.None);
        AddSimulationOption(_runtimeIssueSimulationComboBox, "Copilot auth required", DeveloperRuntimeIssueSimulation.CopilotAuthRequired);
        AddSimulationOption(_runtimeIssueSimulationComboBox, "Bundled SDK repair", DeveloperRuntimeIssueSimulation.BundledSdkRepair);
        AddSimulationOption(_runtimeIssueSimulationComboBox, "Build temp files", DeveloperRuntimeIssueSimulation.BuildTempFiles);
        AddSimulationOption(_runtimeIssueSimulationComboBox, "Generic runtime failure", DeveloperRuntimeIssueSimulation.GenericRuntimeFailure);
        SelectSimulationOption(_runtimeIssueSimulationComboBox, currentSettings.RuntimeIssueSimulation);
        form.Children.Add(_runtimeIssueSimulationComboBox);
        } // end showDevOptions
    }

    private void RevealLink_MouseDown(object sender, MouseButtonEventArgs e) {
        _apiKeyRevealBox.Text = _apiKeyPasswordBox.Password;
        _apiKeyPasswordBox.Visibility = Visibility.Collapsed;
        _apiKeyRevealBox.Visibility = Visibility.Visible;
    }

    private void RevealLink_MouseUp(object sender, MouseButtonEventArgs e) {
        _apiKeyPasswordBox.Password = _apiKeyRevealBox.Text;
        _apiKeyRevealBox.Visibility = Visibility.Collapsed;
        _apiKeyPasswordBox.Visibility = Visibility.Visible;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e) {
        var userName = _userNameBox.Text.Trim();
        var apiKey = _apiKeyRevealBox.IsVisible ? _apiKeyRevealBox.Text : _apiKeyPasswordBox.Password;
        var speechRegion = _speechRegionBox.Text.Trim();
        var startupIssueSimulation = (_startupIssueSimulationComboBox?.SelectedItem as ComboBoxItem)?.Tag is DeveloperStartupIssueSimulation startupValue
            ? startupValue
            : DeveloperStartupIssueSimulation.None;
        var runtimeIssueSimulation = (_runtimeIssueSimulationComboBox?.SelectedItem as ComboBoxItem)?.Tag is DeveloperRuntimeIssueSimulation runtimeValue
            ? runtimeValue
            : DeveloperRuntimeIssueSimulation.None;

        var updated = _settingsStore.SaveUserName(string.IsNullOrWhiteSpace(userName) ? null : userName);
        updated = _settingsStore.SaveSpeechRegion(string.IsNullOrWhiteSpace(speechRegion) ? null : speechRegion);
        updated = _settingsStore.SaveDeveloperIssueSimulation(startupIssueSimulation, runtimeIssueSimulation);
        _onSaved(updated);
        Close();

        // SetEnvironmentVariable(EnvironmentVariableTarget.User) broadcasts WM_SETTINGCHANGE to all
        // top-level windows synchronously, which can block the UI thread for 10+ seconds.
        await Task.Run(() =>
            Environment.SetEnvironmentVariable("SQUAD_SPEECH_KEY", apiKey, EnvironmentVariableTarget.User));
    }

    public static PreferencesWindow Open(
        Window? owner,
        ApplicationSettingsStore settingsStore,
        ApplicationSettingsSnapshot currentSettings,
        bool showDevOptions,
        Action<ApplicationSettingsSnapshot> onSaved) {
        var window = new PreferencesWindow(settingsStore, currentSettings, onSaved, showDevOptions);
        if (owner != null)
            window.Owner = owner;
        window.Show();
        return window;
    }

    private static void AddSimulationOption(ComboBox comboBox, string label, object value) {
        comboBox.Items.Add(new ComboBoxItem {
            Content = label,
            Tag = value
        });
    }

    private static void SelectSimulationOption(ComboBox comboBox, object value) {
        foreach (var item in comboBox.Items) {
            if (item is ComboBoxItem { Tag: not null } comboBoxItem &&
                Equals(comboBoxItem.Tag, value)) {
                comboBox.SelectedItem = comboBoxItem;
                return;
            }
        }

        if (comboBox.Items.Count > 0)
            comboBox.SelectedIndex = 0;
    }
}
