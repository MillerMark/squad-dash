using System.Collections.Generic;
using System.Linq;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;

namespace SquadDash.Tests;

public sealed partial class RegionHighlightingTests
{
    public sealed record RazorToken(string Text, string? Role, int Occurrence = 0);
    private sealed record RazorSample(string Name, string Content, params RazorToken[] Tokens);

    private static IEnumerable<TestCaseData> RazorLexicalCases()
    {
        var samples = new[]
        {
            new RazorSample("MarkupAndCSharp",
                "<h1 class=\"heading\">@Title</h1>\n@code { public void Run() { return; } }\n<p>return</p>",
                new("<h1", "Tag"), new("class", "Attribute"), new("\"heading\"", "String"),
                new("@Title", "Keyword"), new("@code", "Keyword"), new("public", "Keyword"),
                new("<p>", "Tag")),
            new RazorSample("QuotedAttributeWithNestedExpression",
                """<div title="prefix @(Format("inner", new[] { 1, 2 })) suffix" data-id='@Id'>body</div>""",
                new("prefix", "String"), new("\"inner\"", "String"), new("new", "Type"),
                new("suffix", "String"), new("data-id", "Attribute"), new("@Id", "Keyword"),
                new("body", null), new("</div>", "Tag")),
            new RazorSample("DirectiveAttributesAndLambda",
                """<button @onclick="async () => { await Save("value"); }" @bind-Value="model.Name" title="tail">ready</button>""",
                new("@onclick", "Keyword"), new("async", "Keyword"), new("await", "Keyword"),
                new("\"value\"", "String"), new("@bind-Value", "Keyword"),
                new("title", "Attribute"), new("tail", "String"), new("ready", null)),
            new RazorSample("SingleQuotedDirectiveAttribute",
                """<button @onclick='() => Save("inner")' title='after'>body</button>""",
                new("@onclick", "Keyword"), new("\"inner\"", "String"), new("after", "String"),
                new("body", null)),
            new RazorSample("NestedBracesAndCommentSuppression",
                "@{ if (ready) { var s = \"} @code {\"; /* } <div> */\n// } @if\nreturn; } }\n<p>after</p>",
                new("if", "Keyword"), new("\"} @code {\"", "String"),
                new("/* } <div> */", "Comment"), new("// } @if", "Comment"),
                new("return", "Keyword"), new("<p>", "Tag"), new("after", null)),
            new RazorSample("VerbatimStringAndCharacter",
                """@{ var s = @"a "" } <div> "" b"; var c = '}'; return; }<b>after</b>""",
                new("} <div>", "String"), new("'}'", "String"), new("return", "Keyword"),
                new("<b>", "Tag"), new("after", null)),
            new RazorSample("InterpolatedStrings",
                """@{ var s = $"prefix {Format("inner", new[] { 1 })} suffix"; return; }<p>end</p>""",
                new("prefix", "String"), new("\"inner\"", "String"), new("new", "Type"),
                new("suffix", "String"), new("return", "Keyword"), new("<p>", "Tag")),
            new RazorSample("InterpolatedVerbatimStrings",
                """@{ var s = $@"prefix {{literal}} {Format("inner")} suffix"; return; }<p>end</p>""",
                new("prefix", "String"), new("literal", "String"), new("\"inner\"", "String"),
                new("suffix", "String"), new("return", "Keyword")),
            new RazorSample("RawStringOpaqueNotCompilerInterpolation",
                """"@{ var s = $$"""{{value}} } <div> @code {"""; return; }<p>end</p>"""",
                new("{{value}} } <div> @code {", "String"), new("return", "Keyword"),
                new("<p>", "Tag")),
            new RazorSample("MarkupInsideCodeThenReturn",
                "@if (ready) {\n<div title=\"header\">literal } { \"text\" <span>@Name</span></div>\nreturn;\n}\n<p>after</p>",
                new("@if", "Keyword"), new("title", "Attribute"), new("header", "String"),
                new("literal", null), new("\"text\"", null), new("<span>", "Tag"),
                new("@Name", "Keyword"), new("return", "Keyword"), new("after", null)),
            new RazorSample("SelfClosingAndVoidElementsReturnToCode",
                "@{ <Widget Value=\"@Name\" /> <input value=\"literal\"> <br> return; }<p>after</p>",
                new("<Widget", "Tag"), new("@Name", "Keyword"), new("<input", "Tag"),
                new("<br>", "Tag"), new("return", "Keyword"), new("after", null)),
            new RazorSample("ElseBranchReturnsToCode",
                "@if (ready) { <b>yes</b> } else { <i>no</i> return; }<p>after</p>",
                new("@if", "Keyword"), new("else", "Keyword"), new("yes", null), new("no", null),
                new("return", "Keyword"), new("<p>", "Tag")),
            new RazorSample("ImplicitCallsIndexersAndExplicitNesting",
                """<p>@Model.Get("inner")[items[0]].Name @(Call((1 + 2), new[] { 3 })) tail</p>""",
                new("@Model", "Keyword"), new("\"inner\"", "String"), new("new", "Type"),
                new("tail", null), new("</p>", "Tag")),
            new RazorSample("RazorCommentsAcrossLines",
                "@* @code { <h1> \"fake\"\nreturn; } *@\n<p>@Name</p>",
                new("@code", "Comment"), new("<h1>", "Comment"), new("\"fake\"", "Comment"),
                new("return;", "Comment"), new("<p>", "Tag"), new("@Name", "Keyword")),
            new RazorSample("RazorCommentInsideCode",
                "@{ @* } <p> @if *@ return; }<b>after</b>",
                new("} <p> @if", "Comment"), new("return", "Keyword"), new("<b>", "Tag")),
            new RazorSample("HtmlCommentSuppressesTransitions",
                "<!-- @code { <b> -->\n<p>@Name</p>",
                new("@code { <b>", "Comment"), new("@Name", "Keyword")),
            new RazorSample("EscapesAndEmailStayLiteral",
                "<p>@@code @@if @@Name person@example.com</p>\n@functions { return; }",
                new("@@code", null), new("@@if", null), new("@@Name", null),
                new("person@example.com", null), new("@functions", "Keyword")),
            new RazorSample("AttributeEscapeDoesNotOpenExpression",
                """<p title="@@Name and person@example.com">@@(literal)</p>""",
                new("@@Name", "String"), new("person@example.com", "String"), new("@@(literal)", null)),
            new RazorSample("MarkupLineReturnsToCode",
                "@{\n@:literal } { <b>@Name</b>\nreturn;\n}<p>after</p>",
                new("@:", "Keyword"), new("literal", null), new("<b>", "Tag"),
                new("@Name", "Keyword"), new("return", "Keyword"), new("after", null)),
            new RazorSample("IncompleteCommentRetainsState",
                "@* unfinished\n<Widget> @code { return;",
                new RazorToken("<Widget> @code { return;", "Comment")),
            new RazorSample("IncompleteCodeRetainsState",
                "@code {\npublic void Run() {\nreturn;",
                new("@code", "Keyword"), new("public", "Keyword"), new("return", "Keyword")),
            new RazorSample("IncompleteVerbatimStringRetainsState",
                "@{ var s = @\"unfinished\nreturn; <div>",
                new RazorToken("return; <div>", "String")),
            new RazorSample("OrdinaryStringRecoversAtNewline",
                "@{ var s = \"unfinished\nreturn; }\n<p>after</p>",
                new("unfinished", "String"), new("return", "Keyword"), new("<p>", "Tag")),
            new RazorSample("DirectiveLineRecoversAtNewline",
                "@page \"/counter\"\n<p>return</p>",
                new("@page", "Keyword"), new("\"/counter\"", "String"), new("return", null)),
            new RazorSample("ImplicitExpressionRecoversAtNewline",
                "<p>@Model.Name\nplain text</p>",
                new("@Model", "Keyword"), new("plain text", null), new("</p>", "Tag")),
            new RazorSample("IncompleteExpressionRetainsNestedState",
                "<p>@(Call(new[] {\n\"} ) <b>\",\n42",
                new("new", "Type"), new("\"} ) <b>\"", "String"), new("42", "Number")),
            new RazorSample("IncompleteTagAttributeRetainsString",
                "<Widget title=\"unfinished @(Call(\"inner\"",
                new("<Widget", "Tag"), new("title", "Attribute"), new("unfinished", "String"),
                new("\"inner\"", "String")),
            new RazorSample("EscapedOrdinaryStringSuppressesDelimiters",
                """@{ var s = "prefix \" } ) <b> @if suffix"; return; }<p>end</p>""",
                new("} ) <b> @if suffix", "String"), new("return", "Keyword"), new("<p>", "Tag")),
            new RazorSample("MultilineRawStringIsOpaque",
                "@{ var s = \"\"\"\n} @if <b> {{value}}\n\"\"\"; return; }\n<p>end</p>",
                new("} @if <b> {{value}}", "String"), new("return", "Keyword"), new("<p>", "Tag")),
            new RazorSample("ScriptAndStyleBodiesReturnToMarkup",
                "<script>var s = \"@code { }\";</script>\n<style>/* @page fake */ p { color: red; }</style>\n<p>@Name</p>",
                new("\"@code { }\"", "String"), new("/* @page fake */", "Comment"),
                new("<p>", "Tag"), new("@Name", "Keyword")),
            // This is a lexical state machine: do not require semantic tag matching or compilation.
            new RazorSample("ClosingTagNamesAreNotValidated",
                "@{ <Widget>literal</Other> return; }",
                new("literal", null), new("</Other>", "Tag"), new("return", "Keyword")),
        };
        foreach (var sample in samples)
        foreach (var dark in new[] { false, true })
            yield return new TestCaseData(sample.Content, sample.Tokens, dark)
                .SetName($"Razor_Lexical_{sample.Name}_{(dark ? "Dark" : "Light")}");
    }

