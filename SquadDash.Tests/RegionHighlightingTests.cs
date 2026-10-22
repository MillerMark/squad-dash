using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;
using SquadDash;

namespace SquadDash.Tests;

/// <summary>
/// Headless, STA proof for the region syntax-highlighting stack added under
/// AVALONEDIT-20261014-004: the shared <see cref="RegionHighlightingEngine"/> (built-in option
/// registration, aliases, grammar/palette plug-ins, editor-local definition cloning), the UI-free
/// <see cref="RegionHighlightingPalettes"/>, and the production wiring in
/// <see cref="AvalonEditRegionEditor"/> / <see cref="EmbeddedCodeRegionBlock"/>.
/// <para>
/// These tests complement <c>RegionGrammarResolverTests</c> (which only covers the resolver policy).
/// The focus here is: standard definitions never leak theme changes between editors or into the
/// shared <see cref="HighlightingManager"/>, palettes are deterministic and theme-distinct, custom
/// grammar+palette registration works in isolation from the <see cref="RegionHighlightingEngine.Default"/>
/// singleton, and the real AvalonEdit-backed editor actually paints/recolors tokens and gates its
/// auto-detect debounce on WPF load state.
/// </para>
/// <para>
/// No visible window is ever shown. A few tests need a genuinely <c>IsLoaded</c> editor to exercise
/// the auto-detect <see cref="DispatcherTimer"/>; those host the control in an offscreen, zero-size
/// <see cref="HwndSource"/> and pump the dispatcher deterministically (no sleeps). Nothing here
/// claims a human-observed/rendered result.
/// </para>
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class RegionHighlightingTests
{
    [Test]
    public void Options_ExposeAutoDetectPlainText_AndEveryIncludedGrammar()
    {
        var engine = new RegionHighlightingEngine();
        var options = engine.Options;

        var autoDetect = options.SingleOrDefault(o => o.FenceToken == "");
        var plainText = options.SingleOrDefault(o => o.FenceToken == RegionLanguagePicker.PlainTextToken);
        Assert.Multiple(() =>
        {
            Assert.That(autoDetect, Is.Not.Null, "an auto-detect option must be offered");
            Assert.That(autoDetect!.DisplayName, Is.EqualTo("Auto detect"));
            Assert.That(autoDetect.GrammarName, Is.Null);
            Assert.That(plainText, Is.Not.Null, "a plain-text option must be offered");
            Assert.That(plainText!.DisplayName, Is.EqualTo("Plain text"));
            Assert.That(plainText.GrammarName, Is.Null);
        });

        var installed = HighlightingManager.Instance.HighlightingDefinitions
            .Select(d => d.Name)
            .ToHashSet(StringComparer.Ordinal);
        var grammarOptions = options.Where(o => o.GrammarName is not null).ToList();

        installed.Remove("MarkDown");
        installed.Remove("MarkDownWithFontSize");
        installed.UnionWith(new[] { "Json", "TypeScript", "YAML", "SQL", "Go", "Rust", "Bash", "Markdown", "Error message" });
        Assert.That(grammarOptions, Has.Count.EqualTo(installed.Count),
            "one option per included built-in or bundled grammar, with no duplicate JSON entry");
        Assert.That(grammarOptions.Select(o => o.GrammarName!).ToHashSet(StringComparer.Ordinal),
            Is.EquivalentTo(installed));
        Assert.That(options.Take(2).Select(o => o.FenceToken), Is.EqualTo(new[] { "", "text" }));
        Assert.That(grammarOptions.Select(o => o.DisplayName),
            Is.Ordered.Using<string>(StringComparer.OrdinalIgnoreCase));
    }

    [Test]
    public void Options_ExcludeMarkdownFontSizeVariant_AndKeepBundledMarkdown()
    {
        var engine = new RegionHighlightingEngine();
        var installed = HighlightingManager.Instance.HighlightingDefinitions.Select(d => d.Name).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(installed, Does.Contain("MarkDownWithFontSize"),
                "verify AvalonEdit's exact registered name and leave its shared definition intact");
            Assert.That(installed, Does.Contain("MarkDown"));
            Assert.That(engine.Options.Any(o => string.Equals(o.GrammarName,
                "MarkDownWithFontSize", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(RegionLanguagePicker.Options.Any(o => string.Equals(o.GrammarName,
                "MarkDownWithFontSize", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(RegionLanguagePicker.Options, Does.Contain(
                new RegionLanguageOption("markdown", "Markdown", "Markdown")));
            Assert.That(engine.ResolveFenceToken("markdownwithfontsize"), Is.Null);
            Assert.That(engine.ResolveFenceToken("markdown"), Is.EqualTo("Markdown"));
        });
        Assert.That(engine.CreateDefinition("Markdown", PaletteFrom(engine, false)).Name,
            Is.EqualTo("Markdown"));
    }

    [Test]
    public void IncludedBuiltIns_ResolveToStandardDefinitions()
    {
        var engine = new RegionHighlightingEngine();
        var palette = PaletteFrom(engine, isDark: true);

        foreach (var definition in HighlightingManager.Instance.HighlightingDefinitions)
        {
            if (string.Equals(definition.Name, "MarkDownWithFontSize", StringComparison.OrdinalIgnoreCase))
                continue;
            var token = SanitizeToken(definition.Name);
            var expectedName = definition.Name == "MarkDown" ? "Markdown" : definition.Name;

            Assert.That(engine.ResolveFenceToken(token), Is.EqualTo(expectedName),
                $"installed grammar '{definition.Name}' must resolve from its serialized token '{token}'");

            var cloned = engine.CreateDefinition(definition.Name, palette);
            Assert.That(cloned.Name, Is.EqualTo(expectedName));
        }
    }

    [TestCase("JSON", "Json", "json")]
    [TestCase("TypeScript", "TypeScript", "typescript")]
    [TestCase("YAML", "YAML", "yaml")]
    [TestCase("SQL", "SQL", "sql")]
    [TestCase("Go", "Go", "go")]
    [TestCase("Rust", "Rust", "rust")]
    [TestCase("Bash", "Bash", "bash")]
    [TestCase("Markdown", "Markdown", "markdown")]
    [TestCase("ErrorMessage", "Error message", "error")]
    public void BundledGrammar_LoadsEmbeddedResource_AndExposesPickerOption(
        string resource, string name, string token)
    {
        using var stream = typeof(RegionHighlightingEngine).Assembly
            .GetManifestResourceStream($"SquadDash.Grammars.{resource}.xshd");
        Assert.That(stream, Is.Not.Null);
        using var reader = XmlReader.Create(stream!);
        var definition = HighlightingLoader.Load(reader, null);
        Assert.That(definition.Name, Is.EqualTo(name));
        Assert.That(RegionLanguagePicker.Options.Single(o => o.FenceToken == token),
            Is.EqualTo(new RegionLanguageOption(token, resource == "ErrorMessage" ? name : resource, name)));
    }

    [TestCase("json", "Json")]
    [TestCase("JSON", "Json")]
    [TestCase("typescript", "TypeScript")]
    [TestCase("ts", "TypeScript")]
    [TestCase("TS", "TypeScript")]
    [TestCase("yaml", "YAML")]
    [TestCase("yml", "YAML")]
    [TestCase("YML", "YAML")]
    [TestCase("sql", "SQL")]
    [TestCase("SQL", "SQL")]
    [TestCase("go", "Go")]
    [TestCase("GO", "Go")]
    [TestCase("golang", "Go")]
    [TestCase("rust", "Rust")]
    [TestCase("RUST", "Rust")]
    [TestCase("rs", "Rust")]
    [TestCase("bash", "Bash")]
    [TestCase("BASH", "Bash")]
    [TestCase("sh", "Bash")]
    [TestCase("shell", "Bash")]
    [TestCase("markdown", "Markdown")]
    [TestCase("MARKDOWN", "Markdown")]
    [TestCase("md", "Markdown")]
    [TestCase("error", "Error message")]
    [TestCase("ERROR", "Error message")]
    public void BundledGrammar_ExplicitTokensResolveToOwnDefinition(string token, string name)
    {
        var engine = new RegionHighlightingEngine();
        var resolved = engine.Resolve(token, "using System; Console.WriteLine(1);");
        Assert.That(resolved, Is.EqualTo(name));
        Assert.That(engine.CreateDefinition(resolved!, PaletteFrom(engine, false)).Name, Is.EqualTo(name));
    }

    [TestCase(RegionGrammarAutoDetectTests.ReviewerTypeScriptSample, "TypeScript")]
    [TestCase(RegionGrammarAutoDetectTests.ReviewerYamlSample, "YAML")]
    [TestCase(RegionGrammarAutoDetectTests.ReviewerSqlSample, "SQL")]
    [TestCase(RegionGrammarAutoDetectTests.JsonSample, "Json")]
    [TestCase(RegionGrammarAutoDetectTests.JavaScriptSample, "JavaScript")]
    [TestCase(RegionGrammarAutoDetectTests.TsqlSample + "\nGO", "TSQL")]
    [TestCase(RegionGrammarAutoDetectTests.GoSample, "Go")]
    [TestCase(RegionGrammarAutoDetectTests.RustSample, "Rust")]
    [TestCase(RegionGrammarAutoDetectTests.BashSample, "Bash")]
    [TestCase(RegionGrammarAutoDetectTests.MarkdownSample, "Markdown")]
    [TestCase(RegionGrammarAutoDetectTests.ReviewerErrorMessageSample, "Error message")]
    public void AutoDetect_ResolvesAndAppliesExpectedGrammarInBothThemes(string content, string expected)
    {
        var manager = HighlightingManager.Instance.HighlightingDefinitions.ToArray();
        var before = manager.SelectMany(Snapshot).ToArray();
        var engine = new RegionHighlightingEngine();
        var normalized = content.Replace("\r\n", "\n");
        Assert.That(engine.Resolve("", normalized), Is.EqualTo(expected));

        using var editor = new AvalonEditRegionEditor(normalized, engine);
        var control = (TextEditor)editor.Control;
        foreach (var isDark in new[] { false, true })
        {
            editor.ApplyPalette(PaletteFrom(engine, isDark));
            editor.SetLanguage("");
            Assert.That(control.SyntaxHighlighting, Is.Not.Null);
            Assert.That(control.SyntaxHighlighting.Name, Is.EqualTo(expected));
            Assert.That(HighlightAll(control.Document, control.SyntaxHighlighting), Is.Not.Empty);

            editor.SetLanguage("text");
            Assert.That(control.SyntaxHighlighting, Is.Null);
        }

        Assert.That(HighlightingManager.Instance.HighlightingDefinitions, Is.EqualTo(manager));
        Assert.That(manager.SelectMany(Snapshot), Is.EqualTo(before));
    }

    [TestCase("Json")]
    [TestCase("TypeScript")]
    [TestCase("YAML")]
    [TestCase("SQL")]
    [TestCase("Go")]
    [TestCase("Rust")]
    [TestCase("Bash")]
    [TestCase("Markdown")]
    [TestCase("MarkdownDirectives")]
    public void BundledGrammar_PaletteRepushRecolorsEveryRole_WithoutLeaking(string name)
    {
        var manager = HighlightingManager.Instance.HighlightingDefinitions.ToArray();
        var before = manager.SelectMany(Snapshot).ToArray();
        var engine = new RegionHighlightingEngine();
        var light = engine.CreateDefinition(name, PaletteFrom(engine, false));
        var sibling = engine.CreateDefinition(name, PaletteFrom(engine, false));
        var dark = engine.CreateDefinition(name, PaletteFrom(engine, true));
        var lightBefore = Snapshot(light);
        var darkBefore = Snapshot(dark);
        foreach (var color in light.NamedHighlightingColors)
        {
            Assert.That(ColorOf(color), Is.Not.Null, color.Name);
            Assert.That(ColorOf(color), Is.Not.EqualTo(ColorOf(dark.GetNamedColor(color.Name))), color.Name);
            Assert.That(ColorOf(color), Is.EqualTo(ColorOf(engine.CreatePaletteColors(false)[$"{name}/{color.Name}"])));
        }

        light.ApplyPalette(PaletteFrom(engine, true));
        Assert.That(Snapshot(light), Is.EqualTo(darkBefore));
        Assert.That(Snapshot(sibling), Is.EqualTo(lightBefore));
        light.ApplyPalette(PaletteFrom(engine, false));
        Assert.That(Snapshot(light), Is.EqualTo(lightBefore));
        Assert.That(HighlightingManager.Instance.HighlightingDefinitions, Is.EqualTo(manager));
        Assert.That(manager.SelectMany(Snapshot), Is.EqualTo(before));
    }

    [TestCase("Json", "{\"name\": \"SquadDash\", \"enabled\": true, \"count\": -1.2e+3}", "\"name\"", "Attribute")]
    [TestCase("Json", "{\"name\": \"SquadDash\"}", "\"SquadDash\"", "String")]
    [TestCase("Json", "{\"enabled\": true}", "true", "Keyword")]
    [TestCase("Json", "[-1.2e+3]", "-1.2e+3", "Number")]
    [TestCase("Json", "{\"name\": \"Ada\\\"s\"}", "\"Ada\\\"s\"", "String")]
    [TestCase("TypeScript", "interface User { name: string; }", "interface", "Keyword")]
    [TestCase("TypeScript", "const count: number = 42;", "number", "Type")]
    [TestCase("TypeScript", "const name = 'Ada'; // hello", "// hello", "Comment")]
    [TestCase("TypeScript", "// \"quoted\" /* comment\nconst value = 1;", "\"quoted\" /* comment", "Comment")]
    [TestCase("TypeScript", "// /* comment\nconst value = 1;", "const", "Keyword")]
    [TestCase("TypeScript", "const greeting = `Hello ${42}`;", "42", "Number")]
    [TestCase("TypeScript", "const greeting = `Hello\nworld`;", "world", "String")]
    [TestCase("TypeScript", "if (ready) run();", "if", "Keyword")]
    [TestCase("TypeScript", "const name = \"Ada\\\"tail\";", "tail", "String")]
    [TestCase("TypeScript", "const greeting = `Hello ${{ count: 42 }.count} world`;", "world", "String")]
    [TestCase("YAML", "name: SquadDash\ncount: 42\nenabled: true", "name", "Attribute")]
    [TestCase("YAML", "name: \"SquadDash\"", "SquadDash", "String")]
    [TestCase("YAML", "enabled: true # hello", "# hello", "Comment")]
    [TestCase("YAML", "count: 42", "42", "Number")]
    [TestCase("YAML", "enabled: true", "true", "Keyword")]
    [TestCase("YAML", "name: \"Ada\\\"s # team\"", "s # team", "String")]
    [TestCase("YAML", "\"display name\": 'Ada''s team'", "\"display name\"", "Attribute")]
    [TestCase("YAML", "- name: SquadDash", "name", "Attribute")]
    [TestCase("SQL", "select Name from Users where Id = 42;", "select", "Keyword")]
    [TestCase("SQL", "SELECT 'Ada''s team';", "'Ada''s team'", "String")]
    [TestCase("SQL", "SELECT 'Ada''';", "'Ada'''", "String")]
    [TestCase("SQL", "SELECT '/* not a comment */';", "/* not a comment */", "String")]
    [TestCase("SQL", "SELECT 'first\nsecond';", "second", "String")]
    [TestCase("SQL", "-- 'quoted' /* comment\nSELECT 1;", "'quoted' /* comment", "Comment")]
    [TestCase("SQL", "-- /* comment\nSELECT 1;", "SELECT", "Keyword")]
    [TestCase("SQL", "SELECT \"/* column */\", [bracket]]column] FROM Users;", "/* column */", "Attribute")]
    [TestCase("SQL", "SELECT [bracket]]column] FROM Users;", "column", "Attribute")]
    [TestCase("SQL", "CREATE TABLE Users (Id INTEGER);", "INTEGER", "Type")]
    [TestCase("SQL", "SELECT 1.25;", "1.25", "Number")]
    [TestCase("SQL", "-- hello\nSELECT 1;", "-- hello", "Comment")]
    [TestCase("SQL", "SELECT CAST(1 AS VARCHAR(10));", "VARCHAR", "Type")]
    [TestCase("Go", "package main", "package", "Keyword")]
    [TestCase("Go", "var count int = 42", "int", "Type")]
    [TestCase("Go", "count := 42", "42", "Number")]
    [TestCase("Go", "count := 42", ":=", "Punctuation")]
    [TestCase("Go", "var ready = true", "true", "Keyword")]
    [TestCase("Go", "fmt.Println(count)", "Println", "Method")]
    [TestCase("Go", "var text = \"escaped\\\"tail\"", "tail", "String")]
    [TestCase("Go", "var text = `raw\n// content`", "// content", "String")]
    [TestCase("Go", "var letter = 'a'", "'a'", "String")]
    [TestCase("Go", "// \"quote\" /* comment\nfunc main() {}", "\"quote\" /* comment", "Comment")]
    [TestCase("Go", "/* comment */\nfunc main() {}", "func", "Keyword")]
    [TestCase("Rust", "pub fn count() -> usize { 42 }", "fn", "Keyword")]
    [TestCase("Rust", "let count: usize = 42;", "usize", "Type")]
    [TestCase("Rust", "let count = 42u64;", "42u64", "Number")]
    [TestCase("Rust", "println!(\"hello\");", "println!", "Method")]
    [TestCase("Rust", "let s = \"escaped\\\"tail\";", "tail", "String")]
    [TestCase("Rust", "let s = r#\"raw \"quote\"\n// text\"#;", "// text", "String")]
    [TestCase("Rust", "let s = br##\"raw \"# quote\"##;", "quote", "String")]
    [TestCase("Rust", "let c = 'a';", "'a'", "String")]
    [TestCase("Rust", "let c = '\"'; let value = 42;", "'\"'", "String")]
    [TestCase("Rust", "let c = b'\"'; let value = 42;", "let", "Keyword")]
    [TestCase("Rust", "let c = '\"'; let value = 42;", "42", "Number")]
    [TestCase("Rust", "let c = '\\u{1F600}';", "'\\u{1F600}'", "String")]
    [TestCase("Rust", "fn borrow<'a>(s: &'a str) -> &'a str { s }", "str", "Type")]
    [TestCase("Rust", "/* outer /* nested */ still outer */\nlet x = 1;", "still outer", "Comment")]
    [TestCase("Rust", "// \"quoted\" /* comment\nlet x = 1;", "let", "Keyword")]
    [TestCase("Bash", "if [ -n \"$USER\" ]; then\nfi", "then", "Keyword")]
    [TestCase("Bash", "echo \"$USER\"", "echo", "Method")]
    [TestCase("Bash", "echo \"$USER\"", "$USER", "Method")]
    [TestCase("Bash", "echo \"${HOME}\"", "${HOME}", "Method")]
    [TestCase("Bash", "echo '$USER'", "$USER", "String")]
    [TestCase("Bash", "echo \"escaped\\\"tail\"", "tail", "String")]
    [TestCase("Bash", "exit 42", "42", "Number")]
    [TestCase("Bash", "true && echo yes", "&&", "Punctuation")]
    [TestCase("Bash", "# \"quoted\" comment\necho hello", "\"quoted\" comment", "Comment")]
    [TestCase("Bash", "echo ok;# \"quoted\" comment", "# \"quoted\" comment", "Comment")]
    [TestCase("Bash", "echo \"# literal\"", "# literal", "String")]
    [TestCase("Bash", "echo ${#name}", "${#name}", "Method")]
    [TestCase("Markdown", "# Heading", "# Heading", "Keyword")]
    [TestCase("Markdown", "Use **bold** text.", "**bold**", "Keyword")]
    [TestCase("Markdown", "Use _italic_ text.", "_italic_", "Keyword")]
    [TestCase("Markdown", "_italic_", "_italic_", "Keyword")]
    [TestCase("Markdown", "Use __bold__ text.", "__bold__", "Keyword")]
    [TestCase("Markdown", "__bold__", "__bold__", "Keyword")]
    [TestCase("Markdown", "Use *italic* text.", "*italic*", "Keyword")]
    [TestCase("Markdown", "Use ~~strike~~ text.", "~~strike~~", "Keyword")]
    [TestCase("Markdown", "(_italic_)", "_italic_", "Keyword")]
    [TestCase("Markdown", "_italic_.", "_italic_", "Keyword")]
    [TestCase("Markdown", "(__bold__)", "__bold__", "Keyword")]
    [TestCase("Markdown", "__bold__.", "__bold__", "Keyword")]
    [TestCase("Markdown", "[guide](https://example.org)", "[guide](https://example.org)", "Attribute")]
    [TestCase("Markdown", "- item", "- ", "Punctuation")]
    [TestCase("Markdown", "Use `code` here.", "`code`", "InlineCode")]
    [TestCase("Markdown", "Use `<!-- code -->` here.", "<!-- code -->", "InlineCode")]
    [TestCase("Markdown", "Use ``a ` b`` here.", "a ` b", "InlineCode")]
    [TestCase("Markdown", "```text\n# code\n```\n# Heading", "# code", "String")]
    [TestCase("Markdown", "~~~text\ncode\n~~~\n# Heading", "# Heading", "Keyword")]
    [TestCase("Markdown", "````text\n```\n# code\n````\n# Heading", "# code", "String")]
    [TestCase("Markdown", "````text\n```\n# code\n````\n# Heading", "# Heading", "Keyword")]
    [TestCase("Markdown", "~~~text\ncode\n~~~~\n# Heading", "# Heading", "Keyword")]
    [TestCase("Markdown", "~~~~text\n~~~\n# code\n~~~~\n# Heading", "# code", "String")]
    [TestCase("Markdown", "~~~~text\n~~~\n# code\n~~~~\n# Heading", "# Heading", "Keyword")]
    [TestCase("Markdown", "<!-- **comment** -->", "**comment**", "Comment")]
    public void BundledGrammar_HighlightsLanguageTokensInBothThemes(
        string name, string content, string token, string role)
    {
        var engine = new RegionHighlightingEngine();
        foreach (var isDark in new[] { false, true })
        {
            var definition = engine.CreateDefinition(name, PaletteFrom(engine, isDark));
            var sections = HighlightAll(new TextDocument(content), definition);
            var start = content.IndexOf(token, StringComparison.Ordinal);
            Assert.That(sections.Any(section => section.Offset <= start &&
                section.Offset + section.Length >= start + token.Length &&
                ColorOf(section.Color) == ColorOf(definition.GetNamedColor(role))),
                Is.True, $"{name}: '{token}' must use {role}, dark={isDark}");
        }
    }

    [TestCase("INBOX_MESSAGE_JSON")]
    [TestCase("some_identifier")]
    [TestCase("MAX_RETRY_COUNT")]
    [TestCase("a_b_c")]
    [TestCase("a_b_")]
    [TestCase("_b_c")]
    [TestCase("1_b_")]
    [TestCase("_b_2")]
    public void Markdown_IntrawordUnderscoresDoNotUseEmphasisColorInBothThemes(string content)
    {
        var engine = new RegionHighlightingEngine();
        foreach (var isDark in new[] { false, true })
        {
            var definition = engine.CreateDefinition("Markdown", PaletteFrom(engine, isDark));
            var sections = HighlightAll(new TextDocument(content), definition);
            Assert.That(sections.Any(section =>
                ColorOf(section.Color) == ColorOf(definition.GetNamedColor("Keyword"))),
                Is.False, $"'{content}' must not use emphasis anywhere in the token, dark={isDark}");
        }
    }

    [TestCase("go", RegionGrammarAutoDetectTests.GoSample, "Go")]
    [TestCase("rust", RegionGrammarAutoDetectTests.RustSample, "Rust")]
    [TestCase("bash", RegionGrammarAutoDetectTests.BashSample, "Bash")]
    [TestCase("markdown", RegionGrammarAutoDetectTests.MarkdownSample, "Markdown")]
    [TestCase("error", RegionGrammarAutoDetectTests.ReviewerErrorMessageSample, "Error message")]
    public void BundledGrammar_EditorThemeRepushPreservesExplicitSelectionAndLocalColors(
        string token, string content, string name)
    {
        var engine = new RegionHighlightingEngine();
        var normalized = content.Replace("\r\n", "\n");
        using var editor = new AvalonEditRegionEditor(normalized, engine);
        using var sibling = new AvalonEditRegionEditor(normalized, engine);
        editor.ApplyPalette(PaletteFrom(engine, false));
        sibling.ApplyPalette(PaletteFrom(engine, false));
        editor.SetLanguage(token);
        sibling.SetLanguage(token);
        var control = (TextEditor)editor.Control;
        var definition = control.SyntaxHighlighting;
        var before = Snapshot(definition);
        Assert.That(definition.Name, Is.EqualTo(name));
        foreach (var isDark in new[] { true, false })
        {
            editor.ApplyPalette(PaletteFrom(engine, isDark));
            Assert.That(control.SyntaxHighlighting, Is.SameAs(definition));
            Assert.That(Snapshot(definition), Is.EqualTo(Snapshot(engine.CreateDefinition(name, PaletteFrom(engine, isDark)))));
            Assert.That(HighlightAll(control.Document, definition), Is.Not.Empty);
            Assert.That(Snapshot(((TextEditor)sibling.Control).SyntaxHighlighting), Is.EqualTo(before));
            Assert.That(editor.Text, Is.EqualTo(normalized));
        }
    }

    [TestCase("echo foo#bar")]
    [TestCase("echo foo}#bar")]
    [TestCase("echo ${#name}")]
    [TestCase("echo '# literal'")]
    [TestCase("echo \"# literal\"")]
    public void BundledBash_DoesNotTreatWordOrQuotedHashesAsComments(string content)
    {
        var engine = new RegionHighlightingEngine();
        foreach (var isDark in new[] { false, true })
        {
            var definition = engine.CreateDefinition("Bash", PaletteFrom(engine, isDark));
            Assert.That(HighlightAll(new TextDocument(content), definition)
                .Any(section => ColorOf(section.Color) == ColorOf(definition.GetNamedColor("Comment"))), Is.False);
        }
    }

    [TestCase("url: https://example.org/#section")]
    [TestCase("name: \"# not a comment\"")]
    [TestCase("name: '# not a comment'")]
    public void BundledYaml_DoesNotTreatScalarHashesAsComments(string content)
    {
        var engine = new RegionHighlightingEngine();
        var definition = engine.CreateDefinition("YAML", PaletteFrom(engine, true));
        Assert.That(HighlightAll(new TextDocument(content), definition)
            .Any(section => ColorOf(section.Color) == ColorOf(definition.GetNamedColor("Comment"))), Is.False);
    }

    [Test]
    public void Aliases_ResolveToExpectedGrammars()
    {
        var engine = new RegionHighlightingEngine();

        var expected = new (string Alias, string Grammar)[]
        {
            ("cs", "C#"), ("csharp", "C#"),
            ("js", "JavaScript"), ("javascript", "JavaScript"),
            ("cpp", "C++"), ("cxx", "C++"), ("cc", "C++"), ("c", "C++"),
            ("ps", "PowerShell"), ("ps1", "PowerShell"),
            ("vb", "VB"), ("visualbasic", "VB"),
            ("xaml", "XML"),
            ("htm", "HTML"),
            ("aspx", "ASP/XHTML"), ("xhtml", "ASP/XHTML"),
            ("latex", "TeX"),
            ("diff", "Patch"),
        };

        Assert.Multiple(() =>
        {
            foreach (var (alias, grammar) in expected)
                Assert.That(engine.ResolveFenceToken(alias), Is.EqualTo(grammar), $"alias '{alias}'");
        });
    }

    [Test]
    public void UnknownPinnedToken_ResolvesToNull()
    {
        var engine = new RegionHighlightingEngine();

        Assert.Multiple(() =>
        {
            Assert.That(engine.ResolveFenceToken("brainfuck"), Is.Null);
            Assert.That(engine.Resolve("brainfuck", "console.log('x')"), Is.Null,
                "an unsupported pinned token stays null and never falls back to detection");
        });
    }

    [Test]
    public void CreateDefinition_RetainsRuleShape_AndUsesIndependentColorObjects()
    {
        var engine = new RegionHighlightingEngine();
        var source = HighlightingManager.Instance.GetDefinition("C#");
        Assert.That(source, Is.Not.Null, "C# built-in must be installed");

        var cloned = engine.CreateDefinition("C#", PaletteFrom(engine, isDark: true));

        Assert.Multiple(() =>
        {
            Assert.That(cloned.MainRuleSet, Is.Not.SameAs(source!.MainRuleSet),
                "the editor must own a private rule-set graph");
            Assert.That(cloned.MainRuleSet.Rules, Has.Count.EqualTo(source!.MainRuleSet.Rules.Count),
                "rule count (regex rules) must be preserved");
            Assert.That(cloned.MainRuleSet.Spans, Has.Count.EqualTo(source.MainRuleSet.Spans.Count),
                "span count must be preserved");
        });

        var colorName = source!.NamedHighlightingColors.First().Name;
        Assert.That(cloned.GetNamedColor(colorName), Is.Not.SameAs(source.GetNamedColor(colorName)),
            "named colors must be cloned, not shared with the global definition");
    }

    [Test]
    public void TwoThemedDefinitions_AndRePush_NeverMutateManagerDefinition_ButRecolorRetainedObject()
    {
        var engine = new RegionHighlightingEngine();
        var manager = HighlightingManager.Instance.GetDefinition("C#")!;
        var managerBefore = Snapshot(manager);

        var light = engine.CreateDefinition("C#", PaletteFrom(engine, isDark: false));
        var dark = engine.CreateDefinition("C#", PaletteFrom(engine, isDark: true));

        var lightColors = Snapshot(light);
        var darkColors = Snapshot(dark);

        Assert.Multiple(() =>
        {
            Assert.That(Snapshot(manager), Is.EqualTo(managerBefore),
                "cloning for either theme must not mutate the shared HighlightingManager definition");
            Assert.That(lightColors, Is.Not.EqualTo(darkColors),
                "light and dark clones must differ in at least one token color");
        });

        // Re-push a new (dark) palette onto the already-created LIGHT object.
        light.ApplyPalette(PaletteFrom(engine, isDark: true));

        Assert.Multiple(() =>
        {
            Assert.That(Snapshot(light), Is.EqualTo(darkColors),
                "the retained definition object must recolor in place to the newly pushed theme");
            Assert.That(Snapshot(dark), Is.EqualTo(darkColors), "the sibling definition must be untouched");
            Assert.That(Snapshot(manager), Is.EqualTo(managerBefore),
                "re-pushing a palette must still never touch the global definition");
        });
    }

    [Test]
    public void DocumentHighlighter_PaintsTokenSections_AndPaletteDrivesTheirColors()
    {
        var engine = new RegionHighlightingEngine();
        var doc = new TextDocument(
            "using System;\npublic class P { void M() { int i = 0; } }");

        var darkDef = engine.CreateDefinition("C#", PaletteFrom(engine, isDark: true));
        var lightDef = engine.CreateDefinition("C#", PaletteFrom(engine, isDark: false));

        var darkSections = HighlightAll(doc, darkDef);
        var lightSections = HighlightAll(doc, lightDef);

        Assert.Multiple(() =>
        {
            Assert.That(darkSections, Is.Not.Empty, "the highlighter must produce token sections");
            Assert.That(darkSections.Any(s => s.Offset == 0 && s.Length == 5),
                "the leading 'using' keyword token must be its own highlighted section");
            Assert.That(darkSections.Any(s => s.Color is not null),
                "at least one section must carry a resolved color");

            var darkColors = darkSections.Select(s => s.Color).ToList();
            var lightColors = lightSections.Select(s => s.Color).ToList();
            Assert.That(darkColors, Is.Not.EqualTo(lightColors),
                "the applied palette (not the default grammar colors) must drive the token colors");
        });
    }

    [Test]
    public void Palettes_LightAndDarkColors_AreDistinctForEveryRole()
    {
        var light = RegionHighlightingPalettes.CreateColors(isDark: false);
        var dark = RegionHighlightingPalettes.CreateColors(isDark: true);

        Assert.That(dark.Keys, Is.EquivalentTo(light.Keys), "both themes must define the same roles");

        Assert.Multiple(() =>
        {
            foreach (var role in light.Keys)
                Assert.That(ColorOf(dark[role]), Is.Not.EqualTo(ColorOf(light[role])),
                    $"role '{role}' must have a theme-specific color");
        });
    }

    [Test]
    public void Palettes_ClassifySurfaces_AndContrastAgainstThem()
    {
        var darkSurface = Color.FromRgb(0x14, 0x11, 0x0D);
        var lightSurface = Color.FromRgb(0xFB, 0xFA, 0xF7);

        Assert.Multiple(() =>
        {
            Assert.That(RegionHighlightingPalettes.IsDark(darkSurface), Is.True);
            Assert.That(RegionHighlightingPalettes.IsDark(lightSurface), Is.False);
        });

        Assert.Multiple(() =>
        {
            foreach (var isDark in new[] { false, true })
            foreach (var (role, ink) in RegionHighlightingPalettes.CreateColors(isDark)
                .Concat(RegionHighlightingPalettes.CreateErrorMessageColors(isDark))
                .Concat(RegionHighlightingPalettes.CreateMarkdownColors(isDark)))
            {
                var surface = Luminance(isDark ? darkSurface : lightSurface);
                var foreground = Luminance(ColorOf(ink)!.Value);
                var contrast = (Math.Max(surface, foreground) + 0.05) / (Math.Min(surface, foreground) + 0.05);
                Assert.That(contrast, Is.GreaterThanOrEqualTo(4.5), $"{role}, dark={isDark}");
            }
        });
    }

    [TestCase("`code`")]
    [TestCase("``a ` b``")]
    public void Markdown_InlineCodeUsesDistinctMonospaceColor_AcrossThemeChanges(string code)
    {
        var engine = new RegionHighlightingEngine();
        var definition = engine.CreateDefinition("Markdown", PaletteFrom(engine, false));
        var inlineCode = definition.GetNamedColor("InlineCode");
        var content = $"Before {code} after **bold** *italic* ~~strike~~ [link](target) ![image](target)";
        var document = new TextDocument(content);

        foreach (var isDark in new[] { false, true, false })
        {
            var palette = PaletteFrom(engine, isDark);
            definition.ApplyPalette(palette);
            var expected = (Color)ColorConverter.ConvertFromString(isDark ? "#F3A6DE" : "#8A176F");
            var keyword = (Color)ColorConverter.ConvertFromString(isDark ? "#8AB4F8" : "#174EA6");
            var sections = HighlightAll(document, definition);
            var codeSection = sections.Single(section =>
                section.Offset == content.IndexOf(code, StringComparison.Ordinal) && section.Length == code.Length);

            Assert.Multiple(() =>
            {
                Assert.That(codeSection.Color, Is.SameAs(inlineCode));
                Assert.That(inlineCode.Name, Is.EqualTo("InlineCode"));
                Assert.That(inlineCode.FontFamily?.Source, Is.EqualTo("Consolas"));
                Assert.That(inlineCode.FontSize, Is.Null, "inline code inherits the editor's font size");
                Assert.That(ColorOf(inlineCode), Is.EqualTo(expected));
                Assert.That(ColorOf(palette.HighlightColors["Markdown/InlineCode"]), Is.EqualTo(expected));
                Assert.That(ColorOf(definition.GetNamedColor("Keyword")), Is.EqualTo(keyword));
                foreach (var role in new[] { "Keyword", "Tag", "Attribute", "Comment", "String" })
                {
                    Assert.That(ColorOf(inlineCode), Is.Not.EqualTo(ColorOf(definition.GetNamedColor(role))), role);
                    Assert.That(definition.GetNamedColor(role).FontFamily, Is.Null, role);
                }
            });

            foreach (var (token, role) in new[]
            {
                ("**bold**", "Keyword"), ("*italic*", "Keyword"), ("~~strike~~", "Keyword"),
                ("[link](target)", "Attribute"), ("![image](target)", "Tag"),
            })
            {
                var section = sections.Single(section =>
                    section.Offset == content.IndexOf(token, StringComparison.Ordinal) && section.Length == token.Length);
                Assert.That(section.Color, Is.SameAs(definition.GetNamedColor(role)), token);
            }

            var surface = Luminance(((SolidColorBrush)palette.Surface).Color);
            var foreground = Luminance(expected);
            var contrast = (Math.Max(surface, foreground) + 0.05) / (Math.Min(surface, foreground) + 0.05);
            Assert.That(contrast, Is.GreaterThanOrEqualTo(4.5), $"inline code, dark={isDark}");
        }
    }

    [TestCase("```")]
    [TestCase("~~~")]
    public void Markdown_FencedBlocksKeepStringStyle(string fence)
    {
        var engine = new RegionHighlightingEngine();
        foreach (var isDark in new[] { false, true })
        {
            var definition = engine.CreateDefinition("Markdown", PaletteFrom(engine, isDark));
            var document = new TextDocument($"{fence}text\n`code` **bold**\n{fence}");
            var sections = new DocumentHighlighter(document, definition).HighlightLine(2).Sections;
            Assert.That(sections, Is.Not.Empty);
            foreach (var section in sections)
            {
                Assert.That(section.Color, Is.SameAs(definition.GetNamedColor("String")));
                Assert.That(section.Color.FontFamily, Is.Null);
            }
        }
    }

    [Test]
    public void MarkdownDirectives_IsHiddenFromPickerAndResolver_ButCreateDefinitionWorks()
    {
        var engine = new RegionHighlightingEngine();
        const string content = "{{#if safety == \"direct\"}}\n{{/if}}";

        Assert.Multiple(() =>
        {
            Assert.That(engine.Options.Any(o => string.Equals(o.GrammarName, "MarkdownDirectives", StringComparison.OrdinalIgnoreCase)),
                Is.False);
            Assert.That(RegionLanguagePicker.Options.Any(o => string.Equals(o.GrammarName, "MarkdownDirectives", StringComparison.OrdinalIgnoreCase)),
                Is.False);
            Assert.That(engine.ResolveFenceToken("markdowndirectives"), Is.Null);
            Assert.That(engine.Resolve("markdowndirectives", content), Is.Null);
            Assert.That(engine.Resolve("MarkdownDirectives", content), Is.Null);
            Assert.That(engine.Resolve("", content), Is.Not.EqualTo("MarkdownDirectives"));
            Assert.That(engine.CreateDefinition("MarkdownDirectives", PaletteFrom(engine, false)).Name,
                Is.EqualTo("MarkdownDirectives"));
        });
    }

    [TestCase(false, "#174EA6", "#6841A5", "#D82323")]
    [TestCase(true, "#8AB4F8", "#C7ACF0", "#F64F4F")]
    public void MarkdownDirectives_PaintsDirectiveRoles_WithSpecifiedThemeColors(
        bool isDark, string expectedKeyword, string expectedVariable, string expectedString)
    {
        var engine = new RegionHighlightingEngine();
        var palette = PaletteFrom(engine, isDark);
        var definition = engine.CreateDefinition("MarkdownDirectives", palette);
        const string content = """
            {{#if safety == "direct"}}
            {{#if safety == "branch"}}
            {{#if safety == "dir\"ect"}}
            {{#unless safety}}
            {{else}}
            {{/unless}}
            {{/if}}
            """;
        var sections = HighlightAll(new TextDocument(content), definition);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(definition.GetNamedColor("DirectiveKeyword")),
                Is.EqualTo((Color)ColorConverter.ConvertFromString(expectedKeyword)));
            Assert.That(ColorOf(definition.GetNamedColor("DirectiveVariable")),
                Is.EqualTo((Color)ColorConverter.ConvertFromString(expectedVariable)));
            Assert.That(ColorOf(definition.GetNamedColor("DirectiveString")),
                Is.EqualTo((Color)ColorConverter.ConvertFromString(expectedString)));

            AssertTokenUsesRole(content, "#if", "DirectiveKeyword", sections, definition, occurrence: 0);
            AssertTokenUsesRole(content, "#if", "DirectiveKeyword", sections, definition, occurrence: 1);
            AssertTokenUsesRole(content, "#unless", "DirectiveKeyword", sections, definition);
            AssertTokenUsesRole(content, "{{else}}", "DirectiveKeyword", sections, definition, tokenOffset: 2, tokenLength: 4);
            AssertTokenUsesRole(content, "/unless", "DirectiveKeyword", sections, definition);
            AssertTokenUsesRole(content, "/if", "DirectiveKeyword", sections, definition);

            AssertTokenUsesRole(content, "safety", "DirectiveVariable", sections, definition, occurrence: 0);
            AssertTokenUsesRole(content, "safety", "DirectiveVariable", sections, definition, occurrence: 1);
            AssertTokenUsesRole(content, "safety", "DirectiveVariable", sections, definition, occurrence: 2);

            AssertTokenUsesRole(content, "\"direct\"", "DirectiveString", sections, definition);
            AssertTokenUsesRole(content, "\"branch\"", "DirectiveString", sections, definition);
            AssertTokenUsesRole(content, "\"dir\\\"ect\"", "DirectiveString", sections, definition);
            AssertEntireTokenUsesRole(content, "\"direct\"", "DirectiveString", sections, definition);
            AssertEntireTokenUsesRole(content, "\"dir\\\"ect\"", "DirectiveString", sections, definition);
        });
    }

    [Test]
    public void MarkdownDirectives_ColorsHaveThemeContrast_AndRedLightensInDarkTheme()
    {
        var engine = new RegionHighlightingEngine();
        var lightPalette = PaletteFrom(engine, isDark: false);
        var darkPalette = PaletteFrom(engine, isDark: true);
        var light = engine.CreateDefinition("MarkdownDirectives", lightPalette);
        var dark = engine.CreateDefinition("MarkdownDirectives", darkPalette);
        var lightRed = ColorOf(light.GetNamedColor("DirectiveString"))!.Value;
        var darkRed = ColorOf(dark.GetNamedColor("DirectiveString"))!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(lightRed, Is.EqualTo((Color)ColorConverter.ConvertFromString("#D82323")));
            Assert.That(darkRed, Is.EqualTo((Color)ColorConverter.ConvertFromString("#F64F4F")));
            Assert.That(Luminance(darkRed), Is.GreaterThan(Luminance(lightRed)));
        });

        foreach (var (palette, definition, label) in new[]
        {
            (lightPalette, light, "light"),
            (darkPalette, dark, "dark"),
        })
        {
            foreach (var role in new[] { "DirectiveKeyword", "DirectiveVariable", "DirectiveString" })
            {
                var surface = Luminance(((SolidColorBrush)palette.Surface).Color);
                var foreground = Luminance(ColorOf(definition.GetNamedColor(role))!.Value);
                var contrast = (Math.Max(surface, foreground) + 0.05) / (Math.Min(surface, foreground) + 0.05);
                Assert.That(contrast, Is.GreaterThanOrEqualTo(4.5), $"{role} {label}");
            }
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MarkdownDirectives_SuppressesDirectiveSpanInsideFencesInlineCodeAndComments(bool isDark)
    {
        var engine = new RegionHighlightingEngine();
        var definition = engine.CreateDefinition("MarkdownDirectives", PaletteFrom(engine, isDark));
        const string content = """
            <!-- {{#if safety == "comment"}} -->
            `{{#if safety == "inline"}}`
            ```text
            {{#if safety == "fence"}}
            ```
            {{#if safety == "direct"}}
            {{/if}}
            """;
        var sections = HighlightAll(new TextDocument(content), definition);

        Assert.Multiple(() =>
        {
            AssertTokenDoesNotUseRole(content, "#if", "DirectiveKeyword", sections, definition, occurrence: 0);
            AssertTokenDoesNotUseRole(content, "#if", "DirectiveKeyword", sections, definition, occurrence: 1);
            AssertTokenDoesNotUseRole(content, "#if", "DirectiveKeyword", sections, definition, occurrence: 2);
            AssertTokenUsesRole(content, "#if", "DirectiveKeyword", sections, definition, occurrence: 3);
            AssertTokenUsesRole(content, "/if", "DirectiveKeyword", sections, definition);

            AssertTokenDoesNotUseRole(content, "safety", "DirectiveVariable", sections, definition, occurrence: 0);
            AssertTokenDoesNotUseRole(content, "safety", "DirectiveVariable", sections, definition, occurrence: 1);
            AssertTokenDoesNotUseRole(content, "safety", "DirectiveVariable", sections, definition, occurrence: 2);
            AssertTokenUsesRole(content, "safety", "DirectiveVariable", sections, definition, occurrence: 3);

            AssertTokenDoesNotUseRole(content, "\"comment\"", "DirectiveString", sections, definition);
            AssertTokenDoesNotUseRole(content, "\"inline\"", "DirectiveString", sections, definition);
            AssertTokenDoesNotUseRole(content, "\"fence\"", "DirectiveString", sections, definition);
            AssertTokenUsesRole(content, "\"direct\"", "DirectiveString", sections, definition);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MarkdownDirectives_PreservesMarkdownParity_ForCoreMarkdownRules(bool isDark)
    {
        var engine = new RegionHighlightingEngine();
        var palette = PaletteFrom(engine, isDark);
        var markdown = engine.CreateDefinition("Markdown", palette);
        var directives = engine.CreateDefinition("MarkdownDirectives", palette);
        const string content = """
            # Heading
            Use **bold** text and _italic_ text.
            [guide](https://example.org)
            - item
            INBOX_MESSAGE_JSON some_identifier a_b_c
            Use `inline_code` and ``a ` b``.
            ```text
            # code
            ```
            <!-- **comment** -->
            """;
        var document = new TextDocument(content);
        var markdownSections = HighlightAll(document, markdown);
        var directiveSections = HighlightAll(document, directives);

        Assert.That(SnapshotSections(markdownSections), Is.EqualTo(SnapshotSections(directiveSections)));

        Assert.Multiple(() =>
        {
            var inline = "`inline_code`";
            var inlineOffset = content.IndexOf(inline, StringComparison.Ordinal);
            var markdownInline = markdownSections.Single(section => section.Offset == inlineOffset && section.Length == inline.Length);
            var directivesInline = directiveSections.Single(section => section.Offset == inlineOffset && section.Length == inline.Length);
            Assert.That(markdownInline.Color?.FontFamily?.Source, Is.EqualTo("Consolas"));
            Assert.That(directivesInline.Color?.FontFamily?.Source, Is.EqualTo("Consolas"));
            Assert.That(markdownInline.Color?.Name, Is.EqualTo("InlineCode"));
            Assert.That(directivesInline.Color?.Name, Is.EqualTo("InlineCode"));

            AssertTokenDoesNotUseRole(content, "INBOX_MESSAGE_JSON", "Keyword", markdownSections, markdown);
            AssertTokenDoesNotUseRole(content, "INBOX_MESSAGE_JSON", "Keyword", directiveSections, directives);
        });
    }

    [Test]
    public void MarkdownEditor_GrammarNameSwitching_PreservesPaletteAndRecoversFromInvalidAssignment()
    {
        var application = Application.Current ?? new Application();
        var resources = application.Resources;
        var savedResources = resources.Cast<DictionaryEntry>().ToArray();
        try
        {
            EnsureThemeResourcesForMarkdownEditor();
            using var workspace = new TestWorkspace();
            var settingsPath = workspace.GetPath("markdown-directives-settings.json");
            var light = PaletteFrom(RegionHighlightingEngine.Default, isDark: false);
            var dark = PaletteFrom(RegionHighlightingEngine.Default, isDark: true);
            using var defaultEditor = new AvalonEditMarkdownEditor(new ApplicationSettingsStore(settingsPath));
            using var initialized = new AvalonEditMarkdownEditor(new ApplicationSettingsStore(settingsPath))
            {
                GrammarName = "MarkdownDirectives",
            };

            Assert.Multiple(() =>
            {
                Assert.That(defaultEditor.GrammarName, Is.EqualTo("Markdown"));
                Assert.That(defaultEditor.Editor.SyntaxHighlighting.Name, Is.EqualTo("Markdown"));
                Assert.That(initialized.GrammarName, Is.EqualTo("MarkdownDirectives"));
                Assert.That(initialized.Editor.SyntaxHighlighting.Name, Is.EqualTo("MarkdownDirectives"));
            });

            foreach (var palette in new[] { dark, light, dark })
            {
                var before = initialized.Editor.SyntaxHighlighting;
                initialized.ApplyPalette(palette);
                Assert.That(initialized.Editor.SyntaxHighlighting, Is.SameAs(before));
                foreach (var role in new[] { "DirectiveKeyword", "DirectiveVariable", "DirectiveString" })
                    Assert.That(ColorOf(before.GetNamedColor(role)),
                        Is.EqualTo(ColorOf(palette.HighlightColors[$"MarkdownDirectives/{role}"])));
            }

            initialized.GrammarName = "Markdown";
            Assert.That(initialized.Editor.SyntaxHighlighting.Name, Is.EqualTo("Markdown"));
            Assert.That(ColorOf(initialized.Editor.SyntaxHighlighting.GetNamedColor("Keyword")),
                Is.EqualTo((Color)ColorConverter.ConvertFromString("#8AB4F8")));
            initialized.ApplyPalette(light);
            initialized.GrammarName = "MarkdownDirectives";
            Assert.That(ColorOf(initialized.Editor.SyntaxHighlighting.GetNamedColor("DirectiveString")),
                Is.EqualTo((Color)ColorConverter.ConvertFromString("#D82323")));

            initialized.Editor.SyntaxHighlighting = null;
            initialized.ApplyPalette(dark);
            Assert.That(initialized.Editor.SyntaxHighlighting, Is.Not.Null);
            var validDefinition = initialized.Editor.SyntaxHighlighting!;
            Assert.That(validDefinition.Name, Is.EqualTo("MarkdownDirectives"));
            Assert.That(() => initialized.GrammarName = "NotARealGrammar", Throws.ArgumentException);
            Assert.Multiple(() =>
            {
                Assert.That(initialized.GrammarName, Is.EqualTo("MarkdownDirectives"));
                Assert.That(initialized.Editor.SyntaxHighlighting, Is.SameAs(validDefinition));
                Assert.That(defaultEditor.Editor.SyntaxHighlighting.Name, Is.EqualTo("Markdown"));
            });

            initialized.SetText("# Heading");
            var sections = HighlightAll(initialized.Editor.Document, validDefinition);
            Assert.That(sections, Is.Not.Empty);
        }
        finally
        {
            resources.Clear();
            foreach (var entry in savedResources)
                resources[entry.Key] = entry.Value;
        }
    }

    [Test]
    public void ApplyPalette_PreservesSourceFonts_AndSupportsReplacementAndReset()
    {
        var source = new SyntheticDefinition();
        var sourceColor = source.GetNamedColor("SynthKeyword");
        sourceColor.FontFamily = new FontFamily("Courier New");
        sourceColor.FontSize = 15;
        var engine = new RegionHighlightingEngine();
        var palette = PaletteFrom(engine, false);
        var definition = new RegionHighlightingDefinition(source, palette);
        var color = definition.GetNamedColor("SynthKeyword");

        Assert.That(color.FontFamily, Is.EqualTo(sourceColor.FontFamily));
        Assert.That(color.FontSize, Is.EqualTo(15));

        definition.ApplyPalette(palette with
        {
            HighlightColors = new Dictionary<string, HighlightingColor>
            {
                ["SynthLang/SynthKeyword"] = new()
                {
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 18,
                },
            },
        });
        Assert.That(color.FontFamily?.Source, Is.EqualTo("Consolas"));
        Assert.That(color.FontSize, Is.EqualTo(18));
        Assert.That(sourceColor.FontFamily.Source, Is.EqualTo("Courier New"));
        Assert.That(sourceColor.FontSize, Is.EqualTo(15));

        definition.ApplyPalette(palette);
        Assert.That(color.FontFamily, Is.EqualTo(sourceColor.FontFamily));
        Assert.That(color.FontSize, Is.EqualTo(15));
    }

    [Test]
    public void ErrorMessage_PickerSelectionPersistsErrorFenceToken()
    {
        const string content = "System.InvalidOperationException: incomplete dump";
        var region = EmbeddedCodeRegionBlock.Build(content, "error");
        Assert.That(region.ToFencedText(), Is.EqualTo("```error\n" + content + "\n```"));
        Assert.That(RegionLanguagePicker.Options.Single(option => option.FenceToken == "error"),
            Is.EqualTo(new RegionLanguageOption("error", "Error message", "Error message")));
    }

    [TestCase("Method", "#8AB4F8", "#174EA6")]
    [TestCase("Time", "#E4CD8B", "#795B00")]
    [TestCase("MonthDay", "#71C9B8", "#00695C")]
    [TestCase("FileName", "#C7ACF0", "#6841A5")]
    [TestCase("LineNumber", "#78966D", "#527647")]
    [TestCase("ExceptionType", "#F64F4F", "#D82323")]
    [TestCase("Subtle", "#909090", "#707070")]
    public void ErrorMessage_PaletteUsesSpecifiedScopedColors(string role, string dark, string light)
    {
        var engine = new RegionHighlightingEngine();
        foreach (var isDark in new[] { false, true })
        {
            var colors = RegionHighlightingPalettes.CreateErrorMessageColors(isDark);
            Assert.That(colors.Keys, Has.All.StartsWith("Error message/"));
            var expected = (Color)ColorConverter.ConvertFromString(isDark ? dark : light);
            Assert.That(ColorOf(colors["Error message/" + role]), Is.EqualTo(expected));
            Assert.That(ColorOf(engine.CreatePaletteColors(isDark)["Error message/" + role]), Is.EqualTo(expected));
        }
    }

    [TestCase("   at System.Console.WriteLine(String value)", "WriteLine")]
    [TestCase("   at Example.Worker.at()", "at")]
    [TestCase("   at Example.Worker.in()", "in")]
    [TestCase("   at Example.Worker.<>c__DisplayClass12_0.<Run>b__0()", "<Run>b__0")]
    [TestCase("   at Example.Worker.<<Run>g__Step|4>d.MoveNext()", "MoveNext")]
    [TestCase("   at Example.Worker.Run[T](T value)", "Run[T]")]
    [TestCase("   at SquadDash.MainWindow.<.ctor>b__697_80(Object _, EventArgs _)", "<.ctor>b__697_80")]
    [TestCase("   at SquadDash.MainWindow.<.cctor>b__0_0()", "<.cctor>b__0_0")]
    [TestCase("   at Example.Worker..ctor()", "ctor")]
    [TestCase("   at System.IO.__Error.WinIOError(Int32 errorCode, String maybeFullPath)", "WinIOError")]
    [TestCase("    at java.base/java.lang.Double.parseDouble(Unknown Source)", "parseDouble")]
    [TestCase("    at module/com.example.Worker.run(Unknown Source)", "run")]
    [TestCase("    at loader/java.base/java.lang.Double.parseDouble(Double.java:651)", null)]
    [TestCase("    at java.base//java.lang.Double.parseDouble(Double.java:651)", null)]
    [TestCase("   at Example.Worker.Run(String text = \"Other.Call()\")", null)]
    public void ErrorMessage_MethodColorsOnlyTerminalIdentifier(string content, string? method)
    {
        if (method is null)
        {
            AssertErrorSections(content);
            return;
        }
        var expected = new List<(string, string)> { ("at", "Subtle"), (method, "Method") };
        if (content.EndsWith("()", StringComparison.Ordinal))
            expected.Add(("()", "Subtle"));
        else
            expected.AddRange(new[] { ("(", "Subtle"), (")", "Subtle") });
        AssertErrorSections(content, expected.ToArray());
    }

    [TestCase(@"C:\My Source\SquadDash\MainWindow.xaml.cs", "MainWindow.xaml.cs")]
    [TestCase("/src/My Project/Worker.cs", "Worker.cs")]
    [TestCase("Worker.cs", "Worker.cs")]
    [TestCase("in file.cs", "in file.cs")]
    [TestCase(@"C:\My Source\in file.cs", "in file.cs")]
    [TestCase(@"\\server\share\My Source\Worker File.cs", "Worker File.cs")]
    public void ErrorMessage_SourceColorsOnlyFileNameAndLineDigits(string path, string fileName)
    {
        AssertErrorSections("   at Example.Worker.Run(String value) in " + path + ":line 214",
            ("at", "Subtle"), ("Run", "Method"), ("(", "Subtle"), (")", "Subtle"),
            ("in", "Subtle"), (fileName, "FileName"), (":line", "Subtle"), ("214", "LineNumber"));
    }

    [TestCase("com.example.shop.PaymentService.processPayment", "processPayment", "PaymentService.java", "76")]
    [TestCase("java.base/jdk.internal.math.FloatingDecimal.readJavaFormatString", "readJavaFormatString", "FloatingDecimal.java", "2054")]
    [TestCase("java.base/java.lang.Double.parseDouble", "parseDouble", "Double.java", "651")]
    public void ErrorMessage_JavaSourceColorsOnlyFileNameAndLineDigits(
        string qualifiedMethod, string method, string fileName, string lineNumber)
    {
        foreach (var indent in new[] { "", "    ", "\t" })
        {
            AssertErrorSections($"{indent}at {qualifiedMethod}({fileName}:{lineNumber})",
                ("at", "Subtle"), (method, "Method"), ("(", "Subtle"),
                (fileName, "FileName"), (":", "Subtle"), (lineNumber, "LineNumber"), (")", "Subtle"));
        }
    }

    [TestCase("Unknown Source")]
    [TestCase("Native Method")]
    [TestCase("PaymentService.java")]
    [TestCase("PaymentService.java:nope")]
    [TestCase("PaymentService.java:76x")]
    [TestCase("PaymentService.java:76 extra")]
    [TestCase("PaymentService.cs:76")]
    [TestCase("String value, Int32 count")]
    [TestCase("String value = \"PaymentService.java:76\"")]
    public void ErrorMessage_JavaSourceDoesNotColorOtherParenthesizedContent(string content)
    {
        AssertErrorSections($"   at com.example.shop.PaymentService.processPayment({content})",
            ("at", "Subtle"), ("processPayment", "Method"), ("(", "Subtle"), (")", "Subtle"));
    }

    [TestCase("Occurred: 2026-10-19T15:16:39.096-04:00", ".096-04:00")]
    [TestCase("Operation: 2026-10-19T15:16:39Z", "Z")]
    [TestCase("2026-10-19T15:16:39+04:30", "+04:30")]
    [TestCase("2026-10-19T15:16:39-04:00", "-04:00")]
    [TestCase("2026-10-19T15:16:39", "")]
    [TestCase("2026-10-19T15:16:39.6510568", ".6510568")]
    [TestCase("2026-10-19T15:16:39.6510568Z", ".6510568Z")]
    [TestCase("2026-10-19T15:16:39.6510568+04:30", ".6510568+04:30")]
    [TestCase("2026-10-19T15:16:39.6510568-04:00", ".6510568-04:00")]
    public void ErrorMessage_TimestampSubduesSeparatorsAndTailButPreservesYearAndMainTime(string content, string tail)
    {
        var expected = new List<(string, string)>
        {
            ("-", "Subtle"), ("10-19", "MonthDay"), ("T", "Subtle"), ("15:16:39", "Time"),
        };
        if (tail.Length > 0)
            expected.Add((tail, "Subtle"));
        AssertErrorSections(content, expected.ToArray());
        AssertErrorSections(content + " neutral 12:34:56 - at in :line ()", expected.ToArray());
    }

    [TestCase("System.InvalidOperationException: Failed to decompose plan JSON.", "InvalidOperationException")]
    [TestCase(" ---> System.InvalidOperationException: Failed to decompose plan JSON.", "InvalidOperationException")]
    [TestCase("Caused by: java.lang.NumberFormatException: Failed to decompose plan JSON.", "NumberFormatException")]
    [TestCase("AggregateException: Failed to decompose plan JSON.", "AggregateException")]
    public void ErrorMessage_ExceptionColorsTypeAndExplanationSeparately(string content, string exceptionType)
    {
        AssertErrorSections(content, (exceptionType, "ExceptionType"), ("Failed to decompose plan JSON.", "Message"));
    }

    [Test]
    public void ErrorMessage_BareExceptionPreservesExplanationColor()
    {
        AssertErrorSections("Exception: Failed to decompose plan JSON.", ("Failed to decompose plan JSON.", "Message"));
    }

    [Test]
    public void ErrorMessage_ExceptionTypeDoesNotLeakAcrossLines()
    {
        AssertErrorSections("System.InvalidOperationException:\nnext line remains neutral",
            ("InvalidOperationException", "ExceptionType"));
    }

    [TestCase("Observed java.lang.NumberFormatException and AggregateException.",
        "NumberFormatException", "AggregateException")]
    [TestCase("IllegalArgumentException NumberFormatException", "IllegalArgumentException", "NumberFormatException")]
    public void ErrorMessage_ExceptionTypesOutsideHeaders(string content, string first, string second)
    {
        AssertErrorSections(content, (first, "ExceptionType"), (second, "ExceptionType"));
    }

    [Test]
    public void ErrorMessage_ExceptionTypeRequiresUppercaseAndWordBoundaries()
    {
        AssertErrorSections("illegalArgumentException 1IllegalArgumentException _IllegalArgumentException IllegalArgumentExceptions Exception");
    }

    [Test]
    public void ErrorMessage_ExceptionTypeDoesNotOverrideStackFrameRoles()
    {
        AssertErrorSections(
            "   at Example.AggregateException.ThrowException(NumberFormatException value) in C:\\Source\\AggregateException.cs:line 123\n" +
            "    at java.base/java.lang.NumberFormatException.ThrowException(NumberFormatException.java:42)",
            ("at", "Subtle"), ("ThrowException", "Method"), ("(", "Subtle"), (")", "Subtle"),
            ("in", "Subtle"), ("AggregateException.cs", "FileName"), (":line", "Subtle"), ("123", "LineNumber"),
            ("at", "Subtle"), ("ThrowException", "Method"), ("(", "Subtle"),
            ("NumberFormatException.java", "FileName"), (":", "Subtle"), ("42", "LineNumber"), (")", "Subtle"));
    }

    [TestCase("\n")]
    [TestCase("\r\n")]
    public void ErrorMessage_RepresentativeJavaDumpHasExactColoredBoundaries(string newline)
    {
        const string content = """
            Exception in thread "main" java.lang.IllegalArgumentException: Failed to process payment for order 1042
                at com.example.shop.PaymentService.processPayment(PaymentService.java:76)
                at com.example.shop.OrderController.checkout(OrderController.java:42)
                at com.example.shop.Application.main(Application.java:18)
            Caused by: java.lang.NumberFormatException: For input string: "12.5x"
                at java.base/jdk.internal.math.FloatingDecimal.readJavaFormatString(FloatingDecimal.java:2054)
                at java.base/jdk.internal.math.FloatingDecimal.parseDouble(FloatingDecimal.java:110)
                at java.base/java.lang.Double.parseDouble(Double.java:651)
                at com.example.shop.PaymentService.parseAmount(PaymentService.java:91)
                at com.example.shop.PaymentService.processPayment(PaymentService.java:72)
            """;

        AssertErrorSections(content.Replace("\r\n", "\n").Replace("\n", newline),
            ("IllegalArgumentException", "ExceptionType"), ("Failed to process payment for order 1042", "Message"),
            ("at", "Subtle"), ("processPayment", "Method"), ("(", "Subtle"),
            ("PaymentService.java", "FileName"), (":", "Subtle"), ("76", "LineNumber"), (")", "Subtle"),
            ("at", "Subtle"), ("checkout", "Method"), ("(", "Subtle"),
            ("OrderController.java", "FileName"), (":", "Subtle"), ("42", "LineNumber"), (")", "Subtle"),
            ("at", "Subtle"), ("main", "Method"), ("(", "Subtle"),
            ("Application.java", "FileName"), (":", "Subtle"), ("18", "LineNumber"), (")", "Subtle"),
            ("NumberFormatException", "ExceptionType"), ("For input string: \"12.5x\"", "Message"),
            ("at", "Subtle"), ("readJavaFormatString", "Method"), ("(", "Subtle"),
            ("FloatingDecimal.java", "FileName"), (":", "Subtle"), ("2054", "LineNumber"), (")", "Subtle"),
            ("at", "Subtle"), ("parseDouble", "Method"), ("(", "Subtle"),
            ("FloatingDecimal.java", "FileName"), (":", "Subtle"), ("110", "LineNumber"), (")", "Subtle"),
            ("at", "Subtle"), ("parseDouble", "Method"), ("(", "Subtle"),
            ("Double.java", "FileName"), (":", "Subtle"), ("651", "LineNumber"), (")", "Subtle"),
            ("at", "Subtle"), ("parseAmount", "Method"), ("(", "Subtle"),
            ("PaymentService.java", "FileName"), (":", "Subtle"), ("91", "LineNumber"), (")", "Subtle"),
            ("at", "Subtle"), ("processPayment", "Method"), ("(", "Subtle"),
            ("PaymentService.java", "FileName"), (":", "Subtle"), ("72", "LineNumber"), (")", "Subtle"));
    }

    [TestCase("Run() 2026-10-19T15:16:39 214 Worker.cs")]
    [TestCase("Occurred: 2026-10-19")]
    [TestCase("Occurred: 2026-10-19T15:16:39garbage")]
    [TestCase("   at Example.Worker.Run() in C:\\Worker.cs:line nope")]
    [TestCase("   at Example.Worker.Run(\nString value)")]
    [TestCase("See (PaymentService.java:76) for details.")]
    [TestCase("PaymentService.java:76")]
    [TestCase("   com.example.shop.PaymentService.processPayment(PaymentService.java:76)")]
    [TestCase("Look at com.example.shop.PaymentService.processPayment(PaymentService.java:76)")]
    [TestCase("at com.example.shop.PaymentService.processPayment(PaymentService.java:76) is an example")]
    [TestCase("   at com.example.shop.PaymentService.processPayment(PaymentService.java:\n76)")]
    public void ErrorMessage_ContextDoesNotLeakToUnrelatedText(string content)
    {
        AssertErrorSections(content);
    }

    [TestCase("\n")]
    [TestCase("\r\n")]
    public void ErrorMessage_RepresentativeDecomposeDumpHasExactColoredBoundaries(string newline)
    {
        var content = RegionGrammarAutoDetectTests.ReviewerErrorMessageSample.Replace("\r\n", "\n").Replace("\n", newline);
        AssertErrorSections(content, content.IndexOf("2026", StringComparison.Ordinal),
            ("-", "Subtle"), ("10-19", "MonthDay"), ("T", "Subtle"), ("15:16:39", "Time"), (".096-04:00", "Subtle"),
            ("InvalidOperationException", "ExceptionType"),
            ("Failed to decompose plan JSON.", "Message"),
            ("at", "Subtle"), ("ExecuteAsync", "Method"), ("(", "Subtle"), (")", "Subtle"),
            ("at", "Subtle"), ("MoveNext", "Method"), ("()", "Subtle"),
            ("at", "Subtle"), ("MoveNext", "Method"), ("()", "Subtle"),
            ("in", "Subtle"), ("DecomposeRunner.cs", "FileName"), (":line", "Subtle"), ("214", "LineNumber"));
    }

    private const string HintRefreshErrorSample = """
        Operation: HintRefreshTimer.Tick
        Occurred: 2026-10-20T20:52:01.6510568-04:00

        System.NullReferenceException: Object reference not set to an instance of an object.
           at SquadDash.MainWindow.get__settingsSnapshot() in D:\Drive\Source\SquadDash-public\SquadDash\MainWindow.xaml.cs:line 775
           at SquadDash.MainWindow.IsSpeechConfigured() in D:\Drive\Source\SquadDash-public\SquadDash\MainWindow.xaml.cs:line 30814
           at SquadDash.MainWindow.BuildShortcutsHint() in D:\Drive\Source\SquadDash-public\SquadDash\MainWindow.xaml.cs:line 30852
           at SquadDash.MainWindow.<.ctor>b__697_80(Object _, EventArgs _) in D:\Drive\Source\SquadDash-public\SquadDash\MainWindow.xaml.cs:line 1414
        """;

    [TestCase("\n")]
    [TestCase("\r\n")]
    public void ErrorMessage_HintRefreshReferenceHasExactColoredBoundaries(string newline)
    {
        AssertErrorSections(HintRefreshErrorSample.Replace("\r\n", "\n").Replace("\n", newline),
            ("-", "Subtle"), ("10-20", "MonthDay"), ("T", "Subtle"), ("20:52:01", "Time"),
            (".6510568-04:00", "Subtle"), ("NullReferenceException", "ExceptionType"),
            ("Object reference not set to an instance of an object.", "Message"),
            ("at", "Subtle"), ("get__settingsSnapshot", "Method"), ("()", "Subtle"), ("in", "Subtle"),
            ("MainWindow.xaml.cs", "FileName"), (":line", "Subtle"), ("775", "LineNumber"),
            ("at", "Subtle"), ("IsSpeechConfigured", "Method"), ("()", "Subtle"), ("in", "Subtle"),
            ("MainWindow.xaml.cs", "FileName"), (":line", "Subtle"), ("30814", "LineNumber"),
            ("at", "Subtle"), ("BuildShortcutsHint", "Method"), ("()", "Subtle"), ("in", "Subtle"),
            ("MainWindow.xaml.cs", "FileName"), (":line", "Subtle"), ("30852", "LineNumber"),
            ("at", "Subtle"), ("<.ctor>b__697_80", "Method"), ("(", "Subtle"), (")", "Subtle"), ("in", "Subtle"),
            ("MainWindow.xaml.cs", "FileName"), (":line", "Subtle"), ("1414", "LineNumber"));
    }

    [Test]
    [NonParallelizable]
    public void ErrorMessage_ThemeSourceRepushUsesActiveImportantAndSubtleTextWithoutLeaking()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var keys = new[] { "CodeSurface", "CodeText", "ImportantText", "SubtleText" };
        var before = keys.ToDictionary(key => key, key => app.Resources.Contains(key) ? app.Resources[key] : null);
        var engine = new RegionHighlightingEngine();
        var manager = HighlightingManager.Instance.HighlightingDefinitions.ToArray();
        var managerBefore = manager.SelectMany(Snapshot).ToArray();
        using var editor = new AvalonEditRegionEditor(HintRefreshErrorSample, engine);
        using var sibling = new AvalonEditRegionEditor(HintRefreshErrorSample, engine);
        var source = new ThemeResourcePaletteSource(engine);
        try
        {
            app.Resources["CodeSurface"] = new SolidColorBrush(Color.FromRgb(0xFB, 0xFA, 0xF7));
            app.Resources["CodeText"] = Brushes.Black;
            app.Resources["ImportantText"] = new SolidColorBrush(Color.FromRgb(0x53, 0x37, 0x1E));
            app.Resources["SubtleText"] = Brushes.DimGray;
            var light = source.CreatePalette();
            editor.ApplyPalette(light);
            sibling.ApplyPalette(light);
            editor.SetLanguage("error");
            sibling.SetLanguage("error");
            var control = (TextEditor)editor.Control;
            var definition = control.SyntaxHighlighting;
            var siblingBefore = Snapshot(((TextEditor)sibling.Control).SyntaxHighlighting);
            foreach (var isDark in new[] { true, false })
            {
                var surface = isDark ? Color.FromRgb(0x14, 0x11, 0x0D) : Color.FromRgb(0xFB, 0xFA, 0xF7);
                var message = isDark ? Color.FromRgb(0xE5, 0xD5, 0xC0) : Color.FromRgb(0x53, 0x37, 0x1E);
                var subtle = isDark ? Colors.DarkGray : Colors.DimGray;
                app.Resources["CodeSurface"] = new SolidColorBrush(surface);
                app.Resources["ImportantText"] = new SolidColorBrush(message);
                app.Resources["SubtleText"] = new SolidColorBrush(subtle);
                var palette = source.CreatePalette();
                editor.ApplyPalette(palette);
                Assert.That(control.SyntaxHighlighting, Is.SameAs(definition));
                Assert.That(ColorOf(definition.GetNamedColor("Message")), Is.EqualTo(message));
                Assert.That(ColorOf(definition.GetNamedColor("Subtle")), Is.EqualTo(subtle));
                Assert.That(ColorOf(palette.HighlightColors["Error message/Subtle"]), Is.EqualTo(subtle));
                var sections = HighlightAll(control.Document, definition);
                AssertTokenUsesRole(HintRefreshErrorSample, ".6510568-04:00", "Subtle", sections, definition);
                for (var frame = 0; frame < 4; frame++)
                {
                    AssertTokenUsesRole(HintRefreshErrorSample, "   at", "Subtle", sections, definition,
                        occurrence: frame, tokenOffset: 3, tokenLength: 2);
                    AssertTokenUsesRole(HintRefreshErrorSample, " in ", "Subtle", sections, definition,
                        occurrence: frame, tokenOffset: 1, tokenLength: 2);
                    AssertTokenUsesRole(HintRefreshErrorSample, ":line", "Subtle", sections, definition, occurrence: frame);
                    AssertTokenUsesRole(HintRefreshErrorSample, "(", "Subtle", sections, definition, occurrence: frame);
                    AssertTokenUsesRole(HintRefreshErrorSample, ")", "Subtle", sections, definition, occurrence: frame);
                }
                Assert.That(HighlightAll(control.Document, definition).Any(section => ColorOf(section.Color) == message), Is.True);
                Assert.That(Snapshot(((TextEditor)sibling.Control).SyntaxHighlighting), Is.EqualTo(siblingBefore));
                Assert.That(ColorOf(palette.HighlightColors["$Method"]),
                    Is.EqualTo(ColorOf(RegionHighlightingPalettes.CreateColors(isDark)["$Method"])));
                var contrast = (Math.Max(Luminance(surface), Luminance(message)) + 0.05) /
                    (Math.Min(Luminance(surface), Luminance(message)) + 0.05);
                Assert.That(contrast, Is.GreaterThanOrEqualTo(4.5));
            }
            app.Resources["ImportantText"] = Brushes.Magenta;
            app.Resources["SubtleText"] = Brushes.Cyan;
            editor.ApplyPalette(source.CreatePalette());
            Assert.That(ColorOf(definition.GetNamedColor("Message")), Is.EqualTo(Colors.Magenta),
                "the active resource, not a hard-coded light/dark approximation, must drive the message");
            Assert.That(ColorOf(definition.GetNamedColor("Subtle")), Is.EqualTo(Colors.Cyan),
                "the active SubtleText resource must supersede the grammar fallback and generic role");
            Assert.That(HighlightingManager.Instance.HighlightingDefinitions, Is.EqualTo(manager));
            Assert.That(manager.SelectMany(Snapshot), Is.EqualTo(managerBefore));
        }
        finally
        {
            foreach (var key in keys)
            {
                if (before[key] is { } value)
                    app.Resources[key] = value;
                else
                    app.Resources.Remove(key);
            }
        }
    }

    private static void AssertErrorSections(string content, params (string Text, string Role)[] expected)
        => AssertErrorSections(content, 0, expected);

    private static void AssertErrorSections(string content, int expectedSearchStart, params (string Text, string Role)[] expected)
    {
        var engine = new RegionHighlightingEngine();
        foreach (var isDark in new[] { false, true })
        {
            var palette = PaletteFrom(engine, isDark);
            var colors = new Dictionary<string, HighlightingColor>(palette.HighlightColors)
            {
                ["Error message/Message"] = new() { Foreground = new FixedBrush(isDark ? Colors.White : Colors.Black) },
            };
            var definition = engine.CreateDefinition("Error message", palette with { HighlightColors = colors });
            var sections = HighlightAll(new TextDocument(content), definition);
            Assert.That(sections, Has.Count.EqualTo(expected.Length),
                $"Unexpected colored ranges: {content}\n" +
                string.Join("\n", sections.Select(section =>
                    $"{section.Offset}:{section.Length} {section.Color.Name}: '{content.Substring(section.Offset, section.Length)}'")));
            var searchStart = expectedSearchStart;
            for (var i = 0; i < expected.Length; i++)
            {
                var (text, role) = expected[i];
                var offset = content.IndexOf(text, searchStart, StringComparison.Ordinal);
                Assert.That(offset, Is.GreaterThanOrEqualTo(0), text);
                Assert.That((sections[i].Offset, sections[i].Length, ColorOf(sections[i].Color)),
                    Is.EqualTo((offset, text.Length, ColorOf(definition.GetNamedColor(role)))),
                    $"{role}: only '{text}' should be colored, dark={isDark}");
                searchStart = offset + text.Length;
            }
        }
    }

    [Test]
    public void RegisterCustomGrammar_ExposesOption_NameAliasAndThemedCustomColor()
    {
        var engine = new RegionHighlightingEngine();
        var defaultOptionsBefore = RegionHighlightingEngine.Default.Options.Count;

        var lightInk = Colors.Fuchsia;
        var darkInk = Colors.Cyan;
        var definition = new SyntheticDefinition();

        engine.Register(
            new RegionLanguageOption("synthlang", "Synthetic", "SynthLang"),
            () => definition,
            isDark => new Dictionary<string, HighlightingColor>
            {
                ["SynthKeyword"] = new HighlightingColor
                {
                    Name = "SynthKeyword",
                    Foreground = new FixedBrush(isDark ? darkInk : lightInk),
                },
            },
            "slang");

        Assert.Multiple(() =>
        {
            // Registration/options/name/alias.
            Assert.That(engine.ResolveFenceToken("synthlang"), Is.EqualTo("SynthLang"));
            Assert.That(engine.ResolveFenceToken("slang"), Is.EqualTo("SynthLang"), "the alias must resolve too");
            Assert.That(engine.Options.Any(o => o.FenceToken == "synthlang" && o.DisplayName == "Synthetic"),
                Is.True, "the custom grammar must appear in the picker options");

            // Light/dark palette hook with the custom color.
            Assert.That(ColorOf(engine.CreatePaletteColors(isDark: true)["SynthLang/SynthKeyword"]),
                Is.EqualTo(darkInk));
            Assert.That(ColorOf(engine.CreatePaletteColors(isDark: false)["SynthLang/SynthKeyword"]),
                Is.EqualTo(lightInk));
        });

        // The custom color must flow all the way through CreateDefinition into the cloned graph.
        var palette = new RegionEditorPalette(
            new SolidColorBrush(Colors.Black),
            new SolidColorBrush(Colors.White),
            engine.CreatePaletteColors(isDark: true));
        var cloned = engine.CreateDefinition("SynthLang", palette);
        Assert.That(ColorOf(cloned.GetNamedColor("SynthKeyword")), Is.EqualTo(darkInk),
            "the themed custom color must be applied to the editor-local definition");

        // Isolation: registering on a local engine must never mutate the Default singleton.
        Assert.Multiple(() =>
        {
            Assert.That(RegionHighlightingEngine.Default.ResolveFenceToken("synthlang"), Is.Null);
            Assert.That(RegionHighlightingEngine.Default.ResolveFenceToken("slang"), Is.Null);
            Assert.That(RegionHighlightingEngine.Default.Options.Count, Is.EqualTo(defaultOptionsBefore),
                "the Default engine's option set must be unchanged");
        });
    }

    [Test]
    public void Register_RejectsDuplicateReservedAndNonExplicitTokens()
    {
        Assert.Multiple(() =>
        {
            // Reserved plain-text token.
            Assert.That(() => Fresh().Register(
                    new RegionLanguageOption("text", "X", "X"), () => new SyntheticDefinition()),
                Throws.ArgumentException, "the reserved 'text' token must be rejected");

            // Reserved auto-detect (empty) token.
            Assert.That(() => Fresh().Register(
                    new RegionLanguageOption("", "X", "X"), () => new SyntheticDefinition()),
                Throws.ArgumentException, "an empty (auto-detect) token must be rejected");

            // Fence token already taken by a built-in alias.
            Assert.That(() => Fresh().Register(
                    new RegionLanguageOption("csharp", "X", "X"), () => new SyntheticDefinition()),
                Throws.ArgumentException, "a token already registered (as a built-in alias) must be rejected");

            // Grammar name already registered (an installed built-in).
            Assert.That(() => Fresh().Register(
                    new RegionLanguageOption("synthlang", "X", "C#"), () => new SyntheticDefinition()),
                Throws.ArgumentException, "a grammar name that is already registered must be rejected");

            // Reserved/non-explicit alias.
            Assert.That(() => Fresh().Register(
                    new RegionLanguageOption("synthlang", "X", "SynthLang"), () => new SyntheticDefinition(),
                    aliases: "text"),
                Throws.ArgumentException, "a reserved/non-explicit alias must be rejected");

            // A second registration reusing an already-registered custom grammar/token must fail.
            var engine = Fresh();
            engine.Register(new RegionLanguageOption("synthlang", "X", "SynthLang"), () => new SyntheticDefinition());
            Assert.That(() => engine.Register(
                    new RegionLanguageOption("synthlang", "Y", "SynthLang2"), () => new SyntheticDefinition()),
                Throws.ArgumentException, "re-using a live fence token must be rejected");
        });

        static RegionHighlightingEngine Fresh() => new();
    }

    [Test]
    public void CreateDefinition_RejectsFactoryNameMismatchRatherThanSilentlyDroppingPalette()
    {
        var engine = new RegionHighlightingEngine();
        engine.Register(new RegionLanguageOption("mismatch", "Mismatch", "Mismatch"), () => new SyntheticDefinition());
        Assert.That(() => engine.CreateDefinition("Mismatch", PaletteFrom(engine, true)),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Block_BuildCsharp_HostsEditorWithCsharpSyntaxHighlighting()
    {
        var block = EmbeddedCodeRegionBlock.Build("Console.WriteLine(\"Hello, world!\");", "csharp");
        var editor = LogicalDescendants<TextEditor>(block).Single();

        Assert.That(editor.SyntaxHighlighting, Is.InstanceOf<RegionHighlightingDefinition>());
        Assert.That(((IHighlightingDefinition)editor.SyntaxHighlighting).Name, Is.EqualTo("C#"));
    }

    [Test]
    public void Block_PinPlainThenReturnToAuto_UnpinsThenAutoDetects_LabelAndFenceFollow()
    {
        var block = EmbeddedCodeRegionBlock.Build("Console.WriteLine(\"Hello, world!\");", "csharp");
        var editor = LogicalDescendants<TextEditor>(block).Single();
        var label = LogicalDescendants<TextBlock>(block).First();

        // Pin to plain text: highlighting is removed, token persists, label reads plain text.
        block.Language = RegionLanguagePicker.PlainTextToken;
        Assert.Multiple(() =>
        {
            Assert.That(editor.SyntaxHighlighting, Is.Null, "plain-text pinning must clear highlighting");
            Assert.That(block.Language, Is.EqualTo(RegionLanguagePicker.PlainTextToken));
            Assert.That(label.Text, Is.EqualTo("Plain text"));
        });

        // Return to auto-detect: the empty token is persisted and the C# content is detected.
        block.Language = "";
        Assert.Multiple(() =>
        {
            Assert.That(editor.SyntaxHighlighting, Is.InstanceOf<RegionHighlightingDefinition>());
            Assert.That(((IHighlightingDefinition)editor.SyntaxHighlighting).Name, Is.EqualTo("C#"));
            Assert.That(block.Language, Is.EqualTo(""), "the auto-detect fence token stays empty");
            Assert.That(label.Text, Does.Contain("C#"), "the label must surface the detected grammar");
            Assert.That(block.ToFencedText().Split('\n')[0], Is.EqualTo("```"),
                "serialization persists a bare fence when the region is left on auto-detect");
        });
    }

    [TestCase("csharp")]
    [TestCase("json")]
    [TestCase("typescript")]
    [TestCase("yaml")]
    [TestCase("sql")]
    public void Block_NotifyThemeChanged_RecolorsDefinition_WhileContentCaretSelectionStayStable(string token)
    {
        var block = EmbeddedCodeRegionBlock.Build("Console.WriteLine(\"Hello, world!\");", token);
        var editor = LogicalDescendants<TextEditor>(block).Single();
        var definition = (RegionHighlightingDefinition)editor.SyntaxHighlighting;

        editor.Select(2, 5);
        var textBefore = editor.Document.Text;
        var selectionStart = editor.SelectionStart;
        var selectionLength = editor.SelectionLength;
        var caret = editor.TextArea.Caret.Offset;
        var contentBefore = block.Content;

        block.NotifyThemeChanged(PaletteFrom(RegionHighlightingEngine.Default, isDark: false));
        var lightColors = Snapshot(definition);
        block.NotifyThemeChanged(PaletteFrom(RegionHighlightingEngine.Default, isDark: true));
        var darkColors = Snapshot(definition);

        Assert.Multiple(() =>
        {
            Assert.That(lightColors, Is.Not.EqualTo(darkColors),
                "a theme switch must recolor the live editor definition in place");
            Assert.That(editor.Document.Text, Is.EqualTo(textBefore), "text must be untouched");
            Assert.That(block.Content, Is.EqualTo(contentBefore), "serialized content must be untouched");
            Assert.That(editor.SelectionStart, Is.EqualTo(selectionStart), "selection start must be stable");
            Assert.That(editor.SelectionLength, Is.EqualTo(selectionLength), "selection length must be stable");
            Assert.That(editor.TextArea.Caret.Offset, Is.EqualTo(caret), "caret must be stable");
        });
    }

    [Test]
    public void AutoDetectDebounce_Starts_OnEdit_WhenLoadedAndAutoDetect_StopsOnPin()
    {
        var editor = new AvalonEditRegionEditor("", new RegionHighlightingEngine());
        using var _ = HostLoaded(editor.Control);

        editor.SetLanguage(""); // auto-detect
        Assert.That(editor.IsAutoDetectPending, Is.False, "SetLanguage must cancel any pending debounce");

        editor.InsertText("console.log('x');");
        Assert.That(editor.IsAutoDetectPending, Is.True,
            "an edit on a loaded auto-detect editor must arm the debounce timer");

        editor.SetLanguage(RegionLanguagePicker.PlainTextToken);
        Assert.That(editor.IsAutoDetectPending, Is.False, "pinning a language must cancel the debounce");
    }

    [TestCase("csharp", "C#")]
    [TestCase("text", null)]
    public void AutoDetectDebounce_DoesNotArm_WhenLanguageIsPinned(string token, string? expected)
    {
        var editor = new AvalonEditRegionEditor("", new RegionHighlightingEngine());
        using var host = HostLoaded(editor.Control);
        Assert.That(editor.Control.IsLoaded, Is.True, "pinning must be exercised on a loaded editor");

        editor.SetLanguage(token);
        Assert.That(editor.HighlightingName, Is.EqualTo(expected));
        var changes = 0;
        editor.HighlightingChanged += (_, _) => changes++;

        editor.InsertText("console.log('x');");
        AssertPinned();
        editor.Text = "<!DOCTYPE html><html><body>changed</body></html>";
        AssertPinned();

        void AssertPinned()
        {
            Assert.That(RegionGrammarResolver.DetectBuiltIn(editor.Text), Is.Not.Null.And.Not.EqualTo(expected),
                "edited content must be detectable as a different grammar if the pin were removed");
            Assert.Multiple(() =>
            {
                Assert.That(editor.IsAutoDetectPending, Is.False,
                    "neither Plain text nor an explicit language may re-arm detection after an edit");
                Assert.That(editor.HighlightingName, Is.EqualTo(expected),
                    "text changes must not replace the pinned highlighting");
                Assert.That(changes, Is.Zero, "edits must not emit a highlighting change while pinned");
            });
        }
    }

    [Test]
    public void AutoDetectDebounce_DoesNotArm_BeforeTheEditorIsLoaded()
    {
        var editor = new AvalonEditRegionEditor("", new RegionHighlightingEngine());

        editor.SetLanguage(""); // auto-detect, but the control was never hosted/loaded
        editor.InsertText("console.log('x');");

        Assert.That(editor.IsAutoDetectPending, Is.False,
            "the debounce must stay disarmed until the editor has actually loaded");
    }

    // AVALONEDIT-20261014-AMD-001 (Step 5a): a declaration-style C# reviewer sample pasted into an
    // Auto-detect region must drive RefreshHighlighting to a non-null "C#" grammar.
    [TestCase("public interface ICalloutWindow {\n    event EventHandler? RefreshTargetRect;\n    bool ShowDiagnostics { get; set; }\n    void ForceRefresh();\n}")]
    [TestCase("public class MainWindow {\n}")]
    public void AutoDetect_DeclarationStyleCSharpSample_AppliesCsharpHighlighting(string sample)
    {
        var editor = new AvalonEditRegionEditor(sample, new RegionHighlightingEngine());
        using var host = HostLoaded(editor.Control);

        editor.SetLanguage(""); // auto-detect classifies the current content synchronously

        Assert.That(editor.HighlightingName, Is.EqualTo("C#"),
            "declaration-style C# must auto-detect to the C# grammar");
    }

    [Test]
    public void AutoDetectDebounce_ReclassifiesAndClearsHighlightingAfterTypingPauses()
    {
        var editor = new AvalonEditRegionEditor("Console.WriteLine(1);");
        using var host = HostLoaded(editor.Control);
        editor.SetLanguage("");
        Assert.That(editor.HighlightingName, Is.EqualTo("C#"));

        EditAndWait("console.log(1);", "JavaScript");
        EditAndWait("ordinary prose", null);

        void EditAndWait(string text, string? expected)
        {
            var previous = editor.HighlightingName;
            var frame = new DispatcherFrame();
            var deadline = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            deadline.Tick += (_, _) => frame.Continue = false;
            void Changed(object? sender, EventArgs args) => frame.Continue = false;
            editor.HighlightingChanged += Changed;
            try
            {
                editor.Text = text;
                Assert.That(editor.HighlightingName, Is.EqualTo(previous), "typing must not classify synchronously");
                Assert.That(editor.IsAutoDetectPending, Is.True);
                deadline.Start();
                Dispatcher.PushFrame(frame);
                Assert.That(editor.HighlightingName, Is.EqualTo(expected), "the real timer must apply the new grammar");
                Assert.That(editor.IsAutoDetectPending, Is.False);
            }
            finally
            {
                deadline.Stop();
                editor.HighlightingChanged -= Changed;
                editor.SetLanguage("text");
            }
            // Keep the next edit in Auto-detect, without changing the document.
            editor.SetLanguage("");
        }
    }

    private static string SanitizeToken(string name) =>
        Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9_+.#\-]", "");

    private static RegionEditorPalette PaletteFrom(RegionHighlightingEngine engine, bool isDark)
    {
        var surface = new SolidColorBrush(isDark ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Colors.White);
        var text = new SolidColorBrush(isDark ? Colors.White : Colors.Black);
        return new RegionEditorPalette(surface, text, engine.CreatePaletteColors(isDark));
    }

    private static List<string> Snapshot(IHighlightingDefinition definition) =>
        definition.NamedHighlightingColors
            .Select(c => $"{c.Name}={ColorOf(c)}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    private static List<HighlightedSection> HighlightAll(TextDocument document, IHighlightingDefinition definition)
    {
        var highlighter = new DocumentHighlighter(document, definition);
        var sections = new List<HighlightedSection>();
        for (var line = 1; line <= document.LineCount; line++)
            sections.AddRange(highlighter.HighlightLine(line).Sections);
        return sections;
    }

    private static List<string> SnapshotSections(IEnumerable<HighlightedSection> sections) =>
        sections.Select(section =>
        {
            var color = ColorOf(section.Color);
            var rgb = color is null ? "null" : $"{color.Value.R:X2}{color.Value.G:X2}{color.Value.B:X2}";
            var font = section.Color?.FontFamily?.Source ?? "null";
            return $"{section.Offset}:{section.Length}:{section.Color?.Name ?? "null"}:{rgb}:{font}";
        }).ToList();

    private static void AssertTokenUsesRole(
        string content, string token, string role, IReadOnlyList<HighlightedSection> sections,
        IHighlightingDefinition definition, int occurrence = 0, int tokenOffset = 0, int? tokenLength = null)
    {
        var start = IndexOfOccurrence(content, token, occurrence) + tokenOffset;
        var end = start + (tokenLength ?? token.Length);
        var expected = ColorOf(definition.GetNamedColor(role));
        Assert.That(sections.Any(section =>
                section.Offset <= start &&
                section.Offset + section.Length >= end &&
                ColorOf(section.Color) == expected),
            Is.True, $"{token} must use {role}");
    }

    private static void AssertTokenDoesNotUseRole(
        string content, string token, string role, IReadOnlyList<HighlightedSection> sections,
        IHighlightingDefinition definition, int occurrence = 0)
    {
        var start = IndexOfOccurrence(content, token, occurrence);
        var end = start + token.Length;
        var expected = ColorOf(definition.GetNamedColor(role));
        Assert.That(sections.Any(section =>
                section.Offset < end &&
                section.Offset + section.Length > start &&
                ColorOf(section.Color) == expected),
            Is.False, $"{token} must not use {role}");
    }

    private static void AssertEntireTokenUsesRole(
        string content, string token, string role, IReadOnlyList<HighlightedSection> sections,
        IHighlightingDefinition definition, int occurrence = 0)
    {
        var start = IndexOfOccurrence(content, token, occurrence);
        var end = start + token.Length;
        var expected = ColorOf(definition.GetNamedColor(role));
        var overlapping = sections
            .Where(section => section.Offset < end && section.Offset + section.Length > start)
            .ToArray();
        Assert.That(overlapping, Is.Not.Empty, token);
        for (var offset = start; offset < end; offset++)
            Assert.That(overlapping.Any(section => section.Offset <= offset && section.Offset + section.Length > offset),
                Is.True, $"{token} offset {offset} must be colored");
        foreach (var section in overlapping)
            Assert.That(ColorOf(section.Color), Is.EqualTo(expected), $"{token} should be fully {role}");
    }

    private static int IndexOfOccurrence(string content, string token, int occurrence)
    {
        var start = -1;
        for (var i = 0; i <= occurrence; i++)
        {
            start = content.IndexOf(token, start + 1, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Missing occurrence {occurrence} of token '{token}'");
        }
        return start;
    }

    private static void EnsureThemeResourcesForMarkdownEditor()
    {
        if (Application.Current is null)
            _ = new Application();
        var resources = Application.Current!.Resources;
        resources["ThemedButtonStyle"] = new Style(typeof(Button));
        resources["ThemedMenuItemStyle"] = new Style(typeof(MenuItem));
        resources["ThemedContextMenuStyle"] = new Style(typeof(ContextMenu));
        resources["ThemedMenuSeparatorStyle"] = new Style(typeof(Separator));
        resources["SubtleBorder"] = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        resources["PanelBorder"] = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
        resources["LabelText"] = Brushes.Black;
        resources["FontSizeTiny"] = 10d;
        resources["FontSizeNormal"] = 14d;
        resources["FontSizeMedium"] = 15d;
        resources["FontSizeLarge"] = 16d;
        resources["FontSizeHeading"] = 18d;
        resources["CodeSurface"] = new SolidColorBrush(Color.FromRgb(0xFB, 0xFA, 0xF7));
        resources["CodeText"] = new SolidColorBrush(Color.FromRgb(0x2A, 0x1E, 0x12));
        resources["ImportantText"] = new SolidColorBrush(Color.FromRgb(0x53, 0x37, 0x1E));
        resources["FontSizeBody"] = 14d;
    }

    /// <summary>
    /// Reads a <see cref="HighlightingColor"/>'s foreground color without constructing AvalonEdit's
    /// internal <c>SimpleHighlightingBrush</c>: prefer a <see cref="SolidColorBrush"/> from
    /// <see cref="HighlightingBrush.GetBrush"/>, else fall back to <see cref="HighlightingBrush.GetColor"/>.
    /// </summary>
    private static Color? ColorOf(HighlightingColor? color)
    {
        var brush = color?.Foreground;
        if (brush is null)
            return null;
        return brush.GetBrush(null!) is SolidColorBrush solid ? solid.Color : brush.GetColor(null!);
    }

    private static double Luminance(Color c)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }

    /// <summary>
    /// Connects <paramref name="control"/> to an offscreen, zero-size, non-visible
    /// <see cref="HwndSource"/> and pumps the dispatcher until WPF has raised its Loaded pass, so
    /// <c>AvalonEditRegionEditor.IsAutoDetectPending</c> can be exercised against a genuinely
    /// <see cref="UIElement.IsLoaded"/> editor. No window is shown.
    /// </summary>
    private static HwndSource HostLoaded(Control control)
    {
        var source = new HwndSource(new HwndSourceParameters("region-highlighting-test", 0, 0)
        {
            WindowStyle = 0, // no WS_VISIBLE
        })
        {
            RootVisual = control,
        };
        Pump();
        return source;
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject node)
                continue;
            if (node is T match)
                yield return match;
            foreach (var deeper in LogicalDescendants<T>(node))
                yield return deeper;
        }
    }

    /// <summary>A trivial, frozen single-color <see cref="HighlightingBrush"/> for custom palettes.</summary>
    private sealed class FixedBrush : HighlightingBrush
    {
        private readonly SolidColorBrush _brush;

        public FixedBrush(Color color)
        {
            _brush = new SolidColorBrush(color);
            _brush.Freeze();
        }

        public override Brush GetBrush(ITextRunConstructionContext context) => _brush;
    }

    /// <summary>
    /// Minimal, test-only grammar used to prove custom <see cref="RegionHighlightingEngine.Register"/>
    /// options/palette hooks, deliberately NOT a real downstream grammar.
    /// </summary>
    private sealed class SyntheticDefinition : IHighlightingDefinition
    {
        private readonly HighlightingColor _keyword = new()
        {
            Name = "SynthKeyword",
            Foreground = new FixedBrush(Colors.Black),
        };

        public SyntheticDefinition()
        {
            MainRuleSet = new HighlightingRuleSet { Name = null };
            MainRuleSet.Rules.Add(new HighlightingRule { Regex = new Regex("synth"), Color = _keyword });
        }

        public string Name => "SynthLang";
        public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>();
        public HighlightingRuleSet MainRuleSet { get; }
        public IEnumerable<HighlightingColor> NamedHighlightingColors => new[] { _keyword };
        public HighlightingColor GetNamedColor(string name) => name == "SynthKeyword" ? _keyword : null!;
        public HighlightingRuleSet GetNamedRuleSet(string name) => null!;
    }
}
