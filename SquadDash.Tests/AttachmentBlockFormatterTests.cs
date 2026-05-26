namespace SquadDash.Tests;

[TestFixture]
internal sealed class AttachmentBlockFormatterTests {

    // ── BuildTypedAttachmentBlock ─────────────────────────────────────────────

    [Test]
    public void BuildTypedAttachmentBlock_EscapesCloseTagInContent() {
        var block = AttachmentBlockFormatter.BuildTypedAttachmentBlock("code", null, "before</attachment>after");

        // The literal close tag must not appear inside the block before the real one.
        var realClose = block.LastIndexOf("</attachment>", StringComparison.Ordinal);
        var innerContent = block[..realClose];
        Assert.That(innerContent, Does.Not.Contain("</attachment>"));
        Assert.That(innerContent, Does.Contain("&lt;/attachment&gt;"));
    }

    [Test]
    public void BuildTypedAttachmentBlock_WithTitle_EscapesCloseTagInContent() {
        const string original = "line1</attachment>line2";
        var block = AttachmentBlockFormatter.BuildTypedAttachmentBlock("snippet", "MyTitle", original);

        var result = AttachmentBlockFormatter.ExtractAttachmentContent(block);

        Assert.That(result, Is.EqualTo(original));
    }

    // ── StripTypedAttachmentHeaders ───────────────────────────────────────────

    [Test]
    public void StripTypedAttachmentHeaders_HandlesContentWithCloseTag() {
        var block = AttachmentBlockFormatter.BuildTypedAttachmentBlock("code", null, "data</attachment>end");
        const string userMessage = "What does this do?";
        var prompt = $"{block}\n\n{userMessage}";

        var idx = AttachmentBlockFormatter.StripTypedAttachmentHeaders(prompt);

        Assert.That(idx, Is.GreaterThan(0));
        Assert.That(prompt[idx..], Is.EqualTo(userMessage));
    }

    [Test]
    public void StripTypedAttachmentHeaders_WithPreamble_ReturnsCorrectBodyStart() {
        const string Preamble = "The user has attached the following context items. Please refer to them as needed.";
        var block = AttachmentBlockFormatter.BuildTypedAttachmentBlock("file", null, "some content");
        const string userMessage = "Please summarise the above.";
        var prompt = $"{Preamble}\n{block}\n\n{userMessage}";

        var idx = AttachmentBlockFormatter.StripTypedAttachmentHeaders(prompt);

        Assert.That(idx, Is.GreaterThan(0));
        Assert.That(prompt[idx..], Is.EqualTo(userMessage));
    }

    [Test]
    public void StripTypedAttachmentHeaders_PlainMultiParagraphPrompt_ReturnsMinusOne() {
        const string prompt = "First paragraph.\n\nSecond paragraph.";

        var idx = AttachmentBlockFormatter.StripTypedAttachmentHeaders(prompt);

        Assert.That(idx, Is.EqualTo(-1));
    }

    [Test]
    public void StripTypedAttachmentHeaders_TranscriptQuoteHeader_ReturnsBodyStart() {
        const string userMessage = "Please continue from this selection.";
        var prompt = "Regarding this section of the transcript: \"selected text\"\n\n" + userMessage;

        var idx = AttachmentBlockFormatter.StripTypedAttachmentHeaders(prompt);

        Assert.That(idx, Is.GreaterThan(0));
        Assert.That(prompt[idx..], Is.EqualTo(userMessage));
    }

    [Test]
    public void StripTypedAttachmentHeaders_CrlfTypedBlock_ReturnsBodyStart() {
        var block = AttachmentBlockFormatter.BuildTypedAttachmentBlock("file", null, "some content")
            .Replace("\n", "\r\n");
        const string userMessage = "Please summarise the above.";
        var prompt = $"{block}\r\n\r\n{userMessage}";

        var idx = AttachmentBlockFormatter.StripTypedAttachmentHeaders(prompt);

        Assert.That(idx, Is.GreaterThan(0));
        Assert.That(prompt[idx..], Is.EqualTo(userMessage));
    }

    // ── ExtractAttachmentContent ──────────────────────────────────────────────

    [Test]
    public void RoundTrip_ContentWithCloseTag_SurvivesExtract() {
        const string original = "alpha</attachment>beta";
        var block = AttachmentBlockFormatter.BuildTypedAttachmentBlock("text", null, original);

        var result = AttachmentBlockFormatter.ExtractAttachmentContent(block);

        Assert.That(result, Is.EqualTo(original));
    }

    [Test]
    public void RoundTrip_ContentWithMultipleCloseTags() {
        const string original = "</attachment>middle</attachment>end</attachment>";
        var block = AttachmentBlockFormatter.BuildTypedAttachmentBlock("text", null, original);

        var result = AttachmentBlockFormatter.ExtractAttachmentContent(block);

        Assert.That(result, Is.EqualTo(original));
    }

    [Test]
    public void ExtractAttachmentContent_FallsBackForNonAttachmentBlock() {
        const string plain = "This is just a plain string.";

        var result = AttachmentBlockFormatter.ExtractAttachmentContent(plain);

        Assert.That(result, Is.EqualTo(plain));
    }

    [Test]
    public void ExtractAttachmentBlocks_ReturnsTypedBlocksFromHeader() {
        var first = AttachmentBlockFormatter.BuildTypedAttachmentBlock("clipboard", "Error details", "stack trace");
        var second = AttachmentBlockFormatter.BuildTypedAttachmentBlock("note", "Daily note", "note body");
        var prompt = "The user has attached the following context items. Please refer to them as needed.\n" +
                     first + "\n" + second + "\n\nPlease inspect these.";

        var blocks = AttachmentBlockFormatter.ExtractAttachmentBlocks(prompt);

        Assert.That(blocks, Has.Count.EqualTo(2));
        Assert.That(AttachmentBlockFormatter.ExtractAttachmentContent(blocks[0]), Is.EqualTo("stack trace"));
        Assert.That(AttachmentBlockFormatter.ExtractAttachmentContent(blocks[1]), Is.EqualTo("note body"));
    }

    [Test]
    public void ExtractAttachmentMetadata_ReturnsTypeAndDecodedTitle() {
        var block = AttachmentBlockFormatter.BuildTypedAttachmentBlock("inbox-message", "Quoted \"Title\"", "body");

        var (type, title) = AttachmentBlockFormatter.ExtractAttachmentMetadata(block);

        Assert.That(type, Is.EqualTo("inbox-message"));
        Assert.That(title, Is.EqualTo("Quoted \"Title\""));
    }
}