    [TestCaseSource(nameof(RazorLexicalCases))]
    public void Razor_LexicalTokens_PreserveNestedStates(string content, RazorToken[] tokens, bool dark)
    {
        var engine = new RegionHighlightingEngine();
        var definition = engine.CreateDefinition("Razor", PaletteFrom(engine, dark));
        var document = new TextDocument(content);
        var sections = HighlightAll(document, definition);
        Assert.Multiple(() =>
        {
            foreach (var token in tokens)
                AssertRazorToken(content, token, sections, engine, dark);
        });
        Assert.That(SnapshotSections(HighlightAll(document, definition)), Is.EqualTo(SnapshotSections(sections)),
            "a fresh highlighter must produce the same state and offsets");
        Assert.That(document.Text, Is.EqualTo(content));
    }

    private static void AssertRazorToken(string content, RazorToken token,
        IReadOnlyList<HighlightedSection> sections, RegionHighlightingEngine engine, bool dark)
    {
        var start = IndexOfOccurrence(content, token.Text, token.Occurrence);
        var expected = token.Role is null ? null : ColorOf(engine.CreatePaletteColors(dark)["$" + token.Role]);
        for (var offset = start; offset < start + token.Text.Length; offset++)
        {
            if (content[offset] is '\r' or '\n')
                continue;
            // The last applicable foreground wins when spans and nested rules overlap.
            var actual = sections.LastOrDefault(section =>
                section.Offset <= offset && offset < section.Offset + section.Length &&
                section.Color.Foreground is not null);
            Assert.That(ColorOf(actual?.Color), Is.EqualTo(expected),
                $"'{token.Text}' at {offset}, expected {token.Role ?? "uncolored"}, actual {actual?.Color.Name ?? "uncolored"}; source: {content}");
            // Tag and Keyword deliberately share RGB values; color alone cannot prove the token role.
            Assert.That(actual is null ? null : RegionHighlightingPalettes.GetRole(actual.Color.Name),
                Is.EqualTo(token.Role is null ? null : "$" + token.Role),
                $"'{token.Text}' at {offset} must have role {token.Role ?? "uncolored"}; source: {content}");
        }
    }

