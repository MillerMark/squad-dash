namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanRecoveryTranscriptHydrationTests
{
    [Test]
    public void WorkspaceLoad_RestoresInterruptedPlanActionsAfterTranscriptHydrationAndRepair()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        const string loadCall = "await _conversationManager.LoadWorkspaceConversationAsync();";
        const string repairCall = "RepairStalePlanExecutingState();";
        const string restoreCall = "RestoreInterruptedPlanRecoverySurfaces();";

        var loadIndex = source.IndexOf(loadCall, StringComparison.Ordinal);
        var repairIndex = source.IndexOf(repairCall, loadIndex, StringComparison.Ordinal);
        var restoreIndex = source.IndexOf(restoreCall, repairIndex, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(loadIndex, Is.GreaterThanOrEqualTo(0), "Conversation hydration call was not found.");
            Assert.That(repairIndex, Is.GreaterThan(loadIndex), "Stale plan repair must run after hydration.");
            Assert.That(restoreIndex, Is.GreaterThan(repairIndex),
                "Interrupted-plan recovery controls must be reconstructed after hydration and stale-state repair.");
        });
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

        Assert.Fail($"Could not find {Path.Combine(pathParts)} from {TestContext.CurrentContext.TestDirectory}.");
        return string.Empty;
    }
}
