using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ICSharpCode.AvalonEdit.Highlighting;

namespace SquadDash.Tests;

/// <summary>
/// Proof for <see cref="RegionGrammarResolver"/>: fence-first alias lookup, plain-text pinning,
/// unsupported pinned tokens, strict "no heuristic unless auto-detect" discipline, return-to-auto,
/// and the conservative built-in classifier. None of these tests instantiate a WPF editor; the few
/// that consult <see cref="HighlightingManager"/> run on an STA thread to match the registry's
/// resource-loading expectations.
/// </summary>
[TestFixture]
public sealed class RegionGrammarAutoDetectTests
{
    private const string ErrorMessageGrammar = "Error message";

    [TestCase("", true)]
    [TestCase("text", false)]
    [TestCase("TEXT", false)]
    [TestCase("csharp", false)]
    [TestCase("c#", false)]
    public void ShouldAutoDetect_TrueOnlyForEmptyToken(string token, bool expected)
    {
        Assert.That(RegionGrammarResolver.ShouldAutoDetect(token), Is.EqualTo(expected));
    }

    [Test]
    public void ShouldAutoDetect_TreatsNullAsAutoDetect()
    {
        Assert.That(RegionGrammarResolver.ShouldAutoDetect(null!), Is.True);
    }

    [Test]
    public void Resolve_ExplicitToken_UsesAliasMap_AndNeverRunsHeuristic()
    {
        var aliasCalls = new List<string>();
        Func<string, string?> alias = token =>
        {
            aliasCalls.Add(token);
            return token == "csharp" ? "C#" : null;
        };

        var result = RegionGrammarResolver.Resolve("csharp", "anything", alias, ThrowingDetector);

        Assert.That(result, Is.EqualTo("C#"));
        Assert.That(aliasCalls, Is.EqualTo(new[] { "csharp" }));
    }

    [TestCase("csharp", "C#")]
    [TestCase("text", null)]
    [Apartment(ApartmentState.STA)]
    public void Resolve_PinnedFenceToken_OverridesConflictingBuiltInHeuristic(string token, string? expected)
    {
        var engine = new RegionHighlightingEngine();

        Assert.That(engine.Resolve("", JavaScriptSample), Is.EqualTo("JavaScript"),
            "the same content must trigger the real heuristic when the fence token is empty");
        Assert.That(engine.Resolve(token, JavaScriptSample), Is.EqualTo(expected),
            "an explicit or Plain text fence token must override conflicting content");
    }

    [Test]
    public void Resolve_UnsupportedPinnedToken_ReturnsNull_WithoutHeuristic()
    {
        var aliasCalls = 0;
        Func<string, string?> alias = _ =>
        {
            aliasCalls++;
            return null; // unknown/unsupported token
        };

        var result = RegionGrammarResolver.Resolve("brainfuck", "content", alias, ThrowingDetector);

        Assert.That(result, Is.Null);
        Assert.That(aliasCalls, Is.EqualTo(1), "every explicit token must go through the alias map exactly once");
    }

    [TestCase("text")]
    [TestCase("TEXT")]
    [TestCase("Text")]
    public void Resolve_PlainTextToken_ReturnsNull_WithoutAliasOrHeuristic(string token)
    {
        var aliasCalls = 0;
        Func<string, string?> alias = _ => { aliasCalls++; return "C#"; };

        var result = RegionGrammarResolver.Resolve(token, "console.log('x')", alias, ThrowingDetector);

        Assert.That(result, Is.Null);
        Assert.That(aliasCalls, Is.Zero, "plain-text pinning must not consult the alias map");
    }

    [Test]
    public void Resolve_EmptyToken_InvokesInjectedDetector_ExactlyOnce_AndNotAlias()
    {
        var detectorCalls = 0;
        Func<string, string?> detector = content =>
        {
            detectorCalls++;
            Assert.That(content, Is.EqualTo("body { color: red; }"));
            return "CSS";
        };
        Func<string, string?> alias = _ => throw new InvalidOperationException("alias must not run in auto-detect");

        var result = RegionGrammarResolver.Resolve("", "body { color: red; }", alias, detector);

        Assert.That(result, Is.EqualTo("CSS"));
        Assert.That(detectorCalls, Is.EqualTo(1));
    }

