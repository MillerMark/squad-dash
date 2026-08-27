using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanViewerInspectorTaskLinkArchitectureTests
{
    [Test]
    public void ThemeTokens_DefineTaskLinkTextInLightAndDarkThemes()
    {
        var light = File.ReadAllText(FindRepoFile("SquadDash", "Themes", "Light.xaml"));
        var dark = File.ReadAllText(FindRepoFile("SquadDash", "Themes", "Dark.xaml"));

        Assert.Multiple(() =>
        {
            Assert.That(light, Does.Contain("<SolidColorBrush x:Key=\"TaskLinkText\" Color=\"#5B88D8\"/>"));
            Assert.That(dark, Does.Contain("<SolidColorBrush x:Key=\"TaskLinkText\" Color=\"#4A70B8\"/>"));
        });
    }

    [Test]
    public void InspectorTaskLink_CreateTaskLinkUsesTaskLinkTextHoverGlowAndNavigation()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var methodStart = source.IndexOf("private Hyperlink CreateTaskLink(", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private static string FormatTaskStatus", methodStart, StringComparison.Ordinal);

        Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(methodEnd, Is.GreaterThan(methodStart));

        var method = source[methodStart..methodEnd];
        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("link.SetResourceReference(TextElement.ForegroundProperty, \"TaskLinkText\")"));
            Assert.That(method, Does.Contain("TextDecorations = new TextDecorationCollection()"));
            Assert.That(method, Does.Contain("Cursor = Cursors.Hand"));
            Assert.That(method, Does.Contain("_borderByTask.TryGetValue(taskId, out var nodeBorder)"));
            Assert.That(method, Does.Contain("nodeBorder.Effect = TaskNodeGlowEffect();"));
            Assert.That(method, Does.Contain("nodeBorder.Effect = null;"));
            Assert.That(method, Does.Contain("link.Click += (_, _) => NavigateToTask(taskId);"));
        });
    }

    [Test]
    public void InspectorTaskHistory_TracksNavigationAndSupportsBackFromToolbarAndMouseButton()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("private readonly List<SelectedElementIdentity> _inspectorHistory = [];")
                .And.Contain("private int _inspectorHistoryIndex = -1;")
                .And.Contain("private bool _suppressHistoryPush;"));
            Assert.That(source, Does.Contain("_inspectorHistory.Clear();")
                .And.Contain("_inspectorHistoryIndex = -1;"));
            Assert.That(source, Does.Contain("private void NavigateToTask(string taskId)")
                .And.Contain("SelectElement(\"task\", taskId);"));
            Assert.That(source, Does.Contain("private void NavigateBack()")
                .And.Contain("if (_inspectorHistoryIndex <= 0) return;")
                .And.Contain("SelectElement(target.Kind, target.Id);"));
            Assert.That(source, Does.Contain("if (_inspectorHistoryIndex > 0)")
                .And.Contain("AddButton(\"← Back\", NavigateBack, \"Go back to the previously inspected element\")"));
            Assert.That(source, Does.Contain("if (e.ChangedButton != MouseButton.XButton1)")
                .And.Contain("NavigateBack();"));
        });
    }

    [Test]
    public void TaskDependencyDetail_UsesTaskLinksForKnownDependencies()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var methodStart = source.IndexOf("private void PopulateTaskDetail(PlanTask task, Plan? plan)", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private Hyperlink CreateTaskLink(", methodStart, StringComparison.Ordinal);

        Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(methodEnd, Is.GreaterThan(methodStart));

        var method = source[methodStart..methodEnd];
        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("depPara.Inlines.Add(CreateTaskLink(depTitle, depId));"));
            Assert.That(method, Does.Contain("if (depTask is not null)"));
            Assert.That(method, Does.Contain("depPara.Inlines.Add(new Run(depStatus));"));
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
