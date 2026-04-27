using System;
using System.Windows;

namespace SquadDash;

/// <summary>
/// Event-args raised by <see cref="ScreenshotOverlayWindow.ScreenshotSaved"/> after
/// the PNG has been written to disk.
///
/// Carries the provisional PNG path, the capture region, the four edge anchors
/// (from <see cref="VisualTreeEdgeAnalyzer"/> — analysed before the overlay was hidden),
/// the kebab-case name confirmed by the user in the inline rename UI, and whether
/// the selection covered the entire window.  <c>MainWindow</c>'s handler uses this
/// data to build the <c>ScreenshotManifest</c>, rename the PNG, and upsert the
/// <c>ScreenshotDefinition</c> registry.
/// </summary>
internal sealed class ScreenshotSavedEventArgs : EventArgs
{
    /// <summary>
    /// Full file path to the provisionally-saved PNG.
    /// <c>MainWindow</c> renames this to <c>{AcceptedName}-{theme}.png</c>.
    /// </summary>
    public string PngPath { get; }

    /// <summary>
    /// The capture region in logical coordinates relative to the MainWindow,
    /// snapshotted immediately before the overlay was hidden.
    /// </summary>
    public Rect SelectionRect { get; }

    /// <summary>
    /// The four edge anchors returned by <see cref="VisualTreeEdgeAnalyzer.Analyze"/>
    /// immediately before the overlay was hidden.
    /// Order: [0] Top, [1] Right, [2] Bottom, [3] Left.
    /// </summary>
    public EdgeAnchor[] Anchors { get; }

    /// <summary>
    /// <c>true</c> when the selection covered the entire MainWindow client area.
    /// Used to set the manifest's <c>Region</c> field to <c>"full"</c> vs <c>"custom"</c>.
    /// </summary>
    public bool IsFullWindow { get; }

    /// <summary>
    /// The kebab-case screenshot name confirmed by the user in the overlay rename UI.
    /// Owned by the overlay — <c>MainWindow</c> uses this directly and does not
    /// call <c>ScreenshotNamingHelper.SuggestName</c>.
    /// </summary>
    public string AcceptedName { get; }

    internal ScreenshotSavedEventArgs(
        string       pngPath,
        Rect         selectionRect,
        EdgeAnchor[] anchors,
        bool         isFullWindow,
        string       acceptedName)
    {
        PngPath       = pngPath;
        SelectionRect = selectionRect;
        Anchors       = anchors;
        IsFullWindow  = isFullWindow;
        AcceptedName  = acceptedName;
    }
}
