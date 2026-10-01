using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanDelegationEvidenceArchitectureTests
{
    [Test]
    public void FailedDelegationWithoutAnAgentThreadDoesNotConsumePrimaryWorkerIdentity()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var startMethodStart = source.IndexOf(
            "private void StartToolExecution(TranscriptThreadState thread, SquadSdkEvent evt)",
            StringComparison.Ordinal);
        var startMethodEnd = source.IndexOf("private void ", startMethodStart + 1, StringComparison.Ordinal);
        var completeMethodStart = source.IndexOf(
            "private void CompleteToolExecution(TranscriptThreadState thread, SquadSdkEvent evt)",
            StringComparison.Ordinal);
        var completeMethodEnd = source.IndexOf("private void ", completeMethodStart + 1, StringComparison.Ordinal);

        Assert.That(startMethodStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(startMethodEnd, Is.GreaterThan(startMethodStart));
        Assert.That(completeMethodStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(completeMethodEnd, Is.GreaterThan(completeMethodStart));

        var startMethod = source[startMethodStart..startMethodEnd];
        var completeMethod = source[completeMethodStart..completeMethodEnd];
        Assert.Multiple(() =>
        {
            Assert.That(startMethod,
                Does.Contain("CapturePlanAwareBackgroundAgentLaunch(thread, evt);"));
            Assert.That(completeMethod, Does.Contain("if (evt.Success == false)")
                .And.Contain("DiscardUnstartedPlanAgentLaunch(evt);")
                .And.Contain("CapturePlanAwareBackgroundAgentLaunch(thread, evt);"));
            Assert.That(source, Does.Contain("ThreadsByToolCallId.ContainsKey(evt.ToolCallId)")
                .And.Contain("PlanAgentLaunchFailurePolicy.ShouldDiscardLaunch(")
                .And.Contain("attempt.DiscardRejectedLaunch(evt.ToolCallId)"));
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
