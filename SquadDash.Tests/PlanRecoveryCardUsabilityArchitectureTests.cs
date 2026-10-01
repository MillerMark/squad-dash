using System.IO;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanRecoveryCardUsabilityArchitectureTests
{
    [Test]
    public void RecoveryCard_KeepsCurrentEvidenceReadableLinkedAndSeparateFromHistory()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain(
                "bulletParagraph.SetResourceReference(TextElement.ForegroundProperty, \"ImportantText\")"));
            Assert.That(source, Does.Contain(
                "AddRecoveryCommitLinkedText(bulletParagraph.Inlines, bullet.Text)"));
            Assert.That(source, Does.Contain("Header = $\"Previous review ({previousHeaderCommit})\""));
            Assert.That(source, Does.Contain("IsExpanded = false"));
            Assert.That(source, Does.Contain(
                "PlanRecoveryPresentationBuilder.HasCurrentAcceptedReview(durablePlan, taskId)"));
            Assert.That(source, Does.Contain("Changed files ({review.ChangedFiles.Count})"));
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
