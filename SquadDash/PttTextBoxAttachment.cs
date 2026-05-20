using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// Attaches push-to-talk (PTT) voice dictation to any <see cref="TextBox"/> with minimal host code.
/// Handles the double-tap Ctrl gesture, selects the correct speech provider (Azure or OpenAI Whisper),
/// shows the floating <see cref="PushToTalkWindow"/>, and inserts recognized text with heuristics.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
///   // 1. Create once per host window:
///   _pttAttachment = new PttTextBoxAttachment(() => _settingsSnapshot, this, Dispatcher);
///
///   // 2. Wire key events (gate on focus check as needed):
///   PreviewKeyDown += (_, e) => { if (_pttAttachment.HandlePreviewKeyDown(e, _activeTextBox)) e.Handled = true; };
///   PreviewKeyUp   += (_, e) => _pttAttachment.HandlePreviewKeyUp(e);
///
///   // 3. Stop on commit / window close:
///   if (_pttAttachment.IsActive) _ = _pttAttachment.StopAsync();
///   Closed += (_, _) => _pttAttachment.Dispose();
/// </code>
/// </remarks>
internal sealed class PttTextBoxAttachment : IDisposable {
    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly Func<ApplicationSettingsSnapshot> _settingsProvider;
    private readonly Window    _ownerWindow;
    private readonly Dispatcher _dispatcher;

    // ── Per-session state ─────────────────────────────────────────────────────
    private ISpeechRecognitionService? _service;
    private PushToTalkWindow?          _pttWindow;
    private bool                       _stopOnCtrlRelease;
    private TextBox?                   _target;
    private int                        _caretIndex;
    private int                        _selectionLength;

    // ── Gesture tracker ───────────────────────────────────────────────────────
    private readonly CtrlDoubleTapGestureTracker _gesture =
        new(maxTapHoldMs: 250, doubleTapGapMs: 350);

    internal bool IsActive => _service is not null || _pttWindow is not null;

    // ── Constructor ────────────────────────────────────────────────────────────
    internal PttTextBoxAttachment(
        Func<ApplicationSettingsSnapshot> settingsProvider,
        Window ownerWindow,
        Dispatcher dispatcher) {
        _settingsProvider = settingsProvider;
        _ownerWindow      = ownerWindow;
        _dispatcher       = dispatcher;
    }

    // ── Key event handlers ────────────────────────────────────────────────────

    /// <summary>
    /// Call from the host window's PreviewKeyDown when <paramref name="activeTextBox"/> has keyboard focus.
    /// Returns <c>true</c> when the event was consumed — the caller should set <c>e.Handled = true</c>.
    /// </summary>
    internal bool HandlePreviewKeyDown(KeyEventArgs e, TextBox? activeTextBox) {
        if (activeTextBox is null) return false;

        var action = _gesture.HandleKeyDown(e.Key, e.IsRepeat, DateTime.UtcNow);
        if (action != CtrlDoubleTapGestureAction.Triggered) return false;

        if (_service is null) {
            _stopOnCtrlRelease = true;
            _ = StartAsync(activeTextBox);
        }
        else {
            _ = StopAsync();
        }
        return true;
    }

    /// <summary>
    /// Call from the host window's PreviewKeyUp.
    /// Returns <c>true</c> when the event was consumed (set <c>e.Handled = true</c>).
    /// </summary>
    internal bool HandlePreviewKeyUp(KeyEventArgs e) {
        if (!CtrlDoubleTapGestureTracker.IsCtrlKey(e.Key)) return false;

        if (_stopOnCtrlRelease && IsActive) {
            _ = StopAsync();
            return true;
        }

        _gesture.HandleKeyUp(e.Key, DateTime.UtcNow);
        return false;
    }

    // ── PTT session lifecycle ─────────────────────────────────────────────────

