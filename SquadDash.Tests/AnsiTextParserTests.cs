namespace SquadDash.Tests;

[TestFixture]
internal sealed class AnsiTextParserTests {
    [Test]
    public void Parse_PlainText_ReturnsSingleSpanWithDefaultStyle() {
        var spans = AnsiTextParser.Parse("hello world");

        Assert.That(spans, Has.Count.EqualTo(1));
        Assert.That(spans[0].Text, Is.EqualTo("hello world"));
        Assert.That(spans[0].Style, Is.EqualTo(AnsiStyle.Default));
    }

    [Test]
    public void Parse_EmptyOrNullInput_ReturnsNoSpans() {
        Assert.That(AnsiTextParser.Parse(""), Is.Empty);
        Assert.That(AnsiTextParser.Parse(null!), Is.Empty);
    }

    [Test]
    public void Parse_RedBoldThenReset_StripsEscapesAndAppliesStyle() {
        // Matches the real PowerShell error prefix from the bug report: ESC[31;1m...ESC[0m
        var spans = AnsiTextParser.Parse("\u001b[31;1mRestart-Service: \u001b[0mfailure text");

        Assert.That(spans, Has.Count.EqualTo(2));
        Assert.That(spans[0].Text, Is.EqualTo("Restart-Service: "));
        Assert.That(spans[0].Style.Foreground, Is.EqualTo(AnsiColor.Red));
        Assert.That(spans[0].Style.Bold, Is.True);
        Assert.That(spans[1].Text, Is.EqualTo("failure text"));
        Assert.That(spans[1].Style, Is.EqualTo(AnsiStyle.Default));
    }

    [Test]
    public void Parse_CyanBold_FromWarningExample() {
        var spans = AnsiTextParser.Parse("\u001b[36;1mLine |\u001b[0m");

        Assert.That(spans, Has.Count.EqualTo(1));
        Assert.That(spans[0].Text, Is.EqualTo("Line |"));
        Assert.That(spans[0].Style.Foreground, Is.EqualTo(AnsiColor.Cyan));
        Assert.That(spans[0].Style.Bold, Is.True);
    }

    [Test]
    public void Parse_BrightForegroundCodes_MapToBrightColors() {
        var spans = AnsiTextParser.Parse("\u001b[91mbright red\u001b[0m");

        Assert.That(spans[0].Style.Foreground, Is.EqualTo(AnsiColor.BrightRed));
    }

    [Test]
    public void Parse_StandardAndBrightBackgroundCodes_SetBackground() {
        var standard = AnsiTextParser.Parse("\u001b[44mtext\u001b[0m");
        var bright = AnsiTextParser.Parse("\u001b[104mtext\u001b[0m");

        Assert.That(standard[0].Style.Background, Is.EqualTo(AnsiColor.Blue));
        Assert.That(bright[0].Style.Background, Is.EqualTo(AnsiColor.BrightBlue));
    }

    [Test]
    public void Parse_Underline_SetsUnderlineAndClearsOnCode24() {
        var spans = AnsiTextParser.Parse("\u001b[4munderlined\u001b[24mnot underlined\u001b[0m");

        Assert.That(spans[0].Style.Underline, Is.True);
        Assert.That(spans[1].Style.Underline, Is.False);
    }

    [Test]
    public void Parse_BoldOffCode22_ClearsBoldWithoutResettingColor() {
        var spans = AnsiTextParser.Parse("\u001b[31;1mbold red\u001b[22mnormal red\u001b[0m");

        Assert.That(spans[0].Style.Bold, Is.True);
        Assert.That(spans[0].Style.Foreground, Is.EqualTo(AnsiColor.Red));
        Assert.That(spans[1].Style.Bold, Is.False);
        Assert.That(spans[1].Style.Foreground, Is.EqualTo(AnsiColor.Red));
    }

