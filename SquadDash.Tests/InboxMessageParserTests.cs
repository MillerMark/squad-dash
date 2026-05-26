namespace SquadDash.Tests;

[TestFixture]
internal sealed class InboxMessageParserTests {

    private const string MinimalJson = """
        {
          "subject": "README report",
          "from": "argus-weld",
          "body": "Done",
          "attachments": []
        }
        """;

    [Test]
    public void TryExtract_BareBlock_ParsesMessageAndStripsBody() {
        var text = $"""
            Report ready.

            INBOX_MESSAGE_JSON:
            {MinimalJson}
            """;

        var result = InboxMessageParser.TryExtract(text, out var body, out var dto);

        Assert.That(result, Is.True);
        Assert.That(body, Is.EqualTo("Report ready."));
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.Subject, Is.EqualTo("README report"));
        Assert.That(dto.From, Is.EqualTo("argus-weld"));
        Assert.That(dto.Body, Is.EqualTo("Done"));
    }

    [Test]
    public void TryExtract_FencedBlockAtEnd_ParsesMessageAndStripsFenceFromBody() {
        var text = $"""
            Report ready.

            ```
            INBOX_MESSAGE_JSON:
            {MinimalJson}
            ```
            """;

        var result = InboxMessageParser.TryExtract(text, out var body, out var dto);

        Assert.That(result, Is.True);
        Assert.That(body, Is.EqualTo("Report ready."));
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.Subject, Is.EqualTo("README report"));
    }

    /// <summary>
    /// Regression: model emits INBOX block then appends a prose/markdown summary after it.
    /// Parser must succeed and strip from the marker onwards.
    /// </summary>
    [Test]
    public void TryExtract_BlockInMiddleWithTrailingProse_ParsesAndStripsBlock() {
        var text = $"""
            Report ready.

            INBOX_MESSAGE_JSON:
            {MinimalJson}

            ## Summary
            **Tasks Found: 52** — see table above for details.
            """;

        var result = InboxMessageParser.TryExtract(text, out var body, out var dto);

        Assert.That(result, Is.True);
        Assert.That(body, Is.EqualTo("Report ready."));
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.Subject, Is.EqualTo("README report"));
    }

    [Test]
    public void TryExtract_BlockAtStartWithTrailingProse_ParsesAndBodyIsEmpty() {
        var text = $"""
            INBOX_MESSAGE_JSON:
            {MinimalJson}

            Some trailing summary text here.
            """;

        var result = InboxMessageParser.TryExtract(text, out var body, out var dto);

        Assert.That(result, Is.True);
        Assert.That(body, Is.EqualTo(string.Empty));
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.Subject, Is.EqualTo("README report"));
    }

    [Test]
    public void TryExtract_MultipleInboxBlocks_UsesLastOne() {
        const string text = """
            First pass:

            INBOX_MESSAGE_JSON:
            {
              "subject": "First",
              "from": "agent-a",
              "body": "old",
              "attachments": []
            }

            Second pass:

            INBOX_MESSAGE_JSON:
            {
              "subject": "README report",
              "from": "argus-weld",
              "body": "Done",
              "attachments": []
            }
            """;

        var result = InboxMessageParser.TryExtract(text, out var body, out var dto);

        Assert.That(result, Is.True);
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.Subject, Is.EqualTo("README report"), "Should use the LAST INBOX block");
    }

    /// <summary>
    /// The fenced-with-trailing-text scenario now parses successfully because the
    /// parser is intentionally tolerant of content after the closing brace.
    /// </summary>
    [Test]
    public void TryExtract_FencedBlockWithTrailingText_NowParses() {
        const string text = """
            Example:

            ```json
            INBOX_MESSAGE_JSON:
            { "subject": "Example", "from": "", "body": "", "attachments": [] }
            ```

            The real response continues here.
            """;

        var result = InboxMessageParser.TryExtract(text, out var body, out var dto);

        Assert.That(result, Is.True);
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.Subject, Is.EqualTo("Example"));
    }

    [Test]
    public void TryExtract_NoMarker_ReturnsFalse() {
        const string text = "Just some plain text without any marker.";

        var result = InboxMessageParser.TryExtract(text, out var body, out var dto);

        Assert.That(result, Is.False);
        Assert.That(body, Is.EqualTo(text));
        Assert.That(dto, Is.Null);
    }

    // ── null / empty / whitespace ────────────────────────────────────────────

    [Test]
    public void TryExtract_NullInput_ReturnsFalse() {
        var result = InboxMessageParser.TryExtract(null!, out _, out var dto);

        Assert.That(result, Is.False);
        Assert.That(dto, Is.Null);
    }

    [Test]
    public void TryExtract_EmptyInput_ReturnsFalse() {
        var result = InboxMessageParser.TryExtract(string.Empty, out var body, out var dto);

        Assert.That(result, Is.False);
        Assert.That(body, Is.EqualTo(string.Empty));
        Assert.That(dto, Is.Null);
    }

    [Test]
    public void TryExtract_WhitespaceOnlyInput_ReturnsFalse() {
        var result = InboxMessageParser.TryExtract("   \n\t  ", out _, out var dto);

        Assert.That(result, Is.False);
        Assert.That(dto, Is.Null);
    }

    // ── malformed / missing JSON ─────────────────────────────────────────────

    [Test]
    public void TryExtract_MarkerWithNoBrace_ReturnsFalse() {
        const string text = "INBOX_MESSAGE_JSON:\nno braces here at all";

        var result = InboxMessageParser.TryExtract(text, out var body, out var dto);

        Assert.That(result, Is.False);
        Assert.That(body, Is.EqualTo(text));
        Assert.That(dto, Is.Null);
    }

    [Test]
    public void TryExtract_BraceOnlyBeforeMarker_ReturnsFalse() {
        // { appears before the marker – IndexOf searches *after* markerIdx so this should not be found
        const string text = "Some { text } here.\nINBOX_MESSAGE_JSON:\nno braces after the marker";

        var result = InboxMessageParser.TryExtract(text, out _, out var dto);

        Assert.That(result, Is.False);
        Assert.That(dto, Is.Null);
    }

    [Test]
    public void TryExtract_MarkerWithTruncatedJson_ReturnsFalse() {
        // Opening brace but no matching closing brace
        const string text = """
            INBOX_MESSAGE_JSON:
            { "subject": "Truncated", "from": "agent"
            """;

        var result = InboxMessageParser.TryExtract(text, out _, out var dto);

        Assert.That(result, Is.False);
        Assert.That(dto, Is.Null);
    }

    [Test]
    public void TryExtract_MarkerWithInvalidJson_ReturnsFalse() {
        // Closing brace is found by the scanner but content is not valid JSON
        const string text = "INBOX_MESSAGE_JSON:\n{ subject: 123 }";

        var result = InboxMessageParser.TryExtract(text, out _, out var dto);

        Assert.That(result, Is.False);
        Assert.That(dto, Is.Null);
    }

    // ── JSON body content ────────────────────────────────────────────────────

    [Test]
    public void TryExtract_NestedBracesInBodyField_ParsesCorrectly() {
        // Braces inside a JSON string value must not confuse the depth scanner
        const string text = """
            INBOX_MESSAGE_JSON:
            { "subject": "Nested", "from": "argus-weld", "body": "See {key: value} for {more} details", "attachments": [] }
            """;

        var result = InboxMessageParser.TryExtract(text, out _, out var dto);

        Assert.That(result, Is.True);
        Assert.That(dto!.Body, Is.EqualTo("See {key: value} for {more} details"));
    }

    [Test]
    public void TryExtract_EscapedQuotesInBody_ParsesCorrectly() {
        // \" inside a raw string literal is the two-char JSON escape sequence
        const string text = """
            INBOX_MESSAGE_JSON:
            { "subject": "Quotes", "from": "argus-weld", "body": "He said \"hello\" world", "attachments": [] }
            """;

        var result = InboxMessageParser.TryExtract(text, out _, out var dto);

        Assert.That(result, Is.True);
        Assert.That(dto!.Body, Is.EqualTo("He said \"hello\" world"));
    }

    [Test]
    public void TryExtract_NewlineEscapeInBody_ParsesCorrectly() {
        // \n in the raw string is the two-char JSON escape; JSON deserialiser expands it to a real newline
        const string text = """
            INBOX_MESSAGE_JSON:
            { "subject": "Multiline", "from": "argus-weld", "body": "Line1\nLine2", "attachments": [] }
            """;

        var result = InboxMessageParser.TryExtract(text, out _, out var dto);

        Assert.That(result, Is.True);
        Assert.That(dto!.Body, Is.EqualTo("Line1\nLine2"));
    }

    [Test]
    public void TryExtract_UnicodeAndEmojiInBody_ParsesCorrectly() {
        const string text = """
            INBOX_MESSAGE_JSON:
            { "subject": "Unicode", "from": "argus-weld", "body": "Done ✓ 🎉", "attachments": [] }
            """;

        var result = InboxMessageParser.TryExtract(text, out _, out var dto);

        Assert.That(result, Is.True);
        Assert.That(dto!.Body, Is.EqualTo("Done ✓ 🎉"));
    }

    // ── field round-trips ────────────────────────────────────────────────────

    [Test]
    public void TryExtract_FromCoordinator_ParsesCorrectly() {
        const string text = """
            Update complete.

            INBOX_MESSAGE_JSON:
            { "subject": "Ready", "from": "coordinator", "body": "Task done", "attachments": [] }
            """;

        var result = InboxMessageParser.TryExtract(text, out var body, out var dto);

        Assert.That(result, Is.True);
        Assert.That(dto!.From, Is.EqualTo("coordinator"));
        Assert.That(body, Is.EqualTo("Update complete."));
    }

    [Test]
    public void TryExtract_EmptyJsonObject_ReturnsTrueWithDefaultFields() {
        // {} is valid JSON; all DTO fields should be their defaults
        const string text = "INBOX_MESSAGE_JSON:\n{}";

        var result = InboxMessageParser.TryExtract(text, out var body, out var dto);

        Assert.That(result, Is.True);
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.Subject, Is.EqualTo(string.Empty));
        Assert.That(dto.From, Is.EqualTo(string.Empty));
        Assert.That(dto.Body, Is.EqualTo(string.Empty));
        Assert.That(dto.Attachments, Is.Empty);
        Assert.That(dto.Actions, Is.Empty);
        Assert.That(body, Is.EqualTo(string.Empty));
    }

    [Test]
    public void TryExtract_AttachmentsAndActionsDeserializeCorrectly() {
        const string text = """
            INBOX_MESSAGE_JSON:
            {
              "subject": "Rich",
              "from": "argus-weld",
              "body": "See attached",
              "attachments": [{ "type": "file", "label": "report.md", "path": "docs/report.md" }],
              "actions": [{ "label": "Approve", "routeMode": "start_coordinator", "prompt": "Approve the report." }]
            }
            """;

        var result = InboxMessageParser.TryExtract(text, out _, out var dto);

        Assert.That(result, Is.True);
        Assert.That(dto!.Attachments, Has.Count.EqualTo(1));
        Assert.That(dto.Attachments[0].Label, Is.EqualTo("report.md"));
        Assert.That(dto.Actions, Has.Count.EqualTo(1));
        Assert.That(dto.Actions[0].Label, Is.EqualTo("Approve"));
        Assert.That(dto.Actions[0].RouteMode, Is.EqualTo("start_coordinator"));
    }

    // ── withoutBlock / body stripping ────────────────────────────────────────

    [Test]
    public void TryExtract_JsonFencedBlock_BodyStrippedCorrectly() {
        // ```json fence should be stripped from the body along with the block
        const string text = """
            Report below.

            ```json
            INBOX_MESSAGE_JSON:
            { "subject": "Fenced", "from": "argus-weld", "body": "Works", "attachments": [] }
            ```
            """;

        var result = InboxMessageParser.TryExtract(text, out var body, out var dto);

        Assert.That(result, Is.True);
        Assert.That(body, Is.EqualTo("Report below."));
        Assert.That(dto!.Subject, Is.EqualTo("Fenced"));
    }

    [Test]
    public void TryExtract_MultipleBlocks_BodyIsEverythingBeforeLastMarker() {
        // body = everything up to (not including) the last INBOX_MESSAGE_JSON: marker,
        // which means the first block's raw text is included in body.
        const string text = """
            Preamble text.

            INBOX_MESSAGE_JSON:
            { "subject": "First", "from": "agent-a", "body": "old", "attachments": [] }

            Interlude text.

            INBOX_MESSAGE_JSON:
            { "subject": "Second", "from": "agent-b", "body": "new", "attachments": [] }
            """;

        var result = InboxMessageParser.TryExtract(text, out var body, out var dto);

        Assert.That(result, Is.True);
        Assert.That(dto!.Subject, Is.EqualTo("Second"));
        Assert.That(body, Does.Contain("Preamble text."));
        Assert.That(body, Does.Contain("Interlude text."));
        Assert.That(body, Does.Not.Contain("Second"), "Last block content must not appear in body");
    }

    // ── literal whitespace sanitization ─────────────────────────────────────

    [Test]
    public void TryExtract_LiteralNewlinesInBodyAndPrompt_ParsesSuccessfully()
    {
        // Simulate the AI emitting real newlines inside JSON string values
        var json = "{\n  \"subject\": \"Code Review\",\n  \"from\": \"argus-weld\",\n  \"body\": \"Here is the review:\nLine one\nLine two\",\n  \"attachments\": [],\n  \"actions\": [{ \"label\": \"Apply\", \"routeMode\": \"start_coordinator\", \"prompt\": \"Apply the patch:\nstep 1\nstep 2\" }]\n}";
        var text = $"INBOX_MESSAGE_JSON:\n{json}";

        var result = InboxMessageParser.TryExtract(text, out _, out var dto);

        Assert.That(result, Is.True);
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.Body, Does.Contain("Line one"));
        Assert.That(dto.Actions, Has.Count.EqualTo(1));
        Assert.That(dto.Actions[0].Prompt, Does.Contain("step 1"));
    }

    [Test]
    public void TryExtract_NoLiteralNewlines_StillParses()
    {
        // Regression: valid JSON with proper escape sequences must still work
        const string text = """
            INBOX_MESSAGE_JSON:
            {
              "subject": "Normal",
              "from": "argus-weld",
              "body": "Line one\nLine two",
              "attachments": [],
              "actions": [{ "label": "Go", "routeMode": "start_coordinator", "prompt": "Do step 1\nDo step 2" }]
            }
            """;

        var result = InboxMessageParser.TryExtract(text, out _, out var dto);

        Assert.That(result, Is.True);
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.Body, Is.EqualTo("Line one\nLine two"));
        Assert.That(dto.Actions[0].Prompt, Is.EqualTo("Do step 1\nDo step 2"));
    }

    [Test]
    public void TryExtract_EscapedNewlinesInBodyAndLiteralNewlinesInPrompt_ParsesSuccessfully()
    {
        // body uses proper \n escapes (already valid); prompt has literal newlines (needs sanitization)
        var promptWithLiteralNewlines = "Step 1: run tests\nStep 2: commit";
        var json = $"{{\"subject\":\"Mixed\",\"from\":\"agent\",\"body\":\"Line1\\nLine2\",\"attachments\":[],\"actions\":[{{\"label\":\"Run\",\"routeMode\":\"start_coordinator\",\"prompt\":\"{promptWithLiteralNewlines}\"}}]}}";
        var text = $"INBOX_MESSAGE_JSON:\n{json}";

        var result = InboxMessageParser.TryExtract(text, out _, out var dto);

        Assert.That(result, Is.True);
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.Body, Is.EqualTo("Line1\nLine2"));
        Assert.That(dto.Actions[0].Prompt, Does.Contain("Step 1"));
        Assert.That(dto.Actions[0].Prompt, Does.Contain("Step 2"));
    }

    // ── CRLF normalisation ───────────────────────────────────────────────────

    [Test]
    public void TryExtract_CrlfLineEndings_ParsesSuccessfully() {
        var text = "Report ready.\r\n\r\nINBOX_MESSAGE_JSON:\r\n{ \"subject\": \"CRLF\", \"from\": \"argus-weld\", \"body\": \"Done\", \"attachments\": [] }";

        var result = InboxMessageParser.TryExtract(text, out var body, out var dto);

        Assert.That(result, Is.True);
        Assert.That(dto!.Subject, Is.EqualTo("CRLF"));
        Assert.That(body, Is.EqualTo("Report ready."));
    }

    // ── stress test ──────────────────────────────────────────────────────────

    [Test]
    public void TryExtract_LargeResponse_ParsesSuccessfully() {
        var prefix = new string('A', 100_000);
        var text = $"{prefix}\nINBOX_MESSAGE_JSON:\n{{ \"subject\": \"Large\", \"from\": \"argus-weld\", \"body\": \"Done\", \"attachments\": [] }}";

        var result = InboxMessageParser.TryExtract(text, out var body, out var dto);

        Assert.That(result, Is.True);
        Assert.That(dto!.Subject, Is.EqualTo("Large"));
        Assert.That(body.Length, Is.GreaterThan(99_000));
    }
}
