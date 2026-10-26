using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;

namespace SquadDash;

internal static class SteeringPromptBuilder
{
    internal const string VisibleAcknowledgementInstruction =
        "Before any further tool call or delegation, produce a user-facing assistant message in the normal conversation transcript, not hidden reasoning/thinking. " +
        "In that message, acknowledge this steering and explain in one or two sentences how it will affect your approach, " +
        "then continue working. If the steering does not change your approach, say that briefly in the same user-facing message.";

    internal static string AddVisibleAcknowledgementInstruction(string providerPrompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerPrompt);
        return providerPrompt.TrimEnd() +
               "\n\n<SquadDash steering acknowledgement instruction>\n" +
               VisibleAcknowledgementInstruction +
               "\n</SquadDash steering acknowledgement instruction>";
    }
}

/// <summary>
/// Renders and persists host-owned feedback for a steering message accepted by the
/// provider runtime. The entry deliberately says "accepted" rather than implying
/// that the model has already read or acted on the message.
/// </summary>
internal static class SteeringTranscriptEntryFactory
{
    internal const string MarkerText = "↪️ Steering";
    internal const string SystemMarkerText = "↪️ System steering";
    private const string PersistencePrefix = "[[squaddash-steering:v1]]\n";
    private const string SystemPersistencePrefix = "[[squaddash-system-steering:v1]]\n";
    private const string VoiceAnnotation = "\n(some or all of this prompt was dictated by voice)";

    internal sealed record PersistedDisplay(
        string Prompt,
        IReadOnlyList<FollowUpAttachment> Attachments,
        int AttachmentCount);

    internal static string EncodeHostNotice(string providerPrompt, bool isSystem = false) =>
        (isSystem ? SystemPersistencePrefix : PersistencePrefix) + providerPrompt;

    internal static bool TryDecodeHostNotice(string text, out string providerPrompt)
        => TryDecodeHostNotice(text, out providerPrompt, out _);

    internal static bool TryDecodeHostNotice(
        string text,
        out string providerPrompt,
        out bool isSystem)
    {
        if (text.StartsWith(SystemPersistencePrefix, StringComparison.Ordinal))
        {
            providerPrompt = text[SystemPersistencePrefix.Length..];
            isSystem = true;
            return true;
        }

        if (text.StartsWith(PersistencePrefix, StringComparison.Ordinal))
        {
            providerPrompt = text[PersistencePrefix.Length..];
            isSystem = false;
            return true;
        }

        providerPrompt = string.Empty;
        isSystem = false;
        return false;
    }

    internal static TranscriptHostNoticeEntry Append(
        TranscriptTurnView turn,
        string providerPrompt,
        Paragraph paragraph,
        DateTimeOffset occurredAt,
        int sequence,
        bool isSystem = false)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(paragraph);

        var entry = new TranscriptHostNoticeEntry(
            sequence,
            EncodeHostNotice(providerPrompt, isSystem),
            occurredAt);

        // The caller captures the addressed turn before awaiting the SDK ack. Appending to
        // that object (rather than OwnerThread.CurrentTurn) keeps a late positive ack on the
        // exact turn it steered even when lifecycle handling has already finalized the turn.
        turn.HostNotices.Add(entry);
        turn.NarrativeSection.Blocks.Add(paragraph);

