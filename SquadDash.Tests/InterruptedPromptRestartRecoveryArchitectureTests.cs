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
