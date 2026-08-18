using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class DurablePlanStartProjectionTests
{
    [Test]
    public void StartDecomposeLoop_DurablePlanRepairsManagedProjectionBeforeReadingIt()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var method = ExtractMethod(
            source,
            "private async Task<bool> StartDecomposeLoopAsync(",
            "private void TryStartDecomposeGroupFromResponse(");

        var loadIndex = method.IndexOf("_planStore?.Load(groupId)", StringComparison.Ordinal);
        var repairIndex = method.IndexOf(
            "PlanTaskProjectionRepair.Ensure(tasksPath, durablePlan)",
            StringComparison.Ordinal);
        var readIndex = method.IndexOf(
            "PlanTaskProjectionRepair.ReadManagedProjection(tasksPath, groupId)",
            StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(loadIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(repairIndex, Is.GreaterThan(loadIndex));
            Assert.That(readIndex, Is.GreaterThan(repairIndex));
            Assert.That(method, Does.Contain("HostRevision = durablePlan.Revision"));
        });
    }

    [Test]
    public void CollectedApprovedPlan_PreparesCanonicalBranchBeforeStarting()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var method = ExtractMethod(
            source,
            "private async Task<bool> StartDurablePlanExecutionAsync(",
            "private void RepairActivePlanTaskProjections(");

        var prepareIndex = method.IndexOf("PrepareDecomposeBranchAsync(", StringComparison.Ordinal);
        var startIndex = method.IndexOf("StartDecomposeLoopAsync(plan.PlanId)", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("PlanLifecycleStatus.Approved"));
            Assert.That(prepareIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(startIndex, Is.GreaterThan(prepareIndex));
        });
    }

    private static string ExtractMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Missing {startMarker}");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), $"Missing {endMarker}");
        return source[start..end];
    }

    private static string FindRepoFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, pathParts));
    }
}
