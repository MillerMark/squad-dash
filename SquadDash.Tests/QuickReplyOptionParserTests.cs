using System.Linq;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
public sealed class QuickReplyOptionParserTests {
    [Test]
    public void TryExtract_ParsesQuickReplyJsonMetadata_AndRemovesItFromBody() {
        const string text = """
            Choose the next step.
            QUICK_REPLIES_JSON:
            [
              {
                "label": "Start Orion's architectural backlog",
                "routeMode": "start_named_agent",
                "targetAgent": "orion-vale",
                "reason": "Architectural backlog ownership belongs to Orion Vale."
              },
              {
                "label": "Done for now",
                "routeMode": "done",
                "reason": "No follow-up work requested."
              }
            ]
            """;

        var parsed = QuickReplyOptionParser.TryExtractWithMetadata(text, out var body, out QuickReplyOptionMetadata[] options);

        Assert.That(parsed, Is.True);
        Assert.That(body, Is.EqualTo("Choose the next step."));
        Assert.That(options.Select(option => option.Label), Is.EqualTo(new[] {
            "Start Orion's architectural backlog",
            "Done for now"
        }));
        Assert.That(options[0].RouteMode, Is.EqualTo("start_named_agent"));
        Assert.That(options[0].TargetAgent, Is.EqualTo("orion-vale"));
    }

    [Test]
    public void TryExtract_ParsesPlainBracketedOptions() {
        const string text = """
            Which would you like to tackle?
            [Tiered pricing] [Unit tests]
            """;

        var parsed = QuickReplyOptionParser.TryExtract(text, out var body, out var options);

        Assert.That(parsed, Is.True);
        Assert.That(body, Is.EqualTo("Which would you like to tackle?"));
        Assert.That(options, Is.EqualTo(new[] { "Tiered pricing", "Unit tests" }));
    }

    [Test]
    public void TryExtract_ParsesSingleBoldWrappedOptionLine() {
        const string text = """
            Which would you like to tackle?
            **[Tiered pricing] [Unit tests]**
            """;

        var parsed = QuickReplyOptionParser.TryExtract(text, out var body, out var options);

        Assert.That(parsed, Is.True);
        Assert.That(body, Is.EqualTo("Which would you like to tackle?"));
        Assert.That(options, Is.EqualTo(new[] { "Tiered pricing", "Unit tests" }));
    }

    [Test]
    public void TryExtract_ParsesIndividuallyBoldedOptions() {
        const string text = """
            Which would you like to tackle?
            **[Tiered pricing]** **[Unit tests]**
            """;

        var parsed = QuickReplyOptionParser.TryExtract(text, out var body, out var options);

        Assert.That(parsed, Is.True);
        Assert.That(body, Is.EqualTo("Which would you like to tackle?"));
        Assert.That(options, Is.EqualTo(new[] { "Tiered pricing", "Unit tests" }));
    }

    [Test]
    public void TryExtract_ParsesDraftRouteMode_WithPromptField() {
        const string text = """
            Click the button below to pre-fill your answers.
            QUICK_REPLIES_JSON:
            [
              {
                "label": "Answer these questions",
                "routeMode": "draft",
                "prompt": "1. My answer to Q1\n2. My answer to Q2\n3. My answer to Q3",
                "reason": "Pre-fill the answer template."
              }
            ]
            """;

        var parsed = QuickReplyOptionParser.TryExtractWithMetadata(text, out var body, out QuickReplyOptionMetadata[] options);

        Assert.That(parsed, Is.True);
        Assert.That(body, Is.EqualTo("Click the button below to pre-fill your answers."));
        Assert.That(options, Has.Length.EqualTo(1));
        Assert.That(options[0].Label, Is.EqualTo("Answer these questions"));
        Assert.That(options[0].RouteMode, Is.EqualTo("draft"));
        Assert.That(options[0].Prompt, Is.EqualTo("1. My answer to Q1\n2. My answer to Q2\n3. My answer to Q3"));
    }

    [Test]
    public void TryExtract_PromptField_IsNullWhenAbsent() {
        const string text = """
            Choose an option.
            QUICK_REPLIES_JSON:
            [
              {
                "label": "Continue",
                "routeMode": "continue_current_agent",
                "reason": "Keep going."
              }
            ]
            """;

        var parsed = QuickReplyOptionParser.TryExtractWithMetadata(text, out _, out QuickReplyOptionMetadata[] options);

        Assert.That(parsed, Is.True);
        Assert.That(options[0].Prompt, Is.Null);
    }
}