    [Test]
    public void Resolve_ReturnToAuto_SwitchesFromAliasLookupToHeuristic()
    {
        var detectorCalls = 0;
        Func<string, string?> detector = _ => { detectorCalls++; return "JavaScript"; };
        var aliasCalls = 0;
        Func<string, string?> alias = token => { aliasCalls++; return token == "js" ? "JavaScript" : null; };

        // Pinned explicit: alias used, heuristic untouched.
        var pinned = RegionGrammarResolver.Resolve("js", "console.log(1)", alias, detector);
        // User clears the pin (return to auto): heuristic used, alias untouched.
        var auto = RegionGrammarResolver.Resolve("", "console.log(1)", alias, detector);

        Assert.That(pinned, Is.EqualTo("JavaScript"));
        Assert.That(auto, Is.EqualTo("JavaScript"));
        Assert.That(aliasCalls, Is.EqualTo(1));
        Assert.That(detectorCalls, Is.EqualTo(1));
    }

    [Test]
    public void Resolve_NullResolveFenceToken_Throws()
    {
        Assert.That(
            () => RegionGrammarResolver.Resolve("csharp", "x", null!),
            Throws.ArgumentNullException);
    }

    private const string CSharpSample =
        "using System;\nnamespace Demo;\npublic class P {\n  public static void Main(string[] args) {\n    Console.WriteLine(\"hi\");\n  }\n}";

    private const string JavaSample =
        "import java.util.List;\npublic class Main {\n  public static void main(String[] args) {\n    System.out.println(\"hi\");\n  }\n}";

    private const string CppSample =
        "#include <iostream>\nint main() {\n  std::cout << \"hi\" << std::endl;\n  return 0;\n}";

    private const string PowerShellSample =
        "param($Name)\nWrite-Host \"hi $Name\"\nGet-ChildItem -Path . | ForEach-Object { $_.Name }";

    internal const string JavaScriptSample =
        "const greet = (name) => {\n  console.log('hi ' + name);\n};\nmodule.exports = greet;";

    internal const string ReviewerTypeScriptSample = """
        interface User {
          name: string;
          age: number;
        }

        const greet = (user: User): string => `Hello, ${user.name}!`;
        """;

    internal const string ReviewerYamlSample = """
        server:
          host: localhost
          port: 8080
          enabled: true
          tags:
            - development
            - testing
        """;

    internal const string ReviewerSqlSample = """
        SELECT name, COUNT(*) AS total
        FROM Orders
        WHERE status = 'completed'
        GROUP BY name
        HAVING COUNT(*) > 5
        ORDER BY total DESC;
        """;

    // Representative sample: no exact Decompose-plan reviewer dump was found in local .squad artifacts.
    internal const string ReviewerErrorMessageSample = """
        Operation: Decompose-plan
        Occurred: 2026-10-19T15:16:39.096-04:00
        System.InvalidOperationException: Failed to decompose plan JSON.
           at SquadDash.Plan.DecomposeRunner.ExecuteAsync(System.Threading.CancellationToken cancellationToken)
           at SquadDash.Plan.DecomposeRunner.<>c__DisplayClass12_0.<<ExecuteAsync>g__RunStep|4>d.MoveNext()
           at SquadDash.Plan.DecomposeRunner+<<ExecuteAsync>$>d__11.MoveNext() in D:\Drive\Source\SquadDash-public\SquadDash\Plan\DecomposeRunner.cs:line 214
        """;

    internal const string GoSample = """
        package main

        import "fmt"

        func main() {
            message := "Hello"
            fmt.Println(message)
        }
        """;

    internal const string RustSample = """
        use std::collections::HashMap;

        pub fn count() -> usize {
            let mut values = HashMap::new();
            values.insert("team", 42);
            println!("{:?}", values);
            values.len()
        }
        """;

    internal const string BashSample = """
        #!/usr/bin/env bash
        name="SquadDash"
        if [ -n "$name" ]; then
            echo "Hello ${name}"
        fi
        """;

    internal const string MarkdownSample = """
        # Region editor

        - **Highlighting** supports [languages](https://example.org/languages).

        ```text
        A code sample
        ```
        """;

    private const string CssSample =
        "body {\n  color: #fff;\n  margin: 0;\n}\n@media (max-width: 600px) { .a { display: none; } }";

    private const string HtmlSample =
        "<!DOCTYPE html>\n<html>\n<head><title>x</title></head>\n<body><h1>Hi</h1></body>\n</html>";

    private const string XmlSample =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root><child>x</child></root>";

    internal const string XamlSample = """
        <?xml version="1.0" encoding="utf-8"?>
        <Window x:Class="Demo.MainWindow"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
            <!-- A XAML control, not an HTML button. -->
            <Grid><Button Content="{Binding Caption}" /></Grid>
        </Window>
        """;

    private const string PhpSample =
        "<?php\n$name = 'world';\necho \"Hello $name\";\n?>";

    private const string PhpSnippetSample =
        "public function greet($name) {\n" +
        "    echo \"Hello $name\";\n" +
        "}";

    private const string PythonSample =
        "def greet(name):\n" +
        "    print(f\"hi {name}\")\n\n" +
        "if __name__ == \"__main__\":\n" +
        "    greet(\"Ada\")";

