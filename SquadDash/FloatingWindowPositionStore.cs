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
    /// Sets Left/Top/Width/Height and returns <c>true</c> if the saved position
    /// is still valid for the current monitor configuration; returns <c>false</c>
    /// and does NOT modify the window if validation fails.
    /// </summary>
    public bool TryRestore(string key, Window window)
    {
        if (!_entries.TryGetValue(key, out var e)) return false;

        // Find a monitor whose physical bounds match exactly.
        var matchingMonitor = FindMatchingMonitor(e);
        if (matchingMonitor.IsEmpty) return false;

        // Get current DPI transform for this window (or identity if not yet shown).
        var (dpiX, dpiY) = GetDpi(window);

        // Convert the saved global Left/Top to physical pixels and compute the offset
        // from the matching monitor's upper-left corner.
        double physLeft = e.Left * dpiX;
        double physTop  = e.Top  * dpiY;
        double actualOffsetX = (physLeft - matchingMonitor.Left) / dpiX;
        double actualOffsetY = (physTop  - matchingMonitor.Top)  / dpiY;

        // Validate: offset must match stored offset within 1 logical pixel.
        const double tolerance = 1.0;
        if (Math.Abs(actualOffsetX - e.OffsetX) > tolerance ||
            Math.Abs(actualOffsetY - e.OffsetY) > tolerance)
            return false;

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

    private static (double dpiX, double dpiY) GetDpi(Window window)
    {
        var src = PresentationSource.FromVisual(window);
        if (src?.CompositionTarget is { } ct)
            return (ct.TransformToDevice.M11, ct.TransformToDevice.M22);
        return (1.0, 1.0);
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