    internal async Task StartAsync(TextBox target) {
        var settings = _settingsProvider();
        string key, region;
        if (settings.SpeechProvider == SpeechProvider.OpenAI) {
            key    = settings.OpenAiSpeechApiKey ?? string.Empty;
            region = string.Empty;
        }
        else {
            key    = Environment.GetEnvironmentVariable("SQUAD_SPEECH_KEY", EnvironmentVariableTarget.User) ?? string.Empty;
            region = settings.SpeechRegion ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(key) ||
            (settings.SpeechProvider == SpeechProvider.Azure && string.IsNullOrWhiteSpace(region))) {
            _stopOnCtrlRelease = false;
            _gesture.Reset();
            return;
        }

        _target          = target;
        _caretIndex      = target.SelectionStart;
        _selectionLength = target.SelectionLength;

        _service = settings.SpeechProvider == SpeechProvider.OpenAI
            ? new WhisperSpeechRecognitionService()
            : new AzureSpeechRecognitionService();

        _service.PhraseRecognized += (_, text) =>
            _dispatcher.BeginInvoke(() => AppendSpeech(text));

        _service.VolumeChanged += (_, level) =>
            _dispatcher.BeginInvoke(DispatcherPriority.Render, () => {
                if (_pttWindow is not null)
                    _pttWindow.VolumeBar.Height = Math.Max(2, level * 36);
            });

        _service.RecognitionError += (_, _) =>
            _dispatcher.BeginInvoke(() => _ = StopAsync());

        try {
            System.Windows.Point physicalPt;
            try {
                var rect = target.GetRectFromCharacterIndex(Math.Max(0, _caretIndex - 1));
                physicalPt = target.PointToScreen(new System.Windows.Point(rect.Right, rect.Bottom));
            }
            catch {
                physicalPt = target.PointToScreen(new System.Windows.Point(0, target.ActualHeight + 4));
            }

            var physWa          = NativeMethods.GetWorkAreaForPhysicalPoint((int)physicalPt.X, (int)physicalPt.Y);
            var logicalPt       = DpiHelper.PhysicalToLogical(target, physicalPt);
            var logicalWaOrigin = DpiHelper.PhysicalToLogical(target, new System.Windows.Point(physWa.Left, physWa.Top));
            var logicalWaCorner = DpiHelper.PhysicalToLogical(target, new System.Windows.Point(physWa.Right, physWa.Bottom));
            var logicalWorkArea = new System.Windows.Rect(logicalWaOrigin, logicalWaCorner);

            _pttWindow = new PushToTalkWindow(_ownerWindow, showHint: false);
            _pttWindow.PositionUnderCaret(logicalPt, logicalWorkArea);
            _pttWindow.Show();
            target.Focus();

            await _service.StartAsync(key, region, language: settings.SpeechLanguage).ConfigureAwait(false);
        }
        catch {
            await _dispatcher.InvokeAsync(() => {
                _pttWindow?.Close();
                _pttWindow = null;
            });
            _service?.Dispose();
            _service           = null;
            _target            = null;
            _stopOnCtrlRelease = false;
            _gesture.Reset();
        }
    }

    internal async Task StopAsync() {
        await _dispatcher.InvokeAsync(() => {
            _pttWindow?.Close();
            _pttWindow = null;
        });

        var service = _service;
        _service = null;
        _target  = null;

        if (service is not null) {
            try { await service.StopAsync().ConfigureAwait(false); } catch { }
            service.Dispose();
        }

        _stopOnCtrlRelease = false;
        _gesture.Reset();
    }

    // ── Speech text insertion ─────────────────────────────────────────────────

    private void AppendSpeech(string text) {
        var tb = _target;
        if (tb is null) return;

        var current    = tb.Text;
        var caretIndex = Math.Min(_caretIndex, current.Length);
        var selLength  = _selectionLength;
        _selectionLength = 0; // only replace selection on first phrase
        var selEndIndex  = Math.Min(caretIndex + selLength, current.Length);
        var left         = current[..caretIndex];
        var right        = current[selEndIndex..];
        var prefix       = VoiceInsertionHeuristics.LeadingInsertionSpace(left, right);
        var processed    = VoiceInsertionHeuristics.Apply(left, text, right);
        var insert       = prefix + processed;
        var rules        = _settingsProvider().VoiceReplacementRules;
        var replaced     = rules.Count > 0
            ? prefix + VoiceInsertionHeuristics.ApplyReplacementRules(processed, rules)
            : insert;

        // Step 1: insert conditioned (pre-replacement) text
        tb.Select(caretIndex, selEndIndex - caretIndex);
        tb.SelectedText = insert;
        tb.Select(caretIndex + insert.Length, 0);
        _caretIndex = caretIndex + insert.Length;

        // Step 2: apply replacements as a separate undo entry
        if (!string.Equals(replaced, insert, StringComparison.Ordinal)) {
            tb.Select(caretIndex, insert.Length);
            tb.SelectedText = replaced;
            tb.Select(caretIndex + replaced.Length, 0);
            _caretIndex = caretIndex + replaced.Length;
        }
    }

    // ── IDisposable ────────────────────────────────────────────────────────────

    public void Dispose() {
        var service = _service;
        _service = null;
        service?.Dispose();

        _dispatcher.InvokeAsync(() => {
            _pttWindow?.Close();
            _pttWindow = null;
        });
    }
}
