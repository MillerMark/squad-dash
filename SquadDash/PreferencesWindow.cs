using System;
using System.Linq;
using System.Windows;
using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shell;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.CognitiveServices.Speech;

namespace SquadDash;

internal sealed class PreferencesWindow : Window {
    private readonly ApplicationSettingsStore _settingsStore;
    private readonly IWorkspacePaths _workspacePaths;
    private readonly Action<ApplicationSettingsSnapshot> _onSaved;
    private readonly TextBox _userNameBox;
    private readonly PasswordBox _apiKeyPasswordBox;
    private readonly TextBox _apiKeyRevealBox;
    private readonly TextBox _speechRegionBox;
    private readonly ComboBox _speechLanguageComboBox;
    private readonly RadioButton _azureSpeechRadio;
    private readonly RadioButton _openAiSpeechRadio;
    private readonly RadioButton _pttAutoSendRadio;
    private readonly RadioButton _pttDoNothingRadio;
    private readonly PasswordBox _openAiSpeechKeyPasswordBox;
    private readonly TextBox _openAiSpeechKeyRevealBox;
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
    private readonly RadioButton _githubCopilotProviderRadio;
    private readonly RadioButton _customModelProviderRadio;
    private readonly ComboBox _copilotModelComboBox;
    private readonly TextBlock _copilotModelStatusText;
    private readonly StackPanel _githubCopilotModelPanel;
    private readonly StackPanel _customModelProviderPanel;
    private readonly TextBox _byokProviderUrlBox;
    private readonly TextBox _byokModelBox;
    private readonly ComboBox _byokProviderTypeComboBox;
    private readonly PasswordBox _byokApiKeyPasswordBox;
    private readonly TextBox _byokApiKeyRevealBox;
    private readonly CheckBox _byokOfflineModeCheckBox;
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
    // ── Hints page ───────────────────────────────────────────────────────────
    private readonly HintsOptionsPage _hintsOptionsPage;
    private readonly Action?          _startGuidedTour;

    private static readonly string[] KnownCopilotModelOptions = {
        ApplicationSettingsSnapshot.DefaultCopilotModel,
        "claude-sonnet-4.6",
        "claude-sonnet-4.5",
        "claude-sonnet-4",
        "claude-opus-4.6",
        "claude-opus-4.5",
        "claude-haiku-4.5",
        "gpt-5.4",
        "gpt-5.4-mini",
        "gpt-5.3-codex",
        "gpt-5.2-codex",
        "gpt-5.2",
        "gpt-5.1-codex-max",
        "gpt-5.1-codex",
        "gpt-5.1",
        "gpt-5.1-codex-mini",
        "gpt-5-mini",
        "gpt-4.1",
        "gemini-3-pro-preview"
    };

    private readonly UIElement[] _pages;
    private readonly Dictionary<int, TreeViewItem> _leafItems;
    private int _currentPage;
    private readonly ContentControl _pageHost;

    // ── Push-to-talk support ──────────────────────────────────────────────
    private readonly Action<TextBox>? _startPtt;
    private readonly Action?          _stopPtt;
    private readonly CtrlDoubleTapGestureTracker _pttGesture =
        new CtrlDoubleTapGestureTracker(maxTapHoldMs: 250, doubleTapGapMs: 350);
    private bool _pttActive;

    private PreferencesWindow(
        ApplicationSettingsStore settingsStore,
        ApplicationSettingsSnapshot currentSettings,
        PushNotificationService pushNotificationService,
        IWorkspacePaths workspacePaths,
        Action<ApplicationSettingsSnapshot> onSaved,
        bool showDevOptions = false,
        Action<TextBox>? startPtt = null,
        Action? stopPtt = null,
        Action? startGuidedTour = null) {
        _settingsStore    = settingsStore;
        _pushNotificationService = pushNotificationService;
        _workspacePaths   = workspacePaths;
        _onSaved          = onSaved;
        _startPtt         = startPtt;
        _stopPtt          = stopPtt;
        _startGuidedTour  = startGuidedTour;

        Title = "Preferences";
        Width = 640;
        Height = 1000;
        MinWidth = 540;
        MinHeight = 560;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        this.SetResourceReference(BackgroundProperty, "AppSurface");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        WindowChrome.SetWindowChrome(this, new WindowChrome {
            CaptionHeight = 32,
            ResizeBorderThickness = new Thickness(4),
            CornerRadius = new CornerRadius(0),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });
        KeyDown += (_, e) => {
            if (e.Key == Key.Enter)
                SaveButton_Click(this, new RoutedEventArgs());
        };

        // ── Push-to-talk: double-tap Ctrl routes speech to the focused TextBox ──
        PreviewKeyDown += (_, e) => {
            if (_startPtt is null) return;
            var action = _pttGesture.HandleKeyDown(e.Key, e.IsRepeat, DateTime.UtcNow);
            if (action != CtrlDoubleTapGestureAction.Triggered) return;
            if (Keyboard.FocusedElement is not TextBox tb) return;
            _pttActive = true;
            _startPtt(tb);
        };
        PreviewKeyUp += (_, e) => {
            if (!CtrlDoubleTapGestureTracker.IsCtrlKey(e.Key)) return;
            if (_pttActive) {
                _pttActive = false;
                _stopPtt?.Invoke();
                return;
            }
            _pttGesture.HandleKeyUp(e.Key, DateTime.UtcNow);
        };
        Closed += (_, _) => {
            if (_pttActive) {
                _pttActive = false;
                _stopPtt?.Invoke();
            }
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

        _speechLanguageComboBox = new ComboBox { Height = 30, Margin = new Thickness(0, 4, 0, 0) };
        _speechLanguageComboBox.SetResourceReference(StyleProperty, "ThemedComboBoxStyle");
        foreach (var (display, locale) in SpeechLanguageOptions)
        {
            var item = new ComboBoxItem { Content = display, Tag = locale };
            item.SetResourceReference(ComboBoxItem.ForegroundProperty, "LabelText");
            _speechLanguageComboBox.Items.Add(item);
        }
        var savedLocale = currentSettings.SpeechLanguage;
        var matchingItem = _speechLanguageComboBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Tag as string, savedLocale, StringComparison.OrdinalIgnoreCase));
        _speechLanguageComboBox.SelectedItem = matchingItem ?? _speechLanguageComboBox.Items[0];

        var isOpenAi = currentSettings.SpeechProvider == SpeechProvider.OpenAI;
        _azureSpeechRadio = new RadioButton {
            Content = "Azure Cognitive Services",
            GroupName = "SpeechProvider",
            FontSize = (double)Application.Current.Resources["FontSizeNormal"],
            Margin = new Thickness(0, 0, 0, 6),
            IsChecked = !isOpenAi
        };
        _azureSpeechRadio.SetResourceReference(Control.ForegroundProperty, "LabelText");

