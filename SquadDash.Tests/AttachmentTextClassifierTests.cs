using System;
using System.Linq;
using SquadDash;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class AttachmentTextClassifierTests
{
    [Test]
    public void Attachment_Classify_EmptyInput_IsPlainTextAndPreservesContent()
    {
        var result = AttachmentTextClassifier.Classify(string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.PlainText));
            Assert.That(result.Content, Is.EqualTo(string.Empty));
            Assert.That(result.Grammar, Is.Null);
            Assert.That(result.LanguageLabel, Is.Null);
        });
    }

    [Test]
    public void Attachment_Classify_NoFencePlainText_StaysPlainText()
    {
        const string text = "Just a plain attachment body.";

        var result = AttachmentTextClassifier.Classify(text);

        Assert.Multiple(() =>
        {
            Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.PlainText));
            Assert.That(result.Content, Is.EqualTo(text));
            Assert.That(result.Grammar, Is.Null);
            Assert.That(result.LanguageLabel, Is.Null);
        });
    }

    [Test]
    public void Attachment_Classify_NoFenceDetectedCSharp_IsWholeCode()
    {
        const string text = """
            using System;

            public sealed class C
            {
                public static void Main(string[] args) => Console.WriteLine(args.Length);
            }
            """;

        var result = AttachmentTextClassifier.Classify(text);

        Assert.Multiple(() =>
        {
            Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.WholeCode));
            Assert.That(result.Content, Is.EqualTo(text));
            Assert.That(result.Grammar, Is.EqualTo("C#"));
            Assert.That(result.LanguageLabel, Is.EqualTo("C#"));
        });
    }

    [Test]
    public void Attachment_Classify_NoFenceDetectedJson_IsWholeCode()
    {
        const string text = """
            {
              "name": "SquadDash",
              "enabled": true,
              "count": 3
            }
            """;

        var result = AttachmentTextClassifier.Classify(text);

        Assert.Multiple(() =>
        {
            Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.WholeCode));
            Assert.That(result.Content, Is.EqualTo(text));
            Assert.That(result.Grammar, Is.EqualTo("Json"));
            Assert.That(result.LanguageLabel, Is.EqualTo("Json"));
        });
    }

    [Test]
    public void Attachment_Classify_XamlContentOrFence_UsesXamlGrammarAndLabel()
    {
        var content = RegionGrammarAutoDetectTests.XamlSample.Replace("\r\n", "\n");
        foreach (var text in new[] { content, "```xaml\n" + content + "\n```", "```\n" + content + "\n```" })
        {
            var result = AttachmentTextClassifier.Classify(text);
            Assert.Multiple(() =>
            {
                Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.WholeCode));
                Assert.That(result.Content, Is.EqualTo(content));
                Assert.That(result.Grammar, Is.EqualTo("XAML"));
                Assert.That(result.LanguageLabel, Is.EqualTo("XAML"));
            });
        }
    }

    [Test]
    public void Attachment_Classify_WholeFenceExplicitToken_UsesResolvedGrammarAndLabel()
    {
        const string text = "```csharp\nConsole.WriteLine(1);\n```";

        var result = AttachmentTextClassifier.Classify(text);

        Assert.Multiple(() =>
        {
            Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.WholeCode));
            Assert.That(result.Content, Is.EqualTo("Console.WriteLine(1);"));
            Assert.That(result.Grammar, Is.EqualTo("C#"));
            Assert.That(result.LanguageLabel, Is.EqualTo("C#"));
        });
    }

    [Test]
    public void Attachment_Classify_WholeFenceAliasToken_UsesCanonicalGrammarAndLabel()
    {
        const string text = "```cs\nConsole.WriteLine(1);\n```";

        var result = AttachmentTextClassifier.Classify(text);

        Assert.Multiple(() =>
        {
            Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.WholeCode));
            Assert.That(result.Content, Is.EqualTo("Console.WriteLine(1);"));
            Assert.That(result.Grammar, Is.EqualTo("C#"));
            Assert.That(result.LanguageLabel, Is.EqualTo("C#"));
        });
    }

    [Test]
    public void Attachment_Classify_WholeFenceUnknownToken_UsesTokenLabelWithoutGrammar()
    {
        const string text = "```unknownlang\nsome text\n```";

        var result = AttachmentTextClassifier.Classify(text);

        Assert.Multiple(() =>
        {
            Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.WholeCode));
            Assert.That(result.Content, Is.EqualTo("some text"));
            Assert.That(result.Grammar, Is.Null);
            Assert.That(result.LanguageLabel, Is.EqualTo("unknownlang"));
        });
    }

    [Test]
    public void Attachment_Classify_WholeFenceTextToken_UsesPlainTextLabelWithoutGrammar()
    {
        const string text = "```text\nliteral\n```";

        var result = AttachmentTextClassifier.Classify(text);

        Assert.Multiple(() =>
        {
            Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.WholeCode));
            Assert.That(result.Content, Is.EqualTo("literal"));
            Assert.That(result.Grammar, Is.Null);
            Assert.That(result.LanguageLabel, Is.EqualTo("Plain text"));
        });
    }

    [Test]
    public void Attachment_Classify_WholeFenceEmptyToken_AutoDetectsAndLabelsDetectedGrammar()
    {
        const string text = "```\n{ \"ok\": true }\n```";

        var result = AttachmentTextClassifier.Classify(text);

        Assert.Multiple(() =>
        {
            Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.WholeCode));
            Assert.That(result.Content, Is.EqualTo("{ \"ok\": true }"));
            Assert.That(result.Grammar, Is.EqualTo("Json"));
            Assert.That(result.LanguageLabel, Is.EqualTo("Json"));
        });
    }

    [Test]
    public void Attachment_Classify_InteriorFenceWithLeadingProse_IsMixedMarkdown()
    {
        const string text = """
            Intro
            ```cs
            Console.WriteLine(1);
            ```
            """;

        var result = AttachmentTextClassifier.Classify(text);

        Assert.Multiple(() =>
        {
            Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.MixedMarkdown));
            Assert.That(result.Content, Is.EqualTo(text));
            Assert.That(result.Grammar, Is.Null);
            Assert.That(result.LanguageLabel, Is.Null);
        });
    }

    [Test]
    public void Attachment_Classify_WholeFenceWithSingleTerminalNewline_IsStillWholeCode()
    {
        const string text = "```cs\r\nConsole.WriteLine(1);\r\n```\r\n";

        var result = AttachmentTextClassifier.Classify(text);

        Assert.Multiple(() =>
        {
            Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.WholeCode));
            Assert.That(result.Content, Is.EqualTo("Console.WriteLine(1);"));
            Assert.That(result.Grammar, Is.EqualTo("C#"));
            Assert.That(result.LanguageLabel, Is.EqualTo("C#"));
        });
    }

    [Test]
    public void Attachment_Classify_WholeFenceWithExtraTrailingBlankLine_IsMixedMarkdown()
    {
        const string text = "```cs\nConsole.WriteLine(1);\n```\n\n";

        var result = AttachmentTextClassifier.Classify(text);

        Assert.Multiple(() =>
        {
            Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.MixedMarkdown));
            Assert.That(result.Content, Is.EqualTo(text));
            Assert.That(result.Grammar, Is.Null);
            Assert.That(result.LanguageLabel, Is.Null);
        });
    }

    [Test]
    public void Attachment_Classify_MultipleFences_RemainsMixedMarkdown()
    {
        const string text = """
            Before
            ```cs
            Console.WriteLine(1);
            ```
            Middle
            ```json
            { "ok": true }
            ```
            After
            """;

        var result = AttachmentTextClassifier.Classify(text);

        Assert.Multiple(() =>
        {
            Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.MixedMarkdown));
            Assert.That(result.Content, Is.EqualTo(text));
            Assert.That(result.Grammar, Is.Null);
            Assert.That(result.LanguageLabel, Is.Null);
        });
    }

    [Test]
    public void Attachment_Classify_IncompleteFence_DoesNotTreatAsMixedMarkdown()
    {
        const string text = "```cs\nstill typing";

        var result = AttachmentTextClassifier.Classify(text);

        Assert.Multiple(() =>
        {
            Assert.That(result.Mode, Is.Not.EqualTo(AttachmentTextRenderMode.MixedMarkdown));
            Assert.That(result.Content, Is.EqualTo(text));
        });
    }

    [Test]
    public void Attachment_Classify_OverCharacterCap_FallsBackToOriginalPlainText()
    {
        var text = new string('x', TranscriptCodeBlockPreview.MaximumCharacters + 1);

        var result = AttachmentTextClassifier.Classify(text);

        Assert.Multiple(() =>
        {
            Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.PlainText));
            Assert.That(result.Content, Is.EqualTo(text));
            Assert.That(result.Grammar, Is.Null);
            Assert.That(result.LanguageLabel, Is.Null);
        });
    }

    [Test]
    public void Attachment_Classify_CharacterCapInclusive_PermitsClassification()
    {
        const string prefix = "using System;\nclass C {}\n";
        var text = prefix + new string(' ', TranscriptCodeBlockPreview.MaximumCharacters - prefix.Length);

        var result = AttachmentTextClassifier.Classify(text);

        Assert.Multiple(() =>
        {
            Assert.That(text.Length, Is.EqualTo(TranscriptCodeBlockPreview.MaximumCharacters));
            Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.WholeCode));
            Assert.That(result.Grammar, Is.EqualTo("C#"));
            Assert.That(result.LanguageLabel, Is.EqualTo("C#"));
            Assert.That(result.Content, Is.EqualTo(text));
        });
    }

    [TestCase("\n")]
    [TestCase("\r\n")]
    [TestCase("\r")]
    public void Attachment_Classify_OverLineCap_FallsBackToOriginalPlainText(string newline)
    {
        var text = string.Join(newline, Enumerable.Repeat("x", TranscriptCodeBlockPreview.MaximumLines + 1));

        var result = AttachmentTextClassifier.Classify(text);

        Assert.Multiple(() =>
        {
            Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.PlainText));
            Assert.That(result.Content, Is.EqualTo(text));
            Assert.That(result.Grammar, Is.Null);
            Assert.That(result.LanguageLabel, Is.Null);
        });
    }

    [TestCase("\r\n")]
    [TestCase("\r")]
    public void Attachment_Classify_LineCapInclusive_PermitsWholeFenceClassification(string newline)
    {
        var content = string.Join(newline, Enumerable.Repeat("line", TranscriptCodeBlockPreview.MaximumLines - 2));
        var text = EmbeddedFenceDetector.BuildFencedText(content, "text").Replace("\n", newline);

        var result = AttachmentTextClassifier.Classify(text);

        Assert.Multiple(() =>
        {
            Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.WholeCode));
            Assert.That(result.Content, Is.EqualTo(content.Replace("\r\n", "\n").Replace("\r", "\n")));
            Assert.That(result.Grammar, Is.Null);
            Assert.That(result.LanguageLabel, Is.EqualTo("Plain text"));
        });
    }

    [Test]
    public void Attachment_Classify_WholeFenceUsesExtractedContent_PreservesSpacesBlankLinesAndNestedBackticks()
    {
        const string content = "  leading\n\n```\ntrailing  ";
        var text = EmbeddedFenceDetector.BuildFencedText(content, "text");

        var result = AttachmentTextClassifier.Classify(text);

        Assert.Multiple(() =>
        {
            Assert.That(text.StartsWith("````text\n", StringComparison.Ordinal), Is.True);
            Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.WholeCode));
            Assert.That(result.Content, Is.EqualTo(content));
            Assert.That(result.Grammar, Is.Null);
            Assert.That(result.LanguageLabel, Is.EqualTo("Plain text"));
        });
    }

    [TestCase("text")]
    [TestCase("unknown")]
    public void Attachment_ExplicitFenceDoesNotAutoDetectItsCode(string token)
    {
        var result = AttachmentTextClassifier.Classify($"```{token}\nusing System;\n```");
        Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.WholeCode));
        Assert.That(result.Grammar, Is.Null);
    }

    [Test]
    public void Attachment_UnlabelledUnrecognizedFenceRemainsWholeCode()
    {
        var result = AttachmentTextClassifier.Classify("```\nordinary words\n```");
        Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.WholeCode));
        Assert.That(result.Content, Is.EqualTo("ordinary words"));
        Assert.That(result.Grammar, Is.Null);
        Assert.That(result.LanguageLabel, Is.EqualTo("Plain text"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Attachment_FenceClassificationCannotBypassCharacterCap(bool interior)
    {
        var text = (interior ? "Before\n" : "") + "```cs\nusing System;\n"
            + new string('x', TranscriptCodeBlockPreview.MaximumCharacters) + "\n```";
        var result = AttachmentTextClassifier.Classify(text);
        Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.PlainText));
        Assert.That(result.Content, Is.EqualTo(text));
    }

    [Test]
    public void Attachment_DetectedCodeCannotBypassLineCap()
    {
        var text = "using System;" + new string('\n', TranscriptCodeBlockPreview.MaximumLines);
        var result = AttachmentTextClassifier.Classify(text);
        Assert.That(result.Mode, Is.EqualTo(AttachmentTextRenderMode.PlainText));
        Assert.That(result.Content, Is.EqualTo(text));
    }
}
