using System.Linq;
using System.Windows.Media;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PromptTextTokenParserTests {
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
