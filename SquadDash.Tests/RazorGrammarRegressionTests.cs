using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace SquadDash.Tests;

public sealed partial class RegionHighlightingTests
{
    private const string FollowingMarkup = "\n<p>Following content</p>\n<OtherComponent />\nelse catch finally tail";

    private static readonly RazorToken[] FollowingMarkupTokens =
    [
        new("<p>", "Tag"), new("Following content", null), new("</p>", "Tag"),
        new("<OtherComponent", "Tag"), new("/>", "Tag"), new("else catch finally tail", null),
    ];

    private static RazorSample WithFollowingMarkup(string name, string content, params RazorToken[] tokens) =>
        new(name, content + FollowingMarkup, tokens.Concat(FollowingMarkupTokens).ToArray());

    private static IEnumerable<RazorSample> RazorFinalSamples()
    {
        foreach (var word in new[] { "else", "catch", "finally" })
        {
            // Includes the exact original catch-prose repro, with a component after the closing tag.
            yield return new($"Literal_{word}",
                $"<p>\n{word} the latest updates here.\n</p>\n<OtherComponent />",
                new("<p>", "Tag"), new($"{word} the latest updates here.", null),
                new("</p>", "Tag"), new("<OtherComponent", "Tag"), new("/>", "Tag"));
            yield return WithFollowingMarkup($"TerminatedChain_{word}",
                $"@if (ready) {{ <b>yes</b> }}\n<hr>\n{word} the latest updates here.",
                new("@if", "Keyword"), new("<hr>", "Tag"), new($"{word} the latest updates here.", null));
        }

        foreach (var identifier in new[] { "@code", "@functions", "@if" })
        foreach (var header in new[] { "@{", "@code {", "@functions {" })
        {
            // Put the identifier on its own line so assertions cannot accidentally find the directive header.
            yield return WithFollowingMarkup($"EscapedIdentifier_{header}_{identifier}",
                $"{header}\n var {identifier} = 1;\n}}",
                new(identifier, null, header.StartsWith(identifier) ? 1 : 0), new("1", "Number"));
        }

        foreach (var quote in new[] { '"', '\'' })
        {
            yield return WithFollowingMarkup($"DirectiveAttribute_{quote}",
                $"<button @onclick={quote}() =>\n Save(){quote}>\n Save\n</button>",
                new("@onclick", "Keyword"), new(quote.ToString(), "String", 1), new(">\n", "Tag"),
                new("\n Save\n", null), new("</button>", "Tag"));
            yield return WithFollowingMarkup($"OrdinaryAttribute_{quote}",
                $"<button title={quote}first\n second{quote} data-tail={quote}tail{quote}>\n body\n</button>",
                new("title", "Attribute"), new($"first\n second{quote}", "String"),
                new("data-tail", "Attribute"), new($"{quote}tail{quote}", "String"),
                new("\n body\n", null), new("</button>", "Tag"));
        }

        foreach (var (name, gap) in new[]
        {
            ("Whitespace", " \t"),
            ("Newlines", "\n\n"),
            ("LineComment", "\n// chain gap\n"),
            ("BlockComment", "\n/* chain\n gap */\n"),
            ("RazorComment", "\n@* chain\n gap *@\n"),
        })
        {
            yield return WithFollowingMarkup($"IfElse_{name}",
                $"@if (ready) {{ <b>yes</b> }}{gap}else if (other) {{ <i>maybe</i> }}{gap}else {{ <u>no</u> return; }}",
                new("@if", "Keyword"), new("else", "Keyword"), new("else", "Keyword", 1),
                new("if", "Keyword", 1),
                new("<i>", "Tag"), new("maybe", null), new("<u>", "Tag"), new("no", null),
                new("return", "Keyword"));
            yield return WithFollowingMarkup($"TryCatchFinally_{name}",
                $"@try {{ <b>work</b> }}{gap}catch (Exception error) {{ <i>recover</i> }}{gap}finally {{ <u>cleanup</u> return; }}",
                new("@try", "Keyword"), new("catch", "Keyword"), new("finally", "Keyword"),
                new("<i>", "Tag"), new("recover", null), new("<u>", "Tag"), new("cleanup", null),
                new("return", "Keyword"));
            yield return WithFollowingMarkup($"DoWhile_{name}",
                $"@do {{ <b>repeat</b> }}{gap}while (ready);",
                new("@do", "Keyword"), new("<b>", "Tag"), new("repeat", null), new("while", "Keyword"));
        }
    }

