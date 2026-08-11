namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanRecoveryAssessmentTranscriptOrderingTests
{
    [Test]
    public void PartialAssessment_PublishesStatusBeforeStartingRecoveredTurn()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var partialCase = ExtractCase(
            source,
            "case PlanRecoveryClassification.Partial:",
            "case PlanRecoveryClassification.NotStarted:");

        AssertStatusPrecedesRetry(partialCase);
        Assert.That(partialCase, Does.Contain("assessedContinuation"),
            "Partial recovery must still pass its bounded remaining-work context into the resumed turn.");
    }

    [Test]
    public void NotStartedAssessment_PublishesStatusBeforeStartingRecoveredTurn()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var notStartedCase = ExtractCase(
            source,
            "case PlanRecoveryClassification.NotStarted:",
            "default:");

        AssertStatusPrecedesRetry(notStartedCase);
    }

    private static void AssertStatusPrecedesRetry(string source)
    {
        var statusIndex = source.IndexOf("ShowSystemTranscriptEntry(", StringComparison.Ordinal);
        var retryIndex = source.IndexOf("await RetryDecomposeTaskAsync(", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(statusIndex, Is.GreaterThanOrEqualTo(0), "Recovery status publication was not found.");
            Assert.That(retryIndex, Is.GreaterThanOrEqualTo(0), "Recovered-turn startup was not found.");
            Assert.That(statusIndex, Is.LessThan(retryIndex),
                "A recovery status entry must be finalized before the resumed coordinator turn starts streaming.");
        });
    }

    private static string ExtractCase(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Could not find {startMarker}");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), $"Could not find {endMarker} after {startMarker}");
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

        Assert.Fail($"Could not find {Path.Combine(pathParts)} from {TestContext.CurrentContext.TestDirectory}.");
        return string.Empty;
    }
}
