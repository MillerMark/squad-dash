using System.Linq;
using System.Windows.Media;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PromptTextTokenParserTests {
    [Test]
    public void Parse_QuotedPlaceholder_ExactUserExampleIncludesBothBraces() {
        const string text = "\"Say hello to {username}\".";
        Assert.That(PromptTextTokenParser.Parse(text), Is.EqualTo(new[] {
            new PromptTextToken(1, 23, PromptTextTokenKind.QuotedContent),
            new PromptTextToken(14, 10, PromptTextTokenKind.QuotedPlaceholder),
        }));
    }

    [TestCase("\"", "\"")]
    [TestCase("\u201C", "\u201D")]
    public void Parse_QuotedPlaceholders_AcceptUnicodeAndPunctuationWithoutSubstitution(string open, string close) {
        var contents = new[] { "{username}", "{user.name!?:/@-$%&+=,;()[]}", "{\u7528\u6237}", "{e\u0301}", "{\U0001F600}", "{a'b\u2019c}" };
        var text = "\U0001F600 prefix " + open + string.Concat(contents) + close + " {outside}";
        var original = text;
        var tokens = PromptTextTokenParser.Parse(text);
        var placeholders = tokens.Where(t => t.Kind == PromptTextTokenKind.QuotedPlaceholder).ToArray();
        Assert.Multiple(() => {
            Assert.That(placeholders.Select(t => text.Substring(t.Start, t.Length)), Is.EqualTo(contents));
            Assert.That(placeholders.Select(t => t.Start), Is.EqualTo(contents.Select(c => text.IndexOf(c, System.StringComparison.Ordinal))));
            Assert.That(placeholders.Select(t => t.Length), Is.EqualTo(contents.Select(c => c.Length)));
            Assert.That(tokens, Is.Ordered.By(nameof(PromptTextToken.Start)));
            Assert.That(text, Is.EqualTo(original));
        });
    }

    [TestCase("{username}")]
    [TestCase("'hello {username}'")]
    [TestCase("\"{username}")]
    [TestCase("\u201C{username}\"")]
    [TestCase("\"{}\"")]
    [TestCase("\"{ }\"")]
    [TestCase("\"{user name}\"")]
    [TestCase("\"{user\tname}\"")]
    [TestCase("\"{user\nname}\"")]
    [TestCase("\"{user\rname}\"")]
    [TestCase("\"{user\r\nname}\"")]
    [TestCase("\"{user\u00A0name}\"")]
    [TestCase("\"{user\u2003name}\"")]
    [TestCase("\"{user\u2028name}\"")]
    [TestCase("\"{user\u2029name}\"")]
    [TestCase("\"{user\u3000name}\"")]
    [TestCase("\"{username\"")]
    [TestCase("\"username}\"")]
    [TestCase("\"{{username}}\"")]
    [TestCase("\"{{username}\"")]
    [TestCase("\"{username}}\"")]
    [TestCase("\"{outer{inner}tail}\"")]
    [TestCase("\"{outer{inner}\"")]
    [TestCase("\"\\{username}\"")]
    [TestCase("\"{username\\}\"")]
    [TestCase("\"\\{{inner}}\"")]
    [TestCase("\"{outer\\{inner}tail}\"")]
    [TestCase("\"{outer\\{inner\\}tail}\"")]
    [TestCase("\"\\{outer{inner}tail}\"")]
    [TestCase("\"{user\" \"name}\"")]
    [TestCase("\"{user\\\"name}\"")]
    [TestCase("\"{user\u201Cname}\"")]
    [TestCase("\u201C{user\"name}\u201D")]
    [TestCase("`\"{username}\"`")]
    [TestCase("``\u201C{username}\u201D``")]
    [TestCase("```text\n\"{username}\"\n```")]
    [TestCase("`unfinished \"{username}\"")]
    public void Parse_InvalidQuotedPlaceholders_RemainLiteral(string text) {
        Assert.That(PromptTextTokenParser.Parse(text).Where(t => t.Kind == PromptTextTokenKind.QuotedPlaceholder), Is.Empty);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(32)]
    [TestCase(33)]
    public void Parse_QuotedPlaceholderBraceEscapes_UseIndependentBackslashParity(int backslashes) {
        var slashes = new string('\\', backslashes);
        foreach (var text in new[] { "\"" + slashes + "{name}\"", "\"{name" + slashes + "}\"" }) {
            var placeholders = PromptTextTokenParser.Parse(text).Where(t => t.Kind == PromptTextTokenKind.QuotedPlaceholder).ToArray();
            Assert.That(placeholders, Has.Length.EqualTo(backslashes % 2 == 0 ? 1 : 0), text);
            if (backslashes % 2 == 0)
                Assert.That(placeholders.Single(), Is.EqualTo(new PromptTextToken(
                    text.IndexOf('{'), text.LastIndexOf('}') - text.IndexOf('{') + 1, PromptTextTokenKind.QuotedPlaceholder)));
        }
    }

    [TestCase("\"{first}{}{{bad}}{last}{adjacent}\"", "{first}", "{last}", "{adjacent}")]
    [TestCase("\"\\{{bad}}{last}\"", "{last}")]
    [TestCase("\"{bad\\}}{last}\"", "{last}")]
    [TestCase("\"{bad space}{last}\"", "{last}")]
    [TestCase("\"{unfinished\n\"{next}\"", "{next}")]
    [TestCase("\"{cross\" \"quote}{last}\"", "{last}")]
    [TestCase("\"{first}\" {outside} \u201C{last}\u201D", "{first}", "{last}")]
    public void Parse_QuotedPlaceholders_RecoverOnlyAfterMalformedGroupOrQuoteBoundary(string text, params string[] expected) {
        Assert.That(PromptTextTokenParser.Parse(text).Where(t => t.Kind == PromptTextTokenKind.QuotedPlaceholder)
            .Select(t => text.Substring(t.Start, t.Length)), Is.EqualTo(expected));
    }

    [TestCase("\"{before}`{code}`{after}\"", "{before}", "{code}", "{after}")]
    [TestCase("\"{before}``{code}`tick``{after}\"", "{before}", "{code}", "{after}")]
    [TestCase("\"{left`code`right}\"", "{left`code`right}")]
    public void Parse_QuotedPlaceholdersAroundCode_KeepOverlapsForPainterPrecedence(string text, params string[] expected) {
        var tokens = PromptTextTokenParser.Parse(text);
        Assert.Multiple(() => {
            Assert.That(tokens.Count(t => t.Kind == PromptTextTokenKind.InlineCodeContent), Is.EqualTo(1));
            Assert.That(tokens.Single(t => t.Kind == PromptTextTokenKind.QuotedContent),
                Is.EqualTo(new PromptTextToken(1, text.Length - 2, PromptTextTokenKind.QuotedContent)));
            Assert.That(tokens.Where(t => t.Kind == PromptTextTokenKind.QuotedPlaceholder)
                .Select(t => text.Substring(t.Start, t.Length)), Is.EqualTo(expected));
        });
    }

    [Test]
    public void Parse_QuotedPlaceholderBoldAndHex_PreserveAllOverlappingKinds() {
        const string text = "\"**{#abc}**\"";
        Assert.That(PromptTextTokenParser.Parse(text), Is.EqualTo(new[] {
            new PromptTextToken(1, 10, PromptTextTokenKind.QuotedContent),
            new PromptTextToken(3, 6, PromptTextTokenKind.BoldContent),
            new PromptTextToken(3, 6, PromptTextTokenKind.QuotedPlaceholder),
            new PromptTextToken(4, 4, PromptTextTokenKind.HexColor, Color.FromRgb(0xAA, 0xBB, 0xCC)),
        }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Parse_LargeMalformedQuotedBraces_CompletesWithinLinearScanRegressionBudget(bool nested) {
        var malformed = nested
            ? new string('{', 550000) + "name" + new string('}', 550000)
            : string.Concat(Enumerable.Repeat("\\{{inner}}{space name}{empty\\}", 40000));
        var text = "\"" + malformed + "{tail}\"";
        _ = PromptTextTokenParser.Parse("\"{warmup}\"");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var tokens = PromptTextTokenParser.Parse(text);
        watch.Stop();
        Assert.Multiple(() => {
            Assert.That(tokens.Where(t => t.Kind == PromptTextTokenKind.QuotedPlaceholder),
                Is.EqualTo(new[] { new PromptTextToken(text.Length - 7, 6, PromptTextTokenKind.QuotedPlaceholder) }));
            Assert.That(watch.Elapsed, Is.LessThan(System.TimeSpan.FromSeconds(5)),
                "Million-character malformed brace groups must not trigger rescanning or recursive nesting.");
        });
        TestContext.Out.WriteLine($"Parsed {text.Length:N0} malformed brace characters in {watch.Elapsed.TotalMilliseconds:F1} ms.");
    }

    [TestCase("before \"quoted prose\" after", "quoted prose")]
    [TestCase("before \u201Ccurly prose\u201D after", "curly prose")]
    [TestCase("\" don't 'change' apostrophes \u2019 \"", " don't 'change' apostrophes \u2019 ")]
    [TestCase("\"outer \u201Cinner\u201D tail\"", "outer \u201Cinner\u201D tail")]
    [TestCase("\u201Couter \"inner\" tail\u201D", "outer \"inner\" tail")]
    [TestCase("\"say \\\"hello\\\" now\"", "say \\\"hello\\\" now")]
    [TestCase("\u201Cescaped \\\u201D inside\u201D", "escaped \\\u201D inside")]
    public void Parse_QuotedContent_ExcludesOnlyMatchedOuterDelimiters(string text, string content) {
        var token = PromptTextTokenParser.Parse(text).Single(t => t.Kind == PromptTextTokenKind.QuotedContent);
        Assert.Multiple(() => {
            Assert.That(token.Start, Is.EqualTo(text.IndexOf(content, System.StringComparison.Ordinal)));
            Assert.That(token.Length, Is.EqualTo(content.Length));
            Assert.That(text.Substring(token.Start, token.Length), Is.EqualTo(content));
        });
    }

    [TestCase("\"unfinished")]
    [TestCase("\u201Cunfinished")]
    [TestCase("\"mismatched\u201D")]
    [TestCase("\u201Cmismatched\"")]
    [TestCase("\u201Dreversed\u201C")]
    [TestCase("\"\"")]
    [TestCase("\u201C \t\u00A0\u201D")]
    [TestCase("'single' don't users' \u2018curly single\u2019")]
    [TestCase("\\\"escaped\\\"")]
    [TestCase("\"first\nsecond\"")]
    [TestCase("\"first\rsecond\"")]
    [TestCase("\u201Cfirst\r\nsecond\u201D")]
    [TestCase("`\"inside code\"`")]
    [TestCase("``\u201Cinside ` code\u201D``")]
    [TestCase("```csharp\n\"inside fence\"\n```")]
    [TestCase("`unfinished \"still code\"")]
    public void Parse_InvalidOrCodeQuotes_EmitNoQuotedContent(string text) {
        Assert.That(PromptTextTokenParser.Parse(text).Where(t => t.Kind == PromptTextTokenKind.QuotedContent), Is.Empty);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(32)]
    [TestCase(33)]
    public void Parse_QuoteEscapes_UseBackslashParityAtBothDelimiters(int backslashes) {
        var slashes = new string('\\', backslashes);
        var opening = slashes + "\"word\"";
        var closing = "\"word" + slashes + "\"";
        var expectedCount = backslashes % 2 == 0 ? 1 : 0;
        Assert.Multiple(() => {
            Assert.That(PromptTextTokenParser.Parse(opening).Count(t => t.Kind == PromptTextTokenKind.QuotedContent),
                Is.EqualTo(expectedCount), "Escaped opening quote must remain literal.");
            var tokens = PromptTextTokenParser.Parse(closing).Where(t => t.Kind == PromptTextTokenKind.QuotedContent).ToArray();
            Assert.That(tokens, Has.Length.EqualTo(expectedCount), "Escaped closing quote must not complete the pair.");
            if (expectedCount == 1)
                Assert.That(tokens.Single(), Is.EqualTo(new PromptTextToken(1, 4 + backslashes, PromptTextTokenKind.QuotedContent)));
        });
    }

    [TestCase("\n")]
    [TestCase("\r")]
    [TestCase("\r\n")]
    public void Parse_HardBreak_DiscardsUnfinishedQuoteAndRecoversOnNextLine(string lineBreak) {
        var text = "\"unfinished" + lineBreak + "\"next\" \u201Cpair\u201D";
        var tokens = PromptTextTokenParser.Parse(text).Where(t => t.Kind == PromptTextTokenKind.QuotedContent).ToArray();
        Assert.That(tokens.Select(t => (t.Start, text.Substring(t.Start, t.Length))),
            Is.EqualTo(new[] { (text.IndexOf("next", System.StringComparison.Ordinal), "next"),
                (text.IndexOf("pair", System.StringComparison.Ordinal), "pair") }));
    }

    [TestCase("\"before `code \"literal\"` after\"")]
    [TestCase("\"before ``code ` \"literal\"`` after\"")]
    public void Parse_QuotesAroundCode_EmitOverlappingRangeForPainterToMask(string text) {
        var tokens = PromptTextTokenParser.Parse(text);
        var quote = tokens.Single(t => t.Kind == PromptTextTokenKind.QuotedContent);
        var code = tokens.Single(t => t.Kind == PromptTextTokenKind.InlineCodeContent);
        Assert.Multiple(() => {
            Assert.That(quote, Is.EqualTo(new PromptTextToken(1, text.Length - 2, PromptTextTokenKind.QuotedContent)));
            Assert.That(code.Start, Is.GreaterThan(quote.Start));
            Assert.That(code.Start + code.Length, Is.LessThan(quote.Start + quote.Length));
            Assert.That(tokens, Is.Ordered.By(nameof(PromptTextToken.Start)));
        });
    }

    [Test]
    public void Parse_QuotedBoldAndHex_PreserveAllOverlappingTokenKinds() {
        const string text = "\"**bold #abc**\"";
        var tokens = PromptTextTokenParser.Parse(text);
        Assert.That(tokens, Is.EqualTo(new[] {
            new PromptTextToken(1, 13, PromptTextTokenKind.QuotedContent),
            new PromptTextToken(3, 9, PromptTextTokenKind.BoldContent),
            new PromptTextToken(8, 4, PromptTextTokenKind.HexColor, Color.FromRgb(0xAA, 0xBB, 0xCC)),
        }));
    }

    [Test]
    public void Parse_LongIncompleteQuoteAndEscapes_CompletesWithinGenerousRegressionBudget() {
        var text = "\"" + string.Concat(Enumerable.Repeat(new string('\\', 63) + "\" prose ", 16000)) + "#A1B2C3";
        _ = PromptTextTokenParser.Parse("\"warmup\"");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var tokens = PromptTextTokenParser.Parse(text);
        watch.Stop();
        Assert.Multiple(() => {
            Assert.That(tokens.Where(t => t.Kind == PromptTextTokenKind.QuotedContent), Is.Empty);
            Assert.That(tokens.Single(t => t.Kind == PromptTextTokenKind.HexColor).Start, Is.EqualTo(text.Length - 7));
            Assert.That(watch.Elapsed, Is.LessThan(System.TimeSpan.FromSeconds(5)),
                "Over one million characters of incomplete escaped prose must not trigger pathological scanning.");
        });
        TestContext.Out.WriteLine($"Parsed {text.Length:N0} incomplete characters in {watch.Elapsed.TotalMilliseconds:F1} ms.");
    }

    [Test]
    public void Parse_PlainText_ReturnsNoTokens() {
        Assert.That(PromptTextTokenParser.Parse("plain text only"), Is.Empty);
    }

    [Test]
    public void Parse_HeadingMarkers_DetectsStartOfLineWithUpToThreeLeadingSpaces() {
        const string text = "# A\r\n  ## B\n   ### C\n    #### D\n####### E\n#F00";

        var tokens = PromptTextTokenParser.Parse(text);
        var headingTokens = tokens.Where(token => token.Kind == PromptTextTokenKind.HeadingMarker).ToArray();
        var hexTokens = tokens.Where(token => token.Kind == PromptTextTokenKind.HexColor).ToArray();

        Assert.Multiple(() => {
            Assert.That(headingTokens, Has.Length.EqualTo(3));
            Assert.That(headingTokens[0], Is.EqualTo(new PromptTextToken(text.IndexOf("# A", System.StringComparison.Ordinal), 1, PromptTextTokenKind.HeadingMarker)));
            Assert.That(headingTokens[1], Is.EqualTo(new PromptTextToken(text.IndexOf("## B", System.StringComparison.Ordinal), 2, PromptTextTokenKind.HeadingMarker)));
            Assert.That(headingTokens[2], Is.EqualTo(new PromptTextToken(text.IndexOf("### C", System.StringComparison.Ordinal), 3, PromptTextTokenKind.HeadingMarker)));
            Assert.That(hexTokens.Select(token => text.Substring(token.Start, token.Length)).ToArray(), Is.EqualTo(new[] { "#F00" }));
        });
    }

    [Test]
    public void Parse_HeadingMarkers_RequireWhitespaceOrEndOfLine_AndAllowEscapes() {
        const string text = "\\# no\n#yes\n# yes\n###\n  ####";

        var tokens = PromptTextTokenParser.Parse(text)
            .Where(token => token.Kind == PromptTextTokenKind.HeadingMarker)
            .ToArray();

        Assert.That(tokens.Select(token => text.Substring(token.Start, token.Length)).ToArray(),
            Is.EqualTo(new[] { "#", "###", "####" }));
    }

    [Test]
    public void Parse_BoldContent_ExcludesPairedDelimiters() {
        const string text = "a **bold** b __wide__ c";

        var tokens = PromptTextTokenParser.Parse(text)
            .Where(token => token.Kind == PromptTextTokenKind.BoldContent)
            .ToArray();

        Assert.Multiple(() => {
            Assert.That(tokens.Select(token => (token.Start, token.Length)), Is.EqualTo(new[] { (4, 4), (15, 4) }));
            Assert.That(tokens.Select(token => text.Substring(token.Start, token.Length)).ToArray(),
                Is.EqualTo(new[] { "bold", "wide" }));
        });
    }

    [Test]
    public void Parse_BoldContent_RejectsEscapedWhitespaceOnlyAndIntrawordUnderscore() {
        const string text = @"\**no** \__no__ foo__bar__ __ __ __yes__ **ok**";

        var tokens = PromptTextTokenParser.Parse(text)
            .Where(token => token.Kind == PromptTextTokenKind.BoldContent)
            .ToArray();

        Assert.That(tokens.Select(token => text.Substring(token.Start, token.Length)).ToArray(),
            Is.EqualTo(new[] { "yes", "ok" }));
    }

    [Test]
    public void Parse_InlineCode_SkipsMarkdownMarkersButStillFindsHexColors() {
        const string text = "`**not** __not__ #A1B2C3` and **yes** __ok__ #F00";

        var tokens = PromptTextTokenParser.Parse(text);
        var boldTokens = tokens.Where(token => token.Kind == PromptTextTokenKind.BoldContent).ToArray();
        var hexTokens = tokens.Where(token => token.Kind == PromptTextTokenKind.HexColor).ToArray();

        Assert.Multiple(() => {
            Assert.That(boldTokens.Select(token => text.Substring(token.Start, token.Length)).ToArray(),
                Is.EqualTo(new[] { "yes", "ok" }));
            Assert.That(hexTokens.Select(token => text.Substring(token.Start, token.Length)).ToArray(),
                Is.EqualTo(new[] { "#A1B2C3", "#F00" }));
        });
    }

    [Test]
    public void Parse_HexColors_RejectsEscapedAndInvalidBoundariesOrLengths() {
        const string text = "\\#F00 ##F00 a#F00 _#F00 #F00_name #F00z #12345 #1234567 #12345678 #12 #ZZZ #0A1 #ABCDEF. #F00\u0301";

        var tokens = PromptTextTokenParser.Parse(text)
            .Where(token => token.Kind == PromptTextTokenKind.HexColor)
            .ToArray();

        Assert.That(tokens.Select(token => text.Substring(token.Start, token.Length)).ToArray(),
            Is.EqualTo(new[] { "#0A1", "#ABCDEF" }));
    }

    [Test]
    public void Parse_MixedMultilineInput_PreservesOffsetsAndSortOrder() {
        const string text = "  ## Head\r\nText **b** and `__x__ #ABC` then __u__ #1a2.";

        var tokens = PromptTextTokenParser.Parse(text).ToArray();

        Assert.Multiple(() => {
            Assert.That(tokens, Is.Ordered.By(nameof(PromptTextToken.Start)));
            Assert.That(tokens.Where(token => token.Kind == PromptTextTokenKind.HeadingMarker)
                .Select(token => (token.Start, token.Length)).Single(), Is.EqualTo((2, 2)));
            Assert.That(tokens.Where(token => token.Kind == PromptTextTokenKind.BoldContent)
                .Select(token => text.Substring(token.Start, token.Length)).ToArray(), Is.EqualTo(new[] { "b", "u" }));
            Assert.That(tokens.Where(token => token.Kind == PromptTextTokenKind.HexColor)
                .Select(token => text.Substring(token.Start, token.Length)).ToArray(), Is.EqualTo(new[] { "#ABC", "#1a2" }));
        });
    }

    [Test]
    public void Parse_HexColorToken_PopulatesColorFromRendererHelper() {
        const string text = "Color #a1B2c3";

        var token = PromptTextTokenParser.Parse(text).Single(t => t.Kind == PromptTextTokenKind.HexColor);

        Assert.That(token.Color, Is.EqualTo(Color.FromRgb(0xA1, 0xB2, 0xC3)));
    }

    [Test]
    public void Parse_LongAdversarialInput_CompletesAndFindsExpectedTailHex() {
        var text = string.Concat(Enumerable.Repeat(@"\**unterminated ", 4000)) + "#A1B2C3";

        var tokens = PromptTextTokenParser.Parse(text);
        var hexTokens = tokens.Where(token => token.Kind == PromptTextTokenKind.HexColor).ToArray();

        Assert.Multiple(() => {
            Assert.That(hexTokens, Has.Length.EqualTo(1));
            Assert.That(hexTokens[0].Start, Is.EqualTo(text.Length - 7));
            Assert.That(text.Substring(hexTokens[0].Start, hexTokens[0].Length), Is.EqualTo("#A1B2C3"));
        });
    }

    [Test]
    public void Parse_DifferentBacktickRun_DoesNotCloseInlineCodeAtASuffix() {
        const string text = "`` code ``` **not bold** `` **bold**";
        var tokens = PromptTextTokenParser.Parse(text);
        Assert.That(tokens.Where(t => t.Kind == PromptTextTokenKind.BoldContent).Select(t => (t.Start, t.Length)),
            Is.EqualTo(new[] { (text.LastIndexOf("**bold**", System.StringComparison.Ordinal) + 2, 4) }));
    }

    [TestCase("before `code` after", "code")]
    [TestCase("``code ` with tick``", "code ` with tick")]
    [TestCase("`` code ``` **not bold** `` **bold**", " code ``` **not bold** ")]
    [TestCase("`first\nsecond`", "first\nsecond")]
    [TestCase("`**literal** #abc`", "**literal** #abc")]
    public void Parse_InlineCodeContent_ExcludesMatchingBacktickDelimiters(string text, string content) {
        var token = PromptTextTokenParser.Parse(text).Single(t => t.Kind == PromptTextTokenKind.InlineCodeContent);
        Assert.That(text.Substring(token.Start, token.Length), Is.EqualTo(content));
    }

    [TestCase("`unfinished")]
    [TestCase("`mismatched``")]
    [TestCase(@"\\\`escaped\`")]
    [TestCase("``")]
    public void Parse_UnpairedOrEscapedBackticks_EmitNoCodeContent(string text) {
        Assert.That(PromptTextTokenParser.Parse(text).Where(t => t.Kind == PromptTextTokenKind.InlineCodeContent), Is.Empty);
    }

    [TestCase("**unfinished")]
    [TestCase("__unfinished")]
    [TestCase("** **")]
    [TestCase("__\t__")]
    public void Parse_UnpairedOrEmptyBold_EmitsNoContent(string text) {
        Assert.That(PromptTextTokenParser.Parse(text), Is.Empty);
    }

    [Test]
    public void Parse_BoldContent_AllowsNestedAndOverlappingColorRanges() {
        const string text = "**outer __inner #ff5366 color__ tail** #abc";
        var tokens = PromptTextTokenParser.Parse(text);
        Assert.Multiple(() => {
            Assert.That(tokens, Is.Ordered.By(nameof(PromptTextToken.Start)));
            Assert.That(tokens.Where(t => t.Kind == PromptTextTokenKind.BoldContent)
                .Select(t => text.Substring(t.Start, t.Length)), Is.EqualTo(new[] { "outer __inner #ff5366 color__ tail", "inner #ff5366 color" }));
            Assert.That(tokens.Where(t => t.Kind == PromptTextTokenKind.HexColor)
                .Select(t => text.Substring(t.Start, t.Length)), Is.EqualTo(new[] { "#ff5366", "#abc" }));
        });
    }

    [TestCase("#000", 0, 0, 0)]
    [TestCase("#fff", 255, 255, 255)]
    [TestCase("#1aF", 17, 170, 255)]
    public void Parse_ShortHex_ExpandsEachChannel(string text, byte red, byte green, byte blue) {
        Assert.That(PromptTextTokenParser.Parse(text).Single().Color, Is.EqualTo(Color.FromRgb(red, green, blue)));
    }
}
