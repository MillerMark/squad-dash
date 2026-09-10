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
        Assert.That(source[start..end], Does.Contain("RetireDirectQuickReplyHandoffBlockers(\"bridge-inactivity-timeout\")"));
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