    [TestCase("@page \"/counter\"")]
    [TestCase("@inject IService Service")]
    [TestCase("@using System.Text")]
    [TestCase("@model Demo.Model")]
    [TestCase("@inherits ComponentBase")]
    [TestCase("@implements IDisposable")]
    [TestCase("@namespace Demo.Pages")]
    [TestCase("@layout MainLayout")]
    [TestCase("@typeparam TItem")]
    [TestCase("@attribute [Authorize]")]
    [TestCase("@rendermode InteractiveServer")]
    [TestCase("@preservewhitespace true")]
    [TestCase("@addTagHelper *, Demo")]
    [TestCase("@removeTagHelper *, Demo")]
    [TestCase("@tagHelperPrefix th:")]
    [TestCase("@functions { public void Run() { return; } }")]
    public void Razor_Directives_RecognizeTransitionAndReturnToMarkup(string directive)
    {
        var token = directive.Split(' ')[0];
        var content = directive + "\n<p>plain</p>";
        var engine = new RegionHighlightingEngine();
        foreach (var dark in new[] { false, true })
        {
            var definition = engine.CreateDefinition("Razor", PaletteFrom(engine, dark));
            var sections = HighlightAll(new TextDocument(content), definition);
            AssertRazorToken(content, new(token, "Keyword"), sections, engine, dark);
            AssertRazorToken(content, new("<p>", "Tag"), sections, engine, dark);
            AssertRazorToken(content, new("plain", null), sections, engine, dark);
        }
    }

