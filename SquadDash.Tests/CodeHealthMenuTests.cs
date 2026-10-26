using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class CodeHealthMenuTests
{
    [Test]
    public void Populate_SortsAllTasksByDisplayNameWithoutChangingConfiguration()
    {
        WpfTestContext.Run(() =>
        {
            var config = MakeConfig(
                MakeTask("a-id", "Zulu"),
                MakeTask("z-id", "alpha", enabled: false),
                MakeTask("m-id", "Bravo"));
            var menu = new MenuItem();

            CodeHealthMenuBuilder.Populate(menu, config, _ => { });

            Assert.That(menu.Items.Cast<MenuItem>().Select(item => ((TextBlock)item.Header).Text),
                Is.EqualTo(new[] { "alpha", "Bravo", "Zulu" }));
            Assert.That(menu.Items.Cast<MenuItem>().All(item => item.IsEnabled), Is.True,
                "Disabled-for-idle tasks still support Run Now.");
            Assert.That(config.Tasks?.Select(task => task.Id), Is.EqualTo(new[] { "a-id", "z-id", "m-id" }));
        });
    }

    [Test]
    public void Click_RoutesEachTaskIdOnceThroughTheSameCallbackAsPanelRunNow()
    {
        WpfTestContext.Run(() =>
        {
            var calls = new List<string>();
            var config = MakeConfig(
                MakeTask("second", "Same title", enabled: false),
                MakeTask("first", "Same title"));
            var menu = new MenuItem();
            var listPanel = new StackPanel();
            var controller = new CodeHealthPanelController(
                listPanel, new TextBlock(), new ContentControl(),
                getWorkspacePath: () => null,
                toggleTaskEnabled: (_, _) => { },
                reloadPanel: () => { },
                openInMarkdownEditor: _ => { },
                openReportViewer: _ => { },
                showInboxPanel: () => { },
                runTask: calls.Add,
                simulateIdle: () => { });
            controller.Refresh(config, stateStore: null);
            CodeHealthMenuBuilder.Populate(menu, config, calls.Add);
            Assert.That(calls, Is.Empty, "Population must not start tasks.");

            foreach (var item in menu.Items.Cast<MenuItem>())
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.That(calls, Is.EqualTo(new[] { "second", "first" }));

            calls.Clear();
            foreach (var row in listPanel.Children.OfType<Border>().Where(row => row.Tag is CodeHealthTask))
            {
                var runNow = row.ContextMenu.Items.OfType<MenuItem>()
                    .Single(item => Equals(item.Header, "Run Now"));
                runNow.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            Assert.That(calls, Is.EqualTo(new[] { "second", "first" }));
        });
    }

    [Test]
    public void Populate_RefreshesTasksAndPreservesLiteralDisplayNames()
    {
        WpfTestContext.Run(() =>
        {
            var calls = new List<string>();
            var menu = new MenuItem();
            CodeHealthMenuBuilder.Populate(menu, MakeConfig(MakeTask("old", "Old task")), calls.Add);

            CodeHealthMenuBuilder.Populate(menu, MakeConfig(MakeTask("new", "Check_A & B")), calls.Add);

            Assert.That(menu.Items.Count, Is.EqualTo(1));
            var item = (MenuItem)menu.Items[0];
            Assert.That(((TextBlock)item.Header).Text, Is.EqualTo("Check_A & B"));
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.That(calls, Is.EqualTo(new[] { "new" }));
        });
    }

    [TestCase("missing")]
    [TestCase("empty")]
    [TestCase("null-tasks")]
    public void Populate_MissingOrEmptyConfigReplacesStaleTasksWithDisabledPlaceholder(string state)
    {
        WpfTestContext.Run(() =>
        {
            var menu = new MenuItem();
            CodeHealthMenuBuilder.Populate(menu, MakeConfig(MakeTask("old", "Old task")), _ => { });

            var config = state switch
            {
                "missing" => null,
                "null-tasks" => MakeConfig() with { Tasks = null },
                _ => MakeConfig(),
            };
            CodeHealthMenuBuilder.Populate(menu, config, _ => Assert.Fail("No task to run."));

            Assert.That(menu.Items.Count, Is.EqualTo(1));
            var placeholder = (MenuItem)menu.Items[0];
            Assert.That(placeholder.IsEnabled, Is.False);
            Assert.That(placeholder.Header, Is.EqualTo("No code health tasks configured."));
            placeholder.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        });
    }

    [Test]
    public void Health_IsTopLevelImmediatelyAfterToolsAndPopulatesOnOpening()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = XDocument.Load(SourcePath("MainWindow.xaml"));
        var health = document.Descendants(presentation + "MenuItem")
            .Single(item => (string?)item.Attribute(x + "Name") == "HealthMenuItem");

        Assert.Multiple(() =>
        {
            Assert.That(((string?)health.Attribute("Header"))?.Replace("_", ""), Is.EqualTo("Health"));
            Assert.That(health.Parent?.Name, Is.EqualTo(presentation + "Menu"));
            Assert.That((string?)health.ElementsBeforeSelf().Last().Attribute("Header"), Is.EqualTo("_Tools"));
            Assert.That((string?)health.Attribute("SubmenuOpened"), Is.EqualTo("HealthMenuItem_SubmenuOpened"));
            Assert.That((string?)health.Attribute("Style"), Is.EqualTo("{StaticResource TopLevelMenuItemStyle}"));
            Assert.That((string?)health.Attribute("ItemContainerStyle"), Is.EqualTo("{StaticResource ThemedMenuItemStyle}"));
            Assert.That(health.Elements(presentation + "MenuItem"), Is.Not.Empty,
                "An initial child lets WPF open the dynamically populated submenu.");
        });
    }

    [Test]
    public void MainWindow_UsesMergedConfigurationAndSharedManualRunCallbackWithoutRequiringVisiblePanel()
    {
        var source = File.ReadAllText(SourcePath("MainWindow.xaml.cs"));
        var start = source.IndexOf("private void HealthMenuItem_SubmenuOpened(", StringComparison.Ordinal);
        var end = source.IndexOf("private void SyncCodeHealthPanel()", start, StringComparison.Ordinal);
        var menuAndRunHandlers = source[start..end];

        Assert.Multiple(() =>
        {
            Assert.That(menuAndRunHandlers, Does.Contain("CodeHealthMdParser.ParseWithAllSources(workspacePath)"));
            Assert.That(menuAndRunHandlers, Does.Contain("CodeHealthMenuBuilder.Populate(HealthMenuItem, config, RunCodeHealthTaskNow)"));
            Assert.That(menuAndRunHandlers, Does.Not.Contain("_codeHealthPanelVisible"));
            Assert.That(menuAndRunHandlers, Does.Contain("StartCodeHealthCycleAsync(isManual: true, forceTaskIds:"));
            Assert.That(menuAndRunHandlers, Does.Contain("HashSet<string> { taskId }"));
            Assert.That(menuAndRunHandlers, Does.Contain("HandleUiCallbackException(nameof(RunCodeHealthTaskNow)"));
            Assert.That(source[end..], Does.Match(@"runTask:\s+RunCodeHealthTaskNow,"));
        });
    }

    private static CodeHealthTask MakeTask(string id, string title, bool enabled = true) =>
        new(id, enabled, "daily", "branch", title, "Do work.");

    private static CodeHealthMdConfig MakeConfig(params CodeHealthTask[] tasks) =>
        new(IdleTimeout: 15, MaxTasksPerSession: 5, Safety: "branch", Tasks: tasks);

    private static string SourcePath(string fileName)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "SquadDash")))
            directory = directory.Parent;
        return Path.Combine(
            directory?.FullName ?? throw new DirectoryNotFoundException("SquadDash source not found."),
            "SquadDash", fileName);
    }
}
