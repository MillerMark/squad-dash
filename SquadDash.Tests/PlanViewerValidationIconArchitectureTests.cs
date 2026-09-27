using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanViewerValidationIconArchitectureTests
{
    [Test]
    public void ResultIcon_UsesFreshSharedTaskResourceAtInlineSize()
    {
        var source = ReadViewer();
        var factory = Section(source, "private InlineUIContainer CreateValidationResultIcon(",
            "private void PopulateValidationDetail(");

        Assert.Multiple(() =>
        {
            Assert.That(factory, Does.Contain(
                "var icon = (Viewbox)FindResource(passed ? \"TaskSucceeded\" : \"TaskFailed\");"));
            Assert.That(factory, Does.Contain("icon.Width = 14;")
                .And.Contain("icon.Height = 14;")
                .And.Contain("icon.Margin = new Thickness(0, 0, 4, 0);")
                .And.Contain("new InlineUIContainer(icon)")
                .And.Contain("BaselineAlignment = BaselineAlignment.Center"));
            Assert.That(factory, Does.Not.Contain("Geometry.Parse")
                .And.Not.Contain("new Path")
                .And.Not.Contain("new Run"));
        });
    }

    [Test]
    public void Header_UsesTaskResultIconsAndTextBrushesForTerminalStatuses()
    {
        var header = Section(ReadViewer(), "// Status with color indicator", "var hasResultView =");

        Assert.Multiple(() =>
        {
            Assert.That(header, Does.Contain("validation.Status?.ToLowerInvariant()")
                .And.Contain("\"passed\" or \"complete\" => \"TaskSuccessText\"")
                .And.Contain("\"failed\" => \"TaskFailureText\"")
                .And.Contain("if (status is \"passed\" or \"complete\" or \"failed\")")
                .And.Contain("statusPara.Inlines.Add(CreateValidationResultIcon(status is \"passed\" or \"complete\"));")
                .And.Contain("statusRun.SetResourceReference(TextElement.ForegroundProperty, statusColorKey);"));
            Assert.That(header, Does.Contain("\"executing\" or \"running\" => \"ActivePanelBorder\"")
                .And.Contain("_ => \"SubtleText\"")
                .And.Contain("statusRun.Text = \"\u25CF \" + statusRun.Text;"));
            Assert.That(header, Does.Not.Contain("PriorityHigh").And.Not.Contain("PriorityLow"));
        });
    }

    [Test]
    public void BothAssertionBranches_UseResultIconsAndPreserveEvidenceAndUnevaluatedBullets()
    {
        var assertions = Section(ReadViewer(), "var hasResultView =", "// After Task IDs");

        Assert.Multiple(() =>
        {
            Assert.That(Regex.Matches(assertions, @"para\.Inlines\.Add\(CreateValidationResultIcon\("),
                Has.Count.EqualTo(2), "Both completed and nonterminal/evidence-backed branches render results.");
            Assert.That(assertions, Does.Contain("CreateValidationResultIcon(isPassedAssertion)")
                .And.Contain("validation.Evidence![i].StartsWith(\"pass\", StringComparison.OrdinalIgnoreCase)"));
            Assert.That(Regex.Matches(assertions,
                @"!string\.IsNullOrWhiteSpace\(validation\.Evidence\[i\]\)"), Has.Count.EqualTo(2),
                "Missing or blank evidence must not produce a failure icon.");
            Assert.That(assertions, Does.Contain("new Run(\"\u2022 \" + assertion)")
                .And.Contain("new Run(\"\u2022 \")")
                .And.Contain("new Run(assertion)")
                .And.Contain("new LineBreak()")
                .And.Contain("new Run(\"    \" + evidenceText)")
                .And.Contain("FontStyle = FontStyles.Italic")
                .And.Contain("evidenceRun.SetResourceReference(TextElement.ForegroundProperty, \"SubtleText\");")
                .And.Contain("evidenceRun.SetResourceReference(TextElement.FontSizeProperty, \"FontSizeSmall\");"));
            Assert.That(assertions, Does.Not.Contain("\u2713").And.Not.Contain("\u2717")
                .And.Not.Contain("PriorityHigh").And.Not.Contain("PriorityLow"));
        });
    }

    [Test]
    public void ContractAndActiveAgentAssertions_RemainUnevaluatedLists()
    {
        var source = ReadViewer();
        var contract = Section(source, "AddText(\"Contract assertions:", "if (durableValidation?.Evidence");
        var active = Section(source, "if (activeValidation.Assertions is", "else if (!string.IsNullOrWhiteSpace(currentVerifyingTask");

        Assert.Multiple(() =>
        {
            Assert.That(contract, Does.Contain("AddText(\"\u2022 \" + assertion, \"SubtleText\");")
                .And.Not.Contain("CreateValidationResultIcon"));
            Assert.That(active, Does.Contain("assertionList.MarkerStyle = TextMarkerStyle.Disc;")
                .And.Contain("new ListItem(new Paragraph(new Run(assertion)))")
                .And.Not.Contain("CreateValidationResultIcon"));
        });
    }

    [TestCase("TaskSucceeded", "TaskSucceededFill")]
    [TestCase("TaskFailed", "TaskFailedFill")]
    public void TaskIcons_AreNonsharedViewboxesUsingDynamicThemeFill(string key, string fillKey)
    {
        var icons = XDocument.Load(FindSource("Themes", "TaskStatusIcons.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var icon = icons.Root!.Elements().Single(element => (string?)element.Attribute(xaml + "Key") == key);

        Assert.Multiple(() =>
        {
            Assert.That(icon.Name.LocalName, Is.EqualTo("Viewbox"));
            Assert.That((string?)icon.Attribute(xaml + "Shared"), Is.EqualTo("False"));
            Assert.That(icon.Descendants().Any(element =>
                (string?)element.Attribute("Fill") == $"{{DynamicResource {fillKey}}}"), Is.True);
        });
    }

    [TestCase("Light", "#0E642F", "#C00000", "#226B22", "#B22222")]
    [TestCase("Dark", "#97FFBF", "#FF7575", "#4EC84E", "#FF6B6B")]
    public void BothThemes_DefineTheEstablishedTaskIconAndTextColors(
        string theme, string successFill, string failureFill, string successText, string failureText)
    {
        var resources = XDocument.Load(FindSource("Themes", theme + ".xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var expected = new Dictionary<string, string>
        {
            ["TaskSucceededFill"] = successFill,
            ["TaskFailedFill"] = failureFill,
            ["TaskSuccessText"] = successText,
            ["TaskFailureText"] = failureText,
        };

        Assert.Multiple(() =>
        {
            foreach (var (key, color) in expected)
            {
                var brush = resources.Root!.Elements().Single(element =>
                    (string?)element.Attribute(xaml + "Key") == key);
                Assert.That(brush.Name.LocalName, Is.EqualTo("SolidColorBrush"), key);
                Assert.That((string?)brush.Attribute("Color"), Is.EqualTo(color), key);
            }
        });
    }

    private static string ReadViewer() =>
        File.ReadAllText(FindSource("PlanViewerWindow.cs")).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Section(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), startMarker);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), endMarker);
        return source[start..end];
    }

    private static string FindSource(params string[] relativePath)
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var path = Path.Combine([directory.FullName, "SquadDash", .. relativePath]);
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(relativePath)}.");
    }
}
