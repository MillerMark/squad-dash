using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanViewerRunnerStateArchitectureTests
{
    [Test]
    public void NativeRunnerTransitionsRefreshMatchingOpenPlanViewerControls()
    {
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var viewer = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var started = Method(main,
            "private void OnNativeLoopIterationStarted(int iteration)",
            "private void OnNativeLoopStopped()");
        var stopped = Method(main,
            "private void OnNativeLoopStopped()",
            "private void OnNativeLoopError(");
        var error = Method(main,
            "private void OnNativeLoopError(",
            "private void PausePlanForCleanWorktree(");
        var setState = Method(main,
            "private void SetNativePlanRunnerActive(",
            "private void RefreshOpenPlanViewerRunnerState(");
        var refresh = Method(main,
            "private void RefreshOpenPlanViewerRunnerState(",
            "private void OnNativeLoopWaiting(");
        var viewerRefresh = Method(viewer,
            "internal void NotifyPlanRunnerStateChanged()",
            "private void BuildContent(");

        Assert.Multiple(() =>
        {
            Assert.That(started,
                Does.Contain("SetNativePlanRunnerActive(true, _activeDecomposeGroupId);"));
            Assert.That(stopped,
                Does.Contain("SetNativePlanRunnerActive(false, stoppedPlanId);"));
            Assert.That(error,
                Does.Contain("SetNativePlanRunnerActive(false, blockedGroupId);"));
            Assert.That(setState, Does.Contain("_pec.SetIsLoopRunning(active);")
                .And.Contain("RefreshOpenPlanViewerRunnerState(planId);"));
            Assert.That(refresh, Does.Contain("_openPlanViewerWindows.Where")
                .And.Contain("string.Equals(entry.GroupId, planId, StringComparison.Ordinal)")
                .And.Contain("entry.Window.NotifyPlanRunnerStateChanged();"));
            Assert.That(viewerRefresh,
                Does.Contain("RebuildPreservingScroll(_plan, _durablePlan);"));
        });
    }

    private static string Method(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Missing {startMarker}");
        Assert.That(end, Is.GreaterThan(start), $"Missing {endMarker}");
        return source[start..end];
    }

    private static string FindRepoFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        Assert.Fail($"Could not find {Path.Combine(pathParts)} from {TestContext.CurrentContext.TestDirectory}.");
        return string.Empty;
    }
}
