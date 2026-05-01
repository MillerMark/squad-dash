using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SquadDash.Screenshots.Fixtures;

/// <summary>
/// Applies and restores fixture state for the Loop panel so screenshots can
/// show it in a running or configured state without triggering actual loop
/// execution or writing loop configuration to disk.
/// </summary>
/// <remarks>
/// <para>
/// <b>loopRunning</b> — bool; when <c>true</c>, the panel is made to look like a
/// loop is in progress (status label, button enabled/disabled states).
/// When absent the loader is a no-op.
/// </para>
/// <para>
/// <b>currentIteration</b> — int; 1-based iteration number shown in the status
/// label.  Ignored when <b>loopRunning</b> is <c>false</c>.
/// </para>
/// <para>
/// <b>totalIterations</b> — int; optional total-iteration count appended to the
/// status label as <c>" / {total}"</c>.  Omit for an open-ended loop display.
/// </para>
/// <para>
/// Only the UI surface of the loop panel is modified — no background tasks are
/// started and no settings are persisted.
/// </para>
/// </remarks>
internal sealed class LoopPanelFixtureLoader : IFixtureLoader
{
    // ── Known keys ────────────────────────────────────────────────────────────
    private static readonly IReadOnlyList<string> _knownKeys =
        ["loopRunning", "currentIteration", "totalIterations"];

    public IReadOnlyList<string> KnownKeys => _knownKeys;

    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly Func<string>       _getStatusText;
    private readonly Action<string>     _setStatusText;
    private readonly Func<bool>         _getStopEnabled;
    private readonly Action<bool>       _setStopEnabled;
    private readonly Func<bool>         _getStartEnabled;
    private readonly Action<bool>       _setStartEnabled;
    private readonly Func<Visibility>   _getAbortVisibility;
    private readonly Action<Visibility> _setAbortVisibility;
    private readonly Dispatcher         _dispatcher;

    // ── Restore snapshot ──────────────────────────────────────────────────────
    private string     _originalStatusText     = string.Empty;
    private bool       _originalStopEnabled;
    private bool       _originalStartEnabled;
    private Visibility _originalAbortVisibility;
    private bool       _applied;

    // ── Constructor ───────────────────────────────────────────────────────────

    internal LoopPanelFixtureLoader(
        Func<string>       getStatusText,
        Action<string>     setStatusText,
        Func<bool>         getStopEnabled,
        Action<bool>       setStopEnabled,
        Func<bool>         getStartEnabled,
        Action<bool>       setStartEnabled,
        Func<Visibility>   getAbortVisibility,
        Action<Visibility> setAbortVisibility,
        Dispatcher         dispatcher)
    {
        _getStatusText      = getStatusText      ?? throw new ArgumentNullException(nameof(getStatusText));
        _setStatusText      = setStatusText      ?? throw new ArgumentNullException(nameof(setStatusText));
        _getStopEnabled     = getStopEnabled     ?? throw new ArgumentNullException(nameof(getStopEnabled));
        _setStopEnabled     = setStopEnabled     ?? throw new ArgumentNullException(nameof(setStopEnabled));
        _getStartEnabled    = getStartEnabled    ?? throw new ArgumentNullException(nameof(getStartEnabled));
        _setStartEnabled    = setStartEnabled    ?? throw new ArgumentNullException(nameof(setStartEnabled));
        _getAbortVisibility = getAbortVisibility ?? throw new ArgumentNullException(nameof(getAbortVisibility));
        _setAbortVisibility = setAbortVisibility ?? throw new ArgumentNullException(nameof(setAbortVisibility));
        _dispatcher         = dispatcher         ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    // ── IFixtureLoader ────────────────────────────────────────────────────────

    public Task ApplyAsync(ScreenshotFixture fixture, CancellationToken ct)
    {
        if (!fixture.Data.TryGetValue("loopRunning", out var loopRunningEl))
            return Task.CompletedTask;

        var running = loopRunningEl.ValueKind == JsonValueKind.True;

        _dispatcher.Invoke(() =>
        {
            ct.ThrowIfCancellationRequested();

            // ── Snapshot current state ────────────────────────────────────────
            _originalStatusText      = _getStatusText();
            _originalStopEnabled     = _getStopEnabled();
            _originalStartEnabled    = _getStartEnabled();
            _originalAbortVisibility = _getAbortVisibility();

            if (running)
            {
                // ── Build status label from iteration counters ────────────────
                var currentIteration = fixture.Data.TryGetValue("currentIteration", out var iterEl) &&
                                       iterEl.TryGetInt32(out var iter)
                    ? iter : 0;

                var totalIterations = fixture.Data.TryGetValue("totalIterations", out var totalEl) &&
                                      totalEl.TryGetInt32(out var total)
                    ? total : 0;

                string status;
                if (currentIteration > 0 && totalIterations > 0)
                    status = $"● Running · Round {currentIteration} / {totalIterations}";
                else if (currentIteration > 0)
                    status = $"● Running · Round {currentIteration}";
                else
                    status = "● Running";

                _setStatusText(status);
                _setStopEnabled(true);
                _setStartEnabled(false);
                _setAbortVisibility(Visibility.Visible);
            }
            else
            {
                _setStatusText(string.Empty);
                _setStopEnabled(false);
                _setStartEnabled(true);
                _setAbortVisibility(Visibility.Collapsed);
            }

            _applied = true;

        }, DispatcherPriority.Normal, ct);

        return Task.CompletedTask;
    }

    public Task RestoreAsync(CancellationToken ct)
    {
        if (!_applied)
            return Task.CompletedTask;

        _dispatcher.Invoke(() =>
        {
            _setStatusText(_originalStatusText);
            _setStopEnabled(_originalStopEnabled);
            _setStartEnabled(_originalStartEnabled);
            _setAbortVisibility(_originalAbortVisibility);
            _applied = false;

        }, DispatcherPriority.Normal, ct);

        return Task.CompletedTask;
    }
}
