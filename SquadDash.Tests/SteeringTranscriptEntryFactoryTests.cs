using System.Threading;
using System.Windows;
using System.Windows.Documents;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class SteeringTranscriptEntryFactoryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 7, 14, 30, 0, TimeSpan.Zero);

    [Test, Apartment(ApartmentState.STA)]
    public void Append_WhenTurnFinalizesDuringAcknowledgement_AppendsToCapturedTargetOnly()
    {
        var targetThread = CreateThreadWithTurn("target", "Target agent", out var targetTurn);
        var otherThread = CreateThreadWithTurn("other", "Other agent", out var otherTurn);
        targetTurn.ResponseTextBuilder.Append("Existing model output");
        targetThread.CurrentTurn = null; // The positive acknowledgement arrived after finalization.

        const string prompt = "Use the new API.\nKeep the compatibility shim.";
        const string providerPrompt = "<attachment type=\"note\" title=\"Constraint\">\nDetails\n</attachment>\n\n" + prompt;
        var attachments = new[]
        {
            new FollowUpAttachment(
                string.Empty,
                "Constraint",
                null,
                ContentBlock: "<attachment type=\"note\" title=\"Constraint\">\nDetails\n</attachment>")
        };
        var paragraph = SteeringTranscriptEntryFactory.CreateParagraph(
            prompt,
            attachments,
            historicalAttachmentCount: null,
            openLink: null,
            openAttachments: null,
            attachmentToolTip: null);

        var entry = SteeringTranscriptEntryFactory.Append(
            targetTurn,
            providerPrompt,
            paragraph,
            Now,
            sequence: 4);

        Assert.Multiple(() =>
        {
            Assert.That(targetTurn.HostNotices, Has.Count.EqualTo(1));
            Assert.That(targetTurn.HostNotices[0], Is.SameAs(entry));
            Assert.That(targetTurn.HostNotices[0].Sequence, Is.EqualTo(4));
            Assert.That(targetTurn.NarrativeSection.Blocks.LastBlock, Is.SameAs(paragraph));
            Assert.That(targetTurn.ResponseTextBuilder.ToString(), Is.EqualTo("Existing model output"));
            Assert.That(targetTurn.ResponseEntries, Is.Empty);
            Assert.That(otherTurn.HostNotices, Is.Empty);
            Assert.That(otherTurn.NarrativeSection.Blocks, Is.Empty);
            Assert.That(otherThread.CurrentTurn, Is.SameAs(otherTurn));
        });

        Assert.That(
            SteeringTranscriptEntryFactory.TryDecodeHostNotice(entry.Text, out var decoded),
            Is.True);
        Assert.That(decoded, Is.EqualTo(providerPrompt));
        Assert.Multiple(() =>
        {
            Assert.That(entry.GetCopyText(), Does.StartWith("↪️ Steering: Use the new API."));
            Assert.That(entry.GetCopyText(), Does.Contain("📎 1 attachment"));
            Assert.That(entry.GetCopyText(), Does.Not.Contain("[[squaddash-steering:v1]]"));
            Assert.That(entry.GetCopyText(), Does.Not.Contain("<attachment type="));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void CreateParagraph_RendersTruthfulMarkerFullPromptAttachmentAndDictation()
    {
        const string prompt =
            "Follow [the design](https://example.test/design).\nPreserve both providers.\n" +
            "(some or all of this prompt was dictated by voice)";
        var attachments = new[]
        {
            new FollowUpAttachment(string.Empty, "Design note", null, ContentBlock: "details")
        };
        IReadOnlyList<FollowUpAttachment>? opened = null;

        var paragraph = SteeringTranscriptEntryFactory.CreateParagraph(
            prompt,
            attachments,
            historicalAttachmentCount: null,
            openLink: null,
            openAttachments: selected => opened = selected,
            attachmentToolTip: "Open attachments");
        var text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
        var links = paragraph.Inlines.OfType<Hyperlink>().ToArray();
        var attachmentLink = links.Single(link =>
            new TextRange(link.ContentStart, link.ContentEnd).Text.Contains("attachment", StringComparison.Ordinal));

        attachmentLink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("↪️ Steering:"));
            Assert.That(text, Does.Contain("Follow the design."));
            Assert.That(text, Does.Contain("Preserve both providers."));
            Assert.That(text, Does.Contain("📎 1 attachment"));
            Assert.That(text, Does.Contain("(some or all of this prompt was dictated by voice)"));
            Assert.That(text, Does.Not.Contain("agent read").IgnoreCase);
            Assert.That(paragraph.Margin, Is.EqualTo(new Thickness(0, 0, 0, 8)));
            Assert.That(opened, Is.SameAs(attachments));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void PersistedProviderPrompt_ReconstructsPromptAndAttachmentViewerData()
    {
        var typedBlock = AttachmentBlockFormatter.BuildTypedAttachmentBlock(
            "note",
            "Migration constraint",
            "Keep both adapters.");
        var providerPrompt =
            "The user has attached the following context items. Please refer to them as needed.\n" +
            typedBlock + "\n" +
            "[Attached image: C:\\temp\\provider-map.png]\n\n" +
            "Use this context for the implementation.";

        var encoded = SteeringTranscriptEntryFactory.EncodeHostNotice(providerPrompt);
        Assert.That(
            SteeringTranscriptEntryFactory.TryDecodeHostNotice(encoded, out var decoded),
            Is.True);

        var display = SteeringTranscriptEntryFactory.ParsePersistedPrompt(decoded);

        Assert.Multiple(() =>
        {
            Assert.That(display.Prompt, Is.EqualTo("Use this context for the implementation."));
            Assert.That(display.AttachmentCount, Is.EqualTo(2));
            Assert.That(display.Attachments, Has.Count.EqualTo(2));
            Assert.That(display.Attachments[0].Description, Is.EqualTo("Migration constraint"));
            Assert.That(display.Attachments[0].ContentBlock, Is.EqualTo(typedBlock));
            Assert.That(display.Attachments[1].ImagePath, Is.EqualTo("C:\\temp\\provider-map.png"));
        });
    }

    private static TranscriptThreadState CreateThreadWithTurn(
        string id,
        string title,
        out TranscriptTurnView turn)
    {
        var thread = new TranscriptThreadState(id, TranscriptThreadKind.Agent, title, Now);
        var narrative = new Section();
        thread.Document.Blocks.Add(narrative);
        turn = new TranscriptTurnView(thread, "original prompt", Now, narrative, [narrative]);
        thread.CurrentTurn = turn;
        return thread;
    }
}
