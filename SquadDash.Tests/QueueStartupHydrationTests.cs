namespace SquadDash.Tests;

[TestFixture]
internal sealed class QueueStartupHydrationTests
{
    [Test]
    public void Startup_ClosesQueuePersistenceBeforePromptControllerCallbacks()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var managerCreated = source.IndexOf(
            "_conversationManager = new TranscriptConversationManager(",
            StringComparison.Ordinal);
        var hydrationBegun = source.IndexOf(
            "_conversationManager.BeginQueuedPromptsStateHydration();",
            managerCreated,
            StringComparison.Ordinal);
        var promptControllerCreated = source.IndexOf(
            "_pec = new PromptExecutionController(",
            hydrationBegun,
            StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(managerCreated, Is.GreaterThanOrEqualTo(0));
            Assert.That(hydrationBegun, Is.GreaterThan(managerCreated));
            Assert.That(promptControllerCreated, Is.GreaterThan(hydrationBegun),
                "Prompt controller callbacks must not be able to persist transient empty queue state.");
        });
    }

    [Test]
    public void WorkspaceLoad_CompletesQueueHydrationBeforeLoopOrPlanResumePolicy()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var load = source.IndexOf(
            "await _conversationManager.LoadWorkspaceConversationAsync();",
            StringComparison.Ordinal);
        var restorePaused = source.IndexOf("var savedQueuePaused =", load, StringComparison.Ordinal);
        var restoreCaret = source.IndexOf("restoredItem.CaretIndex = entry.CaretIndex;", restorePaused, StringComparison.Ordinal);
        var complete = source.IndexOf("CompleteQueueStateHydration();", restoreCaret, StringComparison.Ordinal);
        var resumePolicy = source.IndexOf("LoopStartupResumePolicy.Resolve(", complete, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(restorePaused, Is.GreaterThan(load));
            Assert.That(restoreCaret, Is.GreaterThan(restorePaused));
            Assert.That(complete, Is.GreaterThan(restoreCaret));
            Assert.That(resumePolicy, Is.GreaterThan(complete),
                "Loop/plan resume must see the fully restored queue and pause state.");
        });
    }

    [Test]
    public void Shutdown_CapturesSelectedTabBeforeFlushingItsEditorState()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var shutdown = source.IndexOf("bool queueWasRightmostHeld", StringComparison.Ordinal);
        var selectedTab = source.IndexOf("int? queueActiveTabIndex = GetActiveQueueTabIndex();", shutdown, StringComparison.Ordinal);
        var flushEditor = source.IndexOf("OnQueueTabClicked(null, persistState: false);", selectedTab, StringComparison.Ordinal);
        var persistedIndex = source.IndexOf("activeTabIndex: queueActiveTabIndex", flushEditor, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(selectedTab, Is.GreaterThan(shutdown));
            Assert.That(flushEditor, Is.GreaterThan(selectedTab));
            Assert.That(persistedIndex, Is.GreaterThan(flushEditor));
        });
    }

    [Test]
    public void QueuePauseSetting_IsNotWrittenUntilHydrationCompletes()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var methodStart = source.IndexOf("private void SetQueuePaused(bool paused)", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private void SyncPromptTextBoxSimBorder()", methodStart, StringComparison.Ordinal);
        var method = source[methodStart..methodEnd];

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("if (!_queueStateHydrationInProgress)"));
            Assert.That(method, Does.Contain("SaveDocsPanelState"));
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
