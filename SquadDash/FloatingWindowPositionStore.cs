using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// Persists and restores floating window positions across application restarts.
/// Positions are validated against monitor geometry so windows are never restored
/// to a monitor that no longer exists or has changed resolution.
/// </summary>
internal sealed class FloatingWindowPositionStore
{
    // ── DTO ──────────────────────────────────────────────────────────────────

    internal sealed class Entry
    {
        // Window position/size in WPF logical units
        public double Left   { get; set; }
        public double Top    { get; set; }
        public double Width  { get; set; }
        public double Height { get; set; }

        // Monitor that contained the window's upper-left corner (physical pixels)
        public double MonitorLeft   { get; set; }
        public double MonitorTop    { get; set; }
        public double MonitorWidth  { get; set; }
        public double MonitorHeight { get; set; }

        // Relative offset: window upper-left minus monitor upper-left (logical units)
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
    }

    // ── Shared singleton ─────────────────────────────────────────────────────

    /// <summary>
    /// Application-wide shared instance. All UI operations are on the dispatcher
    /// thread, so no locking is required.
    /// </summary>
    public static FloatingWindowPositionStore Shared { get; } = new FloatingWindowPositionStore();

    // ── State ────────────────────────────────────────────────────────────────

    private readonly string _filePath;
    private Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    // ── Construction ─────────────────────────────────────────────────────────

    public FloatingWindowPositionStore()
        : this(SquadDashPaths.AppData) { }

    internal FloatingWindowPositionStore(string directory)
    {
        _filePath = Path.Combine(directory, "floating-window-positions.json");
        Load();
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Captures the current position of <paramref name="window"/> and stores it
    /// under the given <paramref name="key"/>. Call this on window close or app shutdown.
    /// </summary>
    public void Save(string key, Window window)
    {
        if (!window.IsLoaded) return;
        var (monitorPhysRect, offsetX, offsetY) = GetMonitorAndOffset(window);
        if (monitorPhysRect.IsEmpty) return;

        double w = window.ActualWidth  > 0 ? window.ActualWidth  : window.Width;
        double h = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
        if (!double.IsFinite(window.Left) || !double.IsFinite(window.Top) ||
            !double.IsFinite(w) || !double.IsFinite(h)) return;

        _entries[key] = new Entry
        {
            Left          = window.Left,
            Top           = window.Top,
            Width         = w,
            Height        = h,
            MonitorLeft   = monitorPhysRect.Left,
            MonitorTop    = monitorPhysRect.Top,
            MonitorWidth  = monitorPhysRect.Width,
            MonitorHeight = monitorPhysRect.Height,
            OffsetX       = offsetX,
            OffsetY       = offsetY,
        };
        Flush();
    }

    /// <summary>
    /// Removes any saved entry for the given key (call when the window is permanently gone).
    /// </summary>
    public void Remove(string key)
    {
        if (_entries.Remove(key))
            Flush();
    }

    /// <summary>
    /// Attempts to restore the saved position for <paramref name="window"/>.
    /// Sets Left/Top/Width/Height and returns <c>true</c> if the saved monitor
    /// still exists in the current configuration; returns <c>false</c> and does
    /// NOT modify the window if no matching monitor is found.
    /// </summary>
    public bool TryRestore(string key, Window window)
        => TryRestoreCore(key, window, FindMatchingMonitor);

    /// <summary>
    /// Testable core of <see cref="TryRestore"/>; accepts an injected monitor-finder
    /// so unit tests can supply a synthetic monitor without live Win32 calls.
    /// </summary>
    internal bool TryRestoreCore(string key, Window window, Func<Entry, Rect> monitorFinder)
    {
        if (!_entries.TryGetValue(key, out var e)) return false;

        // FindMatchingMonitor verifies the monitor still exists with the same physical
        // dimensions it had when the position was saved.  That is the sole validation
        // gate: if the same physical monitor is present we trust the saved Left/Top.
        //
        // A previously present DPI-based offset check was removed because it was
        // evaluated before the window is shown, at which point PresentationSource is
        // null and GetDpi falls back to (1.0, 1.0).  When the actual monitor DPI is
        // not 1.0 the mixed-unit arithmetic produced incorrect results, causing
        // TryRestore to return false for windows on monitors above the primary (negative
        // Y coordinates) and leaving WindowStartupLocation as CenterOwner.
        if (monitorFinder(e).IsEmpty) return false;

        // Apply saved position.
        window.Left   = e.Left;
        window.Top    = e.Top;
        if (e.Width  > 0) window.Width  = e.Width;
        if (e.Height > 0) window.Height = e.Height;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        return true;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static (Rect monitorPhysRect, double offsetX, double offsetY) GetMonitorAndOffset(Window window)
    {
        var src = PresentationSource.FromVisual(window);
        if (src?.CompositionTarget is not { } ct) return (Rect.Empty, 0, 0);
        var m = ct.TransformToDevice;

        double physLeft = window.Left * m.M11;
        double physTop  = window.Top  * m.M22;

        var monRect = NativeMethods.GetMonitorBoundsForPhysicalPoint((int)physLeft, (int)physTop);
        if (monRect.IsEmpty) return (Rect.Empty, 0, 0);

        double offsetX = window.Left - monRect.Left / m.M11;
        double offsetY = window.Top  - monRect.Top  / m.M22;
        return (monRect, offsetX, offsetY);
    }

    private static Rect FindMatchingMonitor(Entry e)
    {
        return NativeMethods.FindMonitorByBounds(
            (int)e.MonitorLeft, (int)e.MonitorTop,
            (int)e.MonitorWidth, (int)e.MonitorHeight);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = File.ReadAllText(_filePath);
            _entries = JsonSerializer.Deserialize<Dictionary<string, Entry>>(json)
                       ?? new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        }
        catch { _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase); }
    }

    private void Flush()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath,
                JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-fatal */ }
    }
}
