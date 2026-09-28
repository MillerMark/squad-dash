using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanViewerDragReorderArchitectureTests
{
    [Test]
    public void CommittedSwapReflectedInTaskSequence()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var methodStart = source.IndexOf(
            "private bool TryApplyCommittedColumnReorder(Plan plan, CommittedColumnReorder committed, out Plan reorderedPlan)",
            StringComparison.Ordinal);
        var nextMethod = source.IndexOf(
            "private bool TryApplyCommittedColumnReorder(",
            methodStart + 1,
            StringComparison.Ordinal);

        Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(nextMethod, Is.GreaterThan(methodStart));

        var method = source[methodStart..nextMethod];
        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("var reorderedStageTaskIds = BuildReorderedStageTaskIds(stageTaskIds, committed.NewTaskOrder);"),
                "Committed reorders must derive a stage-scoped task sequence from the requested sibling order.");
            Assert.That(method, Does.Contain("var reorderedStageTasks = new Queue<PlanTask>("),
                "Stage reorder must queue tasks in the committed sibling order.");
            Assert.That(method, Does.Contain("reorderedPlan = plan with")
                .And.Contain("Tasks = plan.Tasks")
                .And.Contain("? reorderedStageTasks.Dequeue()")
                .And.Contain(": task)"),
                "Committed reorders must rebuild the plan with stage tasks emitted in the committed order while preserving other stages.");
        });
    }

    [Test]
    public void CancelledSwapLeavesModelUnchanged()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var cancelStart = source.IndexOf("private void CancelTaskCardDrag()", StringComparison.Ordinal);
        var commitStart = source.IndexOf("private void CommitTaskCardDrag()", StringComparison.Ordinal);
        var nextMethod = source.IndexOf(
            "private bool TryResolveMilestoneVerticalBoundsForColumn(",
            cancelStart,
            StringComparison.Ordinal);

        Assert.That(cancelStart, Is.GreaterThan(commitStart));
        Assert.That(nextMethod, Is.GreaterThan(cancelStart));

        var cancelMethod = source[cancelStart..nextMethod];
        Assert.Multiple(() =>
        {
            Assert.That(cancelMethod, Does.Not.Contain("PersistCommittedTaskCardReorder(")
                .And.Not.Contain("_lastCommittedColumnOrder ="),
                "Escape cancel must not persist a reordered stage sequence or commit drag state.");
            Assert.That(cancelMethod, Does.Contain("Canvas.SetTop(dragState.Element, dragState.OriginalTop);")
                .And.Contain("Canvas.SetLeft(dragState.Element, dragState.ColumnX);")
                .And.Contain("ApplyTaskCardTopAndConnectors(sibling.TaskId, dragState.ColumnX, sibling.OriginalTop);"),
                "Escape cancel must restore card layout to pre-drag positions without committing model reorder.");
        });
    }

    [Test]
    public void FailedPersistenceRestoresPreDragOrderAndExplainsThatOrderWasNotSaved()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var methodStart = source.IndexOf("private void CommitTaskCardDrag()", StringComparison.Ordinal);
        var nextMethod = source.IndexOf(
            "private bool TryResolveMilestoneVerticalBoundsForColumn(",
            methodStart,
            StringComparison.Ordinal);

        Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(nextMethod, Is.GreaterThan(methodStart));

        var method = source[methodStart..nextMethod];
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("failurePrefix: \"Order wasn’t saved. \""),
                "A rejected reorder should describe the user-visible outcome before technical validation details.");
            Assert.That(method, Does.Contain("if (!PersistCommittedTaskCardReorder())")
                .And.Contain("_lastCommittedColumnOrder = null;")
                .And.Contain("dragState.PreDragTaskNudges")
                .And.Contain("dragState.OriginalTop")
                .And.Contain("sibling.OriginalTop"),
                "A failed save must restore both the persisted-order marker and every pre-drag visual position.");
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
