namespace SquadDash.Tests;

[TestFixture]
internal sealed class ApprovalGroupParserTests {
    [Test]
    public void Parse_SingleLineBlock_ReturnsAssignment() {
        const string text = """
            Committed: abc1234

            APPROVAL_GROUP_JSON:
            {"sha":"abc1234","group":"UI Polish"}
            """;

        var result = ApprovalGroupParser.Parse(text);

        Assert.Multiple(() => {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Sha, Is.EqualTo("abc1234"));
            Assert.That(result[0].Group, Is.EqualTo("UI Polish"));
        });
    }

    [Test]
    public void Parse_PrettyPrintedFencedBlock_ReturnsAssignment() {
        const string text = """
            Committed: def5678

            APPROVAL_GROUP_JSON:
            ```json
            {
              "group": "Loop Reliability",
              "sha": "def5678"
            }
            ```
            """;

        var result = ApprovalGroupParser.Parse(text);

        Assert.Multiple(() => {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Sha, Is.EqualTo("def5678"));
            Assert.That(result[0].Group, Is.EqualTo("Loop Reliability"));
        });
    }

    [Test]
    public void Parse_MalformedBlock_SkipsAssignment() {
        const string text = """
            APPROVAL_GROUP_JSON:
            {"sha":"abc1234","group":
            """;

        var result = ApprovalGroupParser.Parse(text);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Parse_LegacyEscapedQuoteBlock_ReturnsAssignment() {
        const string text = """
            APPROVAL_GROUP_JSON:
            {\"sha\":\"abc1234\",\"group\":\"Guided Tour\"}
            """;

        var result = ApprovalGroupParser.Parse(text);

        Assert.Multiple(() => {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Sha, Is.EqualTo("abc1234"));
            Assert.That(result[0].Group, Is.EqualTo("Guided Tour"));
        });
    }
}