    private static IEnumerable<TestCaseData> RazorFinalRegressionCases()
    {
        foreach (var sample in RazorFinalSamples().Concat(RazorChainSamples()))
        foreach (var dark in new[] { false, true })
            yield return new TestCaseData(sample.Content, sample.Tokens, dark)
                .SetName($"Razor_Final_{sample.Name}_{(dark ? "Dark" : "Light")}");
    }

    private const string TerminalWhileRepro = "@if (ready) { } else { }\nwhile waiting\n<p>after</p>";

    private static RazorSample WithChainTail(string name, string content, params RazorToken[] tokens) =>
        new(name, content + "\n<p>after</p>\n<OtherComponent />\nelse catch finally while tail",
            tokens.Concat(new RazorToken[]
            {
                new("<p>", "Tag"), new("after", null), new("</p>", "Tag"),
                new("<OtherComponent", "Tag"), new("/>", "Tag"),
                new("else catch finally while tail", null),
            }).ToArray());

    private static IEnumerable<RazorSample> RazorChainSamples()
    {
        yield return new("TerminalWhile_ExactRepro", TerminalWhileRepro,
            new("@if", "Keyword"), new("else", "Keyword"), new("while waiting", null),
            new("<p>", "Tag"), new("after", null), new("</p>", "Tag"));

        foreach (var (family, prefix, words) in new[]
        {
            ("If", "@if (ready) { }", new[] { "catch", "finally", "while" }),
            ("ElseIf", "@if (ready) { } else if (other) { }", new[] { "catch", "finally", "while" }),
            ("Try", "@try { }", new[] { "else", "while" }),
            ("Catch", "@try { } catch (Exception error) { }", new[] { "else", "while" }),
            ("Do", "@do { }", new[] { "else", "catch", "finally" }),
        })
        foreach (var word in words)
        foreach (var (gapName, gap) in new[] { ("Adjacent", ""), ("Newline", "\n") })
        {
            yield return WithChainTail($"CrossFamily_{family}_{word}_{gapName}",
                $"{prefix}{gap}{word} waiting",
                new(prefix.Split(' ')[0], "Keyword"), new($"{word} waiting", null));
            yield return WithChainTail($"CrossFamilyBlock_{family}_{word}_{gapName}",
                $"{prefix}{gap}{word} (late) {{ return; }}",
                new RazorToken($"{word} (late) {{ return; }}", null));
        }

        foreach (var (name, prefix, terminal) in new[]
        {
            ("Else", "@if (ready) { } else { }", "else"),
            ("Finally", "@try { } catch { } finally { }", "finally"),
            ("DoWhile", "@do { } while (ready);", "while"),
        })
        {
            foreach (var word in new[] { "else", "catch", "finally", "while", "if" })
            foreach (var (gapName, gap) in new[] { ("Adjacent", ""), ("Newline", "\n") })
                yield return WithChainTail($"TerminalProse_{name}_{word}_{gapName}",
                    $"{prefix}{gap}{word} waiting",
                    new(terminal, "Keyword"), new($"{word} waiting", null));

            foreach (var (edgeName, suffix, token, role) in new (string, string, string, string?)[]
            {
                ("BlockComment", "/* outside */else { return; }", "/* outside */", null),
                ("LineComment", "// outside\nelse { return; }", "// outside", null),
                ("RazorComment", "@* outside *@else { return; }", "@* outside *@", "Comment"),
                ("BareBlock", "{ return; }", "{ return; }", null),
                ("BareIf", "if (late) { return; }", "if (late)", null),
            })
                yield return WithChainTail($"TerminalEdge_{name}_{edgeName}", prefix + suffix,
                    new(terminal, "Keyword"), new(token, role), new("return", null));
        }

        foreach (var (gapName, gap, trivia, role) in new (string, string, string, string?)[]
        {
            ("Adjacent", "", "", null),
            ("Space", " ", "", null),
            ("Newline", "\n", "", null),
            ("BlockComment", "/* gap */", "/* gap */", "Comment"),
            ("LineComment", "// gap\n", "// gap", "Comment"),
            ("RazorComment", "@* gap *@", "@* gap *@", "Comment"),
        })
        {
            var triviaTokens = trivia.Length == 0 ? System.Array.Empty<RazorToken>()
                : new[] { new RazorToken(trivia, role) };
            var elseIfGap = gap.Length == 0 ? " " : gap;
            yield return WithChainTail($"MultiElseIf_{gapName}",
                $"@if (ready) {{ }}{gap}else{elseIfGap}if (second) {{ }}{gap}else{elseIfGap}if (third) {{ }}{gap}else{gap}{{ <b>fallback</b> return; }}",
                new RazorToken[]
                {
                    new("@if", "Keyword"), new("else", "Keyword"), new("else", "Keyword", 1),
                    new("else", "Keyword", 2), new("if", "Keyword", 1), new("if", "Keyword", 2),
                    new("<b>", "Tag"), new("fallback", null), new("return", "Keyword"),
                }.Concat(triviaTokens).ToArray());
            yield return WithChainTail($"MultiCatchFinally_{gapName}",
                $"@try {{ }}{gap}catch (First error) when (ready) {{ }}{gap}catch (Second error) {{ }}{gap}catch {{ }}{gap}finally{gap}{{ <b>cleanup</b> return; }}",
                new RazorToken[]
                {
                    new("@try", "Keyword"), new("catch", "Keyword"), new("catch", "Keyword", 1),
                    new("catch", "Keyword", 2), new("finally", "Keyword"),
                    new("<b>", "Tag"), new("cleanup", null), new("return", "Keyword"),
                }.Concat(triviaTokens).ToArray());
            yield return WithChainTail($"TerminalDoWhile_{gapName}",
                $"@do {{ <b>repeat</b> return; }}{gap}while (Check(new[] {{ 1, 2 }}));",
                new RazorToken[]
                {
                    new("@do", "Keyword"), new("while", "Keyword"), new("new", "Type"),
                    new("1", "Number"), new("<b>", "Tag"), new("repeat", null), new("return", "Keyword"),
                }.Concat(triviaTokens).ToArray());
        }

        foreach (var (name, prefix) in new[]
        {
            ("If", "@if (ready) { }"),
            ("ElseIf", "@if (ready) { } else if (other) { }"),
            ("Catch", "@try { } catch { }"),
        })
        {
            yield return WithChainTail($"AdjacentBareBlock_{name}", prefix + "{ return; }",
                new RazorToken("{ return; }", null));
            yield return WithChainTail($"AdjacentBareIf_{name}", prefix + "if (late) { return; }",
                new RazorToken("if (late) { return; }", null));
        }

        foreach (var (name, header, keyword) in new[]
        {
            ("If", "@if (ready)", "@if"),
            ("ElseIf", "@if (ready) { } else if (other)", "else"),
            ("Else", "@if (ready) { } else", "else"),
            ("Try", "@try", "@try"),
            ("Catch", "@try { } catch (Exception error)", "catch"),
            ("Finally", "@try { } finally", "finally"),
            ("Do", "@do", "@do"),
            ("While", "@do { } while waiting", "while"),
        })
        foreach (var (gapName, gap) in new[] { ("Adjacent", ""), ("Newline", "\n") })
            yield return WithChainTail($"MalformedHeader_{name}_{gapName}",
                header + gap + "<i>recovered</i>",
                new(keyword, "Keyword"), new("<i>", "Tag"), new("recovered", null), new("</i>", "Tag"));

        foreach (var (name, prefix, word) in new[]
        {
            ("ElseIdentifier", "@if (ready) { }", "elsewhere"),
            ("ElseIfIdentifier", "@if (ready) { }", "elseif"),
            ("CatchIdentifier", "@try { }", "catching"),
            ("FinallyIdentifier", "@try { }", "finallyDone"),
            ("WhileIdentifier", "@do { }", "whileWaiting"),
        })
            yield return WithChainTail(name, prefix + word, new RazorToken(word, null));
    }

