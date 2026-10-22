using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Document;
using VoiceHeuristics;

namespace SquadDash;

internal sealed partial class AvalonEditMarkdownEditor
{
    private readonly CtrlDoubleTapGestureTracker _dictationGesture = new(maxTapHoldMs: 250, doubleTapGapMs: 350);
    private readonly SemaphoreSlim _dictationTransitionGate = new(1, 1);
    private ISpeechRecognitionService? _dictationVoiceService;
    private PushToTalkWindow? _dictationPttWindow;
    private EventHandler<string>? _dictationPhraseHandler;
    private EventHandler<double>? _dictationVolumeHandler;
    private EventHandler<string>? _dictationErrorHandler;
    private bool _dictationInitialized;
    private bool _dictationDisposed;
    private bool _dictationStopOnCtrlRelease;
    private int _dictationCaretIndex;
    private int _dictationSelectionLength;
    private int _dictationSessionId;
    private long _dictationDispatchSequence;
    private bool _dictationStopping;
    private TextAnchor? _dictationInsertionAnchor;
    private AvalonEditRevisionSelection? _dictationSelection;

    private void InitializeDictation()
    {
        if (_dictationInitialized || _dictationDisposed)
            return;

        _dictationInitialized = true;
        SquadDashTrace.Write(TraceCategory.Avalon, "AVALON_DICTATION_LIFECYCLE event=initialize");
        Editor.TextArea.PreviewKeyDown += Dictation_PreviewKeyDown;
        Editor.TextArea.PreviewKeyUp += Dictation_PreviewKeyUp;
        Editor.TextArea.LostKeyboardFocus += Dictation_LostKeyboardFocus;
        Editor.TextArea.Unloaded += Dictation_TextAreaUnloaded;
        Unloaded += Dictation_ControlUnloaded;
        IsVisibleChanged += Dictation_IsVisibleChanged;
    }

    private void DisposeDictation()
    {
        if (_dictationDisposed)
            return;

        _dictationDisposed = true;
        _dictationInitialized = false;
        _dictationSessionId++;
        SquadDashTrace.Write(TraceCategory.Avalon, $"AVALON_DICTATION_LIFECYCLE event=dispose session={_dictationSessionId}");
        _dictationStopOnCtrlRelease = false;
        _dictationGesture.Reset();

        Editor.TextArea.PreviewKeyDown -= Dictation_PreviewKeyDown;
        Editor.TextArea.PreviewKeyUp -= Dictation_PreviewKeyUp;
        Editor.TextArea.LostKeyboardFocus -= Dictation_LostKeyboardFocus;
        Editor.TextArea.Unloaded -= Dictation_TextAreaUnloaded;
        Unloaded -= Dictation_ControlUnloaded;
        IsVisibleChanged -= Dictation_IsVisibleChanged;

        CloseDictationWindow();

        _ = StopDictationAsync();
    }

    private void Dictation_ControlUnloaded(object sender, RoutedEventArgs e)
    {
        _ = StopDictationAsync();
    }

    private void Dictation_TextAreaUnloaded(object sender, RoutedEventArgs e)
    {
        _ = StopDictationAsync();
    }

