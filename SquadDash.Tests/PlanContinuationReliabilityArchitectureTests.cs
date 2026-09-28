using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanContinuationReliabilityArchitectureTests
{
    [Test]
    public void DeferredPlanContinuation_IsPersistedReclaimedAndClearedOnlyAfterStart()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var apply = Method(
            source,
            "private async Task ApplyDecomposeRecoveryFromResponseSafelyAsync",
            "private void TryDispatchPendingDecomposeRecoveryResponse");
        var watchdog = Method(
            source,
            "private void EnsurePendingPlanContinuationDispatched()",
            "private void RestorePendingPlanContinuation()");
        var start = Method(
            source,
            "private async Task<bool> StartDecomposeLoopAsync(",
            "private void TryStartDecomposeGroupFromResponse");

        Assert.Multiple(() =>
        {
            Assert.That(apply, Does.Contain("UpdatePendingPlanContinuationState("));
            Assert.That(watchdog, Does.Contain("Reclaiming a deferred plan-continuation dispatch")
                .And.Contain("BuildProtocolResponse()"));
            Assert.That(start, Does.Contain("await _loopController.StartAsync(")
                .And.Contain("UpdatePendingPlanContinuationState(null)")
                .And.Contain("Cleared durable plan continuation after confirmed plan start"));
        });
    }

    [Test]
    public void RestartRequest_PersistsPlanCheckpointBeforeShutdown()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var handler = Method(
            source,
            "private void HandleRestartRequestChanged()",
            "private void TryAdoptPendingRestartRequestForClose()");

        Assert.That(handler, Does.Contain("RestartCheckpointRequestedAt = DateTimeOffset.UtcNow")
            .And.Contain("UpdateActiveLoopExecutionState(")
            .And.Contain("LoopIterationProgressKind.RestartCheckpoint"));
    }

    [Test]
    public void TranscriptAndViewer_ResolveTheSameRecoveryPrimaryAction()
    {
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var viewer = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(main, Does.Contain("PlanRecoveryActionPresentationBuilder.Build(")
                .And.Contain("primaryRecoveryAction.Label")
                .And.Contain("\"End Plan\""));
            Assert.That(viewer, Does.Contain("_resolveInterruptedPrimaryAction?.Invoke(durablePlan)")
                .And.Contain("currentInterruptedPrimaryAction?.Label")
                .And.Contain("\"End Plan\""));
        });
    }

    private static string Method(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Missing {startMarker}");
        Assert.That(end, Is.GreaterThan(start), $"Missing {endMarker}");
        return source[start..end];
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

        Assert.Fail($"Could not find {Path.Combine(pathParts)}.");
        return string.Empty;
    }
}