    [TestCaseSource(nameof(RazorFinalRegressionCases))]
    public void Razor_FinalRegression_PreservesRolesAndFollowingMarkup(
        string content, RazorToken[] tokens, bool dark)
    {
        var engine = new RegionHighlightingEngine();
        var definition = engine.CreateDefinition("Razor", PaletteFrom(engine, dark));
        var sections = HighlightAll(new TextDocument(content), definition);
        foreach (var token in tokens)
            AssertRazorToken(content, token, sections, engine, dark);
        if (content.Contains("chain"))
            AssertRazorToken(content, new("chain", "Comment"), sections, engine, dark);
    }

    private static IEnumerable<TestCaseData> RazorFinalEditCases()
    {
        foreach (var sample in RazorChainSamples())
        foreach (var dark in new[] { false, true })
            yield return new TestCaseData(sample.Content[1..], 0, 0, "@", sample.Content, sample.Tokens, dark)
                .SetName($"Razor_FinalEdit_{sample.Name}_{(dark ? "Dark" : "Light")}");

        foreach (var (name, prefix, terminal, continuation) in new[]
        {
            ("Else", "@if (ready) { }", " else { }", "else { return; }"),
            ("Finally", "@try { } catch { }", " finally { }", "catch { return; }"),
            ("DoWhile", "@do { }", " while (ready);", "while (other);"),
        })
        foreach (var dark in new[] { false, true })
        {
            var before = prefix + "\n" + continuation + "\n<p>after</p>";
            var after = before.Insert(prefix.Length, terminal);
            var tokens = new RazorToken[]
            {
                new(terminal.Trim().Split(' ')[0], "Keyword"), new(continuation, null),
                new("<p>", "Tag"), new("after", null), new("</p>", "Tag"),
            };
            yield return new TestCaseData(before, prefix.Length, 0, terminal, after, tokens, dark)
                .SetName($"Razor_FinalEdit_InsertTerminalBeforeContinuation_{name}_{(dark ? "Dark" : "Light")}");
        }

        foreach (var sample in RazorFinalSamples())
        {
            string oldText;
            string newText;
            if (sample.Name.StartsWith("Literal_"))
            {
                oldText = sample.Content.Split('\n')[1].Split(' ')[0];
                newText = "ordinary";
            }
            else if (sample.Name.StartsWith("EscapedIdentifier_"))
            {
                oldText = sample.Content.Split('\n')[1].Split(' ')[2];
                newText = "@value";
            }
            else if (sample.Name.StartsWith("DirectiveAttribute_") || sample.Name.StartsWith("OrdinaryAttribute_"))
            {
                oldText = "\n ";
                newText = " ";
            }
            else
            {
                continue;
            }

            // Start from the non-regressing form; apply and undo the edit on the SAME warmed highlighter.
            var offset = sample.Content.IndexOf(oldText, System.StringComparison.Ordinal);
            if (sample.Name.StartsWith("EscapedIdentifier_"))
                offset = sample.Content.IndexOf(oldText, sample.Content.IndexOf('\n'), System.StringComparison.Ordinal);
            var before = sample.Content.Remove(offset, oldText.Length).Insert(offset, newText);
            foreach (var dark in new[] { false, true })
                yield return new TestCaseData(before, offset, newText.Length, oldText, sample.Content, sample.Tokens, dark)
                    .SetName($"Razor_FinalEdit_{sample.Name}_{(dark ? "Dark" : "Light")}");
        }

        foreach (var sample in RazorFinalSamples().Where(s =>
            s.Name is "IfElse_BlockComment" or "TryCatchFinally_BlockComment" or "DoWhile_BlockComment"))
        {
            const string gap = "\n/* chain\n gap */\n";
            var offset = sample.Content.IndexOf(gap, System.StringComparison.Ordinal);
            var before = sample.Content.Remove(offset, gap.Length).Insert(offset, "\n");
            foreach (var dark in new[] { false, true })
                yield return new TestCaseData(before, offset, 1, gap, sample.Content, sample.Tokens, dark)
                    .SetName($"Razor_FinalEdit_{sample.Name}_{(dark ? "Dark" : "Light")}");
        }
    }

