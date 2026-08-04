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

    [Test]
    public void PersistedTurnRendering_DoesNotAttachCurrentRecoveryActionsToHistoryBatches()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));

        Assert.That(source, Does.Not.Contain("AppendPersistedDecomposeRecoveryIfNeeded"),
            "Current recovery actions must be owned by post-hydration restoration, not by a batch's last persisted turn.");
    }

    [Test]
    public void ExhaustedTaskPlanRepair_RestoresRecoverySurfaceAndReportsSpecificFailure()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var methodStart = source.IndexOf(
            "private void QueueDecomposeRepair(string? validationError)",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf(
            "private void AppendPendingDecomposeApproval(",
            methodStart,
            StringComparison.Ordinal);
        var method = source[methodStart..methodEnd];

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("Specific validation failure:"));
            Assert.That(method, Does.Contain("RestoreInterruptedPlanRecoverySurfaces();"));
            Assert.That(method, Does.Contain("Reason: {validationError}"));
        });
    }

    [Test]
    public void PlansPanel_OpensAuthoritativeDurableRevision_NotPendingProposalWithSameId()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var methodStart = source.IndexOf(
            "private void OpenPlanFromStore(Plan plan)",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf(
            "private void ArchivePlan(Plan plan)",
            methodStart,
            StringComparison.Ordinal);
        var method = source[methodStart..methodEnd];

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("PendingDecomposePlanAdapter.FromPlan(plan)"));
            Assert.That(method, Does.Not.Contain("PendingDecomposePlanStore"),
                "The Plans panel must not replace durable progress with a pending proposal sharing the plan ID.");
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
