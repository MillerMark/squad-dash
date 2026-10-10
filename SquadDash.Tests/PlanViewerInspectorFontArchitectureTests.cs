using System.Text.RegularExpressions;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanViewerInspectorFontArchitectureTests
{
    [Test]
    public void CtrlWheel_ConsumesOnlyControlGestureAndPersistsTheAppliedScale()
    {
        var source = ReadViewer();
        var handler = Method(source, "private void OnInspectorPreviewMouseWheel(",
            "private void OnGraphScrollPreviewMouseWheel(");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain(
                "detailViewer.PreviewMouseWheel += OnInspectorPreviewMouseWheel;"));
            Assert.That(handler, Does.Match(
                @"if \(Keyboard.Modifiers != ModifierKeys.Control\)\s+return;\s+e.Handled = true;"));
            Assert.That(handler, Does.Contain("if (e.Delta == 0)")
                .And.Contain("Math.Pow(1.1, e.Delta / 120.0)")
                .And.Contain("Math.Clamp(")
                .And.Contain("PlanVisualizerViewportStore.MinimumInspectorFontScale")
                .And.Contain("PlanVisualizerViewportStore.MaximumInspectorFontScale"));
            Assert.That(handler, Does.Match(
                @"_inspectorFontScale = scale;\s+ApplyInspectorFontScale\(\);\s+try\s+\{\s+if \(!_isReadOnlyFixture\)\s+_viewportStore.SaveInspectorFontScale\(_inspectorFontScale\);"),
                "Normal viewers persist the applied scale; approval fixtures must not change global preferences.");
            Assert.That(handler, Does.Not.Contain("SetFontSizeScale")
                .And.Not.Contain("SaveDefaultZoom")
                .And.Not.Contain("_zoomFactor")
                .And.Not.Contain("Application.Current"));
            Assert.That(handler, Does.Contain("SquadDashTrace.Write(")
                .And.Contain("ShowActionStatus(\"Inspector font size could not be saved\""));
        });
    }

    [Test]
    public void DocumentZoom_ScalesExplicitFontsAndEmbeddedControlsWithoutChangingThemeResources()
    {
        var source = ReadViewer();
        var apply = Method(source, "private void ApplyInspectorFontScale(",
            "private void OnInspectorPreviewMouseWheel(");
        var construction = Method(source, "var detailViewer = new FlowDocumentScrollViewer",
            "detailViewer.MouseDown +=");

        Assert.Multiple(() =>
        {
            Assert.That(apply, Does.Contain("_detailViewer.Zoom = _inspectorFontScale * 100.0;"));
            Assert.That(construction, Does.Contain(
                    "MinZoom = PlanVisualizerViewportStore.MinimumInspectorFontScale * 100.0,")
                .And.Contain("MaxZoom = PlanVisualizerViewportStore.MaximumInspectorFontScale * 100.0,"));
            Assert.That(source, Does.Contain(
                    "detailDocument.SetResourceReference(FlowDocument.FontSizeProperty, \"FontSizeMedium\");")
                .And.Contain("new BlockUIContainer(panel)"),
                "Keep theme typography and embedded controls; native document zoom scales both.");
            Assert.That(apply, Does.Not.Contain("Resources[")
                .And.Not.Contain("_graphCanvas"));
        });
    }

    [Test]
    public void EveryInspectorViewerConstruction_RestoresGlobalScaleExceptReadOnlyFixtures()
    {
        var source = ReadViewer();
        var build = Method(source, "private void BuildContent(",
            "private PlanTaskCorrectionActivityState ResolveTaskCorrectionActivity(");
        var restore = Method(source, "private void RestoreInspectorFontScale(",
            "private void ApplyInspectorFontScale(");

        Assert.Multiple(() =>
        {
            Assert.That(Regex.Matches(source, @"new FlowDocumentScrollViewer\b"), Has.Count.EqualTo(1),
                "Any additional inspector construction path must also restore global zoom.");
            Assert.That(build, Does.Match(
                @"_detailViewer = detailViewer;\s+RestoreInspectorFontScale\(\);"));
            Assert.That(build.IndexOf("RestoreInspectorFontScale();", StringComparison.Ordinal),
                Is.LessThan(build.IndexOf("_contentHolder.Child = root;", StringComparison.Ordinal)));
            Assert.That(restore, Does.Match(
                @"_inspectorFontScale = _isReadOnlyFixture \? 1.0 : _viewportStore.LoadInspectorFontScale\(\);\s+ApplyInspectorFontScale\(\);"),
                "Normal viewers reload shared storage; fixtures use a deterministic scale without changing it.");
        });
    }

    [Test]
    public void ReloadLiveProgressAndEnvironmentChanges_AllReachInspectorConstruction()
    {
        var source = ReadViewer();
        var refresh = Method(source, "internal void RefreshPlan(", "private bool ApplyStagedMutation(");
        var live = Method(source, "private void ApplyLiveUpdate(", "internal void NotifyFontSizeChanged(");
        var font = Method(source, "internal void NotifyFontSizeChanged(", "private void BuildContent(");
        var rebuild = Method(source, "private void RebuildPreservingScroll(\n", "internal void RefreshPlan(");
        var constructor = Method(source, "internal PlanViewerWindow(", "private void RestoreInspectorFontScale(");

        Assert.Multiple(() =>
        {
            Assert.That(constructor, Does.Contain("BuildContent(plan, durablePlan);"));
            Assert.That(refresh, Does.Contain("RebuildPreservingScroll(displayedPlan, displayedDurablePlan);"));
            Assert.That(live, Does.Contain(
                "RebuildPreservingScroll(updatedDefinition, displayedPlan, autoScrollForPlanProgress);"));
            Assert.That(font, Does.Contain("RebuildPreservingScroll(_plan, _durablePlan);"));
            Assert.That(rebuild, Does.Contain("BuildContent(plan, durablePlan);"));
        });
    }

    [Test]
    public void DetailOnlyRefresh_RestoresBeforeSelectionAndEditorEarlyReturns()
    {
        var source = ReadViewer();
        var refresh = Method(source, "private void RefreshDetailPanel()", "private void PrependInspectorToolbar(");
        var restoreIndex = refresh.IndexOf("RestoreInspectorFontScale();", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(restoreIndex, Is.GreaterThanOrEqualTo(0)
                .And.LessThan(refresh.IndexOf("TryPopulateStagedEditor()", StringComparison.Ordinal))
                .And.LessThan(refresh.IndexOf("if (_selectedElement is null)", StringComparison.Ordinal))
                .And.LessThan(refresh.IndexOf("PopulateTaskDetail(", StringComparison.Ordinal)));
            Assert.That(refresh, Does.Not.Contain("_inspectorFontScale = 1"),
                "Selection, editor rebuilds and state advances must not reset the inspector scale.");
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
             directory is not null;
             directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "SquadDash", "PlanViewerWindow.cs");
            if (File.Exists(path))
                return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        throw new FileNotFoundException("Could not locate PlanViewerWindow.cs.");
    }
}