    [Test]
    public void Parse_DefaultForegroundAndBackgroundCodes_ClearColors() {
        var spans = AnsiTextParser.Parse("\u001b[31;44mcolored\u001b[39;49mplain\u001b[0m");

        Assert.That(spans[0].Style.Foreground, Is.EqualTo(AnsiColor.Red));
        Assert.That(spans[0].Style.Background, Is.EqualTo(AnsiColor.Blue));
        Assert.That(spans[1].Style.Foreground, Is.Null);
        Assert.That(spans[1].Style.Background, Is.Null);
    }

    [Test]
    public void Parse_TrueColorForeground_SetsRgbAndClearsNamedColor() {
        var spans = AnsiTextParser.Parse("\u001b[38;2;10;20;30mtrue color\u001b[0m");

        Assert.That(spans[0].Style.ForegroundRgb, Is.EqualTo(new AnsiRgbColor(10, 20, 30)));
        Assert.That(spans[0].Style.Foreground, Is.Null);
    }

    [Test]
    public void Parse_256ColorPaletteIndex_IsSkippedWithoutThrowing() {
        // 256-color codes are not mapped to a specific color; they must not crash or desync
        // parsing of codes that follow them in the same escape sequence.
        Assert.DoesNotThrow(() => AnsiTextParser.Parse("\u001b[38;5;202;1mtext\u001b[0m"));
        var spans = AnsiTextParser.Parse("\u001b[38;5;202;1mtext\u001b[0m");
        Assert.That(spans[0].Style.Bold, Is.True);
    }

    [Test]
    public void Parse_UnknownSgrCode_IsIgnoredWithoutAffectingOtherCodes() {
        var spans = AnsiTextParser.Parse("\u001b[5;31mtext\u001b[0m"); // 5 = blink, unsupported

        Assert.That(spans[0].Style.Foreground, Is.EqualTo(AnsiColor.Red));
    }

    [Test]
    public void Parse_NonSgrCsiSequence_IsStrippedAndIgnored() {
        // ESC[2K (clear line) is a real sequence PowerShell/console apps emit; it must not show
        // up as text and must not be misread as a color code.
        var spans = AnsiTextParser.Parse("before\u001b[2Kafter");

        Assert.That(spans, Has.Count.EqualTo(1));
        Assert.That(spans[0].Text, Is.EqualTo("beforeafter"));
    }

    [Test]
    public void Parse_UnterminatedEscapeAtEndOfBuffer_IsDroppedNotShown() {
        var spans = AnsiTextParser.Parse("partial output\u001b[31;1");

        Assert.That(spans, Has.Count.EqualTo(1));
        Assert.That(spans[0].Text, Is.EqualTo("partial output"));
    }

    [Test]
    public void Parse_MultilineSpan_PreservesEmbeddedNewlinesWithinASingleStyledSpan() {
        var spans = AnsiTextParser.Parse("\u001b[31mline one\nline two\u001b[0m");

        Assert.That(spans, Has.Count.EqualTo(1));
        Assert.That(spans[0].Text, Is.EqualTo("line one\nline two"));
        Assert.That(spans[0].Style.Foreground, Is.EqualTo(AnsiColor.Red));
    }

    [Test]
    public void Parse_RealWorldPowerShellErrorExcerpt_ProducesExpectedPlainText() {
        var input = "\u001b[31;1mRestart-Service: \u001b[0mC:\\temp\\996110f010cf4caeaf01e7f3a95e1b0b.ps1:1\n" +
                    "\u001b[36;1mLine |\u001b[0m\n" +
                    "\u001b[36;1m\u001b[36;1m   1 | \u001b[0m \u001b[36;1mrestart-service audiosrv\u001b[0m";

        var spans = AnsiTextParser.Parse(input);
        var plainText = string.Concat(spans.Select(s => s.Text));

        Assert.That(plainText, Is.EqualTo(
            "Restart-Service: C:\\temp\\996110f010cf4caeaf01e7f3a95e1b0b.ps1:1\n" +
            "Line |\n" +
            "   1 |  restart-service audiosrv"));
        Assert.That(plainText, Does.Not.Contain("[31;1m"));
    }
}
