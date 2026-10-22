using System;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Documents;
using SquadDash;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class TranscriptAttachmentSourceFactoryTests
{
    [Test, Apartment(ApartmentState.STA)]
    public void FromSelection_UserPromptSelection_UsesUserRoleAndPromptMetadata()
    {
        var timestamp = new DateTimeOffset(2026, 6, 17, 10, 0, 0, TimeSpan.Zero);
        var thread = CreateThread("user-thread");
        var paragraph = new Paragraph(new Run("Mark: Investigate docking trace"));
        thread.Document.Blocks.Add(paragraph);
        thread.PromptParagraphs.Add(new PromptEntry(paragraph, timestamp, "Investigate docking trace"));

        var source = BuildSourceForFullSelection(thread, paragraph);

        Assert.That(source, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(source!.Role, Is.EqualTo("user"));
            Assert.That(source.TurnIndex, Is.EqualTo(0));
            Assert.That(source.Timestamp, Is.EqualTo(timestamp.ToString("O")));
            Assert.That(source.ConversationHint, Is.EqualTo("Investigate docking trace"));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FromSelection_ResponseSelection_UsesAssistantRoleWithTurnAnchorMetadata()
    {
        var timestamp = new DateTimeOffset(2026, 6, 17, 10, 5, 0, TimeSpan.Zero);
        var thread = CreateThread("assistant-thread");
        var prompt = new Paragraph(new Run("Mark: Give me a summary"));
        thread.Document.Blocks.Add(prompt);
        thread.PromptParagraphs.Add(new PromptEntry(prompt, timestamp, "Give me a summary"));

        var narrative = new Section();
        thread.Document.Blocks.Add(narrative);
        var turn = new TranscriptTurnView(thread, "Give me a summary", timestamp, narrative, [narrative]);
        thread.CurrentTurn = turn;

        var responseSection = new Section();
        var responseParagraph = new Paragraph(new Run("Assistant: Here is the summary."));
        responseSection.Blocks.Add(responseParagraph);
        narrative.Blocks.Add(responseSection);
        turn.ResponseEntries.Add(new TranscriptResponseEntry(turn, 1, responseSection));

        var source = BuildSourceForFullSelection(thread, responseParagraph);

        Assert.That(source, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(source!.Role, Is.EqualTo("assistant"));
            Assert.That(source.TurnIndex, Is.EqualTo(0));
            Assert.That(source.Timestamp, Is.EqualTo(timestamp.ToString("O")));
            Assert.That(source.ConversationHint, Is.EqualTo("Give me a summary"));
        });
        thread.CurrentTurn = null;
        Assert.That(TranscriptAttachmentSourceFactory.FromSelection(thread, thread.Document,
            responseParagraph.ContentStart, responseParagraph.ContentEnd), Is.EqualTo(source),
            "Rendered historical responses retain attribution after the current turn changes.");
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FromSelection_UserTextThatResemblesSystemMarker_RemainsUser()
    {
        var thread = CreateThread("user-marker");
        var paragraph = new Paragraph(new Run(SteeringTranscriptEntryFactory.SystemMarkerText + ": quoted text"));
        thread.Document.Blocks.Add(paragraph);
        thread.PromptParagraphs.Add(new PromptEntry(paragraph, DateTimeOffset.UtcNow));
        Assert.That(BuildSourceForFullSelection(thread, paragraph)!.Role, Is.EqualTo("user"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FromSelection_PromptAndUnknownContent_DoesNotMisattributeUnknownContent()
    {
        var thread = CreateThread("mixed-unknown");
        var prompt = new Paragraph(new Run("question"));
        var unknown = new Paragraph(new Run("tool output"));
        thread.Document.Blocks.Add(prompt);
        thread.Document.Blocks.Add(unknown);
        thread.PromptParagraphs.Add(new PromptEntry(prompt, DateTimeOffset.UtcNow, "question"));
        var source = BuildSourceForSelectionRange(thread, prompt.ContentStart, unknown.ContentEnd);
        Assert.That(source!.Role, Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FromSelection_SystemSteeringSelection_UsesSystemRole()
    {
        var timestamp = new DateTimeOffset(2026, 6, 17, 10, 10, 0, TimeSpan.Zero);
        var thread = CreateThread("system-thread");
        var paragraph = SteeringTranscriptEntryFactory.CreateParagraph(
            "No write call observed.", [], null, null, null, null, isSystem: true);
        thread.Document.Blocks.Add(paragraph);
        thread.PromptParagraphs.Add(new PromptEntry(paragraph, timestamp, null));

        var source = BuildSourceForFullSelection(thread, paragraph);

        Assert.That(source, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(source!.Role, Is.EqualTo("system"));
            Assert.That(source.TurnIndex, Is.EqualTo(0));
            Assert.That(source.Timestamp, Is.EqualTo(timestamp.ToString("O")));
            Assert.That(source.ConversationHint, Is.Null);
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FromSelection_NoStructuralSource_ReturnsNull()
    {
        var thread = CreateThread("unknown-thread");
        var paragraph = new Paragraph(new Run("Detached text without prompt metadata"));
        thread.Document.Blocks.Add(paragraph);

        var source = BuildSourceForFullSelection(thread, paragraph);

        Assert.That(source, Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FromSelection_CrossRoleSelection_SuppressesRoleButKeepsSharedTurnAnchor()
    {
        var timestamp = new DateTimeOffset(2026, 6, 17, 10, 15, 0, TimeSpan.Zero);
        var thread = CreateThread("cross-role-thread");
        var prompt = new Paragraph(new Run("Mark: Compare both approaches"));
        thread.Document.Blocks.Add(prompt);
        thread.PromptParagraphs.Add(new PromptEntry(prompt, timestamp, "Compare both approaches"));

        var narrative = new Section();
        thread.Document.Blocks.Add(narrative);
        var turn = new TranscriptTurnView(thread, "Compare both approaches", timestamp, narrative, [narrative]);
        thread.CurrentTurn = turn;

        var responseSection = new Section();
        var responseParagraph = new Paragraph(new Run("Assistant: Here are the tradeoffs."));
        responseSection.Blocks.Add(responseParagraph);
        narrative.Blocks.Add(responseSection);
        turn.ResponseEntries.Add(new TranscriptResponseEntry(turn, 1, responseSection));

        var source = BuildSourceForSelectionRange(
            thread,
            prompt.ContentStart,
            responseParagraph.ContentEnd);

        Assert.That(source, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(source!.Role, Is.Null);
            Assert.That(source.TurnIndex, Is.EqualTo(0));
            Assert.That(source.Timestamp, Is.EqualTo(timestamp.ToString("O")));
            Assert.That(source.ConversationHint, Is.EqualTo("Compare both approaches"));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FromSelection_CrossTurnSelection_SuppressesTurnSpecificFields()
    {
        var t0 = new DateTimeOffset(2026, 6, 17, 10, 20, 0, TimeSpan.Zero);
        var t1 = new DateTimeOffset(2026, 6, 17, 10, 21, 0, TimeSpan.Zero);
        var thread = CreateThread("cross-turn-thread");
        var first = new Paragraph(new Run("Mark: First question"));
        var second = new Paragraph(new Run("Mark: Second question"));
        thread.Document.Blocks.Add(first);
        thread.Document.Blocks.Add(second);
        thread.PromptParagraphs.Add(new PromptEntry(first, t0, "First question"));
        thread.PromptParagraphs.Add(new PromptEntry(second, t1, "Second question"));

        var source = BuildSourceForSelectionRange(thread, first.ContentStart, second.ContentEnd);

        Assert.That(source, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(source!.Role, Is.EqualTo("user"));
            Assert.That(source.TurnIndex, Is.Null);
            Assert.That(source.Timestamp, Is.Null);
            Assert.That(source.ConversationHint, Is.Null);
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FromSelection_ForeignDocument_ReturnsNull()
    {
        var thread = CreateThread("foreign-doc-thread");
        var prompt = new Paragraph(new Run("Mark: Local thread prompt"));
        thread.Document.Blocks.Add(prompt);
        thread.PromptParagraphs.Add(new PromptEntry(prompt, DateTimeOffset.UtcNow, "Local thread prompt"));

        var foreign = new FlowDocument(new Paragraph(new Run("Other document")));
        var source = InvokeFromSelection(
            thread,
            foreign,
            foreign.ContentStart,
            foreign.ContentEnd);

        Assert.That(source, Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FromSelection_ForeignTextPointersInGuardedDocument_DoesNotThrowAndReturnsNull()
    {
        var thread = CreateThread("foreign-pointer-thread");
        var prompt = new Paragraph(new Run("Mark: Local thread prompt"));
        thread.Document.Blocks.Add(prompt);
        thread.PromptParagraphs.Add(new PromptEntry(prompt, DateTimeOffset.UtcNow, "Local thread prompt"));

        var foreign = new FlowDocument(new Paragraph(new Run("Other document")));
        var source = InvokeFromSelection(
            thread,
            thread.Document,
            foreign.ContentStart,
            foreign.ContentEnd);

        Assert.That(source, Is.Null);
    }

    private static TranscriptThreadState CreateThread(string id) =>
        new(id, TranscriptThreadKind.Coordinator, "Coordinator", DateTimeOffset.UtcNow);

    private static TranscriptAttachmentSource? BuildSourceForFullSelection(
        TranscriptThreadState thread,
        TextElement element)
    {
        var rtb = new RichTextBox { Document = thread.Document };
        rtb.Selection.Select(element.ContentStart, element.ContentEnd);
        return InvokeFromSelection(
            thread,
            rtb.Document,
            rtb.Selection.Start,
            rtb.Selection.End);
    }

    private static TranscriptAttachmentSource? BuildSourceForSelectionRange(
        TranscriptThreadState thread,
        TextPointer start,
        TextPointer end)
    {
        var rtb = new RichTextBox { Document = thread.Document };
        rtb.Selection.Select(start, end);
        return InvokeFromSelection(
            thread,
            rtb.Document,
            rtb.Selection.Start,
            rtb.Selection.End);
    }

    private static TranscriptAttachmentSource? InvokeFromSelection(
        TranscriptThreadState thread,
        FlowDocument document,
        TextPointer start,
        TextPointer end)
        => TranscriptAttachmentSourceFactory.FromSelection(thread, document, start, end);
}
