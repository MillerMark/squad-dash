using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Documents;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class AttachmentTransportRegressionTests
{
    private const string Preamble =
        "The user has attached the following context items. Please refer to them as needed.";
    private const string Summary = "\U0001F4CE 2 attachments";
    private const string VoiceAnnotation = "\n(some or all of this prompt was dictated by voice)";
    private const string FirstContent = "this would cover text/code attachments.";
    private const string SecondContent =
        "Razor needs a dedicated mixed-language grammar; the current highlighter doesn\u2019t include one.";
    private const string Timestamp = "2026-10-24T19:16:06.6257788-04:00";
    private const string FirstBlock =
        "<attachment type=\"transcript\" title=\"" + FirstContent +
        "\" encoding=\"json-notes-v1\" attribute-encoding=\"xml-v1\"" +
        " source-conversation-hint=\"Attachment highlighting\" source-role=\"assistant\"" +
        " source-timestamp=\"" + Timestamp + "\" source-turn-index=\"5\">\n" +
        "{\"Content\":\"this would cover text/code attachments.\",\"Notes\":\"and inline code regions\"}\n" +
        "</attachment>";
    private const string SecondBlock =
        "<attachment type=\"transcript\" title=\"" + SecondContent +
        "\" encoding=\"json-notes-v1\" attribute-encoding=\"xml-v1\"" +
        " source-conversation-hint=\"Attachment highlighting\" source-role=\"assistant\"" +
        " source-timestamp=\"" + Timestamp + "\" source-turn-index=\"5\">\n" +
        "{\"Content\":\"Razor needs a dedicated mixed-language grammar; the current highlighter doesn\\u2019t include one.\"," +
        "\"Notes\":\"let\\u00B4s build one.\"}\n</attachment>";

    // Keep the reported wire format independent of the formatter under test.
    private static string ReportedHeader(string newline = "\n") =>
        (Preamble + "\n" + FirstBlock + "\n" + SecondBlock).Replace("\n", newline);

    [Test, Combinatorial]
    public void ReportedTwoNotesAttachments_EofAndBodyBoundariesStripAndCountTogether(
        [Values("\n", "\r\n")] string newline,
        [Values(0, 1, 2)] int separatorLines,
        [Values("", Summary, "Please implement the grammar.")] string body)
    {
        var providerPrompt = ReportedHeader(newline) +
            string.Concat(Enumerable.Repeat(newline, separatorLines)) + body;
        var expectedBody = body == Summary ? string.Empty : body;
        var header = AttachmentBlockFormatter.ParseAttachmentHeaders(providerPrompt);
        var display = SteeringTranscriptEntryFactory.ParsePersistedPrompt(providerPrompt);

        Assert.That(header, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(header!.Count, Is.EqualTo(2));
            Assert.That(header.Blocks, Has.Count.EqualTo(2));
            Assert.That(header.Lines, Is.Empty);
            Assert.That(providerPrompt[header.BodyStart..], Is.EqualTo(body));
            Assert.That(AttachmentBlockFormatter.StripTypedAttachmentHeaders(providerPrompt), Is.EqualTo(header.BodyStart));
            Assert.That(AttachmentBlockFormatter.CountAttachmentBlocks(providerPrompt), Is.EqualTo(2));
            Assert.That(display.Prompt, Is.EqualTo(expectedBody));
            Assert.That(display.AttachmentCount, Is.EqualTo(2));
            Assert.That(display.Attachments.Select(a => a.TranscriptQuote), Is.EqualTo(new[] { FirstContent, SecondContent }));
            Assert.That(display.Attachments.Select(a => a.Description), Is.EqualTo(new[] { FirstContent, SecondContent }));
            Assert.That(display.Attachments.Select(a => a.Notes), Is.EqualTo(new[] { "and inline code regions", "let\u00B4s build one." }));
            Assert.That(display.Attachments.Select(a => a.TranscriptSource), Is.All.EqualTo(
                new TranscriptAttachmentSource("assistant", 5, Timestamp, "Attachment highlighting")));
        });
    }

    [TestCase("\n \t\n")]
    [TestCase("\r\n \t\r\n")]
    public void WhitespaceOnlySeparator_DoesNotTrimOrConsumeUserBody(string separator)
    {
        const string body = "  Keep this indentation.\n\nKeep this paragraph.  ";
        var prompt = FirstBlock + separator + body;
        var display = SteeringTranscriptEntryFactory.ParsePersistedPrompt(prompt);
        Assert.That(display.Prompt, Is.EqualTo(body));
        Assert.That(display.AttachmentCount, Is.EqualTo(1));
    }

    [TestCase("text")]
    [TestCase("code")]
    [TestCase("clipboard")]
    [TestCase("file")]
    [TestCase("file-reference")]
    [TestCase("doc")]
    [TestCase("inbox-excerpt")]
    [TestCase("inbox-message")]
    [TestCase("note")]
    [TestCase("task")]
    [TestCase("transcript")]
    [TestCase("future-type")]
    public void TypedAttachments_WithAndWithoutNotes_RetainContentAndType(string type)
    {
        const string content = "Text and `inline code`.\n\n```razor\n@if (ready) { <p>@name</p> }\n```\n</attachment>";
        const string notes = "Preserve notes with `code` and </attachment> too.";
        var plain = AttachmentBlockFormatter.BuildTypedAttachmentBlock(type, "A \"quoted\" title > limit", content);
        var annotated = AttachmentBlockFormatter.WithNotes(plain, notes);
        var prompt = Preamble + "\n" + plain + "\n" + annotated + "\nContinue.";
        var display = SteeringTranscriptEntryFactory.ParsePersistedPrompt(prompt);

        Assert.Multiple(() =>
        {
            Assert.That(display.Prompt, Is.EqualTo("Continue."));
            Assert.That(display.AttachmentCount, Is.EqualTo(2));
            Assert.That(display.Attachments.Select(a => a.Notes), Is.EqualTo(new string?[] { null, notes }));
            Assert.That(display.Attachments.Select(a => a.TranscriptQuote ??
                AttachmentBlockFormatter.ExtractAttachmentContent(a.ContentBlock!)), Is.All.EqualTo(content));
            Assert.That(AttachmentBlockFormatter.ParseAttachmentHeaders(prompt)!.Blocks
                .Select(b => AttachmentBlockFormatter.ExtractAttachmentMetadata(b).Type), Is.All.EqualTo(type));
        });
    }

    [TestCase("\n")]
    [TestCase("\r\n")]
    public void ReorderedAndExtraAttributes_QuotedAnglesAndXmlEntitiesPreserveMetadata(string newline)
    {
        const string block =
            "<attachment title=\"A &quot;quoted&quot; > title &amp; &lt;tag&gt;\" extra=\"x > y\"" +
            " source-turn-index=\"5\" source-timestamp=\"" + Timestamp + "\" source-message-id=\"id&amp;5\"" +
            " encoding=\"json-notes-v1\" source-conversation-hint=\"A &amp; B &quot;quoted&quot; &lt;/attachment&gt;\"" +
            " source-role=\"assistant\" attribute-encoding=\"xml-v1\" type=\"transcript\">\n" +
            "{\"Content\":\"selected `code`\",\"Notes\":\"note\"}\n</attachment>";
        var display = SteeringTranscriptEntryFactory.ParsePersistedPrompt(block.Replace("\n", newline));
        var attachment = display.Attachments.Single();

        Assert.Multiple(() =>
        {
            Assert.That(display.Prompt, Is.Empty);
            Assert.That(attachment.Description, Is.EqualTo("A \"quoted\" > title & <tag>"));
            Assert.That(attachment.TranscriptQuote, Is.EqualTo("selected `code`"));
            Assert.That(attachment.Notes, Is.EqualTo("note"));
            Assert.That(attachment.TranscriptSource, Is.EqualTo(
                new TranscriptAttachmentSource("assistant", 5, Timestamp, "A & B \"quoted\" </attachment>", "id&5")));
        });
    }

    [TestCase("[Attached image: C:\\images\\screenshot.png]")]
    [TestCase("[Follow-up on abc123 \u2014 \"Description\": original prompt]")]
    [TestCase("Regarding this section of the transcript: \"selected text\"")]
    [TestCase("Regarding this section of the transcript: \"first line\nsecond line\"")]
    [TestCase("[Follow-up on abc123 \u2014 \"Description\": first line\nsecond line]")]
    public void LegacyHeaders_AcceptEofSingleNewlineAndBlankSeparator(string legacyHeader)
    {
        foreach (var newline in new[] { "\n", "\r\n" })
        {
            var header = legacyHeader.Replace("\n", newline);
            foreach (var suffix in new[] { "", newline, newline + "Continue.", newline + newline + "Continue." })
            {
                var display = SteeringTranscriptEntryFactory.ParsePersistedPrompt(header + suffix);
                Assert.Multiple(() =>
                {
                    Assert.That(display.Prompt, Is.EqualTo(suffix.TrimStart('\r', '\n')), header + suffix);
                    Assert.That(display.AttachmentCount, Is.EqualTo(1));
                    Assert.That(display.Attachments, Has.Count.EqualTo(1));
                    Assert.That(AttachmentBlockFormatter.CountAttachmentBlocks(header + suffix), Is.Zero);
                });
            }
        }
    }

    [TestCase("first paragraph\n\nsecond paragraph")]
    [TestCase("first line\n```csharp\nvar x = 1;\n```")]
    [TestCase("He said \"yes\"\nand continued")]
    public void GeneratedLegacyTranscriptQuote_DoesNotLeakOrLoseMultilineSelection(string quote)
    {
        var pending = new[] { new FollowUpAttachment("", "Transcript", null, TranscriptQuote: quote) };
        var prompt = FollowUpPromptBuilder.Build("Continue.", pending);
        var live = SteeringTranscriptEntryFactory.ParsePromptForDisplay(prompt, pending);
        var replay = SteeringTranscriptEntryFactory.ParsePersistedPrompt(prompt);
        Assert.Multiple(() =>
        {
            Assert.That(live.Prompt, Is.EqualTo("Continue."));
            Assert.That(replay.Prompt, Is.EqualTo("Continue."));
            Assert.That(replay.AttachmentCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void InterleavedLegacyAndTypedAttachments_ReplayPreservesLiveViewerOrder()
    {
        var pending = new[]
        {
            new FollowUpAttachment("", "Image", null, ImagePath: @"C:\image.png"),
            new FollowUpAttachment("", "First", null, ContentBlock:
                AttachmentBlockFormatter.BuildTypedAttachmentBlock("note", "First", "one")),
            new FollowUpAttachment("", "Image", null, ImagePath: @"C:\second.png"),
            new FollowUpAttachment("", "Second", null, ContentBlock:
                AttachmentBlockFormatter.BuildTypedAttachmentBlock("code", "Second", "two"))
        };
        var prompt = FollowUpPromptBuilder.Build("Use these in order.", pending);
        var live = SteeringTranscriptEntryFactory.ParsePromptForDisplay(prompt, pending);
        var replay = SteeringTranscriptEntryFactory.ParsePersistedPrompt(prompt);
        Assert.Multiple(() =>
        {
            Assert.That(replay.Prompt, Is.EqualTo(live.Prompt));
            Assert.That(replay.AttachmentCount, Is.EqualTo(4));
            Assert.That(replay.Attachments.Select(a => a.ImagePath ?? a.ContentBlock),
                Is.EqualTo(pending.Select(a => a.ImagePath ?? a.ContentBlock)));
        });
    }

    [TestCase("```xml\n{sample}\n```")]
    [TestCase("~~~text\n{sample}\n~~~")]
    [TestCase("The transcript leaked this:\n```text\n{sample}\n```")]
    [TestCase("\"{sample}\"")]
    [TestCase("`{sample}`")]
    [TestCase("Literal example:\n{sample}")]
    public void FencedQuotedAndLiteralBugReports_AreNotTransport(string template)
    {
        var prompt = template.Replace("{sample}", ReportedHeader() + "\n" + Summary);
        AssertNotTransport(prompt);
    }

    [TestCase("> ")]
    [TestCase("    ")]
    public void BlockQuotedAndIndentedBugReports_AreNotTransport(string prefix)
    {
        var prompt = string.Join("\n", (ReportedHeader() + "\n" + Summary).Split('\n').Select(line => prefix + line));
        AssertNotTransport(prompt);
    }

    [TestCase("\n")]
    [TestCase("\n\n")]
    [TestCase("")]
    public void OrdinaryAdjacentProse_StopsScanningBeforeLaterAttachmentExamples(string separator)
    {
        var body = "Keep ordinary prose.\n" + FirstBlock + "\n" +
            "[Attached image: example.png]\n" + Preamble + "\n```xml\n" + SecondBlock + "\n```";
        var prompt = FirstBlock + separator + body;
        var display = SteeringTranscriptEntryFactory.ParsePersistedPrompt(prompt);
        Assert.Multiple(() =>
        {
            Assert.That(display.Prompt, Is.EqualTo(body));
            Assert.That(display.AttachmentCount, Is.EqualTo(1));
            Assert.That(AttachmentBlockFormatter.CountAttachmentBlocks(prompt), Is.EqualTo(1));
        });
    }

    [Test]
    public void BlankSeparator_EndsProtocolEvenWhenBodyStartsWithAnotherValidAttachment()
    {
        var body = SecondBlock + "\nThis is a literal example, not a third attachment.";
        var display = SteeringTranscriptEntryFactory.ParsePersistedPrompt(ReportedHeader() + "\n\n" + body);
        Assert.That(display.Prompt, Is.EqualTo(body));
        Assert.That(display.AttachmentCount, Is.EqualTo(2));
    }

    [TestCase(Preamble)]
    [TestCase(Preamble + "\nOrdinary prose.")]
    [TestCase(Preamble + "<attachment type=\"text\">content</attachment>")]
    [TestCase("<attachment type=\"text\">\nmissing close")]
    [TestCase("<attachment type=\"text\">\ncontent</wrong>")]
    [TestCase("<attachment title=\"missing type\">\ncontent</attachment>")]
    [TestCase("<attachment type=\"\">\ncontent</attachment>")]
    [TestCase("<attachment type=\"text\" title=\"unclosed>\ncontent</attachment>")]
    [TestCase("[Attached image: missing close\n\nOrdinary prose]")]
    [TestCase("[Follow-up on abc: missing close\n\nOrdinary prose]")]
    [TestCase("Regarding this section of the transcript: \"unclosed\n\nOrdinary prose\"")]
    public void MalformedOrPreambleOnlyInput_IsNeitherStrippedNorCounted(string prompt) =>
        AssertNotTransport(prompt);

    [Test]
    public void ValidBlockThenUnterminatedTypedHeader_DoesNotInventSuccessfulPartialParse() =>
        AssertNotTransport(FirstBlock + "\n<attachment type=\"text\">\nmissing close");

    [Test]
    public void UnterminatedTypedHeader_CannotBorrowClosingTagFromFencedUserExample()
    {
        var prompt = "<attachment type=\"text\">\nmissing close\n\n" +
            "Please fix this example:\n```xml\n<attachment type=\"code\">literal</attachment>\n```";
        AssertNotTransport(prompt);
    }

    [Test]
    public void UnterminatedOpenTag_CannotBorrowQuotesAndAnglesFromLaterUserProse()
    {
        var prompt = "<attachment type=\"text\" title=\"unclosed\n\n" +
            "The user said \" > then supplied </attachment> literally.";
        AssertNotTransport(prompt);
    }

    [TestCase("```xml", "```")]
    [TestCase("~~~~xml", "~~~~")]
    public void UnterminatedTypedHeader_CannotBorrowStandaloneClosingTagInsideFence(string open, string close)
    {
        AssertNotTransport("<attachment type=\"text\">\nmissing close\n\nExample:\n" +
            open + "\n<attachment type=\"code\">\nliteral\n</attachment>\n" + close);
    }

    [TestCase("{not json}")]
    [TestCase("{\"Content\":\"text\"}")]
    [TestCase("{\"Content\":4,\"Notes\":\"note\"}")]
    public void MalformedNotesPayload_RemainsLiteralInsteadOfThrowing(string payload)
    {
        AssertNotTransport("<attachment type=\"text\" encoding=\"json-notes-v1\">\n" +
            payload + "\n</attachment>\n\nKeep this.");
    }

    [Test]
    public void AdjacentTypedBlocksWithoutNewlines_PreserveOrderAndCount()
    {
        var display = SteeringTranscriptEntryFactory.ParsePersistedPrompt(FirstBlock + SecondBlock + Summary);
        Assert.That(display.Prompt, Is.Empty);
        Assert.That(display.AttachmentCount, Is.EqualTo(2));
        Assert.That(display.Attachments.Select(a => a.TranscriptQuote), Is.EqualTo(new[] { FirstContent, SecondContent }));
    }

    [TestCase("first paragraph\n\nsecond paragraph")]
    [TestCase("first line\n```csharp\nvar x = 1;\n```")]
    [TestCase("He said \"yes\"\nand continued")]
    public void GeneratedLegacyQuote_ReplayViewerRetainsEntireSelection(string quote)
    {
        var pending = new[] { new FollowUpAttachment("", "Transcript", null, TranscriptQuote: quote) };
        var display = SteeringTranscriptEntryFactory.ParsePersistedPrompt(FollowUpPromptBuilder.Build("Continue.", pending));
        Assert.That(display.Prompt, Is.EqualTo("Continue."));
        Assert.That(display.Attachments.Single().TranscriptQuote, Is.EqualTo(quote));
    }

    [TestCase("```csharp\nvar unfinished = 1;")]
    [TestCase("~~~xml\n<attachment type=\"code\">literal</attachment>\n~~~")]
    public void GeneratedTypedContent_WithOpenOrNestedCodeFence_RetainsContent(string content)
    {
        var block = AttachmentBlockFormatter.BuildTypedAttachmentBlock("code", null, content);
        var display = SteeringTranscriptEntryFactory.ParsePersistedPrompt(block + "\n\nContinue.");
        Assert.That(display.Prompt, Is.EqualTo("Continue."));
        Assert.That(AttachmentBlockFormatter.ExtractAttachmentContent(display.Attachments.Single().ContentBlock!), Is.EqualTo(content));
    }

    [Test]
    public void MultilineAttribute_NotesEncodingAndRawExtractionRemainUnchanged()
    {
        var block = AttachmentBlockFormatter.BuildTypedAttachmentBlock("text", "first\nsecond", "content");
        var annotated = AttachmentBlockFormatter.WithNotes(block, "notes");
        Assert.That(AttachmentBlockFormatter.ExtractAttachmentMetadata(annotated).Title, Is.EqualTo("first\nsecond"));
        Assert.That(AttachmentBlockFormatter.ExtractAttachmentContent(annotated), Is.EqualTo("content"));
        Assert.That(AttachmentBlockFormatter.ExtractAttachmentNotes(annotated), Is.EqualTo("notes"));
    }

    [Test, Combinatorial]
    public void GeneratedMultilineTitles_ProjectWithoutChangingTransportOrMetadata(
        [Values("\n", "\r\n")] string newline,
        [Values(false, true)] bool withNotes,
        [Values(false, true)] bool withSource)
    {
        var title = "First > line" + newline + newline + "second \"line\" &lt;literal&gt;";
        var source = withSource ? new TranscriptAttachmentSource(
            "assistant", 5, Timestamp, "conversation" + newline + "continued") : null;
        var block = AttachmentBlockFormatter.BuildTranscriptAttachmentBlock(title, "selected text", source);
        var pending = new[]
        {
            new FollowUpAttachment("", title, null, ContentBlock: block, Notes: withNotes ? "notes" : null),
            new FollowUpAttachment("", "Image", null, ImagePath: @"C:\image.png")
        };
        var prompt = FollowUpPromptBuilder.Build("Keep this body.", pending);
        var replay = SteeringTranscriptEntryFactory.ParsePersistedPrompt(prompt);
        var live = SteeringTranscriptEntryFactory.ParsePromptForDisplay(prompt, pending);
        var parsedBlock = AttachmentBlockFormatter.ParseAttachmentHeaders(prompt)!.Blocks.Single();
        Assert.Multiple(() =>
        {
            Assert.That(live.Prompt, Is.EqualTo("Keep this body."));
            Assert.That(replay.Prompt, Is.EqualTo(live.Prompt));
            Assert.That(replay.AttachmentCount, Is.EqualTo(2));
            Assert.That(replay.Attachments[0].Description, Is.EqualTo(title));
            Assert.That(replay.Attachments[0].TranscriptQuote, Is.EqualTo("selected text"));
            Assert.That(replay.Attachments[0].Notes, Is.EqualTo(withNotes ? "notes" : null));
            Assert.That(replay.Attachments[0].TranscriptSource, Is.EqualTo(source));
            Assert.That(parsedBlock, Is.EqualTo(withNotes ? AttachmentBlockFormatter.WithNotes(block, "notes") : block));
            Assert.That(FollowUpPromptBuilder.Build("Keep this body.", pending), Is.EqualTo(prompt));
        });
    }

    [Test, Combinatorial]
    public void GeneratedLegacyQuote_WithParagraphsOrLiteralQuotes_PreservesMixedHeaderSequence(
        [Values("first paragraph\n\nsecond paragraph", "one unmatched \" literal",
            "one unmatched \" literal\ncontinued", "He said \"yes\"\nand continued",
            "```text\nfirst\n\nsecond\n```")] string quote,
        [Values(false, true)] bool followedByAttachment,
        [Values("\n", "\r\n")] string newline)
    {
        quote = quote.Replace("\n", newline);
        var pending = new List<FollowUpAttachment>
        {
            new("", "Transcript", null, TranscriptQuote: quote)
        };
        if (followedByAttachment)
        {
            pending.Add(new("", "Image", null, ImagePath: @"C:\image.png"));
            pending.Add(new("", "Note", null, ContentBlock:
                AttachmentBlockFormatter.BuildTypedAttachmentBlock("note", null, "note")));
        }
        var prompt = FollowUpPromptBuilder.Build("Keep this body.", pending);
        var display = SteeringTranscriptEntryFactory.ParsePersistedPrompt(prompt);
        Assert.Multiple(() =>
        {
            Assert.That(display.Prompt, Is.EqualTo("Keep this body."));
            Assert.That(display.AttachmentCount, Is.EqualTo(pending.Count));
            Assert.That(display.Attachments.FirstOrDefault()?.TranscriptQuote, Is.EqualTo(quote));
            Assert.That(SteeringTranscriptEntryFactory.ParsePromptForDisplay(prompt, pending).Prompt,
                Is.EqualTo("Keep this body."));
            Assert.That(FollowUpPromptBuilder.Build("Keep this body.", pending), Is.EqualTo(prompt));
        });
    }

    [TestCase("<attachment type=\"code\">\nliteral\n</attachment>")]
    [TestCase("```xml\n<attachment type=\"code\">\nliteral\n</attachment>\n\n```")]
    [TestCase("~~~~xml\n<attachment type=\"code\">\nliteral\n</attachment>\n \t\n\n~~~~")]
    [TestCase("```xml\n</attachment>\n\n```")]
    [TestCase("    <attachment type=\"code\">\n    literal\n</attachment>")]
    public void UnterminatedTypedBlock_DoesNotBorrowLaterLiteralExample(string example)
    {
        foreach (var newline in new[] { "\n", "\r\n" })
            AssertNotTransport(("<attachment type=\"text\">\nmissing close\n\nExample:\n" +
                example + "\n\nKeep this body.").Replace("\n", newline));
    }

    [TestCase("<attachment type=\"text\" title=\"unclosed\n\nThe user said \" >\n</attachment>")]
    [TestCase("<attachment type=\"text\" title=\"unclosed\n\n<attachment type=\"code\">\nliteral\n</attachment>")]
    [TestCase("<attachment type=\"text\" broken>\nliteral\n</attachment>")]
    [TestCase("<attachment title=\"a type=&quot;text&quot;\">\nliteral\n</attachment>")]
    [TestCase("<attachment type=\"text\" type=\"code\">\nliteral\n</attachment>")]
    [TestCase("<attachment type=\"text\">missing close\n\nExample:\n</attachment>\n\nBody")]
    [TestCase("Regarding this section of the transcript: \"missing\n\n```text\nliteral\"\n\n```\n\nBody")]
    [TestCase("Regarding this section of the transcript: \"missing\n<attachment type=\"code\">\nliteral\n</attachment>\n\nBody")]
    [TestCase("[Attached image: first.png]\nRegarding this section of the transcript: \"missing")]
    public void MalformedHeaderGrammar_RemainsEntirelyVisible(string prompt) => AssertNotTransport(prompt);

    [TestCase("first paragraph\n\nsecond paragraph")]
    [TestCase("one unmatched \" literal")]
    public void LegacyComplexQuote_BeforeMalformedNextHeader_DoesNotPartiallyStrip(string quote)
    {
        AssertNotTransport("Regarding this section of the transcript: \"" + quote +
            "\"\n[Attached image: missing close\n\nKeep this body.");
    }

    [TestCase("one unmatched \"\ncontinued")]
    [TestCase("selected\"\nThe user said \"keep this")]
    public void LegacyLineEndingLiteralQuote_WithCompetingCompleteBoundary_RemainsVisible(string quote)
    {
        var pending = new[] { new FollowUpAttachment("", "Transcript", null, TranscriptQuote: quote) };
        AssertNotTransport(FollowUpPromptBuilder.Build("Keep this body.", pending));
    }

    [Test]
    public void SimpleLegacyQuote_DoesNotBorrowQuotesAcrossBodyParagraphOrFence()
    {
        const string header = "Regarding this section of the transcript: \"selected\"";
        foreach (var body in new[]
        {
            "Keep this paragraph.\n\nA later \"quotation\"\n\nKeep this too.",
            "```text\nliteral\"\n\n```\n\nKeep this too."
        })
        {
            var display = SteeringTranscriptEntryFactory.ParsePersistedPrompt(header + "\n" + body);
            Assert.That(display.Prompt, Is.EqualTo(body));
            Assert.That(display.Attachments.Single().TranscriptQuote, Is.EqualTo("selected"));
        }
    }

    [TestCase("<attachment type=\"code\">\nexample\n</attachment>")]
    [TestCase("```xml\n<attachment type=\"code\">\nexample\n</attachment>\n\n```")]
    [TestCase("First paragraph\n\nSecond paragraph")]
    public void GeneratedTypedContent_WithNestedExamplesAndParagraphs_RemainsAnAttachment(string content)
    {
        var block = AttachmentBlockFormatter.BuildTypedAttachmentBlock("text", "Example", content);
        var display = SteeringTranscriptEntryFactory.ParsePersistedPrompt(block + "\n\nBody");
        Assert.Multiple(() =>
        {
            Assert.That(display.Prompt, Is.EqualTo("Body"));
            Assert.That(display.AttachmentCount, Is.EqualTo(1));
            Assert.That(display.Attachments.Single().ContentBlock, Is.EqualTo(block));
            Assert.That(AttachmentBlockFormatter.ExtractAttachmentContent(block), Is.EqualTo(content));
        });
    }

    [TestCase("```text\n" + Summary + "\n```")]
    [TestCase("> " + Summary)]
    [TestCase("    " + Summary)]
    [TestCase("\t" + Summary)]
    [TestCase("Please preserve this text: " + Summary)]
    [TestCase("The label is:\n" + Summary + "\nKeep it in the explanation.")]
    [TestCase("\U0001F4CE 3 attachments")]
    public void SummaryLookingUserContent_IsNotDeduplicated(string body)
    {
        var display = SteeringTranscriptEntryFactory.ParsePersistedPrompt(ReportedHeader() + "\n\n" + body);
        Assert.That(display.Prompt, Is.EqualTo(body));
    }

    [Test]
    public void StandaloneSummaryWithoutTransport_IsOrdinaryUserText() => AssertNotTransport(Summary);

    [TestCase(false)]
    [TestCase(true)]
    [Apartment(ApartmentState.STA)]
    public void AttachmentOnlySummary_RendersOnceWithoutLeadingBlankAndOpensBothAttachments(bool dictated)
    {
        var providerPrompt = ReportedHeader() + "\n" + Summary + (dictated ? VoiceAnnotation : "");
        var display = SteeringTranscriptEntryFactory.ParsePersistedPrompt(providerPrompt);
        var paragraph = new Paragraph();
        IReadOnlyList<FollowUpAttachment>? opened = null;
        SteeringTranscriptEntryFactory.AppendPromptSupplements(
            paragraph.Inlines, display.Attachments, display.AttachmentCount, dictated,
            attachments => opened = attachments, "Open attachments");
        var link = paragraph.Inlines.OfType<Hyperlink>().Single();
        link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));

        Assert.Multiple(() =>
        {
            Assert.That(display.Prompt, Is.EqualTo(dictated ? VoiceAnnotation : ""));
            Assert.That(new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text,
                Is.EqualTo(Summary + (dictated ? "  " + VoiceAnnotation[1..] : "")));
            Assert.That(opened, Is.SameAs(display.Attachments));
            Assert.That(opened, Has.Count.EqualTo(2));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void LiveReplayAndSteeringCopy_KeepOnlyProseAndOneSummary()
    {
        const string body = "Implement the grammar.";
        var pending = ReportedAttachments();
        var providerPrompt = FollowUpPromptBuilder.Build(body, pending);
        var live = SteeringTranscriptEntryFactory.ParsePromptForDisplay(providerPrompt, pending);
        var replay = SteeringTranscriptEntryFactory.ParsePersistedPrompt(providerPrompt);
        var liveParagraph = SteeringTranscriptEntryFactory.CreateParagraph(
            live.Prompt, live.Attachments, live.AttachmentCount, null, null, null);
        var replayParagraph = SteeringTranscriptEntryFactory.CreateParagraph(
            replay.Prompt, replay.Attachments, replay.AttachmentCount, null, null, null);
        var expected = SteeringTranscriptEntryFactory.MarkerText + ": " + body + "\n" + Summary;
        var notice = new TranscriptHostNoticeEntry(1,
            SteeringTranscriptEntryFactory.EncodeHostNotice(providerPrompt), DateTimeOffset.UtcNow);
        Assert.Multiple(() =>
        {
            Assert.That(new TextRange(liveParagraph.ContentStart, liveParagraph.ContentEnd).Text, Is.EqualTo(expected));
            Assert.That(new TextRange(replayParagraph.ContentStart, replayParagraph.ContentEnd).Text, Is.EqualTo(expected));
            Assert.That(notice.GetCopyText(), Is.EqualTo(expected));
            Assert.That(live.Attachments, Is.SameAs(pending));
            Assert.That(replay.Attachments, Is.EqualTo(pending));
        });
    }

    [TestCase("\n")]
    [TestCase("\r\n")]
    public void OutboundAndPersistedTransport_RetainsNotesContentMetadataAndReplayParity(string newline)
    {
        using var workspace = new TestWorkspace();
        var workspacePath = workspace.GetPath("workspace");
        Directory.CreateDirectory(workspacePath);
        var store = new WorkspaceConversationStore(workspace.GetPath("store"));
        var pending = ReportedAttachments();
        const string body = "Implement the grammar.";
        var outbound = FollowUpPromptBuilder.Build(body, pending).Replace("\n", newline);
        var snapshot = JsonSerializer.Serialize(pending);
        var live = SteeringTranscriptEntryFactory.ParsePromptForDisplay(outbound, pending);
        var now = DateTimeOffset.UtcNow;
        var encodedNotice = SteeringTranscriptEntryFactory.EncodeHostNotice(outbound);
        store.Save(workspacePath, new WorkspaceConversationState("attachments", now, "", [],
            [new TranscriptTurnRecord(now, now, outbound, "", "Done.", true, [],
                HostNotices: [new TranscriptHostNoticeRecord(encodedNotice, 1, now)])]));
        var loaded = store.Load(workspacePath).Turns.Single();
        Assert.That(SteeringTranscriptEntryFactory.TryDecodeHostNotice(
            loaded.GetHostNotices().Single().Text, out var decoded), Is.True);
        var replay = SteeringTranscriptEntryFactory.ParsePersistedPrompt(loaded.Prompt);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Prompt, Is.EqualTo(outbound));
            Assert.That(decoded, Is.EqualTo(loaded.Prompt));
            Assert.That(replay.Prompt, Is.EqualTo(live.Prompt).And.EqualTo(body));
            Assert.That(replay.Attachments, Is.EqualTo(pending));
            Assert.That(replay.AttachmentCount, Is.EqualTo(live.AttachmentCount));
            Assert.That(JsonSerializer.Serialize(pending), Is.EqualTo(snapshot));
            Assert.That(FollowUpPromptBuilder.Build(body, pending).Replace("\n", newline), Is.EqualTo(outbound));
            Assert.That(AttachmentBlockFormatter.CountAttachmentBlocks(loaded.Prompt), Is.EqualTo(2));
        });
    }

    [Test]
    public void LegacyClipboardFence_KeepsInternalBlankLinesAndIgnoresBodyExamples()
    {
        const string header = "<<<SQUADCLIP:abc123>>>\nfirst\n\nsecond\n<<<ENDCLIP:abc123>>>";
        var body = "Please inspect.\n```xml\n" + FirstBlock + "\n```";
        var display = SteeringTranscriptEntryFactory.ParsePersistedPrompt(header + "\n\n" + body);
        Assert.Multiple(() =>
        {
            Assert.That(display.Prompt, Is.EqualTo(body));
            Assert.That(display.AttachmentCount, Is.EqualTo(1));
            Assert.That(display.Attachments, Is.Empty, "Legacy clipboard replay has a count but no viewer payload.");
        });
        AssertNotTransport(header.Replace("ENDCLIP:abc123", "ENDCLIP:wrong") + "\n\n" + body);
    }

    private static FollowUpAttachment[] ReportedAttachments()
    {
        var source = new TranscriptAttachmentSource("assistant", 5, Timestamp, "Attachment highlighting");
        return
        [
            new("", FirstContent, null, TranscriptQuote: FirstContent, Notes: "and inline code regions", TranscriptSource: source),
            new("", SecondContent, null, TranscriptQuote: SecondContent, Notes: "let\u00B4s build one.", TranscriptSource: source)
        ];
    }

    private static void AssertNotTransport(string prompt)
    {
        var display = SteeringTranscriptEntryFactory.ParsePersistedPrompt(prompt);
        Assert.Multiple(() =>
        {
            Assert.That(AttachmentBlockFormatter.StripTypedAttachmentHeaders(prompt), Is.EqualTo(-1));
            Assert.That(AttachmentBlockFormatter.CountAttachmentBlocks(prompt), Is.Zero);
            Assert.That(display.Prompt, Is.EqualTo(prompt));
            Assert.That(display.AttachmentCount, Is.Zero);
            Assert.That(display.Attachments, Is.Empty);
        });
    }
}
