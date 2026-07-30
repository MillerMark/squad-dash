using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanViewerRefreshArchitectureTests
{
    [Test]
    public void RefreshPlan_RebuildsInPlaceWithoutConstructingAnotherWindow()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var methodStart = source.IndexOf("internal void RefreshPlan", StringComparison.Ordinal);
        var nextMethod = source.IndexOf("private static ToolTip BuildTaskToolTip", methodStart, StringComparison.Ordinal);

        Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(nextMethod, Is.GreaterThan(methodStart));

        var refreshMethod = source[methodStart..nextMethod];
        Assert.Multiple(() =>
        {
            Assert.That(refreshMethod, Does.Contain("RebuildPreservingScroll(plan, durablePlan)"),
                "Refresh should rebuild the existing viewer's visual tree through the scroll-preserving helper.");
            Assert.That(source, Does.Contain("private void RebuildPreservingScroll(PendingDecomposePlan plan, Plan? durablePlan)")
                .And.Contain("BuildContent(plan, durablePlan)"),
                "The shared helper must rebuild this existing viewer rather than constructing another window.");
            Assert.That(refreshMethod, Does.Not.Contain("new PlanViewerWindow"),
                "Constructing a hidden Window during refresh can keep WPF alive during restart and binds handlers to the wrong owner.");
        });
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
