using System.Collections.Generic;
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
    private readonly PushNotificationService _pushNotificationService;
    private readonly CheckBox _notificationsEnabledCheckBox;
    private readonly TextBox _notificationTopicBox;
    private Image _qrCodeImage = null!;
    private TextBlock _ntfyUrlText = null!;
    private readonly CheckBox _notifyAiTurnCheckBox;
    private readonly CheckBox _notifyGitCommitCheckBox;
    private readonly CheckBox _notifyLoopIterationCheckBox;
    private readonly CheckBox _notifyLoopStoppedCheckBox;
    private readonly CheckBox _notifyRcEstablishedCheckBox;
    private readonly CheckBox _notifyRcDroppedCheckBox;
    private readonly ComboBox _tunnelModeComboBox;
    private readonly PasswordBox _tunnelTokenPasswordBox;
    private readonly TextBox _tunnelTokenRevealBox;

    private PreferencesWindow(
        ApplicationSettingsStore settingsStore,
        ApplicationSettingsSnapshot currentSettings,
        PushNotificationService pushNotificationService,
        Action<ApplicationSettingsSnapshot> onSaved,
        bool showDevOptions = false) {
        _settingsStore = settingsStore;
        _pushNotificationService = pushNotificationService;
        _onSaved = onSaved;

        Title = "Preferences";
        Width = 500;
        Height = 700;
        MinWidth = 420;
        MinHeight = 560;
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
        
        var testButton = new Button {
            Content = "Test Notification",
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0)
        };
        testButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        DockPanel.SetDock(testButton, Dock.Right);
        
        buttonRow.Children.Add(testButton);
        buttonRow.Children.Add(saveButton);
        testButton.Click += TestButton_Click;
        saveButton.Click += SaveButton_Click;

        _statusText = new TextBlock {
            VerticalAlignment = VerticalAlignment.Center
        };
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        buttonRow.Children.Add(_statusText);

        // Form fields
        var form = new StackPanel();
        var scrollViewer = new ScrollViewer {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = form
        };
        root.Children.Add(scrollViewer);

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

        // ── Tunnel Section ────────────────────────────────────────────────
        form.Children.Add(new Separator { Margin = new Thickness(0, 22, 0, 18) });

        var tunnelSectionLabel = new TextBlock {
            Text = "Remote Access Tunnel",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 6)
        };
        tunnelSectionLabel.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        form.Children.Add(tunnelSectionLabel);

        var tunnelHint = new TextBlock {
            Text = "Optionally auto-start a public tunnel when Remote Access starts, for access from outside your local network.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 12)
        };
        tunnelHint.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        form.Children.Add(tunnelHint);

        var tunnelModeLabel = new TextBlock {
            Text = "Tunnel Provider:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        };
        tunnelModeLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        form.Children.Add(tunnelModeLabel);

        _tunnelModeComboBox = new ComboBox { Height = 30, Margin = new Thickness(0, 0, 0, 12) };
        _tunnelModeComboBox.Items.Add(new ComboBoxItem { Content = "None", Tag = (string?)null });
        _tunnelModeComboBox.Items.Add(new ComboBoxItem { Content = "ngrok", Tag = "ngrok" });
        _tunnelModeComboBox.Items.Add(new ComboBoxItem { Content = "Cloudflare", Tag = "cloudflare" });
        // Select current mode
        var savedTunnelMode = currentSettings.TunnelMode;
        foreach (ComboBoxItem item in _tunnelModeComboBox.Items)
            if (string.Equals(item.Tag as string, savedTunnelMode, StringComparison.OrdinalIgnoreCase))
                item.IsSelected = true;
        if (_tunnelModeComboBox.SelectedItem is null)
            ((ComboBoxItem)_tunnelModeComboBox.Items[0]).IsSelected = true;
        form.Children.Add(_tunnelModeComboBox);

        var tunnelTokenLabel = new TextBlock {
            Text = "Tunnel Auth Token (optional — leave blank if tunnel binary is pre-configured)",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 5)
        };
        tunnelTokenLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        form.Children.Add(tunnelTokenLabel);

        var currentTunnelToken = currentSettings.TunnelToken ?? string.Empty;
        var tunnelTokenHost = new Grid();
        _tunnelTokenPasswordBox = new PasswordBox {
            Password = currentTunnelToken,
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30
        };
        _tunnelTokenRevealBox = new TextBox {
            Text = currentTunnelToken,
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30,
            Visibility = Visibility.Collapsed
        };
        tunnelTokenHost.Children.Add(_tunnelTokenPasswordBox);
        tunnelTokenHost.Children.Add(_tunnelTokenRevealBox);
        form.Children.Add(tunnelTokenHost);

        var revealTunnelLink = new TextBlock {
            Margin = new Thickness(0, 6, 0, 0),
            Cursor = Cursors.Hand
        };
        var revealTunnelRun = new System.Windows.Documents.Run("(reveal token)");
        revealTunnelRun.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "ActionLinkText");
        revealTunnelLink.Inlines.Add(revealTunnelRun);
        revealTunnelLink.MouseLeftButtonDown += (_, _) => {
            _tunnelTokenRevealBox.Text = _tunnelTokenPasswordBox.Password;
            _tunnelTokenPasswordBox.Visibility = Visibility.Collapsed;
            _tunnelTokenRevealBox.Visibility = Visibility.Visible;
        };
        revealTunnelLink.MouseLeftButtonUp += (_, _) => {
            _tunnelTokenPasswordBox.Password = _tunnelTokenRevealBox.Text;
            _tunnelTokenRevealBox.Visibility = Visibility.Collapsed;
            _tunnelTokenPasswordBox.Visibility = Visibility.Visible;
        };
        form.Children.Add(revealTunnelLink);

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

        // ── Notifications Section ──────────────────────────────────────────
        form.Children.Add(new Separator { Margin = new Thickness(0, 22, 0, 18) });

        var notifSectionLabel = new TextBlock {
            Text = "Notifications",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 12)
        };
        notifSectionLabel.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        form.Children.Add(notifSectionLabel);

        _notificationsEnabledCheckBox = new CheckBox {
            Content = "Enable Phone Notifications",
            IsChecked = !string.IsNullOrWhiteSpace(currentSettings.NotificationProvider),
            Margin = new Thickness(0, 0, 0, 16)
        };
        _notificationsEnabledCheckBox.SetResourceReference(ForegroundProperty, "BodyText");
        form.Children.Add(_notificationsEnabledCheckBox);

        var deliveryRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };

        var deliveryLabel = new TextBlock {
            Text = "Delivery Method:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        deliveryLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        deliveryRow.Children.Add(deliveryLabel);

        var deliveryCombo = new ComboBox { Width = 140, Height = 28 };
        deliveryCombo.Items.Add(new ComboBoxItem { Content = "ntfy.sh", IsSelected = true });
        deliveryRow.Children.Add(deliveryCombo);

        form.Children.Add(deliveryRow);

        var ntfyBorder = new Border {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14, 12, 14, 14),
            Margin = new Thickness(0, 0, 0, 16)
        };
        ntfyBorder.SetResourceReference(Border.BorderBrushProperty, "SubtleBorder");
        ntfyBorder.SetResourceReference(Border.BackgroundProperty, "InputSurface");

        var ntfyStack = new StackPanel();
        ntfyBorder.Child = ntfyStack;
        form.Children.Add(ntfyBorder);

        var topicLabel = new TextBlock {
            Text = "Topic:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        topicLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        ntfyStack.Children.Add(topicLabel);

        _notificationTopicBox = new TextBox {
            Text = (currentSettings.NotificationEndpoint != null && currentSettings.NotificationEndpoint.TryGetValue("topic", out var ntfyTopic_) ? ntfyTopic_ : null) ?? GenerateDefaultTopic(currentSettings),
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30,
            Margin = new Thickness(0, 0, 0, 6)
        };
        ntfyStack.Children.Add(_notificationTopicBox);

        var generateTopicButton = new Button {
            Content = "Generate Random Topic",
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12)
        };
        generateTopicButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        generateTopicButton.Click += (_, _) => {
            _notificationTopicBox.Text = GenerateRandomTopic(currentSettings);
            UpdateQrCode();
        };
        ntfyStack.Children.Add(generateTopicButton);

        // Wire topic box text changes to QR update
        _notificationTopicBox.TextChanged += (_, _) => UpdateQrCode();

        _qrCodeImage = new Image {
            Width = 120,
            Height = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6),
            Stretch = System.Windows.Media.Stretch.Uniform
        };
        ntfyStack.Children.Add(_qrCodeImage);

        var scanHint = new TextBlock {
            Text = "Scan with ntfy phone app",
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 2)
        };
        scanHint.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        ntfyStack.Children.Add(scanHint);

        _ntfyUrlText = new TextBlock {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            FontFamily = new System.Windows.Media.FontFamily("Consolas")
        };
        _ntfyUrlText.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        ntfyStack.Children.Add(_ntfyUrlText);

        var notifyWhenLabel = new TextBlock {
            Text = "Notify me when:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        notifyWhenLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        form.Children.Add(notifyWhenLabel);

        _notifyAiTurnCheckBox = AddNotifyCheckBox(form, "AI turn completes", GetToggle(currentSettings, "assistant_turn_complete", true));
        _notifyGitCommitCheckBox = AddNotifyCheckBox(form, "Git commit pushed (agent-authored only)", GetToggle(currentSettings, "git_commit_pushed", false));
        _notifyLoopIterationCheckBox = AddNotifyCheckBox(form, "Loop iteration completes", GetToggle(currentSettings, "loop_iteration_complete", false));
        _notifyLoopStoppedCheckBox = AddNotifyCheckBox(form, "Loop stopped", GetToggle(currentSettings, "loop_stopped", true));
        _notifyRcEstablishedCheckBox = AddNotifyCheckBox(form, "Remote connection established", GetToggle(currentSettings, "rc_connection_established", false));
        _notifyRcDroppedCheckBox = AddNotifyCheckBox(form, "Remote connection dropped", GetToggle(currentSettings, "rc_connection_dropped", true));

        // Initialize QR code display
        UpdateQrCode();
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
        var notifEnabled = _notificationsEnabledCheckBox.IsChecked == true;
        var notifTopic = _notificationTopicBox.Text.Trim();
        updated = _settingsStore.SaveNotificationSettings(
            notifEnabled ? "ntfy" : null,
            notifEnabled && !string.IsNullOrWhiteSpace(notifTopic)
                ? new System.Collections.Generic.Dictionary<string, string> { ["topic"] = notifTopic }
                : null,
            new System.Collections.Generic.Dictionary<string, bool> {
                ["assistant_turn_complete"]   = _notifyAiTurnCheckBox.IsChecked == true,
                ["git_commit_pushed"]         = _notifyGitCommitCheckBox.IsChecked == true,
                ["loop_iteration_complete"]   = _notifyLoopIterationCheckBox.IsChecked == true,
                ["loop_stopped"]             = _notifyLoopStoppedCheckBox.IsChecked == true,
                ["rc_connection_established"] = _notifyRcEstablishedCheckBox.IsChecked == true,
                ["rc_connection_dropped"]     = _notifyRcDroppedCheckBox.IsChecked == true,
            });
        _pushNotificationService.ReloadProvider();
        var tunnelMode = (_tunnelModeComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        var tunnelToken = _tunnelTokenRevealBox.IsVisible ? _tunnelTokenRevealBox.Text : _tunnelTokenPasswordBox.Password;
        updated = _settingsStore.SaveTunnelSettings(tunnelMode, string.IsNullOrWhiteSpace(tunnelToken) ? null : tunnelToken);
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
        PushNotificationService pushNotificationService,
        bool showDevOptions,
        Action<ApplicationSettingsSnapshot> onSaved) {
        var window = new PreferencesWindow(settingsStore, currentSettings, pushNotificationService, onSaved, showDevOptions);
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

    private static CheckBox AddNotifyCheckBox(StackPanel parent, string label, bool isChecked) {
        var cb = new CheckBox {
            Content = label,
            IsChecked = isChecked,
            Margin = new Thickness(0, 0, 0, 6)
        };
        cb.SetResourceReference(ForegroundProperty, "BodyText");
        parent.Children.Add(cb);
        return cb;
    }

    private static string GenerateDefaultTopic(ApplicationSettingsSnapshot settings) {
        if (settings.NotificationEndpoint != null && settings.NotificationEndpoint.TryGetValue("topic", out var _nt_) && !string.IsNullOrWhiteSpace(_nt_))
            return _nt_!;
        return GenerateRandomTopic(settings);
    }

    private static string GenerateRandomTopic(ApplicationSettingsSnapshot settings) {
        var userName = (settings.UserName ?? Environment.UserName ?? "user")
            .ToLowerInvariant()
            .Replace(" ", "")
            .Replace("-", "");
        if (userName.Length > 8) userName = userName[..8];
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return $"squad-dash-{userName}-{suffix}";
    }

    private void UpdateQrCode() {
        var topic = _notificationTopicBox.Text.Trim();
        var url = $"https://ntfy.sh/{topic}";
        _ntfyUrlText.Text = url;

        if (string.IsNullOrWhiteSpace(topic)) {
            _qrCodeImage.Source = null;
            return;
        }

        try {
            var qrGenerator = new QRCoder.QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(url, QRCoder.QRCodeGenerator.ECCLevel.Q);
            var qrCode = new QRCoder.BitmapByteQRCode(qrData);
            var bitmapBytes = qrCode.GetGraphic(4);

            using var ms = new System.IO.MemoryStream(bitmapBytes);
            var bitmapImage = new System.Windows.Media.Imaging.BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = ms;
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            _qrCodeImage.Source = bitmapImage;
        }
        catch {
            _qrCodeImage.Source = null;
        }
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e) {
        var topic = _notificationTopicBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(topic)) {
            _statusText.Text = "Enter a topic first.";
            return;
        }
        _statusText.Text = "Sending test...";
        // Temporarily build a settings snapshot with current UI state for the test
        var tempProvider = new NtfyNotificationProvider(topic);
        await tempProvider.SendAsync("SquadDash Test", "Notifications are working!");
        _statusText.Text = "Test sent!";
    }

    private static bool GetToggle(ApplicationSettingsSnapshot s, string key, bool defaultValue) {
        if (s.NotificationEventToggles is null) return defaultValue;
        return s.NotificationEventToggles.TryGetValue(key, out var v) ? v : defaultValue;
    }
}
