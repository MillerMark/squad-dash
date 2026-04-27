using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SquadDash.Screenshots;

/// <summary>
/// Stub implementation of <see cref="IReplayableUiAction"/> that opens the
/// Preferences window so screenshot automation can capture its UI state.
/// </summary>
/// <remarks>
/// <para>
/// Concrete window interaction is supplied via constructor delegates so this class
/// has no hard dependency on <c>MainWindow</c> internals and can be tested in
/// isolation without a live WPF application.
/// </para>
/// <para>
/// Phase 3 note: <see cref="IsReadyAsync"/> currently returns <c>true</c>
/// immediately.  A production implementation should poll
/// <c>Dispatcher.HasShutdownStarted</c>, verify layout pass completion, and
/// optionally await a short animation-settle delay before confirming readiness.
/// </para>
/// </remarks>
public sealed class OpenPreferencesWindowAction : IReplayableUiAction
{
    // ── Delegates wired by MainWindow at registration time ─────────────────

    /// <summary>Opens (or activates) the Preferences window. Runs on UI thread.</summary>
    private readonly Action _openPreferencesWindow;

    /// <summary>
    /// Returns the currently-open <c>PreferencesWindow</c> instance, or <c>null</c>
    /// if it is not visible.  Used by <see cref="IsReadyAsync"/> and
    /// <see cref="UndoAsync"/>.
    /// </summary>
    private readonly Func<Window?> _getPreferencesWindow;

    // ── IReplayableUiAction ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public string ActionId => "open-preferences-window";

    /// <inheritdoc/>
    public string Description => "Opens the Preferences window so its UI state can be captured.";

    /// <inheritdoc/>
    public bool IsSideEffectFree => true;

    // ── Constructor ─────────────────────────────────────────────────────────

    /// <param name="openPreferencesWindow">
    ///   Delegate that opens (or brings to front) the Preferences window.
    ///   Must be safe to call on the WPF dispatcher thread.
    /// </param>
    /// <param name="getPreferencesWindow">
    ///   Delegate that returns the current <c>PreferencesWindow</c> if it is
    ///   visible, or <c>null</c> otherwise.
    /// </param>
    public OpenPreferencesWindowAction(
        Action openPreferencesWindow,
        Func<Window?> getPreferencesWindow)
    {
        _openPreferencesWindow = openPreferencesWindow ?? throw new ArgumentNullException(nameof(openPreferencesWindow));
        _getPreferencesWindow  = getPreferencesWindow  ?? throw new ArgumentNullException(nameof(getPreferencesWindow));
    }

    // ── IReplayableUiAction implementation ──────────────────────────────────

    /// <summary>
    /// Opens the Preferences window (or activates it if already open).
    /// </summary>
    public Task ExecuteAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _openPreferencesWindow();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns <c>true</c> when the Preferences window is visible.
    /// Phase-3 production upgrade: also await layout/animation settle.
    /// </summary>
    public Task<bool> IsReadyAsync()
    {
        var window = _getPreferencesWindow();
        return Task.FromResult(window is { IsVisible: true });
    }

    /// <summary>
    /// Closes the Preferences window if it is currently open.
    /// Safe to call when the window is already closed.
    /// </summary>
    public Task UndoAsync()
    {
        var window = _getPreferencesWindow();
        window?.Close();
        return Task.CompletedTask;
    }
}
