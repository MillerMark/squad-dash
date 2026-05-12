using System;
using System.Linq;
using System.Windows;
using System.Net.Http;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;
using Microsoft.CognitiveServices.Speech;

namespace SquadDash;

internal sealed class PreferencesWindow : Window {
    private readonly ApplicationSettingsStore _settingsStore;
    private readonly Action<ApplicationSettingsSnapshot> _onSaved;
    private readonly TextBox _userNameBox;
    private readonly PasswordBox _apiKeyPasswordBox;
    private readonly TextBox _apiKeyRevealBox;
    private readonly TextBox _speechRegionBox;
    private readonly RadioButton _azureSpeechRadio;
    private readonly RadioButton _openAiSpeechRadio;
    private readonly PasswordBox _openAiSpeechKeyPasswordBox;
    private readonly TextBox _openAiSpeechKeyRevealBox;
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
    private readonly TextBox _cleanupPromptBox;
    // ── Sound notification controls ──────────────────────────────────────
    private readonly CheckBox _soundPromptCompleteCheckBox;
    private readonly TextBox  _soundPromptCompletePathBox;
    private readonly CheckBox _soundPromptErrorCheckBox;
    private readonly TextBox  _soundPromptErrorPathBox;
    private readonly CheckBox _soundApprovalNeededCheckBox;
    private readonly TextBox  _soundApprovalNeededPathBox;
    private readonly CheckBox _soundQueueEmptyCheckBox;
    private readonly TextBox  _soundQueueEmptyPathBox;
    private readonly CheckBox _soundLoopIterationCompleteCheckBox;
    private readonly TextBox  _soundLoopIterationCompletePathBox;
    private readonly CheckBox _soundLoopStoppedCheckBox;
    private readonly TextBox  _soundLoopStoppedPathBox;
    private readonly CheckBox _soundCommitMadeCheckBox;
    private readonly TextBox  _soundCommitMadePathBox;
    private readonly CheckBox _soundQuickRepliesShownCheckBox;
    private readonly TextBox  _soundQuickRepliesShownPathBox;
    private readonly ObservableCollection<VoiceReplacementRuleViewModel> _voiceReplacementRules;

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
        _userNameBox.SetResourceReference(TextBox.BackgroundProperty, "TextBoxBackground");
        _userNameBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        _userNameBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");

        var currentApiKey = Environment.GetEnvironmentVariable("SQUAD_SPEECH_KEY", EnvironmentVariableTarget.User) ?? string.Empty;
        _apiKeyPasswordBox = new PasswordBox {
            Password = currentApiKey,
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30
        };
        _apiKeyPasswordBox.SetResourceReference(PasswordBox.BackgroundProperty, "TextBoxBackground");
        _apiKeyPasswordBox.SetResourceReference(PasswordBox.BorderBrushProperty, "InputBorder");
        _apiKeyPasswordBox.SetResourceReference(PasswordBox.ForegroundProperty, "LabelText");
        _apiKeyRevealBox = new TextBox {
            Text = currentApiKey,
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30,
            Visibility = Visibility.Collapsed
        };
        _apiKeyRevealBox.SetResourceReference(TextBox.BackgroundProperty, "TextBoxBackground");
        _apiKeyRevealBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        _apiKeyRevealBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");

        _speechRegionBox = new TextBox {
            Text = currentSettings.SpeechRegion ?? string.Empty,
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30
        };
        _speechRegionBox.SetResourceReference(TextBox.BackgroundProperty, "TextBoxBackground");
        _speechRegionBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        _speechRegionBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");

