using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanViewerRefreshArchitectureTests
{
    [Test]
    public void RefreshPlan_RebuildsInPlaceWithoutConstructingAnotherWindow()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var methodStart = source.IndexOf("internal void RefreshPlan", StringComparison.Ordinal);
        var nextMethod = source.IndexOf("private static ToolTip BuildTaskToolTip", methodStart, StringComparison.Ordinal);

        Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(nextMethod, Is.GreaterThan(methodStart));

        var refreshMethod = source[methodStart..nextMethod];
        Assert.Multiple(() =>
        {
            Assert.That(refreshMethod, Does.Contain("RebuildPreservingScroll(plan, durablePlan)"),
                "Refresh should rebuild the existing viewer's visual tree through the scroll-preserving helper.");
            Assert.That(source, Does.Contain("private void RebuildPreservingScroll(PendingDecomposePlan plan, Plan? durablePlan)")
                .And.Contain("BuildContent(plan, durablePlan)"),
                "The shared helper must rebuild this existing viewer rather than constructing another window.");
            Assert.That(refreshMethod, Does.Not.Contain("new PlanViewerWindow"),
                "Constructing a hidden Window during refresh can keep WPF alive during restart and binds handlers to the wrong owner.");
        });
    }

    [Test]
    public void StagedEditing_RemainsInsideTheExistingInspectorAndGraphContextMenus()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("private bool PopulateTaskEditor")
                .And.Contain("private bool PopulateValidationEditor")
                .And.Contain("private bool PopulateGateEditor"));
            Assert.That(source, Does.Contain("new BlockUIContainer(panel)")
                .And.Contain("AddButton(_isInspectorEditing ? \"Cancel edit\" : \"Edit\""),
                "Editors must be hosted by the existing inspector and stay read-only until Edit.");
            Assert.That(source, Does.Contain("Header = \"Add validation after this task\"")
                .And.Contain("Header = \"Delete task\"")
                .And.Contain("Header = \"Delete validation\""));
            Assert.That(source, Does.Contain("AddButton(\"Undo\"")
                .And.Contain("AddButton(\"Redo\"")
                .And.Contain("Review with AI"));
            Assert.That(source, Does.Contain("TourActionLabel = ReviewActionLabel(annotation)")
                .And.Contain("TourPrevRequested")
                .And.Contain("TourNextRequested")
                .And.Contain("ApplyActiveReviewChange"));
            Assert.That(source, Does.Not.Contain("new PlanEditorWindow")
                .And.Not.Contain("new PlanReviewWindow"));
        });
    }

    [Test]
    public void AiReview_UsesSilentHostTurnAndDestructiveHighContrastAction()
    {
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var conversation = File.ReadAllText(FindRepoFile("SquadDash", "TranscriptConversationManager.cs"));
        var overlay = File.ReadAllText(
            FindRepoFile("SquadDash", "Callouts", "UI", "TourCalloutNavigationOverlay.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(main, Does.Contain("sourceTag: \"plan-visualizer-review\"")
                .And.Contain("_pendingPromptSuppressTranscript")
                .And.Contain("SuppressTranscript = suppressTranscript"));
            Assert.That(conversation, Does.Contain("turn.SuppressTranscript"));
            Assert.That(overlay, Does.Contain("DangerButtonBg")
                .And.Contain("DangerButtonFg")
                .And.Contain("ActionClicked"));
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