    private void Dictation_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (IsDictationActive)
            _ = StopDictationAsync();
    }

    private void Dictation_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false)
            _ = StopDictationAsync();
    }

    private void Dictation_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_dictationDisposed || _isDisposed || _dictationStopping)
            return;

        if (IsDictationActive)
        {
            if (e.Key == Key.Escape)
            {
                _ = StopDictationAsync();
                e.Handled = true;
                return;
            }

            if (!CtrlDoubleTapGestureTracker.IsCtrlKey(e.Key))
                _ = StopDictationAsync();
        }

        var action = _dictationGesture.HandleKeyDown(e.Key, e.IsRepeat, DateTime.UtcNow);
        if (action != CtrlDoubleTapGestureAction.Triggered)
            return;

        if (_dictationVoiceService is null)
        {
            _dictationStopOnCtrlRelease = true;
            _ = StartDictationAsync();
        }
        else
        {
            _ = StopDictationAsync();
        }

        e.Handled = true;
    }

    private void Dictation_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (_dictationDisposed || _isDisposed || !CtrlDoubleTapGestureTracker.IsCtrlKey(e.Key))
            return;

        if (_dictationStopOnCtrlRelease && IsDictationActive)
        {
            _ = StopDictationAsync();
            e.Handled = true;
            return;
        }

        _dictationGesture.HandleKeyUp(e.Key, DateTime.UtcNow);
    }

    private bool IsDictationActive => _dictationVoiceService is not null || _dictationPttWindow is not null;

    private async Task StartDictationAsync()
    {
        await _dictationTransitionGate.WaitAsync();
        try
        {
            if (_dictationDisposed || _isDisposed || !_dictationStopOnCtrlRelease || _dictationVoiceService is not null)
                return;

            var settings = _settingsStore.Load();
            string key;
            string region;
            if (settings.SpeechProvider == SpeechProvider.OpenAI)
            {
                key = settings.OpenAiSpeechApiKey ?? string.Empty;
                region = string.Empty;
            }
            else
            {
                key = Environment.GetEnvironmentVariable("SQUAD_SPEECH_KEY", EnvironmentVariableTarget.User) ?? string.Empty;
                region = settings.SpeechRegion ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(key) ||
                (settings.SpeechProvider == SpeechProvider.Azure && string.IsNullOrWhiteSpace(region)))
            {
                SquadDashTrace.Write(TraceCategory.UI, "Dictation requires speech credentials and a configured region in Preferences.");
                MessageBox.Show("Configure speech credentials and region in Preferences before using push-to-talk.",
                    "Dictation", MessageBoxButton.OK, MessageBoxImage.Information);
                _dictationStopOnCtrlRelease = false;
                _dictationGesture.Reset();
                return;
            }

            CaptureDictationSelection();
            if (!CanMutateSelection())
            {
                SquadDashTrace.Write(TraceCategory.UI, "Dictation cannot start inside an active revision.");
                _dictationStopOnCtrlRelease = false;
                _dictationGesture.Reset();
                return;
            }
            _dictationSessionId++;
            var sessionId = _dictationSessionId;
            SquadDashTrace.Write(
                TraceCategory.Avalon,
                $"AVALON_DICTATION_LIFECYCLE event=start session={sessionId} provider={settings.SpeechProvider} caret={_dictationCaretIndex} selection={_dictationSelectionLength} docLen={Editor.Document.TextLength}");

            ISpeechRecognitionService service = settings.SpeechProvider == SpeechProvider.OpenAI
                ? new WhisperSpeechRecognitionService()
                : new AzureSpeechRecognitionService();
            AttachDictationServiceHandlers(service, sessionId);
            _dictationVoiceService = service;

            try
            {
                ShowDictationWindow();
                await service.StartAsync(key, region, language: settings.SpeechLanguage);
            }
            catch (Exception ex) when (IsSpeechFailure(ex))
            {
                SquadDashTrace.Write(TraceCategory.UI, $"[voice error] AvalonEdit dictation start failed: {ex.Message}");
                await Dispatcher.InvokeAsync(() =>
                {
                    if (sessionId != _dictationSessionId)
                        return;
                    CloseDictationWindow();
                });
                DetachDictationServiceHandlers(service);
                _dictationVoiceService = null;
                service.Dispose();
                _dictationStopOnCtrlRelease = false;
                _dictationGesture.Reset();
            }
        }
        finally
        {
            _dictationTransitionGate.Release();
        }
    }

    private async Task StopDictationAsync()
    {
        if (_dictationStopping)
            return;
        _dictationStopping = true;
        var sessionId = _dictationSessionId;
        SquadDashTrace.Write(
            TraceCategory.Avalon,
            $"AVALON_DICTATION_LIFECYCLE event=stop-begin session={sessionId} callbackPriority={DispatcherPriority.Normal} cleanupPriority={DispatcherPriority.Background}");
        _dictationStopOnCtrlRelease = false;
        await _dictationTransitionGate.WaitAsync();
        try
        {
            _dictationStopOnCtrlRelease = false;
            _dictationGesture.Reset();
            CloseDictationWindow();

            var service = _dictationVoiceService;
            _dictationVoiceService = null;
            if (service is null)
                return;

            try
            {
                await service.StopAsync();
                // Drain the final recognition phrase before retiring this session (Whisper
                // commonly returns its entire transcript only when recording stops).
                SquadDashTrace.Write(
                    TraceCategory.Avalon,
                    $"AVALON_DICTATION_LIFECYCLE event=stop-drain-schedule session={sessionId} priority={DispatcherPriority.Background}");
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                SquadDashTrace.Write(
                    TraceCategory.Avalon,
                    $"AVALON_DICTATION_LIFECYCLE event=stop-drain-exec session={sessionId} priority={DispatcherPriority.Background}");
            }
            catch (ObjectDisposedException ex)
            {
                SquadDashTrace.Write(TraceCategory.UI, $"[voice error] AvalonEdit dictation stop disposed service: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                SquadDashTrace.Write(TraceCategory.UI, $"[voice error] AvalonEdit dictation stop invalid operation: {ex.Message}");
            }
            catch (Exception ex) when (IsSpeechFailure(ex))
            {
                SquadDashTrace.Write(TraceCategory.UI, $"[voice error] AvalonEdit dictation stop failed: {ex.Message}");
            }
            finally
            {
                _dictationSessionId++;
                SquadDashTrace.Write(
                    TraceCategory.Avalon,
                    $"AVALON_DICTATION_LIFECYCLE event=stop-finalize session={sessionId} retiredSession={_dictationSessionId}");
                DetachDictationServiceHandlers(service);
                service.Dispose();
            }
        }
        finally
        {
            _dictationStopping = false;
            _dictationTransitionGate.Release();
        }
    }

    private static bool IsSpeechFailure(Exception ex) =>
        ex is InvalidOperationException or System.IO.IOException or System.Net.Http.HttpRequestException
            or OperationCanceledException or System.Runtime.InteropServices.ExternalException
            or UnauthorizedAccessException or ArgumentException;

    internal void CaptureDictationSelection()
    {
        _dictationCaretIndex = Editor.SelectionStart;
        _dictationSelectionLength = Editor.SelectionLength;
        _dictationSelection = _dictationSelectionLength > 0
            ? new AvalonEditRevisionSelection(Editor.Document, _dictationCaretIndex, _dictationSelectionLength) : null;
        SquadDashTrace.Write(
            TraceCategory.Avalon,
            $"AVALON_DICTATION_CAPTURE session={_dictationSessionId} caret={_dictationCaretIndex} selection={_dictationSelectionLength} docLen={Editor.Document.TextLength}");
        SetDictationInsertionAnchor();
    }

    private void SetDictationInsertionAnchor()
    {
        _dictationInsertionAnchor = Editor.Document.CreateAnchor(_dictationCaretIndex);
        _dictationInsertionAnchor.MovementType = AnchorMovementType.AfterInsertion;
    }

    private void AttachDictationServiceHandlers(ISpeechRecognitionService service, int sessionId)
    {
        _dictationPhraseHandler = (_, text) =>
        {
            var dispatchId = Interlocked.Increment(ref _dictationDispatchSequence);
            SquadDashTrace.Write(
                TraceCategory.Avalon,
                $"AVALON_DICTATION_CALLBACK_SCHEDULE event=phrase session={sessionId} dispatch={dispatchId} priority={DispatcherPriority.Normal}");
            Dispatcher.BeginInvoke(() => AppendSpeechToEditor(text, sessionId, dispatchId));
        };

        _dictationVolumeHandler = (_, level) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
            {
                if (sessionId != _dictationSessionId || _dictationPttWindow is null)
                    return;
                _dictationPttWindow.VolumeBar.Height = Math.Max(2, level * 36);
            });

        _dictationErrorHandler = (_, message) =>
        {
            var dispatchId = Interlocked.Increment(ref _dictationDispatchSequence);
            SquadDashTrace.Write(
                TraceCategory.Avalon,
                $"AVALON_DICTATION_CALLBACK_SCHEDULE event=error session={sessionId} dispatch={dispatchId} priority={DispatcherPriority.Normal}");
            Dispatcher.BeginInvoke(() =>
            {
                SquadDashTrace.Write(
                    TraceCategory.Avalon,
                    $"AVALON_DICTATION_CALLBACK_EXEC event=error session={sessionId} dispatch={dispatchId} currentSession={_dictationSessionId}");
                if (sessionId != _dictationSessionId)
                    return;
                SquadDashTrace.Write(TraceCategory.UI, $"[voice error] AvalonEdit dictation recognition error: {message}");
                _ = StopDictationAsync();
            });
        };

        service.PhraseRecognized += _dictationPhraseHandler;
        service.VolumeChanged += _dictationVolumeHandler;
        service.RecognitionError += _dictationErrorHandler;
    }

    private void DetachDictationServiceHandlers(ISpeechRecognitionService? service)
    {
        if (service is null)
            return;
        if (_dictationPhraseHandler is not null)
            service.PhraseRecognized -= _dictationPhraseHandler;
        if (_dictationVolumeHandler is not null)
            service.VolumeChanged -= _dictationVolumeHandler;
        if (_dictationErrorHandler is not null)
            service.RecognitionError -= _dictationErrorHandler;
        _dictationPhraseHandler = null;
        _dictationVolumeHandler = null;
        _dictationErrorHandler = null;
    }

    private void AppendSpeechToEditor(string text, int sessionId, long dispatchId)
    {
        SquadDashTrace.Write(
            TraceCategory.Avalon,
            $"AVALON_DICTATION_CALLBACK_EXEC event=phrase session={sessionId} dispatch={dispatchId} currentSession={_dictationSessionId} disposed={_dictationDisposed} editorDisposed={_isDisposed}");
        if (_dictationDisposed || _isDisposed || sessionId != _dictationSessionId)
        {
            SquadDashTrace.Write(
                TraceCategory.Avalon,
                $"AVALON_DICTATION_CALLBACK_DROP session={sessionId} dispatch={dispatchId} reason={(sessionId != _dictationSessionId ? "session-mismatch" : "disposed")}");
            return;
        }
        InsertDictationPhrase(text);
    }

    internal void InsertDictationPhrase(string text)
    {
        ThrowIfDisposed();
        var liveCaret = Editor.CaretOffset;
        var anchor = _dictationInsertionAnchor;
        var anchorOffset = anchor is { IsDeleted: false } ? anchor.Offset : -1;
        SquadDashTrace.Write(
            TraceCategory.Avalon,
            $"AVALON_DICTATION_RANGE_DECISION session={_dictationSessionId} source=initial capturedCaret={_dictationCaretIndex} capturedSelection={_dictationSelectionLength} " +
            $"liveCaret={liveCaret} anchorOffset={anchorOffset} anchorDeleted={(anchor?.IsDeleted ?? true).ToString().ToLowerInvariant()}");
        if (_dictationSelection is { } selection)
        {
            if (!selection.TryGetRange(out _dictationCaretIndex, out _dictationSelectionLength) ||
                Editor.Document.GetText(_dictationCaretIndex, _dictationSelectionLength) != selection.OriginalText)
            {
                SquadDashTrace.Write(
                    TraceCategory.Avalon,
                    $"AVALON_DICTATION_DROP session={_dictationSessionId} reason=target-changed capturedCaret={_dictationCaretIndex} capturedSelection={_dictationSelectionLength} " +
                    $"liveCaret={Editor.CaretOffset} anchorOffset={anchorOffset}");
                SquadDashTrace.Write(TraceCategory.UI, "Dictation target changed before recognition completed.");
                MessageBox.Show($"The dictation target was edited. Recognized text:\n\n{text}",
                    "Dictation", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SquadDashTrace.Write(
                TraceCategory.Avalon,
                $"AVALON_DICTATION_RANGE_DECISION session={_dictationSessionId} source=selection capturedCaret={_dictationCaretIndex} capturedSelection={_dictationSelectionLength} " +
                $"liveCaret={Editor.CaretOffset} anchorOffset={anchorOffset}");
        }
        else if (anchor is { IsDeleted: false })
        {
            _dictationCaretIndex = anchor.Offset;
            SquadDashTrace.Write(
                TraceCategory.Avalon,
                $"AVALON_DICTATION_RANGE_DECISION session={_dictationSessionId} source=anchor capturedCaret={_dictationCaretIndex} capturedSelection={_dictationSelectionLength} " +
                $"liveCaret={Editor.CaretOffset} anchorOffset={anchor.Offset}");
        }
        else
        {
            SquadDashTrace.Write(
                TraceCategory.Avalon,
                $"AVALON_DICTATION_DROP session={_dictationSessionId} reason=anchor-deleted capturedCaret={_dictationCaretIndex} capturedSelection={_dictationSelectionLength} " +
                $"liveCaret={Editor.CaretOffset} anchorOffset={anchorOffset}");
            SquadDashTrace.Write(TraceCategory.UI, "Dictation insertion anchor was deleted.");
            MessageBox.Show($"The dictation target was deleted. Recognized text:\n\n{text}",
                "Dictation", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!CanMutateRange(_dictationCaretIndex, _dictationSelectionLength))
        {
            SquadDashTrace.Write(
                TraceCategory.Avalon,
                $"AVALON_DICTATION_DROP session={_dictationSessionId} reason=active-revision caret={_dictationCaretIndex} selection={_dictationSelectionLength} liveCaret={Editor.CaretOffset}");
            SquadDashTrace.Write(TraceCategory.UI, "Dictation insertion blocked by an active revision.");
            return;
        }

        var current = Editor.Document.Text;
        var rules = _settingsStore.Load().VoiceReplacementRules;
        var plan = VoiceInsertionHeuristics.PlanDictationInsertion(
            current,
            _dictationCaretIndex,
            _dictationSelectionLength,
            NormalizeLf(text),
            rules);
        plan = plan with { InsertedText = NormalizeLf(plan.InsertedText), ReplacedText = NormalizeLf(plan.ReplacedText) };
        SquadDashTrace.Write(
            TraceCategory.Avalon,
            $"AVALON_DICTATION_PLAN session={_dictationSessionId} currentLen={current.Length} caret={_dictationCaretIndex} selection={_dictationSelectionLength} " +
            $"planCaret={plan.CaretIndex} planSelection={plan.SelectionLength} insertedLen={plan.InsertedText.Length} hasReplacement={plan.HasReplacementStep.ToString().ToLowerInvariant()} " +
            $"planCaretAfterInsert={plan.CaretIndexAfterInsert} planCaretAfterReplace={plan.CaretIndexAfterReplace} " +
            $"liveCaret={Editor.CaretOffset} anchorOffset={(anchor is { IsDeleted: false } ? anchor.Offset : -1)}");
        _dictationSelectionLength = 0;
        _dictationSelection = null;

        Editor.Document.Replace(plan.CaretIndex, plan.SelectionLength, plan.InsertedText);
        Editor.Select(plan.CaretIndexAfterInsert, 0);
        Editor.CaretOffset = plan.CaretIndexAfterInsert;
        _dictationCaretIndex = plan.CaretIndexAfterInsert;

        if (plan.HasReplacementStep)
        {
            Editor.Document.Replace(plan.CaretIndex, plan.InsertedText.Length, plan.ReplacedText);
            Editor.Select(plan.CaretIndexAfterReplace, 0);
            Editor.CaretOffset = plan.CaretIndexAfterReplace;
            _dictationCaretIndex = plan.CaretIndexAfterReplace;
        }
        SetDictationInsertionAnchor();
        SquadDashTrace.Write(
            TraceCategory.Avalon,
            $"AVALON_DICTATION_INSERT_POST session={_dictationSessionId} docLen={Editor.Document.TextLength} caret={_dictationCaretIndex}");
    }

    private void ShowDictationWindow()
    {
        CloseDictationWindow();
        var owner = Window.GetWindow(this);
        if (owner is null)
            return;

        var physicalPt = GetCaretScreenPoint(Editor);
        var physWa = NativeMethods.GetWorkAreaForPhysicalPoint((int)physicalPt.X, (int)physicalPt.Y);
        var logicalPt = DpiHelper.PhysicalToLogical(Editor, physicalPt);
        var logicalWaOrigin = DpiHelper.PhysicalToLogical(Editor, new Point(physWa.Left, physWa.Top));
        var logicalWaCorner = DpiHelper.PhysicalToLogical(Editor, new Point(physWa.Right, physWa.Bottom));
        var logicalWorkArea = new Rect(logicalWaOrigin, logicalWaCorner);

        _dictationPttWindow = new PushToTalkWindow(owner, showHint: false);
        _dictationPttWindow.PositionUnderCaret(logicalPt, logicalWorkArea);
        _dictationPttWindow.Show();
        Editor.TextArea.Focus();
    }

    private static Point GetCaretScreenPoint(ICSharpCode.AvalonEdit.TextEditor editor)
    {
        try
        {
            var textView = editor.TextArea.TextView;
            textView.EnsureVisualLines();
            var visualPosition = textView.GetVisualPosition(editor.TextArea.Caret.Position, VisualYPosition.LineBottom) - textView.ScrollOffset;
            return textView.PointToScreen(visualPosition);
        }
        catch (InvalidOperationException)
        {
            return editor.TextArea.PointToScreen(new Point(0, editor.TextArea.ActualHeight + 4));
        }
    }

    private void CloseDictationWindow()
    {
        if (_dictationPttWindow is null)
            return;
        _dictationPttWindow.Close();
        _dictationPttWindow = null;
    }
}
