using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanResumeBranchPolicyTests
{
    [TestCase(true, true, PlanResumeBranchRelationship.SameCommit)]
    [TestCase(true, false, PlanResumeBranchRelationship.CurrentAhead)]
    [TestCase(false, true, PlanResumeBranchRelationship.PlanAhead)]
    [TestCase(false, false, PlanResumeBranchRelationship.Diverged)]
    public void Classify_DescribesCommitAncestry(
        bool planIsAncestorOfCurrent,
        bool currentIsAncestorOfPlan,
        PlanResumeBranchRelationship expected)
    {
        Assert.That(
            PlanResumeBranchPolicy.Classify(planIsAncestorOfCurrent, currentIsAncestorOfPlan),
            Is.EqualTo(expected));
    }

    [Test]
    public void CurrentAhead_RecommendsBringingWorkIntoPlan()
    {
        var situation = Situation(PlanResumeBranchRelationship.CurrentAhead, currentOnly: 4);

        Assert.Multiple(() =>
        {
            Assert.That(situation.CanBringCurrentWork, Is.True);
            Assert.That(PlanResumeBranchPolicy.RecommendedAction(situation),
                Is.EqualTo(PlanResumeBranchAction.BringCurrentWork));
        });
    }

    [Test]
    public void DivergedDirtyWorktree_DisablesAutomaticCombine()
    {
        var situation = Situation(
            PlanResumeBranchRelationship.Diverged,
            currentOnly: 2,
            planOnly: 3,
            clean: false);

        Assert.Multiple(() =>
        {
            Assert.That(situation.CanBringCurrentWork, Is.False);
            Assert.That(PlanResumeBranchPolicy.RecommendedAction(situation),
                Is.EqualTo(PlanResumeBranchAction.SwitchOnly));
        });
    }

    [Test]
    public void ChoiceDialog_ShowsBranchCardsAndConsequenceOrientedActions()
    {
        WpfTestContext.Run(() =>
        {
            var dialog = new PlanResumeBranchDialog(
                Situation(PlanResumeBranchRelationship.CurrentAhead, currentOnly: 4));
            var text = Descendants(dialog.Content)
                .OfType<TextBlock>()
                .Select(value => value.Text)
                .ToArray();
            var buttons = Descendants(dialog.Content).OfType<Button>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("YOU ARE HERE"));
                Assert.That(text, Does.Contain("PLAN RESUMES HERE"));
                Assert.That(text.Any(value => value.Contains("Bring Current Work & Resume", StringComparison.Ordinal)),
                    Is.True);
                Assert.That(text.Any(value => value.Contains("Switch Without Combining", StringComparison.Ordinal)),
                    Is.True);
                Assert.That(buttons.Any(value => value.IsDefault), Is.True);
            });
            dialog.Close();
        });
    }

    [Test]
    public void ResumeFlow_RequiresChoiceBeforeCheckoutAndHasSafeMergeRecovery()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var resumeStart = source.IndexOf(
            "private async Task StartOrResumeDurablePlanAsync", StringComparison.Ordinal);
        var choiceStart = source.IndexOf(
            "private async Task<bool> EnsureResumeBranchChoiceAsync", resumeStart, StringComparison.Ordinal);
        var inspectStart = source.IndexOf(
            "private async Task<PlanResumeBranchSituation> InspectResumeBranchSituationAsync", choiceStart,
            StringComparison.Ordinal);
        var applyStart = source.IndexOf(
            "private async Task ApplyResumeBranchChoiceAsync", inspectStart, StringComparison.Ordinal);
        var nextMethod = source.IndexOf(
            "private void OpenPlanFromStore", applyStart, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(resumeStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(choiceStart, Is.GreaterThan(resumeStart));
            Assert.That(inspectStart, Is.GreaterThan(choiceStart));
            Assert.That(applyStart, Is.GreaterThan(inspectStart));
            Assert.That(nextMethod, Is.GreaterThan(applyStart));
        });

        var resume = source[resumeStart..choiceStart];
        var choice = source[choiceStart..inspectStart];
        var apply = source[applyStart..nextMethod];
        Assert.Multiple(() =>
        {
            Assert.That(
                resume.IndexOf("await EnsureResumeBranchChoiceAsync(plan)", StringComparison.Ordinal),
                Is.LessThan(resume.IndexOf("StartDurablePlanExecutionAsync(plan)", StringComparison.Ordinal)),
                "Resume must resolve the branch choice before execution can switch or mutate branches.");
            Assert.That(choice, Does.Contain("new PlanResumeBranchDialog")
                .And.Contain("dialog.ShowDialog()")
                .And.Contain("ApplyResumeBranchChoiceAsync"));
            Assert.That(apply, Does.Contain("branch -f")
                .And.Contain("merge --no-edit")
                .And.Contain("merge --abort"),
                "Fast-forward ancestry and divergent merges need separate safe paths.");
        });
    }

    [Test]
    public void DeveloperSimulationMenu_OpensPresentationOnlyBranchChoicePreview()
    {
        var xaml = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml"));
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        const string handlerName = "SimulatePlanResumeBranchChoiceDialog_Click";
        var handlerStart = source.IndexOf($"private void {handlerName}", StringComparison.Ordinal);
        var nextMethod = source.IndexOf(
            "private void ClearPlanResponseCardPreviews_Click", handlerStart, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("Header=\"Resume _branch choice dialog...\"")
                .And.Contain($"Click=\"{handlerName}\""));
            Assert.That(handlerStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(nextMethod, Is.GreaterThan(handlerStart));
        });

        var handler = source[handlerStart..nextMethod];
        Assert.Multiple(() =>
        {
            Assert.That(handler, Does.Contain("PlanResumeBranchRelationship.CurrentAhead")
                .And.Contain("new PlanResumeBranchDialog")
                .And.Contain("ShowDialog()"));
            Assert.That(handler, Does.Not.Contain("ApplyResumeBranchChoiceAsync")
                .And.Not.Contain("RunGitAsync"),
                "The preview must not perform repository operations.");
        });
    }

    [Test]
    public void PromptDrivenSafeResume_UsesNormalHostStartBeforeLookingForFailedTask()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var methodStart = source.IndexOf(
            "private async Task<bool> TryApplyDecomposeRecoveryFromResponseAsync", StringComparison.Ordinal);
        var nextMethod = source.IndexOf(
            "private void QueueDecomposeReplan", methodStart, StringComparison.Ordinal);
        var method = source[methodStart..nextMethod];

        Assert.Multiple(() =>
        {
            Assert.That(method.IndexOf("PlanLifecycleStatus.WaitingForCleanWorktree", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(method.IndexOf("return await ResumeSafelyResumablePlanAsync(durablePlan)", StringComparison.Ordinal),
                Is.LessThan(method.IndexOf("var recoveryTaskId", StringComparison.Ordinal)),
                "A preflight-paused plan has no failed task; its validated recovery JSON must enter the normal host resume path first.");
            Assert.That(method, Does.Contain("await StartOrResumeDurablePlanAsync(resumable)"),
                "The shared safe-resume path must persist the resume transition before starting the host runner.");
        });
    }

    private static PlanResumeBranchSituation Situation(
        PlanResumeBranchRelationship relationship,
        int currentOnly = 0,
        int planOnly = 0,
        bool clean = true) =>
        new(
            "feature/current-work",
            "feature/plan-work",
            "1111111111111111111111111111111111111111",
            "2222222222222222222222222222222222222222",
            relationship,
            currentOnly,
            planOnly,
            clean);

    private static IEnumerable<object> Descendants(object? root)
    {
        if (root is null) yield break;
        yield return root;
        if (root is not DependencyObject dependencyObject) yield break;
        foreach (var child in LogicalTreeHelper.GetChildren(dependencyObject).Cast<object>())
        {
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static string FindRepoFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, ..pathParts]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, pathParts));
    }
}
