using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SquadDash;

internal sealed class PushToTalkController : IDisposable {
    // ── State machine ──────────────────────────────────────────────────────
    private enum PttState { Idle, TapDown, TapReleased, Active }

    private const int PttMaxTapHoldMs   = 250;
    private const int PttDoubleClickTime = 350;

    // ── Injected dependencies ──────────────────────────────────────────────
    private readonly Func<ApplicationSettingsSnapshot> _settingsSnapshotProvider;
    private readonly TextBox            _promptTextBox;
    private readonly UIElement          _pushToTalkPanel;   // Grid in XAML
    private readonly FrameworkElement   _volumeBar;         // Border in XAML
    private readonly TextBlock          _voiceHintText;
    private readonly Dispatcher         _dispatcher;
    private readonly Action<string>     _onError;           // called with full display text
    private readonly Action             _onSendPrompt;      // wraps RunButton_Click guard
    private readonly Func<bool>         _isPromptRunning;

    // ── Instance state ─────────────────────────────────────────────────────
    private PttState                    _pttState = PttState.Idle;
    private bool                        _promptHasVoiceInput;
    private DateTime                    _ctrlFirstDownTime;
    private DateTime                    _ctrlFirstReleaseTime;
    private SpeechRecognitionService?   _speechService;
    private Func<IEnumerable<string>>?  _phraseHintsProvider;
    private bool                        _voiceStartedWithSendEnabled;
    private int                         _sessionCaretIndex;

    // ── Public surface ─────────────────────────────────────────────────────
    internal bool IsActive           => _pttState == PttState.Active;
    internal bool PromptHasVoiceInput => _promptHasVoiceInput;
    internal void ClearVoiceInput()  => _promptHasVoiceInput = false;

    // ── Constructor ────────────────────────────────────────────────────────
    internal PushToTalkController(
        Func<ApplicationSettingsSnapshot> settingsSnapshotProvider,
        TextBox           promptTextBox,
        UIElement         pushToTalkPanel,
        FrameworkElement  volumeBar,
        TextBlock         voiceHintText,
        Dispatcher        dispatcher,
        Action<string>    onError,
        Action            onSendPrompt,
        Func<bool>        isPromptRunning,
        Func<IEnumerable<string>>? phraseHintsProvider = null) {
        _settingsSnapshotProvider = settingsSnapshotProvider;
        _promptTextBox            = promptTextBox;
        _pushToTalkPanel          = pushToTalkPanel;
        _volumeBar                = volumeBar;
        _voiceHintText            = voiceHintText;
        _dispatcher               = dispatcher;
        _onError                  = onError;
        _onSendPrompt             = onSendPrompt;
        _isPromptRunning          = isPromptRunning;
        _phraseHintsProvider      = phraseHintsProvider;
    }

    // ── Key handling ───────────────────────────────────────────────────────
    /// <summary>
    /// Process a PreviewKeyDown event for PTT logic.
    /// Returns <c>true</c> when the event was fully handled (e.Handled should be set).
    /// The Escape/fullscreen guard in MainWindow should run <em>before</em> calling this.
    /// </summary>
    internal bool HandlePreviewKeyDown(Key key, bool isRepeat) {
        switch (_pttState) {
            case PttState.Idle:
                if (IsCtrlKey(key) && !isRepeat) {
                    _ctrlFirstDownTime = DateTime.UtcNow;
                    _pttState = PttState.TapDown;
                }
                break;

            case PttState.TapDown:
                if (IsCtrlKey(key)) {
                    // Still holding first Ctrl — check if held too long
                    if (isRepeat && (DateTime.UtcNow - _ctrlFirstDownTime).TotalMilliseconds > PttMaxTapHoldMs)
                        _pttState = PttState.Idle;
                }
                else {
                    // Any other key invalidates the sequence
                    _pttState = PttState.Idle;
                }
                break;

            case PttState.TapReleased:
                if (IsCtrlKey(key) && !isRepeat) {
                    var gapMs = (DateTime.UtcNow - _ctrlFirstReleaseTime).TotalMilliseconds;
                    if (gapMs <= PttDoubleClickTime) {
                        // Capture whether Send is enabled at the moment PTT starts
                        _voiceStartedWithSendEnabled = !_isPromptRunning();
                        _pttState = PttState.Active;
                        _ = StartPushToTalkAsync();
                    }
                    else {
                        // Too slow — treat as fresh first tap
                        _ctrlFirstDownTime = DateTime.UtcNow;
                        _pttState = PttState.TapDown;
                    }
                }
                else if (!IsCtrlKey(key)) {
                    _pttState = PttState.Idle;
                }
                break;

            case PttState.Active:
                if (IsCtrlKey(key) && isRepeat) {
                    // Still holding Ctrl — keep recording
                }
                else if (key == Key.Escape) {
                    _ = StopPushToTalkAsync(send: false);
                    return true; // handled — consume the Escape key
                }
                else if (!IsCtrlKey(key)) {
                    // Any other key disengages PTT (no send)
                    _ = StopPushToTalkAsync(send: false);
                }
                break;
        }

        return false;
    }