    internal const string JsonSample =
        "{\n" +
        "  \"name\": \"SquadDash\",\n" +
        "  \"features\": [\"regions\", \"syntax\"],\n" +
        "  \"enabled\": true\n" +
        "}";

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Resolve_AutoDetectedJson_UsesBundledDefinitionWithoutChangingBuiltInDetection()
    {
        var engine = new RegionHighlightingEngine();
        var name = engine.Resolve("", JsonSample);
        Assert.That(name, Is.EqualTo("Json"));
        var palette = new RegionEditorPalette(
            System.Windows.Media.Brushes.White, System.Windows.Media.Brushes.Black,
            engine.CreatePaletteColors(false));
        var definition = engine.CreateDefinition(name!, palette);
        Assert.That(definition.GetNamedColor("Attribute"), Is.Not.Null,
            "JSON property coloring comes from the bundled grammar");
        Assert.That(engine.Resolve("", TsqlSample), Is.EqualTo("TSQL"),
            "T-SQL-specific constructs must retain TSQL auto-detection");
    }

    internal const string TsqlSample =
        "DECLARE @teamId int = 42;\n" +
        "SELECT Name, Status\n" +
        "FROM dbo.Squads\n" +
        "WHERE TeamId = @teamId;";

    private const string VbSample =
        "Public Class Greeter\n" +
        "    Public Function SayHello(name As String) As String\n" +
        "        Return \"Hello \" & name\n" +
        "    End Function\n" +
        "End Class";

    private const string PatchSample =
        "diff --git a/file.cs b/file.cs\n" +
        "--- a/file.cs\n" +
        "+++ b/file.cs\n" +
        "@@ -1,2 +1,2 @@\n" +
        "-old\n" +
        "+new";

    private const string TexSample =
        "\\documentclass{article}\n" +
        "\\usepackage{amsmath}\n" +
        "\\begin{document}\n" +
        "\\section{Intro}\n" +
        "Hello\n" +
        "\\end{document}";

    private const string AspXhtmlSample =
        "<%@ Page Language=\"C#\" %>\n" +
        "<asp:Label runat=\"server\" Text=\"Hello\" />";

    private const string XmlFragmentSample =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
        "  <PropertyGroup><TargetFramework>net10.0-windows</TargetFramework></PropertyGroup>\n" +
        "</Project>";