    [Test]
    public void Razor_IncrementalHighlighting_AfterClosingCommentMatchesFreshPass()
    {
        var engine = new RegionHighlightingEngine();
        var definition = engine.CreateDefinition("Razor", PaletteFrom(engine, true));
        var document = new TextDocument("@* unfinished\n<p>@Name</p>");
        using var highlighter = new DocumentHighlighter(document, definition);
        var before = highlighter.HighlightLine(2);
        Assert.That(before.Sections, Is.Not.Empty);
        Assert.That(before.Sections.All(s => ColorOf(s.Color) == ColorOf(definition.GetNamedColor("Comment"))), Is.True);
        document.Insert(document.GetLineByNumber(1).EndOffset, " *@");
        Assert.That(SnapshotSections(highlighter.HighlightLine(2).Sections),
            Is.EqualTo(SnapshotSections(HighlightAll(document, definition)
                .Where(s => s.Offset >= document.GetLineByNumber(2).Offset))));
    }

    [TestCase("@foreach (var item in items)")]
    [TestCase("@for (var i = 0; i < 2; i++)")]
    [TestCase("@while (ready)")]
    [TestCase("@lock (gate)")]
    [TestCase("@using (var scope = Open())")]
    [TestCase("@section Header")]
    public void Razor_ControlBlocks_EnterMarkupAndResumeCSharp(string header)
    {
        var content = header + " {\n<div>@Name</div>\nreturn;\n}\n<p>after</p>";
        var engine = new RegionHighlightingEngine();
        foreach (var dark in new[] { false, true })
        {
            var definition = engine.CreateDefinition("Razor", PaletteFrom(engine, dark));
            var sections = HighlightAll(new TextDocument(content), definition);
            AssertRazorToken(content, new(header.Split(' ')[0], "Keyword"), sections, engine, dark);
            AssertRazorToken(content, new("<div>", "Tag"), sections, engine, dark);
            AssertRazorToken(content, new("@Name", "Keyword"), sections, engine, dark);
            AssertRazorToken(content, new("return", "Keyword"), sections, engine, dark);
            AssertRazorToken(content, new("after", null), sections, engine, dark);
        }
    }

    [Test]
    public void Razor_ImportedCSharpNewKeyword_UsesExistingTypeRole()
    {
        var engine = new RegionHighlightingEngine();
        foreach (var dark in new[] { false, true })
        {
            var definition = engine.CreateDefinition("C#", PaletteFrom(engine, dark));
            AssertRazorToken("new object()", new("new", "Type"),
                HighlightAll(new TextDocument("new object()"), definition), engine, dark);
        }
    }
}