    /// <summary>Process a PreviewKeyUp event for PTT logic.</summary>
    internal void HandlePreviewKeyUp(Key key) {
        switch (_pttState) {
            case PttState.TapDown:
                if (IsCtrlKey(key)) {
                    var heldMs = (DateTime.UtcNow - _ctrlFirstDownTime).TotalMilliseconds;
                    if (heldMs <= PttMaxTapHoldMs) {
                        _ctrlFirstReleaseTime = DateTime.UtcNow;
                        _pttState = PttState.TapReleased;
                    }
                    else {
                        _pttState = PttState.Idle;
                    }
                }
                break;

            case PttState.Active:
                if (IsCtrlKey(key))
                    _ = StopPushToTalkAsync(send: _voiceStartedWithSendEnabled);
                break;
        }
    }

    // ── PTT session management ─────────────────────────────────────────────
    private async Task StartPushToTalkAsync() {
        var key    = Environment.GetEnvironmentVariable("SQUAD_SPEECH_KEY", EnvironmentVariableTarget.User);
        var region = _settingsSnapshotProvider().SpeechRegion;

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region)) {
            _pttState = PttState.Idle;
            return;
        }

        // Capture caret position before the panel becomes visible.
        // CaretIndex can change (or appear to reset) once layout shifts occur,
        // so we snapshot it here to guarantee a correct leftContext at recognition time.
        _sessionCaretIndex = _promptTextBox.CaretIndex;

        _pushToTalkPanel.Visibility = Visibility.Visible;
        _volumeBar.Height = 0;

        _speechService = new SpeechRecognitionService();

        _speechService.PhraseRecognized += (_, text) =>
            _dispatcher.BeginInvoke(() => AppendSpeechToPrompt(text));

        _speechService.VolumeChanged += (_, level) =>
            _dispatcher.BeginInvoke(() => _volumeBar.Height = Math.Max(2, level * 36));

        _speechService.RecognitionError += (_, msg) =>
            _dispatcher.BeginInvoke(() => {
                _ = StopPushToTalkAsync(send: false);
                _onError("[voice error] " + msg);
            });

        try {
            var phraseHints = _phraseHintsProvider?.Invoke();
            await _speechService.StartAsync(key, region, phraseHints).ConfigureAwait(false);
        }
        catch (Exception ex) {
            _dispatcher.Invoke(() => {
                _pttState = PttState.Idle;
                _pushToTalkPanel.Visibility = Visibility.Collapsed;
                _speechService?.Dispose();
                _speechService = null;
                _onError("[voice error] " + ex.Message);
            });
        }
    }

    private async Task StopPushToTalkAsync(bool send) {
        _pttState = PttState.Idle;
        _pushToTalkPanel.Visibility = Visibility.Collapsed;
        _volumeBar.Height = 0;

        var service = _speechService;
        _speechService = null;

        if (service != null) {
            try { await service.StopAsync().ConfigureAwait(false); }
            catch { }
            service.Dispose();
        }

        if (send) {
            await Task.Delay(220).ConfigureAwait(false);
            _dispatcher.Invoke(_onSendPrompt);
        }
    }

    // ── Speech text insertion ──────────────────────────────────────────────
    private void AppendSpeechToPrompt(string text) {
        _promptHasVoiceInput = true;
        var current      = _promptTextBox.Text;
        // Clamp in case text was externally modified since session start.
        var caretIndex   = Math.Min(_sessionCaretIndex, current.Length);
        var leftContext  = current[..caretIndex];
        var rightContext = current[caretIndex..];
        var prefix       = caretIndex > 0 && current[caretIndex - 1] != ' '
                               ? " "
                               : string.Empty;
        var processed = VoiceInsertionHeuristics.Apply(leftContext, text, rightContext);
        var insert    = prefix + processed;
        _promptTextBox.Text       = current[..caretIndex] + insert + current[caretIndex..];
        _promptTextBox.CaretIndex = caretIndex + insert.Length;
        _sessionCaretIndex        = caretIndex + insert.Length;
    }

    // ── UI hint ────────────────────────────────────────────────────────────
    internal void UpdateVoiceHintVisibility() {
        var hasKey    = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("SQUAD_SPEECH_KEY", EnvironmentVariableTarget.User));
        var hasRegion = !string.IsNullOrWhiteSpace(_settingsSnapshotProvider().SpeechRegion);
        _voiceHintText.Visibility = hasKey && hasRegion ? Visibility.Collapsed : Visibility.Visible;
    }

    // ── IDisposable ────────────────────────────────────────────────────────
    public void Dispose() {
        _speechService?.Dispose();
        _speechService = null;
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private static bool IsCtrlKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl;
}
