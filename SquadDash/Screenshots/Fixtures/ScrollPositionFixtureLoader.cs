using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SquadDash.Screenshots.Fixtures;

/// <summary>
/// Applies and restores scroll positions for the coordinator transcript, the active-agent
/// roster lane, and the inactive-agent roster lane before a screenshot is captured, so
/// that screenshots taken at a specific scroll position are reproducible.
/// </summary>
/// <remarks>
/// <para>
/// Because the transcript's inner <see cref="System.Windows.Controls.ScrollViewer"/> is
/// managed by <see cref="TranscriptScrollController"/>, this loader receives scroll access
/// for all three targets through delegate parameters injected at registration time.  For
/// the transcript, the getter calls <c>TranscriptScrollController.GetVerticalOffset()</c>
/// and the setter calls <c>TranscriptScrollController.ScrollToAbsoluteOffset()</c>, so
/// that programmatic scrolls are correctly bracketed by the controller and do not disturb
/// its user-scroll tracking.  The active- and inactive-roster getters/setters wrap
/// <c>ScrollViewer.HorizontalOffset</c> and <c>ScrollToHorizontalOffset()</c> directly.
/// </para>
/// <para>
/// Only fixture keys that are actually present in the data bag are applied; absent keys
/// leave the corresponding scroll position unchanged.
/// </para>
/// <para>
/// Register this loader after <c>agentCard</c> — agent-card layout must be settled before
/// scroll positions are meaningful.
/// </para>
/// </remarks>
internal sealed class ScrollPositionFixtureLoader : IFixtureLoader
{
    // ── Known keys ────────────────────────────────────────────────────────────
    private static readonly IReadOnlyList<string> _knownKeys =
        ["transcriptScrollOffset", "activeRosterScrollOffset", "inactiveRosterScrollOffset"];

    public IReadOnlyList<string> KnownKeys => _knownKeys;

    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly Func<double>   _getTranscriptOffset;
    private readonly Action<double> _setTranscriptOffset;
    private readonly Func<double>   _getActiveRosterOffset;
    private readonly Action<double> _setActiveRosterOffset;
    private readonly Func<double>   _getInactiveRosterOffset;
    private readonly Action<double> _setInactiveRosterOffset;
    private readonly Dispatcher     _dispatcher;

    // ── Restore snapshot ──────────────────────────────────────────────────────
    private double _originalTranscriptOffset;
    private double _originalActiveRosterOffset;
    private double _originalInactiveRosterOffset;
    private bool   _transcriptOffsetApplied;
    private bool   _activeRosterOffsetApplied;
    private bool   _inactiveRosterOffsetApplied;
    private bool   _applied;

    // ── Constructor ───────────────────────────────────────────────────────────