    [TestCaseSource(nameof(RazorFinalEditCases))]
    public void Razor_FinalRegression_IncrementalEditAndUndoMatchFreshRoles(
        string before, int offset, int length, string replacement, string after, RazorToken[] tokens, bool dark)
    {
        var engine = new RegionHighlightingEngine();
        var definition = engine.CreateDefinition("Razor", PaletteFrom(engine, dark));
        var document = new TextDocument(before);
        using var highlighter = new DocumentHighlighter(document, definition);
        var initial = SnapshotSections(HighlightExisting(document, highlighter));
        document.Replace(offset, length, replacement);
        Assert.That(document.Text, Is.EqualTo(after));
        // Asking for the tail first exercises invalidation/forward scanning, not just sequential recoloring.
        highlighter.HighlightLine(document.LineCount);
        var incremental = HighlightExisting(document, highlighter);
        foreach (var token in tokens)
            AssertRazorToken(after, token, incremental, engine, dark);
        if (after.Contains("chain"))
            AssertRazorToken(after, new("chain", "Comment"), incremental, engine, dark);
        Assert.That(SnapshotSections(incremental),
            Is.EqualTo(SnapshotSections(HighlightAll(document, definition))));
        document.Replace(offset, replacement.Length, before.Substring(offset, length));
        highlighter.HighlightLine(document.LineCount);
        Assert.That(document.Text, Is.EqualTo(before));
        Assert.That(SnapshotSections(HighlightExisting(document, highlighter)), Is.EqualTo(initial));
    }

