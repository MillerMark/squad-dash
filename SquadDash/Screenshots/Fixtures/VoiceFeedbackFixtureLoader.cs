using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SquadDash.Screenshots.Fixtures;

/// <summary>
/// Applies and restores fixture state for the voice dictation feedback area.
/// </summary>
/// <remarks>
/// <para>
/// <b>dictationText</b> is placed in <paramref name="promptTextBox"/> — the same
/// control that receives live speech-recognition output during push-to-talk sessions.
/// </para>
/// <para>
/// <b>isListening</b> shows a <see cref="PushToTalkWindow"/> positioned near the
/// prompt area to represent the "listening" visual state for screenshot purposes.
/// The window is closed and nulled in <see cref="RestoreAsync"/>.
/// </para>
/// </remarks>
internal sealed class VoiceFeedbackFixtureLoader : IFixtureLoader
{
    // ── Known keys ────────────────────────────────────────────────────────────
    private static readonly IReadOnlyList<string> _knownKeys = ["dictationText", "isListening"];

    public IReadOnlyList<string> KnownKeys => _knownKeys;

    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly TextBox    _promptTextBox;
    private readonly Window     _ownerWindow;
    private readonly Dispatcher _dispatcher;

    // ── Restore snapshot ──────────────────────────────────────────────────────
    private string?           _originalPromptText;
    private PushToTalkWindow? _fixtureListeningWindow;
    private bool              _applied;

    // ── Constructor ───────────────────────────────────────────────────────────

    internal VoiceFeedbackFixtureLoader(
        TextBox    promptTextBox,
        Window     ownerWindow,
        Dispatcher dispatcher)
    {
        _promptTextBox = promptTextBox ?? throw new ArgumentNullException(nameof(promptTextBox));
        _ownerWindow   = ownerWindow   ?? throw new ArgumentNullException(nameof(ownerWindow));
        _dispatcher    = dispatcher    ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    // ── IFixtureLoader ────────────────────────────────────────────────────────

    public Task ApplyAsync(ScreenshotFixture fixture, CancellationToken ct)
    {
        // Require at least one of our known keys to be present before doing anything
        var hasDictation  = fixture.Data.ContainsKey("dictationText");
        var hasListening  = fixture.Data.ContainsKey("isListening");
        if (!hasDictation && !hasListening)
            return Task.CompletedTask;

        _dispatcher.Invoke(() =>
        {
            ct.ThrowIfCancellationRequested();

            // Snapshot
            _originalPromptText = _promptTextBox.Text;

            // ── dictationText → PromptTextBox ────────────────────────────────
            if (hasDictation &&
                fixture.Data.TryGetValue("dictationText", out var dictationEl))
            {
                var text = dictationEl.GetString() ?? string.Empty;
                _promptTextBox.Text        = text;
                _promptTextBox.CaretIndex  = text.Length;
            }

            // ── isListening → show PushToTalkWindow ──────────────────────────
            if (hasListening &&
                fixture.Data.TryGetValue("isListening", out var listeningEl) &&
                listeningEl.ValueKind == JsonValueKind.True)
            {
                try
                {
                    _fixtureListeningWindow = new PushToTalkWindow(_ownerWindow, showHint: false);

                    // Position the window near the bottom-left of the owner window,
                    // approximating where it appears during a live push-to-talk session.
                    _fixtureListeningWindow.Left = _ownerWindow.Left + 24;
                    _fixtureListeningWindow.Top  = _ownerWindow.Top
                                                   + _ownerWindow.ActualHeight
                                                   - 80;
                    _fixtureListeningWindow.Show();
                }
                catch (Exception ex)
                {
                    // Non-fatal: the dictation text is more important for screenshots
                    // than the floating window; log and continue.
                    Debug.WriteLine(
                        $"[VoiceFeedbackFixtureLoader] Could not show PushToTalkWindow: {ex.Message}");
                    _fixtureListeningWindow = null;
                }
            }

            _applied = true;
        }, DispatcherPriority.Normal, ct);

        return Task.CompletedTask;
    }

    public Task RestoreAsync(CancellationToken ct)
    {
        if (!_applied)
            return Task.CompletedTask; // idempotent — nothing was applied

        _dispatcher.Invoke(() =>
        {
            // Restore prompt text
            if (_originalPromptText is not null)
            {
                _promptTextBox.Text       = _originalPromptText;
                _promptTextBox.CaretIndex = _originalPromptText.Length;
                _originalPromptText       = null;
            }

            // Close the fixture listening window if we opened one
            _fixtureListeningWindow?.Close();
            _fixtureListeningWindow = null;

            _applied = false;
        }, DispatcherPriority.Normal, ct);

        return Task.CompletedTask;
    }
}
