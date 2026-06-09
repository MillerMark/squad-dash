using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;

namespace SquadDash.Tests;

/// <summary>
/// Tests for <see cref="FloatingWindowPositionStore"/>, with focus on the
/// multi-monitor / negative-coordinate restore path.
/// </summary>
[TestFixture]
internal sealed class FloatingWindowPositionStoreTests
{
    private string _tempDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fwps_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── TryRestore — no entry ────────────────────────────────────────────────

    [Test]
    public void TryRestore_ReturnsFalse_WhenKeyNotFound()
    {
        var store = new FloatingWindowPositionStore(_tempDir);
        WpfTestContext.Run(() =>
        {
            var window = new Window();
            var result = store.TryRestoreCore("missing", window,
                _ => new Rect(0, 0, 1920, 1080));

            Assert.That(result, Is.False);
        });
    }

    // ── TryRestore — monitor gone ────────────────────────────────────────────

    [Test]
    public void TryRestore_ReturnsFalse_WhenMonitorNoLongerPresent()
    {
        WriteEntries(new()
        {
            ["win1"] = MakeEntry(left: -50, top: -700, monitorTop: -1080)
        });
        var store = new FloatingWindowPositionStore(_tempDir);

        WpfTestContext.Run(() =>
        {
            var window = new Window();
            // Simulate monitor not found.
            var result = store.TryRestoreCore("win1", window, _ => Rect.Empty);

            Assert.That(result, Is.False);
            // Window position should not have been touched.
            Assert.That(double.IsNaN(window.Left), Is.True);
        });
    }

    // ── TryRestore — negative coordinates (bug regression) ──────────────────

    /// <summary>
    /// Reproduces the original bug: a window saved on a secondary monitor above the
    /// primary (negative Y) must be restored to those negative coordinates, not
    /// centred on the owner.  Previously the DPI-based offset check incorrectly
    /// returned false when called before the window was shown (DPI fell back to 1.0),
    /// leaving WindowStartupLocation as CenterOwner.
    /// </summary>
    [Test]
    public void TryRestore_AppliesNegativeCoordinates_WhenMonitorMatches()
    {
        const double savedLeft   = -50.0;
        const double savedTop    = -700.0;
        const double savedWidth  = 1312.0;
        const double savedHeight = 825.0;

        // Monitor above primary: physical rect (0, -1080, 1920, 1080), DPI was 1.25 at save time.
        WriteEntries(new()
        {
            ["InboxMessage:msg-001"] = MakeEntry(
                left: savedLeft, top: savedTop,
                width: savedWidth, height: savedHeight,
                monitorLeft: 0, monitorTop: -1080, monitorWidth: 1920, monitorHeight: 1080,
                offsetX: -50.0, offsetY: 64.0)
        });

        var store = new FloatingWindowPositionStore(_tempDir);

        WpfTestContext.Run(() =>
        {
            var window = new Window
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width  = 100,
                Height = 100,
            };

            // Inject a fake monitor-finder that confirms the monitor is present.
            var syntheticMonitor = new Rect(0, -1080, 1920, 1080);
            var result = store.TryRestoreCore(
                "InboxMessage:msg-001", window, _ => syntheticMonitor);

            Assert.That(result, Is.True, "TryRestore should succeed when monitor is present");
            Assert.That(window.Left,   Is.EqualTo(savedLeft).Within(0.01));
            Assert.That(window.Top,    Is.EqualTo(savedTop).Within(0.01));
            Assert.That(window.Width,  Is.EqualTo(savedWidth).Within(0.01));
            Assert.That(window.Height, Is.EqualTo(savedHeight).Within(0.01));
            Assert.That(window.WindowStartupLocation, Is.EqualTo(WindowStartupLocation.Manual),
                "WindowStartupLocation must be Manual so Show() uses saved Left/Top");
        });
    }

    // ── Round-trip: negative coordinates survive JSON ────────────────────────

    [Test]
    public void Load_DeserializesNegativeCoordinates_Correctly()
    {
        WriteEntries(new()
        {
            ["check"] = MakeEntry(left: -200.5, top: -1050.0, monitorTop: -1080)
        });

        // A synthetic monitor so we can inspect what TryRestore would apply.
        var store = new FloatingWindowPositionStore(_tempDir);

        WpfTestContext.Run(() =>
        {
            var window = new Window();
            var syntheticMonitor = new Rect(0, -1080, 1920, 1080);
            var ok = store.TryRestoreCore("check", window, _ => syntheticMonitor);

            Assert.That(ok, Is.True);
            Assert.That(window.Left, Is.EqualTo(-200.5).Within(0.01));
            Assert.That(window.Top,  Is.EqualTo(-1050.0).Within(0.01));
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void WriteEntries(Dictionary<string, object> entries)
    {
        var json = JsonSerializer.Serialize(
            entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(
            Path.Combine(_tempDir, "floating-window-positions.json"), json);
    }

    private static object MakeEntry(
        double left = 100, double top = 200,
        double width = 800, double height = 600,
        double monitorLeft = 0, double monitorTop = 0,
        double monitorWidth = 1920, double monitorHeight = 1080,
        double offsetX = 100, double offsetY = 200)
        => new
        {
            Left          = left,
            Top           = top,
            Width         = width,
            Height        = height,
            MonitorLeft   = monitorLeft,
            MonitorTop    = monitorTop,
            MonitorWidth  = monitorWidth,
            MonitorHeight = monitorHeight,
            OffsetX       = offsetX,
            OffsetY       = offsetY,
        };
}
