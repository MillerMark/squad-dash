namespace SquadDash.Tests;

using System.Threading;
using System.Windows.Controls;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class TasksPanelExecutionSelectionTests
{
    [Test]
    public void ResolveVisibleExecutionSelection_SingleFilteredPlan_UsesDecomposeEngine()
    {
        var controller = CreateController(TasksPanelParser.Parse(PlanLines()));

        controller.SetFilter("God");
        var selection = controller.ResolveVisibleExecutionSelection();

        Assert.Multiple(() =>
        {
            Assert.That(selection.Kind, Is.EqualTo(TasksPanelExecutionKind.DecomposeGroup));
            Assert.That(selection.Group?.GroupId, Is.EqualTo("GODCLASS-20260725"));
            Assert.That(selection.VisibleTaskCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void ResolveVisibleExecutionSelection_NoMatchingRows_FailsClosed()
    {
        var controller = CreateController(TasksPanelParser.Parse(PlanLines()));

        controller.SetFilter("does-not-exist");
        var selection = controller.ResolveVisibleExecutionSelection();

        Assert.That(selection.Kind, Is.EqualTo(TasksPanelExecutionKind.NoTasks));
    }

    [Test]
    public void ResolveVisibleExecutionSelection_MixedPlanAndBacklog_FailsClosed()
    {
        var lines = PlanLines().Concat([
            "## 🟡 Mid Priority",
            "- [ ] Ordinary backlog task",
        ]).ToArray();
        var controller = CreateController(TasksPanelParser.Parse(lines));

        var selection = controller.ResolveVisibleExecutionSelection();

        Assert.That(selection.Kind, Is.EqualTo(TasksPanelExecutionKind.InvalidPlanSelection));
    }

    [Test]
    public void Parse_DecomposeHeaderWithDecodedBom_RemainsStructured()
    {
        var lines = PlanLines();
        lines[0] = "\uFEFF" + lines[0];

        var parsed = TasksPanelParser.Parse(lines);

        Assert.That(parsed.DecomposeGroups.ContainsKey("GODCLASS-20260725"), Is.True);
    }

    private static TasksPanelController CreateController(TaskParseResult parsed)
    {
        var controller = new TasksPanelController(
            new StackPanel(),
            new StackPanel(),
            new Border(),
            new Border(),
            getTasksPath: () => null,
            editTasksAction: () => { },
            reloadPanel: () => { });
        controller.Refresh(parsed);
        return controller;
    }

    private static string[] PlanLines() =>
    [
        "<!-- decompose-group: GODCLASS-20260725 | branch: refactor/mainwindow -->",
        "<!-- decompose-revision: abc123 -->",
        "**[GODCLASS-20260725] MainWindow God Class Decomposition**",
        "> Safely extract responsibilities.",
        "- [ ] **[GODCLASS-20260725-001]** Extract watcher coordinator",
        "  Group: GODCLASS-20260725 | Branch: refactor/mainwindow | Priority: high",
        "  description: Extract the watcher coordinator.",
        "  dependsOn: (none)",
        "- [ ] **[GODCLASS-20260725-002]** Extract settings manager",
        "  Group: GODCLASS-20260725 | Branch: refactor/mainwindow | Priority: high",
        "  description: Extract the settings manager.",
        "  dependsOn: GODCLASS-20260725-001",
    ];
}
