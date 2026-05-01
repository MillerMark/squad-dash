using System.Linq;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class HostCommandParserTests {

    // ── Basic parsing ─────────────────────────────────────────────────────────

    [Test]
    public void TryExtract_ValidBlock_ParsesCommandsAndStripsFromBody() {
        const string text = """
            Some response text here.

            HOST_COMMAND_JSON:
            [
              { "command": "get_queue_status" }
            ]
            """;

        var result = HostCommandParser.TryExtract(text, out var body, out var commands);

        Assert.That(result, Is.True);
        Assert.That(body.Trim(), Is.EqualTo("Some response text here."));
        Assert.That(commands, Has.Length.EqualTo(1));
        Assert.That(commands[0].Command, Is.EqualTo("get_queue_status"));
    }

    [Test]
    public void TryExtract_MultipleCommands_AllParsedInOrder() {
        const string text = """
            Choose what to do next.

            HOST_COMMAND_JSON:
            [
              { "command": "get_queue_status" },
              { "command": "open_panel", "parameters": { "name": "Approvals" } },
              { "command": "stop_loop" }
            ]
            """;

        var result = HostCommandParser.TryExtract(text, out _, out var commands);

        Assert.That(result, Is.True);
        Assert.That(commands, Has.Length.EqualTo(3));
        Assert.That(commands[0].Command, Is.EqualTo("get_queue_status"));
        Assert.That(commands[1].Command, Is.EqualTo("open_panel"));
        Assert.That(commands[2].Command, Is.EqualTo("stop_loop"));
    }

    [Test]
    public void TryExtract_CommandWithParameters_ParsesParametersCorrectly() {
        const string text = """
            Opening the approvals panel.

            HOST_COMMAND_JSON:
            [
              { "command": "open_panel", "parameters": { "name": "Approvals" } }
            ]
            """;

        var result = HostCommandParser.TryExtract(text, out _, out var commands);

        Assert.That(result, Is.True);
        Assert.That(commands[0].Command, Is.EqualTo("open_panel"));
        Assert.That(commands[0].Parameters, Is.Not.Null);
        Assert.That(commands[0].Parameters!["name"], Is.EqualTo("Approvals"));
    }

    // ── Non-match cases ───────────────────────────────────────────────────────

    [Test]
    public void TryExtract_NoBlock_ReturnsFalseAndBodyUnchanged() {
        const string text = "This is a normal response with no command block.";

        var result = HostCommandParser.TryExtract(text, out var body, out var commands);

        Assert.That(result, Is.False);
        Assert.That(body, Is.EqualTo(text));
        Assert.That(commands, Is.Empty);
    }

    [Test]
    public void TryExtract_EmptyText_ReturnsFalse() {
        var result = HostCommandParser.TryExtract(string.Empty, out var body, out var commands);

        Assert.That(result, Is.False);
        Assert.That(body, Is.EqualTo(string.Empty));
        Assert.That(commands, Is.Empty);
    }

    // ── Malformed / edge cases ────────────────────────────────────────────────

    [Test]
    public void TryExtract_MalformedJson_ReturnsFalse() {
        const string text = """
            Response here.

            HOST_COMMAND_JSON:
            [
              { "command": "start_loop"
            """;

        var result = HostCommandParser.TryExtract(text, out var body, out var commands);

        Assert.That(result, Is.False);
        Assert.That(commands, Is.Empty);
    }

    [Test]
    public void TryExtract_InvalidJson_ReturnsFalseAndBodyUnchanged() {
        const string text = """
            Response here.

            HOST_COMMAND_JSON:
            not valid json at all %%%
            """;

        var result = HostCommandParser.TryExtract(text, out var body, out var commands);

        Assert.That(result, Is.False);
        Assert.That(commands, Is.Empty);
    }

    [Test]
    public void TryExtract_EmptyArray_ReturnsFalse() {
        const string text = """
            Nothing to do.

            HOST_COMMAND_JSON:
            []
            """;

        var result = HostCommandParser.TryExtract(text, out _, out var commands);

        Assert.That(result, Is.False);
        Assert.That(commands, Is.Empty);
    }

    [Test]
    public void TryExtract_ArrayWithNullOrEmptyCommandEntries_SkipsInvalidReturnsValid() {
        const string text = """
            Response.

            HOST_COMMAND_JSON:
            [
              { "command": "start_loop" },
              { "command": "" },
              { "command": null }
            ]
            """;

        var result = HostCommandParser.TryExtract(text, out _, out var commands);

        Assert.That(result, Is.True);
        Assert.That(commands.All(c => !string.IsNullOrWhiteSpace(c.Command)), Is.True);
        Assert.That(commands.Any(c => c.Command == "start_loop"), Is.True);
    }

    // ── Positioning / structure ───────────────────────────────────────────────

    [Test]
    public void TryExtract_BlockWithPrecedingText_StillFindsIt() {
        const string text = """
            First paragraph.

            Second paragraph.

            HOST_COMMAND_JSON:
            [
              { "command": "get_queue_status" }
            ]
            """;

        var result = HostCommandParser.TryExtract(text, out var body, out var commands);

        Assert.That(result, Is.True);
        Assert.That(commands, Has.Length.EqualTo(1));
        Assert.That(commands[0].Command, Is.EqualTo("get_queue_status"));
        Assert.That(body, Does.Contain("First paragraph."));
        Assert.That(body, Does.Not.Contain("HOST_COMMAND_JSON:"));
    }

    [Test]
    public void TryExtract_MultipleBlocks_ReturnsFalseAsJsonSpansBothArrays() {
        // Greedy JSON matching spans both arrays + the text between them → invalid JSON → false.
        // The AI should only ever emit one HOST_COMMAND_JSON block per response.
        const string text = """
            First block:

            HOST_COMMAND_JSON:
            [
              { "command": "start_loop" }
            ]

            Second block:

            HOST_COMMAND_JSON:
            [
              { "command": "stop_loop" }
            ]
            """;

        var result = HostCommandParser.TryExtract(text, out _, out var commands);

        Assert.That(result, Is.False);
        Assert.That(commands, Is.Empty);
    }

    [Test]
    public void TryExtract_CrLfLineEndings_HandledCorrectly() {
        var text = "Response text.\r\n\r\nHOST_COMMAND_JSON:\r\n[\r\n  { \"command\": \"stop_loop\" }\r\n]\r\n";

        var result = HostCommandParser.TryExtract(text, out var body, out var commands);

        Assert.That(result, Is.True);
        Assert.That(commands, Has.Length.EqualTo(1));
        Assert.That(commands[0].Command, Is.EqualTo("stop_loop"));
        Assert.That(body, Does.Not.Contain("HOST_COMMAND_JSON:"));
    }

    // ── Parameter types ───────────────────────────────────────────────────────

    [Test]
    public void TryExtract_ParametersWithVariousStringValues_AllParsedAsStrings() {
        const string text = """
            Injecting text.

            HOST_COMMAND_JSON:
            [
              {
                "command": "inject_text",
                "parameters": {
                  "text": "Hello world",
                  "count": "42",
                  "flag": "true"
                }
              }
            ]
            """;

        var result = HostCommandParser.TryExtract(text, out _, out var commands);

        Assert.That(result, Is.True);
        var parameters = commands[0].Parameters!;
        Assert.That(parameters["text"], Is.EqualTo("Hello world"));
        Assert.That(parameters["count"], Is.EqualTo("42"));
        Assert.That(parameters["flag"], Is.EqualTo("true"));
    }

    [Test]
    public void TryExtract_UnknownExtraFieldsInJson_IgnoredGracefully() {
        const string text = """
            Response.

            HOST_COMMAND_JSON:
            [
              {
                "command": "start_loop",
                "unknownField": "some value",
                "anotherExtra": 99
              }
            ]
            """;

        var result = HostCommandParser.TryExtract(text, out _, out var commands);

        Assert.That(result, Is.True);
        Assert.That(commands[0].Command, Is.EqualTo("start_loop"));
    }

    // ── Volume ────────────────────────────────────────────────────────────────

    [Test]
    public void TryExtract_LargeArray_AllCommandsParsedCorrectly() {
        var entries = Enumerable.Range(0, 22)
            .Select(i => $"  {{ \"command\": \"cmd_{i}\" }}");
        var json = string.Join(",\n", entries);
        var text = $"Response.\n\nHOST_COMMAND_JSON:\n[\n{json}\n]\n";

        var result = HostCommandParser.TryExtract(text, out _, out var commands);

        Assert.That(result, Is.True);
        Assert.That(commands, Has.Length.EqualTo(22));
        Assert.That(commands[0].Command, Is.EqualTo("cmd_0"));
        Assert.That(commands[21].Command, Is.EqualTo("cmd_21"));
    }

    // ── Whitespace ────────────────────────────────────────────────────────────

    [Test]
    public void TryExtract_WhitespaceAroundBlock_HandledCorrectly() {
        const string text = """


            HOST_COMMAND_JSON:

            [
              { "command": "clear_approved" }
            ]

            """;

        var result = HostCommandParser.TryExtract(text, out _, out var commands);

        Assert.That(result, Is.True);
        Assert.That(commands[0].Command, Is.EqualTo("clear_approved"));
    }
}
