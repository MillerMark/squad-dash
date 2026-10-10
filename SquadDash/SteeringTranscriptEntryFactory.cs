using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;

namespace SquadDash;

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

        inlines.Add(new Run("\n") { FontWeight = FontWeights.Normal });
        if (hasAttachments)
        {
            var linkText = attachmentCount == 1
                ? "📎 1 attachment"
                : $"📎 {attachmentCount} attachments";
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

    internal static PersistedDisplay ParsePersistedPrompt(string providerPrompt)
    {
        ArgumentNullException.ThrowIfNull(providerPrompt);

        var bodyStart = ResolvePromptBodyStart(providerPrompt);
        if (bodyStart < 0)
            return new PersistedDisplay(providerPrompt, [], 0);

        var header = providerPrompt[..bodyStart];
        var prompt = providerPrompt[bodyStart..];
        var attachments = new List<FollowUpAttachment>();

        foreach (var block in AttachmentBlockFormatter.ExtractAttachmentBlocks(header))
            attachments.Add(BuildHistoricalTypedAttachment(block));

        foreach (var line in header.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            const string imagePrefix = "[Attached image: ";
            if (line.StartsWith(imagePrefix, StringComparison.Ordinal) && line.EndsWith(']'))
            {
                attachments.Add(new FollowUpAttachment(
                    string.Empty,
                    "Image",
                    null,
                    ImagePath: line[imagePrefix.Length..^1]));
            }
        }

        var recognizableLineCount = header.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Count(line =>
                line.StartsWith("[Attached image: ", StringComparison.Ordinal) ||
                line.StartsWith("[Follow-up on ", StringComparison.Ordinal) ||
                line.StartsWith("Regarding this section of the transcript:", StringComparison.Ordinal));
        var attachmentCount = attachments.Count;
        if (recognizableLineCount > attachmentCount)
            attachmentCount = recognizableLineCount;
        if (attachmentCount == 0 && !string.IsNullOrWhiteSpace(header))
            attachmentCount = 1;

        return new PersistedDisplay(prompt, attachments, attachmentCount);
    }

    private static int ResolvePromptBodyStart(string providerPrompt)
    {
        if (providerPrompt.StartsWith("<<<SQUADCLIP:", StringComparison.Ordinal))
            return StripClipboardFenceHeader(providerPrompt);

        return AttachmentBlockFormatter.StripTypedAttachmentHeaders(providerPrompt);
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
        return new FollowUpAttachment(
            string.Empty,
            description,
            null,
            ContentBlock: block);
    }
}