        return entry;
    }

    internal static Paragraph CreateParagraph(
        string prompt,
        IReadOnlyList<FollowUpAttachment> attachments,
        int? historicalAttachmentCount,
        Action<string>? openLink,
        Action<IReadOnlyList<FollowUpAttachment>>? openAttachments,
        object? attachmentToolTip,
        bool isSystem = false)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(attachments);

        var hasDictation = prompt.EndsWith(VoiceAnnotation, StringComparison.Ordinal);
        var promptBody = hasDictation ? prompt[..^VoiceAnnotation.Length] : prompt;
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
        TranscriptAttachmentSourceFactory.SetRole(paragraph, isSystem ? "system" : "user");

        var prefix = new Run((isSystem ? SystemMarkerText : MarkerText) + ": ") { FontWeight = FontWeights.SemiBold };
        prefix.SetResourceReference(TextElement.ForegroundProperty, "UserPromptPrefixText");
        paragraph.Inlines.Add(prefix);
        AppendPromptBodyInlines(
            paragraph.Inlines,
            promptBody,
            FontWeights.SemiBold,
            "UserPromptText",
            openLink);

        AppendPromptSupplements(
            paragraph.Inlines,
            attachments,
            historicalAttachmentCount,
            hasDictation,
            openAttachments,
            attachmentToolTip);
        return paragraph;
    }

    /// <summary>
    /// Shared prompt-body renderer used by ordinary prompts and steering feedback so
    /// link typography and behavior remain identical.
    /// </summary>
    internal static void AppendPromptBodyInlines(
        InlineCollection inlines,
        string text,
        FontWeight fontWeight,
        string colorKey,
        Action<string>? openLink)
    {
        var pattern = new Regex(@"\[([^\]]+)\]\(([^)]+)\)");
        var lastEnd = 0;
        foreach (Match match in pattern.Matches(text))
        {
            if (match.Index > lastEnd)
            {
                var run = new Run(text[lastEnd..match.Index]) { FontWeight = fontWeight };
                run.SetResourceReference(TextElement.ForegroundProperty, colorKey);
                inlines.Add(run);
            }

            var capturedUrl = match.Groups[2].Value;
            var link = new Hyperlink(new Run(match.Groups[1].Value))
            {
                FontWeight = fontWeight,
                TextDecorations = null,
                Cursor = Cursors.Hand,
            };
            link.SetResourceReference(TextElement.ForegroundProperty, "ActionLinkText");
            if (openLink is not null)
                link.Click += (_, _) => openLink(capturedUrl);
            inlines.Add(link);
            lastEnd = match.Index + match.Length;
        }

        if (lastEnd < text.Length)
        {
            var run = new Run(text[lastEnd..]) { FontWeight = fontWeight };
            run.SetResourceReference(TextElement.ForegroundProperty, colorKey);
            inlines.Add(run);
        }
    }

    /// <summary>
    /// Shared attachment/dictation footer used by ordinary prompts and steering
    /// feedback. Historical entries may know a count without retaining attachment data.
    /// </summary>
    internal static void AppendPromptSupplements(
        InlineCollection inlines,
        IReadOnlyList<FollowUpAttachment> attachments,
        int? historicalAttachmentCount,
        bool hasDictation,
        Action<IReadOnlyList<FollowUpAttachment>>? openAttachments,
        object? attachmentToolTip)
    {
        var attachmentCount = historicalAttachmentCount ?? attachments.Count;
        var hasAttachments = attachmentCount > 0;
        if (!hasAttachments && !hasDictation)
            return;

        if (inlines.Count > 0)
            inlines.Add(new Run("\n") { FontWeight = FontWeights.Normal });
        if (hasAttachments)
        {
            var linkText = BuildAttachmentSummary(attachmentCount);
            var link = new Hyperlink(new Run(linkText))
            {
                FontWeight = FontWeights.Normal,
                TextDecorations = null,
                Cursor = Cursors.Hand,
                ToolTip = attachmentToolTip,
            };
            link.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
            if (openAttachments is not null && attachments.Count > 0)
                link.Click += (_, _) => openAttachments(attachments);
            inlines.Add(link);
        }

        if (hasDictation)
        {
            if (hasAttachments)
                inlines.Add(new Run("  ") { FontWeight = FontWeights.Normal });
            var voiceRun = new Run("(some or all of this prompt was dictated by voice)")
            {
                FontWeight = FontWeights.Normal,
            };
            voiceRun.SetResourceReference(TextElement.ForegroundProperty, "VoiceAnnotationText");
            inlines.Add(voiceRun);
        }
    }

    internal static string BuildAttachmentSummary(int count) =>
        count == 1 ? "📎 1 attachment" : $"📎 {count} attachments";

    internal static PersistedDisplay ParsePersistedPrompt(string providerPrompt) =>
        ParsePromptForDisplay(providerPrompt);

    internal static PersistedDisplay ParsePromptForDisplay(
        string providerPrompt,
        IReadOnlyList<FollowUpAttachment>? pendingAttachments = null)
    {
        ArgumentNullException.ThrowIfNull(providerPrompt);

        var header = AttachmentBlockFormatter.ParseAttachmentHeaders(providerPrompt);
        var bodyStart = header?.BodyStart ??
            (providerPrompt.StartsWith("<<<SQUADCLIP:", StringComparison.Ordinal)
                ? StripClipboardFenceHeader(providerPrompt)
                : -1);
        if (bodyStart < 0)
            return new PersistedDisplay(providerPrompt, pendingAttachments ?? [], pendingAttachments?.Count ?? 0);

        var prompt = providerPrompt[bodyStart..];
        IReadOnlyList<FollowUpAttachment> attachments;
        if (pendingAttachments is { Count: > 0 })
        {
            attachments = pendingAttachments;
        }
        else
        {
            var reconstructed = new List<FollowUpAttachment>();
            if (header is not null)
            {
                foreach (var item in header.Items)
                {
                    const string imagePrefix = "[Attached image: ";
                    const string quotePrefix = "Regarding this section of the transcript: \"";
                    reconstructed.Add(item.StartsWith("<attachment ", StringComparison.Ordinal)
                        ? BuildHistoricalTypedAttachment(item)
                        : item.StartsWith(imagePrefix, StringComparison.Ordinal)
                            ? new FollowUpAttachment(string.Empty, "Image", null, ImagePath: item[imagePrefix.Length..^1])
                            : item.StartsWith(quotePrefix, StringComparison.Ordinal)
                                ? new FollowUpAttachment(string.Empty, "Transcript", null, TranscriptQuote: item[quotePrefix.Length..^1])
                                : new FollowUpAttachment(string.Empty, "Attachment", item));
                }
            }
            attachments = reconstructed;
        }

        var attachmentCount = pendingAttachments is { Count: > 0 }
            ? pendingAttachments.Count
            : header?.Count ?? 1; // The legacy clipboard fence contains one attachment.
        var hasDictation = prompt.EndsWith(VoiceAnnotation, StringComparison.Ordinal);
        var body = hasDictation ? prompt[..^VoiceAnnotation.Length] : prompt;
        // A copied display may already contain its sole summary. Only replace an exact,
        // standalone summary after a parsed header, never matching lines inside user prose.
        if (string.Equals(body.TrimEnd('\r', '\n'), BuildAttachmentSummary(attachmentCount), StringComparison.Ordinal))
            prompt = hasDictation ? VoiceAnnotation : string.Empty;
        return new PersistedDisplay(prompt, attachments, attachmentCount);
    }

    private static int StripClipboardFenceHeader(string prompt)
    {
        var openEnd = prompt.IndexOf(">>>", StringComparison.Ordinal);
        if (openEnd < 0)
            return -1;
        var nonce = prompt["<<<SQUADCLIP:".Length..openEnd];
        var closeFence = $"\n<<<ENDCLIP:{nonce}>>>";
        var closeIndex = prompt.IndexOf(closeFence, openEnd, StringComparison.Ordinal);
        if (closeIndex < 0)
            return -1;
        var afterClose = closeIndex + closeFence.Length;
        return afterClose + 2 <= prompt.Length &&
               prompt[afterClose] == '\n' &&
               prompt[afterClose + 1] == '\n'
            ? afterClose + 2
            : -1;
    }

    private static FollowUpAttachment BuildHistoricalTypedAttachment(string block)
    {
        var (type, title) = AttachmentBlockFormatter.ExtractAttachmentMetadata(block);
        var notes = AttachmentBlockFormatter.ExtractAttachmentNotes(block);
        var content = AttachmentBlockFormatter.ExtractAttachmentContent(block);
        var description = !string.IsNullOrWhiteSpace(title)
            ? title!
            : type switch
            {
                "clipboard" => "Clipboard",
                "doc" => "Documentation",
                "inbox-excerpt" => "Inbox excerpt",
                "inbox-message" => "Inbox message",
                "note" => "Note",
                "task" => "Task",
                "transcript" => "Transcript",
                _ => "Attachment",
            };
        if (string.Equals(type, "transcript", StringComparison.Ordinal))
        {
            return new FollowUpAttachment(
                string.Empty,
                description,
                null,
                TranscriptQuote: content,
                Notes: notes,
                TranscriptSource: AttachmentBlockFormatter.ExtractTranscriptSource(block));
        }
        return new FollowUpAttachment(
            string.Empty,
            description,
            null,
            ContentBlock: block,
            Notes: notes);
    }
}