    [TestCase(ReviewerTypeScriptSample, "TypeScript")]
    [TestCase(ReviewerYamlSample, "YAML")]
    [TestCase(ReviewerSqlSample, "SQL")]
    [TestCase(JavaScriptSample, "JavaScript")]
    [TestCase(TsqlSample, "TSQL")]
    [TestCase("SELECT name FROM Orders;\nGO", "TSQL")]
    [TestCase("CREATE PROC GetOrders AS SELECT name FROM Orders;", "TSQL")]
    [TestCase("SELECT [name] FROM [Orders];", "TSQL")]
    [TestCase("CREATE TABLE Orders (id INTEGER);", "SQL")]
    [TestCase("INSERT INTO Orders VALUES (1);", "SQL")]
    [TestCase("type Name = string;\nconsole.log('hi');", "TypeScript")]
    [TestCase("const greet = (name: string): string => name;", "TypeScript")]
    [TestCase("const greet = (name: string) => console.log(name);", "TypeScript")]
    [TestCase("function greet(name: string) { console.log(name); }", "TypeScript")]
    [TestCase("const count: number = 42;\nconsole.log(count);", "TypeScript")]
    [TestCase("server:\n  host: localhost", "YAML")]
    [TestCase("tags:\n  - development\n  - testing", "YAML")]
    [TestCase(CssSample, "CSS")]
    [TestCase(GoSample, "Go")]
    [TestCase(RustSample, "Rust")]
    [TestCase(BashSample, "Bash")]
    [TestCase(MarkdownSample, "Markdown")]
    [TestCase("func greet(name string) string { return name }", "Go")]
    [TestCase("package main\nimport (\n\"fmt\"\n)", "Go")]
    [TestCase("value := 42\nfmt.Println(value)", "Go")]
    [TestCase("fn main() { println!(\"hello\"); }", "Rust")]
    [TestCase("let mut values = vec![1, 2];", "Rust")]
    [TestCase("use std::io;", "Rust")]
    [TestCase("if [ -n \"$name\" ]; then\n  echo \"$name\"\nfi", "Bash")]
    [TestCase("function greet() {\n  echo \"Hello $USER\"\n}", "Bash")]
    [TestCase("for file in *.txt; do\n  echo \"$file\"\ndone", "Bash")]
    [TestCase("#!/bin/bash\necho hello", "Bash")]
    [TestCase("#!/usr/bin/env -S bash -eu\necho hello", "Bash")]
    [TestCase("# Heading\n- item\nSee [guide](./guide.md).", "Markdown")]
    [TestCase("# Heading\n~~~text\ncode\n~~~", "Markdown")]
    [TestCase(CSharpSample, "C#")]
    [TestCase(JavaSample, "Java")]
    [TestCase(CppSample, "C++")]
    [TestCase(PowerShellSample, "PowerShell")]
    [TestCase(PhpSample, "PHP")]
    [TestCase(PhpSnippetSample, "PHP")]
    [TestCase(HtmlSample, "HTML")]
    [TestCase(XmlSample, "XML")]
    [TestCase(XamlSample, "XAML")]
    [TestCase("<Window />", "XAML")]
    [TestCase("<UserControl><StackPanel><TextBlock Text=\"Hello\" /></StackPanel></UserControl>", "XAML")]
    [TestCase("<ResourceDictionary><Style TargetType=\"Button\" /></ResourceDictionary>", "XAML")]
    [TestCase("<Grid><Button Content=\"OK\" /></Grid>", "XAML")]
    [TestCase("<Page x:Class=\"Demo.Page\">", "XAML")]
    [TestCase("<local:View x:Class='Demo.View' />", "XAML")]
    [TestCase("<local:View xmlns:local='clr-namespace:Demo' x:Class='Demo.View' />", "XAML")]
    [TestCase("<Button xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Content='OK' />", "XAML")]
    [TestCase("<p:Button xmlns:p='http://schemas.microsoft.com/winfx/2006/xaml/presentation' />", "XAML")]
    [TestCase("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button' />", "XAML")]
    [TestCase("\uFEFF<?xml version='1.0'?>\n<!-- view -->\n<Window />", "XAML")]
    [TestCase("<Window\n    x:Class=\"Demo.Window\"", "XAML")]
    [TestCase("<Window Width='1' Width='2' />", "XAML")]
    [TestCase("<root><child>x</child></root>", "XML")]
    [TestCase("<Grid xmlns='urn:inventory' />", "XML")]
    [TestCase("<View xmlns:x='urn:other' x:Class='Inventory' />", "XML")]
    [TestCase("<window />", "XML")]
    [TestCase("<WindowSettings />", "XML")]
    [TestCase("<button>OK</button>", "HTML")]
    [TestCase("<style>button { color: red; }</style>", "HTML")]
    [TestCase("<html><body><pre>&lt;Window x:Class='Demo.Window' /&gt;</pre></body></html>", "HTML")]
    [TestCase("<?xml version='1.0'?><root><!-- <Window /> --></root>", "XML")]
    [TestCase("<root note=\"x:Class='Demo.Window'\" />", "XML")]
    [TestCase(JsonSample, "Json")]
    [TestCase(PythonSample, "Python")]
    [TestCase("package demo;\nimport java.util.List;\nclass Demo { }", "Java")]
    [TestCase("using System;\n// # heading\n// echo \"$USER\"", "C#")]
    [TestCase("const go = () => console.log('hello');", "JavaScript")]
    [Apartment(ApartmentState.STA)]
    public void Resolve_AutoDetect_UsesEngineRegisteredGrammars(string content, string expected)
    {
        var engine = new RegionHighlightingEngine();
        var normalized = content.Replace("\r\n", "\n");
        Assert.That(engine.Resolve("", normalized), Is.EqualTo(expected));
        Assert.That(engine.Resolve("text", normalized), Is.Null);
    }

    [TestCase(ReviewerTypeScriptSample, "TypeScript")]
    [TestCase(ReviewerYamlSample, "YAML")]
    [TestCase(ReviewerSqlSample, "SQL")]
    [TestCase(JsonSample, "Json")]
    [TestCase(GoSample, "Go")]
    [TestCase(RustSample, "Rust")]
    [TestCase(BashSample, "Bash")]
    [TestCase(MarkdownSample, "Markdown")]
    [TestCase(XamlSample, "XAML")]
    public void Detect_UsesOnlyCallerProvidedGrammarAvailability(string content, string expected)
    {
        var normalized = content.Replace("\r\n", "\n");
        Assert.That(RegionGrammarResolver.Detect(normalized, name => name == expected), Is.EqualTo(expected));
        Assert.That(RegionGrammarResolver.Detect(normalized, _ => false), Is.Null);
    }

