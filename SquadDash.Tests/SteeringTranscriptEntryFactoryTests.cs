using System.Threading;
using System.Windows;
using System.Windows.Documents;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class SteeringTranscriptEntryFactoryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 7, 14, 30, 0, TimeSpan.Zero);

    [Test]
    public void ProviderPrompt_AddsExactVisibleAcknowledgementInstructionWithoutChangingUserPrompt()
    {
        const string userPrompt = "Use the Azure adapter and retain the existing tests.";

        var providerPrompt = SteeringPromptBuilder.AddVisibleAcknowledgementInstruction(userPrompt);

        Assert.Multiple(() =>
        {
            Assert.That(providerPrompt, Does.StartWith(userPrompt + "\n\n"));
            Assert.That(providerPrompt, Does.Contain(
                "Before any further tool call or delegation, produce a user-facing assistant message in the normal conversation transcript, not hidden reasoning/thinking. " +
                "In that message, acknowledge this steering and explain in one or two sentences how it will affect your approach, " +
                "then continue working. If the steering does not change your approach, say that briefly in the same user-facing message."));
            Assert.That(providerPrompt, Does.EndWith("</SquadDash steering acknowledgement instruction>"));
        });
    }

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

    [Test, Apartment(ApartmentState.STA)]
    public void PersistedProviderPrompt_ReconstructsTranscriptSourceMetadata()
    {
        var source = new TranscriptAttachmentSource(
            TurnIndex: 3,
            Timestamp: "2026-06-17T10:00:00.0000000Z",
            ConversationHint: "Queue investigation");
        var transcriptBlock = AttachmentBlockFormatter.BuildTranscriptAttachmentBlock(
            "Transcript",
            "selected quote",
            source);
        var providerPrompt =
            "The user has attached the following context items. Please refer to them as needed.\n" +
            transcriptBlock + "\n\n" +
            "Continue.";

        var display = SteeringTranscriptEntryFactory.ParsePersistedPrompt(providerPrompt);

        Assert.Multiple(() =>
        {
            Assert.That(display.Attachments, Has.Count.EqualTo(1));
            Assert.That(display.Attachments[0].TranscriptQuote, Is.EqualTo("selected quote"));
            Assert.That(display.Attachments[0].TranscriptSource, Is.EqualTo(source));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ResolveInsertionIndex_NestedSteeringParagraph_SortsByDocumentOrder()
    {
        var thread = new TranscriptThreadState("doc-order", TranscriptThreadKind.Agent, "Agent", Now);

        var prompt1 = new Paragraph(new Run("First user prompt"));
        thread.Document.Blocks.Add(prompt1);
        var section1 = new Section();
        thread.Document.Blocks.Add(section1);

        var prompt2 = new Paragraph(new Run("Second user prompt"));
        thread.Document.Blocks.Add(prompt2);
        var section2 = new Section();
        thread.Document.Blocks.Add(section2);

        var existing = new[] { prompt1, prompt2 };

        // Steering typed during turn 1 is nested in section1 (before prompt2 in document order).
        var steeringMid = new Paragraph(new Run("Steering for turn 1"));
        section1.Blocks.Add(steeringMid);
        var midIndex = PromptRegistrationMath.ResolveInsertionIndex(
            thread.Document.ContentStart, steeringMid, existing);

        // Steering typed during the latest turn is nested in section2 (after both prompts).
        var steeringLatest = new Paragraph(new Run("Steering for turn 2"));
        section2.Blocks.Add(steeringLatest);
        var latestIndex = PromptRegistrationMath.ResolveInsertionIndex(
            thread.Document.ContentStart, steeringLatest, existing);

        Assert.Multiple(() =>
        {
            Assert.That(midIndex, Is.EqualTo(1),
                "A nested mid-conversation steering paragraph sorts between the two prompts.");
            Assert.That(latestIndex, Is.EqualTo(2),
                "Latest-turn steering appends at the end so Shift+click 'latest' lands on it.");
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void RegisteredSteering_BecomesNavStopInDocumentOrder()
    {
        var thread = new TranscriptThreadState("nav-order", TranscriptThreadKind.Agent, "Agent", Now);

        var prompt1 = new Paragraph(new Run("Prompt one"));
        thread.Document.Blocks.Add(prompt1);
        var section1 = new Section();
        thread.Document.Blocks.Add(section1);
        var prompt2 = new Paragraph(new Run("Prompt two"));
        thread.Document.Blocks.Add(prompt2);

        thread.PromptParagraphs.Add(new PromptEntry(prompt1, Now, "Prompt one"));
        thread.PromptParagraphs.Add(new PromptEntry(prompt2, Now, "Prompt two"));

        // Register a steering entry nested in turn 1 the same way MainWindow does.
        var steering = new Paragraph(new Run("Steering mid"));
        section1.Blocks.Add(steering);
        var existing = thread.PromptParagraphs.Select(entry => entry.Paragraph).ToArray();
        var index = PromptRegistrationMath.ResolveInsertionIndex(
            thread.Document.ContentStart, steering, existing);
        thread.PromptParagraphs.Insert(index, new PromptEntry(steering, Now, "Steering mid"));

        var documentStart = thread.Document.ContentStart;
        var offsets = thread.PromptParagraphs
            .Select(entry => documentStart.GetOffsetToPosition(entry.Paragraph.ContentStart))
            .ToArray();

        // Model GetScrollBasedNavState: project the (list-ordered) prompts onto spaced
        // document-space Ys and confirm the registered steering entry is the nearest stop
        // both above and below a viewport sitting between prompt one and the steering entry.
        var absYs = new[] { 100.0, 500.0, 900.0 };
        var navState = PromptScrollNavMath.Compute(absYs, viewportTop: 300.0, scrollableMax: 2000.0);

        Assert.Multiple(() =>
        {
            Assert.That(thread.PromptParagraphs.Select(entry => entry.Title),
                Is.EqualTo(new[] { "Prompt one", "Steering mid", "Prompt two" }));
            Assert.That(offsets, Is.Ordered.Ascending,
                "The list the nav geometry walks must stay in ascending document order.");
            Assert.That(navState.NearestAboveIdx, Is.EqualTo(0));
            Assert.That(navState.NearestBelowIdx, Is.EqualTo(1),
                "↓ lands on the registered steering entry — it is no longer skipped.");
        });
    }

    private static TranscriptThreadState CreateThreadWithTurn(
        string id,
        string title,
        out TranscriptTurnView turn)
    {        var thread = new TranscriptThreadState(id, TranscriptThreadKind.Agent, title, Now);
        var narrative = new Section();
        thread.Document.Blocks.Add(narrative);
        turn = new TranscriptTurnView(thread, "original prompt", Now, narrative, [narrative]);
        thread.CurrentTurn = turn;
        return thread;
    }
}