    private static IEnumerable<TestCaseData> RazorOriginalDefectMutations()
    {
        foreach (var sample in RazorFinalSamples().Where(s =>
            s.Name.StartsWith("Literal_") || s.Name.StartsWith("EscapedIdentifier_@{_") ||
            s.Name.StartsWith("DirectiveAttribute_") || s.Name.StartsWith("OrdinaryAttribute_")))
        foreach (var dark in new[] { false, true })
            yield return new TestCaseData(sample.Name, sample.Content, sample.Tokens, dark)
                .SetName($"Razor_OriginalDefectRejected_{sample.Name}_{(dark ? "Dark" : "Light")}");
    }

    [TestCaseSource(nameof(RazorOriginalDefectMutations))]
    public void Razor_RegressionAssertions_RejectOriginalDefectInMemory(
        string name, string content, RazorToken[] tokens, bool dark)
    {
        using var stream = typeof(RegionHighlightingEngine).Assembly
            .GetManifestResourceStream("SquadDash.Grammars.Razor.xshd");
        Assert.That(stream, Is.Not.Null);
        var xml = XDocument.Load(stream!);
        var ns = xml.Root!.Name.Namespace;
        XElement Rules(string ruleSet) =>
            xml.Root.Elements(ns + "RuleSet").Single(e => (string?)e.Attribute("name") == ruleSet);

        // Recreate each faulty rule in memory only; never replace Lyra's resource or the shared registry.
        if (name.StartsWith("Literal_"))
        {
            var header = Rules("Transitions").Elements(ns + "Span")
                .Single(e => (string?)e.Attribute("ruleSet") == "MixedHeader");
            header.Element(ns + "Begin")!.Value += @"|(?<!\w)(?:else(?:[ \t]+if)?|catch|finally)\b";
        }
        else if (name.StartsWith("EscapedIdentifier_"))
        {
            Rules("MixedCode").Elements(ns + "Import")
                .Single(e => (string?)e.Attribute("ruleSet") == "CodeTransitions")
                .SetAttributeValue("ruleSet", "Transitions");
        }
        else
        {
            var quote = name[^1].ToString();
            var spans = Rules("TagAttributes").Elements(ns + "Span");
            var attribute = name.StartsWith("DirectiveAttribute_")
                ? spans.Single(e => (string?)e.Attribute("ruleSet") == "DirectiveAttributeValue" &&
                    e.Element(ns + "Begin")!.Value.EndsWith(quote))
                : spans.Single(e => (string?)e.Attribute("ruleSet") == "AttributeText" &&
                    (string?)e.Attribute("begin") == quote);
            attribute.SetAttributeValue("multiline", "false");
        }

        using var reader = xml.CreateReader();
        var engine = new RegionHighlightingEngine();
        var definition = new RegionHighlightingDefinition(
            HighlightingLoader.Load(reader, HighlightingManager.Instance), PaletteFrom(engine, dark));
        var sections = HighlightAll(new TextDocument(content), definition);
        Assert.Throws<AssertionException>(() =>
        {
            foreach (var token in tokens)
                AssertRazorToken(content, token, sections, engine, dark);
        }, "The regression must fail against the reconstructed original defect, not merely find colored spans.");

        RazorToken boundary = name.StartsWith("Literal_") ? new("</p>", "Tag")
            : name.StartsWith("EscapedIdentifier_") ? new("else catch finally tail", null)
            : name.StartsWith("DirectiveAttribute_") ? new("\n Save\n", null)
            : new("\n body\n", null);
        if (name.EndsWith("_@if"))
        {
            // The old @if header recovers at ';'; its identifier role (asserted above) is the defect.
            AssertRazorToken(content, boundary, sections, engine, dark);
            return;
        }
        Assert.Throws<AssertionException>(() =>
            AssertRazorToken(content, boundary, sections, engine, dark),
            "The boundary assertion itself must catch leaked code/string/tag state.");
    }