        var isOpenAi = currentSettings.SpeechProvider == SpeechProvider.OpenAI;
        _azureSpeechRadio = new RadioButton {
            Content = "Azure Cognitive Services",
            GroupName = "SpeechProvider",
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 6),
            IsChecked = !isOpenAi
        };
        _azureSpeechRadio.SetResourceReference(Control.StyleProperty, "ThemedRadioButtonStyle");

        _openAiSpeechRadio = new RadioButton {
            Content = "OpenAI Whisper",
            GroupName = "SpeechProvider",
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 6),
            IsChecked = isOpenAi
        };
        _openAiSpeechRadio.SetResourceReference(Control.StyleProperty, "ThemedRadioButtonStyle");

        var currentOpenAiKey = currentSettings.OpenAiSpeechApiKey ?? string.Empty;
        _openAiSpeechKeyPasswordBox = new PasswordBox {
            Password = currentOpenAiKey,
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30
        };
        _openAiSpeechKeyPasswordBox.SetResourceReference(PasswordBox.BackgroundProperty, "TextBoxBackground");
        _openAiSpeechKeyPasswordBox.SetResourceReference(PasswordBox.BorderBrushProperty, "InputBorder");
        _openAiSpeechKeyPasswordBox.SetResourceReference(PasswordBox.ForegroundProperty, "LabelText");
        _openAiSpeechKeyRevealBox = new TextBox {
            Text = currentOpenAiKey,
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30,
            Visibility = Visibility.Collapsed
        };
        _openAiSpeechKeyRevealBox.SetResourceReference(TextBox.BackgroundProperty, "TextBoxBackground");
        _openAiSpeechKeyRevealBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        _openAiSpeechKeyRevealBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");

        _cleanupPromptBox= new TextBox {
            Text = currentSettings.CleanupPrompt,
            Padding = new Thickness(6, 4, 6, 4),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false,
            Height = 60,
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _cleanupPromptBox.SetResourceReference(TextBox.BackgroundProperty, "TextBoxBackground");
        _cleanupPromptBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        _cleanupPromptBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");

        // ── Sound notification controls ───────────────────────────────────
        (_soundPromptCompleteCheckBox,        _soundPromptCompletePathBox)        = MakeSoundRow(currentSettings.Sound_PromptComplete_Enabled,        currentSettings.Sound_PromptComplete_CustomPath);
        (_soundPromptErrorCheckBox,           _soundPromptErrorPathBox)           = MakeSoundRow(currentSettings.Sound_PromptError_Enabled,           currentSettings.Sound_PromptError_CustomPath);
        (_soundApprovalNeededCheckBox,        _soundApprovalNeededPathBox)        = MakeSoundRow(currentSettings.Sound_ApprovalNeeded_Enabled,        currentSettings.Sound_ApprovalNeeded_CustomPath);
        (_soundQueueEmptyCheckBox,            _soundQueueEmptyPathBox)            = MakeSoundRow(currentSettings.Sound_QueueEmpty_Enabled,            currentSettings.Sound_QueueEmpty_CustomPath);
        (_soundLoopIterationCompleteCheckBox, _soundLoopIterationCompletePathBox) = MakeSoundRow(currentSettings.Sound_LoopIterationComplete_Enabled, currentSettings.Sound_LoopIterationComplete_CustomPath);
        (_soundLoopStoppedCheckBox,           _soundLoopStoppedPathBox)           = MakeSoundRow(currentSettings.Sound_LoopStopped_Enabled,           currentSettings.Sound_LoopStopped_CustomPath);
        (_soundCommitMadeCheckBox,            _soundCommitMadePathBox)            = MakeSoundRow(currentSettings.Sound_CommitMade_Enabled,            currentSettings.Sound_CommitMade_CustomPath);
        (_soundQuickRepliesShownCheckBox,     _soundQuickRepliesShownPathBox)     = MakeSoundRow(currentSettings.Sound_QuickRepliesShown_Enabled,     currentSettings.Sound_QuickRepliesShown_CustomPath);

        // CheckBox tooltips — explain when each sound event fires
        _soundPromptCompleteCheckBox.ToolTip        = "Plays when a prompt finishes successfully.";
        _soundPromptErrorCheckBox.ToolTip           = "Plays when a prompt fails with an error.";
        _soundApprovalNeededCheckBox.ToolTip        = "Plays when an item is added to the Approvals panel.";
        _soundQueueEmptyCheckBox.ToolTip            = "Plays when the prompt queue drains to empty.";
        _soundLoopIterationCompleteCheckBox.ToolTip = "Plays at the end of each loop iteration.";
        _soundLoopStoppedCheckBox.ToolTip           = "Plays when the loop stops entirely.";
        _soundCommitMadeCheckBox.ToolTip            = "Plays when a git commit is made by an agent.";
        _soundQuickRepliesShownCheckBox.ToolTip     = "Plays when quick reply buttons appear in the transcript.";

        _voiceReplacementRules = new ObservableCollection<VoiceReplacementRuleViewModel>(
            currentSettings.VoiceReplacementRules.Select(r =>
                new VoiceReplacementRuleViewModel { Pattern = r.Pattern, Replacement = r.Replacement }));

        _tunnelModeComboBox= new ComboBox { Height = 30, Margin = new Thickness(0, 0, 0, 12) };
        _tunnelModeComboBox.SetResourceReference(StyleProperty, "ThemedComboBoxStyle");
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
        _tunnelTokenPasswordBox.SetResourceReference(PasswordBox.BackgroundProperty, "TextBoxBackground");
        _tunnelTokenPasswordBox.SetResourceReference(PasswordBox.BorderBrushProperty, "InputBorder");
        _tunnelTokenPasswordBox.SetResourceReference(PasswordBox.ForegroundProperty, "LabelText");
        _tunnelTokenRevealBox = new TextBox {
            Text = currentTunnelToken,
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30,
            Visibility = Visibility.Collapsed
        };
        _tunnelTokenRevealBox.SetResourceReference(TextBox.BackgroundProperty, "TextBoxBackground");
        _tunnelTokenRevealBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        _tunnelTokenRevealBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");

        _byokProviderUrlBox = new TextBox {
            Text = currentSettings.ByokProviderUrl ?? string.Empty,
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30
        };
        _byokProviderUrlBox.SetResourceReference(TextBox.BackgroundProperty, "TextBoxBackground");
        _byokProviderUrlBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        _byokProviderUrlBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");
        _byokModelBox = new TextBox {
            Text = currentSettings.ByokModel ?? string.Empty,
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30,
            Margin = new Thickness(0, 0, 0, 12)
        };
        _byokModelBox.SetResourceReference(TextBox.BackgroundProperty, "TextBoxBackground");
        _byokModelBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        _byokModelBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");

        _byokProviderTypeComboBox = new ComboBox { Height = 30, Margin = new Thickness(0, 0, 0, 12) };
        _byokProviderTypeComboBox.SetResourceReference(StyleProperty, "ThemedComboBoxStyle");
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
        _byokApiKeyPasswordBox.SetResourceReference(PasswordBox.BackgroundProperty, "TextBoxBackground");
        _byokApiKeyPasswordBox.SetResourceReference(PasswordBox.BorderBrushProperty, "InputBorder");
        _byokApiKeyPasswordBox.SetResourceReference(PasswordBox.ForegroundProperty, "LabelText");
        _byokApiKeyRevealBox = new TextBox {
            Text = currentByokApiKey,
            Padding = new Thickness(6, 4, 6, 4),
            Height = 30,
            Visibility = Visibility.Collapsed
        };
        _byokApiKeyRevealBox.SetResourceReference(TextBox.BackgroundProperty, "TextBoxBackground");
        _byokApiKeyRevealBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        _byokApiKeyRevealBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");
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
        _notificationTopicBox.SetResourceReference(TextBox.BackgroundProperty, "TextBoxBackground");
        _notificationTopicBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        _notificationTopicBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");
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

        _notifyAiTurnCheckBox = MakeCheckBox("AI turn completes", GetToggle(currentSettings, "assistant_turn_complete", true));
        _notifyGitCommitCheckBox = MakeCheckBox("Git commit pushed (agent-authored only)", GetToggle(currentSettings, "git_commit_pushed", false));
        _notifyLoopIterationCheckBox = MakeCheckBox("Loop iteration completes", GetToggle(currentSettings, "loop_iteration_complete", false));
        _notifyLoopStoppedCheckBox = MakeCheckBox("Loop stopped", GetToggle(currentSettings, "loop_stopped", true));
        _notifyRcEstablishedCheckBox = MakeCheckBox("Remote connection established", GetToggle(currentSettings, "rc_connection_established", false));
        _notifyRcDroppedCheckBox = MakeCheckBox("Remote connection dropped", GetToggle(currentSettings, "rc_connection_dropped", true));

        if (showDevOptions) {
            _startupIssueSimulationComboBox = new ComboBox { Height = 30, Margin = new Thickness(0, 0, 0, 14) };
            _startupIssueSimulationComboBox.SetResourceReference(StyleProperty, "ThemedComboBoxStyle");
            AddSimulationOption(_startupIssueSimulationComboBox, "None", DeveloperStartupIssueSimulation.None);
            AddSimulationOption(_startupIssueSimulationComboBox, "Missing Node.js tooling", DeveloperStartupIssueSimulation.MissingNodeTooling);
            AddSimulationOption(_startupIssueSimulationComboBox, "Squad not installed", DeveloperStartupIssueSimulation.SquadNotInstalled);
            AddSimulationOption(_startupIssueSimulationComboBox, "Partial Squad install", DeveloperStartupIssueSimulation.PartialSquadInstall);
            SelectSimulationOption(_startupIssueSimulationComboBox, currentSettings.StartupIssueSimulation);

            _runtimeIssueSimulationComboBox = new ComboBox { Height = 30 };
            _runtimeIssueSimulationComboBox.SetResourceReference(StyleProperty, "ThemedComboBoxStyle");
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
            ("Speech",        BuildSpeechPage()),
            ("Remote Access", BuildRemoteAccessPage()),
            ("Custom Model",  BuildByokPage()),
            ("Notifications", BuildNotificationsPage(currentSettings)),
            ("Sounds",        BuildSoundsPage(currentSettings)),
            ("AI",            BuildAiPage()),
        };
        if (showDevOptions)
            pageList.Add(("Dev / Diag.", BuildDevPage()));

        _pages = new UIElement[pageList.Count];
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

        NavigateTo(Math.Min(currentSettings.Preferences_LastPage, _pages.Length - 1));
        UpdateQrCode();
    }

    private void NavigateTo(int index) {
        _currentPage = index;
        _pageHost.Content = _pages[index];
        for (int i = 0; i < _navButtons.Length; i++)
            _navButtons[i].Tag = i == index ? "selected" : null;
        _settingsStore.SavePreferencesLastPage(index);
    }

    private UIElement BuildGeneralPage() {
        var form = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };

        AddLabel(form, "User Name (appears in the Transcript, before user prompts)");
        form.Children.Add(_userNameBox);

        return WrapInScrollViewer(form);
    }

    private UIElement BuildSpeechPage() {
        var form = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };

        AddSectionHeader(form, "Speech");

        AddLabel(form, "Provider");
        form.Children.Add(_azureSpeechRadio);
        form.Children.Add(_openAiSpeechRadio);

        // Azure-specific fields
        var azureSection = new StackPanel {
            Visibility = _azureSpeechRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed
        };

        AddLabel(azureSection, "Azure Speech API Key", topMargin: 20);
        var apiKeyHost = new Grid();
        apiKeyHost.Children.Add(_apiKeyPasswordBox);
        apiKeyHost.Children.Add(_apiKeyRevealBox);
        azureSection.Children.Add(apiKeyHost);

        var revealLink = MakeRevealLink("(reveal key)");
        revealLink.MouseLeftButtonDown += RevealLink_MouseDown;
        revealLink.MouseLeftButtonUp += RevealLink_MouseUp;
        azureSection.Children.Add(revealLink);

        AddLabel(azureSection, "Azure Speech Region", topMargin: 20);
        azureSection.Children.Add(_speechRegionBox);

        var regionHint = new TextBlock {
            Text = "e.g. eastus, westus2, westeurope",
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0)
        };
        regionHint.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        azureSection.Children.Add(regionHint);

        form.Children.Add(azureSection);

        // OpenAI Whisper-specific fields
        var openAiSection = new StackPanel {
            Visibility = _openAiSpeechRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed
        };

        AddLabel(openAiSection, "OpenAI speech API key", topMargin: 20);
        var openAiKeyHost = new Grid();
        openAiKeyHost.Children.Add(_openAiSpeechKeyPasswordBox);
        openAiKeyHost.Children.Add(_openAiSpeechKeyRevealBox);
        openAiSection.Children.Add(openAiKeyHost);

        var openAiRevealLink = MakeRevealLink("(reveal key)");
        openAiRevealLink.MouseLeftButtonDown += OpenAiRevealLink_MouseDown;
        openAiRevealLink.MouseLeftButtonUp += OpenAiRevealLink_MouseUp;
        openAiSection.Children.Add(openAiRevealLink);

        form.Children.Add(openAiSection);

        // Wire radio buttons to toggle sections and auto-save
        _azureSpeechRadio.Checked += (_, _) => {
            azureSection.Visibility = Visibility.Visible;
            openAiSection.Visibility = Visibility.Collapsed;
            SaveSpeechProviderNow();
        };
        _openAiSpeechRadio.Checked += (_, _) => {
            azureSection.Visibility = Visibility.Collapsed;
            openAiSection.Visibility = Visibility.Visible;
            SaveSpeechProviderNow();
        };

        _openAiSpeechKeyPasswordBox.PasswordChanged += (_, _) => SaveSpeechProviderNow();

        // ── Voice Text Replacements ───────────────────────────────────────
        AddSectionHeader(form, "Voice Text Replacements", topMargin: 24);
        AddLabel(form, "Pattern (regex) → Replacement — applied to every voice phrase in order.", topMargin: 4);

        var gridHint = new TextBlock {
            Text = "Double-click a cell to edit. Changes are saved automatically.",
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 6)
        };
        gridHint.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        form.Children.Add(gridHint);

        var replacementsGrid = new DataGrid {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            ItemsSource = _voiceReplacementRules,
            Height = 180,
            Margin = new Thickness(0, 0, 0, 0),
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column
        };
        replacementsGrid.SetResourceReference(DataGrid.BackgroundProperty, "AppSurface");
        replacementsGrid.SetResourceReference(DataGrid.ForegroundProperty, "LabelText");
        replacementsGrid.SetResourceReference(DataGrid.BorderBrushProperty, "SubtleBorder");
        replacementsGrid.SetResourceReference(DataGrid.RowBackgroundProperty, "TextBoxBackground");
        replacementsGrid.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty, "TextBoxBackground");
        replacementsGrid.RowHeaderWidth = 0;
        replacementsGrid.BorderThickness = new Thickness(1);
        replacementsGrid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "SubtleBorder");
        replacementsGrid.SetResourceReference(DataGrid.VerticalGridLinesBrushProperty, "SubtleBorder");

        var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.DataGridColumnHeader.BackgroundProperty, new DynamicResourceExtension("AppSurface")));
        headerStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.DataGridColumnHeader.ForegroundProperty, new DynamicResourceExtension("LabelText")));
        headerStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.DataGridColumnHeader.BorderBrushProperty, new DynamicResourceExtension("SubtleBorder")));
        headerStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.DataGridColumnHeader.PaddingProperty, new Thickness(6, 4, 6, 4)));
        replacementsGrid.ColumnHeaderStyle = headerStyle;

        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(DataGridCell.BackgroundProperty, new DynamicResourceExtension("TextBoxBackground")));
        cellStyle.Setters.Add(new Setter(DataGridCell.ForegroundProperty, new DynamicResourceExtension("LabelText")));
        cellStyle.Setters.Add(new Setter(DataGridCell.BorderBrushProperty, new DynamicResourceExtension("SubtleBorder")));
        cellStyle.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
        replacementsGrid.CellStyle = cellStyle;

        var patternCol = new DataGridTextColumn {
            Header = "Pattern (regex)",
            Binding = new System.Windows.Data.Binding(nameof(VoiceReplacementRuleViewModel.Pattern)) {
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        };
        var replacementCol = new DataGridTextColumn {
            Header = "Replacement",
            Binding = new System.Windows.Data.Binding(nameof(VoiceReplacementRuleViewModel.Replacement)) {
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        };
        replacementsGrid.Columns.Add(patternCol);
        replacementsGrid.Columns.Add(replacementCol);

        var editingElementStyle = new Style(typeof(TextBox));
        editingElementStyle.Setters.Add(new Setter(TextBox.BackgroundProperty, new DynamicResourceExtension("TextBoxBackground")));
        editingElementStyle.Setters.Add(new Setter(TextBox.ForegroundProperty, new DynamicResourceExtension("LabelText")));
        editingElementStyle.Setters.Add(new Setter(TextBox.BorderBrushProperty, new DynamicResourceExtension("InputBorder")));
        patternCol.EditingElementStyle = editingElementStyle;
        replacementCol.EditingElementStyle = editingElementStyle;

        form.Children.Add(replacementsGrid);

        var btnPanel = new StackPanel {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var addBtn = new Button {
            Content = "Add Rule",
            Height = 28,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 0, 8, 0)
        };
        addBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        addBtn.Click += (_, _) => {
            var newRule = new VoiceReplacementRuleViewModel();
            newRule.PropertyChanged += (_, _) => SaveVoiceReplacementsNow();
            _voiceReplacementRules.Add(newRule);
            replacementsGrid.SelectedItem = newRule;
            replacementsGrid.ScrollIntoView(newRule);
        };
        btnPanel.Children.Add(addBtn);

        var removeBtn = new Button {
            Content = "Remove Selected",
            Height = 28,
            Padding = new Thickness(12, 4, 12, 4)
        };
        removeBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        removeBtn.Click += (_, _) => {
            if (replacementsGrid.SelectedItem is VoiceReplacementRuleViewModel selected)
                _voiceReplacementRules.Remove(selected);
        };
        btnPanel.Children.Add(removeBtn);
        form.Children.Add(btnPanel);

        // Subscribe PropertyChanged on pre-existing rules loaded from settings
        foreach (var rule in _voiceReplacementRules)
            rule.PropertyChanged += (_, _) => SaveVoiceReplacementsNow();

        // Auto-save on add/remove
        _voiceReplacementRules.CollectionChanged += (_, e) => {
            SaveVoiceReplacementsNow();
        };

        return WrapInScrollViewer(form);
    }

    private void SaveVoiceReplacementsNow() {
        var rules = _voiceReplacementRules
            .Where(r => !string.IsNullOrWhiteSpace(r.Pattern))
            .Select(r => new VoiceReplacementRule(r.Pattern.Trim(), r.Replacement ?? string.Empty));
        _settingsStore.SaveVoiceReplacementRules(rules);
    }

    private void SaveSpeechProviderNow() {
        var provider = _openAiSpeechRadio.IsChecked == true ? SpeechProvider.OpenAI : SpeechProvider.Azure;
        var openAiKey = _openAiSpeechKeyPasswordBox.Password.Trim();
        _settingsStore.SaveSpeechProvider(provider, string.IsNullOrWhiteSpace(openAiKey) ? null : openAiKey);
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
        deliveryCombo.SetResourceReference(StyleProperty, "ThemedComboBoxStyle");
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

    private UIElement BuildSoundsPage(ApplicationSettingsSnapshot currentSettings) {
        var form = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };

        AddSectionHeader(form, "Notification Sounds");

        var headerHint = new TextBlock {
            Text = "Play a sound when events occur. Check the box to enable; enter a path to a .mp3 or .wav file for a custom sound, leave blank to use the default Windows alert sound, or enter a quoted phrase like \"Prompt complete\" to have text-to-speech speak it. Right-click a path box to test.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 16)
        };
        headerHint.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        form.Children.Add(headerHint);

        var evtGrid = new Grid();
        evtGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        evtGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        evtGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddSoundEventRow(evtGrid, 0, SoundEvent.PromptComplete,        "Prompt complete",         _soundPromptCompleteCheckBox,        _soundPromptCompletePathBox);
        AddSoundEventRow(evtGrid, 1, SoundEvent.PromptError,           "Prompt error / failed",   _soundPromptErrorCheckBox,           _soundPromptErrorPathBox);
        AddSoundEventRow(evtGrid, 2, SoundEvent.ApprovalNeeded,        "Approval needed",         _soundApprovalNeededCheckBox,        _soundApprovalNeededPathBox);
        AddSoundEventRow(evtGrid, 3, SoundEvent.QueueEmpty,            "Queue empty",             _soundQueueEmptyCheckBox,            _soundQueueEmptyPathBox);
        AddSoundEventRow(evtGrid, 4, SoundEvent.LoopIterationComplete, "Loop iteration complete", _soundLoopIterationCompleteCheckBox, _soundLoopIterationCompletePathBox);
        AddSoundEventRow(evtGrid, 5, SoundEvent.LoopStopped,           "Loop stopped",            _soundLoopStoppedCheckBox,           _soundLoopStoppedPathBox);
        AddSoundEventRow(evtGrid, 6, SoundEvent.CommitMade,            "Commit made",             _soundCommitMadeCheckBox,            _soundCommitMadePathBox);
        AddSoundEventRow(evtGrid, 7, SoundEvent.QuickRepliesShown,     "Quick replies shown",     _soundQuickRepliesShownCheckBox,     _soundQuickRepliesShownPathBox);

        form.Children.Add(evtGrid);

        // ── Update path-box tooltips to mention TTS ───────────────────────────────
        const string ttsPathTip = "Enter a file path, or a quoted phrase like \"Hello!\" to speak it aloud using TTS.";
        _soundPromptCompletePathBox.ToolTip        = ttsPathTip;
        _soundPromptErrorPathBox.ToolTip           = ttsPathTip;
        _soundApprovalNeededPathBox.ToolTip        = ttsPathTip;
        _soundQueueEmptyPathBox.ToolTip            = ttsPathTip;
        _soundLoopIterationCompletePathBox.ToolTip = ttsPathTip;
        _soundLoopStoppedPathBox.ToolTip           = ttsPathTip;
        _soundCommitMadePathBox.ToolTip            = ttsPathTip;
        _soundQuickRepliesShownPathBox.ToolTip     = ttsPathTip;

        // ── TTS Configuration ─────────────────────────────────────────────────────
        AddSectionHeader(form, "Text-to-Speech", topMargin: 24);

        var ttsHint = new TextBlock {
            Text = "When a sound-event path is a quoted phrase like \"Done!\", SquadDash speaks it aloud using the TTS provider below.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 12)
        };
        ttsHint.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        form.Children.Add(ttsHint);

        // ── Row: Provider ─────────────────────────────────────────────────────────
        var ttsProviderRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        ttsProviderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        ttsProviderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var ttsProviderLabel = new TextBlock {
            Text = "TTS Provider",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        ttsProviderLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        Grid.SetColumn(ttsProviderLabel, 0);

        var ttsProviderCombo = new ComboBox {
            Height = 28,
            ToolTip = "Which service to use when a sound event path is a quoted phrase like \"Done!\"."
        };
        ttsProviderCombo.Items.Add("Azure Speech");
        ttsProviderCombo.Items.Add("OpenAI TTS");
        ttsProviderCombo.SelectedIndex = currentSettings.Tts_Provider == TtsProvider.OpenAI ? 1 : 0;
        Grid.SetColumn(ttsProviderCombo, 1);

        ttsProviderRow.Children.Add(ttsProviderLabel);
        ttsProviderRow.Children.Add(ttsProviderCombo);
        form.Children.Add(ttsProviderRow);

        // ── Row: Azure Voice ──────────────────────────────────────────────────────
        var azureVoiceRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        azureVoiceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        azureVoiceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        azureVoiceRow.Visibility = currentSettings.Tts_Provider == TtsProvider.Azure
            ? Visibility.Visible : Visibility.Collapsed;

        var azureVoiceLabel = new TextBlock {
            Text = "Azure Voice",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        azureVoiceLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        Grid.SetColumn(azureVoiceLabel, 0);

        var azureVoiceCombo = new ComboBox {
            IsEditable = true,
            Height = 28,
            ToolTip = "Azure Neural voice name, e.g. en-US-JennyNeural. Type manually or select from the list once loaded."
        };
        azureVoiceCombo.SetResourceReference(ComboBox.BackgroundProperty, "TextBoxBackground");
        azureVoiceCombo.SetResourceReference(ComboBox.BorderBrushProperty, "InputBorder");
        azureVoiceCombo.SetResourceReference(ComboBox.ForegroundProperty, "LabelText");
        Grid.SetColumn(azureVoiceCombo, 1);

        azureVoiceRow.Children.Add(azureVoiceLabel);
        azureVoiceRow.Children.Add(azureVoiceCombo);
        form.Children.Add(azureVoiceRow);

        // ── Row: OpenAI Voice + Model ─────────────────────────────────────────────
        var openAiRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        openAiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        openAiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        openAiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        openAiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        openAiRow.Visibility = currentSettings.Tts_Provider == TtsProvider.OpenAI
            ? Visibility.Visible : Visibility.Collapsed;

        var openAiVoiceLabel = new TextBlock {
            Text = "OpenAI Voice",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        openAiVoiceLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        Grid.SetColumn(openAiVoiceLabel, 0);

        var openAiVoiceCombo = new ComboBox {
            Height = 28,
            Margin = new Thickness(0, 0, 16, 0),
            ToolTip = "OpenAI TTS voice."
        };
        foreach (var v in new[] { "alloy", "echo", "fable", "onyx", "nova", "shimmer" })
            openAiVoiceCombo.Items.Add(v);
        openAiVoiceCombo.SelectedItem = currentSettings.Tts_OpenAi_Voice ?? "alloy";
        if (openAiVoiceCombo.SelectedIndex < 0) openAiVoiceCombo.SelectedIndex = 0;
        Grid.SetColumn(openAiVoiceCombo, 1);

        var openAiModelLabel = new TextBlock {
            Text = "Model",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        openAiModelLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        Grid.SetColumn(openAiModelLabel, 2);

        var openAiModelCombo = new ComboBox { Height = 28 };
        openAiModelCombo.Items.Add("tts-1 (fast)");
        openAiModelCombo.Items.Add("tts-1-hd (quality)");
        openAiModelCombo.SelectedIndex = currentSettings.Tts_OpenAi_Model == OpenAiTtsModel.HD ? 1 : 0;
        Grid.SetColumn(openAiModelCombo, 3);

        openAiRow.Children.Add(openAiVoiceLabel);
        openAiRow.Children.Add(openAiVoiceCombo);
        openAiRow.Children.Add(openAiModelLabel);
        openAiRow.Children.Add(openAiModelCombo);
        form.Children.Add(openAiRow);

        // ── Wire save handlers ────────────────────────────────────────────────────
        void SaveTtsNow() {
            var provider = ttsProviderCombo.SelectedIndex == 1 ? TtsProvider.OpenAI : TtsProvider.Azure;
            _settingsStore.SaveTtsSettings(
                provider,
                azureVoiceCombo.Text.Trim(),
                openAiVoiceCombo.SelectedItem?.ToString() ?? "alloy",
                openAiModelCombo.SelectedIndex == 1 ? OpenAiTtsModel.HD : OpenAiTtsModel.Standard);
        }

        ttsProviderCombo.SelectionChanged += (_, _) => {
            bool isAzure = ttsProviderCombo.SelectedIndex == 0;
            azureVoiceRow.Visibility = isAzure ? Visibility.Visible  : Visibility.Collapsed;
            openAiRow.Visibility     = isAzure ? Visibility.Collapsed : Visibility.Visible;
            SaveTtsNow();
        };
        azureVoiceCombo.SelectionChanged  += (_, _) => SaveTtsNow();
        azureVoiceCombo.AddHandler(TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) => SaveTtsNow()));
        openAiVoiceCombo.SelectionChanged += (_, _) => SaveTtsNow();
        openAiModelCombo.SelectionChanged += (_, _) => SaveTtsNow();

        // ── Populate Azure voice list ─────────────────────────────────────────────
        var speechKey = Environment.GetEnvironmentVariable("SQUAD_SPEECH_KEY", EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(speechKey) && !string.IsNullOrWhiteSpace(currentSettings.SpeechRegion))
            _ = LoadAzureVoicesAsync(azureVoiceCombo, currentSettings.Tts_Azure_Voice, speechKey, currentSettings.SpeechRegion);
        else
        {
            azureVoiceCombo.Items.Add(currentSettings.Tts_Azure_Voice);
            azureVoiceCombo.SelectedIndex = 0;
            azureVoiceCombo.ToolTip = "Configure Azure Speech key and region to load the full voice list.";
        }

        // ── Test TTS Button ───────────────────────────────────────────────────────
        var testButtonPanel = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var testButton = new Button {
            Content = "🔊 Test TTS",
            Padding = new Thickness(12, 4, 12, 4),
            MinWidth = 120
        };
        testButton.SetResourceReference(Button.StyleProperty, "ThemedButton");

        var ttsErrorText = new TextBlock {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 12
        };
        ttsErrorText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorText");

        var copyErrorButton = new Button {
            Content = "📋 Copy error",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0)
        };
        copyErrorButton.SetResourceReference(Button.StyleProperty, "ThemedButton");
        copyErrorButton.Click += (_, _) => Clipboard.SetText(ttsErrorText.Text);

        var ttsErrorPanel = new StackPanel {
            Orientation = Orientation.Vertical,
            Visibility = Visibility.Collapsed
        };
        ttsErrorPanel.Children.Add(ttsErrorText);
        ttsErrorPanel.Children.Add(copyErrorButton);

        testButton.Click += (_, _) => {
            testButton.IsEnabled = false;
            // Capture all UI-owned values on the UI thread before handing off to Task.Run.
            var provider    = ttsProviderCombo.SelectedIndex == 1 ? TtsProvider.OpenAI : TtsProvider.Azure;
            var azureVoice  = azureVoiceCombo.Text.Trim();
            var openAiVoice = openAiVoiceCombo.SelectedItem?.ToString() ?? "alloy";
            var modelIndex  = openAiModelCombo.SelectedIndex;
            var region      = _speechRegionBox.Text.Trim();
            _ = Task.Run(async () => {
                try {
                    var openAiKey   = currentSettings.OpenAiSpeechApiKey;
                    var azureKey    = Environment.GetEnvironmentVariable("SQUAD_SPEECH_KEY", EnvironmentVariableTarget.User);
                    var oaiModel    = modelIndex == 1 ? "tts-1-hd" : "tts-1";

                    ITtsProvider? tts = null;
                    string phrase;
                    if (provider == TtsProvider.Azure) {
                        phrase = $"This is a test of Azure Speech text to speech with the {azureVoice} voice.";
                        if (!string.IsNullOrWhiteSpace(azureKey) && !string.IsNullOrWhiteSpace(region))
                            tts = new AzureTtsProvider(azureKey, region, azureVoice);
                    }
                    else {
                        phrase = $"This is a test of OpenAI text to speech with the {openAiVoice} voice.";
                        if (!string.IsNullOrWhiteSpace(openAiKey))
                            tts = new OpenAiTtsProvider(openAiKey!, openAiVoice, oaiModel);
                    }

                    if (tts is null) {
                        await Dispatcher.InvokeAsync(() => {
                            testButton.Content = "⚠ Not configured";
                        });
                        await Task.Delay(2000);
                    }
                    else {
                        await tts.SpeakAsync(phrase);
                        await Dispatcher.InvokeAsync(() => ttsErrorPanel.Visibility = Visibility.Collapsed);
                    }
                }
                catch (Exception ex) {
                    await Dispatcher.InvokeAsync(() => {
                        testButton.Content = "⚠ Error (see below)";
                        ttsErrorText.Text = ex.Message;
                        ttsErrorPanel.Visibility = Visibility.Visible;
                    });
                    await Task.Delay(2000);
                }
                finally {
                    await Dispatcher.InvokeAsync(() => {
                        testButton.Content = "🔊 Test TTS";
                        testButton.IsEnabled = true;
                    });
                }
            });
        };

        testButtonPanel.Children.Add(testButton);
        form.Children.Add(testButtonPanel);
        form.Children.Add(ttsErrorPanel);

        // Hide error panel when a new test starts
        testButton.Click += (_, _) => ttsErrorPanel.Visibility = Visibility.Collapsed;

        return WrapInScrollViewer(form);
    }

    private void AddSoundEventRow(Grid grid, int rowIndex, SoundEvent evt, string label, CheckBox checkBox, TextBox pathBox) {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        bool isEnabled = checkBox.IsChecked == true;

        // Col 0: CheckBox with event label
        checkBox.Content = label;
        checkBox.Margin = new Thickness(0, 3, 8, 3);
        checkBox.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(checkBox, rowIndex);
        Grid.SetColumn(checkBox, 0);
        grid.Children.Add(checkBox);

        // Col 1: TextBox — star width, tooltip replaces inline hint
        pathBox.Margin = new Thickness(0, 3, 6, 3);
        pathBox.ToolTip = "Leave blank to use the default Windows sound. Right-click to test.";
        Grid.SetRow(pathBox, rowIndex);
        Grid.SetColumn(pathBox, 1);

        // Right-click context menu: play a preview of the current path (or TTS phrase, or system sound).
        var testMenuItem = new MenuItem { Header = "▶  Test sound" };
        testMenuItem.Click += (_, _) => {
            var raw = pathBox.Text.Trim();
            // Quoted phrase → speak via TTS if configured.
            if (raw.StartsWith("\"") && raw.EndsWith("\"") && raw.Length >= 2) {
                var phrase = raw[1..^1];
                _ = Task.Run(async () => {
                    var tts = TryBuildTtsProviderFromStore();
                    if (tts != null)
                        await tts.SpeakAsync(phrase);
                    else
                        await Dispatcher.InvokeAsync(() => System.Media.SystemSounds.Asterisk.Play());
                });
            } else if (!string.IsNullOrEmpty(raw) && System.IO.File.Exists(raw)) {
                try {
                    var player = new MediaPlayer();
                    player.MediaOpened += (_, _) => player.Play();
                    player.MediaEnded  += (_, _) => player.Close();
                    player.Open(new Uri(raw, UriKind.Absolute));
                } catch { System.Media.SystemSounds.Asterisk.Play(); }
            } else {
                System.Media.SystemSounds.Asterisk.Play();
            }
        };
        var ctxMenu = new ContextMenu();
        ctxMenu.Items.Add(testMenuItem);
        pathBox.ContextMenu = ctxMenu;

        grid.Children.Add(pathBox);

        // Col 2: Browse button — auto width
        var browseBtn = new Button {
            Content = "Browse…",
            Padding = new Thickness(10, 4, 10, 4),
            Height = 28,
            Margin = new Thickness(0, 3, 0, 3),
            IsEnabled = isEnabled,
            VerticalAlignment = VerticalAlignment.Center
        };
        browseBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        Grid.SetRow(browseBtn, rowIndex);
        Grid.SetColumn(browseBtn, 2);
        grid.Children.Add(browseBtn);

        // Wire events — save immediately on every change
        checkBox.Checked   += (_, _) => { pathBox.IsEnabled = true;  browseBtn.IsEnabled = true;  SaveSoundSettingNow(evt, checkBox, pathBox); };
        checkBox.Unchecked += (_, _) => { pathBox.IsEnabled = false; browseBtn.IsEnabled = false; SaveSoundSettingNow(evt, checkBox, pathBox); };
        pathBox.LostFocus  += (_, _) => SaveSoundSettingNow(evt, checkBox, pathBox);
        browseBtn.Click    += (_, _) => {
            var dlg = new Microsoft.Win32.OpenFileDialog {
                Title = $"Select sound file for \"{label}\"",
                Filter = "Audio files (*.mp3;*.wav)|*.mp3;*.wav|All files (*.*)|*.*",
                FilterIndex = 1
            };
            if (!string.IsNullOrWhiteSpace(pathBox.Text) && System.IO.File.Exists(pathBox.Text))
                dlg.InitialDirectory = System.IO.Path.GetDirectoryName(pathBox.Text);
            if (dlg.ShowDialog(this) == true) {
                pathBox.Text = dlg.FileName;
                SaveSoundSettingNow(evt, checkBox, pathBox);
            }
        };
    }

    private void SaveSoundSettingNow(SoundEvent evt, CheckBox checkBox, TextBox pathBox) {
        _settingsStore.SaveSoundNotificationSettings(evt, checkBox.IsChecked == true, pathBox.Text.Trim());
    }

    private ITtsProvider? TryBuildTtsProviderFromStore() {
        var s = _settingsStore.Load();
        if (s.Tts_Provider == TtsProvider.Azure) {
            var key = Environment.GetEnvironmentVariable("SQUAD_SPEECH_KEY", EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(s.SpeechRegion))
                return new AzureTtsProvider(key, s.SpeechRegion, s.Tts_Azure_Voice);
        } else if (s.Tts_Provider == TtsProvider.OpenAI) {
            if (!string.IsNullOrWhiteSpace(s.OpenAiSpeechApiKey)) {
                var model = s.Tts_OpenAi_Model == OpenAiTtsModel.HD ? "tts-1-hd" : "tts-1";
                return new OpenAiTtsProvider(s.OpenAiSpeechApiKey, s.Tts_OpenAi_Voice, model);
            }
        }
        return null;
    }

    private static (CheckBox cb, TextBox tb) MakeSoundRow(bool enabled, string customPath) {
        var cb = new CheckBox { IsChecked = enabled, Margin = new Thickness(0, 0, 0, 4) };
        cb.SetResourceReference(ForegroundProperty, "BodyText");
        var tb = new TextBox {
            Text = customPath ?? string.Empty,
            IsEnabled = enabled,
            Padding = new Thickness(6, 4, 6, 4),
            Height = 28
        };
        tb.SetResourceReference(TextBox.BackgroundProperty, "TextBoxBackground");
        tb.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        tb.SetResourceReference(TextBox.ForegroundProperty, "LabelText");
        return (cb, tb);
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

    private UIElement BuildAiPage() {
        var form = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };

        AddSectionHeader(form, "AI");

        AddLabel(form, "Quick Cleanup prompt (Ctrl+Shift+C):", wrap: true);
        form.Children.Add(_cleanupPromptBox);

        var hint = new TextBlock {
            Text = "This prompt is sent automatically when you press Ctrl+Shift+C with text selected in any markdown source editor. When the AI revision lands, the original selection is replaced with the cleaned-up version. You can work elsewhere in the document while waiting for AI to return.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        form.Children.Add(hint);

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

    private void OpenAiRevealLink_MouseDown(object sender, MouseButtonEventArgs e) {
        _openAiSpeechKeyRevealBox.Text = _openAiSpeechKeyPasswordBox.Password;
        _openAiSpeechKeyPasswordBox.Visibility = Visibility.Collapsed;
        _openAiSpeechKeyRevealBox.Visibility = Visibility.Visible;
    }

    private void OpenAiRevealLink_MouseUp(object sender, MouseButtonEventArgs e) {
        _openAiSpeechKeyPasswordBox.Password = _openAiSpeechKeyRevealBox.Text;
        _openAiSpeechKeyRevealBox.Visibility = Visibility.Collapsed;
        _openAiSpeechKeyPasswordBox.Visibility = Visibility.Visible;
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
            }
            else {
                _byokTestStatusText.Text = "✅ Reachable (no models listed)";
            }
        }
        catch (Exception ex) {
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
        updated = _settingsStore.SaveSpeechProvider(
            _openAiSpeechRadio.IsChecked == true ? SpeechProvider.OpenAI : SpeechProvider.Azure,
            string.IsNullOrWhiteSpace(_openAiSpeechKeyPasswordBox.Password.Trim()) ? null : _openAiSpeechKeyPasswordBox.Password.Trim());
        updated = _settingsStore.SaveDeveloperIssueSimulation(startupIssueSimulation, runtimeIssueSimulation);
        var notifEnabled = _notificationsEnabledCheckBox.IsChecked == true;
        var notifTopic = _notificationTopicBox.Text.Trim();
        updated = _settingsStore.SaveNotificationSettings(
            notifEnabled ? "ntfy" : null,
            notifEnabled && !string.IsNullOrWhiteSpace(notifTopic)
                ? new System.Collections.Generic.Dictionary<string, string> { ["topic"] = notifTopic }
                : null,
            new System.Collections.Generic.Dictionary<string, bool> {
                ["assistant_turn_complete"] = _notifyAiTurnCheckBox.IsChecked == true,
                ["git_commit_pushed"] = _notifyGitCommitCheckBox.IsChecked == true,
                ["loop_iteration_complete"] = _notifyLoopIterationCheckBox.IsChecked == true,
                ["loop_stopped"] = _notifyLoopStoppedCheckBox.IsChecked == true,
                ["rc_connection_established"] = _notifyRcEstablishedCheckBox.IsChecked == true,
                ["rc_connection_dropped"] = _notifyRcDroppedCheckBox.IsChecked == true,
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
        updated = _settingsStore.SaveCleanupPrompt(_cleanupPromptBox.Text.Trim());
        SaveVoiceReplacementsNow();
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

    private static void AddSectionHeader(Panel parent, string text, double topMargin = 0) {
        var header = new TextBlock {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, topMargin, 0, 12)
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
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty);
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

    private static async Task LoadAzureVoicesAsync(ComboBox combo, string currentVoice, string key, string region)
    {
        combo.Items.Clear();
        combo.Items.Add("Loading voices…");
        combo.SelectedIndex = 0;
        combo.IsEnabled = false;

        try
        {
            var config = SpeechConfig.FromSubscription(key, region);
            using var synth = new SpeechSynthesizer(config, null);
            var result = await synth.GetVoicesAsync().ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                combo.Items.Clear();
                var voices = result.Voices
                    .OrderBy(v => v.Locale)
                    .ThenBy(v => v.ShortName)
                    .Select(v => v.ShortName)
                    .ToList();

                foreach (var v in voices)
                    combo.Items.Add(v);

                combo.IsEnabled = true;

                var idx = voices.IndexOf(currentVoice);
                combo.SelectedIndex = idx >= 0 ? idx : 0;
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                combo.Items.Clear();
                combo.Items.Add(currentVoice);
                combo.SelectedIndex = 0;
                combo.IsEnabled = true;
                Debug.WriteLine($"Azure voice load failed: {ex.Message}");
            });
        }
    }

    private sealed class VoiceReplacementRuleViewModel : System.ComponentModel.INotifyPropertyChanged {
        private string _pattern = string.Empty;
        private string _replacement = string.Empty;

        public string Pattern {
            get => _pattern;
            set { _pattern = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Pattern))); }
        }
        public string Replacement {
            get => _replacement;
            set { _replacement = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Replacement))); }
        }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}
