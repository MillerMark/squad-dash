using System.Text.RegularExpressions;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class MarkdownHtmlBuilderListTests {
    private static string Build(string markdown) => MarkdownHtmlBuilder.Build(markdown, "Test");

    [Test]
    public void Build_MixedNestedRunbookList_KeepsOuterNumberingContinuous() {
        const string markdown = """
## Setup
1. Build and install the package.
2. Close all Visual Studio instances.
3. Start Visual Studio with logging:
   - `devenv.exe /log`
4. Confirm the sample project exists at:
   - `Plugins/Web/SampleBlazorApp/`
5. Ensure the reviewer has known-good files.
""";

        var body = ExtractBody(Build(markdown));

        Assert.Multiple(() => {
            Assert.That(Regex.Matches(body, "<ol(?: |>)").Count, Is.EqualTo(1));
            Assert.That(Regex.Matches(body, "<ul(?: |>)").Count, Is.EqualTo(2));
            Assert.That(body, Does.Contain("<li data-source-line=\"4\">Start Visual Studio with logging:\n<ul data-source-line=\"5\">"));
            Assert.That(body, Does.Contain("</ul>\n</li>\n<li data-source-line=\"6\">Confirm the sample project exists at:"));
            Assert.That(body, Does.Contain("</ul>\n</li>\n<li data-source-line=\"8\">Ensure the reviewer has known-good files.</li>"));
            Assert.That(body, Does.Not.Contain("<ol start=\"4\""));
        });
    }

    [Test]
    public void Build_NestedOrderedRunbookList_PreservesHierarchy() {
        const string markdown = """
## Act
1. **Blazor markup change scenario**
   1. Place the caret in the component.
   2. Invoke AI Gen with this prompt:
      - `Update the existing card markup.`
   3. Apply the generated response.
2. **Blazor NewFile scenario**
   1. Invoke AI Gen again.
   2. Apply the generated response.
3. **C#/XAML regression check**
   1. Run one familiar C# action.
   2. Run one familiar XAML action.
""";

        var body = ExtractBody(Build(markdown));

        Assert.Multiple(() => {
            Assert.That(Regex.Matches(body, "<ol(?: |>)").Count, Is.EqualTo(4));
            Assert.That(Regex.Matches(body, "<ul(?: |>)").Count, Is.EqualTo(1));
            Assert.That(body, Does.Contain("<li data-source-line=\"2\"><strong>Blazor markup change scenario</strong>\n<ol data-source-line=\"3\">"));
            Assert.That(body, Does.Contain("<li data-source-line=\"4\">Invoke AI Gen with this prompt:\n<ul data-source-line=\"5\">"));
            Assert.That(body, Does.Contain("</ol>\n</li>\n<li data-source-line=\"7\"><strong>Blazor NewFile scenario</strong>"));
        });
    }

    [Test]
    public void Build_OrderedListBeginningAboveOne_UsesStartAttribute() {
        var body = ExtractBody(Build("4. Fourth\n5. Fifth"));

        Assert.That(body, Does.Contain("<ol start=\"4\" data-source-line=\"1\">"));
    }

    private static string ExtractBody(string html) {
        var start = html.IndexOf("<body>", StringComparison.Ordinal) + "<body>".Length;
        var end = html.IndexOf("<script>", start, StringComparison.Ordinal);
        return html[start..end]
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();
    }
}
