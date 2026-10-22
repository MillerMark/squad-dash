namespace SquadDash;

internal static class FollowUpPromptBuilder
{
    internal static string Build(string text, IReadOnlyList<FollowUpAttachment> attachments)
    {
        if (attachments.Count == 0)
            return text;

        var headers = attachments.Select(att =>
        {
            var context = BuildContext(att);
            return att.SupportsNotes ? AttachmentBlockFormatter.WithNotes(context, att.Notes) : context;
        });
        bool hasTypedBlocks = attachments.Any(a => a.ContentBlock != null || a.ImagePath != null ||
            a.FileReferencePath != null || (a.SupportsNotes && !string.IsNullOrEmpty(a.Notes)));
        const string preamble = "The user has attached the following context items. Please refer to them as needed.";
        var headerSection = string.Join("\n", headers);
        return (hasTypedBlocks ? preamble + "\n" + headerSection : headerSection) + "\n\n" + text;
    }

    private static string BuildContext(FollowUpAttachment att)
    {
        if (att.ImagePath != null)
            return $"[Attached image: {att.ImagePath}]";
        if (att.ContentBlock != null)
            return att.ContentBlock;
        if (att.FileReferencePath != null)
            return AttachmentBlockFormatter.BuildTypedAttachmentBlock("file-reference", att.Description, att.FileReferencePath);
        if (att.TranscriptQuote != null)
            return $"Regarding this section of the transcript: \"{att.TranscriptQuote}\"";
        var summaryHint = att.OriginalPrompt is { Length: > 0 } op
            ? (op.Length > 120 ? op[..120] + "\u2026" : op)
            : att.Description;
        return $"[Follow-up on {att.CommitSha} \u2014 \"{att.Description}\": {summaryHint}]";
    }
}
