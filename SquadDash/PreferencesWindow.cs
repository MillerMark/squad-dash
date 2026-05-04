using System.Collections.Generic;
using System;
using System.Net.Http;
using System.Threading.Tasks;
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
    private readonly Image _qrCodeImage;
    private readonly TextBlock _ntfyUrlText;
    private readonly CheckBox _notifyAiTurnCheckBox;
    private readonly CheckBox _notifyGitCommitCheckBox;
    private readonly CheckBox _notifyLoopIterationCheckBox;
    private readonly CheckBox _notifyLoopStoppedCheckBox;
    private readonly CheckBox _notifyRcEstablishedCheckBox;
    private readonly CheckBox _notifyRcDroppedCheckBox;
    private readonly ComboBox _tunnelModeComboBox;
    private readonly PasswordBox _tunnelTokenPasswordBox;
    private readonly TextBox _tunnelTokenRevealBox;
    private readonly TextBox _byokProviderUrlBox;
    private readonly TextBox _byokModelBox;
    private readonly ComboBox _byokProviderTypeComboBox;
    private readonly PasswordBox _byokApiKeyPasswordBox;
    private readonly TextBox _byokApiKeyRevealBox;
    private readonly TextBlock _byokTestStatusText;

    private readonly UIElement[] _pages;
    private readonly Button[] _navButtons;
    private int _currentPage;
    private readonly ContentControl _pageHost;

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
        Width = 640;
        Height = 720;
        MinWidth = 540;
        MinHeight = 560;
        ResizeMode = ResizeMode.CanResize;
        this.SetResourceReference(BackgroundProperty, "AppSurface");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        KeyDown += (_, e) => {
            if (e.Key == Key.Enter)
                SaveButton_Click(this, new RoutedEventArgs());
        };

        // ── Initialize all field controls ─────────────────────────────────

        _userNameBox = new TextBox {
            Text = string.IsNullOrWhiteSpace(currentSettings.UserName) ? "User" : currentSettings.UserName,
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30,
            Margin = new Thickness(0, 0, 0, 20)
        };

        var currentApiKey = Environment.GetEnvironmentVariable("SQUAD_SPEECH_KEY", EnvironmentVariableTarget.User) ?? string.Empty;
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

        _speechRegionBox = new TextBox {
            Text = currentSettings.SpeechRegion ?? string.Empty,
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30
        };

        _tunnelModeComboBox = new ComboBox { Height = 30, Margin = new Thickness(0, 0, 0, 12) };
        _tunnelModeComboBox.Items.Add(new ComboBoxItem { Content = "None", Tag = (string?)null });
        _tunnelModeComboBox.Items.Add(new ComboBoxItem { Content = "ngrok", Tag = "ngrok" });
        _tunnelModeComboBox.Items.Add(new ComboBoxItem { Content = "Cloudflare", Tag = "cloudflare" });
        var savedTunnelMode = currentSettings.TunnelMode;
        foreach (ComboBoxItem item in _tunnelModeComboBox.Items)
            if (string.Equals(item.Tag as string, savedTunnelMode, StringComparison.OrdinalIgnoreCase))
                item.IsSelected = true;
        if (_tunnelModeComboBox.SelectedItem is null)
            ((ComboBoxItem)_tunnelModeComboBox.Items[0]).IsSelected = true;

        var currentTunnelToken = currentSettings.TunnelToken ?? string.Empty;
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

        _byokProviderUrlBox = new TextBox {
            Text = currentSettings.ByokProviderUrl ?? "",
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30
        };
        _byokModelBox = new TextBox {
            Text = currentSettings.ByokModel ?? "",
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30,
            Margin = new Thickness(0, 0, 0, 12)
        };

        _byokProviderTypeComboBox = new ComboBox { Height = 30, Margin = new Thickness(0, 0, 0, 12) };
        _byokProviderTypeComboBox.Items.Add(new ComboBoxItem { Content = "OpenAI / Ollama (default)", Tag = "openai" });
        _byokProviderTypeComboBox.Items.Add(new ComboBoxItem { Content = "Azure", Tag = "azure" });
        _byokProviderTypeComboBox.Items.Add(new ComboBoxItem { Content = "Anthropic", Tag = "anthropic" });
        var savedByokType = currentSettings.ByokProviderType;
        var byokTypeSelected = false;
        foreach (ComboBoxItem item in _byokProviderTypeComboBox.Items) {
            if (string.Equals(item.Tag as string, savedByokType, StringComparison.OrdinalIgnoreCase)) {
                item.IsSelected = true;
                byokTypeSelected = true;
                break;
            }
        }
        if (!byokTypeSelected)
            ((ComboBoxItem)_byokProviderTypeComboBox.Items[0]).IsSelected = true;

        var currentByokApiKey = currentSettings.ByokApiKey ?? string.Empty;
        _byokApiKeyPasswordBox = new PasswordBox {
            Password = currentByokApiKey,
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30
        };
        _byokApiKeyRevealBox = new TextBox {
            Text = currentByokApiKey,
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30,
            Visibility = Visibility.Collapsed
        };
        _byokTestStatusText = new TextBlock {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            FontSize = 11
        };
        _byokTestStatusText.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");

        _notificationsEnabledCheckBox = new CheckBox {
            Content = "Enable Phone Notifications",
            IsChecked = !string.IsNullOrWhiteSpace(currentSettings.NotificationProvider),
            Margin = new Thickness(0, 0, 0, 16)
        };
        _notificationsEnabledCheckBox.SetResourceReference(ForegroundProperty, "BodyText");

        _notificationTopicBox = new TextBox {
            Text = (currentSettings.NotificationEndpoint != null && currentSettings.NotificationEndpoint.TryGetValue("topic", out var ntfyTopic_) ? ntfyTopic_ : null) ?? GenerateDefaultTopic(currentSettings),
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30,
            Margin = new Thickness(0, 0, 0, 6)
        };
        _notificationTopicBox.TextChanged += (_, _) => UpdateQrCode();

        _qrCodeImage = new Image {
            Width = 120,
            Height = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6),
            Stretch = System.Windows.Media.Stretch.Uniform
        };
        _ntfyUrlText = new TextBlock {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            FontFamily = new System.Windows.Media.FontFamily("Consolas")
        };
        _ntfyUrlText.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");

        _notifyAiTurnCheckBox        = MakeCheckBox("AI turn completes",                           GetToggle(currentSettings, "assistant_turn_complete", true));
        _notifyGitCommitCheckBox     = MakeCheckBox("Git commit pushed (agent-authored only)",      GetToggle(currentSettings, "git_commit_pushed", false));
        _notifyLoopIterationCheckBox = MakeCheckBox("Loop iteration completes",                     GetToggle(currentSettings, "loop_iteration_complete", false));
        _notifyLoopStoppedCheckBox   = MakeCheckBox("Loop stopped",                                 GetToggle(currentSettings, "loop_stopped", true));
        _notifyRcEstablishedCheckBox = MakeCheckBox("Remote connection established",                GetToggle(currentSettings, "rc_connection_established", false));
        _notifyRcDroppedCheckBox     = MakeCheckBox("Remote connection dropped",                    GetToggle(currentSettings, "rc_connection_dropped", true));

        if (showDevOptions) {
            _startupIssueSimulationComboBox = new ComboBox { Height = 30, Margin = new Thickness(0, 0, 0, 14) };
            AddSimulationOption(_startupIssueSimulationComboBox, "None", DeveloperStartupIssueSimulation.None);
            AddSimulationOption(_startupIssueSimulationComboBox, "Missing Node.js tooling", DeveloperStartupIssueSimulation.MissingNodeTooling);
            AddSimulationOption(_startupIssueSimulationComboBox, "Squad not installed", DeveloperStartupIssueSimulation.SquadNotInstalled);
            AddSimulationOption(_startupIssueSimulationComboBox, "Partial Squad install", DeveloperStartupIssueSimulation.PartialSquadInstall);
            SelectSimulationOption(_startupIssueSimulationComboBox, currentSettings.StartupIssueSimulation);

            _runtimeIssueSimulationComboBox = new ComboBox { Height = 30 };
            AddSimulationOption(_runtimeIssueSimulationComboBox, "None", DeveloperRuntimeIssueSimulation.None);
            AddSimulationOption(_runtimeIssueSimulationComboBox, "Copilot auth required", DeveloperRuntimeIssueSimulation.CopilotAuthRequired);
            AddSimulationOption(_runtimeIssueSimulationComboBox, "Bundled SDK repair", DeveloperRuntimeIssueSimulation.BundledSdkRepair);
            AddSimulationOption(_runtimeIssueSimulationComboBox, "Build temp files", DeveloperRuntimeIssueSimulation.BuildTempFiles);
            AddSimulationOption(_runtimeIssueSimulationComboBox, "Generic runtime failure", DeveloperRuntimeIssueSimulation.GenericRuntimeFailure);
            SelectSimulationOption(_runtimeIssueSimulationComboBox, currentSettings.RuntimeIssueSimulation);
        }

        // ── Window skeleton ───────────────────────────────────────────────

        var root = new DockPanel();
        Content = root;

        // Footer: Save button + status text
        var footer = new DockPanel { Margin = new Thickness(16, 8, 16, 12) };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var saveButton = new Button { Content = "Save", Width = 88, Height = 30 };
        saveButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        DockPanel.SetDock(saveButton, Dock.Right);
        saveButton.Click += SaveButton_Click;
        footer.Children.Add(saveButton);

        _statusText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        footer.Children.Add(_statusText);

        var footerSep = new Separator();
        DockPanel.SetDock(footerSep, Dock.Bottom);
        root.Children.Add(footerSep);

        // Body: 130 px nav strip + content host
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(body);

        var navStrip = new Border {
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(0, 8, 0, 0)
        };
        navStrip.SetResourceReference(Border.BackgroundProperty, "SidebarPanelSurface");
        navStrip.SetResourceReference(Border.BorderBrushProperty, "SidebarPanelBorder");
        var navStack = new StackPanel();
        navStrip.Child = navStack;
        Grid.SetColumn(navStrip, 0);
        body.Children.Add(navStrip);

        _pageHost = new ContentControl();
        Grid.SetColumn(_pageHost, 1);
        body.Children.Add(_pageHost);

        // ── Build pages and wire nav buttons ─────────────────────────────

        var pageList = new List<(string label, UIElement page)> {
            ("General",       BuildGeneralPage()),
            ("Remote Access", BuildRemoteAccessPage()),
            ("Custom Model",  BuildByokPage()),
            ("Notifications", BuildNotificationsPage(currentSettings)),
        };
        if (showDevOptions)
            pageList.Add(("Dev / Diag.", BuildDevPage()));

        _pages      = new UIElement[pageList.Count];
        _navButtons = new Button[pageList.Count];
        for (int i = 0; i < pageList.Count; i++) {
            var (label, page) = pageList[i];
            _pages[i] = page;
            var btn = new Button { Content = label };
            btn.SetResourceReference(Control.StyleProperty, "PrefsNavItemStyle");
            var idx = i;
            btn.Click += (_, _) => NavigateTo(idx);
            navStack.Children.Add(btn);
            _navButtons[i] = btn;
        }

        NavigateTo(0);
        UpdateQrCode();
    }

    private void NavigateTo(int index) {
        _currentPage = index;
        _pageHost.Content = _pages[index];
        for (int i = 0; i < _navButtons.Length; i++)
            _navButtons[i].Tag = i == index ? "selected" : null;
    }

    private UIElement BuildGeneralPage() {
        var form = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };

        AddLabel(form, "User Name (appears in the Transcript, before user prompts)");
        form.Children.Add(_userNameBox);

        AddLabel(form, "Azure Speech API Key");
        var apiKeyHost = new Grid();
        apiKeyHost.Children.Add(_apiKeyPasswordBox);
        apiKeyHost.Children.Add(_apiKeyRevealBox);
        form.Children.Add(apiKeyHost);

        var revealLink = MakeRevealLink("(reveal key)");
        revealLink.MouseLeftButtonDown += RevealLink_MouseDown;
        revealLink.MouseLeftButtonUp += RevealLink_MouseUp;
        form.Children.Add(revealLink);

        AddLabel(form, "Azure Speech Region", topMargin: 20);
        form.Children.Add(_speechRegionBox);

        var regionHint = new TextBlock {
            Text = "e.g. eastus, westus2, westeurope",
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0)
        };
        regionHint.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        form.Children.Add(regionHint);

        return WrapInScrollViewer(form);
    }

    private UIElement BuildRemoteAccessPage() {
        var form = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };

        AddSectionHeader(form, "Remote Access Tunnel");

        var hint = new TextBlock {
            Text = "Optionally auto-start a public tunnel when Remote Access starts, for access from outside your local network.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 12)
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        form.Children.Add(hint);

        AddLabel(form, "Tunnel Provider:");
        form.Children.Add(_tunnelModeComboBox);

        AddLabel(form, "Tunnel Auth Token (optional — leave blank if tunnel binary is pre-configured)", wrap: true);
        var tokenHost = new Grid();
        tokenHost.Children.Add(_tunnelTokenPasswordBox);
        tokenHost.Children.Add(_tunnelTokenRevealBox);
        form.Children.Add(tokenHost);

        var revealTunnelLink = MakeRevealLink("(reveal token)");
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

        return WrapInScrollViewer(form);
    }

    private UIElement BuildByokPage() {
        var form = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };

        AddSectionHeader(form, "Custom Model Provider (BYOK)");

        var hint = new TextBlock {
            Text = "Override the default Copilot model with a custom provider (e.g. Ollama). Leave blank to use GitHub Copilot.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 12)
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        form.Children.Add(hint);

        AddLabel(form, "Provider URL:");
        form.Children.Add(_byokProviderUrlBox);

        var urlHint = new TextBlock {
            Text = "e.g. http://localhost:11434/v1",
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 12)
        };
        urlHint.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        form.Children.Add(urlHint);

        AddLabel(form, "Model:");
        form.Children.Add(_byokModelBox);

        AddLabel(form, "Provider Type:");
        form.Children.Add(_byokProviderTypeComboBox);

        AddLabel(form, "API Key (optional):");
        var byokApiKeyHost = new Grid();
        byokApiKeyHost.Children.Add(_byokApiKeyPasswordBox);
        byokApiKeyHost.Children.Add(_byokApiKeyRevealBox);
        form.Children.Add(byokApiKeyHost);

        var revealByokLink = MakeRevealLink("(reveal key)");
        revealByokLink.MouseLeftButtonDown += (_, _) => {
            _byokApiKeyRevealBox.Text = _byokApiKeyPasswordBox.Password;
            _byokApiKeyPasswordBox.Visibility = Visibility.Collapsed;
            _byokApiKeyRevealBox.Visibility = Visibility.Visible;
        };
        revealByokLink.MouseLeftButtonUp += (_, _) => {
            _byokApiKeyPasswordBox.Password = _byokApiKeyRevealBox.Text;
            _byokApiKeyRevealBox.Visibility = Visibility.Collapsed;
            _byokApiKeyPasswordBox.Visibility = Visibility.Visible;
        };
        form.Children.Add(revealByokLink);

        var byokTestPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 4) };
        var byokTestButton = new Button {
            Content = "Test Connection",
            Padding = new Thickness(12, 4, 12, 4),
            Height = 28
        };
        byokTestButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        byokTestButton.Click += ByokTestButton_Click;
        byokTestPanel.Children.Add(byokTestButton);
        byokTestPanel.Children.Add(_byokTestStatusText);
        form.Children.Add(byokTestPanel);

        return WrapInScrollViewer(form);
    }

    private UIElement BuildNotificationsPage(ApplicationSettingsSnapshot currentSettings) {
        var form = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };

        AddSectionHeader(form, "Notifications");
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

        AddLabel(ntfyStack, "Topic:");
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

        ntfyStack.Children.Add(_qrCodeImage);

        var scanHint = new TextBlock {
            Text = "Scan with ntfy phone app",
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 2)
        };
        scanHint.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        ntfyStack.Children.Add(scanHint);
        ntfyStack.Children.Add(_ntfyUrlText);

        var notifyWhenLabel = new TextBlock {
            Text = "Notify me when:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        notifyWhenLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        form.Children.Add(notifyWhenLabel);

        form.Children.Add(_notifyAiTurnCheckBox);
        form.Children.Add(_notifyGitCommitCheckBox);
        form.Children.Add(_notifyLoopIterationCheckBox);
        form.Children.Add(_notifyLoopStoppedCheckBox);
        form.Children.Add(_notifyRcEstablishedCheckBox);
        form.Children.Add(_notifyRcDroppedCheckBox);

        var testButton = new Button {
            Content = "Test Notification",
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 12, 0, 0)
        };
        testButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        testButton.Click += TestButton_Click;
        form.Children.Add(testButton);

        return WrapInScrollViewer(form);
    }

    private UIElement BuildDevPage() {
        var form = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };

        AddSectionHeader(form, "Developer Issue Simulation");

        var devHint = new TextBlock {
            Text = "Use this only for UI testing. Startup simulations affect the top issue panel. Runtime simulations make the next prompt fail through the friendly error path.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 12)
        };
        devHint.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        form.Children.Add(devHint);

        AddLabel(form, "Startup Issue Preview");
        form.Children.Add(_startupIssueSimulationComboBox!);

        AddLabel(form, "Runtime Failure Simulation", topMargin: 6);
        form.Children.Add(_runtimeIssueSimulationComboBox!);

        return WrapInScrollViewer(form);
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

    private async void ByokTestButton_Click(object sender, RoutedEventArgs e) {
        var url = _byokProviderUrlBox.Text.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(url)) {
            _byokTestStatusText.Text = "Enter a Provider URL first.";
            return;
        }
        _byokTestStatusText.Text = "Testing…";
        try {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var apiKey = _byokApiKeyRevealBox.IsVisible ? _byokApiKeyRevealBox.Text : _byokApiKeyPasswordBox.Password;
            if (!string.IsNullOrWhiteSpace(apiKey))
                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            var response = await http.GetStringAsync($"{url}/models").ConfigureAwait(true);
            var ids = System.Text.RegularExpressions.Regex.Matches(response, "\"id\"\\s*:\\s*\"([^\"]+)\"");
            if (ids.Count > 0) {
                var names = string.Join(", ", System.Linq.Enumerable.Select(ids.Cast<System.Text.RegularExpressions.Match>(), m => m.Groups[1].Value));
                _byokTestStatusText.Text = $"✅ Connected — {ids.Count} model(s): {names}";
            } else {
                _byokTestStatusText.Text = "✅ Reachable (no models listed)";
            }
        } catch (Exception ex) {
            _byokTestStatusText.Text = $"❌ {ex.Message}";
        }
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
        var byokProviderType = (_byokProviderTypeComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        var byokApiKey = _byokApiKeyRevealBox.IsVisible ? _byokApiKeyRevealBox.Text : _byokApiKeyPasswordBox.Password;
        updated = _settingsStore.SaveByokSettings(
            string.IsNullOrWhiteSpace(_byokProviderUrlBox.Text.Trim()) ? null : _byokProviderUrlBox.Text.Trim(),
            string.IsNullOrWhiteSpace(_byokModelBox.Text.Trim()) ? null : _byokModelBox.Text.Trim(),
            byokProviderType,
            string.IsNullOrWhiteSpace(byokApiKey) ? null : byokApiKey);
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

    private static CheckBox MakeCheckBox(string label, bool isChecked) {
        var cb = new CheckBox {
            Content = label,
            IsChecked = isChecked,
            Margin = new Thickness(0, 0, 0, 6)
        };
        cb.SetResourceReference(ForegroundProperty, "BodyText");
        return cb;
    }

    private static void AddLabel(Panel parent, string text, int topMargin = 0, bool wrap = false) {
        var label = new TextBlock {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, topMargin, 0, 5),
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        parent.Children.Add(label);
    }

    private static void AddSectionHeader(Panel parent, string text) {
        var header = new TextBlock {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 12)
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        parent.Children.Add(header);
    }

    private static TextBlock MakeRevealLink(string text) {
        var link = new TextBlock {
            Margin = new Thickness(0, 6, 0, 0),
            Cursor = Cursors.Hand
        };
        var run = new System.Windows.Documents.Run(text);
        run.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "ActionLinkText");
        link.Inlines.Add(run);
        return link;
    }

    private static ScrollViewer WrapInScrollViewer(UIElement content) => new ScrollViewer {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Content = content
    };

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
        var tempProvider = new NtfyNotificationProvider(topic);
        await tempProvider.SendAsync("SquadDash Test", "Notifications are working!");
        _statusText.Text = "Test sent!";
    }

    private static bool GetToggle(ApplicationSettingsSnapshot s, string key, bool defaultValue) {
        if (s.NotificationEventToggles is null) return defaultValue;
        return s.NotificationEventToggles.TryGetValue(key, out var v) ? v : defaultValue;
    }
}