    private static List<HighlightedSection> HighlightExisting(TextDocument document, DocumentHighlighter highlighter)
    {
        var sections = new List<HighlightedSection>();
        for (var line = 1; line <= document.LineCount; line++)
            sections.AddRange(highlighter.HighlightLine(line).Sections);
        return sections;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Razor_TerminalWhileAssertions_RejectUniversalChainInMemory(bool dark)
    {
        using var stream = typeof(RegionHighlightingEngine).Assembly
            .GetManifestResourceStream("SquadDash.Grammars.Razor.xshd");
        Assert.That(stream, Is.Not.Null);
        var xml = XDocument.Load(stream!);
        var ns = xml.Root!.Name.Namespace;
        var transitions = xml.Root.Elements(ns + "RuleSet")
            .Single(e => (string?)e.Attribute("name") == "Transitions");
        var familySpans = transitions.Elements(ns + "Span")
            .Where(e => (string?)e.Attribute("ruleSet") is "IfChain" or "TryChain" or "DoChain").ToArray();
        Assert.That(familySpans, Has.Length.EqualTo(3));
        familySpans[0].AddBeforeSelf(new XElement(ns + "Span",
            new XAttribute("ruleSet", "UniversalChain"), new XAttribute("multiline", "true"),
            new XElement(ns + "Begin", @"(?=(?<!\w)@(?:if|try|do)\b)"),
            new XElement(ns + "End", @"(?=\S)")));
        foreach (var span in familySpans)
            span.Remove();
        // Recreate the defect: every body, even terminal else, admits every continuation family.
        xml.Root.Add(new XElement(ns + "RuleSet", new XAttribute("name", "UniversalChain"),
            new XElement(ns + "Span", new XAttribute("ruleSet", "MixedHeader"),
                new XAttribute("multiline", "true"),
                new XElement(ns + "Begin", new XAttribute("color", "Keyword"),
                    @"(?<!\w)(?:@(?:if|try|do)|else(?:[ \t]+if)?|catch|finally|while)\b"),
                new XElement(ns + "End", @"\}|;")),
            new XElement(ns + "Import", new XAttribute("ruleSet", "ChainTrivia"))));
        using var reader = xml.CreateReader();
        var engine = new RegionHighlightingEngine();
        var definition = new RegionHighlightingDefinition(
            HighlightingLoader.Load(reader, HighlightingManager.Instance), PaletteFrom(engine, dark));
        var sections = HighlightAll(new TextDocument(TerminalWhileRepro), definition);
        Assert.Throws<AssertionException>(() =>
            AssertRazorToken(TerminalWhileRepro, new("while waiting", null), sections, engine, dark));
        Assert.Throws<AssertionException>(() =>
            AssertRazorToken(TerminalWhileRepro, new("<p>", "Tag"), sections, engine, dark),
            "The exact markup boundary must reject the universal chain independently of the keyword check.");
    }
}
