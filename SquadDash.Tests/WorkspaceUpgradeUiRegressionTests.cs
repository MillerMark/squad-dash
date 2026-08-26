using System;
using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class WorkspaceUpgradeUiRegressionTests
{
    [Test]
    public void DryRunSummary_UsesFileLinksInsteadOfReviewChangesButton()
    {
        var xaml = File.ReadAllText(FindRepoFile("SquadDash", "WorkspaceUpgradeSummaryPanel.xaml"));
        var source = File.ReadAllText(FindRepoFile("SquadDash", "WorkspaceUpgradeSummaryPanel.xaml.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Not.Contain("Review Changes"));
            Assert.That(xaml, Does.Contain("Click=\"SafeFileLink_Click\""));
            Assert.That(xaml, Does.Contain("Click=\"TrackedFileLink_Click\""));
            Assert.That(xaml, Does.Contain("Click=\"GeneratedFileLink_Click\""));
            Assert.That(xaml, Does.Contain("Generated files at risk (would be replaced)"));
            Assert.That(source, Does.Contain("Review the dry-run summary and choose how to proceed."));
            Assert.That(source, Does.Not.Contain("Idle — review"));
        });
    }

    [Test]
    public void ApplyCallbacks_OpenPassiveChangesViewerWithoutDispatchingCoordinatorPrompt()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var simulation = ExtractMethod(source, "private async void SimulateWorkspaceUpgradeButtonFlow_Click(");
        var production = ExtractMethod(source, "private async Task OpenWorkspaceUpgradeSummaryAsync()");
        var passiveViewer = ExtractMethod(source, "private async Task OpenWorkspaceUpgradeChangesAsync(");

        Assert.Multiple(() =>
        {
            Assert.That(simulation, Does.Contain(
                "OpenWorkspaceUpgradeChangesAsync(panel.DryRunResult, simulated: true)"));
            Assert.That(simulation, Does.Not.Contain("HandleCheckGitDiffAsync"));
            Assert.That(production, Does.Contain(
                "OpenWorkspaceUpgradeChangesAsync(panel.DryRunResult, simulated: false)"));
            Assert.That(production, Does.Not.Contain("HandleCheckGitDiffAsync"));
            Assert.That(passiveViewer, Does.Not.Contain("ExecutePromptAsync"));
            Assert.That(passiveViewer, Does.Not.Contain("HandleCheckGitDiffAsync"));
        });
    }

    [Test]
    public void WorkspaceUpgradeBadge_FollowsWorkspaceIdentifier()
    {
        var xaml = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml"));
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var workspaceIdentifier = xaml.IndexOf("x:Name=\"WorkspaceTitleText\"", StringComparison.Ordinal);
        var workspaceBadge = xaml.IndexOf("x:Name=\"WorkspaceUpgradePendingBadge\"", StringComparison.Ordinal);
        var squadPanelEnd = xaml.IndexOf("</StackPanel>", xaml.IndexOf("x:Name=\"SquadVersionPanel\"", StringComparison.Ordinal), StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(workspaceIdentifier, Is.GreaterThan(squadPanelEnd));
            Assert.That(workspaceBadge, Is.GreaterThan(workspaceIdentifier));
            Assert.That(source, Does.Contain("Workspace-file layout upgrade"));
        });
    }

    [Test]
    public void SimulatedChangesPresentation_LabelsRepresentativeFilesAndOmitsRepositoryDiff()
    {
        var result = new WorkspaceUpgradeDryRunResult(
            [".squad/team.md"],
            [".squad/routing.md"],
            [".squad/agents/lyra-morn/charter.md"]);

        var text = WorkspaceUpgradeChangesPresentation.Build(
            result,
            " M unrelated.txt",
            "+unrelated content",
            simulated: true);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("SIMULATED WORKSPACE-UPGRADE FILES"));
            Assert.That(text, Does.Contain(".squad/team.md"));
            Assert.That(text, Does.Contain(".squad/routing.md"));
            Assert.That(text, Does.Contain(".squad/agents/lyra-morn/charter.md"));
            Assert.That(text, Does.Not.Contain("unrelated.txt"));
            Assert.That(text, Does.Not.Contain("unrelated content"));
        });
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Missing method signature: {signature}");
        var brace = source.IndexOf('{', start);
        Assert.That(brace, Is.GreaterThan(start));
        var depth = 0;
        for (var index = brace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) return source[start..(index + 1)];
        }

        Assert.Fail($"Could not find method end: {signature}");
        return string.Empty;
    }

    private static string FindRepoFile(params string[] pathParts)
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. pathParts]);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file '{Path.Combine(pathParts)}'.");
    }
}