    [TestCase(ReviewerTypeScriptSample)]
    [TestCase(ReviewerYamlSample)]
    [TestCase(ReviewerSqlSample)]
    [TestCase(GoSample)]
    [TestCase(RustSample)]
    [TestCase(BashSample)]
    [TestCase(MarkdownSample)]
    [TestCase(XamlSample)]
    [Apartment(ApartmentState.STA)]
    public void Resolve_BundledDetection_PreservesScanLimitAndSignaturePrecedence(string content)
    {
        var engine = new RegionHighlightingEngine();
        var normalized = content.Replace("\r\n", "\n");
        Assert.That(engine.Resolve("", new string(' ', RegionGrammarResolver.MaxScanLength) + normalized), Is.Null);
        Assert.That(engine.Resolve("", "<?php\n" + normalized), Is.EqualTo("PHP"));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Xaml_PreservesExplicitXmlPlainTextAndBuiltInFallback()
    {
        var engine = new RegionHighlightingEngine();
        Assert.Multiple(() =>
        {
            Assert.That(engine.Resolve("xml", XamlSample), Is.EqualTo("XML"));
            Assert.That(engine.Resolve("text", XamlSample), Is.Null);
            Assert.That(engine.Resolve("xaml", XmlSample), Is.EqualTo("XAML"));
            Assert.That(RegionGrammarResolver.DetectBuiltIn(XamlSample), Is.EqualTo("XML"));
            Assert.That(RegionGrammarResolver.Detect(XamlSample, name => name == "XML"), Is.EqualTo("XML"));
            Assert.That(engine.Resolve("", "Use <Window x:Class='Demo.Window' /> for a XAML view."), Is.Null);
        });
    }

    [TestCase("Notes:\n- a markdown list item\n- another item")]
    [TestCase("The server: is available.\n  Please try again.")]
    [TestCase("server:\n  some ordinary prose")]
    [TestCase("server: { host: localhost }")]
    [TestCase("TYPE Name = STRING;")]
    [TestCase(ReviewerTypeScriptSample + "\nstd::cout << x;")]
    [TestCase(ReviewerTypeScriptSample + "\nusing System;")]
    [TestCase(ReviewerYamlSample + "\nconsole.log('x');")]
    [TestCase(ReviewerSqlSample + "\nconsole.log('x');")]
    [TestCase(GoSample + "\nusing System;")]
    [TestCase(RustSample + "\nconsole.log('x');")]
    [TestCase(RustSample + "\nstd::cout << value;")]
    [TestCase(RustSample + "\n#include <vector>")]
    [TestCase("if [ -n \"$name\" ]; then\nfi\nusing System;")]
    [TestCase(MarkdownSample + "\nusing System;")]
    [TestCase("The package includes a go function and a rust finish.")]
    [TestCase("# Heading only")]
    [TestCase("- item\n- item\n- **another item**")]
    [TestCase("This is **bold** prose with a [link](https://example.org).")]
    [TestCase("# Heading\n- item")]
    [TestCase("echo hello")]
    [TestCase("$USER ${HOME}")]
    [TestCase("package main")]
    [TestCase("PACKAGE main\nFUNC main() {}")]
    [TestCase("PUB FN main() {}\nLET MUT value = 1;")]
    [TestCase("#!/bin/bashful\necho hello")]
    [TestCase("#!/usr/bin/env python\nprint('hello')")]
    [Apartment(ApartmentState.STA)]
    public void Resolve_BundledDetection_RejectsWeakOrAmbiguousMarkers(string content)
    {
        var engine = new RegionHighlightingEngine();
        Assert.That(engine.Resolve("", content.Replace("\r\n", "\n")), Is.Null);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Resolve_BashShebang_PrecedesEmbeddedLanguageMarkers()
    {
        var engine = new RegionHighlightingEngine();
        Assert.That(engine.Resolve("", "#!/bin/bash\n# using System;\necho \"$USER\""), Is.EqualTo("Bash"));
        Assert.That(RegionGrammarResolver.Detect("#!/bin/bash\nusing System;", name => name == "C#"), Is.Null);
        Assert.That(engine.Resolve("", "echo hello\n#!/bin/bash"), Is.Null,
            "a shebang is only a document signature at the start");
    }

    [TestCase(CSharpSample, "C#")]
    [TestCase(JavaSample, "Java")]
    [TestCase(CppSample, "C++")]
    [TestCase(PowerShellSample, "PowerShell")]
    [TestCase(JavaScriptSample, "JavaScript")]
    [TestCase(CssSample, "CSS")]
    [TestCase(HtmlSample, "HTML")]
    [TestCase(XmlSample, "XML")]
    [TestCase(PhpSample, "PHP")]
    [TestCase(PhpSnippetSample, "PHP")]
    [TestCase(PythonSample, "Python")]
    [TestCase(JsonSample, "Json")]
    [TestCase(TsqlSample, "TSQL")]
    [TestCase(VbSample, "VB")]
    [TestCase(PatchSample, "Patch")]
    [TestCase(TexSample, "TeX")]
    [TestCase(AspXhtmlSample, "ASP/XHTML")]
    [TestCase(XmlFragmentSample, "XML")]
    [Apartment(ApartmentState.STA)]
    public void DetectBuiltIn_ClassifiesInstalledBuiltIns(string content, string expected)
    {
        Assert.That(RegionGrammarResolver.DetectBuiltIn(content), Is.EqualTo(expected));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void DetectBuiltIn_OnlyEverReturnsInstalledGrammarNames()
    {
        var installed = HighlightingManager.Instance.HighlightingDefinitions
            .Select(definition => definition.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var content in new[]
        {
            CSharpSample, JavaSample, CppSample, PowerShellSample,
            JavaScriptSample, CssSample, HtmlSample, XmlSample, PhpSample,
            PhpSnippetSample, PythonSample, JsonSample, TsqlSample, VbSample,
            PatchSample, TexSample, AspXhtmlSample, XmlFragmentSample,
        })
        {
            var detected = RegionGrammarResolver.DetectBuiltIn(content);
            Assert.That(detected, Is.Not.Null);
            Assert.That(installed, Does.Contain(detected));
        }
    }

    [TestCase("")]
    [TestCase("   \n\t  ")]
    [TestCase("The quick brown fox jumps over the lazy dog. This is ordinary prose.")]
    [TestCase("- a markdown list item\n- another item\n# A heading")]
    [Apartment(ApartmentState.STA)]
    public void DetectBuiltIn_ReturnsNullForEmptyOrProse(string content)
    {
        Assert.That(RegionGrammarResolver.DetectBuiltIn(content), Is.Null);
    }

    [TestCase("{ this is not json }")]
    [TestCase("SELECT this option from the menu before continuing.")]
    [Apartment(ApartmentState.STA)]
    public void DetectBuiltIn_ReturnsNullForLookalikeText(string content)
    {
        Assert.That(RegionGrammarResolver.DetectBuiltIn(content), Is.Null);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void DetectBuiltIn_ReturnsNullForAmbiguousOverlap()
    {
        // Strong markers for two different tier-2 languages at once => ambiguous => null.
        var ambiguous = "std::cout << x;\nconsole.log('x');";
        Assert.That(RegionGrammarResolver.DetectBuiltIn(ambiguous), Is.Null);
    }

    // AVALONEDIT-20261014-AMD-001 (Step 5a): declaration-style C# with none of the four original
    // narrow markers must still classify as "C#" via the broadened signals, while Java / JavaScript
    // / C++ remain unaffected and ambiguous or prose content still returns null.
    private const string ReviewerInterfaceSample =
        "public interface ICalloutWindow {\n" +
        "    event EventHandler? RefreshTargetRect;\n" +
        "    event EventHandler? AngleChanged;\n" +
        "    bool ShowDiagnostics { get; set; }\n" +
        "    void UpdateTargetRect(Rect rect);\n" +
        "    void TargetMoved();\n" +
        "    void ForceRefresh();\n" +
        "    double LastDragAngle { get; }\n" +
        "}";

    private const string ReviewerEmptyClassSample =
        "public class MainWindow {\n}";

    private const string DeclarationPropertyClassSample =
        "public class Person\n" +
        "{\n" +
        "    public string Name { get; set; }\n" +
        "    public int Age { get; set; }\n" +
        "    public string Greet() => \"Hi \" + Name;\n" +
        "}";

    private const string DeclarationPropertiesOnlySample =
        "public string Name { get; init; }\n" +
        "public int Count { get; set; }\n" +
        "public bool IsEnabled { get; private set; }";

    private const string DeclarationMethodOnlySample =
        "public async Task<Result> ExecuteAsync(CancellationToken cancellationToken)\n" +
        "{\n" +
        "    return await runner.RunAsync(cancellationToken);\n" +
        "}";

    [TestCase(ReviewerInterfaceSample)]
    [TestCase(ReviewerEmptyClassSample)]
    [TestCase(DeclarationPropertyClassSample)]
    [TestCase(DeclarationPropertiesOnlySample)]
    [TestCase(DeclarationMethodOnlySample)]
    [Apartment(ApartmentState.STA)]
    public void DetectBuiltIn_ClassifiesDeclarationStyleCSharp(string content)
    {
        var normalized = content.Replace("\r\n", "\n");
        Assert.That(RegionGrammarResolver.DetectBuiltIn(normalized), Is.EqualTo("C#"));
    }

    [TestCase("import java.util.List; public class X { public static void main(String[] a){} }", "Java")]
    [TestCase("console.log(1)", "JavaScript")]
    [TestCase("#include <vector>", "C++")]
    [Apartment(ApartmentState.STA)]
    public void DetectBuiltIn_BroadenedCSharpDoesNotFalsePositiveOtherLanguages(string content, string expected)
    {
        Assert.That(RegionGrammarResolver.DetectBuiltIn(content), Is.EqualTo(expected));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void DetectBuiltIn_ScoredMarkersStillRejectCloseOverlap()
    {
        var ambiguous = "def greet(name):\n    console.log(name)";
        Assert.That(RegionGrammarResolver.DetectBuiltIn(ambiguous), Is.Null);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void DetectBuiltIn_DeclarationStyleCSharp_StaysNullForProse()
    {
        // Prose that merely mentions code-ish words must not trip the broadened C# markers.
        var prose = "The event was planned for the class reunion; everyone used the main hall.";
        Assert.That(RegionGrammarResolver.DetectBuiltIn(prose), Is.Null);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void DetectBuiltIn_IgnoresMarkersBeyondScanWindow()
    {
        var hidden = new string(' ', RegionGrammarResolver.MaxScanLength + 100) + "console.log('x');";
        Assert.That(RegionGrammarResolver.DetectBuiltIn(hidden), Is.Null);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void DetectBuiltIn_DetectsMarkerWithinScanWindowDespiteLargeTrailingInput()
    {
        var visible = "console.log('x');" + new string(' ', RegionGrammarResolver.MaxScanLength + 100);
        Assert.That(RegionGrammarResolver.DetectBuiltIn(visible), Is.EqualTo("JavaScript"));
    }

    [Test]
    public void Detect_ErrorMessageCandidate_RequiresValidDotNetFrameAndAvailability()
    {
        Assert.That(
            RegionGrammarResolver.Detect(ReviewerErrorMessageSample, name => name == ErrorMessageGrammar),
            Is.EqualTo(ErrorMessageGrammar));
        Assert.That(
            RegionGrammarResolver.Detect(ReviewerErrorMessageSample, _ => false),
            Is.Null);
    }

    [TestCase("   at System.Console.WriteLine(String value)")]
    [TestCase("\tat Example.Worker.Run(CancellationToken token)\r\n   at Example.Worker.Done()")]
    [TestCase("   at Example.Worker.<>c__DisplayClass12_0.<Run>b__0()")]
    [TestCase("   at Example.Worker.<<Run>g__Step|4>d.MoveNext()")]
    [TestCase("   at Example.Worker+<Run>d__4.MoveNext()")]
    [TestCase("   at Example.Worker.Run[T](T value)")]
    [TestCase("   at Example.Worker..ctor()")]
    [TestCase("   at Example.Worker.Run() in C:\\My Source\\MainWindow.xaml.cs:line 42")]
    [TestCase("   at Example.Worker.Run() in /src/My Project/Worker.cs:line 42")]
    public void Detect_ErrorMessageCandidate_RecognizesFrameOnlyDumps(string content)
    {
        Assert.That(RegionGrammarResolver.Detect(content, _ => true), Is.EqualTo(ErrorMessageGrammar));
        Assert.That(RegionGrammarResolver.Detect(content, name => name != ErrorMessageGrammar), Is.Null);
    }

    [TestCase("at Example.Worker.Run()")]
    [TestCase("Look at Example.Worker.Run()")]
    [TestCase("   at Run()")]
    [TestCase("   at Example.Worker.Run")]
    [TestCase("   at Example.Worker.Run();")]
    [TestCase("   at Example.Worker.Run() is helpful")]
    [TestCase("   at Example.Worker.Run() in C:\\Worker.cs:line nope")]
    [TestCase("   at Example.Worker.Run() in C:\\Worker.cs:line 42x")]
    [TestCase("   at Example.Worker.Run(\n)")]
    [TestCase("   at\nExample.Worker.Run()")]
    [TestCase("   at Example.Worker.Run() in\nC:\\Worker.cs:line 42")]
    public void Detect_ErrorMessageCandidate_RejectsMalformedFrames(string content)
    {
        Assert.That(RegionGrammarResolver.Detect(content, name => name == ErrorMessageGrammar), Is.Null);
    }

    [TestCase("using System;")]
    [TestCase("Console.WriteLine(1);")]
    [TestCase("console.log('hello');")]
    [TestCase("type Name = string;")]
    [TestCase("server:\n  host: localhost")]
    public void Detect_ErrorMessageCandidate_RejectsIndependentLanguageMarkers(string code)
    {
        Assert.That(RegionGrammarResolver.Detect("   at System.Console.WriteLine(String value)\n" + code,
            _ => true), Is.Null);
    }

    [Test]
    public void Detect_ErrorMessageCandidate_RequiresCompleteFrameWithinScanWindow()
    {
        const string frame = "   at Example.Worker.Run()";
        var prefix = new string('x', RegionGrammarResolver.MaxScanLength - frame.Length - 1) + "\n";
        Assert.That(RegionGrammarResolver.Detect(prefix + frame, _ => true), Is.EqualTo(ErrorMessageGrammar));
        Assert.That(RegionGrammarResolver.Detect("x" + prefix + frame, _ => true), Is.Null);
    }

    [TestCase("error", "Error message")]
    [TestCase("ERROR", "Error message")]
    [TestCase("text", null)]
    [TestCase("csharp", "C#")]
    [TestCase("unknown", null)]
    [Apartment(ApartmentState.STA)]
    public void Resolve_ErrorMessageCandidate_PreservesEnginePins(string token, string? expected)
    {
        var engine = new RegionHighlightingEngine();
        Assert.That(engine.Resolve(token, ReviewerErrorMessageSample), Is.EqualTo(expected));
        Assert.That(engine.Resolve("error", "Incomplete dump"), Is.EqualTo(ErrorMessageGrammar));
    }

    [TestCase("System.InvalidOperationException: failed.")]
    [TestCase("Operation: Decompose-plan\nOccurred: 2026-10-19T15:16:39.096-04:00\nException: InvalidOperationException")]
    [TestCase("Operation: Decompose-plan\nOccurred: 2026-10-19T15:16:39.096-04:00")]
    public void Detect_ErrorMessageCandidate_DoesNotTriggerFromSecondaryFieldsOrExceptionAlone(string content)
    {
        Assert.That(
            RegionGrammarResolver.Detect(content, name => name == ErrorMessageGrammar),
            Is.Null);
    }

    [Test]
    public void Detect_ErrorMessageCandidate_SuppressesFrameLineMarkerScoring()
    {
        var frameOnly = """
            System.InvalidOperationException: failed
               at SquadDash.Plan.DecomposeRunner.ExecuteAsync(System.Threading.CancellationToken cancellationToken)
               at SquadDash.Plan.DecomposeRunner.Log(System.String message)
            """;

        Assert.That(
            RegionGrammarResolver.Detect(frameOnly, name => name == "C#"),
            Is.Null,
            "C# marker words inside stack-frame lines must not score as standalone code");
        Assert.That(
            RegionGrammarResolver.Detect(frameOnly, name => name == ErrorMessageGrammar),
            Is.EqualTo(ErrorMessageGrammar));
    }

    [Test]
    public void Detect_ErrorMessageCandidate_StaysAmbiguousWithSeparateMixedCode()
    {
        var mixed = ReviewerErrorMessageSample + "\nusing System;\nnamespace Demo;\npublic class P { }";
        Assert.That(
            RegionGrammarResolver.Detect(
                mixed,
                name => name is "C#" or ErrorMessageGrammar),
            Is.Null);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void DetectBuiltIn_ErrorMessageCandidate_UsesCallerRegistryOnly()
    {
        Assert.That(RegionGrammarResolver.DetectBuiltIn(ReviewerErrorMessageSample), Is.Null);
    }

    [Test]
    public void Detect_ErrorMessageCandidate_YieldsToTier1Signatures()
    {
        var withPhpSignature = "<?php\n" + ReviewerErrorMessageSample;
        Assert.That(
            RegionGrammarResolver.Detect(
                withPhpSignature,
                name => name is "PHP" or ErrorMessageGrammar),
            Is.EqualTo("PHP"));
    }

    [Test]
    public void Detect_ErrorMessageCandidate_RespectsScanWindow()
    {
        var beyond = new string(' ', RegionGrammarResolver.MaxScanLength + 10) + ReviewerErrorMessageSample;
        var inside = ReviewerErrorMessageSample + new string(' ', RegionGrammarResolver.MaxScanLength + 10);

        Assert.That(
            RegionGrammarResolver.Detect(beyond, name => name == ErrorMessageGrammar),
            Is.Null);
        Assert.That(
            RegionGrammarResolver.Detect(inside, name => name == ErrorMessageGrammar),
            Is.EqualTo(ErrorMessageGrammar));
    }

    [Test]
    public void Resolve_ErrorMessageContent_StillHonorsPlainTextAndExplicitPins()
    {
        Func<string, string?> alias = token => token == "csharp" ? "C#" : null;
        Func<string, string?> detector = _ => ErrorMessageGrammar;

        Assert.That(RegionGrammarResolver.Resolve("text", ReviewerErrorMessageSample, alias, detector), Is.Null);
        Assert.That(RegionGrammarResolver.Resolve("csharp", ReviewerErrorMessageSample, alias, detector), Is.EqualTo("C#"));
    }

    private static string? ThrowingDetector(string content) =>
        throw new InvalidOperationException("heuristic must not run outside auto-detect mode");
}
