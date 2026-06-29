using System;
using System.Collections.Generic;
using System.Windows;

namespace SquadDash;

/// <summary>
/// Allows floating windows (e.g. FrmUltimateCallout) that live outside the main
/// WPF window tree to opt in to Theme Reveal (Ctrl+F11) inspection without creating
/// a direct dependency between ThemeRevealOverlay and the window type.
///
/// Call <see cref="Register"/> when the window is shown and
/// <see cref="Unregister"/> (or let the GC clean up) when it is closed.
/// </summary>
internal static class ThemeRevealWindowRegistry
{
    private static readonly List<WeakReference<Window>> _windows = new();

    public static void Register(Window window)
    {
        lock (_windows)
        {
            Prune();
            foreach (var wr in _windows)
            {
                if (wr.TryGetTarget(out var existing) && ReferenceEquals(existing, window))
                    return;
            }
            _windows.Add(new WeakReference<Window>(window));
        }
    }

    public static void Unregister(Window window)
    {
        lock (_windows)
        {
            _windows.RemoveAll(wr =>
                !wr.TryGetTarget(out var target) || ReferenceEquals(target, window));
        }
    }

    /// <summary>Returns all currently-alive registered windows.</summary>
    public static IReadOnlyList<Window> GetWindows()
    {
        lock (_windows)
        {
            Prune();
            var result = new List<Window>();
            foreach (var wr in _windows)
                if (wr.TryGetTarget(out var w))
                    result.Add(w);
            return result;
        }
    }

    private static void Prune()
    {
        _windows.RemoveAll(wr => !wr.TryGetTarget(out _));
    }
}