        _openAiSpeechRadio = new RadioButton {
            Content = "OpenAI Whisper",
            GroupName = "SpeechProvider",
            FontSize = (double)Application.Current.Resources["FontSizeNormal"],
            Margin = new Thickness(0, 0, 0, 6),
            IsChecked = isOpenAi
        };
        _openAiSpeechRadio.SetResourceReference(Control.ForegroundProperty, "LabelText");

        _pttAutoSendRadio = new RadioButton {
            Content = "Send/queue my spoken prompt immediately",
            GroupName = "PttBehavior",
            IsChecked = currentSettings.PttAutoSend,
            Margin = new Thickness(0, 0, 0, 2)
        };
        _pttAutoSendRadio.SetResourceReference(Control.ForegroundProperty, "LabelText");
        _pttDoNothingRadio = new RadioButton {
            Content = "Do nothing",
            GroupName = "PttBehavior",
            IsChecked = !currentSettings.PttAutoSend,
            Margin = new Thickness(0, 0, 0, 2)
        };
        _pttDoNothingRadio.SetResourceReference(Control.ForegroundProperty, "LabelText");

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
        _soundPromptCompleteCheckBox.ToolTip        = ToolTipHelper.MakeThemedToolTip("Plays when a prompt finishes successfully.");
        _soundPromptErrorCheckBox.ToolTip           = ToolTipHelper.MakeThemedToolTip("Plays when a prompt fails with an error.");
        _soundApprovalNeededCheckBox.ToolTip        = ToolTipHelper.MakeThemedToolTip("Plays when an item is added to the Approvals panel.");
        _soundQueueEmptyCheckBox.ToolTip            = ToolTipHelper.MakeThemedToolTip("Plays when the prompt queue drains to empty.");
        _soundLoopIterationCompleteCheckBox.ToolTip = ToolTipHelper.MakeThemedToolTip("Plays at the end of each loop iteration.");
        _soundLoopStoppedCheckBox.ToolTip           = ToolTipHelper.MakeThemedToolTip("Plays when the loop stops entirely.");
        _soundCommitMadeCheckBox.ToolTip            = ToolTipHelper.MakeThemedToolTip("Plays when a git commit is made by an agent.");
        _soundQuickRepliesShownCheckBox.ToolTip     = ToolTipHelper.MakeThemedToolTip("Plays when quick reply buttons appear in the transcript.");

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

        var useCustomModelProvider = currentSettings.ModelProvider == ModelProvider.Custom;
        _githubCopilotProviderRadio = new RadioButton {
            Content = "GitHub Copilot",
            GroupName = "ModelProvider",
            IsChecked = !useCustomModelProvider,
            Margin = new Thickness(0, 0, 0, 6)
        };
        _githubCopilotProviderRadio.SetResourceReference(Control.ForegroundProperty, "LabelText");
        _customModelProviderRadio = new RadioButton {
            Content = "Custom Model",
            GroupName = "ModelProvider",
            IsChecked = useCustomModelProvider,
            Margin = new Thickness(0, 0, 0, 6)
        };
        _customModelProviderRadio.SetResourceReference(Control.ForegroundProperty, "LabelText");

        _copilotModelComboBox = new ComboBox {
            Height = 30,
            Margin = new Thickness(0, 0, 0, 12),
            IsEditable = true,
            IsTextSearchEnabled = true
        };
        _copilotModelComboBox.SetResourceReference(StyleProperty, "ThemedEditableComboBoxStyle");
        foreach (var model in KnownCopilotModelOptions)
            _copilotModelComboBox.Items.Add(model);
        var savedCopilotModel = ApplicationSettingsSnapshot.NormalizeCopilotDefaultModel(currentSettings.CopilotDefaultModel);
        if (!KnownCopilotModelOptions.Contains(savedCopilotModel, StringComparer.OrdinalIgnoreCase))
            _copilotModelComboBox.Items.Add(savedCopilotModel);
        SelectCopilotModel(savedCopilotModel);
        _copilotModelStatusText = new TextBlock {
            TextWrapping = TextWrapping.Wrap,
            FontSize = (double)Application.Current.Resources["FontSizeSmall"],
            Margin = new Thickness(0, -8, 0, 12)
        };
        _copilotModelStatusText.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");

        _githubCopilotModelPanel = new StackPanel {
            Visibility = useCustomModelProvider ? Visibility.Collapsed : Visibility.Visible
        };
        _customModelProviderPanel = new StackPanel {
            Visibility = useCustomModelProvider ? Visibility.Visible : Visibility.Collapsed
        };

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
            Height = 30
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
        _byokOfflineModeCheckBox = new CheckBox {
            Content = "Offline mode (sets COPILOT__OFFLINE=true — skips GitHub auth)",
            IsChecked = currentSettings.ByokOfflineMode,
            Margin = new Thickness(0, 10, 0, 0)
        };
        _byokOfflineModeCheckBox.SetResourceReference(ForegroundProperty, "BodyText");
        _byokTestStatusText = new TextBlock {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            FontSize = (double)Application.Current.Resources["FontSizeSmall"]
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
            FontSize = (double)Application.Current.Resources["FontSizeSmall"],
            FontFamily = new System.Windows.Media.FontFamily("Consolas")
        };
        _ntfyUrlText.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");

        _notifyAiTurnCheckBox = MakeCheckBox("AI turn completes", GetToggle(currentSettings, "assistant_turn_complete", true));
        _notifyGitCommitCheckBox = MakeCheckBox("Git commit pushed (agent-authored only)", GetToggle(currentSettings, "git_commit_pushed", false));
        _notifyLoopIterationCheckBox = MakeCheckBox("Loop iteration completes", GetToggle(currentSettings, "loop_iteration_complete", false));
        _notifyLoopStoppedCheckBox = MakeCheckBox("Loop stopped", GetToggle(currentSettings, "loop_stopped", true));
        _notifyRcEstablishedCheckBox = MakeCheckBox("Remote connection established", GetToggle(currentSettings, "rc_connection_established", false));
        _notifyRcDroppedCheckBox = MakeCheckBox("Remote connection dropped", GetToggle(currentSettings, "rc_connection_dropped", true));

        // ── Window skeleton ───────────────────────────────────────────────

        var root = new DockPanel();
        Content = root;

        // Title bar
        var titleBar = new Grid { Height = 32 };
        titleBar.SetResourceReference(Grid.BackgroundProperty, "ChromeSurface");
        DockPanel.SetDock(titleBar, Dock.Top);

        var titleLayout = new DockPanel();
        titleBar.Children.Add(titleLayout);

        var closeBtn = new Button { Width = 46, Height = 32, FontSize = 13, Content = "✕" };
        closeBtn.SetResourceReference(Button.StyleProperty, "CaptionCloseButtonStyle");
        closeBtn.Click += (_, _) => Close();
        WindowChrome.SetIsHitTestVisibleInChrome(closeBtn, true);
        DockPanel.SetDock(closeBtn, Dock.Right);
        titleLayout.Children.Add(closeBtn);

