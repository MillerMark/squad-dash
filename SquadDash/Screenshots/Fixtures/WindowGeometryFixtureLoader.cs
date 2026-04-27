using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SquadDash.Screenshots.Fixtures;

/// <summary>
/// Applies and restores window geometry (size, position, state) before a screenshot is
/// captured so that <c>CaptureBounds</c> recorded at a specific window configuration are
/// reproducible on any machine.
/// </summary>
/// <remarks>
/// <para>
/// This loader must be registered <em>first</em> — all layout-dependent loaders depend on
/// window geometry being established before they run.
/// </para>
/// <para>
/// Only fixture keys that are actually present in the data bag are applied; absent keys
/// leave the corresponding window property unchanged.  This lets fixtures specify only the
/// properties they care about (e.g., just <c>windowWidth</c> and <c>windowHeight</c>
/// without touching position).
/// </para>
/// </remarks>
internal sealed class WindowGeometryFixtureLoader : IFixtureLoader
{
    // ── Known keys ────────────────────────────────────────────────────────────
    private static readonly IReadOnlyList<string> _knownKeys =
        ["windowWidth", "windowHeight", "windowLeft", "windowTop", "windowState"];

    public IReadOnlyList<string> KnownKeys => _knownKeys;

    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly Window     _mainWindow;
    private readonly Dispatcher _dispatcher;

    // ── Restore snapshot ──────────────────────────────────────────────────────
    private double      _originalLeft;
    private double      _originalTop;
    private double      _originalWidth;
    private double      _originalHeight;
    private WindowState _originalWindowState;
    private bool        _applied;

    // ── Constructor ───────────────────────────────────────────────────────────

    internal WindowGeometryFixtureLoader(Window mainWindow, Dispatcher dispatcher)
    {
        _mainWindow  = mainWindow  ?? throw new ArgumentNullException(nameof(mainWindow));
        _dispatcher  = dispatcher  ?? throw new ArgumentNullException(nameof(dispatcher));
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

            // ── Snapshot original geometry ───────────────────────────────────
            _originalLeft        = _mainWindow.Left;
            _originalTop         = _mainWindow.Top;
            _originalWidth       = _mainWindow.Width;
            _originalHeight      = _mainWindow.Height;
            _originalWindowState = _mainWindow.WindowState;

            // ── Determine target window state ────────────────────────────────
            var targetMaximized = false;
            if (fixture.Data.TryGetValue("windowState", out var stateEl))
            {
                var stateStr = stateEl.GetString();
                targetMaximized = string.Equals(stateStr, "Maximized", StringComparison.OrdinalIgnoreCase);
            }

            if (targetMaximized)
            {
                // Must be Normal before setting geometry so WPF accepts the values.
                _mainWindow.WindowState = WindowState.Normal;
                ApplyPositionAndSize(fixture);
                _mainWindow.WindowState = WindowState.Maximized;
            }
            else
            {
                _mainWindow.WindowState = WindowState.Normal;
                ApplyPositionAndSize(fixture);
            }

            _applied = true;

        }, DispatcherPriority.Normal, ct);

        // Flush the layout pipeline so callers see a fully-measured window.
        _dispatcher.Invoke(() => { }, DispatcherPriority.Render);

        return Task.CompletedTask;
    }

    public Task RestoreAsync(CancellationToken ct)
    {
        if (!_applied)
            return Task.CompletedTask; // idempotent — nothing was applied

        _dispatcher.Invoke(() =>
        {
            if (_originalWindowState == WindowState.Maximized)
            {
                _mainWindow.WindowState = WindowState.Normal;
                RestorePositionAndSize();
                _mainWindow.WindowState = WindowState.Maximized;
            }
            else
            {
                _mainWindow.WindowState = WindowState.Normal;
                RestorePositionAndSize();
            }

            _applied = false;

        }, DispatcherPriority.Normal, ct);

        _dispatcher.Invoke(() => { }, DispatcherPriority.Render);

        return Task.CompletedTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool HasAnyKey(ScreenshotFixture fixture) =>
        fixture.Data.ContainsKey("windowWidth")  ||
        fixture.Data.ContainsKey("windowHeight") ||
        fixture.Data.ContainsKey("windowLeft")   ||
        fixture.Data.ContainsKey("windowTop")    ||
        fixture.Data.ContainsKey("windowState");

    /// <summary>
    /// Applies Left, Top, Width, Height from the fixture — only for keys that are present.
    /// Must be called while already on the dispatcher thread.
    /// </summary>
    private void ApplyPositionAndSize(ScreenshotFixture fixture)
    {
        if (fixture.Data.TryGetValue("windowLeft", out var leftEl) &&
            leftEl.TryGetDouble(out var left))
            _mainWindow.Left = left;

        if (fixture.Data.TryGetValue("windowTop", out var topEl) &&
            topEl.TryGetDouble(out var top))
            _mainWindow.Top = top;

        if (fixture.Data.TryGetValue("windowWidth", out var widthEl) &&
            widthEl.TryGetDouble(out var width))
            _mainWindow.Width = width;

        if (fixture.Data.TryGetValue("windowHeight", out var heightEl) &&
            heightEl.TryGetDouble(out var height))
            _mainWindow.Height = height;
    }

    /// <summary>
    /// Restores Left, Top, Width, Height to the snapshotted originals.
    /// Must be called while already on the dispatcher thread.
    /// </summary>
    private void RestorePositionAndSize()
    {
        _mainWindow.Left   = _originalLeft;
        _mainWindow.Top    = _originalTop;
        _mainWindow.Width  = _originalWidth;
        _mainWindow.Height = _originalHeight;
    }
}
