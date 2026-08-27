using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanViewerInspectorTaskLinkArchitectureTests
{
    [Test]
    public void ThemeTokens_DefineTaskLinkTextAndMatchChipSelectedBorderInLightAndDarkThemes()
    {
        var light = File.ReadAllText(FindRepoFile("SquadDash", "Themes", "Light.xaml"));
        var dark = File.ReadAllText(FindRepoFile("SquadDash", "Themes", "Dark.xaml"));

        AssertThemeBrushColors(light, expectedTaskLinkText: "#1A52B1", expectedChipSelectedBorder: "#5B88D8");
        AssertThemeBrushColors(dark, expectedTaskLinkText: "#8FB1F0", expectedChipSelectedBorder: "#4A70B8");
    }

    [Test]
    public void InspectorTaskLink_CreateTaskLinkUsesTaskLinkTextHoverGlowAndNavigation()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var method = ExtractMethod(source, "private Hyperlink CreateTaskLink(string taskTitle, string taskId)");
        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("link.SetResourceReference(TextElement.ForegroundProperty, \"TaskLinkText\")"));
            Assert.That(method, Does.Contain("TextDecorations = new TextDecorationCollection()"));
            Assert.That(method, Does.Not.Contain("TextDecorations.Underline"));
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
        var navigateToTask = ExtractMethod(source, "private void NavigateToTask(string taskId)");
        var navigateBack = ExtractMethod(source, "private void NavigateBack()");
        var prependInspectorToolbar = ExtractMethod(source, "private void PrependInspectorToolbar()");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("private readonly List<SelectedElementIdentity> _inspectorHistory = [];")
                .And.Contain("private int _inspectorHistoryIndex = -1;")
                .And.Contain("private bool _suppressHistoryPush;"));
            Assert.That(navigateToTask, Does.Contain("SelectElement(\"task\", taskId);"));
            Assert.That(navigateBack, Does.Contain("if (_inspectorHistoryIndex <= 0) return;")
                .And.Contain("_inspectorHistoryIndex--;")
                .And.Contain("var target = _inspectorHistory[_inspectorHistoryIndex];")
                .And.Contain("_suppressHistoryPush = true;")
                .And.Contain("SelectElement(target.Kind, target.Id);")
                .And.Contain("_suppressHistoryPush = false;"));
            Assert.That(prependInspectorToolbar, Does.Contain("if (_inspectorHistoryIndex > 0)")
                .And.Contain("AddButton(\"← Back\", NavigateBack, \"Go back to the previously inspected element\")"));
            Assert.That(source, Does.Contain("if (e.ChangedButton != MouseButton.XButton1)")
                .And.Contain("NavigateBack();"));
        });
    }

    [Test]
    public void TaskDependencyDetail_UsesTaskLinksForKnownDependenciesAndRunFallbackForUnknownOnes()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var method = ExtractMethod(source, "private void PopulateTaskDetail(PlanTask task, Plan? plan)");
        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("if (task.DependsOn is { Count: > 0 })"));
            Assert.That(method, Does.Contain("depPara.Inlines.Add(CreateTaskLink(depTitle, depId));"));
            Assert.That(method, Does.Contain("if (depTask is not null)"));
            Assert.That(method, Does.Contain("var depStatusRun = new Run(depStatusText);"));
            Assert.That(method, Does.Contain("depStatusRun.SetResourceReference(TextElement.ForegroundProperty, depIsActive ? \"ImportantText\" : \"SubtleText\");"));
            Assert.That(method, Does.Contain("depPara.Inlines.Add(depStatusRun);"));
            Assert.That(method, Does.Contain("depPara.Inlines.Add(new Run(depTitle));"));
            Assert.That(method.IndexOf("depPara.Inlines.Add(CreateTaskLink(depTitle, depId));", StringComparison.Ordinal),
                Is.GreaterThan(method.IndexOf("if (depTask is not null)", StringComparison.Ordinal)));
            Assert.That(method.IndexOf("depPara.Inlines.Add(new Run(depTitle));", StringComparison.Ordinal),
                Is.GreaterThan(method.IndexOf("depPara.Inlines.Add(CreateTaskLink(depTitle, depId));", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void BorderByTask_IsClassFieldAndNotLocallyShadowed()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var classScope = source[..source.IndexOf("internal string? CurrentRevision", StringComparison.Ordinal)];

        Assert.Multiple(() =>
        {
            Assert.That(classScope, Does.Contain("private Dictionary<string, Border> _borderByTask = new(StringComparer.Ordinal);"));
            Assert.That(source, Does.Not.Contain("var _borderByTask ="));
            Assert.That(Regex.IsMatch(source, @"(?m)^\s*Dictionary<string,\s*Border>\s+_borderByTask\s*="), Is.False);
        });
    }

    private static void AssertThemeBrushColors(
        string themeXaml,
        string expectedTaskLinkText,
        string expectedChipSelectedBorder)
    {
        var taskLinkTextColor = ExtractBrushColor(themeXaml, "TaskLinkText");
        var chipSelectedBorderColor = ExtractBrushColor(themeXaml, "ChipSelectedBorder");

        Assert.Multiple(() =>
        {
            Assert.That(taskLinkTextColor, Is.EqualTo(expectedTaskLinkText));
            Assert.That(chipSelectedBorderColor, Is.EqualTo(expectedChipSelectedBorder));
        });
    }

    private static string ExtractBrushColor(string themeXaml, string brushKey)
    {
        var pattern = "<SolidColorBrush\\s+x:Key=\"" + Regex.Escape(brushKey) + "\"\\s+Color=\"(?<color>#[0-9A-Fa-f]{6})\"\\s*/>";
        var match = Regex.Match(themeXaml, pattern);
        Assert.That(match.Success, Is.True, $"Expected brush key '{brushKey}' in theme.");
        return match.Groups["color"].Value.ToUpperInvariant();
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Could not find method signature: {signature}");

        var braceStart = source.IndexOf('{', start);
        Assert.That(braceStart, Is.GreaterThan(start), $"Could not find method body for signature: {signature}");

        var depth = 0;
        for (var i = braceStart; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
                depth--;

            if (depth == 0)
                return source[start..(i + 1)];
        }

        Assert.Fail($"Could not find method end for signature: {signature}");
        return string.Empty;
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