        var titleText = new TextBlock {
            Text = "Preferences",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        titleText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        titleLayout.Children.Add(titleText);

        titleBar.MouseLeftButtonDown += (_, _) => DragMove();
        root.Children.Add(titleBar);

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
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(165) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(body);

        var navStrip = new Border {
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(0, 8, 0, 0)
        };
        navStrip.SetResourceReference(Border.BackgroundProperty, "SidebarPanelSurface");
        navStrip.SetResourceReference(Border.BorderBrushProperty, "SidebarPanelBorder");
        Grid.SetColumn(navStrip, 0);
        body.Children.Add(navStrip);

        _pageHost = new ContentControl();
        Grid.SetColumn(_pageHost, 1);
        body.Children.Add(_pageHost);

        // ── Build pages ───────────────────────────────────────────────────

        _hintsOptionsPage = new HintsOptionsPage();
        _hintsOptionsPage.Initialize(currentSettings);

        var pageList = new List<(string label, UIElement page)> {
            ("General",           BuildGeneralPage()),
            ("Provider",          BuildSpeechProviderPage()),
            ("Push to Talk",      BuildPushToTalkPage()),
            ("Replacements",      BuildTextReplacementsPage()),
            ("Remote Access",     BuildRemoteAccessPage()),
            ("Model",             BuildByokPage()),
            ("Notifications",     BuildNotificationsPage(currentSettings)),
            ("Sound Alerts",      BuildSoundsPage(currentSettings)),
            ("TTS Provider",      BuildTtsProviderPage(currentSettings)),
            ("Commands",          BuildAiPage()),
            ("Hints",             WrapInScrollViewer(BuildHintsPage())),
        };


        _pages = new UIElement[pageList.Count];
        for (int i = 0; i < pageList.Count; i++)
            _pages[i] = pageList[i].page;

        // ── Build grouped TreeView nav ────────────────────────────────────

        var (navTree, leafItems) = BuildNavTree(pageList);
        _leafItems = leafItems;
        navStrip.Child = navTree;

        NavigateTo(Math.Min(currentSettings.Preferences_LastPage, _pages.Length - 1));
        UpdateQrCode();
        Loaded += async (_, _) => await LoadCopilotModelsIntoComboBoxAsync();
    }

    private void NavigateTo(int index) {
        _currentPage = index;
        _pageHost.Content = _pages[index];
        if (_leafItems.TryGetValue(index, out var leafItem) && !leafItem.IsSelected)
            leafItem.IsSelected = true;
        _settingsStore.SavePreferencesLastPage(index);
    }

    // ── TreeView nav ──────────────────────────────────────────────────────

    private (TreeView tree, Dictionary<int, TreeViewItem> leafItems) BuildNavTree(
        List<(string label, UIElement page)> pageList) {

        var tree = new TreeView {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FocusVisualStyle = null,
        };
        // Suppress the TreeView's own focus border
        tree.SetValue(VirtualizingStackPanel.IsVirtualizingProperty, false);

        var leafItems = new Dictionary<int, TreeViewItem>();
        var pageIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < pageList.Count; i++)
            pageIndex[pageList[i].label] = i;

        var groupStyle = CreateGroupItemStyle();
        var leafStyle  = CreateLeafItemStyle();

        TreeViewItem MakeLeaf(string label) {
            if (!pageIndex.TryGetValue(label, out int idx))
                return new TreeViewItem();          // placeholder — never reached
            var item = new TreeViewItem { Style = leafStyle };
            item.Header = label;
            item.Selected += (_, _) => NavigateTo(idx);
            leafItems[idx] = item;
            return item;
        }

        TreeViewItem MakeGroup(string label, params string[] children) {
            var groupItem = new TreeViewItem { Style = groupStyle, IsExpanded = true };
            groupItem.Header = label;
            groupItem.Selected += (s, e) => {
                var item = (TreeViewItem)s;
                item.IsSelected = false;
                // Only toggle expansion when the group header itself was clicked,
                // not when a child leaf's Selected event bubbles up.
                if (ReferenceEquals(e.Source, item))
                    item.IsExpanded = !item.IsExpanded;
            };
            foreach (var child in children)
                if (pageIndex.ContainsKey(child))
                    groupItem.Items.Add(MakeLeaf(child));
            return groupItem;
        }

        // General always first; Dev / Diag. always last
        foreach (var standalone in new[] { "General" })
            if (pageIndex.ContainsKey(standalone))
                tree.Items.Add(MakeLeaf(standalone));

        tree.Items.Add(MakeGroup("Voice & Speech", "Provider", "Push to Talk", "Replacements"));
        tree.Items.Add(MakeGroup("Sound",          "Sound Alerts", "TTS Provider"));
        tree.Items.Add(MakeGroup("AI",             "Commands", "Model"));
        tree.Items.Add(MakeGroup("Connectivity",   "Remote Access", "Notifications"));
        tree.Items.Add(MakeGroup("Discoverability", "Hints"));

        foreach (var standalone in new[] { "Dev / Diag." })
            if (pageIndex.ContainsKey(standalone))
                tree.Items.Add(MakeLeaf(standalone));

