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

    private const string JavaScriptSample =
        "const greet = (name) => {\n  console.log('hi ' + name);\n};\nmodule.exports = greet;";

    private const string CssSample =
        "body {\n  color: #fff;\n  margin: 0;\n}\n@media (max-width: 600px) { .a { display: none; } }";

    private const string HtmlSample =
        "<!DOCTYPE html>\n<html>\n<head><title>x</title></head>\n<body><h1>Hi</h1></body>\n</html>";

    private const string XmlSample =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root><child>x</child></root>";

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

    private const string JsonSample =
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
            "adding explicit SQL must not steal existing TSQL auto-detection");
    }

    private const string TsqlSample =
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

    private static string? ThrowingDetector(string content) =>
        throw new InvalidOperationException("heuristic must not run outside auto-detect mode");
}