    internal ScrollPositionFixtureLoader(
        Func<double>   getTranscriptOffset,
        Action<double> setTranscriptOffset,
        Func<double>   getActiveRosterOffset,
        Action<double> setActiveRosterOffset,
        Func<double>   getInactiveRosterOffset,
        Action<double> setInactiveRosterOffset,
        Dispatcher     dispatcher)
    {
        _getTranscriptOffset     = getTranscriptOffset     ?? throw new ArgumentNullException(nameof(getTranscriptOffset));
        _setTranscriptOffset     = setTranscriptOffset     ?? throw new ArgumentNullException(nameof(setTranscriptOffset));
        _getActiveRosterOffset   = getActiveRosterOffset   ?? throw new ArgumentNullException(nameof(getActiveRosterOffset));
        _setActiveRosterOffset   = setActiveRosterOffset   ?? throw new ArgumentNullException(nameof(setActiveRosterOffset));
        _getInactiveRosterOffset = getInactiveRosterOffset ?? throw new ArgumentNullException(nameof(getInactiveRosterOffset));
        _setInactiveRosterOffset = setInactiveRosterOffset ?? throw new ArgumentNullException(nameof(setInactiveRosterOffset));
        _dispatcher              = dispatcher              ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    // ── IFixtureLoader ────────────────────────────────────────────────────────

    public Task ApplyAsync(ScreenshotFixture fixture, CancellationToken ct)
    {
        // Bail early if none of our keys are present.
        if (!HasAnyKey(fixture))
            return Task.CompletedTask;

        _dispatcher.Invoke(() =>
        {
            ct.ThrowIfCancellationRequested();

            // ── transcriptScrollOffset ───────────────────────────────────────
            if (fixture.Data.TryGetValue("transcriptScrollOffset", out var transcriptEl))
            {
                if (transcriptEl.TryGetDouble(out var transcriptOffset) && transcriptOffset >= 0)
                {
                    _originalTranscriptOffset = _getTranscriptOffset();
                    _setTranscriptOffset(transcriptOffset);
                    _transcriptOffsetApplied  = true;
                }
                else
                {
                    Debug.WriteLine(
                        $"[ScrollPositionFixtureLoader] 'transcriptScrollOffset' value '{transcriptEl}' is missing or invalid — skipping");
                }
            }

            // ── activeRosterScrollOffset ─────────────────────────────────────
            if (fixture.Data.TryGetValue("activeRosterScrollOffset", out var activeEl))
            {
                if (activeEl.TryGetDouble(out var activeOffset) && activeOffset >= 0)
                {
                    _originalActiveRosterOffset = _getActiveRosterOffset();
                    _setActiveRosterOffset(activeOffset);
                    _activeRosterOffsetApplied  = true;
                }
                else
                {
                    Debug.WriteLine(
                        $"[ScrollPositionFixtureLoader] 'activeRosterScrollOffset' value '{activeEl}' is missing or invalid — skipping");
                }
            }

            // ── inactiveRosterScrollOffset ───────────────────────────────────
            if (fixture.Data.TryGetValue("inactiveRosterScrollOffset", out var inactiveEl))
            {
                if (inactiveEl.TryGetDouble(out var inactiveOffset) && inactiveOffset >= 0)
                {
                    _originalInactiveRosterOffset = _getInactiveRosterOffset();
                    _setInactiveRosterOffset(inactiveOffset);
                    _inactiveRosterOffsetApplied  = true;
                }
                else
                {
                    Debug.WriteLine(
                        $"[ScrollPositionFixtureLoader] 'inactiveRosterScrollOffset' value '{inactiveEl}' is missing or invalid — skipping");
                }
            }

            _applied = true;

        }, DispatcherPriority.Normal, ct);

        // Flush the layout pipeline after scroll changes.
        _dispatcher.Invoke(() => { }, DispatcherPriority.Render);

        return Task.CompletedTask;
    }

    public Task RestoreAsync(CancellationToken ct)
    {
        if (!_applied)
            return Task.CompletedTask; // idempotent — nothing was applied

        _dispatcher.Invoke(() =>
        {
            if (_transcriptOffsetApplied)
            {
                _setTranscriptOffset(_originalTranscriptOffset);
                _transcriptOffsetApplied = false;
            }

            if (_activeRosterOffsetApplied)
            {
                _setActiveRosterOffset(_originalActiveRosterOffset);
                _activeRosterOffsetApplied = false;
            }

            if (_inactiveRosterOffsetApplied)
            {
                _setInactiveRosterOffset(_originalInactiveRosterOffset);
                _inactiveRosterOffsetApplied = false;
            }

            _applied = false;

        }, DispatcherPriority.Normal, ct);

        _dispatcher.Invoke(() => { }, DispatcherPriority.Render);

        return Task.CompletedTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool HasAnyKey(ScreenshotFixture fixture) =>
        fixture.Data.ContainsKey("transcriptScrollOffset")    ||
        fixture.Data.ContainsKey("activeRosterScrollOffset")  ||
        fixture.Data.ContainsKey("inactiveRosterScrollOffset");
}
