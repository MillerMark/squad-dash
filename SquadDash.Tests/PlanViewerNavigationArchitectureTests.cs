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

    private static string ReadViewer()
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
             directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "SquadDash", "PlanViewerWindow.cs");
            if (File.Exists(path))
                return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        }
        throw new FileNotFoundException("Could not locate PlanViewerWindow.cs.");
    }
}
