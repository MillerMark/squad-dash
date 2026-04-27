using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SquadDash.Screenshots.Fixtures;

/// <summary>
/// Applies and restores the application view mode (normal or full-screen
/// transcript) before a screenshot is captured so that screenshots of
/// full-screen transcript mode are reproducible.
/// </summary>
/// <remarks>
/// <para>
/// Because <c>MainWindow</c>'s view-mode fields are private, this loader receives the
/// necessary state access through delegate parameters injected at registration time.
/// The setter delegates must update both the underlying field <em>and</em> refresh the
/// UI (e.g. by calling <c>ApplyViewMode()</c>), without persisting the change to the
/// settings store — fixture apply/restore must be side-effect-free with respect to
/// persisted settings.
/// </para>
/// <para>
/// Register this loader after <c>WindowGeometryFixtureLoader</c> (window geometry must
/// be established first) and before <c>AgentCardFixtureLoader</c> (view mode affects
/// panel visibility, which affects agent-card layout).
/// </para>
/// </remarks>
internal sealed class ViewModeFixtureLoader : IFixtureLoader
{
    // ── Known keys ────────────────────────────────────────────────────────────
    private static readonly IReadOnlyList<string> _knownKeys = ["viewMode"];

    public IReadOnlyList<string> KnownKeys => _knownKeys;

    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly Func<bool>    _getTranscriptFullScreen;
    private readonly Action<bool>  _setTranscriptFullScreen;
    private readonly Dispatcher    _dispatcher;

    // ── Restore snapshot ──────────────────────────────────────────────────────
    private bool _originalTranscriptFullScreen;
    private bool _applied;

    // ── Constructor ───────────────────────────────────────────────────────────

    internal ViewModeFixtureLoader(
        Func<bool>   getTranscriptFullScreen,
        Action<bool> setTranscriptFullScreen,
        Dispatcher   dispatcher)
    {
        _getTranscriptFullScreen = getTranscriptFullScreen ?? throw new ArgumentNullException(nameof(getTranscriptFullScreen));
        _setTranscriptFullScreen = setTranscriptFullScreen ?? throw new ArgumentNullException(nameof(setTranscriptFullScreen));
        _dispatcher              = dispatcher              ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    // ── IFixtureLoader ────────────────────────────────────────────────────────

    public Task ApplyAsync(ScreenshotFixture fixture, CancellationToken ct)
    {
        if (!fixture.Data.TryGetValue("viewMode", out var viewModeEl))
            return Task.CompletedTask; // key absent — nothing to do

        var viewMode = viewModeEl.GetString();

        _dispatcher.Invoke(() =>
        {
            ct.ThrowIfCancellationRequested();

            // ── Snapshot original view mode ──────────────────────────────────
            _originalTranscriptFullScreen = _getTranscriptFullScreen();

            // ── Apply requested view mode ────────────────────────────────────
            switch (viewMode)
            {
                case "fullscreenTranscript":
                    _setTranscriptFullScreen(true);
                    break;

                default: // "normal" or any unrecognised value
                    _setTranscriptFullScreen(false);
                    break;
            }

            _applied = true;

        }, DispatcherPriority.Normal, ct);

        // Flush the layout pipeline after the view-mode change.
        _dispatcher.Invoke(() => { }, DispatcherPriority.Render);

        return Task.CompletedTask;
    }

    public Task RestoreAsync(CancellationToken ct)
    {
        if (!_applied)
            return Task.CompletedTask; // idempotent — nothing was applied

        _dispatcher.Invoke(() =>
        {
            _setTranscriptFullScreen(_originalTranscriptFullScreen);

            _applied = false;

        }, DispatcherPriority.Normal, ct);

        _dispatcher.Invoke(() => { }, DispatcherPriority.Render);

        return Task.CompletedTask;
    }
}
