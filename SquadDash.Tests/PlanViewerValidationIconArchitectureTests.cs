using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
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
                .And.Contain("EvidenceIndicatesPass(validation.Evidence![i])"));
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
    public void BothAssertionBranches_ReserveFontRelativeLeadingInsideEachListItem()
    {
        var source = ReadViewer();
        var assertions = Section(source, "var hasResultView =", "// After Task IDs");
        var leading = Section(source, "private double GetAssertionLeadingSpace()", "private void PopulateValidationDetail(");

        Assert.Multiple(() =>
        {
            Assert.That(Regex.Matches(assertions, @"var assertionLeading = GetAssertionLeadingSpace\(\);"),
                Has.Count.EqualTo(2));
            Assert.That(Regex.Matches(assertions,
                @"para\.Margin = new Thickness\(0\);\s*(?://[^\n]*\n\s*)?assertList\.ListItems\.Add\(new ListItem\(para\)\s*\{\s*Padding = new Thickness\(0, assertionLeading, 0, 0\),\s*\}\);"),
                Has.Count.EqualTo(2), "Both branches must reserve non-collapsing leading, not inner paragraph margins.");
            Assert.That(leading, Does.Contain("FindResource(\"FontSizeBody\")")
                .And.Contain("SystemFonts.MessageFontFamily.LineSpacing")
                .And.Contain("return fontSize * lineSpacing * 0.8;"));
            Assert.That(assertions, Does.Not.Contain("para.LineHeight").And.Not.Contain("assertList.LineHeight"),
                "Wrapped assertion text and evidence must keep their existing line spacing.");
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void AssertionLeading_ItemPaddingAddsEightyPercentOfLineHeightWithoutChangingInternalSpacing(
        [Values(10.0, 14.0, 24.0)] double fontSize,
        [Values(100.0, 150.0)] double zoom,
        [Values(false, true)] bool withEvidence,
        [Values(false, true)] bool withHeader)
    {
        var baseline = MeasureAssertionLayout(fontSize, zoom, withEvidence, withHeader, false);
        var padded = MeasureAssertionLayout(fontSize, zoom, withEvidence, withHeader, true);
        var expectedLeading = baseline.LineHeight * 0.8;

        Assert.Multiple(() =>
        {
            Assert.That(padded.Bounds[0].Top - baseline.Bounds[0].Top,
                Is.EqualTo(expectedLeading).Within(0.1), "Even the first assertion gets its leading.");
            for (var i = 0; i < padded.Bounds.Length; i++)
            {
                Assert.That(padded.Bounds[i].Height, Is.EqualTo(baseline.Bounds[i].Height).Within(0.1),
                    "Wrapped text and evidence retain their original internal spacing.");
                Assert.That(padded.Bounds[i].Left, Is.EqualTo(baseline.Bounds[i].Left).Within(0.1),
                    "List indentation is unchanged.");
                if (i > 0)
                {
                    var oldGap = baseline.Bounds[i].Top - baseline.Bounds[i - 1].Bottom;
                    var newGap = padded.Bounds[i].Top - padded.Bounds[i - 1].Bottom;
                    Assert.That(newGap - oldGap, Is.EqualTo(expectedLeading).Within(0.1),
                        "Every inter-assertion gap gains 80% of a body line.");
                }
            }
        });
    }

    private static (Rect[] Bounds, double LineHeight) MeasureAssertionLayout(
        double fontSize, double zoom, bool withEvidence, bool withHeader, bool padded)
    {
        var leading = fontSize * SystemFonts.MessageFontFamily.LineSpacing * 0.8;
        var list = new List { MarkerStyle = TextMarkerStyle.None, Margin = new Thickness(0, 0, 0, 12) };
        var paragraphs = new List<Paragraph>();
        var assertionRuns = new List<Run>();
        for (var i = 0; i < 3; i++)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            if (withEvidence)
            {
                paragraph.Inlines.Add(new InlineUIContainer(new Viewbox
                {
                    Width = 14, Height = 14, Margin = new Thickness(0, 0, 4, 0),
                    Child = new Border { Width = 14, Height = 14 },
                }) { BaselineAlignment = BaselineAlignment.Center });
            }
            else
            {
                paragraph.Inlines.Add(new Run("\u2022 "));
            }
            var assertion = new Run(string.Join(" ", Enumerable.Repeat("Wrapped assertion text", 12)));
            paragraph.Inlines.Add(assertion);
            assertionRuns.Add(assertion);
            if (withEvidence)
            {
                paragraph.Inlines.Add(new LineBreak());
                paragraph.Inlines.Add(new Run("    [PASS] Evidence sub-line")
                {
                    FontStyle = FontStyles.Italic, FontSize = fontSize * 0.85,
                });
            }
            list.ListItems.Add(new ListItem(paragraph)
            {
                Padding = new Thickness(0, padded ? leading : 0, 0, 0),
            });
            paragraphs.Add(paragraph);
        }
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI, Segoe UI Emoji"),
            FontSize = fontSize,
            PagePadding = new Thickness(0),
        };
        if (withHeader)
        {
            document.Blocks.Add(new Paragraph(new Run("Assertions"))
            {
                Margin = new Thickness(0, 12, 0, 4),
            });
        }
        document.Blocks.Add(list);
        var viewer = new FlowDocumentScrollViewer { Document = document, Zoom = zoom };
        viewer.Measure(new Size(400, 2000));
        viewer.Arrange(new Rect(0, 0, 400, 2000));
        viewer.UpdateLayout();
        var line2 = assertionRuns[0].ContentStart.GetLineStartPosition(1)!;
        var line3 = assertionRuns[0].ContentStart.GetLineStartPosition(2)!;
        var lineHeight = line3.GetCharacterRect(LogicalDirection.Forward).Top
            - line2.GetCharacterRect(LogicalDirection.Forward).Top;
        var bounds = paragraphs.Select(p =>
        {
            var first = p.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            var last = p.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            return new Rect(first.Left, first.Top, 0, last.Bottom - first.Top);
        }).ToArray();
        return (bounds, lineHeight);
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
