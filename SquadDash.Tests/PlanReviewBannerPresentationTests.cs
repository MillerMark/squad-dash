namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanReviewBannerPresentationTests
{
    [Test]
    public void PlanViewerHeader_UsesCompactBannerInsteadOfInlineChecklist()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("PlanReviewBannerPresentation.Create(reviewAttention)"));
            Assert.That(source, Does.Contain("Paused plans hide the transcript review card."));
            Assert.That(source, Does.Contain("The transcript also has the live review card."));
            Assert.That(source, Does.Not.Contain("foreach (var item in reviewAttention.Checklist)"));
            Assert.That(source, Does.Not.Contain("PlanReviewAttentionPresentationResolver.FindRequirement(reviewAttention, item)"));
            Assert.That(source, Does.Not.Contain("Open Review Card"));
        });
    }

    [Test]
    public void PlanViewerHeader_DoesNotExposeTranscriptNavigationButton()
    {
        var viewer = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(viewer, Does.Not.Contain("_onOpenReviewCard"));
            Assert.That(viewer, Does.Not.Contain("onOpenReviewCard"));
            Assert.That(main, Does.Not.Contain("private void OpenReviewCard("));
            Assert.That(main, Does.Not.Contain("onOpenReviewCard:"));
        });
    }

    [Test]
    public void Create_SummarizesChecklistWithoutCarryingIndividualReviewText()
    {
        var task = new PlanTask(
            "TASK-1",
            "Replace region TextBox with AvalonEdit editor",
            "Work",
            [],
            "high",
            PlanTaskStatus.HumanReviewRequired,
            DisplayStepLabel: "2");
        var gate = new PlanApprovalGate(
            "GATE-1",
            "Review the editor behavior",
            [task.TaskId],
            [],
            PlanGateStatus.AwaitingApproval);
        var checklist = Enumerable.Range(1, 7)
            .Select(index => new PlanHumanReviewChecklistItem(
                gate.GateId,
                $"item-{index}",
                $"req-{index}",
                index == 1
                    ? "Does a single click inside the region place the caret at the clicked position?"
                    : $"Question {index}?",
                "abcdef1",
                index <= 2,
                false,
                null,
                $"Check {index}"))
            .ToArray();
        var attention = new PlanReviewAttentionPresentation(
            task,
            gate,
            checklist,
            "Review Required",
            "Implementation and automated verification for Step 2 are complete. Human observation remains.");

        var banner = PlanReviewBannerPresentation.Create(attention);

        Assert.Multiple(() =>
        {
            Assert.That(banner.Title, Is.EqualTo("Review Required"));
            Assert.That(banner.ProgressText, Is.EqualTo("2 of 7 checks confirmed."));
            Assert.That(banner.AllItemsChecked, Is.False);
            Assert.That(banner.Summary, Does.Contain("Step 2"));
            Assert.That(banner.Summary, Does.Not.Contain("single click inside the region"));
        });
    }

    [Test]
    public void Create_AllCheckedUsesCompleteProgressText()
    {
        var task = new PlanTask(
            "TASK-1", "Review UI", "Work", [], "high",
            PlanTaskStatus.HumanReviewRequired);
        var gate = new PlanApprovalGate(
            "GATE-1", "Review UI", [task.TaskId], [], PlanGateStatus.AwaitingApproval);
        var checklist = new[]
        {
            new PlanHumanReviewChecklistItem(
                gate.GateId, "item-1", "req-1", "Does it work?", "abcdef1", true, false, null),
        };
        var attention = new PlanReviewAttentionPresentation(
            task, gate, checklist, string.Empty, string.Empty);

        var banner = PlanReviewBannerPresentation.Create(attention);

        Assert.Multiple(() =>
        {
            Assert.That(banner.Title, Is.EqualTo("Review Required"));
            Assert.That(banner.ProgressText, Is.EqualTo("1 check confirmed."));
            Assert.That(banner.AllItemsChecked, Is.True);
            Assert.That(banner.Summary, Is.EqualTo("Review UI is waiting for human review."));
        });
    }

    private static string FindRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(path)) return path;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }
}
