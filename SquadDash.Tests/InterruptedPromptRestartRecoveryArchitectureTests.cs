namespace SquadDash.Tests;

[TestFixture]
internal sealed class InterruptedPromptRestartRecoveryArchitectureTests
{
    [Test]
    public void TimeoutRecovery_RetiresDirectHandoffBlockers()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var start = source.IndexOf("private void EnterInterruptedPromptRecoveryHold", StringComparison.Ordinal);
        var end = source.IndexOf("private void ReleaseInterruptedPromptRecoveryHold", start, StringComparison.Ordinal);

        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));
        Assert.That(source[start..end], Does.Contain("RetireDirectQuickReplyHandoffBlockers(\"recoverable-prompt-timeout\")"));
    }

    [Test]
    public void FirstTimeout_DurablyConsumesAutomaticAttemptBeforeContinuation()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var start = source.IndexOf(
            "private async Task ContinueInterruptedPromptAutomaticallyAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private void EnterInterruptedPromptRecoveryHold",
            start,
            StringComparison.Ordinal);

        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));
        var method = source[start..end];
        Assert.Multiple(() => {
            Assert.That(method, Does.Contain("AutomaticContinuationAttempted = true"));
            Assert.That(method.IndexOf("store!.Save(attempted)", StringComparison.Ordinal),
                Is.LessThan(method.IndexOf(
                    "ContinueInterruptedPromptAsync(attempted, automatic: true)",
                    StringComparison.Ordinal)));
            Assert.That(method, Does.Contain("BuildCardText(attempted)"));
        });
    }

    [Test]
    public void ClearTranscript_AlsoClearsInterruptedPromptRecovery()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PromptExecutionController.cs"));
        var start = source.IndexOf("private bool HandleClearConfirmed", StringComparison.Ordinal);
        var end = source.IndexOf("private void MaybeClearPromptAfterLocalCommand", start, StringComparison.Ordinal);

        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));
        Assert.That(source[start..end], Does.Contain(
            "ClearInterruptedPromptRecovery(workspace, \"transcript-cleared\")"));
    }

    [Test]
    public void RestartPanel_UsesTheCurrentDeferralReason()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var start = source.IndexOf("private void UpdateRestartStatusPanelVisibility", StringComparison.Ordinal);
        var end = source.IndexOf("private void OnDocRevisionCompleted", start, StringComparison.Ordinal);

        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));
        Assert.Multiple(() => {
            Assert.That(source[start..end], Does.Contain("GetRestartDeferralReason()"));
            Assert.That(source[start..end], Does.Contain("RestartDeferralPolicy.BuildStatusMessage(blocker)"));
        });
    }

    [Test]
    public void DeadPromptRestart_DurablyCheckpointsBeforeClosingBridge()
    {
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var controller = File.ReadAllText(FindRepoFile("SquadDash", "PromptExecutionController.cs"));
        var restart = Slice(
            main,
            "private bool TryCompletePendingRestart(",
            "private void UpdateRestartStatusPanelVisibility");
        var checkpoint = Slice(
            controller,
            "internal bool CheckpointRunningPromptForRestart(",
            "private InterruptedPromptRecoveryState SaveInterruptedPromptRecovery(");

        Assert.Multiple(() =>
        {
            Assert.That(restart, Does.Contain("_pec.CheckpointRunningPromptForRestart(reason)"));
            Assert.That(
                restart.IndexOf("CheckpointRunningPromptForRestart", StringComparison.Ordinal),
                Is.LessThan(restart.IndexOf("Close();", StringComparison.Ordinal)));
            Assert.That(checkpoint, Does.Contain("SaveInterruptedPromptRecovery("));
            Assert.That(checkpoint, Does.Contain("_activeRecoveryWorkspace"));
            Assert.That(checkpoint, Does.Contain("_activePromptRestartCheckpointed = true"));
        });
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));
        return source[start..end];
    }

    private static string FindRepoFile(params string[] pathParts)
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. pathParts]);
            if (File.Exists(candidate))
                return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file '{Path.Combine(pathParts)}'.");
    }
}