        return (tree, leafItems);
    }

    private static Style CreateGroupItemStyle() {
        var style = new Style(typeof(TreeViewItem));
        style.Setters.Add(new Setter(FrameworkElement.OverridesDefaultStyleProperty, true));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));

        var tpl = new ControlTemplate(typeof(TreeViewItem));

        // Root: vertical stack — header row then children
        var root = new FrameworkElementFactory(typeof(StackPanel));
        root.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);

        // Header border
        var headerBorder = new FrameworkElementFactory(typeof(Border));
        headerBorder.Name = "HeaderBd";
        headerBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        headerBorder.SetValue(Border.PaddingProperty, new Thickness(10, 8, 10, 5));
        headerBorder.SetValue(Border.CursorProperty, Cursors.Hand);

        // Arrow + label in a horizontal panel
        var headerRow = new FrameworkElementFactory(typeof(StackPanel));
        headerRow.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var arrow = new FrameworkElementFactory(typeof(TextBlock));
        arrow.Name = "ArrowGlyph";
        arrow.SetValue(TextBlock.TextProperty, "▼");
        arrow.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        arrow.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 5, 0));
        arrow.SetValue(TextBlock.FontSizeProperty, 10.0);
        arrow.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");

        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        cp.SetValue(TextElement.FontWeightProperty, FontWeights.SemiBold);
        cp.SetResourceReference(TextElement.ForegroundProperty, "LabelText");
        cp.SetResourceReference(TextElement.FontSizeProperty, "FontSizeNormal");

        headerRow.AppendChild(arrow);
        headerRow.AppendChild(cp);
        headerBorder.AppendChild(headerRow);

        // Children area
        var itemsHost = new FrameworkElementFactory(typeof(ItemsPresenter));
        itemsHost.Name = "ItemsHost";

        root.AppendChild(headerBorder);
        root.AppendChild(itemsHost);
        tpl.VisualTree = root;

        // Collapsed: hide items + flip arrow to ▸
        var collapseTrigger = new Trigger { Property = TreeViewItem.IsExpandedProperty, Value = false };
        collapseTrigger.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed, "ItemsHost"));
        collapseTrigger.Setters.Add(new Setter(TextBlock.TextProperty, "▶", "ArrowGlyph"));
        tpl.Triggers.Add(collapseTrigger);

        // Header hover highlight
        var groupHoverTrigger = new MultiTrigger();
        groupHoverTrigger.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
        groupHoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("HoverSurface"), "HeaderBd"));
        tpl.Triggers.Add(groupHoverTrigger);

        style.Setters.Add(new Setter(TreeViewItem.TemplateProperty, tpl));
        return style;
    }

    private static Style CreateLeafItemStyle() {
        var style = new Style(typeof(TreeViewItem));
        style.Setters.Add(new Setter(FrameworkElement.OverridesDefaultStyleProperty, true));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
        // Default foreground on the TreeViewItem so ContentPresenter can inherit it.
        // Do NOT set foreground locally on the ContentPresenter — that would block
        // the selected-trigger's ImportantText from propagating via inheritance.
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("BodyText")));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Normal));

        var tpl = new ControlTemplate(typeof(TreeViewItem));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.PaddingProperty, new Thickness(28, 9, 14, 9));

        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        cp.SetResourceReference(TextElement.FontSizeProperty, "FontSizeNormal");
        // Foreground and FontWeight are inherited from the TreeViewItem — no local value here.

        border.AppendChild(cp);
        tpl.VisualTree = border;

        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("HoverSurface"), "Bd"));
        tpl.Triggers.Add(hoverTrigger);

        var selectedTrigger = new Trigger { Property = TreeViewItem.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("ActivePanelSurface"), "Bd"));
        selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("ImportantText")));
        selectedTrigger.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        tpl.Triggers.Add(selectedTrigger);

        style.Setters.Add(new Setter(TreeViewItem.TemplateProperty, tpl));
        return style;
    }

    private UIElement BuildGeneralPage() {
        var form = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };

        AddLabel(form, "User Name (appears in the Transcript, before user prompts)");
        form.Children.Add(_userNameBox);

        return WrapInScrollViewer(form);
    }

    private UIElement BuildSpeechProviderPage() {
        var form = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };

        AddSectionHeader(form, "Speech Provider");

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
            FontSize = (double)Application.Current.Resources["FontSizeSmall"],
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

        // ── Recognition Language ───────────────────────────────────────────
        AddSectionHeader(form, "Recognition Language", topMargin: 24);

        var langHint = new TextBlock {
            Text = "Select the spoken language. Azure uses the full locale (e.g. fr-FR); Whisper uses the language prefix (e.g. fr). Auto-detect works for both providers but is slightly slower.",
            FontSize = (double)Application.Current.Resources["FontSizeSmall"],
            Margin = new Thickness(0, 0, 0, 6),
            TextWrapping = TextWrapping.Wrap
        };
        langHint.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        form.Children.Add(langHint);
        form.Children.Add(_speechLanguageComboBox);

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

        return WrapInScrollViewer(form);
    }

    private UIElement BuildPushToTalkPage() {
        var form = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };

        // ── Push-to-talk ──────────────────────────────────────────────────
        AddSectionHeader(form, "Push-to-talk");

        var pttHint = new TextBlock { FontSize = (double)Application.Current.Resources["FontSizeSmall"], Margin = new Thickness(0, 4, 0, 4), TextWrapping = TextWrapping.Wrap };
        pttHint.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        pttHint.Inlines.Add(new Run("Double-tap and hold the "));
        pttHint.Inlines.Add(new Bold(new Run("Ctrl")));
        pttHint.Inlines.Add(new Run(" key to use PTT."));
        form.Children.Add(pttHint);

        AddLabel(form, "When I release the Ctrl key in the prompt text box (after I'm done speaking):");

        var pttStack = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

        pttStack.Children.Add(_pttAutoSendRadio);

        var autoSendSubHint = new TextBlock { FontSize = (double)Application.Current.Resources["FontSizeSmall"], Margin = new Thickness(20, 2, 0, 8), TextWrapping = TextWrapping.Wrap };
        autoSendSubHint.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        autoSendSubHint.Inlines.Add(new Run("(unless I'm using voice to modify "));
        autoSendSubHint.Inlines.Add(new Bold(new Run("existing")));
        autoSendSubHint.Inlines.Add(new Run(" text or I tap the "));
        autoSendSubHint.Inlines.Add(new Bold(new Run("Shift")));
        autoSendSubHint.Inlines.Add(new Run(" key while speaking)"));
        pttStack.Children.Add(autoSendSubHint);

        pttStack.Children.Add(_pttDoNothingRadio);

        var doNothingSubHint = new TextBlock { FontSize = (double)Application.Current.Resources["FontSizeSmall"], Margin = new Thickness(20, 2, 0, 8), TextWrapping = TextWrapping.Wrap };
        doNothingSubHint.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        doNothingSubHint.Inlines.Add(new Run("(I'll press "));
        doNothingSubHint.Inlines.Add(new Bold(new Run("Enter")));
        doNothingSubHint.Inlines.Add(new Run(" to send/queue the prompt when I'm ready)"));
        pttStack.Children.Add(doNothingSubHint);

        form.Children.Add(pttStack);

        _pttAutoSendRadio.Checked += (_, _) => _settingsStore.SavePttAutoSend(true);
        _pttDoNothingRadio.Checked += (_, _) => _settingsStore.SavePttAutoSend(false);

        return WrapInScrollViewer(form);
    }

    private UIElement BuildTextReplacementsPage() {
        var form = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };

        // ── Voice Text Replacements ───────────────────────────────────────
        AddSectionHeader(form, "Voice Text Replacements");
        AddLabel(form, "Pattern (regex) → Replacement — applied to every voice phrase in order.", topMargin: 4);

        var gridHint = new TextBlock {
            Text = "Double-click a cell to edit. Changes are saved automatically.",
            FontSize = (double)Application.Current.Resources["FontSizeSmall"],
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
            FontSize = (double)Application.Current.Resources["FontSizeSmall"],
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

        AddSectionHeader(form, "Model");

        var hint = new TextBlock {
            Text = "Choose the AI provider used by Squad Dash. GitHub Copilot uses your Copilot account and can be pinned to a specific model; Custom Model uses your own compatible provider settings.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = (double)Application.Current.Resources["FontSizeSmall"],
            Margin = new Thickness(0, 0, 0, 12)
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        form.Children.Add(hint);

        AddLabel(form, "Provider:");
        form.Children.Add(_githubCopilotProviderRadio);
        form.Children.Add(_customModelProviderRadio);

        AddLabel(_githubCopilotModelPanel, "Default Model:", topMargin: 8);
        _githubCopilotModelPanel.Children.Add(_copilotModelComboBox);
        _copilotModelStatusText.Text = "Use auto to let GitHub Copilot choose. Loading available models...";
        _githubCopilotModelPanel.Children.Add(_copilotModelStatusText);
        form.Children.Add(_githubCopilotModelPanel);

        var byokDevWarning = new TextBlock {
            Text = "Custom provider support is experimental. Use an OpenAI-compatible endpoint such as Ollama /v1.",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.Bold,
            FontSize = (double)Application.Current.Resources["FontSizeSmall"],
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00)),
            Margin = new Thickness(0, 8, 0, 12)
        };
        _customModelProviderPanel.Children.Add(byokDevWarning);

        AddLabel(_customModelProviderPanel, "Provider URL:");
        _customModelProviderPanel.Children.Add(_byokProviderUrlBox);

        var urlHint = new TextBlock {
            Text = "e.g. http://localhost:11434/v1 or http://localhost:11434",
            FontSize = (double)Application.Current.Resources["FontSizeSmall"],
            Margin = new Thickness(0, 3, 0, 12)
        };
        urlHint.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        _customModelProviderPanel.Children.Add(urlHint);

        AddLabel(_customModelProviderPanel, "Model:");
        var modelRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        modelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        modelRow.Children.Add(_byokModelBox);

        var byokProbeButton = new Button {
            Content = "Models...",
            Width = 86,
            Height = 30,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 4, 10, 4)
        };
        byokProbeButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        byokProbeButton.Click += ByokProbeButton_Click;
        Grid.SetColumn(byokProbeButton, 1);
        modelRow.Children.Add(byokProbeButton);
        _customModelProviderPanel.Children.Add(modelRow);

        AddLabel(_customModelProviderPanel, "Provider Type:");
        _customModelProviderPanel.Children.Add(_byokProviderTypeComboBox);

        AddLabel(_customModelProviderPanel, "API Key (optional):");
        var byokApiKeyHost = new Grid();
        byokApiKeyHost.Children.Add(_byokApiKeyPasswordBox);
        byokApiKeyHost.Children.Add(_byokApiKeyRevealBox);
        _customModelProviderPanel.Children.Add(byokApiKeyHost);

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
        _customModelProviderPanel.Children.Add(revealByokLink);
        _customModelProviderPanel.Children.Add(_byokOfflineModeCheckBox);

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
        _customModelProviderPanel.Children.Add(byokTestPanel);

        form.Children.Add(_customModelProviderPanel);

        _githubCopilotProviderRadio.Checked += (_, _) => UpdateModelProviderSectionVisibility();
        _customModelProviderRadio.Checked += (_, _) => UpdateModelProviderSectionVisibility();

        return WrapInScrollViewer(form);
    }

    private void UpdateModelProviderSectionVisibility() {
        var useCustomModelProvider = _customModelProviderRadio.IsChecked == true;
        _githubCopilotModelPanel.Visibility = useCustomModelProvider ? Visibility.Collapsed : Visibility.Visible;
        _customModelProviderPanel.Visibility = useCustomModelProvider ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadCopilotModelsIntoComboBoxAsync() {
        var currentModel = ReadCopilotDefaultModelInput();
        try {
            var modelIds = await FetchCopilotModelIdsAsync();
            if (modelIds.Count == 0)
                throw new InvalidOperationException("The Copilot API returned no models.");

            PopulateCopilotModelOptions(modelIds, currentModel);
            _copilotModelStatusText.Text = "Use auto to let GitHub Copilot choose. Model list loaded from GitHub Copilot.";
        }
        catch (Exception ex) {
            PopulateCopilotModelOptions(KnownCopilotModelOptions, currentModel);
            _copilotModelStatusText.Text = $"Use auto to let GitHub Copilot choose. Using the built-in list because live model discovery failed: {ex.Message}";
            SquadDashTrace.Write("Preferences", $"Copilot model discovery failed: {ex.Message}");
        }
    }

    private void PopulateCopilotModelOptions(IEnumerable<string> models, string selectedModel) {
        var options = models
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Select(model => model.Trim())
            .Prepend(ApplicationSettingsSnapshot.DefaultCopilotModel)
            .Append(selectedModel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(model => string.Equals(model, ApplicationSettingsSnapshot.DefaultCopilotModel, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(model => model, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _copilotModelComboBox.Items.Clear();
        foreach (var model in options)
            _copilotModelComboBox.Items.Add(model);

        SelectCopilotModel(selectedModel);
    }

    private void SelectCopilotModel(string model) {
        var normalizedModel = ApplicationSettingsSnapshot.NormalizeCopilotDefaultModel(model);
        foreach (var item in _copilotModelComboBox.Items) {
            if (string.Equals(item as string, normalizedModel, StringComparison.OrdinalIgnoreCase)) {
                _copilotModelComboBox.SelectedItem = item;
                _copilotModelComboBox.Text = item as string ?? normalizedModel;
                return;
            }
        }

        _copilotModelComboBox.Items.Add(normalizedModel);
        _copilotModelComboBox.SelectedItem = normalizedModel;
        _copilotModelComboBox.Text = normalizedModel;
    }

    private async Task<IReadOnlyList<string>> FetchCopilotModelIdsAsync() {
        var sdkDirectory = _workspacePaths.SquadSdkDirectory;
        var scriptPath = Path.Combine(sdkDirectory, "listModels.js");
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("Bundled model discovery script was not found.", scriptPath);

        using var process = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = ResolveNodeExecutablePath(),
                Arguments = "listModels.js",
                WorkingDirectory = sdkDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) {
            TryKillProcess(process);
            throw new TimeoutException("Timed out while asking GitHub Copilot for models.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                ? $"Model discovery exited with code {process.ExitCode}."
                : stderr.Trim());

        return ParseModelIds(stdout);
    }

    private static IReadOnlyList<string> ParseModelIds(string json) {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var models = new List<string>();
        foreach (var element in document.RootElement.EnumerateArray()) {
            string? id = element.ValueKind switch {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Object when element.TryGetProperty("id", out var idProperty) => idProperty.GetString(),
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(id))
                models.Add(id.Trim());
        }

        return models
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveNodeExecutablePath() {
        static IEnumerable<string> SplitPath(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? Enumerable.Empty<string>()
                : value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var candidates = new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe")
        };

        foreach (var candidate in candidates)
            if (File.Exists(candidate))
                return candidate;

        foreach (var scope in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine }) {
            foreach (var directory in SplitPath(Environment.GetEnvironmentVariable("PATH", scope))) {
                var nodePath = Path.Combine(directory.Trim('"'), "node.exe");
                if (File.Exists(nodePath))
                    return nodePath;
            }
        }

        return "node";
    }

    private static void TryKillProcess(Process process) {
        try {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch {
        }
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
            FontSize = (double)Application.Current.Resources["FontSizeSmall"],
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
            FontSize = (double)Application.Current.Resources["FontSizeSmall"],
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
        _soundPromptCompletePathBox.ToolTip        = ToolTipHelper.MakeThemedToolTip(ttsPathTip);
        _soundPromptErrorPathBox.ToolTip           = ToolTipHelper.MakeThemedToolTip(ttsPathTip);
        _soundApprovalNeededPathBox.ToolTip        = ToolTipHelper.MakeThemedToolTip(ttsPathTip);
        _soundQueueEmptyPathBox.ToolTip            = ToolTipHelper.MakeThemedToolTip(ttsPathTip);
        _soundLoopIterationCompletePathBox.ToolTip = ToolTipHelper.MakeThemedToolTip(ttsPathTip);
        _soundLoopStoppedPathBox.ToolTip           = ToolTipHelper.MakeThemedToolTip(ttsPathTip);
        _soundCommitMadePathBox.ToolTip            = ToolTipHelper.MakeThemedToolTip(ttsPathTip);
        _soundQuickRepliesShownPathBox.ToolTip     = ToolTipHelper.MakeThemedToolTip(ttsPathTip);

        return WrapInScrollViewer(form);
    }

    private UIElement BuildTtsProviderPage(ApplicationSettingsSnapshot currentSettings) {
        var form = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };

        AddSectionHeader(form, "Text-to-Speech");

        var ttsHint = new TextBlock {
            Text = "When a sound-event path is a quoted phrase like \"Done!\", SquadDash speaks it aloud using the TTS provider below.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = (double)Application.Current.Resources["FontSizeSmall"],
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
        ttsProviderCombo.SetResourceReference(StyleProperty, "ThemedComboBoxStyle");
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
        azureVoiceCombo.SetResourceReference(StyleProperty, "ThemedComboBoxStyle");
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
        openAiVoiceCombo.SetResourceReference(StyleProperty, "ThemedComboBoxStyle");
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
        openAiModelCombo.SetResourceReference(StyleProperty, "ThemedComboBoxStyle");
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
            azureVoiceCombo.ToolTip = ToolTipHelper.MakeThemedToolTip("Configure Azure Speech key and region to load the full voice list.");
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
        testButton.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");

        var ttsErrorText = new TextBlock {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = (double)Application.Current.Resources["FontSizeBody"]
        };
        ttsErrorText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorText");

        var copyErrorButton = new Button {
            Content = "📋 Copy error",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0)
        };
        copyErrorButton.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
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
        pathBox.ToolTip = ToolTipHelper.MakeThemedToolTip("Leave blank to use the default Windows sound. Right-click to test.");
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
                    try
                    {
                        var tts = TryBuildTtsProviderFromStore();
                        if (tts != null)
                            await tts.SpeakAsync(phrase);
                        else
                            await Dispatcher.InvokeAsync(() => System.Media.SystemSounds.Asterisk.Play());
                    }
                    catch (Exception ex)
                    {
                        SquadDashTrace.Write("TTS", $"SpeakAsync failed: {ex.Message}");
                    }
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
        ctxMenu.SetResourceReference(ContextMenu.StyleProperty, "ThemedContextMenuStyle");
        testMenuItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
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

    private UIElement BuildAiPage() {
        var form = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };

        AddSectionHeader(form, "Commands");

        AddLabel(form, "Quick Cleanup prompt (Ctrl+Shift+C):", wrap: true);
        form.Children.Add(_cleanupPromptBox);

        var hint = new TextBlock {
            Text = "This prompt is sent automatically when you press Ctrl+Shift+C with text selected in any markdown source editor. When the AI revision lands, the original selection is replaced with the cleaned-up version. You can work elsewhere in the document while waiting for AI to return.",
            FontSize = (double)Application.Current.Resources["FontSizeSmall"],
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
        var url = ByokProviderSettings.NormalizeProviderUrl(_byokProviderUrlBox.Text);
        if (string.IsNullOrEmpty(url)) {
            _byokTestStatusText.Text = "Enter a Provider URL first.";
            return;
        }
        _byokTestStatusText.Text = "Testing…";
        try {
            var apiKey = _byokApiKeyRevealBox.IsVisible ? _byokApiKeyRevealBox.Text : _byokApiKeyPasswordBox.Password;
            using var probeService = new ModelProviderProbeService();
            var models = await probeService.DiscoverModelsAsync(
                url,
                string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim());
            _byokProviderUrlBox.Text = url;

            if (models.Count > 0) {
                var names = string.Join(", ", models.Select(model => model.ModelId));
                _byokTestStatusText.Text = $"✅ Connected — {models.Count} model(s): {names}";
            }
            else {
                _byokTestStatusText.Text = "✅ Reachable (no models listed)";
            }
        }
        catch (Exception ex) {
            _byokTestStatusText.Text = $"❌ {ex.Message}";
        }
    }

    private async void ByokProbeButton_Click(object sender, RoutedEventArgs e) {
        var url = ByokProviderSettings.NormalizeProviderUrl(_byokProviderUrlBox.Text);
        if (string.IsNullOrEmpty(url)) {
            _byokTestStatusText.Text = "Enter a Provider URL first.";
            return;
        }

        var clickedButton = sender as Button;
        if (clickedButton is not null)
            clickedButton.IsEnabled = false;

        _byokTestStatusText.Text = "Discovering models...";
        try {
            var apiKey = _byokApiKeyRevealBox.IsVisible ? _byokApiKeyRevealBox.Text : _byokApiKeyPasswordBox.Password;
            using var probeService = new ModelProviderProbeService();
            var models = await probeService.DiscoverModelsAsync(
                url,
                string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim());
            _byokProviderUrlBox.Text = url;
            var providerWarning = await BuildModelProbeProviderWarningAsync(
                probeService,
                url,
                models);
            var canLoadFoundryModels = LooksLikeLocalFoundryProvider(url, models);
            var localStatus = canLoadFoundryModels
                ? await BuildModelProbeLocalStatusAsync(probeService)
                : null;

            var probeWindow = new ModelProviderProbeWindow(
                probeService,
                url,
                string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim(),
                models,
                providerWarning,
                _byokModelBox.Text,
                canLoadFoundryModels,
                localStatus) {
                Owner = this
            };

            if (probeWindow.ShowDialog() == true &&
                !string.IsNullOrWhiteSpace(probeWindow.SelectedModelId)) {
                _byokModelBox.Text = probeWindow.SelectedModelId;
                _byokTestStatusText.Text = $"Selected {probeWindow.SelectedModelId}.";
            }
            else {
                _byokTestStatusText.Text = $"{models.Count} model(s) discovered.";
            }
        }
        catch (Exception ex) {
            _byokTestStatusText.Text = $"Probe failed: {ex.Message}";
            UIErrorHelper.ShowError(
                "Model Probe",
                $"Unable to open the model probe.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                owner: this);
        }
        finally {
            if (clickedButton is not null)
                clickedButton.IsEnabled = true;
        }
    }

    private static async Task<ModelProviderLocalStatus> BuildModelProbeLocalStatusAsync(
        ModelProviderProbeService probeService) {
        IReadOnlyList<FoundryLoadedModel> loadedModels;
        var loadedModelsAvailable = true;
        try {
            loadedModels = await probeService.ListLoadedFoundryModelsAsync();
        }
        catch {
            loadedModelsAvailable = false;
            loadedModels = Array.Empty<FoundryLoadedModel>();
        }

        IReadOnlyList<LocalGpuMemoryInfo> gpuMemory;
        var gpuMemoryAvailable = true;
        try {
            gpuMemory = await probeService.GetNvidiaGpuMemoryAsync();
        }
        catch {
            gpuMemoryAvailable = false;
            gpuMemory = Array.Empty<LocalGpuMemoryInfo>();
        }

        return new ModelProviderLocalStatus(loadedModels, gpuMemory, loadedModelsAvailable, gpuMemoryAvailable);
    }

    private static async Task<ModelProviderProbeWarning?> BuildModelProbeProviderWarningAsync(
        ModelProviderProbeService probeService,
        string providerUrl,
        IReadOnlyList<ModelProviderProbeResult> models) {
        if (!LooksLikeFoundryProvider(providerUrl, models))
            return null;

        try {
            var foundry = await probeService.DiagnoseFoundryCliAsync();
            if (!foundry.HasConflict && !foundry.IsLegacyVersion)
                return null;

            var selectedVersion = foundry.SelectedVersion?.ToString() ?? "unknown";
            var message = foundry.HasConflict
                ? BuildFoundryWarningMessage(
                    "Foundry CLI conflict detected.",
                    foundry.Diagnostic)
                : BuildFoundryWarningMessage(
                    "Older Foundry CLI detected.",
                    foundry.Diagnostic);
            var guidance = BuildFoundryWarningGuidance(selectedVersion);
            return new ModelProviderProbeWarning(
                message,
                guidance,
                BuildCliFolders(foundry.Locations));
        }
        catch (Exception ex) {
            return new ModelProviderProbeWarning(
                $"Unable to verify the Foundry CLI installation: {ex.Message}",
                Guidance: null,
                Array.Empty<string>());
        }
    }

    private static string BuildFoundryWarningMessage(
        string heading,
        string diagnostic) {
        return string.Join(
            Environment.NewLine,
            heading,
            diagnostic);
    }

    private static string BuildFoundryWarningGuidance(string selectedVersion) {
        return string.Join(
            Environment.NewLine,
            "When conflicts are found, SquadDash will use the most recent version it can run.",
            $"Selected Foundry CLI version: {selectedVersion}.");
    }

    private static IReadOnlyList<string> BuildCliFolders(IReadOnlyList<FoundryCliLocation> locations) {
        var folders = new List<string>();
        foreach (var location in locations) {
            AddFolderForFile(folders, location.FileName);
            if (TryGetFoundryPackageInstallFolder(location, out var installFolder))
                folders.Add(installFolder);
        }

        foreach (var aliasFolder in BuildKnownFoundryAliasFolders())
            folders.Add(aliasFolder);

        return folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddFolderForFile(List<string> folders, string fileName) {
        if (!Path.IsPathRooted(fileName))
            return;

        var folder = Path.GetDirectoryName(fileName);
        if (!string.IsNullOrWhiteSpace(folder))
            folders.Add(folder);
    }

    private static bool TryGetFoundryPackageInstallFolder(
        FoundryCliLocation location,
        out string folder) {
        folder = string.Empty;
        var packageRoot = location.Source.Contains("Foundry Local CLI", StringComparison.OrdinalIgnoreCase)
            ? "Microsoft.FoundryLocalCLI"
            : location.Source.Contains("Foundry Local package", StringComparison.OrdinalIgnoreCase)
                ? "Microsoft.FoundryLocal"
                : string.Empty;
        if (string.IsNullOrWhiteSpace(packageRoot) || location.Version is null)
            return false;

        var windowsApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "WindowsApps");
        var versionText = location.Version.ToString();
        var exactCandidate = Path.Combine(
            windowsApps,
            $"{packageRoot}_{versionText}_x64__8wekyb3d8bbwe");
        if (Directory.Exists(exactCandidate)) {
            folder = exactCandidate;
            return true;
        }

        var versionParts = versionText.Split('.');
        if (versionParts.Length == 3) {
            var fourPartCandidate = Path.Combine(
                windowsApps,
                $"{packageRoot}_{versionText}.0_x64__8wekyb3d8bbwe");
            if (Directory.Exists(fourPartCandidate)) {
                folder = fourPartCandidate;
                return true;
            }

        }

        try {
            if (Directory.Exists(windowsApps)) {
                var pattern = $"{packageRoot}_{versionText}*_x64__8wekyb3d8bbwe";
                var match = Directory.EnumerateDirectories(windowsApps, pattern).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(match)) {
                    folder = match;
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) {
            return false;
        }

        return false;
    }

    private static IReadOnlyList<string> BuildKnownFoundryAliasFolders() {
        var localWindowsApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps");
        if (!Directory.Exists(localWindowsApps))
            return Array.Empty<string>();

        try {
            return Directory.EnumerateDirectories(localWindowsApps, "Microsoft.FoundryLocal*")
                .Where(folder => File.Exists(Path.Combine(folder, "foundry.exe")))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) {
            return Array.Empty<string>();
        }
    }

    private static bool LooksLikeFoundryProvider(
        string providerUrl,
        IReadOnlyList<ModelProviderProbeResult> models) {
        return providerUrl.Contains("5273", StringComparison.OrdinalIgnoreCase) ||
               models.Any(model =>
                   string.Equals(model.Owner, "Microsoft", StringComparison.OrdinalIgnoreCase) ||
                   model.ModelId.Contains("-cuda-gpu", StringComparison.OrdinalIgnoreCase) ||
                   model.ModelId.Contains("-generic-cpu", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeLocalFoundryProvider(
        string providerUrl,
        IReadOnlyList<ModelProviderProbeResult> models) {
        return LooksLikeFoundryProvider(providerUrl, models) &&
               Uri.TryCreate(providerUrl, UriKind.Absolute, out var uri) &&
               (uri.IsLoopback ||
                string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e) {
        var userName = _userNameBox.Text.Trim();
        var apiKey = _apiKeyRevealBox.IsVisible ? _apiKeyRevealBox.Text : _apiKeyPasswordBox.Password;
        var speechRegion = _speechRegionBox.Text.Trim();
        var updated = _settingsStore.SaveUserName(string.IsNullOrWhiteSpace(userName) ? null : userName);
        updated = _settingsStore.SaveSpeechRegion(string.IsNullOrWhiteSpace(speechRegion) ? null : speechRegion);
        updated = _settingsStore.SaveSpeechProvider(
            _openAiSpeechRadio.IsChecked == true ? SpeechProvider.OpenAI : SpeechProvider.Azure,
            string.IsNullOrWhiteSpace(_openAiSpeechKeyPasswordBox.Password.Trim()) ? null : _openAiSpeechKeyPasswordBox.Password.Trim());
        var speechLocale = (_speechLanguageComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        updated = _settingsStore.SaveSpeechLanguage(speechLocale);
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
        updated = _settingsStore.SaveModelSettings(
            _customModelProviderRadio.IsChecked == true ? ModelProvider.Custom : ModelProvider.GitHubCopilot,
            ReadCopilotDefaultModelInput());
        var byokProviderType = (_byokProviderTypeComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        var byokApiKey = _byokApiKeyRevealBox.IsVisible ? _byokApiKeyRevealBox.Text : _byokApiKeyPasswordBox.Password;
        updated = _settingsStore.SaveByokSettings(
            ByokProviderSettings.NormalizeProviderUrl(_byokProviderUrlBox.Text),
            string.IsNullOrWhiteSpace(_byokModelBox.Text.Trim()) ? null : _byokModelBox.Text.Trim(),
            byokProviderType,
            string.IsNullOrWhiteSpace(byokApiKey) ? null : byokApiKey,
            _byokOfflineModeCheckBox.IsChecked == true);
        updated = _settingsStore.SaveCleanupPrompt(_cleanupPromptBox.Text.Trim());
        SaveVoiceReplacementsNow();
        updated = _settingsStore.SaveHintSettings(_hintsOptionsPage.GetCurrentSettings());
        _onSaved(updated);
        Close();

        // SetEnvironmentVariable(EnvironmentVariableTarget.User) broadcasts WM_SETTINGCHANGE to all
        // top-level windows synchronously, which can block the UI thread for 10+ seconds.
        try
        {
            await Task.Run(() =>
                Environment.SetEnvironmentVariable("SQUAD_SPEECH_KEY", apiKey, EnvironmentVariableTarget.User));
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("Preferences", $"SetEnvironmentVariable failed: {ex.Message}");
        }
    }

    private string ReadCopilotDefaultModelInput() =>
        string.IsNullOrWhiteSpace(_copilotModelComboBox.Text)
            ? ApplicationSettingsSnapshot.DefaultCopilotModel
            : _copilotModelComboBox.Text.Trim();

    public static PreferencesWindow Open(
        Window? owner,
        ApplicationSettingsStore settingsStore,
        ApplicationSettingsSnapshot currentSettings,
        PushNotificationService pushNotificationService,
        IWorkspacePaths workspacePaths,
        bool showDevOptions,
        Action<ApplicationSettingsSnapshot> onSaved,
        Action<TextBox>? startPtt = null,
        Action? stopPtt = null,
        Action? startGuidedTour = null) {
        var window = new PreferencesWindow(settingsStore, currentSettings, pushNotificationService, workspacePaths, onSaved, showDevOptions, startPtt, stopPtt, startGuidedTour);
        if (owner != null)
            window.Owner = owner;
        window.Show();
        return window;
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

    private static readonly (string Display, string? Locale)[] SpeechLanguageOptions = [
        ("Auto-detect",              null),
        ("English (US) — en-US",    "en-US"),
        ("English (UK) — en-GB",    "en-GB"),
        ("French — fr-FR",          "fr-FR"),
        ("German — de-DE",          "de-DE"),
        ("Spanish (Spain) — es-ES", "es-ES"),
        ("Spanish (Mexico) — es-MX","es-MX"),
        ("Italian — it-IT",         "it-IT"),
        ("Portuguese (Brazil) — pt-BR", "pt-BR"),
        ("Portuguese (Portugal) — pt-PT", "pt-PT"),
        ("Dutch — nl-NL",           "nl-NL"),
        ("Russian — ru-RU",         "ru-RU"),
        ("Japanese — ja-JP",        "ja-JP"),
        ("Korean — ko-KR",          "ko-KR"),
        ("Chinese (Simplified) — zh-CN", "zh-CN"),
        ("Chinese (Traditional) — zh-TW", "zh-TW"),
        ("Arabic — ar-SA",          "ar-SA"),
        ("Hindi — hi-IN",           "hi-IN"),
        ("Polish — pl-PL",          "pl-PL"),
        ("Swedish — sv-SE",         "sv-SE"),
        ("Turkish — tr-TR",         "tr-TR"),
    ];

    private static void AddSectionHeader(Panel parent, string text, double topMargin = 0) {
        var header = new TextBlock {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            FontSize = (double)Application.Current.Resources["FontSizeMedium"],
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

    private UIElement BuildHintsPage()
    {
        var stack = new StackPanel { Margin = new Thickness(0) };
        stack.Children.Add(_hintsOptionsPage);

        var separator = new System.Windows.Shapes.Rectangle
        {
            Height = 1,
            Margin = new Thickness(20, 12, 20, 12),
        };
        separator.SetResourceReference(System.Windows.Shapes.Rectangle.FillProperty, "PanelBorder");
        stack.Children.Add(separator);

        var tourHeader = new TextBlock
        {
            Text       = "Guided Tours",
            FontWeight = FontWeights.SemiBold,
            Margin     = new Thickness(20, 0, 20, 6),
        };
        tourHeader.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        tourHeader.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
        stack.Children.Add(tourHeader);

        var tourDesc = new TextBlock
        {
            Text         = "Guided tours walk you through key features of SquadDash.",
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(20, 0, 20, 10),
        };
        tourDesc.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        tourDesc.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
        stack.Children.Add(tourDesc);

        var startTourButton = new Button
        {
            Content             = "Start a Guided Tour...",
            Height              = 28,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin              = new Thickness(20, 0, 0, 20),
            Padding             = new Thickness(10, 2, 10, 2),
        };
        startTourButton.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
        startTourButton.Click += (_, _) => _startGuidedTour?.Invoke();
        stack.Children.Add(startTourButton);

        return stack;
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
        try
        {
            await tempProvider.SendAsync("SquadDash Test", "Notifications are working!");
            _statusText.Text = "Test sent!";
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Send failed: {ex.Message}";
        }
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
