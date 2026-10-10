using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanViewerNavigationArchitectureTests
{
    [Test]
    public void SpacePan_UsesPreviewInputAndLeavesEditorsAlone()
    {
        var source = ReadViewer();
        var keyHandler = Method(source, "private void OnPlanViewerPreviewKeyDown(",
            "private void CommitTaskCardDrag(");
        var mouseHandler = Method(source, "private void OnGraphPanMouseDown(",
            "private void OnGraphPanMouseMove(");

        Assert.Multiple(() =>
        {
            Assert.That(keyHandler, Does.Contain("e.Key == Key.Space")
                .And.Contain("_graphScroll?.IsMouseOver == true")
                .And.Contain("!IsGraphTextInput(Keyboard.FocusedElement)"));
            Assert.That(source, Does.Contain("element is TextBoxBase or PasswordBox or ComboBox { IsEditable: true }"));
            Assert.That(mouseHandler, Does.Contain("IsGraphTextInput(Keyboard.FocusedElement)")
                .And.Contain("_taskCardDragState is not null")
                .And.Contain("_graphScroll.CaptureMouse()")
                .And.Contain("e.Handled = true;"));
            Assert.That(source, Does.Contain("scroll.PreviewMouseLeftButtonDown += OnGraphPanMouseDown;")
                .And.Contain("scroll.PreviewMouseMove += OnGraphPanMouseMove;")
                .And.Contain("scroll.PreviewMouseLeftButtonUp += OnGraphPanMouseUp;"));
            Assert.That(source, Does.Contain("AnnotationCursors.ClosedHand : AnnotationCursors.OpenHand")
                .And.Contain("_graphScroll.ForceCursor = true;")
                .And.Contain("_panHorizontalOffset - delta.X")
                .And.Contain("_panVerticalOffset - delta.Y"));
        });
    }

    [Test]
    public void PanCapture_IsReleasedOnKeyUpDeactivationAndRebuild()
    {
        var source = ReadViewer();
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("PreviewKeyUp += OnGraphPanPreviewKeyUp;")
                .And.Contain("Deactivated += (_, _) => ReleaseGraphPan();")
                .And.Contain("scroll.LostMouseCapture +="));
            Assert.That(Method(source, "private void OnGraphPanPreviewKeyUp(",
                "private void OnGraphPanMouseEnter("), Does.Contain("ReleaseGraphPan();"));
            Assert.That(Method(source, "private void EndGraphPan(",
                "private void ReleaseGraphPan("), Does.Contain("_graphScroll.ReleaseMouseCapture();"));
            Assert.That(Method(source, "private void BuildContent(",
                "private PlanTaskCorrectionActivityState ResolveTaskCorrectionActivity("),
                Does.Contain("EndGraphPan();"));
        });
    }

    [Test]
    public void CtrlWheel_AnchorsGraphPointWithoutWritingGlobalFont()
    {
        var handler = Method(ReadViewer(), "private void OnGraphScrollPreviewMouseWheel(",
            "private static bool IsGraphTextInput(");
        Assert.Multiple(() =>
        {
            Assert.That(handler, Does.Contain("e.GetPosition(_graphCanvas)")
                .And.Contain("e.GetPosition(_graphScroll)")
                .And.Contain("_graphCanvas.TranslatePoint(anchor, _graphScroll)")
                .And.Contain("movedAnchor.X - pointer.X")
                .And.Contain("movedAnchor.Y - pointer.Y"));
            Assert.That(handler, Does.Contain("PlanVisualizerViewportStore.MinimumZoom")
                .And.Contain("PlanVisualizerViewportStore.MaximumZoom")
                .And.Contain("_viewportStore.SaveDefaultZoom(zoom)"));
            Assert.That(handler, Does.Not.Contain("FontSizeBody")
                .And.Not.Contain("SetFontSizeScale")
                .And.Not.Contain("_onFontScaleDelta"));
        });
    }

    [Test]
    public void SavedViewport_WinsOnlyOnOpenWhileLiveProgressRetainsQuietPeriod()
    {
        var source = ReadViewer();
        var initial = Method(source, "private void OnInitialContentRendered(",
            "private void ApplyInitialWindowLayout(");
        var refresh = Method(source, "private void RebuildPreservingScroll(\n",
            "internal void RefreshPlan(");
        Assert.Multiple(() =>
        {
            Assert.That(initial, Does.Contain("if (_initialViewport is not null)")
                .And.Contain("RestoreGraphViewport(_initialViewport);")
                .And.Contain("else\n                EnsureZoomedAutomaticScrollTargetVisible();"));
            Assert.That(refresh, Does.Contain("RestoreGraphViewport(viewport);")
                .And.Contain("_autoScrollForPlanProgressPending |= autoScrollForPlanProgress;")
                .And.Contain("if (_autoScrollForPlanProgressPending &&")
                .And.Contain("_autoScrollForPlanProgressPending = false;")
                .And.Contain("PlanViewerAutoScrollPolicy.IsInteractionQuiet(_lastViewerInteractionUtc, DateTime.UtcNow)")
                .And.Contain("EnsureZoomedAutomaticScrollTargetVisible();"));
            Assert.That(source, Does.Contain("Closing += (_, _) => SaveGraphViewport();")
                .And.Contain("scroll.ScrollChanged +="));
        });
    }

    [Test]
    public void RestoredWindowGeometry_WinsOverContentSizingBeforeViewportOffsetsAreRestored()
    {
        var source = ReadViewer();
        var restore = Method(source, "internal void RestoreWindowGeometry(",
            "private void OnInitialContentRendered(");
        var initial = Method(source, "private void OnInitialContentRendered(",
            "private void ApplyInitialWindowLayout(");
        var host = ReadSource("MainWindow.xaml.cs");
        var open = Method(host, "win.RestoreWindowGeometry(FloatingWindowPositionStore.Shared);",
            "async Task<bool> ApplyFromViewerAsync(");

        Assert.Multiple(() =>
        {
            Assert.That(restore, Does.Contain(
                "_windowGeometryRestored = positionStore.TryRestore($\"PlanViewer:{_viewportGroupId}\", this);"));
            Assert.That(initial, Does.Contain(
                "if (!_windowGeometryRestored)\n            ApplyInitialWindowLayout();"));
            Assert.That(initial.IndexOf("_initialLayoutApplied = true;", StringComparison.Ordinal),
                Is.GreaterThan(initial.IndexOf("ApplyInitialWindowLayout();", StringComparison.Ordinal)));
            Assert.That(initial.IndexOf("RestoreGraphViewport(_initialViewport);", StringComparison.Ordinal),
                Is.GreaterThan(initial.IndexOf("Dispatcher.BeginInvoke(", StringComparison.Ordinal)));
            Assert.That(initial, Does.Not.Contain("Height =").And.Not.Contain("Width =")
                .And.Not.Contain("KeepWindowInside("));
            Assert.That(open.IndexOf("win.Show();", StringComparison.Ordinal), Is.GreaterThan(0),
                "Restore success must be recorded before Show can raise ContentRendered.");
            Assert.That(host, Does.Contain(
                "win.Closed += (_, _) => FloatingWindowPositionStore.Shared.Save($\"PlanViewer:{groupId}\", win);")
                .And.Contain("FloatingWindowPositionStore.Shared.Save($\"PlanViewer:{gid}\", planWin);"),
                "Close and application shutdown must retain the same persistence keys.");
        });
    }

    [Test]
    public void FirstOpenSizing_UsesAlreadyRestoredZoomAndOnlyScalesGraphDimensions()
    {
        var source = ReadViewer();
        var sizing = Method(source, "private void ApplyInitialWindowLayout(",
            "private Rect GetLogicalWorkArea(");
        var constructor = Method(source, "internal PlanViewerWindow(",
            "Closing += (_, _) => SaveGraphViewport();");

        Assert.Multiple(() =>
        {
            Assert.That(constructor, Does.Contain(
                "_zoomFactor = isReadOnlyFixture ? 1.0 : _initialViewport?.Zoom ?? _viewportStore.LoadDefaultZoom();"));
            Assert.That(constructor.IndexOf("_zoomFactor = ", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0).And.LessThan(
                    constructor.IndexOf("BuildContent(plan, durablePlan);", StringComparison.Ordinal)));
            Assert.That(source, Does.Contain("LayoutTransform     = new ScaleTransform(_zoomFactor, _zoomFactor)"));
            Assert.That(sizing, Does.Contain("_canvasContentWidth * _zoomFactor + graphHorizontalInsets")
                .And.Contain("_canvasContentHeight * _zoomFactor + canvasVerticalInsets"));
            Assert.That(sizing, Does.Contain("Math.Min(workArea.Width, Math.Max(MinWidth, idealWidth))")
                .And.Contain("Math.Min(workArea.Height, Math.Max(MinHeight, fixedHeight))")
                .And.Contain("KeepWindowInside(workArea);"),
                "First-open and missing-monitor fallback must still fit the current work area.");
        });
    }

    [Test]
    public void AutoScrollCoordinateAdapter_PreservesUnscaledTargetForSubsequentUpdates()
    {
        var adapter = Method(ReadViewer(), "private void EnsureZoomedAutomaticScrollTargetVisible(",
            "private void ApplyLiveUpdate(");
        Assert.Multiple(() =>
        {
            Assert.That(adapter, Does.Contain("margin + (start - margin) * _zoomFactor")
                .And.Contain("margin + (end - margin) * _zoomFactor")
                .And.Contain("EnsureAutomaticScrollTargetVisible();"));
            Assert.That(adapter, Does.Contain("finally")
                .And.Contain("_activeVisibilityStart = start;")
                .And.Contain("_activeVisibilityEnd = end;"));
        });
    }

    private static string Method(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), startMarker);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), endMarker);
        return source[start..end];
    }

    private static string ReadViewer() => ReadSource("PlanViewerWindow.cs");

    private static string ReadSource(string fileName)
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
             directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "SquadDash", fileName);
            if (File.Exists(path))
                return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        }
        throw new FileNotFoundException($"Could not locate {fileName}.");
    }
}
